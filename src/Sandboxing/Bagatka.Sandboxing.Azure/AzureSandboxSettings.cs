using System;
using System.Buffers;
using Bagatka.Azure.Sandboxes;

namespace Bagatka.Sandboxing.Azure;

/// <summary>
/// Settings for <see cref="AzureSandboxProviderRegistration.AddAzureSandboxProvider"/>: the sandbox
/// group sandboxes run in, and the deployment's scope within it.
/// </summary>
public sealed record AzureSandboxSettings
{
    private const int MaxScopeLength = 40;

    private static readonly SearchValues<char> ScopeCharacters = SearchValues.Create("abcdefghijklmnopqrstuvwxyz0123456789-");

    /// <summary>Creates the settings.</summary>
    /// <param name="subscriptionId">The Azure subscription the sandbox group is in.</param>
    /// <param name="resourceGroup">The resource group the sandbox group is in.</param>
    /// <param name="sandboxGroup">The sandbox group (<c>Microsoft.App/sandboxGroups</c>).</param>
    /// <param name="region">The sandbox group's region, such as <c>eastus2</c>.</param>
    /// <param name="scope">
    /// The deployment this provider serves: 1 to 40 lowercase letters, digits, or hyphens. Several
    /// deployments can share one sandbox group because each touches only its own scope.
    /// </param>
    public AzureSandboxSettings(string subscriptionId, string resourceGroup, string sandboxGroup, string region, string scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (scope.Length is 0 or > MaxScopeLength || scope.AsSpan().ContainsAnyExcept(ScopeCharacters))
        {
            throw new ArgumentException("The scope must be 1 to 40 lowercase letters, digits, or hyphens.", nameof(scope));
        }

        Group = new SandboxGroupId(subscriptionId, resourceGroup, sandboxGroup);
        Endpoint = SandboxGroupClient.GetEndpoint(region);
        Region = region;
        Scope = scope;
    }

    /// <summary>The Azure subscription the sandbox group is in.</summary>
    public string SubscriptionId => Group.SubscriptionId;

    /// <summary>The resource group the sandbox group is in.</summary>
    public string ResourceGroup => Group.ResourceGroupName;

    /// <summary>The sandbox group's name.</summary>
    public string SandboxGroup => Group.SandboxGroupName;

    /// <summary>The sandbox group's region.</summary>
    public string Region { get; }

    /// <summary>The deployment this provider serves.</summary>
    public string Scope { get; }

    /// <summary>The sandbox group.</summary>
    public SandboxGroupId Group { get; }

    /// <summary>The service in the sandbox group's region.</summary>
    public Uri Endpoint { get; }
}
