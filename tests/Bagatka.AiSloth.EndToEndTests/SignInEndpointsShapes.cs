using System;
using Bagatka.AiSloth.Users.Contracts;
using Bagatka.AiSloth.Workspaces.Contracts;

namespace Bagatka.AiSloth.EndToEndTests;

/// <summary>The sign-in endpoints' answers, as a client reads them.</summary>
internal static class SignInEndpointsShapes
{
    public sealed record SignedIn(string Token, SessionId Session, UserSummary User, WorkspaceId? Workspace);

    public sealed record HostDiscovery(string Name, int ApiVersion, SignInMethods SignIn);

    public sealed record SignInMethods(string? Provider, bool InviteSignUp);

    public sealed record LinkCode(string Code, DateTimeOffset ExpiresAt);
}
