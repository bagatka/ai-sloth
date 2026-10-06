using System;
using System.Security.Cryptography;
using Azure.Identity;
using Bagatka.Sandboxing.Azure;
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
    /// <summary>
    /// The names the tests run with: Azure too when BAGATKA_AZURE_SANDBOXES_GROUP is
    /// <c>subscription/resource-group/group/region</c>, signed in with <c>az login</c>.
    /// </summary>
    public static TheoryData<string> Names => AzureGroup is null ? new TheoryData<string>(["docker", "remote"]) : new TheoryData<string>(["docker", "remote", "azure"]);

    /// <summary>A tiny image whose entry point runs until it is stopped, available to every backend.</summary>
    internal static SandboxSource Image => new SandboxSource(new SandboxImage("registry.k8s.io/pause:3.10"));

    internal static SandboxResources Resources => new SandboxResources(CpuMillicores: 250, MemoryMebibytes: 64);

    /// <summary>The Docker Engine, which has Sysbox: DOCKER_HOST's, as for the docker command, or the default one.</summary>
    internal static Uri DockerEndpoint { get; } = new Uri(Environment.GetEnvironmentVariable("DOCKER_HOST") is { Length: > 0 } host ? host : "unix:///var/run/docker.sock");

    private static string[]? AzureGroup { get; } = Environment.GetEnvironmentVariable("BAGATKA_AZURE_SANDBOXES_GROUP") is { Length: > 0 } group ? group.Split('/') : null;

    /// <summary>
    /// Creates the named provider in a scope of its own, so tests run in parallel without seeing each
    /// other's sandboxes.
    /// </summary>
    internal static ProviderUnderTest Create(string name)
    {
        string scope = "conformance-" + RandomNumberGenerator.GetHexString(12, lowercase: true);
        ServiceCollection services = new ServiceCollection();
        if (name is "azure" && AzureGroup is string[] group)
        {
            services.AddAzureSandboxProvider(new AzureSandboxSettings(group[0], group[1], group[2], group[3], scope), new AzureCliCredential());
        }
        else
        {
            services.AddDockerSandboxProvider(new DockerSandboxSettings(DockerEndpoint, scope));
        }

        ServiceProvider built = services.BuildServiceProvider();
        return name switch
        {
            "docker" or "azure" => new ProviderUnderTest(built, throughRemote: false),

            // Docker again, reached through RemoteSandboxProvider and SandboxCalls, as machines are.
            "remote" => new ProviderUnderTest(built, throughRemote: true),
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, "No provider has this name."),
        };
    }
}
