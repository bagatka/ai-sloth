using System;
using System.Linq;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.AiSloth.Workspaces.Data;
using Bagatka.AiSloth.Workspaces.Model;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Workspaces;

// The front door: dependencies only. Each feature is a file in Features/.
internal sealed partial class WorkspacesApi(WorkspacesDbContext db, TimeProvider time) : IWorkspacesApi
{
    // The workspaces a user is a member of, with their role in each, for features to compose further.
    private IQueryable<Membership> MembershipsOf(UserId userId)
    {
        return db.Members
            .Where(member => member.UserId == userId)
            .Join(
                db.Workspaces,
                member => member.WorkspaceId,
                workspace => workspace.Id,
                (member, workspace) => new Membership { Id = workspace.Id, Name = workspace.Name, Role = member.Role });
    }

    // A member initializer, unlike a record constructor, lets EF keep composing the query.
    private sealed class Membership
    {
        public required WorkspaceId Id { get; init; }

        public required WorkspaceName Name { get; init; }

        public required WorkspaceRole Role { get; init; }
    }
}
