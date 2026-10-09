using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.AiSloth.Workspaces.Data;
using Bagatka.AiSloth.Workspaces.Model;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Workspaces;

internal sealed partial class WorkspacesApi
{
    public async Task<Result<Resource>> AcceptInviteAsync(Actor actor, AcceptInvite command, CancellationToken ct)
    {
        await using WorkspacesDbContext db = await databases.CreateDbContextAsync(ct);

        ArgumentNullException.ThrowIfNull(command);
        if (actor is not UserActor user)
        {
            return new Result<Resource>(Error.Unauthorized);
        }

        byte[] codeHash = OneTimeCode.Hash(command.Code ?? string.Empty);
        StoredInvite? invite = await db.Invites.SingleOrDefaultAsync(found => found.CodeHash == codeHash, ct);
        bool usable = invite is not null && invite.UsableAt(time.GetUtcNow());
        if (invite is null || !usable)
        {
            return new Result<Resource>(WorkspacesErrors.InviteNotFound);
        }

        Grant? grant = await db.Grants.SingleOrDefaultAsync(found => found.ResourceId == invite.ResourceId && found.UserId == user.UserId, ct);
        if (grant is null)
        {
            db.Grants.Add(Grant.Give(invite.Resource, user.UserId, invite.Access));
        }
        else
        {
            grant.Raise(invite.Access);
        }

        invite.Accept(user.UserId, time);
        Result saved = await db.SaveAsync(ct);
        if (saved.Failed)
        {
            return new Result<Resource>(saved.Error);
        }

        Guid? workspace = invite.Resource.Kind == ResourceKind.Workspace ? invite.Resource.Id : null;
        productEvents.Capture(new ProductEvent("invite_accepted", user.UserId, workspace, new Dictionary<string, ProductFact>(StringComparer.Ordinal)
        {
            ["access"] = new ProductFact(invite.Access.ToString()),
            ["resource"] = new ProductFact(invite.Resource.Kind.ToString()),
        }));

        return new Result<Resource>(invite.Resource);
    }
}
