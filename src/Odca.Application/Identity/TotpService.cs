using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Odca.Application.Common;

namespace Odca.Application.Identity;

public sealed class TotpService(IClock clock)
{
    public const int PeriodSeconds = 30;
    private const int SecretBytes = 20;
    private const int CodeDigits = 6;
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string GenerateSecret() => EncodeBase32(RandomNumberGenerator.GetBytes(SecretBytes));

    public string GenerateCode(string secret) => GenerateCode(secret, CurrentTimeStep());

    public TotpVerification? Verify(string secret, string providedCode)
    {
        var normalizedCode = new string(providedCode.Where(char.IsDigit).ToArray());
        if (normalizedCode.Length != CodeDigits)
        {
            return null;
        }

        var current = CurrentTimeStep();
        for (var offset = -1; offset <= 1; offset++)
        {
            var step = current + offset;
            var expected = GenerateCode(secret, step);
            if (CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(expected),
                    Encoding.ASCII.GetBytes(normalizedCode)))
            {
                return new TotpVerification(step);
            }
        }

        return null;
    }

    public static string BuildOtpAuthUri(string issuer, string account, string secret)
    {
        var label = Uri.EscapeDataString($"{issuer}:{account}");
        var escapedIssuer = Uri.EscapeDataString(issuer);
        return $"otpauth://totp/{label}?secret={secret}&issuer={escapedIssuer}&digits={CodeDigits}&period={PeriodSeconds}";
    }

    private long CurrentTimeStep() => clock.UtcNow.ToUnixTimeSeconds() / PeriodSeconds;

    private static string GenerateCode(string secret, long timeStep)
    {
        Span<byte> counter = stackalloc byte[8];
        for (var index = 7; index >= 0; index--)
        {
            counter[index] = (byte)(timeStep & 0xff);
            timeStep >>= 8;
        }

        var key = DecodeBase32(secret);
        Span<byte> hash = stackalloc byte[20];
#pragma warning disable CA5350 // TOTP (RFC 6238) requires HMAC-SHA1 unless a different algorithm is negotiated.
        using var hmac = new HMACSHA1(key);
#pragma warning restore CA5350
        hmac.TryComputeHash(counter, hash, out _);
        var offset = hash[^1] & 0x0f;
        var binary =
            ((hash[offset] & 0x7f) << 24) |
            ((hash[offset + 1] & 0xff) << 16) |
            ((hash[offset + 2] & 0xff) << 8) |
            (hash[offset + 3] & 0xff);
        var otp = binary % 1_000_000;
        return otp.ToString("D6", CultureInfo.InvariantCulture);
    }

    private static string EncodeBase32(byte[] data)
    {
        var result = new StringBuilder((data.Length + 4) / 5 * 8);
        var buffer = 0;
        var bitsLeft = 0;
        foreach (var value in data)
        {
            buffer = (buffer << 8) | value;
            bitsLeft += 8;
            while (bitsLeft >= 5)
            {
                result.Append(Base32Alphabet[(buffer >> (bitsLeft - 5)) & 31]);
                bitsLeft -= 5;
            }
        }

        if (bitsLeft > 0)
        {
            result.Append(Base32Alphabet[(buffer << (5 - bitsLeft)) & 31]);
        }

        return result.ToString();
    }

    private static byte[] DecodeBase32(string value)
    {
        var cleaned = value.Trim().Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
        var output = new List<byte>(cleaned.Length * 5 / 8);
        var buffer = 0;
        var bitsLeft = 0;
        foreach (var character in cleaned)
        {
            var index = Base32Alphabet.IndexOf(character, StringComparison.Ordinal);
            if (index < 0)
            {
                throw new FormatException("Segredo MFA inválido.");
            }

            buffer = (buffer << 5) | index;
            bitsLeft += 5;
            if (bitsLeft >= 8)
            {
                output.Add((byte)((buffer >> (bitsLeft - 8)) & 0xff));
                bitsLeft -= 8;
            }
        }

        return [.. output];
    }
}

public sealed record TotpVerification(long TimeStep);
