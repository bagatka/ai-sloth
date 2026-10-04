using System;

namespace Bagatka.AiSloth.Daemon;

/// <summary>
/// What the daemon needs, read once at startup from the environment the nook was created with.
/// </summary>
internal sealed record DaemonSettings
{
    public DaemonSettings(
        Uri controlPlaneUrl,
        Guid nookId,
        string token,
        string workingDirectory,
        string stateDirectory,
        OutputLimits limits,
        long diskReserveBytes)
    {
        ArgumentNullException.ThrowIfNull(controlPlaneUrl);
        if (!controlPlaneUrl.IsAbsoluteUri || controlPlaneUrl.Scheme is not ("https" or "http"))
        {
            throw new ArgumentException("The control plane URL must be an absolute http or https URL.", nameof(controlPlaneUrl));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentOutOfRangeException.ThrowIfNegative(diskReserveBytes);
        ControlPlaneUrl = controlPlaneUrl;
        NookId = nookId;
        Token = token;
        WorkingDirectory = workingDirectory;
        StateDirectory = stateDirectory;
        Limits = limits;
        DiskReserveBytes = diskReserveBytes;
    }

    /// <summary>Where the control plane's daemon endpoint listens.</summary>
    public Uri ControlPlaneUrl { get; }

    /// <summary>The nook this daemon runs in.</summary>
    public Guid NookId { get; }

    /// <summary>The secret the daemon proves its nook with.</summary>
    public string Token { get; }

    /// <summary>Where processes start unless an instruction says otherwise.</summary>
    public string WorkingDirectory { get; }

    /// <summary>Where the daemon keeps process output.</summary>
    public string StateDirectory { get; }

    /// <summary>How much output is kept.</summary>
    public OutputLimits Limits { get; }

    /// <summary>The space the daemon holds back for a full disk; 0 holds none.</summary>
    public long DiskReserveBytes { get; }
}
