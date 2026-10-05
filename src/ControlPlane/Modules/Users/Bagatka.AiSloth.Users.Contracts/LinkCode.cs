using System;

namespace Bagatka.AiSloth.Users.Contracts;

/// <summary>A code that signs its creator in on another device, once, until it expires. Shown only here.</summary>
/// <param name="Code">The code.</param>
/// <param name="ExpiresAt">When it stops working.</param>
public sealed record LinkCode(string Code, DateTimeOffset ExpiresAt);
