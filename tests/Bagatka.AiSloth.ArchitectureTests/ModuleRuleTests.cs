using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bagatka.AiSloth.ArchitectureTests;

/// <summary>
/// The module rules in <c>ARCHITECTURE.md</c> ("Dependency rules", "Module"): what each kind of project
/// may reference, what a module exposes, what a contract holds, and which modules ask which.
/// </summary>
public sealed class ModuleRuleTests
{
    private const string Product = "Bagatka.AiSloth.";
    private const string ContractsSuffix = ".Contracts";

    // General-purpose projects a module's implementation may use; everything product-specific it uses
    // is another module's contract.
    private static readonly string[] ModuleMayUse =
    [
        "Bagatka.Foundation", "Bagatka.Foundation.Modules", "Bagatka.Sandboxing", "Bagatka.Harnesses", "Bagatka.ObjectStorage",
    ];

    // Machines' contract relays the remote provider's call messages unchanged (ARCHITECTURE.md, "Dependency rules").
    private static readonly (string Project, string Reference) KnownException = ("Bagatka.AiSloth.Machines.Contracts", "Bagatka.Sandboxing.Remote");

    [Fact]
    public void Modules_and_contracts_reference_only_what_the_dependency_rules_allow()
    {
        List<string> offenders = [];
        foreach (ProjectFile project in ProjectFile.InSourceTree().Where(IsModuleOrContracts))
        {
            foreach (string reference in project.ProjectReferences.Where(reference => !Allowed(project, reference)))
            {
                offenders.Add(project.Name + " -> " + reference);
            }

            foreach (string reference in project.FrameworkReferences.Concat(project.PackageReferences).Where(reference => reference.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal)))
            {
                offenders.Add(project.Name + " -> " + reference);
            }
        }

