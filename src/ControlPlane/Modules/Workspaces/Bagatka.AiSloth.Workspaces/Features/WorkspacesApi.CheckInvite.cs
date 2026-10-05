using System;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.AiSloth.Workspaces.Model;
using Bagatka.Foundation;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Workspaces;

internal sealed partial class WorkspacesApi
{
    public async Task<Result<Resource>> CheckInviteAsync(Actor actor, AcceptInvite command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        byte[] codeHash = OneTimeCode.Hash(command.Code ?? string.Empty);
        StoredInvite? invite = await db.Invites.AsNoTracking().SingleOrDefaultAsync(found => found.CodeHash == codeHash, ct);
        bool usable = invite is not null && invite.UsableAt(time.GetUtcNow());
        if (invite is null || !usable)
        {
            return new Result<Resource>(WorkspacesErrors.InviteNotFound);
        }

        return new Result<Resource>(invite.Resource);
    }
}
