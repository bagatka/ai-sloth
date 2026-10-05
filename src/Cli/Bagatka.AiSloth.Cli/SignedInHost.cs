using System;

namespace Bagatka.AiSloth.Cli;

// A host sloth is signed in to, named by its address's authority (sloth.example.com, localhost:5170).
// The session's token is a secret, kept in the hosts file only. The workspace is the one commands use.
internal sealed record SignedInHost(
    string Name,
    Uri Url,
    string Token,
    Guid Session,
    Guid UserId,
    string UserName,
    Guid? Workspace,
    string? WorkspaceName,
    ChatDefaults? Defaults);
