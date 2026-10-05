using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Chats.Harness;
using Bagatka.AiSloth.Chats.Model;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Chats;

internal sealed partial class ChatsApi
{
    public async Task<Result<Instructions>> GetInstructionsAsync(Actor actor, WorkspaceId workspaceId, CancellationToken ct)
    {
        Result<UserId> person = await PersonInAsync(actor, workspaceId, AccessLevel.Read, ct);
        if (person.Failed)
        {
            return new Result<Instructions>(person.Error);
        }

        string? workspace = await db.WorkspaceInstructions.Where(found => found.WorkspaceId == workspaceId).Select(found => found.Text).SingleOrDefaultAsync(ct);
        string? personal = await db.PersonalInstructions.Where(found => found.PersonId == person.Output).Select(found => found.Text).SingleOrDefaultAsync(ct);
        return new Result<Instructions>(new Instructions(workspace ?? string.Empty, personal ?? string.Empty));
    }

    public async Task<Result> SetWorkspaceInstructionsAsync(Actor actor, WorkspaceId workspaceId, string text, CancellationToken ct)
    {
        Result<UserId> person = await PersonInAsync(actor, workspaceId, AccessLevel.Write, ct);
        if (person.Failed)
        {
            return new Result(person.Error);
        }

        Error? invalid = AgentInstructions.Check(text);
        if (invalid is not null)
        {
            return new Result(invalid);
        }

        WorkspaceInstructions? instructions = await db.WorkspaceInstructions.SingleOrDefaultAsync(found => found.WorkspaceId == workspaceId, ct);
        if (instructions is null)
        {
            db.WorkspaceInstructions.Add(WorkspaceInstructions.Write(workspaceId, text, person.Output, time));
        }
        else
        {
            instructions.Rewrite(text, person.Output, time);
        }

        return await db.SaveAsync(ct);
    }

    public async Task<Result> SetPersonalInstructionsAsync(Actor actor, string text, CancellationToken ct)
    {
        if (actor is not UserActor user)
        {
            return new Result(Error.Forbidden);
        }

        Error? invalid = AgentInstructions.Check(text);
        if (invalid is not null)
        {
            return new Result(invalid);
        }

        PersonalInstructions? instructions = await db.PersonalInstructions.SingleOrDefaultAsync(found => found.PersonId == user.UserId, ct);
        if (instructions is null)
        {
            db.PersonalInstructions.Add(PersonalInstructions.Write(user.UserId, text, time));
        }
        else
        {
            instructions.Rewrite(text, time);
        }

        return await db.SaveAsync(ct);
    }
}
