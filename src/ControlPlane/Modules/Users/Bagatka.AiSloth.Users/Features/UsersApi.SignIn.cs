using Bagatka.AiSloth.Users.Data;
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
    public async Task<Result<StartedSession>> SignInAsync(Actor actor, SignIn command, CancellationToken ct)
    {
        await using UsersDbContext db = await databases.CreateDbContextAsync(ct);

        // Only system code, the host's sign-in, starts sessions: it validated a provider's token or
        // checked that a newcomer may join. A code proves itself.
        if (actor is not SystemActor)
        {
            return new Result<StartedSession>(Error.Forbidden);
        }

        Result<BoundedName> device = BoundedName.Parse(command.Device, "device");
        if (device.Failed)
        {
            return new Result<StartedSession>(device.Error);
        }

        Result<SignedInPerson> person = await PersonAsync(db, command.Proof, ct);
        if (person.Failed)
        {
            return new Result<StartedSession>(person.Error);
        }

        // The session commits with what finding the person changed, such as using up a code.
        (Session session, string token) = Session.Start(person.Output.User.Id, device.Output, time);
        db.Sessions.Add(session);
        Result saved = await db.SaveAsync(ct);
        if (saved.Failed)
        {
            // A code used twice at the same moment: the other sign-in removed it first.
            return new Result<StartedSession>(command.Proof is SignInCode ? UsersErrors.CodeNotFound : saved.Error);
        }

        return new Result<StartedSession>(new StartedSession(session.Id, token, person.Output.User.ToSummary(), person.Output.IsNew));
    }

    // The person a provider vouches for, recorded with the name it gave on their first sign-in. A new
    // person is saved at once, so two first sign-ins at the same moment, such as a web app's parallel
    // requests, end with the same person: the unique index lets one win, and the other reads it.
    private async Task<Result<SignedInPerson>> PersonWithIdentityAsync(UsersDbContext db, VerifiedIdentity identity, CancellationToken ct)
    {
        Result<User> recorded = User.FromProvider(identity, time);
        if (recorded.Failed)
        {
            return new Result<SignedInPerson>(recorded.Error);
        }

        User? existing = await FindByIdentityAsync(db, identity, ct);
        if (existing is not null)
        {
            return new Result<SignedInPerson>(new SignedInPerson(existing, IsNew: false));
        }

        db.Users.Add(recorded.Output);
        Result saved = await db.SaveAsync(ct);
        if (saved.Failed)
        {
            bool otherSignInWon = saved.Error == ModuleDbContextExtensions.AlreadyExists;
            User? winner = null;
            if (otherSignInWon)
            {
                // The failed insert stays tracked; forget it, or saving the session would repeat it.
                db.ChangeTracker.Clear();
                winner = await FindByIdentityAsync(db, identity, ct);
            }

            return winner is null ? new Result<SignedInPerson>(saved.Error) : new Result<SignedInPerson>(new SignedInPerson(winner, IsNew: false));
        }

        return new Result<SignedInPerson>(new SignedInPerson(recorded.Output, IsNew: true));
    }

    private static async Task<User?> FindByIdentityAsync(UsersDbContext db, VerifiedIdentity identity, CancellationToken ct)
    {
        return await db.Users.AsNoTracking().SingleOrDefaultAsync(user => user.Issuer == identity.Issuer && user.Subject == identity.Subject, ct);
    }

    // The setup code makes its user the host's first person, while nobody has signed up; a link code
    // signs in its creator. The code is removed with the session, so it works once.
    private async Task<Result<SignedInPerson>> PersonWithCodeAsync(UsersDbContext db, SignInCode proof, CancellationToken ct)
    {
        byte[] hash = OneTimeCode.Hash(proof.Code ?? string.Empty);
        IssuedCode? code = await db.IssuedCodes.SingleOrDefaultAsync(found => found.CodeHash == hash, ct);
        DateTimeOffset now = time.GetUtcNow();
        if (code is null || !code.UsableAt(now))
        {
            return new Result<SignedInPerson>(UsersErrors.CodeNotFound);
        }

        db.IssuedCodes.Remove(code);
        if (code.Purpose == IssuedCodePurpose.Link)
        {
            User? creator = await db.Users.AsNoTracking().SingleOrDefaultAsync(found => found.Id == code.UserId, ct);
            return creator is null ? new Result<SignedInPerson>(UsersErrors.CodeNotFound) : new Result<SignedInPerson>(new SignedInPerson(creator, IsNew: false));
        }

        bool someoneSignedUp = await db.Users.AnyAsync(ct);
        if (someoneSignedUp)
        {
            return new Result<SignedInPerson>(UsersErrors.CodeNotFound);
        }

        Result<BoundedName> name = BoundedName.Parse(proof.Name, "name");
        if (name.Failed)
        {
            return new Result<SignedInPerson>(name.Error);
        }

        User first = User.Named(name.Output, time);
        db.Users.Add(first);
        return new Result<SignedInPerson>(new SignedInPerson(first, IsNew: true));
    }

    // The person the proof vouches for.
    private Task<Result<SignedInPerson>> PersonAsync(UsersDbContext db, SignInProof proof, CancellationToken ct)
    {
        return proof switch
        {
            VerifiedIdentity identity => PersonWithIdentityAsync(db, identity, ct),
            SignInCode code => PersonWithCodeAsync(db, code, ct),
            Newcomer newcomer => Task.FromResult(AddNewcomer(db, newcomer)),
        };
    }

    private Result<SignedInPerson> AddNewcomer(UsersDbContext db, Newcomer newcomer)
    {
        Result<BoundedName> name = BoundedName.Parse(newcomer.Name, "name");
        if (name.Failed)
        {
            return new Result<SignedInPerson>(name.Error);
        }

        User person = User.Named(name.Output, time);
        db.Users.Add(person);
        return new Result<SignedInPerson>(new SignedInPerson(person, IsNew: true));
    }

    private sealed record SignedInPerson(User User, bool IsNew);
}
