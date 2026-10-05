using System;

namespace Bagatka.AiSloth.Users.Contracts;

/// <summary>A signed-in device, without its token.</summary>
/// <param name="Id">The session.</param>
/// <param name="Device">What the device is, such as <c>sloth on alex-laptop</c>.</param>
/// <param name="StartedAt">When it signed in.</param>
/// <param name="LastUsedAt">When it was last used, to the day.</param>
public sealed record SessionSummary(SessionId Id, string Device, DateTimeOffset StartedAt, DateTimeOffset LastUsedAt);
