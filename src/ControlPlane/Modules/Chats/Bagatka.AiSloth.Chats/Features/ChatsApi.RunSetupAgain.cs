using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Chats.Data;
using Bagatka.AiSloth.Chats.Model;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Chats;

internal sealed partial class ChatsApi
{
    public async Task<Result> RunSetupAgainAsync(Actor actor, ChatId id, CancellationToken ct)
    {
        await using ChatsDbContext db = await databases.CreateDbContextAsync(ct);

        Result<Chat> found = await FindChatAsync(db, actor, id, AccessLevel.Write, ct);
        if (found.Failed)
        {
            return new Result(found.Error);
        }

        if (actor is not UserActor user)
        {
            return new Result(Error.Forbidden);
        }

        Result<Chat> asked = await ChangeChatAsync(db, id, chat => chat.AskSetupRun(), new ChatEventBody(new SetupRunAsked(user.UserId)), ct);
        if (asked.Failed)
        {
            return new Result(asked.Error);
        }

        runners.Wake(id);
        signals.Notify(id);
        return new Result(new Success());
    }
}
