using System;
using System.Security.Cryptography;
using Bagatka.Sandboxing.Docker;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bagatka.Sandboxing.ConformanceTests;

/// <summary>
/// Every provider the conformance suite runs against. Adding a provider here runs every test
/// against it too.
/// </summary>
public static class ProvidersUnderTest
{
    /// <summary>The names the tests run with.</summary>
    public static TheoryData<string> Names => new TheoryData<string>(["docker", "remote"]);

    /// <summary>A tiny image whose entry point runs until it is stopped, available to every backend.</summary>
    internal static SandboxSource Image => new SandboxSource(new SandboxImage("registry.k8s.io/pause:3.10"));

    internal static SandboxResources Resources => new SandboxResources(CpuMillicores: 250, MemoryMebibytes: 64);

    /// <summary>The Docker Engine, which has Sysbox: DOCKER_HOST's, as for the docker command, or the default one.</summary>
    internal static Uri DockerEndpoint { get; } = new Uri(Environment.GetEnvironmentVariable("DOCKER_HOST") is { Length: > 0 } host ? host : "unix:///var/run/docker.sock");

    /// <summary>
    /// Creates the named provider in a scope of its own, so tests run in parallel without seeing each
    /// other's sandboxes.
    /// </summary>
    internal static ProviderUnderTest Create(string name)
    {
        string scope = "conformance-" + RandomNumberGenerator.GetHexString(12, lowercase: true);
        ServiceCollection services = new ServiceCollection();
        services.AddDockerSandboxProvider(new DockerSandboxSettings(DockerEndpoint, scope));
        ServiceProvider built = services.BuildServiceProvider();
        return name switch
        {
            "docker" => new ProviderUnderTest(built, throughRemote: false),

            // Docker again, reached through RemoteSandboxProvider and SandboxCalls, as machines are.
            "remote" => new ProviderUnderTest(built, throughRemote: true),
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, "No provider has this name."),
        };
    }
}
