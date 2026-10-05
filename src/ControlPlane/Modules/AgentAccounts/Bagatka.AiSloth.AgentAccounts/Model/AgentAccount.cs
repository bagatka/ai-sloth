using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.AgentAccounts.Model;

// An account at an agent vendor: a workspace's, which its members use, or a person's own. The secret
// is kept only sealed: a key or token as given, or a signed-in plan's session (PlanSession) as JSON.
// What each kind is at its vendor, where its calls go and how they are paid for, is decided here.
internal sealed class AgentAccount
{
    public const int MaxSecretLength = 4096;

    private static readonly Uri AnthropicApi = new Uri("https://api.anthropic.com");
    private static readonly Uri OpenAIApi = new Uri("https://api.openai.com/v1");

    // Used by the factories and by EF: parameter names match property names.
    private AgentAccount(AgentAccountId id, AgentAccountKind kind, AgentAccountName name, WorkspaceId? workspaceId, UserId? ownerId, DateTimeOffset addedAt, ApiEndpoint? endpoint)
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

    public AgentAccountKind Kind { get; private set; }

    public AgentAccountName Name { get; private set; }

    // Exactly one is set: whose account it is.
    public WorkspaceId? WorkspaceId { get; private set; }

    public UserId? OwnerId { get; private set; }

    public DateTimeOffset AddedAt { get; private set; }

    // Another endpoint than the vendor's, for an API key.
    public ApiEndpoint? Endpoint { get; private set; }

    // Set when the vendor ended a signed-in plan's session; its tokens are gone.
    public DateTimeOffset? SignInEndedAt { get; private set; }

    public byte[] SealedSecret { get; private set; } = [];

    public bool NeedsSignIn => SignInEndedAt is not null;

    // An account added with its secret. Only API keys name an endpoint.
    public static Result<AgentAccount> Add(WorkspaceId? workspaceId, UserId? ownerId, AgentAccountKind kind, AgentAccountName name, string? secret, ApiEndpoint? endpoint, SecretBox box, TimeProvider time)
    {
        string trimmed = (secret ?? string.Empty).Trim();
        if (trimmed.Length is 0 or > MaxSecretLength)
        {
            string message = string.Create(CultureInfo.InvariantCulture, $"Must be 1 to {MaxSecretLength} characters.");
            return new Result<AgentAccount>(Error.Validation("secret", message));
        }

        bool isApiKey = kind is AgentAccountKind.AnthropicApiKey or AgentAccountKind.OpenAIApiKey;
        if (endpoint is not null && !isApiKey)
        {
            return new Result<AgentAccount>(Error.Validation("endpoint", "Only an API key names an endpoint."));
        }

        AgentAccount account = new AgentAccount(AgentAccountId.New(), kind, name, workspaceId, ownerId, time.GetUtcNow(), endpoint);
        account.SealedSecret = box.Seal(trimmed, account.Id.Value);
        return new Result<AgentAccount>(account);
    }

    // A person's plan, added by signing in.
    public static AgentAccount SignedIn(UserId ownerId, AgentAccountKind kind, AgentAccountName name, PlanSession session, SecretBox box, TimeProvider time)
    {
        AgentAccount account = new AgentAccount(AgentAccountId.New(), kind, name, workspaceId: null, ownerId, time.GetUtcNow(), endpoint: null);
        account.Seal(session, box);
        return account;
    }

    // Whether a signed-in plan's access token should be renewed before it is used.
    public bool RenewalDue(SecretBox box, DateTimeOffset now)
    {
        return Kind == AgentAccountKind.ChatGptPlan && !NeedsSignIn && OpenSession(box).RenewalDue(now);
    }

    // The plan's session, while it lasts: for renewing it, and for ending it at the vendor on removal.
    public PlanSession? Session(SecretBox box)
    {
        if (Kind != AgentAccountKind.ChatGptPlan || NeedsSignIn)
        {
            return null;
        }

        return OpenSession(box);
    }

    public void Renewed(PlanSession session, SecretBox box)
    {
        Seal(session, box);
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

    // What running an agent on the account takes. Model APIs are reached through the model gateway,
    // which adds these headers; plan tokens tied to one harness go to it.
    public AgentAccountCredential ToCredential(SecretBox box, Uri chatGptApi)
    {
        AgentAccountAccess access = Kind switch
        {
            AgentAccountKind.AnthropicApiKey => Through(Endpoint?.Value ?? AnthropicApi, "x-api-key", OpenSecret(box)),
            AgentAccountKind.OpenAIApiKey => Through(Endpoint?.Value ?? OpenAIApi, "Authorization", "Bearer " + OpenSecret(box)),
            AgentAccountKind.ChatGptPlan => Through(chatGptApi, "Authorization", "Bearer " + OpenSession(box).AccessToken),
            AgentAccountKind.GitHubCopilotToken or AgentAccountKind.ClaudeSubscription => new AgentAccountAccess(new HarnessToken(OpenSecret(box))),
        };
        return new AgentAccountCredential(Id, Kind, OwnerId, access);
    }

    private static AgentAccountAccess Through(Uri url, string header, string value)
    {
        Dictionary<string, string> headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [header] = value };
        return new AgentAccountAccess(new ModelEndpoint(url, headers));
    }

    private string OpenSecret(SecretBox box)
    {
        return box.Open(SealedSecret, Id.Value);
    }

    private PlanSession OpenSession(SecretBox box)
    {
        PlanSession? session = JsonSerializer.Deserialize<PlanSession>(OpenSecret(box), FoundationJson.Options);
        if (session is null)
        {
            throw new InvalidOperationException("Agent account " + Id.Value + " has no session.");
        }

        return session;
    }

    private void Seal(PlanSession session, SecretBox box)
    {
        SealedSecret = box.Seal(JsonSerializer.Serialize(session, FoundationJson.Options), Id.Value);
    }
}
