using System;
using System.Collections.Generic;

namespace Bagatka.AiSloth.Nooks;

/// <summary>
/// What the Nooks module needs from its host.
/// </summary>
public sealed record NooksSettings
{
    /// <summary>Creates the settings.</summary>
    /// <param name="daemonUrl">The control plane's daemon endpoint as a nook reaches it, such as <c>http://host.docker.internal:5171</c> in development.</param>
    /// <param name="image">The base image a nook starts from; it runs <c>slothd</c> (<c>src/Daemon/Dockerfile</c>).</param>
    /// <param name="images">
    /// The other images a nook can start from, by name, such as <c>claude-code</c> for the base image with
    /// that harness installed. A nook starts from one image, so hosts pull only the ones used.
    /// </param>
    /// <param name="cpuMillicores">The CPU each nook gets, in thousandths of a core: two cores unless given.</param>
    /// <param name="memoryMebibytes">The memory each nook gets: 4 GiB unless given.</param>
    /// <param name="sleepAfter">How long a nook nobody uses stays awake: two minutes unless given.</param>
    /// <param name="evictAfter">How long a nook sleeps before its sandbox is deleted: a day unless given.</param>
    /// <param name="nearlyFull">
    /// How full a nook's disk is, from 0 to 1, when people are warned that its work and its
    /// checkpoints may soon fail: 0.85 unless given.
    /// </param>
    /// <param name="maxAwakePerWorkspace">
    /// How many of a workspace's nooks may be awake at once, bounding what a workspace costs its host:
    /// 10 unless given.
    /// </param>
    public NooksSettings(
        Uri daemonUrl,
        string image,
        IReadOnlyDictionary<string, string>? images = null,
        int? cpuMillicores = null,
        int? memoryMebibytes = null,
        TimeSpan? sleepAfter = null,
        TimeSpan? evictAfter = null,
        double? nearlyFull = null,
        int? maxAwakePerWorkspace = null)
    {
        ArgumentNullException.ThrowIfNull(daemonUrl);
        if (!daemonUrl.IsAbsoluteUri || daemonUrl.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException("The daemon URL must be an absolute http or https URL.", nameof(daemonUrl));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(image);
        DaemonUrl = daemonUrl;
        Image = image;
        Images = new Dictionary<string, string>(images ?? new Dictionary<string, string>(StringComparer.Ordinal), StringComparer.Ordinal);
        CpuMillicores = cpuMillicores ?? 2000;
        MemoryMebibytes = memoryMebibytes ?? 4096;
        SleepAfter = sleepAfter ?? TimeSpan.FromMinutes(2);
        EvictAfter = evictAfter ?? TimeSpan.FromDays(1);
        NearlyFull = nearlyFull ?? 0.85;
        MaxAwakePerWorkspace = maxAwakePerWorkspace ?? 10;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxAwakePerWorkspace, nameof(maxAwakePerWorkspace));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(CpuMillicores, nameof(cpuMillicores));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MemoryMebibytes, nameof(memoryMebibytes));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(SleepAfter, TimeSpan.Zero, nameof(sleepAfter));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(EvictAfter, TimeSpan.Zero, nameof(evictAfter));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(NearlyFull, nameof(nearlyFull));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(NearlyFull, 1, nameof(nearlyFull));
    }

    /// <summary>The control plane's daemon endpoint as a nook reaches it.</summary>
    public Uri DaemonUrl { get; }

    /// <summary>The base image a nook starts from.</summary>
    public string Image { get; }

    /// <summary>The other images a nook can start from, by name.</summary>
    public IReadOnlyDictionary<string, string> Images { get; }

    // The image a nook with the named image starts from, or the base one for none; none when the
    // deployment no longer offers it.
    internal string? ImageOf(string? name)
    {
        return name is null ? Image : Images.GetValueOrDefault(name);
    }

    /// <summary>The CPU each nook gets, in thousandths of a core.</summary>
    public int CpuMillicores { get; }

    /// <summary>The memory each nook gets.</summary>
    public int MemoryMebibytes { get; }

    /// <summary>How long a nook nobody uses stays awake.</summary>
    public TimeSpan SleepAfter { get; }

    /// <summary>How long a nook sleeps before its sandbox is deleted.</summary>
    public TimeSpan EvictAfter { get; }

    /// <summary>How full a nook's disk is, from 0 to 1, when people are warned.</summary>
    public double NearlyFull { get; }

    /// <summary>How many of a workspace's nooks may be awake at once.</summary>
    public int MaxAwakePerWorkspace { get; }

    /// <summary>
    /// The PostHog project nooks' daemons report their crashes to, by its ingestion host, such as
    /// <c>https://eu.i.posthog.com</c>; none unless given, and then they report nothing.
    /// </summary>
    public Uri? CrashReportsHost { get; init; }

    /// <summary>That project's token, which can only send.</summary>
    public string? CrashReportsToken { get; init; }
}
