using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Bagatka.AiSloth.ArchitectureTests;

public sealed class ProjectReferenceTests
{
    [Fact]
    public void General_purpose_projects_never_reference_product_projects()
    {
        List<string> offenders = ProjectFile.InSourceTree()
            .Where(project => project.IsGeneralPurpose)
            .SelectMany(project => project.ProjectReferences
                .Where(reference => reference.StartsWith("Bagatka.AiSloth.", StringComparison.Ordinal))
                .Select(reference => project.Name + " -> " + reference))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Foundation_depends_only_on_the_base_class_library()
    {
        ProjectFile foundation = Assert.Single(
            ProjectFile.InSourceTree(),
            project => string.Equals(project.Name, "Bagatka.Foundation", StringComparison.Ordinal));

        Assert.Empty(foundation.ProjectReferences);
        Assert.Empty(foundation.PackageReferences);
        Assert.Empty(foundation.FrameworkReferences);
    }
}
