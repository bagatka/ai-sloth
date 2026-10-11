using System;
using System.Globalization;
using System.Text.Json;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;
using Bagatka.ModelAccess;

namespace Bagatka.AiSloth.AgentAccounts.Model;

// An account at an agent vendor: a workspace's, which its members use, or a person's own. Its
// credential is kept only sealed. What its kind means at its vendor is its kind's (Kinds/).
internal sealed class AgentAccount
{
    public const int MaxSecretLength = 4096;

    // Used by the factories and by EF: parameter names match property names.
    private AgentAccount(AgentAccountId id, ModelAccessKind kind, AgentAccountName name, WorkspaceId? workspaceId, UserId? ownerId, DateTimeOffset addedAt, ApiEndpoint? endpoint)
    {
        Id = id;
        Kind = kind;
        Name = name;
        WorkspaceId = workspaceId;
        OwnerId = ownerId;
        AddedAt = addedAt;
        Endpoint = endpoint;
    }

    public AgentAccountId Id { get; private set; }

    public ModelAccessKind Kind { get; private set; }

    public AgentAccountName Name { get; private set; }

    // Exactly one is set: whose account it is.
    public WorkspaceId? WorkspaceId { get; private set; }

    public UserId? OwnerId { get; private set; }

    public DateTimeOffset AddedAt { get; private set; }

    // Another endpoint than the vendor's, for an API key whose kind takes one.
    public ApiEndpoint? Endpoint { get; private set; }

    // Set when the vendor ended a plan's sign-in; its credential is gone.
    public DateTimeOffset? SignInEndedAt { get; private set; }

    public byte[] SealedSecret { get; private set; } = [];

    public bool NeedsSignIn => SignInEndedAt is not null;

    // An API key, added with its secret.
    public static Result<AgentAccount> Add(WorkspaceId? workspaceId, UserId? ownerId, ModelAccessKind kind, AgentAccountName name, string? secret, ApiEndpoint? endpoint, SecretBox box, TimeProvider time)
    {
        string trimmed = (secret ?? string.Empty).Trim();
        if (trimmed.Length is 0 or > MaxSecretLength)
        {
            string message = string.Create(CultureInfo.InvariantCulture, $"Must be 1 to {MaxSecretLength} characters.");
            return new Result<AgentAccount>(Error.Validation("secret", message));
        }

        AgentAccount account = new AgentAccount(AgentAccountId.New(), kind, name, workspaceId, ownerId, time.GetUtcNow(), endpoint);
        account.Seal(new ModelCredential(trimmed), box);
        return new Result<AgentAccount>(account);
    }

    // A person's plan, added by signing in.
    public static AgentAccount SignedIn(UserId ownerId, ModelAccessKind kind, AgentAccountName name, ModelCredential credential, SecretBox box, TimeProvider time)
    {
        AgentAccount account = new AgentAccount(AgentAccountId.New(), kind, name, workspaceId: null, ownerId, time.GetUtcNow(), endpoint: null);
        account.Seal(credential, box);
        return account;
    }

    public ModelCredential OpenCredential(SecretBox box)
    {
        if (NeedsSignIn)
        {
            throw new InvalidOperationException("Agent account " + Id.Value + " has no credential: its sign-in ended.");
        }

        ModelCredential? credential = JsonSerializer.Deserialize<ModelCredential>(box.Open(SealedSecret, Id.Value), FoundationJson.Options);
        if (credential is null)
        {
            throw new InvalidOperationException("Agent account " + Id.Value + " has no credential.");
        }

        return credential;
    }

    // Whether its credential should be renewed before it is used.
    public bool RenewalDue(SecretBox box, DateTimeOffset now)
    {
        return !NeedsSignIn && OpenCredential(box).RenewalDue(now);
    }

    public void Renewed(ModelCredential credential, SecretBox box)
    {
        Seal(credential, box);
    }

    public void SignInEnded(TimeProvider time)
    {
        SignInEndedAt = time.GetUtcNow();
        SealedSecret = [];
    }

    public AgentAccountSummary ToSummary()
    {
        return new AgentAccountSummary(Id, Kind, Name.Value, WorkspaceId, OwnerId, AddedAt, Endpoint?.Value, NeedsSignIn);
    }

    private void Seal(ModelCredential credential, SecretBox box)
    {
        SealedSecret = box.Seal(JsonSerializer.Serialize(credential, FoundationJson.Options), Id.Value);
    }
}
