using System.Diagnostics;
using System.Security.Claims;
using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Odca.Application.Contracts;
using Odca.Contracts.Administration;

namespace Odca.Api.Controllers;

/// <summary>
/// D-OC34 A.3: diagnóstico operacional das dependências externas usadas pelo processamento
/// documental e pelas notificações, além das filas da plataforma (odca.platform_queue_summary,
/// v057). Cada dependência reporta estado em corrente honesta: unavailable (não configurado) →
/// configured (valor presente mas binário não localizado) → accessible (binário responde) →
/// verified (sonda funcional com um documento mínimo gerado em memória passou). Valores
/// configurados nunca são exibidos; apenas sua presença e o resultado da sonda.
/// </summary>
[ApiController]
[Authorize(Policy = "PlatformAdministrator")]
public sealed class PlatformDiagnosticsController(NpgsqlDataSource dataSource, IConfiguration configuration) : ControllerBase
{
    private const string ProbeContent = """{"type":"document","content":[{"type":"heading","level":1,"content":[{"type":"text","text":"Cláusula médica"}]},{"type":"table","content":[{"type":"tableRow","content":[{"type":"tableCell","content":[{"type":"text","text":"Descrição"}]},{"type":"tableCell","content":[{"type":"text","text":"Atenção"}]}]}]},{"type":"pageBreak"},{"type":"paragraph","content":[{"type":"text","text":"Fim"}]}]}""";
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(8);
    private static readonly string[] ScannerVersionArguments = ["-V"];

