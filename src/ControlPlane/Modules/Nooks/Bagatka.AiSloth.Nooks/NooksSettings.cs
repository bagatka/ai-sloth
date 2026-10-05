using System;
using System.Collections.Generic;

namespace Bagatka.AiSloth.Nooks;

/// <summary>
/// What the Nooks module needs from its host.
/// </summary>
public sealed record NooksSettings
{
    /// <summary>Creates the settings.</summary>
    /// <param name="connectionString">The PostgreSQL database that holds the <c>nooks</c> schema.</param>
    /// <param name="daemonUrl">The control plane's daemon endpoint as a nook reaches it, such as <c>http://host.docker.internal:5171</c> in development.</param>
    /// <param name="image">The image a nook without a harness starts from; it runs <c>slothd</c> (<c>src/Daemon/Dockerfile</c>).</param>
    /// <param name="cpuMillicores">The CPU each nook gets, in thousandths of a core.</param>
    /// <param name="memoryMebibytes">The memory each nook gets.</param>
    /// <param name="harnessImages">
    /// The image for each harness a nook can carry, by harness ID, such as <c>claude-code</c>: the base
    /// image with that harness installed. A nook carries one harness, so hosts pull only the ones used.
    /// </param>
    /// <param name="sleepAfter">How long a nook nobody uses stays awake: two minutes unless given.</param>
    /// <param name="evictAfter">How long a nook sleeps before its sandbox is deleted: a day unless given.</param>
    public NooksSettings(string connectionString, Uri daemonUrl, string image, int cpuMillicores, int memoryMebibytes, IReadOnlyDictionary<string, string>? harnessImages = null, TimeSpan? sleepAfter = null, TimeSpan? evictAfter = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(daemonUrl);
        if (!daemonUrl.IsAbsoluteUri || daemonUrl.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException("The daemon URL must be an absolute http or https URL.", nameof(daemonUrl));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(image);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cpuMillicores);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(memoryMebibytes);
        ConnectionString = connectionString;
        DaemonUrl = daemonUrl;
        Image = image;
        CpuMillicores = cpuMillicores;
        MemoryMebibytes = memoryMebibytes;
        HarnessImages = new Dictionary<string, string>(harnessImages ?? new Dictionary<string, string>(StringComparer.Ordinal), StringComparer.Ordinal);
        SleepAfter = sleepAfter ?? TimeSpan.FromMinutes(2);
        EvictAfter = evictAfter ?? TimeSpan.FromDays(1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(SleepAfter, TimeSpan.Zero, nameof(sleepAfter));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(EvictAfter, TimeSpan.Zero, nameof(evictAfter));
    }

    /// <summary>The PostgreSQL database that holds the <c>nooks</c> schema.</summary>
    public string ConnectionString { get; }

    /// <summary>The control plane's daemon endpoint as a nook reaches it.</summary>
    public Uri DaemonUrl { get; }

    /// <summary>The image a nook without a harness starts from.</summary>
    public string Image { get; }

    /// <summary>The image for each harness a nook can carry, by harness ID.</summary>
    public IReadOnlyDictionary<string, string> HarnessImages { get; }

    /// <summary>The CPU each nook gets, in thousandths of a core.</summary>
    public int CpuMillicores { get; }

    /// <summary>The memory each nook gets.</summary>
    public int MemoryMebibytes { get; }

    /// <summary>How long a nook nobody uses stays awake.</summary>
    public TimeSpan SleepAfter { get; }

    /// <summary>How long a nook sleeps before its sandbox is deleted.</summary>
    public TimeSpan EvictAfter { get; }
}
