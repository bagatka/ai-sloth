using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Nooks.Model;

// Where agents work. The record comes first; the reconciler creates its sandbox, and its daemon
// connecting makes it Running.
internal sealed class Nook
{
    public const int MaxProviderLength = 40;
    public const int MaxLocationLength = 64;
    public const int MaxHarnessLength = 32;

    // Used by Create and by EF: parameter names match property names.
    private Nook(NookId id, WorkspaceId workspaceId, string provider, string? location, string? harness, NookStatus status, DateTimeOffset createdAt, UserId? createdBy, NookId? copyOf, int? copyCheckpoint, List<string> keptPaths)
    {
        Id = id;
        WorkspaceId = workspaceId;
        Provider = provider;
        Location = location;
        Harness = harness;
        Status = status;
        CreatedAt = createdAt;
        CreatedBy = createdBy;
        CopyOf = copyOf;
        CopyCheckpoint = copyCheckpoint;
        KeptPaths = keptPaths;
    }

    public NookId Id { get; private set; }

    public WorkspaceId WorkspaceId { get; private set; }

    // The provider's name, and where within the provider the nook runs, such as a machine.
    public string Provider { get; private set; }

    public string? Location { get; private set; }

    // The harness its image carries for its chat's agent; it never changes, because the image doesn't.
    public string? Harness { get; private set; }

    public NookStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    // Whose GitHub connection copies its repositories in; none for a nook the control plane created.
    public UserId? CreatedBy { get; private set; }

    // The nook whose files it starts with, if any, and which of its checkpoints; none until one is
    // taken for the copy. A nook that lost its sandbox starts again from its own latest checkpoint.
    public NookId? CopyOf { get; private set; }

    public int? CopyCheckpoint { get; private set; }

    // Absolute paths outside /work its checkpoints keep too.
    public List<string> KeptPaths { get; private set; }

    // Whether its sources are in place: its repositories copied in, or another nook's files, and the
    // agents' guide to them.
    // Nothing else runs in it before.
    public bool SourcesReady { get; private set; }

    // The setup scripts found when its files arrived, relative to /work, and the process
    // that runs them; none when it has no scripts.
    public List<string> SetupScripts { get; private set; } = [];

    public ProcessId? SetupProcessId { get; private set; }

    // When it fell asleep, while it sleeps; and whether its resume scripts run again before anything
    // else, because it woke.
    public DateTimeOffset? SleptAt { get; private set; }

    public bool ResumeDue { get; private set; }

    // The SHA-256 of the daemon's token; the token itself is never stored.
    public byte[]? DaemonTokenHash { get; private set; }

    public long? DiskTotalBytes { get; private set; }

    public long? DiskAvailableBytes { get; private set; }

    // PostgreSQL's xmin: concurrent changes to one nook conflict instead of overwriting each other.
    public uint Version { get; private set; }

    public static Nook Create(WorkspaceId workspaceId, ProviderId provider, string? harness, UserId? createdBy, NookId? copyOf, int? copyCheckpoint, List<string> keptPaths, TimeProvider time)
    {
        return new Nook(NookId.New(), workspaceId, provider.Name, provider.Location, harness, NookStatus.Creating, time.GetUtcNow(), createdBy, copyOf, copyCheckpoint, keptPaths);
    }

    // Its files are in place, and its setup, if it has one, runs in the process.
    public void SourcesPrepared(List<string> setupScripts, ProcessId? setupProcess)
    {
        SourcesReady = true;
        ResumeDue = false;
        SetupScripts = setupScripts;
        SetupProcessId = setupProcess;
    }

    // Nobody used it for a while, so its provider releases its compute. Returns whether it was awake.
    public bool FallAsleep()
    {
        if (Status != NookStatus.Running)
        {
            return false;
        }

        Status = NookStatus.Sleeping;
        return true;
    }

    // Its provider released its compute, keeping its memory (Paused) or only its files (Stopped).
    public void FellAsleep(NookStatus asleep, TimeProvider time)
    {
        if (asleep is not (NookStatus.Paused or NookStatus.Stopped))
        {
            throw new ArgumentOutOfRangeException(nameof(asleep), asleep, "A nook sleeps Paused or Stopped.");
        }

        Status = asleep;
        SleptAt = time.GetUtcNow();
    }

    // Its provider gave it compute again; its daemon connecting makes it Running, and its resume
    // scripts run again first. One a failure left going to sleep counts as Stopped, so its daemon
    // connecting wakes it too.
    public void Woke()
    {
        if (Status == NookStatus.Sleeping)
        {
            Status = NookStatus.Stopped;
        }

        SleptAt = null;
        ResumeDue = true;
    }

    // Asleep so long that its sandbox goes; it comes back from its latest checkpoint.
    public void Evict()
    {
        Status = NookStatus.Evicted;
        SleptAt = null;
        ResumeDue = false;
        SourcesReady = false;
        SetupScripts = [];
        SetupProcessId = null;
        DaemonTokenHash = null;
    }

    public bool Asleep => Status is NookStatus.Sleeping or NookStatus.Paused or NookStatus.Stopped or NookStatus.Evicted;

    // The checkpoint of the nook it copies that its files come from, once taken.
    public void Copies(int checkpoint)
    {
        CopyCheckpoint = checkpoint;
    }

    // Its sandbox is gone: a new one is created, whose files come from the latest checkpoint when
    // there is one, and otherwise from where they first came from.
    public void Replace(int? latestCheckpoint)
    {
        Status = NookStatus.Creating;
        SourcesReady = false;
        ResumeDue = false;
        SleptAt = null;
        SetupScripts = [];
        SetupProcessId = null;
        DaemonTokenHash = null;
        if (latestCheckpoint is int number)
        {
            CopyOf = Id;
            CopyCheckpoint = number;
        }
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

    // Its daemon dialed in, so the nook runs. A nook being deleted takes no more instructions, and one
    // going to sleep stays so, its daemon about to be frozen or stopped.
    public bool DaemonConnected()
    {
        switch (Status)
        {
            case NookStatus.Deleting:
                return false;
            case NookStatus.Sleeping:
                return true;
            case NookStatus.Creating or NookStatus.Running or NookStatus.Paused or NookStatus.Stopped or NookStatus.Unreachable or NookStatus.Failed or NookStatus.Evicted:
                Status = NookStatus.Running;
                return true;
        }

        throw new InvalidOperationException("Nook " + Id.Value + " has no status.");
    }

    // Its daemon is away and its provider can't say what became of its sandbox, such as on a machine
    // that is offline. Returns whether that is news.
    public bool Unreachable()
    {
        if (Status != NookStatus.Running)
        {
            return false;
        }

        Status = NookStatus.Unreachable;
        return true;
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

    public NookSummary ToSummary(IReadOnlyList<NookSource> copies)
    {
        DiskUsage? disk = DiskTotalBytes is long total && DiskAvailableBytes is long available ? new DiskUsage(total, available) : null;
        return new NookSummary(Id, WorkspaceId, new ProviderId(Provider, Location).ToString(), Status, CreatedAt, disk, Harness, copies);
    }

    private static byte[] Hash(string token)
    {
        return SHA256.HashData(Encoding.UTF8.GetBytes(token));
    }
}