    [HttpGet("api/v1/platform/diagnostics")]
    public async Task<IActionResult> Diagnostics(CancellationToken ct)
    {
        var actor = Actor(); if (actor is null) return Unauthorized();

        var dependencies = new List<PlatformDiagnosticsDependency>();
        var probeDir = Path.Combine(Path.GetTempPath(), "odca-diag-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(probeDir);
            var pdfPath = Path.Combine(probeDir, "probe.pdf");
            var pngPrefix = Path.Combine(probeDir, "page");
            var pngPath = pngPrefix + "-1.png";

            // 1) Renderizador PDF interno (sem dependência externa): sempre sondado; o PDF de
            // sonda resultante alimenta as sondas de pdftotext/pdftoppm/tesseract/pdfinfo.
            var (rendererStatus, rendererDetail, probePdf) = ProbeInternalRenderer();
            dependencies.Add(new PlatformDiagnosticsDependency("pdf.renderer", "interno (Odca.Application)", rendererStatus, rendererDetail));
            var pdfAvailable = false;
            var pngAvailable = false;
            if (probePdf is not null)
            {
                await System.IO.File.WriteAllBytesAsync(pdfPath, probePdf, ct);
                pdfAvailable = true;
            }

            // 2) Extração de texto nativo (pdftotext).
            var pdfTextExecutable = configuration["Documents:PdfTextExecutable"];
            if (string.IsNullOrWhiteSpace(pdfTextExecutable))
                dependencies.Add(new PlatformDiagnosticsDependency("documents.pdf-text", "Documents:PdfTextExecutable", "unavailable", "chave ausente ou sem valor."));
            else if (!pdfAvailable)
                dependencies.Add(new PlatformDiagnosticsDependency("documents.pdf-text", "Documents:PdfTextExecutable", "not_applicable", "o renderizador interno de PDF falhou na sonda (ver acima)."));
            else
            {
                try
                {
                    var (exit, output) = await RunToolAsync(pdfTextExecutable, new[] { "-enc", "UTF-8", "-", pdfPath });
                    if (exit == 0 && output.Trim().Length > 0)
                        dependencies.Add(new PlatformDiagnosticsDependency("documents.pdf-text", "Documents:PdfTextExecutable", "verified", $"texto extraído do documento mínimo ({output.Trim().Length} caracteres)."));
                    else if (exit == 0)
                        dependencies.Add(new PlatformDiagnosticsDependency("documents.pdf-text", "Documents:PdfTextExecutable", "accessible", "executável respondeu (exit 0) sem extrair texto."));
                    else
                        dependencies.Add(new PlatformDiagnosticsDependency("documents.pdf-text", "Documents:PdfTextExecutable", "accessible", $"executável respondeu com exit {exit}: {Short(output)}"));
                }
                catch (Exception ex)
                {
                    dependencies.Add(new PlatformDiagnosticsDependency("documents.pdf-text", "Documents:PdfTextExecutable", "configured", "binário não localizado ou não inicializável: " + Short(ex.Message)));
                }
            }

            // 3) Renderização de páginas (pdftoppm): produz o PNG que alimenta a sonda OCR.
            var renderExecutable = configuration["Documents:PdfRenderExecutable"];
            if (string.IsNullOrWhiteSpace(renderExecutable))
                dependencies.Add(new PlatformDiagnosticsDependency("documents.pdf-render", "Documents:PdfRenderExecutable", "unavailable", "chave ausente ou sem valor."));
            else if (!pdfAvailable)
                dependencies.Add(new PlatformDiagnosticsDependency("documents.pdf-render", "Documents:PdfRenderExecutable", "not_applicable", "o renderizador interno de PDF falhou na sonda (ver acima)."));
            else
            {
                try
                {
                    var (exit, output) = await RunToolAsync(renderExecutable, new[] { "-r", "96", "-f", "1", "-l", "1", "-png", pdfPath, pngPrefix });
                    if (System.IO.File.Exists(pngPath) && new FileInfo(pngPath).Length > 0)
                    {
                        pngAvailable = true;
                        dependencies.Add(new PlatformDiagnosticsDependency("documents.pdf-render", "Documents:PdfRenderExecutable", "verified", "página 1 renderizada em PNG."));
                    }
                    else if (exit == 0)
                        dependencies.Add(new PlatformDiagnosticsDependency("documents.pdf-render", "Documents:PdfRenderExecutable", "accessible", "executável respondeu (exit 0) sem produzir o PNG esperado."));
                    else
                        dependencies.Add(new PlatformDiagnosticsDependency("documents.pdf-render", "Documents:PdfRenderExecutable", "accessible", $"executável respondeu com exit {exit}: {Short(output)}"));
                }
                catch (Exception ex)
                {
                    dependencies.Add(new PlatformDiagnosticsDependency("documents.pdf-render", "Documents:PdfRenderExecutable", "configured", "binário não localizado ou não inicializável: " + Short(ex.Message)));
                }
            }

            // 4) OCR de páginas escaneadas (tesseract): depende do PNG da etapa anterior.
            var ocrExecutable = configuration["Documents:OcrExecutable"];
            if (string.IsNullOrWhiteSpace(ocrExecutable))
                dependencies.Add(new PlatformDiagnosticsDependency("documents.ocr", "Documents:OcrExecutable", "unavailable", "chave ausente ou sem valor."));
            else if (!pngAvailable)
                dependencies.Add(new PlatformDiagnosticsDependency("documents.ocr", "Documents:OcrExecutable", "not_applicable", "a renderização de páginas não produziu o PNG de sonda (ver acima)."));
            else
            {
                try
                {
                    var (exit, output) = await RunToolAsync(ocrExecutable, new[] { pngPath, Path.Combine(probeDir, "ocr-out") });
                    if (exit is 0 or 1)
                        dependencies.Add(new PlatformDiagnosticsDependency("documents.ocr", "Documents:OcrExecutable", "verified", $"OCR respondeu (exit {exit})."));
                    else
                        dependencies.Add(new PlatformDiagnosticsDependency("documents.ocr", "Documents:OcrExecutable", "accessible", $"executável respondeu com exit {exit}: {Short(output)}"));
                }
                catch (Exception ex)
                {
                    dependencies.Add(new PlatformDiagnosticsDependency("documents.ocr", "Documents:OcrExecutable", "configured", "binário não localizado ou não inicializável: " + Short(ex.Message)));
                }
            }

            // 5) Metadados de PDF (pdfinfo).
            var pdfInfoExecutable = configuration["Documents:PdfInfoExecutable"];
            if (string.IsNullOrWhiteSpace(pdfInfoExecutable))
                dependencies.Add(new PlatformDiagnosticsDependency("documents.pdf-info", "Documents:PdfInfoExecutable", "unavailable", "chave ausente ou sem valor."));
            else if (!pdfAvailable)
                dependencies.Add(new PlatformDiagnosticsDependency("documents.pdf-info", "Documents:PdfInfoExecutable", "not_applicable", "o renderizador interno de PDF falhou na sonda (ver acima)."));
            else
            {
                try
                {
                    var (exit, output) = await RunToolAsync(pdfInfoExecutable, new[] { pdfPath });
                    if (exit == 0 && output.Contains("Pages:", StringComparison.Ordinal))
                        dependencies.Add(new PlatformDiagnosticsDependency("documents.pdf-info", "Documents:PdfInfoExecutable", "verified", "metadados lidos do documento mínimo."));
                    else if (exit == 0)
                        dependencies.Add(new PlatformDiagnosticsDependency("documents.pdf-info", "Documents:PdfInfoExecutable", "accessible", "executável respondeu (exit 0) sem o campo Pages esperado."));
                    else
                        dependencies.Add(new PlatformDiagnosticsDependency("documents.pdf-info", "Documents:PdfInfoExecutable", "accessible", $"executável respondeu com exit {exit}: {Short(output)}"));
                }
                catch (Exception ex)
                {
                    dependencies.Add(new PlatformDiagnosticsDependency("documents.pdf-info", "Documents:PdfInfoExecutable", "configured", "binário não localizado ou não inicializável: " + Short(ex.Message)));
                }
            }

            // 6) Scanner de malware (clamscan): varredura best-effort no upload; a sonda limita-se
            // a comprovar que o binário responde à versão.
            var scannerExecutable = configuration["Documents:MalwareScannerExecutable"];
            if (string.IsNullOrWhiteSpace(scannerExecutable))
                dependencies.Add(new PlatformDiagnosticsDependency("security.scanner", "Documents:MalwareScannerExecutable", "unavailable", "chave ausente ou sem valor."));
            else
            {
                try
                {
                    var (exit, output) = await RunToolAsync(scannerExecutable, ScannerVersionArguments);
                    dependencies.Add(new PlatformDiagnosticsDependency("security.scanner", "Documents:MalwareScannerExecutable", "accessible",
                        exit == 0 ? "binário responde à versão; varredura efetiva ocorre best-effort no upload." : $"binário respondeu com exit {exit}: {Short(output)}"));
                }
                catch (Exception ex)
                {
                    dependencies.Add(new PlatformDiagnosticsDependency("security.scanner", "Documents:MalwareScannerExecutable", "configured", "binário não localizado ou não inicializável: " + Short(ex.Message)));
                }
            }

            // 7) Transporte de notificações (espelha a seleção do Worker: FilePickup dev ou SMTP).
            var pickupDir = configuration["Notifications:DevelopmentPickupDirectory"];
            var smtpHost = configuration["Notifications:Smtp:Host"];
            var smtpSender = configuration["Notifications:Smtp:SenderAddress"];
            if (!string.IsNullOrWhiteSpace(pickupDir))
            {
                var exists = Directory.Exists(pickupDir);
                dependencies.Add(new PlatformDiagnosticsDependency("notifications.transport", "Notifications:DevelopmentPickupDirectory", exists ? "accessible" : "configured",
                    exists ? "transporte local de coleta (FilePickup) configurado e diretório presente." : "diretório de coleta ainda não existe neste host."));
            }
            else if (!string.IsNullOrWhiteSpace(smtpHost) && !string.IsNullOrWhiteSpace(smtpSender))
            {
                dependencies.Add(new PlatformDiagnosticsDependency("notifications.transport", "Notifications:Smtp", "accessible", "SMTP configurado (host e remetente definidos); conexão não testada automaticamente."));
            }
            else
            {
                dependencies.Add(new PlatformDiagnosticsDependency("notifications.transport", "Notifications:", "unavailable", "nenhum provedor de transporte configurado (FilePickup ou SMTP)."));
            }

            // 8) Envelope de assinatura externa: dependência conhecida e pendente de decisão de
            // fornecedor — exibida como indisponível com a decisão registrada (DECISIONS-LOG).
            dependencies.Add(new PlatformDiagnosticsDependency("signatures.external-envelope", "—", "unavailable", "fornecedor externo não escolhido para a estreia; decisão registrada em DECISIONS-LOG."));
        }
        finally
        {
            try { Directory.Delete(probeDir, recursive: true); }
            catch (IOException) { /* sonda: diretório temporário */ }
            catch (UnauthorizedAccessException) { /* sonda: arquivo retido pelo sistema */ }
        }

        await using var c = await dataSource.OpenConnectionAsync(ct);
        var rows = (await c.QueryAsync<QueueSummaryRow>(new CommandDefinition("""
            SELECT queue AS Queue, pending_count AS PendingCount, stalled_count AS StalledCount,
                   failed_count AS FailedCount, oldest_pending_at AS OldestPendingAt
              FROM odca.platform_queue_summary()
             ORDER BY queue
            """, cancellationToken: ct))).AsList();
        var queues = rows
            .Select(r => new PlatformDiagnosticsQueueSummary(r.Queue, r.PendingCount, r.StalledCount, r.FailedCount, r.OldestPendingAt))
            .ToList();
        return Ok(new PlatformDiagnosticsPayload(DateTime.UtcNow, dependencies, queues));
    }

