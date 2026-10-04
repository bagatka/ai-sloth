using System;
using System.IO;

namespace Bagatka.AiSloth.ArchitectureTests;

internal static class RepositoryRoot
{
    internal static string FullPath { get; } = Find();

    private static string Find()
    {
        DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "AiSloth.slnx")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new InvalidOperationException("AiSloth.slnx was not found above " + AppContext.BaseDirectory);
        }

        return directory.FullName;
    }
}
