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
    public static TheoryData<string> Names => new TheoryData<string>(["docker"]);

    /// <summary>A tiny image whose entry point runs until it is stopped, available to every backend.</summary>
    internal static SandboxSource Image => new SandboxSource(new SandboxImage("registry.k8s.io/pause:3.10"));

    internal static SandboxResources Resources => new SandboxResources(CpuMillicores: 250, MemoryMebibytes: 64);

    /// <summary>
    /// Creates the named provider in a scope of its own, so tests run in parallel without seeing each
    /// other's sandboxes.
    /// </summary>
    internal static ProviderUnderTest Create(string name)
    {
        string scope = "conformance-" + RandomNumberGenerator.GetHexString(12, lowercase: true);
        ServiceCollection services = new ServiceCollection();
        switch (name)
        {
            case "docker":
                services.AddDockerSandboxProvider(new DockerSandboxSettings(new Uri("unix:///var/run/docker.sock"), scope));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(name), name, "No provider has this name.");
        }

        return new ProviderUnderTest(services.BuildServiceProvider());
    }
}
