using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Bagatka.AiSloth.ArchitectureTests;

/// <summary>
/// Rules about the types in every source project this test project references.
/// </summary>
public sealed class TypeRuleTests
{
    // The Aspire app host only composes the local environment; referencing it would pull Aspire into tests.
    private const string AppHost = "Bagatka.AiSloth.AppHost";

    private static readonly ProjectFile ThisProject = ProjectFile.Load(Path.Combine(
        RepositoryRoot.FullPath,
        "tests",
        "Bagatka.AiSloth.ArchitectureTests",
        "Bagatka.AiSloth.ArchitectureTests.csproj"));

    [Fact]
    public void Every_source_project_is_checked()
    {
        List<string> missing = ProjectFile.InSourceTree()
            .Select(project => project.Name)
            .Where(name => !string.Equals(name, AppHost, StringComparison.Ordinal))
            .Except(ThisProject.ProjectReferences, StringComparer.Ordinal)
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void No_type_declares_an_implicit_conversion()
    {
        List<string> offenders = CheckedTypes()
            .Where(type => type
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Any(method => string.Equals(method.Name, "op_Implicit", StringComparison.Ordinal)))
            .Select(type => type.FullName ?? type.Name)
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void No_class_derives_from_another_project_class()
    {
        // gRPC requires a service to derive from the base generated from its .proto file.
        List<string> offenders = CheckedTypes()
            .Where(type => type.IsClass && type.BaseType is not null && IsProjectAssembly(type.BaseType.Assembly) && !IsGrpcServiceBase(type.BaseType))
            .Select(type => (type.FullName ?? type.Name) + " : " + type.BaseType!.FullName)
            .ToList();

        Assert.Empty(offenders);
    }

    private static IEnumerable<Type> CheckedTypes()
    {
        return ThisProject.ProjectReferences
            .Select(name => Assembly.Load(new AssemblyName(name)))
            .SelectMany(assembly => assembly.GetTypes());
    }

    private static bool IsGrpcServiceBase(Type type)
    {
        return type.GetCustomAttributesData().Any(attribute => string.Equals(attribute.AttributeType.FullName, "Grpc.Core.BindServiceMethodAttribute", StringComparison.Ordinal));
    }

    private static bool IsProjectAssembly(Assembly assembly)
    {
        return assembly.GetName().Name?.StartsWith("Bagatka.", StringComparison.Ordinal) ?? false;
    }
}
