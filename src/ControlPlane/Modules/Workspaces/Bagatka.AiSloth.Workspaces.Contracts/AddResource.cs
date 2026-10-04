namespace Bagatka.AiSloth.Workspaces.Contracts;

/// <summary>Input to <see cref="IWorkspacesApi.AddResourceAsync"/>.</summary>
/// <param name="Resource">The new resource, such as a nook.</param>
/// <param name="Parent">What it is in, such as the nook's workspace.</param>
public sealed record AddResource(Resource Resource, Resource Parent);
