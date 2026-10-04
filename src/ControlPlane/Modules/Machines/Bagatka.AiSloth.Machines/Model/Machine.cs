using System;
using System.Security.Cryptography;
using System.Text;
using Bagatka.AiSloth.Machines.Contracts;
using Bagatka.AiSloth.Workspaces.Contracts;

namespace Bagatka.AiSloth.Machines.Model;

// A computer a workspace added to run its nooks. It registers once with a short-lived code, then
// connects with a token; both are kept only as hashes.
internal sealed class Machine
{
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromHours(1);

    // Codes are typed by people: 16 characters without look-alikes such as 0 and O, about 80 bits.
    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int CodeLength = 16;

    // Used by Add and by EF: parameter names match property names.
    private Machine(MachineId id, WorkspaceId workspaceId, MachineName name, DateTimeOffset addedAt)
    {
        Id = id;
        WorkspaceId = workspaceId;
        Name = name;
        AddedAt = addedAt;
    }

    public MachineId Id { get; private set; }

    public WorkspaceId WorkspaceId { get; private set; }

    public MachineName Name { get; private set; }

    public DateTimeOffset AddedAt { get; private set; }

    public byte[]? CodeHash { get; private set; }

    public DateTimeOffset? CodeExpiresAt { get; private set; }

    public byte[]? TokenHash { get; private set; }

    // PostgreSQL's xmin: two registrations with one code can't both win.
    public uint Version { get; private set; }

    public bool IsRegistered => TokenHash is not null;

    public static Machine Add(WorkspaceId workspaceId, MachineName name, TimeProvider time)
    {
        return new Machine(MachineId.New(), workspaceId, name, time.GetUtcNow());
    }

    // Codes are compared case-insensitively, as people type them.
    public static byte[] HashCode(string code)
    {
        return Hash(code.Trim().ToUpperInvariant());
    }

    public string IssueRegistrationCode(TimeProvider time)
    {
        string code = RandomNumberGenerator.GetString(CodeAlphabet, CodeLength);
        CodeHash = HashCode(code);
        CodeExpiresAt = time.GetUtcNow() + CodeLifetime;
        return code;
    }

    // Trades the code for a token; null once the code expired. The code never works again.
    public string? Register(TimeProvider time)
    {
        if (CodeExpiresAt is null || time.GetUtcNow() > CodeExpiresAt)
        {
            return null;
        }

        string token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        TokenHash = Hash(token);
        CodeHash = null;
        CodeExpiresAt = null;
        return token;
    }

    public bool AcceptsToken(string token)
    {
        return TokenHash is not null && CryptographicOperations.FixedTimeEquals(TokenHash, Hash(token));
    }

    private static byte[] Hash(string secret)
    {
        return SHA256.HashData(Encoding.UTF8.GetBytes(secret));
    }
}
