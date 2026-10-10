using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Identity;
using Bagatka.Azure.Sandboxes;
using Bagatka.Azure.Sandboxes.Models;
using Bagatka.Foundation;
using Bagatka.Sandboxing.Azure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bagatka.Sandboxing.ConformanceTests;

/// <summary>
/// Behavior of the Azure provider that the contract can't observe, against the real service when
/// BAGATKA_AZURE_SANDBOXES_GROUP names a sandbox group as <c>subscription/resource-group/group/region</c>
/// and BAGATKA_AZURE_CONTAINER_REGISTRY an Azure Container Registry, such as <c>myco.azurecr.io</c>,
/// that has <c>pause:3.10</c> (<c>az acr import</c>), both open to the <c>az login</c> identity.
/// </summary>
public sealed class AzureSandboxProviderTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_image_from_an_Azure_Container_Registry_is_pulled_with_the_providers_own_identity()
    {
        string? group = Environment.GetEnvironmentVariable("BAGATKA_AZURE_SANDBOXES_GROUP");
        string? registry = Environment.GetEnvironmentVariable("BAGATKA_AZURE_CONTAINER_REGISTRY");
        Assert.SkipWhen(string.IsNullOrEmpty(group) || string.IsNullOrEmpty(registry), "Set BAGATKA_AZURE_SANDBOXES_GROUP and BAGATKA_AZURE_CONTAINER_REGISTRY to run against the real service.");
        string[] parts = group!.Split('/');
        string image = registry + "/pause:3.10";
        AzureCliCredential credential = new AzureCliCredential();
        SandboxGroupClient client = new SandboxGroupClient(SandboxGroupClient.GetEndpoint(parts[3]), new SandboxGroupId(parts[0], parts[1], parts[2]), credential);
        AzureSandboxSettings settings = new AzureSandboxSettings(parts[0], parts[1], parts[2], parts[3], "conformance-" + RandomNumberGenerator.GetHexString(12, lowercase: true), [registry!]);
        ServiceCollection services = new ServiceCollection();
        services.AddAzureSandboxProvider(settings, credential);
        await using ServiceProvider built = services.BuildServiceProvider();
        ISandboxProvider provider = built.GetRequiredService<ISandboxProvider>();
        SandboxSpec spec = new SandboxSpec(SandboxKey.From(Guid.CreateVersion7()), new SandboxSource(new SandboxImage(image)), ProvidersUnderTest.Resources, new Dictionary<string, string>(StringComparer.Ordinal), Location: null);

        // Without signing in, Azure can't pull the image at all.
        RequestFailedException refused = await Assert.ThrowsAsync<RequestFailedException>(() => client.CreateDiskImageAsync(WaitUntil.Completed, new DiskImageCreateOptions(image), Ct));
        Result<SandboxObservation> created;
        try
        {
            created = await provider.CreateAsync(spec, Ct);
        }
        finally
        {
            await provider.DeleteAsync(spec.Key, CancellationToken.None);
        }

        Assert.Equal("RegistryAuthFailed", refused.ErrorCode);
        Assert.False(created.Failed, created.Failed ? created.Error.Message : null);
        Assert.Equal(spec.Key, created.Output.Key);
    }
}
