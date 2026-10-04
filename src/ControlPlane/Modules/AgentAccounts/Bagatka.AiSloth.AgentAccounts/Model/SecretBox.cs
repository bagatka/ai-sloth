using System;
using System.Security.Cryptography;
using System.Text;
using Bagatka.AiSloth.AgentAccounts.Contracts;

namespace Bagatka.AiSloth.AgentAccounts.Model;

// Encrypts accounts' secrets at rest with AES-256-GCM (PATTERNS.md, "Secrets at rest"). The account's
// ID is authenticated with each secret, so a secret copied onto another row doesn't open.
// Not handled: rotating the key; a new key makes every stored secret unreadable.
internal sealed class SecretBox(string encryptionKey)
{
    private const byte Format = 1;
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly byte[] _key = SHA256.HashData(Encoding.UTF8.GetBytes(encryptionKey));

    // Format byte, nonce, tag, then the ciphertext.
    public byte[] Seal(string secret, AgentAccountId account)
    {
        byte[] plaintext = Encoding.UTF8.GetBytes(secret);
        byte[] box = new byte[1 + NonceSize + TagSize + plaintext.Length];
        box[0] = Format;
        Span<byte> nonce = box.AsSpan(1, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using AesGcm aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plaintext, box.AsSpan(1 + NonceSize + TagSize), box.AsSpan(1 + NonceSize, TagSize), account.Value.ToByteArray());
        return box;
    }

    public string Open(byte[] box, AgentAccountId account)
    {
        if (box.Length < 1 + NonceSize + TagSize || box[0] != Format)
        {
            throw new InvalidOperationException("Agent account " + account.Value + " has a secret in an unknown format.");
        }

        byte[] plaintext = new byte[box.Length - 1 - NonceSize - TagSize];
        using AesGcm aes = new AesGcm(_key, TagSize);
        aes.Decrypt(box.AsSpan(1, NonceSize), box.AsSpan(1 + NonceSize + TagSize), box.AsSpan(1 + NonceSize, TagSize), plaintext, account.Value.ToByteArray());
        return Encoding.UTF8.GetString(plaintext);
    }
}
