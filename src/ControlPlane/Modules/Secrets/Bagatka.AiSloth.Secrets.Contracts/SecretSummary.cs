using System;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Secrets.Contracts;

/// <summary>A secret as people see it: never its value.</summary>
/// <param name="Id">The secret; replacing its value keeps it.</param>
/// <param name="WorkspaceId">The workspace whose nooks get it.</param>
/// <param name="Name">The environment variable's name.</param>
/// <param name="SetBy">Who set its value last.</param>
/// <param name="SetAt">When its value was set last.</param>
public sealed record SecretSummary(SecretId Id, WorkspaceId WorkspaceId, string Name, UserId SetBy, DateTimeOffset SetAt);
