using System;
using System.Collections.Generic;

namespace Bagatka.Sdk.Docker;

/// <summary>
/// A container as the engine reports it.
/// </summary>
/// <param name="Id">The container ID.</param>
/// <param name="Name">The container name, without the leading slash.</param>
/// <param name="Created">When it was created.</param>
/// <param name="ImageId">The ID of the image it was created from.</param>
/// <param name="Status">The engine's status: <c>created</c>, <c>running</c>, <c>paused</c>, <c>restarting</c>, <c>removing</c>, <c>exited</c>, or <c>dead</c>.</param>
/// <param name="ExitCode">The entry point's exit code, once it has exited.</param>
/// <param name="Error">The engine's error for a container that couldn't run, or empty.</param>
/// <param name="Environment">Its environment variables as <c>NAME=value</c>.</param>
/// <param name="Labels">Its labels.</param>
public sealed record ContainerDetails(
    string Id,
    string Name,
    DateTimeOffset Created,
    string ImageId,
    string Status,
    int ExitCode,
    string Error,
    IReadOnlyList<string> Environment,
    IReadOnlyDictionary<string, string> Labels);
