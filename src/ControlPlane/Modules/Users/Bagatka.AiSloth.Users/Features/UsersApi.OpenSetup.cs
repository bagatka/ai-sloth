using Bagatka.AiSloth.Users.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Users.Model;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Users;

internal sealed partial class UsersApi
{
    public async Task<string?> OpenSetupAsync(Actor actor, CancellationToken ct)
    {
        await using UsersDbContext db = await databases.CreateDbContextAsync(ct);

        if (actor is not SystemActor)
        {
            return null;
        }

        bool someoneSignedUp = await db.Users.AnyAsync(ct);
        if (someoneSignedUp)
        {
            return null;
        }

        // Each start replaces the last code: only the newest one, shown by the last start, works.
        await db.IssuedCodes.Where(code => code.Purpose == IssuedCodePurpose.Setup).ExecuteDeleteAsync(ct);
        (IssuedCode code, string text) = IssuedCode.Setup(time);
        db.IssuedCodes.Add(code);
        Result saved = await db.SaveAsync(ct);
        if (saved.Failed)
        {
            throw new System.InvalidOperationException("Saving the setup code failed: " + saved.Error.Message);
        }

        return text;
    }
}
