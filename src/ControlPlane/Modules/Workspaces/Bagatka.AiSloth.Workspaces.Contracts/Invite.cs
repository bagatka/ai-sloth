using System;

namespace Bagatka.AiSloth.Workspaces.Contracts;

/// <summary>
/// A new invite: whoever accepts <paramref name="Code"/> first gets the access, once, until it expires.
/// The code is shown only here.
/// </summary>
/// <param name="Code">The one-time code to pass on.</param>
/// <param name="Resource">What it gives access to.</param>
/// <param name="Access">How much.</param>
/// <param name="ExpiresAt">When it stops working.</param>
public sealed record Invite(string Code, Resource Resource, AccessLevel Access, DateTimeOffset ExpiresAt);
