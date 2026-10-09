using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Bagatka.PostHog;

/// <summary>
/// One event to capture: what happened, to whom, with its properties and groups.
/// </summary>
/// <example>
/// <code>
/// client.Capture(new PostHogEvent("chat_started", userId)
/// {
///     Properties = { ["harness"] = "codex", ["from_ready_copy"] = true },
///     Groups = { ["workspace"] = workspaceId },
/// });
/// </code>
/// </example>
public sealed class PostHogEvent
{
    /// <summary>Creates an event.</summary>
    /// <param name="name">Its name, such as <c>chat_started</c>.</param>
    /// <param name="distinctId">Whom it happened to: a person's ID, up to 200 characters.</param>
    public PostHogEvent(string name, string distinctId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(distinctId);
        Name = name;
        DistinctId = distinctId;
    }

    /// <summary>Its name.</summary>
    public string Name { get; }

    /// <summary>Whom it happened to.</summary>
    public string DistinctId { get; }

    /// <summary>
    /// Its properties, PostHog's own (named with <c>$</c>) among them, such as
    /// <c>$process_person_profile</c> set to false for an event that belongs to no person.
    /// </summary>
    public JsonObject Properties { get; } = new JsonObject();

    /// <summary>The groups it belongs to, by group type, such as a workspace by its ID.</summary>
    public IDictionary<string, string> Groups { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>When it happened: when it was captured unless given.</summary>
    public DateTimeOffset? Timestamp { get; init; }
}
