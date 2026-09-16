using System.Diagnostics;
using Dapper;
using Npgsql;
using Odca.Application.Documents;

namespace Odca.Worker;

public sealed class DocumentWorker(NpgsqlDataSource dataSource, IConfiguration configuration, ILogger<DocumentWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        try
        {
            do
            {
                try { if (!await ScanOne(stoppingToken)) await ExtractOne(stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                catch (Exception exception) { logger.LogError(exception, "Falha isolada no processamento documental; o ciclo continuará."); }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { logger.LogInformation("Processador documental encerrado pelo host."); }
    }

    private async Task<bool> ScanOne(CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        var item = await connection.QuerySingleOrDefaultAsync<VersionItem>(new CommandDefinition(
            "SELECT id AS Id,tenant_id AS TenantId,storage_key AS StorageKey,detected_type AS DetectedType FROM odca.claim_document_scan()", cancellationToken: ct));
        if (item is null) return false;
        await connection.ExecuteAsync(new CommandDefinition("SELECT set_config('odca.tenant_id',@tenant,false)", new { tenant = item.TenantId.ToString() }, cancellationToken: ct));
        var scanner = configuration["Documents:MalwareScannerExecutable"];
        if (string.IsNullOrWhiteSpace(scanner)) { await ScanResult(connection, item.Id, "scan_failed", "scanner-not-configured", ct); return true; }
        var path = StoragePath(item.StorageKey);
        try
        {
            var exit = await Run(scanner, ["--no-summary", path], TimeSpan.FromSeconds(60), ct);
            await ScanResult(connection, item.Id, exit == 0 ? "safe" : "rejected", Path.GetFileName(scanner), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogWarning("Análise da versão {VersionId} falhou sem expor conteúdo: {ErrorType}", item.Id, ex.GetType().Name); await ScanResult(connection, item.Id, "scan_failed", "scanner-failed", ct); }
        return true;
    }

    private async Task ExtractOne(CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        var lease = Guid.NewGuid();
        var job = await connection.QuerySingleOrDefaultAsync<JobItem>(new CommandDefinition(
            "SELECT id AS Id,tenant_id AS TenantId,contract_id AS ContractId,version_id AS VersionId,lease_token AS LeaseToken FROM odca.claim_extraction_job(@lease)",
            new { lease }, cancellationToken: ct));
        if (job is null) return;
        await connection.ExecuteAsync(new CommandDefinition("SELECT set_config('odca.tenant_id',@tenant,false)", new { tenant = job.TenantId.ToString() }, cancellationToken: ct));
        var version = await connection.QuerySingleAsync<VersionItem>(new CommandDefinition("SELECT id AS Id,tenant_id AS TenantId,storage_key AS StorageKey,detected_type AS DetectedType FROM odca.document_versions WHERE id=@id", new { id = job.VersionId }, cancellationToken: ct));
        try
        {
            var extracted = await Extract(version, ct);
            var suggestions = DocumentContent.Suggest(extracted.Text, extracted.Method);
            await using var tx = await connection.BeginTransactionAsync(ct);
            await connection.ExecuteAsync(new CommandDefinition("SELECT set_config('odca.tenant_id',@tenant,true)", new { tenant = job.TenantId.ToString() }, tx, cancellationToken: ct));
            var resultId = Guid.NewGuid();
            var inserted = await connection.ExecuteAsync(new CommandDefinition("""
                INSERT INTO odca.extraction_results(id,tenant_id,job_id,version_id,method,raw_text)
                SELECT @resultId,@tenantId,@jobId,@versionId,@method,@text WHERE EXISTS(SELECT 1 FROM odca.extraction_jobs WHERE id=@jobId AND lease_token=@lease AND status='processing');
                """, new { resultId, tenantId = job.TenantId, jobId = job.Id, versionId = job.VersionId, method = extracted.Method, text = extracted.Text, lease = job.LeaseToken }, tx, cancellationToken: ct));
            if (inserted == 0) { await tx.RollbackAsync(ct); return; }
            foreach (var suggestion in suggestions)
                await connection.ExecuteAsync(new CommandDefinition("INSERT INTO odca.extraction_suggestions(tenant_id,result_id,version_id,field_name,extracted_value,normalized_value,evidence,page_number,method) VALUES(@tenantId,@resultId,@versionId,@Field,@ExtractedValue,@NormalizedValue,@Evidence,@Page,@Method)", new { tenantId = job.TenantId, resultId, versionId = job.VersionId, suggestion.Field, suggestion.ExtractedValue, suggestion.NormalizedValue, suggestion.Evidence, suggestion.Page, suggestion.Method }, tx, cancellationToken: ct));
            await connection.ExecuteAsync(new CommandDefinition("UPDATE odca.extraction_jobs SET status='ready_for_review',completed_at=now(),lease_token=NULL,lease_expires_at=NULL WHERE id=@id AND lease_token=@lease", new { id = job.Id, lease = job.LeaseToken }, tx, cancellationToken: ct));
            await connection.ExecuteAsync(new CommandDefinition("UPDATE odca.contract_imports SET status='awaiting_review',current_step='contract_data',attempt_count=@attempt,updated_at=now(),safe_diagnostic_code=NULL WHERE tenant_id=@tenant AND extraction_job_id=@id AND status NOT IN('confirmed','cancelled')", new { tenant = job.TenantId, id = job.Id, attempt = 1 }, tx, cancellationToken: ct));
            await tx.CommitAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger.LogWarning("Extração {JobId} falhou sem registrar conteúdo: {ErrorType}", job.Id, exception.GetType().Name);
            await connection.ExecuteAsync(new CommandDefinition("UPDATE odca.extraction_jobs SET status=CASE WHEN attempt_count>=max_attempts THEN 'failed' ELSE 'queued' END,available_at=now()+make_interval(secs=>least(300,attempt_count*15)),failure_code=@code,lease_token=NULL,lease_expires_at=NULL WHERE id=@id AND lease_token=@lease", new { id = job.Id, lease = job.LeaseToken, code = exception is InvalidDataException ? "invalid-document" : "extractor-failed" }, cancellationToken: CancellationToken.None));
            await connection.ExecuteAsync(new CommandDefinition("UPDATE odca.contract_imports SET status=CASE WHEN (SELECT attempt_count>=max_attempts FROM odca.extraction_jobs WHERE id=@id) THEN 'failed' ELSE 'queued' END,current_step='document',attempt_count=(SELECT attempt_count FROM odca.extraction_jobs WHERE id=@id),safe_diagnostic_code=@code,updated_at=now() WHERE tenant_id=@tenant AND extraction_job_id=@id AND status NOT IN('confirmed','cancelled')", new { id = job.Id, tenant = job.TenantId, code = exception is InvalidDataException ? "invalid-document" : "extractor-unavailable" }, cancellationToken: CancellationToken.None));
        }
    }

    private async Task<ExtractedText> Extract(VersionItem item, CancellationToken ct)
    {
        var path = StoragePath(item.StorageKey);
        if (item.DetectedType == "docx") { await using var input = File.OpenRead(path); return await DocumentContent.ExtractDocxAsync(input, ct); }
        var executable = item.DetectedType == "pdf" ? configuration["Documents:PdfTextExecutable"] : configuration["Documents:OcrExecutable"];
        if (string.IsNullOrWhiteSpace(executable)) throw new InvalidOperationException("extractor-not-configured");
        var output = Path.GetTempFileName();
        try
        {
            IReadOnlyList<string> args = item.DetectedType == "pdf" ? ["-f", "1", "-l", "200", path, output] : [path, output[..^4], "-l", "por"];
            if (await Run(executable, args, TimeSpan.FromMinutes(2), ct) != 0) throw new InvalidDataException("parser-returned-error");
            var textPath = item.DetectedType == "pdf" ? output : output[..^4] + ".txt";
            var text = await File.ReadAllTextAsync(textPath, ct);
            if (text.Length > 5_000_000) throw new InvalidDataException("extracted-text-too-large");
            return new ExtractedText(text, item.DetectedType == "pdf" ? "pdf-native" : "ocr");
        }
        finally { File.Delete(output); File.Delete(output[..^4] + ".txt"); }
    }

    private string StoragePath(string key) => Path.Combine(Path.GetFullPath(configuration["Documents:StoragePath"] ?? "./private-documents"), key.Replace('/', Path.DirectorySeparatorChar));
    private static async Task ScanResult(NpgsqlConnection c, Guid id, string status, string engine, CancellationToken ct)
        => _ = await c.ExecuteAsync(new CommandDefinition("UPDATE odca.document_versions SET security_status=@status,security_checked_at=now(),security_engine=@engine WHERE id=@id", new { id, status, engine }, cancellationToken: ct));
    internal static async Task<int> Run(string executable, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken ct)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("process-start-failed");
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct); limit.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(limit.Token);
            return process.ExitCode;
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
            throw;
        }
    }
    private sealed record VersionItem(Guid Id, Guid TenantId, string StorageKey, string DetectedType);
    private sealed record JobItem(Guid Id, Guid TenantId, Guid ContractId, Guid VersionId, Guid LeaseToken);
}
