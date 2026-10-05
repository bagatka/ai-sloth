using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Users.Contracts;
using Bagatka.AiSloth.Users.Model;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Users;

internal sealed partial class UsersApi
{
    public async Task<Result<LinkCode>> CreateLinkCodeAsync(Actor actor, CancellationToken ct)
    {
        if (actor is not UserActor user)
        {
            return new Result<LinkCode>(Error.Unauthorized);
        }

        // Maintenance, without rules: the person's link codes that can no longer work.
        DateTimeOffset now = time.GetUtcNow();
        await db.IssuedCodes.Where(code => code.UserId == user.UserId && code.ExpiresAt <= now).ExecuteDeleteAsync(ct);

        (IssuedCode code, string text) = IssuedCode.Link(user.UserId, time);
        db.IssuedCodes.Add(code);
        Result saved = await db.SaveAsync(ct);
        if (saved.Failed)
        {
            return new Result<LinkCode>(saved.Error);
        }

        return new Result<LinkCode>(new LinkCode(text, code.ExpiresAt));
    }
}
