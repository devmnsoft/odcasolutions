using System.Diagnostics;
using System.Globalization;
using Dapper;
using Microsoft.Extensions.Configuration;
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
                try { if (!await ScanOne(stoppingToken)) await ExtractOne(stoppingToken); await ReconcileOnce(stoppingToken); }
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
        // Item 4 (Bloco A): a indisponibilidade do scanner NÃO produz evidência de segurança.
        // Só a simulação explícita em Development resulta em 'safe', e ela é identificável
        // pelo engine 'simulated-scanner-dev' (nunca ativa fora de Development, mesmo com flag).
        var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "";
        var simulate = env == "Development" && configuration.GetValue("Documents:SimulateScanner", true);
        if (simulate)
        {
            logger.LogWarning("Verificação de segurança SIMULADA em Development (resultado de teste, não é auditoria real): versão {VersionId}.", item.Id);
            await ScanResult(connection, item.Id, "safe", "simulated-scanner-dev", ct);
            return true;
        }
        if (string.IsNullOrWhiteSpace(scanner))
        {
            await ScanResult(connection, item.Id, "scan_failed", "scanner-not-configured", ct);
            return true;
        }
        var path = StoragePath(item.StorageKey);
        try
        {
            var exit = await Run(scanner, ["--no-summary", path], TimeSpan.FromSeconds(60), ct);
            await ScanResult(connection, item.Id, exit == 0 ? "safe" : "rejected", Path.GetFileName(scanner), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Verificação indisponível em qualquer ambiente: nunca vira 'safe'.
            logger.LogWarning("Análise da versão {VersionId} indisponível sem expor conteúdo: {ErrorType}", item.Id, ex.GetType().Name);
            await ScanResult(connection, item.Id, "scan_failed", "scanner-unavailable", ct);
        }
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
            var availability = await connection.ExecuteScalarAsync<string>(new CommandDefinition("""
                SELECT CASE WHEN tenant.is_deleted OR tenant.status = 'suspended' THEN 'suspended'
                            ELSE odca.organization_feature_state(tenant.id, 'imports') END
                  FROM odca.tenants AS tenant WHERE tenant.id = @tenant
                """, new { tenant = job.TenantId }, tx, cancellationToken: ct));
            if (availability is "suspended" or "administratively_blocked" or "plan_restricted")
            {
                await tx.RollbackAsync(ct);
                await PauseJob(connection, job, "organization-unavailable", ct);
                return;
            }
            if (extracted.Method == "ocr")
            {
                var outcome = await connection.ExecuteScalarAsync<string>(new CommandDefinition(
                    "SELECT odca.consume_monthly_franchise(@tenant,'ocr_credit',@pages,@key,'extraction_job',@jobId,'document-worker')",
                    new { tenant = job.TenantId, pages = CountOcrPages(extracted.Text), key = "ocr-job:" + job.Id.ToString("N"), jobId = job.Id }, tx, cancellationToken: ct));
                if (outcome is not ("consumed" or "duplicate"))
                {
                    await tx.RollbackAsync(ct);
                    if (outcome is "exhausted" or "not_contracted") await FailJob(connection, job, "quota-exhausted", ct);
                    else await FailJob(connection, job, "franchise-meter-rejected", ct);
                    return;
                }
            }
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
            await connection.ExecuteAsync(new CommandDefinition("UPDATE odca.extraction_jobs SET status=CASE WHEN attempt_count>=max_attempts THEN 'failed' ELSE 'queued' END,available_at=now()+make_interval(secs=>least(300,attempt_count*15)),failure_code=@code,lease_token=NULL,lease_expires_at=NULL WHERE id=@id AND lease_token=@lease", new { id = job.Id, lease = job.LeaseToken, code = exception is NoPdfTextLayerException ? "pdf-no-text-layer" : exception is InvalidDataException ? "invalid-document" : "extractor-failed" }, cancellationToken: CancellationToken.None));
            await connection.ExecuteAsync(new CommandDefinition("UPDATE odca.contract_imports SET status=CASE WHEN (SELECT attempt_count>=max_attempts FROM odca.extraction_jobs WHERE id=@id) THEN 'failed' ELSE 'queued' END,current_step='document',attempt_count=(SELECT attempt_count FROM odca.extraction_jobs WHERE id=@id),safe_diagnostic_code=@code,updated_at=now() WHERE tenant_id=@tenant AND extraction_job_id=@id AND status NOT IN('confirmed','cancelled')", new { id = job.Id, tenant = job.TenantId, code = exception is NoPdfTextLayerException ? "pdf-no-text-layer" : exception is InvalidDataException ? "invalid-document" : "extractor-unavailable" }, cancellationToken: CancellationToken.None));
        }
    }

    private async Task<ExtractedText> Extract(VersionItem item, CancellationToken ct)
    {
        var path = StoragePath(item.StorageKey);
        if (item.DetectedType == "docx") { await using var input = File.OpenRead(path); return await DocumentContent.ExtractDocxAsync(input, ct); }
        var isPdf = item.DetectedType == "pdf";
        var executable = isPdf ? configuration["Documents:PdfTextExecutable"] : configuration["Documents:OcrExecutable"];
        if (string.IsNullOrWhiteSpace(executable)) throw new InvalidOperationException("extractor-not-configured");
        var language = (configuration["Documents:OcrLanguage"] ?? string.Empty).Trim();
        if (language.Length == 0) language = "por";
        var output = Path.GetTempFileName();
        try
        {
            string text;
            var method = isPdf ? "pdf-native" : "ocr";
            if (isPdf)
            {
                if (await Run(executable, ["-f", "1", "-l", "200", path, output], TimeSpan.FromMinutes(2), ct) != 0) throw new InvalidDataException("parser-returned-error");
                text = await File.ReadAllTextAsync(output, ct);
                if (!DocumentContent.PdfHasUsableTextLayer(text))
                {
                    // D-OC33 C: PDF digitalizado. Com renderer configurado, renderiza as paginas
                    // iniciais e roteia pelo OCR (metodo vira "ocr" e a franquia mensal consome as
                    // paginas efetivas); sem renderer, falha honesta com codigo estavel.
                    var rendered = await RenderScannedPagesToOcrText(path, language, ct);
                    if (rendered is null) throw NoPdfTextLayerException.Instance;
                    text = rendered;
                    method = "ocr";
                }
            }
            else
            {
                // Tesseract: exit 0 produz o .txt (pode ser vazio com legitimidade), exit 1 e
                // "sem texto detectavel" (pagina em branco -> zero sugestoes, nao falha); demais
                // codigos sao erro do parser.
                var exit = await Run(executable, [path, output[..^4], "-l", language], TimeSpan.FromMinutes(2), ct);
                text = exit switch
                {
                    0 => await File.ReadAllTextAsync(output[..^4] + ".txt", ct),
                    1 => string.Empty,
                    _ => throw new InvalidDataException("parser-returned-error"),
                };
            }
            if (text.Length > 5_000_000) throw new InvalidDataException("extracted-text-too-large");
            return new ExtractedText(text, method);
        }
        finally { File.Delete(output); File.Delete(output[..^4] + ".txt"); }
    }

    // D-OC33 C: convencao pdftoppm ("-png -r {dpi} -f 1 -l {max} {pdf} {prefix}" -> prefix-N.png).
    // Cada pagina renderizada passa pelo OCR com a mesma linguagem das imagens diretas e o texto
    // final preserva form feeds para atribuicao por pagina e contagem da franquia ocr_credit.
    private async Task<string?> RenderScannedPagesToOcrText(string pdfPath, string language, CancellationToken ct)
    {
        var renderer = configuration["Documents:PdfRenderExecutable"];
        var ocrExecutable = configuration["Documents:OcrExecutable"];
        if (string.IsNullOrWhiteSpace(renderer) || string.IsNullOrWhiteSpace(ocrExecutable)) return null;
        var maxPages = Math.Max(1, configuration.GetValue("Documents:OcrMaxPages", 20));
        var dpi = Math.Clamp(configuration.GetValue("Documents:OcrDpi", 300), 72, 600);
        var dir = Path.Combine(Path.GetTempPath(), "odca-scan-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            var prefix = Path.Combine(dir, "page");
            if (await Run(renderer, ["-png", "-r", dpi.ToString(CultureInfo.InvariantCulture), "-f", "1", "-l", maxPages.ToString(CultureInfo.InvariantCulture), pdfPath, prefix], TimeSpan.FromMinutes(2), ct) != 0)
                throw new InvalidDataException("parser-returned-error");
            var pages = Directory.EnumerateFiles(dir, "page-*.png")
                .Select(file => (Number: ScanPageNumber(file), File: file))
                .Where(page => page.Number > 0)
                .OrderBy(page => page.Number)
                .ToList();
            if (pages.Count == 0) return null;
            var rendered = new List<string>(pages.Count);
            foreach (var page in pages)
            {
                var tmp = Path.GetTempFileName();
                var exit = await Run(ocrExecutable, [page.File, tmp[..^4], "-l", language], TimeSpan.FromMinutes(2), ct);
                rendered.Add(exit switch
                {
                    0 => await File.ReadAllTextAsync(tmp[..^4] + ".txt", ct),
                    1 => string.Empty,
                    _ => throw new InvalidDataException("parser-returned-error"),
                });
                File.Delete(tmp);
                File.Delete(tmp[..^4] + ".txt");
            }
            return string.Join("\n\f\n", rendered);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(exception, "OCR nao removeu o diretorio temporario de renderizacao (sem expor caminho): {ErrorType}", exception.GetType().Name);
            }
        }
    }

    internal static int ScanPageNumber(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        const string marker = "page-";
        if (!name.StartsWith(marker, StringComparison.Ordinal)) return -1;
        var digits = name[marker.Length..];
        return digits.Length > 0 && digits.All(char.IsDigit) ? int.Parse(digits, CultureInfo.InvariantCulture) : -1;
    }

    // D-OC33 C: PDF sem camada de texto e sem renderer configurado — falha com codigo estavel
    // "pdf-no-text-layer" em vez de terminar "ready" com zero sugestoes por silencio.
    private sealed class NoPdfTextLayerException : Exception
    {
        private NoPdfTextLayerException() { }
        internal static readonly NoPdfTextLayerException Instance = new();
    }

    internal static int CountOcrPages(string text)
    {
        if (string.IsNullOrEmpty(text)) return 1;
        var separators = 0;
        foreach (var character in text)
        {
            if (character == '\f') separators++;
        }
        if (separators == 0) return 1;
        return text[^1] == '\f' ? separators : separators + 1;
    }

    private static async Task PauseJob(NpgsqlConnection connection, JobItem job, string code, CancellationToken ct)
    {
        await connection.ExecuteAsync(new CommandDefinition("UPDATE odca.extraction_jobs SET status='queued',attempt_count=GREATEST(attempt_count-1,0),available_at=now()+interval '1 minute',failure_code=@code,lease_token=NULL,lease_expires_at=NULL WHERE id=@id AND lease_token=@lease", new { id = job.Id, lease = job.LeaseToken, code }, cancellationToken: ct));
        await connection.ExecuteAsync(new CommandDefinition("UPDATE odca.contract_imports SET status='queued',current_step='document',safe_diagnostic_code=@code,updated_at=now() WHERE tenant_id=@tenant AND extraction_job_id=@id AND status NOT IN('confirmed','cancelled')", new { id = job.Id, tenant = job.TenantId, code }, cancellationToken: ct));
    }

    private static async Task FailJob(NpgsqlConnection connection, JobItem job, string code, CancellationToken ct)
    {
        await connection.ExecuteAsync(new CommandDefinition("UPDATE odca.extraction_jobs SET status='failed',completed_at=now(),failure_code=@code,lease_token=NULL,lease_expires_at=NULL WHERE id=@id AND lease_token=@lease", new { id = job.Id, lease = job.LeaseToken, code }, cancellationToken: ct));
        await connection.ExecuteAsync(new CommandDefinition("UPDATE odca.contract_imports SET status='failed',current_step='document',safe_diagnostic_code=@code,updated_at=now() WHERE tenant_id=@tenant AND extraction_job_id=@id AND status NOT IN('confirmed','cancelled')", new { id = job.Id, tenant = job.TenantId, code }, cancellationToken: ct));
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
    // D-OC32 B.2: reconciliacao de armazenamento. (1) Reservas 'reserved' com prazo vencido
    // viram 'expired' e a cota reservada correspondente e liberada (funcao SECURITY DEFINER,
    // lote limitado, FOR UPDATE SKIP LOCKED — segura para varios workers). (2) Arquivos sem
    // dono — tmps da quarentena e arquivos finais cujos storage_keys nao aparecem em nenhuma
    // tabela — mais velhos que a retencao configuravel sao MOVIDOS para reconciled/{yyyy-MM}/
    // (retencao auditavel; nunca exclusao direta). Logs sem conteudo.
    private DateTime lastReconcileSweep = DateTime.MinValue;

    private async Task ReconcileOnce(CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        var expired = await connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT odca.expire_storage_reservations(500)", cancellationToken: ct));
        if (expired > 0) logger.LogInformation("Reservas de armazenamento expiradas e cota reservada liberada: {Count}.", expired);

        var interval = TimeSpan.FromSeconds(configuration.GetValue("Documents:ReconcileIntervalSeconds", 30));
        if (DateTime.UtcNow - lastReconcileSweep < interval) return;
        lastReconcileSweep = DateTime.UtcNow;

        var root = Path.GetFullPath(configuration["Documents:StoragePath"] ?? "./private-documents");
        if (!Directory.Exists(root)) return;
        var cutoff = DateTime.UtcNow - TimeSpan.FromHours(configuration.GetValue("Documents:ReconcileOrphanRetentionHours", 24));

        var movedQuarantine = 0;
        var quarantine = Path.Combine(root, "quarantine");
        if (Directory.Exists(quarantine))
        {
            foreach (var file in Directory.EnumerateFiles(quarantine, "*.tmp"))
            {
                if (File.GetLastWriteTimeUtc(file) > cutoff) continue;
                MoveToReconciled(root, file, "quarantine/" + Path.GetFileName(file));
                movedQuarantine++;
            }
        }

        var referenced = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in await connection.QueryAsync<string>(new CommandDefinition("SELECT storage_key FROM odca.document_versions", cancellationToken: ct))) referenced.Add(key);
        foreach (var key in await connection.QueryAsync<string>(new CommandDefinition("SELECT storage_key FROM odca.generated_contract_versions", cancellationToken: ct))) referenced.Add(key);
        foreach (var key in await connection.QueryAsync<string>(new CommandDefinition("SELECT pdf_storage_key FROM odca.generated_contract_versions WHERE pdf_storage_key IS NOT NULL", cancellationToken: ct))) referenced.Add(key);

        var movedFinal = 0;
        foreach (var tenantDir in Directory.EnumerateDirectories(root))
        {
            var tenantName = Path.GetFileName(tenantDir);
            if (tenantName is "quarantine" or "reconciled") continue;
            if (!Guid.TryParse(tenantName, out _)) continue;
            foreach (var file in Directory.EnumerateFiles(tenantDir, "*", SearchOption.AllDirectories))
            {
                var relative = file.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar).Replace(Path.DirectorySeparatorChar, '/');
                if (referenced.Contains(relative) || File.GetLastWriteTimeUtc(file) > cutoff) continue;
                MoveToReconciled(root, file, relative);
                movedFinal++;
            }
        }

        if (movedQuarantine > 0 || movedFinal > 0)
            logger.LogInformation("Reconciliacao documental: {Quarantine} tmp(s) de quarentena e {Final} arquivo(s) final(is) movidos para retencao.", movedQuarantine, movedFinal);
    }

    private void MoveToReconciled(string root, string file, string relativeKey)
    {
        try
        {
            var targetDir = Path.Combine(root, "reconciled", DateTime.UtcNow.ToString("yyyy-MM", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(targetDir);
            var name = relativeKey.Replace('/', '-').Replace('\\', '-').Replace(' ', '_');
            var target = Path.Combine(targetDir, name);
            for (var i = 1; File.Exists(target); i++) target = Path.Combine(targetDir, $"{i}-{name}");
            File.Move(file, target);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Reconciliacao documental nao moveu um arquivo para retencao (sem expor caminho): {ErrorType}", exception.GetType().Name);
        }
    }

    private sealed record VersionItem(Guid Id, Guid TenantId, string StorageKey, string DetectedType);
    private sealed record JobItem(Guid Id, Guid TenantId, Guid ContractId, Guid VersionId, Guid LeaseToken);
}
