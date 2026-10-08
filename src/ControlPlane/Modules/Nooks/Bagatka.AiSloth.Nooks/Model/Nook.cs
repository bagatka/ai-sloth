using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Nooks.Model;

// Where agents work. The record comes first; the lifecycle job creates its sandbox, and its daemon
// connecting makes it Ready.
internal sealed class Nook
{
    public const int MaxProviderLength = 40;
    public const int MaxLocationLength = 64;
    public const int MaxImageLength = 32;

    // Used by Create and by EF: parameter names match property names.
    private Nook(NookId id, WorkspaceId workspaceId, string provider, string? location, string? image, NookStatus status, DateTimeOffset createdAt, UserId? createdBy, UserId? reservedFor, NookId? copyOf, int? copyCheckpoint, List<string> keptPaths, bool fromScratch)
    {
        Id = id;
        WorkspaceId = workspaceId;
        Provider = provider;
        Location = location;
        Image = image;
        Status = status;
        CreatedAt = createdAt;
        CreatedBy = createdBy;
        ReservedFor = reservedFor;
        CopyOf = copyOf;
        CopyCheckpoint = copyCheckpoint;
        KeptPaths = keptPaths;
        FromScratch = fromScratch;
    }

    public NookId Id { get; private set; }

    public WorkspaceId WorkspaceId { get; private set; }

    // The provider's name, and where within the provider the nook runs, such as a machine.
    public string Provider { get; private set; }

    public string? Location { get; private set; }

    // The image it starts from, by the name the deployment offers it under; none for the base image.
    // It never changes, so neither does what is installed in the nook.
    public string? Image { get; private set; }

    public NookStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    // Whose GitHub connection copies its repositories in; none for a nook the control plane created.
    public UserId? CreatedBy { get; private set; }

    // The one person who may change what runs in it or its files; none for everyone with Write on its
    // workspace.
    public UserId? ReservedFor { get; private set; }

    // The nook whose files it starts with, if any, and which of its checkpoints; none until one is
    // taken for the copy. A nook that lost its sandbox starts again from its own latest checkpoint.
    public NookId? CopyOf { get; private set; }

    public int? CopyCheckpoint { get; private set; }

    // Absolute paths outside /work its checkpoints keep too.
    public List<string> KeptPaths { get; private set; }

    // Whether its sandbox starts from its image even when a ready copy would match, such as to test a
    // setup from scratch.
    public bool FromScratch { get; private set; }

    // When the ready copy its sandbox started from was made; none when it started from its image.
    public DateTimeOffset? ReadyCopyMadeAt { get; private set; }

    // Whether its setup started after its files arrived and a ready copy may be taken once it ended.
    public bool ReadyCopyDue { get; private set; }

    // Whether its sources are in place: its repositories copied in, or another nook's files. Nothing
    // else runs in it before.
    public bool SourcesReady { get; private set; }

    // The setup scripts found when its files arrived, relative to /work, and the process
    // that runs them; none when it has no scripts.
    public List<string> SetupScripts { get; private set; } = [];

    public ProcessId? SetupProcessId { get; private set; }

    // When it fell asleep, while it sleeps; whether its sandbox was deleted since, so it comes back
    // from its latest checkpoint; and whether its resume scripts run again before anything else,
    // because it woke.
    public DateTimeOffset? SleptAt { get; private set; }

    public bool Evicted { get; private set; }

    public bool ResumeDue { get; private set; }

    // The SHA-256 of the daemon's token; the token itself is never stored.
    public byte[]? DaemonTokenHash { get; private set; }

    // PostgreSQL's xmin: concurrent changes to one nook conflict instead of overwriting each other.
    public uint Version { get; private set; }

    public static Nook Create(WorkspaceId workspaceId, ProviderId provider, string? image, UserId? createdBy, UserId? reservedFor, NookId? copyOf, int? copyCheckpoint, List<string> keptPaths, bool fromScratch, TimeProvider time)
    {
        return new Nook(NookId.New(), workspaceId, provider.Name, provider.Location, image, NookStatus.Starting, time.GetUtcNow(), createdBy, reservedFor, copyOf, copyCheckpoint, keptPaths, fromScratch);
    }

    // Its sandbox is about to be created, from the ready copy made then, or from its image.
    public void StartsFrom(DateTimeOffset? readyCopyMadeAt)
    {
        ReadyCopyMadeAt = readyCopyMadeAt;
    }

