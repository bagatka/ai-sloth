using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Bagatka.AiSloth.ArchitectureTests;

/// <summary>The references a .csproj declares, read from its XML.</summary>
internal sealed record ProjectFile(
    string Name,
    IReadOnlyList<string> ProjectReferences,
    IReadOnlyList<string> PackageReferences,
    IReadOnlyList<string> FrameworkReferences)
{
    /// <summary><c>Bagatka.*</c> without the product segment: never references product code.</summary>
    public bool IsGeneralPurpose =>
        Name.StartsWith("Bagatka.", StringComparison.Ordinal)
        && !Name.StartsWith("Bagatka.AiSloth.", StringComparison.Ordinal);

    public static IReadOnlyList<ProjectFile> InSourceTree()
    {
        return Directory
            .EnumerateFiles(Path.Combine(RepositoryRoot.FullPath, "src"), "*.csproj", SearchOption.AllDirectories)
            .Select(Load)
            .ToList();
    }

    public static ProjectFile Load(string path)
    {
        XDocument document = XDocument.Load(path);
        return new ProjectFile(
            Path.GetFileNameWithoutExtension(path),
            Includes(document, "ProjectReference").Select(ProjectName).ToList(),
            Includes(document, "PackageReference").ToList(),
            Includes(document, "FrameworkReference").ToList());
    }

    private static IEnumerable<string> Includes(XDocument document, string itemName)
    {
        return document
            .Descendants(itemName)
            .Select(item => (string?)item.Attribute("Include"))
            .OfType<string>();
    }

    // MSBuild item paths conventionally use '\' on every OS.
    private static string ProjectName(string include)
    {
        return Path.GetFileNameWithoutExtension(include.Replace('\\', Path.DirectorySeparatorChar));
    }
}
