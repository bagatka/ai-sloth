using System;

namespace Bagatka.AiSloth.Nooks;

/// <summary>
/// What the Nooks module needs from its host.
/// </summary>
public sealed record NooksSettings
{
    /// <summary>Creates the settings.</summary>
    /// <param name="connectionString">The PostgreSQL database that holds the <c>nooks</c> schema.</param>
    /// <param name="daemonUrl">The control plane's daemon endpoint as a nook reaches it, such as <c>http://host.docker.internal:5171</c> in development.</param>
    /// <param name="image">The image every nook starts from; it runs <c>slothd</c> (<c>src/Daemon/Dockerfile</c>).</param>
    /// <param name="cpuMillicores">The CPU each nook gets, in thousandths of a core.</param>
    /// <param name="memoryMebibytes">The memory each nook gets.</param>
    public NooksSettings(string connectionString, Uri daemonUrl, string image, int cpuMillicores, int memoryMebibytes)
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
    }

    /// <summary>The PostgreSQL database that holds the <c>nooks</c> schema.</summary>
    public string ConnectionString { get; }

    /// <summary>The control plane's daemon endpoint as a nook reaches it.</summary>
    public Uri DaemonUrl { get; }

    /// <summary>The image every nook starts from.</summary>
    public string Image { get; }

    /// <summary>The CPU each nook gets, in thousandths of a core.</summary>
    public int CpuMillicores { get; }

    /// <summary>The memory each nook gets.</summary>
    public int MemoryMebibytes { get; }
}
