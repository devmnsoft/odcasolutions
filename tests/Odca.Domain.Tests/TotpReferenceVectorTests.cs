using Odca.Application.Common;
using Odca.Application.Identity;

namespace Odca.Domain.Tests;

public sealed class TotpReferenceVectorTests
{
    [Fact]
    public void MatchesRfc6238Sha1VectorTruncatedToConfiguredSixDigits()
    {
        // RFC 6238 uses the independent ASCII secret "12345678901234567890"
        // (Base32 below) and expects 94287082 at Unix time 59. The ODCA profile
        // intentionally displays the final six digits.
        var service = new TotpService(new FixedClock(
            DateTimeOffset.FromUnixTimeSeconds(59)));

        Assert.Equal("287082", service.GenerateCode("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ"));
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