        Assert.Empty(offenders);
    }

    [Fact]
    public void A_module_exposes_only_its_registration_and_settings()
    {
        List<string> offenders = ModuleProjects()
            .Select(project => Assembly.Load(new AssemblyName(project.Name)))
            .SelectMany(assembly => assembly.GetExportedTypes())
            .Where(type => !type.Name.EndsWith("Module", StringComparison.Ordinal) && !type.Name.EndsWith("Settings", StringComparison.Ordinal) && !IsMigration(type))
            .Select(type => type.FullName ?? type.Name)
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Contracts_hold_plain_data()
    {
        List<string> offenders = ProjectFile.InSourceTree()
            .Where(project => IsContracts(project.Name))
            .Select(project => Assembly.Load(new AssemblyName(project.Name)))
            .SelectMany(assembly => assembly.GetExportedTypes())
            .Where(type => !(type.IsInterface || type.IsEnum || type.IsValueType || IsRecord(type) || IsStatic(type)))
            .Select(type => type.FullName ?? type.Name)
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void No_two_modules_ask_each_other()
    {
        Dictionary<string, HashSet<string>> asks = ModuleProjects().ToDictionary(project => ModuleOf(project.Name), AsksOf, StringComparer.Ordinal);
        List<string> cycles = asks.Keys.Where(module => Reaches(asks, module, module, new HashSet<string>(StringComparer.Ordinal))).ToList();

        Assert.Empty(cycles);
    }

    [Fact]
    public void A_module_api_holds_no_state_of_its_own()
    {
        // Its constructor's dependencies are captured as compiler-named fields, `<name>P`.
        List<string> offenders = ModuleProjects()
            .Select(project => Assembly.Load(new AssemblyName(project.Name)))
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.Name.EndsWith("Api", StringComparison.Ordinal) && type.IsClass)
            .SelectMany(type => type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Where(field => !field.Name.Contains('<', StringComparison.Ordinal))
                .Select(field => type.Name + "." + field.Name))
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void Module_classes_name_their_dependencies()
    {
        // A class that takes the container looks up what it needs where it runs, so nobody can see
        // its dependencies or call it without that container. Registration is the exception: it is
        // static, so it never appears here.
        Type[] containers = [typeof(IServiceProvider), typeof(IServiceScopeFactory)];
        List<string> offenders = ModuleProjects()
            .Select(project => Assembly.Load(new AssemblyName(project.Name)))
            .SelectMany(assembly => assembly.GetTypes())
            .SelectMany(type => type.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .SelectMany(constructor => constructor.GetParameters())
                .Where(parameter => containers.Contains(parameter.ParameterType))
                .Select(parameter => type.Name + "(" + parameter.Name + ")"))
            .ToList();

        Assert.Empty(offenders);
    }

    private static IEnumerable<ProjectFile> ModuleProjects()
    {
        HashSet<string> contracts = new HashSet<string>(ProjectFile.InSourceTree().Where(project => IsContracts(project.Name)).Select(project => project.Name), StringComparer.Ordinal);
        return ProjectFile.InSourceTree().Where(project => contracts.Contains(project.Name + ContractsSuffix));
    }

    private static bool IsModuleOrContracts(ProjectFile project)
    {
        return IsContracts(project.Name) || ModuleProjects().Any(module => string.Equals(module.Name, project.Name, StringComparison.Ordinal));
    }

    private static bool IsContracts(string name)
    {
        return name.StartsWith(Product, StringComparison.Ordinal) && name.EndsWith(ContractsSuffix, StringComparison.Ordinal);
    }

    private static string ModuleOf(string name)
    {
        string module = name[Product.Length..];
        return module.EndsWith(ContractsSuffix, StringComparison.Ordinal) ? module[..^ContractsSuffix.Length] : module;
    }

    private static bool Allowed(ProjectFile project, string reference)
    {
        if (IsContracts(reference) || string.Equals(reference, "Bagatka.Foundation", StringComparison.Ordinal))
        {
            return true;
        }

        if (string.Equals(project.Name, KnownException.Project, StringComparison.Ordinal) && string.Equals(reference, KnownException.Reference, StringComparison.Ordinal))
        {
            return true;
        }

        bool module = !IsContracts(project.Name);
        return module && (ModuleMayUse.Contains(reference, StringComparer.Ordinal) || reference.StartsWith("Bagatka.Sdk.", StringComparison.Ordinal));
    }

    // The modules whose contracts a module's project or its own contracts reference: those it asks.
    private static HashSet<string> AsksOf(ProjectFile module)
    {
        ProjectFile contracts = ProjectFile.InSourceTree().Single(project => string.Equals(project.Name, module.Name + ContractsSuffix, StringComparison.Ordinal));
        IEnumerable<string> asked = module.ProjectReferences.Concat(contracts.ProjectReferences)
            .Where(IsContracts)
            .Select(ModuleOf)
            .Where(other => !string.Equals(other, ModuleOf(module.Name), StringComparison.Ordinal));
        return new HashSet<string>(asked, StringComparer.Ordinal);
    }

    private static bool Reaches(Dictionary<string, HashSet<string>> asks, string from, string target, HashSet<string> seen)
    {
        foreach (string next in asks.GetValueOrDefault(from) ?? new HashSet<string>(StringComparer.Ordinal))
        {
            if (string.Equals(next, target, StringComparison.Ordinal))
            {
                return true;
            }

            if (seen.Add(next) && Reaches(asks, next, target, seen))
            {
                return true;
            }
        }

        return false;
    }

    // Records generate a clone method the language reserves.
    private static bool IsRecord(Type type)
    {
        return type.GetMethod("<Clone>$") is not null;
    }

    private static bool IsStatic(Type type)
    {
        return type.IsAbstract && type.IsSealed;
    }

    // Migrations are generated code, public as EF Core makes them.
    private static bool IsMigration(Type type)
    {
        return type.BaseType?.FullName is "Microsoft.EntityFrameworkCore.Migrations.Migration" or "Microsoft.EntityFrameworkCore.Infrastructure.ModelSnapshot";
    }
}
