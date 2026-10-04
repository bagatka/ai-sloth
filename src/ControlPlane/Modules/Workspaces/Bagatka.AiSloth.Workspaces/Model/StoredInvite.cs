using System;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Workspaces.Model;

// A one-time invite. Its code is kept only as a hash; accepting it uses it up.
internal sealed class StoredInvite
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);

    // Used by Issue and by EF: parameter names match property names.
    private StoredInvite(Guid id, byte[] codeHash, ResourceKind resourceKind, Guid resourceId, AccessLevel access, UserId createdBy, DateTimeOffset expiresAt)
    {
        Id = id;
        CodeHash = codeHash;
        ResourceKind = resourceKind;
        ResourceId = resourceId;
        Access = access;
        CreatedBy = createdBy;
        ExpiresAt = expiresAt;
    }

    public Guid Id { get; private set; }

    public byte[] CodeHash { get; private set; }

    public ResourceKind ResourceKind { get; private set; }

    public Guid ResourceId { get; private set; }

    public AccessLevel Access { get; private set; }

    public UserId CreatedBy { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public UserId? AcceptedBy { get; private set; }

    public DateTimeOffset? AcceptedAt { get; private set; }

    // PostgreSQL's xmin: two people accepting at once conflict instead of both getting in.
    public uint Version { get; private set; }

    public Resource Resource => new Resource(ResourceKind, ResourceId);

    public static (StoredInvite Invite, string Code) Issue(Resource resource, AccessLevel access, UserId createdBy, TimeProvider time)
    {
        string code = OneTimeCode.Create();
        StoredInvite invite = new StoredInvite(Guid.CreateVersion7(), OneTimeCode.Hash(code), resource.Kind, resource.Id, access, createdBy, time.GetUtcNow() + Lifetime);
        return (invite, code);
    }

    public bool UsableAt(DateTimeOffset now)
    {
        return AcceptedBy is null && now < ExpiresAt;
    }

    public void Accept(UserId userId, TimeProvider time)
    {
        AcceptedBy = userId;
        AcceptedAt = time.GetUtcNow();
    }
}
