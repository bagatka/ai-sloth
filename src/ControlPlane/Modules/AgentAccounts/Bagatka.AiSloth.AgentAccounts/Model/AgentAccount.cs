using System;
using System.Globalization;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.AgentAccounts.Model;

// An account at an agent vendor: a workspace's, which its members use, or a person's own. The
// secret is kept only sealed.
internal sealed class AgentAccount
{
    public const int MaxSecretLength = 4096;

    // Used by the factories and by EF: parameter names match property names.
    private AgentAccount(AgentAccountId id, AgentAccountKind kind, AgentAccountName name, WorkspaceId? workspaceId, UserId? ownerId, DateTimeOffset addedAt)
    {
        Id = id;
        Kind = kind;
        Name = name;
        WorkspaceId = workspaceId;
        OwnerId = ownerId;
        AddedAt = addedAt;
    }

    public AgentAccountId Id { get; private set; }

    public AgentAccountKind Kind { get; private set; }

    public AgentAccountName Name { get; private set; }

    // Exactly one is set: whose account it is.
    public WorkspaceId? WorkspaceId { get; private set; }

    public UserId? OwnerId { get; private set; }

    public DateTimeOffset AddedAt { get; private set; }

    public byte[] SealedSecret { get; private set; } = [];

    public static Result<AgentAccount> Add(WorkspaceId? workspaceId, UserId? ownerId, AgentAccountKind kind, AgentAccountName name, string? secret, SecretBox box, TimeProvider time)
    {
        string trimmed = (secret ?? string.Empty).Trim();
        if (trimmed.Length is 0 or > MaxSecretLength)
        {
            string message = string.Create(CultureInfo.InvariantCulture, $"Must be 1 to {MaxSecretLength} characters.");
            return new Result<AgentAccount>(Error.Validation("secret", message));
        }

        AgentAccount account = new AgentAccount(AgentAccountId.New(), kind, name, workspaceId, ownerId, time.GetUtcNow());
        account.SealedSecret = box.Seal(trimmed, account.Id);
        return new Result<AgentAccount>(account);
    }

    public AgentAccountSummary ToSummary()
    {
        return new AgentAccountSummary(Id, Kind, Name.Value, WorkspaceId, OwnerId, AddedAt);
    }

    public AgentAccountCredential ToCredential(SecretBox box)
    {
        return new AgentAccountCredential(Id, Kind, OwnerId, box.Open(SealedSecret, Id));
    }
}
