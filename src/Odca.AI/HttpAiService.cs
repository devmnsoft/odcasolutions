using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Odca.AI;

/// <summary>
/// D-OC52: provedor http para endpoints compatíveis com a API de chat em /chat/completions
/// (OpenAI e gateways equivalentes). A chamada exige JSON de saída estruturado; a resposta é
/// validada por <see cref="AiResponseParser"/>. Qualquer falha — rede, timeout, status, JSON —
/// degrada para "indisponível" com reason estável: análise IA nunca quebra a jornada documental.
/// </summary>
public sealed class HttpAiService(HttpClient http, IOptions<AiOptions> options) : IAiService
{
    public async Task<AiAnalysis> AnalyzeContractAsync(AiContractInput input, CancellationToken ct)
    {
        var configured = options.Value;
        var providerName = string.IsNullOrWhiteSpace(configured.Model) ? "http" : configured.Model!.Trim();
        try
        {
            if (string.IsNullOrWhiteSpace(input.Text)) return AiAnalysis.Unavailable(providerName);

            var maximumChars = Math.Max(1_000, configured.MaxInputChars);
            var text = input.Text.Trim();
            if (text.Length > maximumChars) text = text[..maximumChars];

            using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions");
            if (!string.IsNullOrWhiteSpace(configured.ApiKey))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", configured.ApiKey);
            }
            var payload = new Dictionary<string, object>
            {
                ["temperature"] = 0,
                ["max_tokens"] = 700,
                ["response_format"] = new { type = "json_object" },
                ["messages"] = new object[]
                {
                    new { role = "system", content = SystemPrompt(input.Culture) },
                    new { role = "user", content = $"Texto extraído de um documento contratual:\n<<<\n{text}\n>>>" }
                }
            };
            if (!string.IsNullOrWhiteSpace(configured.Model)) payload["model"] = configured.Model.Trim();
            request.Content = JsonContent.Create(payload);

            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return AiAnalysis.Unavailable(providerName);

            var body = await response.Content.ReadAsStringAsync(ct);
            var content = ExtractMessageContent(body);
            return AiResponseParser.Parse(content, providerName) ?? AiAnalysis.Unavailable(providerName);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException or JsonException or UriFormatException)
        {
            return AiAnalysis.Unavailable(providerName);
        }
    }

    internal static string SystemPrompt(string culture)
    {
        var language = culture switch
        {
            "en-US" => "inglês",
            "es-ES" => "espanhol",
            "fr-FR" => "francês",
            _ => "português do Brasil"
        };
        return "Você analisa textos extraídos de documentos contratuais para apoiar a revisão humana. " +
               "Responda APENAS com um objeto JSON válido, sem comentários, no formato: " +
               "{\"summary\":\"resumo executivo em 3 frases\",\"overall_risk\":\"low|medium|high\"," +
               "\"categories\":[\"categorias curtas em até 6 itens\"],\"clauses\":[{\"code\":\"identificador\",\"risk\":\"low|medium|high\",\"evidence\":\"trecho curto do texto\"}]}. " +
               "Identificadores permitidos para clauses.code: " + string.Join(", ", AiResponseParser.ClauseCodes) +
               " (ou outro identificador curto em minúsculas com underscore quando nenhum se aplicar). " +
               "evidence deve ser um trecho literal do documento, com até 300 caracteres. " +
               "summary e categories em " + language + ". Não invente conteúdo ausente do texto.";
    }

    internal static string? ExtractMessageContent(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            // corpos malformados do provedor (root array, choices nao-array) sao tolerados:
            // TryGetProperty sobre um elemento nao-object lancaria InvalidOperationException.
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("choices", out var choices)) return null;
            if (choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0) return null;
            if (choices[0].ValueKind != JsonValueKind.Object || !choices[0].TryGetProperty("message", out var message)) return null;
            if (message.ValueKind != JsonValueKind.Object || !message.TryGetProperty("content", out var content)) return null;
            return content.GetString();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
