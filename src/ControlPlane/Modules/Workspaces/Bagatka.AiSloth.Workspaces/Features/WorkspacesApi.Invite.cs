using Bagatka.AiSloth.Workspaces.Data;
using System;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.AiSloth.Workspaces.Model;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;

namespace Bagatka.AiSloth.Workspaces;

internal sealed partial class WorkspacesApi
{
    public async Task<Result<Invite>> InviteAsync(Actor actor, CreateInvite command, CancellationToken ct)
    {
        await using WorkspacesDbContext db = await databases.CreateDbContextAsync(ct);

        ArgumentNullException.ThrowIfNull(command);
        if (actor is not UserActor user)
        {
            return new Result<Invite>(Error.Unauthorized);
        }

        if (!Enum.IsDefined(command.Access))
        {
            return new Result<Invite>(Error.Validation("access", "Must be Read, Write, or Manage."));
        }

        Error? refused = await RefusalAsync(actor, command.Resource, AccessLevel.Manage, ct);
        if (refused is not null)
        {
            return new Result<Invite>(refused);
        }

        (StoredInvite invite, string code) = StoredInvite.Issue(command.Resource, command.Access, user.UserId, time);
        db.Invites.Add(invite);
        Result saved = await db.SaveAsync(ct);
        if (saved.Failed)
        {
            return new Result<Invite>(saved.Error);
        }

        return new Result<Invite>(new Invite(code, invite.Resource, invite.Access, invite.ExpiresAt));
    }
}
