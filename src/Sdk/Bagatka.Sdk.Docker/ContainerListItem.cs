using System;
using System.Collections.Generic;

namespace Bagatka.Sdk.Docker;

/// <summary>
/// A container as the engine lists it.
/// </summary>
/// <param name="Id">The container ID.</param>
/// <param name="Labels">Its labels.</param>
/// <param name="State">The engine's status, as in <see cref="ContainerDetails.Status"/>.</param>
/// <param name="Status">The engine's human-readable status, such as <c>Exited (1) 3 minutes ago</c>.</param>
/// <param name="Created">When it was created.</param>
public sealed record ContainerListItem(
    string Id,
    IReadOnlyDictionary<string, string> Labels,
    string State,
    string Status,
    DateTimeOffset Created);
