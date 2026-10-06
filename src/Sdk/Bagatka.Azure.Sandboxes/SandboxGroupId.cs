using System;
using System.Globalization;

namespace Bagatka.Azure.Sandboxes;

/// <summary>
/// A sandbox group: the Azure resource sandboxes live in, made in Azure Resource Manager as
/// <c>Microsoft.App/sandboxGroups</c>.
/// </summary>
public sealed class SandboxGroupId
{
    /// <summary>Identifies the group.</summary>
    /// <param name="subscriptionId">The Azure subscription's ID.</param>
    /// <param name="resourceGroupName">The resource group it is in.</param>
    /// <param name="sandboxGroupName">Its name.</param>
    public SandboxGroupId(string subscriptionId, string resourceGroupName, string sandboxGroupName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subscriptionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceGroupName);
        ArgumentException.ThrowIfNullOrWhiteSpace(sandboxGroupName);
        SubscriptionId = subscriptionId;
        ResourceGroupName = resourceGroupName;
        SandboxGroupName = sandboxGroupName;
    }

    /// <summary>The Azure subscription's ID.</summary>
    public string SubscriptionId { get; }

    /// <summary>The resource group the sandbox group is in.</summary>
    public string ResourceGroupName { get; }

    /// <summary>The sandbox group's name.</summary>
    public string SandboxGroupName { get; }

    /// <summary>The group's path in the service's URLs.</summary>
    public override string ToString()
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"/subscriptions/{Uri.EscapeDataString(SubscriptionId)}/resourceGroups/{Uri.EscapeDataString(ResourceGroupName)}/sandboxGroups/{Uri.EscapeDataString(SandboxGroupName)}");
    }
}
