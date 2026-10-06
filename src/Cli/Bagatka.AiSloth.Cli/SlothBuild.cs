using System;
using System.Linq;
using System.Reflection;

namespace Bagatka.AiSloth.Cli;

// What this sloth is: a release, which updates itself from its repository's releases, or built from
// source, which updates by building again; the platform its releases' files are named for, such as
// linux-x64; and the file it runs from, which an update replaces.
internal sealed record SlothBuild(SlothRelease? Release, string Platform, string? Executable)
{
    // A release's assembly carries the repository its releases come from, and its version.
    public static SlothBuild Of(Assembly assembly, string platform, string? executable)
    {
        string? repository = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(metadata => string.Equals(metadata.Key, "SlothReleases", StringComparison.Ordinal))?.Value;
        Version? version = assembly.GetName().Version;
        SlothRelease? release = repository is { Length: > 0 } && version is not null
            ? new SlothRelease(new Version(version.Major, version.Minor, version.Build), new Uri("https://api.github.com/repos/" + repository + "/releases/"))
            : null;
        return new SlothBuild(release, platform, executable);
    }
}