    // Its files are in place, and its setup, if it has one, runs in the process. A run of setup
    // scripts, not only resume ones, may earn a ready copy.
    public void SourcesPrepared(List<string> setupScripts, ProcessId? setupProcess)
    {
        SourcesReady = true;
        ResumeDue = false;
        SetupScripts = setupScripts;
        SetupProcessId = setupProcess;
        ReadyCopyDue = setupProcess is not null && RunsSetup(setupScripts);
    }

    // When the ready copy its latest setup run set it up from was made: none when the run set it up from
    // scratch, or only resumed it.
    public DateTimeOffset? SetUpFromReadyCopyMadeAt => RunsSetup(SetupScripts) ? ReadyCopyMadeAt : null;

    // Its ready copy was taken, or its setup didn't earn one.
    public void ReadyCopyHandled()
    {
        ReadyCopyDue = false;
    }

    // Its provider released its compute, keeping its memory or only its files.
    public void FellAsleep(TimeProvider time)
    {
        Status = NookStatus.Asleep;
        SleptAt = time.GetUtcNow();
    }

    // Its provider gave it compute again; its daemon connecting makes it Ready, and its resume scripts
    // run again first.
    public void Woke()
    {
        if (Status == NookStatus.Asleep)
        {
            Status = NookStatus.Starting;
        }

        SleptAt = null;
        ResumeDue = true;
    }

    // Asleep so long that its sandbox goes; it comes back from its latest checkpoint.
    public void Evict()
    {
        Evicted = true;
        ResumeDue = false;
        ReadyCopyDue = false;
        SourcesReady = false;
        SetupScripts = [];
        SetupProcessId = null;
        DaemonTokenHash = null;
    }

    public bool Asleep => Status == NookStatus.Asleep;

    // The checkpoint of the nook it copies that its files come from, once taken.
    public void Copies(int checkpoint)
    {
        CopyCheckpoint = checkpoint;
    }

    // Its sandbox is gone: a new one is created, whose files come from the latest checkpoint when
    // there is one, and otherwise from where they first came from.
    public void Replace(int? latestCheckpoint)
    {
        Status = NookStatus.Starting;
        SourcesReady = false;
        ResumeDue = false;
        ReadyCopyDue = false;
        SleptAt = null;
        Evicted = false;
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

    // Its daemon dialed in, so the nook runs, even one its provider resumed by itself, such as a
    // machine that restarted. A nook being deleted takes no more instructions.
    public bool DaemonConnected()
    {
        if (Status == NookStatus.Deleting)
        {
            return false;
        }

        Status = NookStatus.Ready;
        SleptAt = null;
        return true;
    }

    // Its daemon is away and its provider can't say what became of its sandbox, such as on a machine
    // that is offline. Returns whether that is news.
    public bool Offline()
    {
        if (Status != NookStatus.Ready)
        {
            return false;
        }

        Status = NookStatus.Offline;
        return true;
    }

    public void Fail()
    {
        if (Status != NookStatus.Deleting)
        {
            Status = NookStatus.Failed;
        }
    }

    // It can't run anywhere any more, such as when its machine was removed: it fails, and its daemon,
    // which may still run out of reach, is refused from now on.
    public void FailForGood()
    {
        DaemonTokenHash = null;
        Fail();
    }

    // Deleting again changes nothing.
    public void Delete()
    {
        Status = NookStatus.Deleting;
    }

    public NookSummary ToSummary(IReadOnlyList<NookSource> copies, NookUsage? usage, double nearlyFull)
    {
        bool diskNearlyFull = usage is { DiskTotalBytes: > 0 } && (double)usage.DiskUsedBytes / usage.DiskTotalBytes >= nearlyFull;
        return new NookSummary(Id, WorkspaceId, new ProviderId(Provider, Location).ToString(), Status, CreatedAt, usage, diskNearlyFull, Image, ReservedFor, copies);
    }

    // Whether the scripts of a run set the nook up, rather than only resume it.
    private static bool RunsSetup(List<string> scripts)
    {
        return scripts.Exists(script => script.EndsWith("/setup", StringComparison.Ordinal));
    }

    private static byte[] Hash(string token)
    {
        return SHA256.HashData(Encoding.UTF8.GetBytes(token));
    }
}
