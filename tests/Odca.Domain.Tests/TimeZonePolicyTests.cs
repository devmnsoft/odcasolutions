using Odca.Application.Tenancy;

namespace Odca.Domain.Tests;

public sealed class TimeZonePolicyTests
{
    [Fact]
    public void OrganizationTimeZoneTakesPrecedence()
    {
        var result = TimeZonePolicy.Resolve("UTC", "America/Sao_Paulo");

        Assert.Equal(TimeZoneInfo.Utc.Id, result.Id);
    }

    [Fact]
    public void ApplicationDefaultIsUsedOnlyWhenOrganizationValueIsAbsent()
    {
        var result = TimeZonePolicy.Resolve(null, "UTC");

        Assert.Equal(TimeZoneInfo.Utc.Id, result.Id);
    }

    [Fact]
    public void MissingOrganizationAndDefaultAreRejected()
    {
        var exception = Assert.Throws<TimeZoneConfigurationException>(() => TimeZonePolicy.Resolve(null, null));

        Assert.Equal(TimeZoneConfigurationError.Missing, exception.Error);
    }

    [Fact]
    public void EmptyOrganizationValueDoesNotSilentlyUseDefault()
    {
        var exception = Assert.Throws<TimeZoneConfigurationException>(() => TimeZonePolicy.Resolve("  ", "UTC"));

        Assert.Equal(TimeZoneConfigurationError.Empty, exception.Error);
    }

    [Fact]
    public void InvalidIdentifierIsRejectedBeforePlatformLookup()
    {
        var exception = Assert.Throws<TimeZoneConfigurationException>(() => TimeZonePolicy.Resolve("invalid\nzone", "UTC"));

        Assert.Equal(TimeZoneConfigurationError.Invalid, exception.Error);
    }

    [Fact]
    public void IdentifierMissingFromTheOperatingSystemIsReportedAsUnavailable()
    {
        var exception = Assert.Throws<TimeZoneConfigurationException>(() => TimeZonePolicy.Resolve("ODCA/DefinitelyUnavailable", "UTC"));

        Assert.Equal(TimeZoneConfigurationError.Unavailable, exception.Error);
    }
}
