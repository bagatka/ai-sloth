using System;
using System.Security.Cryptography;
using System.Text;

namespace Bagatka.Foundation.Modules;

/// <summary>
/// Encrypts secrets a module keeps at rest with AES-256-GCM under a key from its settings (PATTERNS.md,
/// "Secrets at rest"). The ID of the row that holds a secret is authenticated with it, so a secret
/// copied onto another row doesn't open. Thread-safe.
/// </summary>
/// <remarks>Not handled: rotating the key; a new key makes every stored secret unreadable.</remarks>
/// <param name="encryptionKey">The module's encryption key, at least 32 random characters.</param>
public sealed class SecretBox(string encryptionKey)
{
    private const byte Format = 1;
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[] _key = SHA256.HashData(Encoding.UTF8.GetBytes(encryptionKey));

    /// <summary>Seals <paramref name="secret"/> for the row with ID <paramref name="row"/>: a format byte, the nonce, the tag, then the ciphertext.</summary>
    public byte[] Seal(string secret, Guid row)
    {
        byte[] plaintext = Encoding.UTF8.GetBytes(secret);
        byte[] box = new byte[1 + NonceSize + TagSize + plaintext.Length];
        box[0] = Format;
        Span<byte> nonce = box.AsSpan(1, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using AesGcm aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plaintext, box.AsSpan(1 + NonceSize + TagSize), box.AsSpan(1 + NonceSize, TagSize), row.ToByteArray());
        return box;
    }

    /// <summary>Opens what <see cref="Seal"/> sealed for the same row; throws for anything else.</summary>
    public string Open(byte[] box, Guid row)
    {
        if (box.Length < 1 + NonceSize + TagSize || box[0] != Format)
        {
            throw new InvalidOperationException("Row " + row + " has a secret in an unknown format.");
        }

        byte[] plaintext = new byte[box.Length - 1 - NonceSize - TagSize];
        using AesGcm aes = new AesGcm(_key, TagSize);
        aes.Decrypt(box.AsSpan(1, NonceSize), box.AsSpan(1 + NonceSize + TagSize), box.AsSpan(1 + NonceSize, TagSize), plaintext, row.ToByteArray());
        return Encoding.UTF8.GetString(plaintext);
    }

    /// <summary>
    /// A stable identifier for this deployment, for one purpose, derived from the key: it reveals
    /// nothing about the key and changes only with it. Formatted as a UUID (version 8, custom).
    /// </summary>
    public Guid DeriveId(string purpose)
    {
        byte[] hash = HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(purpose));
        byte[] id = hash[..16];
        id[6] = (byte)((id[6] & 0x0F) | 0x80);
        id[8] = (byte)((id[8] & 0x3F) | 0x80);
        return new Guid(id, bigEndian: true);
    }
}
