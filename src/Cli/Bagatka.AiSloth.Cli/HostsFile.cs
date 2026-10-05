using System;
using System.Collections.Generic;
using System.Linq;

namespace Bagatka.AiSloth.Cli;

// The hosts sloth is signed in to, and the one commands use.
internal sealed record HostsFile(string? Current, IReadOnlyList<SignedInHost> Hosts)
{
    public static HostsFile Empty { get; } = new HostsFile(Current: null, []);

    public SignedInHost? Find(string name)
    {
        return Hosts.SingleOrDefault(host => string.Equals(host.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    public SignedInHost? CurrentHost()
    {
        return Current is null ? null : Find(Current);
    }

    // Adds or replaces the host and makes it current.
    public HostsFile With(SignedInHost host)
    {
        List<SignedInHost> hosts = [.. Hosts.Where(other => !string.Equals(other.Name, host.Name, StringComparison.OrdinalIgnoreCase)), host];
        return new HostsFile(host.Name, hosts);
    }

    public HostsFile Without(string name)
    {
        List<SignedInHost> hosts = [.. Hosts.Where(other => !string.Equals(other.Name, name, StringComparison.OrdinalIgnoreCase))];
        string? current = string.Equals(Current, name, StringComparison.OrdinalIgnoreCase) ? hosts.FirstOrDefault()?.Name : Current;
        return new HostsFile(current, hosts);
    }
}