    /// <summary>Sonda o renderizador interno (sempre disponível quando o serviço compila).</summary>
    private static (string Status, string Detail, byte[]? Pdf) ProbeInternalRenderer()
    {
        byte[] pdf;
        try
        {
            pdf = ContractDocumentRenderer.ToPdf(ProbeContent, "[]", "[]", "Sonda ODCA", 1);
        }
        catch (InvalidDataException ex)
        {
            return ("configured", "renderizador presente, mas a estrutura mínima não renderizou: " + Short(ex.Message), null);
        }
        catch (Exception ex)
        {
            return ("configured", "falha inesperada ao renderizar o PDF de sonda: " + Short(ex.Message), null);
        }
        return ("verified", $"PDF mínimo renderizado em memória ({pdf.Length} bytes).", pdf);
    }

    /// <summary>Inicia o executável capturando saída padrão+erro, com limite de tempo da sonda.</summary>
    private static async Task<(int ExitCode, string Output)> RunToolAsync(string executable, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("process-start-failed");
        using var limit = new CancellationTokenSource(ProbeTimeout);
        var stdoutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
        try
        {
            await process.WaitForExitAsync(limit.Token);
            return (process.ExitCode, (await stdoutTask) + (await stderrTask));
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
            return (-999, "timeout: o executável não respondeu no prazo da sonda.");
        }
    }

    private static string Short(string message)
    {
        message = message.Trim().Replace(Environment.NewLine, " ");
        return message.Length <= 160 ? message : message[..160] + "…";
    }

    private Guid? Actor() => Guid.TryParse(User.FindFirstValue("sub"), out var id) ? id : null;

    private sealed record QueueSummaryRow(string Queue, int PendingCount, int StalledCount, int FailedCount, DateTime? OldestPendingAt);
}
