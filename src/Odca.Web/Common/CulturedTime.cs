using System.Globalization;

namespace Odca.Web.Common;

/// <summary>
/// D-OC22: formata os timestamps das telas do Estúdio — metadados de UI, não conteúdo de
/// documento — na ordem de data da língua ativa, com relógio de 24h. O conteúdo do documento
/// (papel/PDF) continua no formato canônico fixo dd/MM/yyyy, preservando preview == saída
/// independentemente da língua da interface (D-OC5). Cultura nula ou desconhecida cai em pt-BR.
/// </summary>
public static class CulturedTime
{
    public static string Date(DateTimeOffset value, string? culture) => Format(value, culture, null);

    public static string Date(DateTime value, string? culture) => Format(ToOffset(value), culture, null);

    /// <summary>Data pura (ex.: nascimento) na ordem da data da língua ativa.</summary>
    public static string Date(DateOnly value, string? culture) => Format(new DateTimeOffset(value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero), culture, null);

    public static string DateTime(DateTimeOffset value, string? culture) => Format(value, culture, " HH:mm");

    public static string DateTime(DateTime value, string? culture) => Format(ToOffset(value), culture, " HH:mm");

    public static string DateTimeSeconds(DateTime value, string? culture) => Format(ToOffset(value), culture, " HH:mm:ss");

    private static string Format(DateTimeOffset value, string? culture, string? timePart)
    {
        var info = Get(culture);
        return value.ToString(info.DateTimeFormat.ShortDatePattern + (timePart ?? string.Empty), info);
    }

    // Valores DateTime chegam da API sem offset explícito mas representam UTC
    // (timestamptz lido pelo provider); DateTimeKind.Local preserva o offset local.
    private static DateTimeOffset ToOffset(DateTime value) =>
        value.Kind == DateTimeKind.Local ? new DateTimeOffset(value) : new DateTimeOffset(value, TimeSpan.Zero);

    private static CultureInfo Get(string? culture) =>
        CultureInfo.GetCultures(CultureTypes.SpecificCultures)
            .FirstOrDefault(item => string.Equals(item.Name, culture, StringComparison.OrdinalIgnoreCase))
            ?? CultureInfo.GetCultureInfo("pt-BR");
}
