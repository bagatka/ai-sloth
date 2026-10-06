using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Aspire.Hosting;
using Aspire.Hosting.Azure;
using Aspire.Hosting.Pipelines;
using Azure;
using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.Authorization;
using Azure.ResourceManager.Authorization.Models;
using Azure.ResourceManager.Resources;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable ASPIREPIPELINES001 // Deployment steps are experimental in Aspire 13.6.

namespace Bagatka.AiSloth.AppHost;

// The sandbox group a deployment's nooks run in: made in the deployment's resource group, at its
// location, with the WebApi's identity allowed to use it. Sandbox groups fail Azure Resource Manager's
// validation inside templates, though creating one directly works, so deploying makes it in a step of
// its own. The group never gets an identity: code in its sandboxes could use it.
internal static class NookSandboxGroup
{
    public const string Name = "nooks";

    private static readonly ResourceType SandboxGroups = new ResourceType("Microsoft.App/sandboxGroups");

    // Container Apps SandboxGroup Data Owner.
    private const string DataOwner = "c24cf47c-5077-412d-a19c-45202126392c";

    // Deploying makes the group once the identity is provisioned; making it again changes nothing.
    public static void MakeWhenDeploying(IDistributedApplicationBuilder builder, AzureUserAssignedIdentityResource identity)
    {
        PipelineStep make = new PipelineStep
        {
            Name = "make-nook-sandbox-group",
            Description = "Makes the sandbox group nooks run in.",
            Action = context => MakeAsync(context, identity),
            RequiredBySteps = [WellKnownPipelineSteps.Deploy],
        };
        builder.Pipeline.AddStep(make);
        builder.Pipeline.AddPipelineConfiguration(context =>
        {
            foreach (PipelineStep provision in context.GetSteps(identity, WellKnownPipelineTags.ProvisionInfrastructure))
            {
                make.DependsOn(provision);
            }

            return Task.CompletedTask;
        });
    }

    private static async Task MakeAsync(PipelineStepContext context, AzureUserAssignedIdentityResource identity)
    {
        CancellationToken ct = context.CancellationToken;
        ResourceIdentifier identityId = new ResourceIdentifier((string)identity.Outputs["id"]!);
        string principalId = (string)identity.Outputs["principalId"]!;
        ArmClientOptions options = new ArmClientOptions();
        options.SetApiVersion(SandboxGroups, "2026-07-01");
        ArmClient arm = new ArmClient(context.Services.GetRequiredService<ITokenCredentialProvider>().TokenCredential, identityId.SubscriptionId, options);

        Response<ResourceGroupResource> resourceGroup = await arm.GetResourceGroupResource(identityId.Parent!).GetAsync(ct);
        ResourceIdentifier groupId = resourceGroup.Value.Id.AppendProviderResource("Microsoft.App", "sandboxGroups", Name);
        await arm.GetGenericResources().CreateOrUpdateAsync(WaitUntil.Completed, groupId, new GenericResourceData(resourceGroup.Value.Data.Location), ct);

        ResourceIdentifier role = new ResourceIdentifier("/subscriptions/" + identityId.SubscriptionId + "/providers/Microsoft.Authorization/roleDefinitions/" + DataOwner);
        RoleAssignmentCreateOrUpdateContent assignment = new RoleAssignmentCreateOrUpdateContent(role, Guid.Parse(principalId))
        {
            PrincipalType = RoleManagementPrincipalType.ServicePrincipal,
        };
        await arm.GetRoleAssignments(groupId).CreateOrUpdateAsync(WaitUntil.Completed, AssignmentName(groupId, principalId), assignment, ct);
    }

    // The same group, identity, and role always make the same name, so assigning again changes nothing.
    private static string AssignmentName(ResourceIdentifier groupId, string principalId)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(groupId + "|" + principalId + "|" + DataOwner));
        return new Guid(hash.AsSpan(0, 16)).ToString();
    }
}
