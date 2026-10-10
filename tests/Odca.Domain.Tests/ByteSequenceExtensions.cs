namespace Odca.Domain.Tests;

/// <summary>Suporte para asserts de sequência exata de bytes em artefatos binários (PDF).</summary>
internal static class ByteSequenceExtensions
{
    public static bool Contains(byte[] haystack, byte[] needle)
    {
        if (needle.Length == 0) return true;
        for (var i = 0; i + needle.Length <= haystack.Length; i++)
        {
            var matched = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    matched = false;
                    break;
                }
            }
            if (matched) return true;
        }
        return false;
    }
}
