using System;
using System.Security.Cryptography;
using System.Text;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Workspaces.Contracts;

namespace Bagatka.AiSloth.Nooks.Model;

// Where agents work. The record comes first; the reconciler creates its sandbox, and its daemon
// connecting makes it Running.
internal sealed class Nook
{
    public const int MaxProviderLength = 40;
    public const int MaxLocationLength = 64;
    public const int MaxHarnessLength = 32;

    // Used by Create and by EF: parameter names match property names.
    private Nook(NookId id, WorkspaceId workspaceId, string provider, string? location, string? harness, NookStatus status, DateTimeOffset createdAt)
    {
        Id = id;
        WorkspaceId = workspaceId;
        Provider = provider;
        Location = location;
        Harness = harness;
        Status = status;
        CreatedAt = createdAt;
    }

    public NookId Id { get; private set; }

    public WorkspaceId WorkspaceId { get; private set; }

    // The provider's name, and where within the provider the nook runs, such as a machine.
    public string Provider { get; private set; }

    public string? Location { get; private set; }

    // The harness its image carries for chats' agents; it never changes, because the image doesn't.
    public string? Harness { get; private set; }

    public NookStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    // The SHA-256 of the daemon's token; the token itself is never stored.
    public byte[]? DaemonTokenHash { get; private set; }

    public long? DiskTotalBytes { get; private set; }

    public long? DiskAvailableBytes { get; private set; }

    // PostgreSQL's xmin: concurrent changes to one nook conflict instead of overwriting each other.
    public uint Version { get; private set; }

    public static Nook Create(WorkspaceId workspaceId, ProviderId provider, string? harness, TimeProvider time)
    {
        return new Nook(NookId.New(), workspaceId, provider.Name, provider.Location, harness, NookStatus.Creating, time.GetUtcNow());
    }

    // A new token for the daemon of a sandbox about to be created. It replaces any earlier one, which
    // never reached a daemon.
    public string IssueDaemonToken()
    {
        string token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        DaemonTokenHash = Hash(token);
        return token;
    }

    public bool AcceptsDaemonToken(string token)
    {
        return DaemonTokenHash is not null && CryptographicOperations.FixedTimeEquals(DaemonTokenHash, Hash(token));
    }

    // Its daemon dialed in, so the nook runs. A nook being deleted takes no more instructions.
    public bool DaemonConnected()
    {
        switch (Status)
        {
            case NookStatus.Deleting:
                return false;
            case NookStatus.Creating or NookStatus.Running or NookStatus.Paused or NookStatus.Stopped or NookStatus.Unreachable or NookStatus.Failed:
                Status = NookStatus.Running;
                return true;
        }

        throw new InvalidOperationException("Nook " + Id.Value + " has no status.");
    }

    public void Fail()
    {
        if (Status != NookStatus.Deleting)
        {
            Status = NookStatus.Failed;
        }
    }

    // Deleting again changes nothing.
    public void Delete()
    {
        Status = NookStatus.Deleting;
    }

    public void ReportDisk(DiskUsage disk)
    {
        DiskTotalBytes = disk.TotalBytes;
        DiskAvailableBytes = disk.AvailableBytes;
    }

    public NookSummary ToSummary()
    {
        DiskUsage? disk = DiskTotalBytes is long total && DiskAvailableBytes is long available ? new DiskUsage(total, available) : null;
        return new NookSummary(Id, WorkspaceId, new ProviderId(Provider, Location).ToString(), Status, CreatedAt, disk, Harness);
    }

    private static byte[] Hash(string token)
    {
        return SHA256.HashData(Encoding.UTF8.GetBytes(token));
    }
}
