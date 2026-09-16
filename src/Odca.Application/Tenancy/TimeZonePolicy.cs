namespace Odca.Application.Tenancy;

public enum TimeZoneConfigurationError
{
    Missing,
    Empty,
    Invalid,
    Unavailable
}

public sealed class TimeZoneConfigurationException(
    TimeZoneConfigurationError error,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public TimeZoneConfigurationError Error { get; } = error;
}

public static class TimeZonePolicy
{
    public static TimeZoneInfo Resolve(string? organizationTimeZoneId, string? applicationDefaultTimeZoneId)
    {
        if (organizationTimeZoneId is not null)
        {
            if (string.IsNullOrWhiteSpace(organizationTimeZoneId))
                throw new TimeZoneConfigurationException(TimeZoneConfigurationError.Empty, "O fuso horário da organização está vazio.");

            return Find(organizationTimeZoneId.Trim());
        }

        if (applicationDefaultTimeZoneId is null)
            throw new TimeZoneConfigurationException(TimeZoneConfigurationError.Missing, "Não há fuso horário da organização nem padrão da aplicação.");
        if (string.IsNullOrWhiteSpace(applicationDefaultTimeZoneId))
            throw new TimeZoneConfigurationException(TimeZoneConfigurationError.Empty, "O fuso horário padrão da aplicação está vazio.");

        return Find(applicationDefaultTimeZoneId.Trim());
    }

    private static TimeZoneInfo Find(string id)
    {
        if (id.IndexOfAny(['\0', '\r', '\n']) >= 0)
            throw new TimeZoneConfigurationException(TimeZoneConfigurationError.Invalid, "O identificador de fuso horário possui caracteres inválidos.");

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (TimeZoneNotFoundException exception)
        {
            throw new TimeZoneConfigurationException(TimeZoneConfigurationError.Unavailable, $"O fuso horário '{id}' não está disponível neste ambiente.", exception);
        }
        catch (InvalidTimeZoneException exception)
        {
            throw new TimeZoneConfigurationException(TimeZoneConfigurationError.Invalid, $"Os dados do fuso horário '{id}' são inválidos.", exception);
        }
    }
}
