using Bagatka.AiSloth.Users.Data;
using System;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Users.Model;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Users;

internal sealed partial class UsersApi
{
    public async Task<UserId?> AuthenticateAsync(string token, CancellationToken ct)
    {
        if (!token.StartsWith(Session.TokenPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        await using UsersDbContext db = await databases.CreateDbContextAsync(ct);

        byte[] hash = Session.HashToken(token);
        Session? session = await db.Sessions.SingleOrDefaultAsync(found => found.TokenHash == hash, ct);
        DateTimeOffset now = time.GetUtcNow();
        if (session is null || session.ExpiredAt(now))
        {
            return null;
        }

        // Not handled: the session ending between this read and the save, which then answers for it
        // once more.
        bool used = session.Use(now);
        if (used)
        {
            _ = await db.SaveAsync(ct);
        }

        return session.UserId;
    }
}
