using System;
using System.Collections.Generic;

namespace Bagatka.AiSloth.Sources.Contracts;

/// <summary>
/// The repositories a person's GitHub connection reaches: those of the accounts and organizations the
/// host's GitHub App is installed on that the person may see.
/// </summary>
/// <param name="Repositories">Their names, such as <c>acme/api</c>, and whether each is private.</param>
/// <param name="InstallUrl">Where the person installs the app on more accounts or repositories.</param>
public sealed record AvailableRepositories(IReadOnlyList<AvailableRepository> Repositories, Uri InstallUrl);
