using System.Reflection;

namespace DrinkIt.ArchitectureTests;

/// <summary>
/// Executable form of the dependency rule in CLAUDE.md:
/// Api -> Application -> Domain, with Infrastructure implementing the ports
/// Application declares.
/// </summary>
public class DependencyRuleTests
{
    private static readonly Assembly DomainAssembly = typeof(Domain.AssemblyMarker).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(Application.AssemblyMarker).Assembly;

    /// <summary>Assemblies every project may reference: the BCL and nothing else.</summary>
    private static readonly string[] BaseClassLibrary = ["System", "netstandard", "mscorlib"];

    /// <summary>
    /// Framework assemblies that pass the "System.*" prefix but are infrastructure
    /// concerns: serialisation, HTTP and data access belong outside the inner layers.
    /// </summary>
    private static readonly string[] InfrastructureInDisguise =
        ["System.Text.Json", "System.Net.Http", "System.Data.Common", "System.Data"];

    [Fact]
    public void Domain_WhenInspectingReferences_OnlyDependsOnTheBcl()
    {
        string[] unexpected = ReferencesOutsideOf(DomainAssembly, BaseClassLibrary);

        Assert.True(
            unexpected.Length == 0,
            $"Domain must depend only on the BCL, but also references: {string.Join(", ", unexpected)}");
    }

    [Fact]
    public void Application_WhenInspectingReferences_OnlyDependsOnDomainAndTheBcl()
    {
        string[] allowed = [.. BaseClassLibrary, "DrinkIt.Domain"];

        string[] unexpected = ReferencesOutsideOf(ApplicationAssembly, allowed);

        Assert.True(
            unexpected.Length == 0,
            $"Application may only depend on Domain and the BCL, but also references: {string.Join(", ", unexpected)}");
    }

    [Fact]
    public void DomainTypes_WhenInspectingProperties_ExposeNoPublicSetters()
    {
        string[] offenders =
        [
            .. DomainAssembly.GetExportedTypes()
                .Where(type => type is { IsClass: true, IsAbstract: false })
                // DeclaredOnly matters: without it every type that inherits from
                // Exception reports HelpLink, Source and HResult, which are the
                // BCL's public setters and not ours to fix.
                .SelectMany(type => type.GetProperties(
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                .Where(property => property.SetMethod is { IsPublic: true })
                .Select(property => $"{property.DeclaringType!.Name}.{property.Name}")
        ];

        Assert.True(
            offenders.Length == 0,
            $"Domain types must not expose public setters: {string.Join(", ", offenders)}");
    }

    /// <summary>
    /// Allow-list check. Listing what is permitted catches packages nobody thought to
    /// forbid; a deny-list only catches the ones we predicted. The "System.*" prefix is
    /// waved through because the compiler always emits some of those, except for the
    /// handful in <see cref="InfrastructureInDisguise"/>.
    /// </summary>
    private static string[] ReferencesOutsideOf(Assembly assembly, string[] allowed) =>
    [
        .. assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name => !IsAllowed(name, allowed))
            .Order(StringComparer.Ordinal)
    ];

    private static bool IsAllowed(string name, string[] allowed)
    {
        if (InfrastructureInDisguise.Contains(name, StringComparer.Ordinal)) return false;

        return name.StartsWith("System.", StringComparison.Ordinal)
            || allowed.Contains(name, StringComparer.Ordinal);
    }
}
