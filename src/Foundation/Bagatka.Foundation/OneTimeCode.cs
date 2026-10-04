using System;
using System.Security.Cryptography;
using System.Text;

namespace Bagatka.Foundation;

/// <summary>
/// Codes people pass on once, such as a machine's registration code or an invite: 16 characters
/// without look-alikes such as 0 and O, about 80 bits. Store only <see cref="Hash"/>; people may type
/// a code in any case and with stray spaces.
/// </summary>
public static class OneTimeCode
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int Length = 16;

    /// <summary>A new random code.</summary>
    public static string Create()
    {
        return RandomNumberGenerator.GetString(Alphabet, Length);
    }

    /// <summary>The SHA-256 of the code as people may type it: trimmed and upper-cased.</summary>
    public static byte[] Hash(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        return SHA256.HashData(Encoding.UTF8.GetBytes(code.Trim().ToUpperInvariant()));
    }
}
