using System.Reflection;
using System.Xml.Linq;

namespace Meta.Architecture.Tests;

public sealed class FoundationBoundaryTests
{
    private static readonly string[] FoundationAssemblies =
    {
        "Meta.Operations",
        "Meta.Core",
        "Meta.Surfaces",
        "Meta.Surfaces.Xml",
        "Meta.Surfaces.CSharp",
        "Meta.Surfaces.Sql",
        "Meta.Integration",
    };

    [Fact]
    public void DeclaredProjectAndPackageReferencesRespectTheFoundationDag()
    {
        var root = FindRepositoryRoot();

        AssertProject(root, "Meta/Operations/Meta.Operations.csproj", [], []);
        AssertProject(root, "Meta/Core/Meta.Core.csproj", ["Meta.Operations"], []);
        AssertProject(root, "Meta/Surfaces/Meta.Surfaces.csproj", ["Meta.Core", "Meta.Operations"], []);
        AssertProject(root, "Meta/Surfaces.Xml/Meta.Surfaces.Xml.csproj",
            ["Meta.Core", "Meta.Operations", "Meta.Surfaces"], []);
        AssertProject(root, "Meta/Surfaces.CSharp/Meta.Surfaces.CSharp.csproj",
            ["Meta.Core", "Meta.Operations", "Meta.Surfaces"], ["Microsoft.CodeAnalysis.CSharp"]);
        AssertProject(root, "Meta/Surfaces.Sql/Meta.Surfaces.Sql.csproj",
            ["Meta.Core", "Meta.Operations", "Meta.Surfaces"], ["Microsoft.Data.SqlClient"]);
        AssertProject(root, "Meta/Integration/Meta.Integration.csproj",
            ["Meta.Core", "Meta.Operations", "Meta.Surfaces", "Meta.Surfaces.Xml", "Meta.Surfaces.CSharp", "Meta.Surfaces.Sql"],
            ["Microsoft.Data.SqlClient"]);
    }

    [Fact]
    public void BuiltAssemblyClosuresContainOnlyTheirOwnedTechnologyStacks()
    {
        AssertClosure("Meta.Operations", hasRoslyn: false, hasSqlClient: false);
        AssertClosure("Meta.Core", hasRoslyn: false, hasSqlClient: false);
        AssertClosure("Meta.Surfaces", hasRoslyn: false, hasSqlClient: false);
        AssertClosure("Meta.Surfaces.Xml", hasRoslyn: false, hasSqlClient: false);
        AssertClosure("Meta.Surfaces.CSharp", hasRoslyn: true, hasSqlClient: false);
        AssertClosure("Meta.Surfaces.Sql", hasRoslyn: false, hasSqlClient: true);
        AssertClosure("Meta.Integration", hasRoslyn: true, hasSqlClient: true);
    }

    [Fact]
    public void ExportedNamespacesDiscloseTheOwningAssembly()
    {
        AssertNamespaces("Meta.Operations", "Meta.Operations");
        AssertNamespaces("Meta.Core", "Meta.Core");
        AssertNamespaces("Meta.Surfaces", "Meta.Surfaces");
        AssertNamespaces("Meta.Surfaces.Xml", "Meta.Surfaces.Xml");
        AssertNamespaces("Meta.Surfaces.CSharp", "Meta.Surfaces.CSharp");
        AssertNamespaces("Meta.Surfaces.Sql", "Meta.Surfaces.Sql");
        AssertNamespaces("Meta.Integration", "Meta.Integration");
    }

    private static void AssertProject(
        string root,
        string relativePath,
        string[] expectedFoundationReferences,
        string[] expectedTechnologyPackages)
    {
        var document = XDocument.Load(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var projectReferences = document.Descendants("ProjectReference")
            .Select(element => Path.GetFileNameWithoutExtension((string)element.Attribute("Include")!))
            .Where(FoundationAssemblies.Contains)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        var technologyPackages = document.Descendants("PackageReference")
            .Select(element => (string)element.Attribute("Include")!)
            .Where(value => value is "Microsoft.CodeAnalysis.CSharp" or "Microsoft.Data.SqlClient")
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expectedFoundationReferences.OrderBy(value => value, StringComparer.Ordinal), projectReferences);
        Assert.Equal(expectedTechnologyPackages.OrderBy(value => value, StringComparer.Ordinal), technologyPackages);
    }

    private static void AssertClosure(string assemblyName, bool hasRoslyn, bool hasSqlClient)
    {
        var closure = ReadClosure(assemblyName);
        Assert.Equal(hasRoslyn, closure.Contains("Microsoft.CodeAnalysis.CSharp"));
        Assert.Equal(hasSqlClient, closure.Contains("Microsoft.Data.SqlClient"));
    }

    private static HashSet<string> ReadClosure(string rootAssemblyName)
    {
        var discovered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<string>();
        pending.Enqueue(rootAssemblyName);

        while (pending.TryDequeue(out var name))
        {
            if (!discovered.Add(name))
            {
                continue;
            }

            var path = Path.Combine(AppContext.BaseDirectory, name + ".dll");
            if (!File.Exists(path))
            {
                continue;
            }

            foreach (var reference in Assembly.LoadFrom(path).GetReferencedAssemblies())
            {
                if (!discovered.Contains(reference.Name!))
                {
                    pending.Enqueue(reference.Name!);
                }
            }
        }

        return discovered;
    }

    private static void AssertNamespaces(string assemblyName, string namespacePrefix)
    {
        var assembly = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, assemblyName + ".dll"));
        var violations = assembly.ExportedTypes
            .Where(type => type.Namespace is null ||
                           !type.Namespace.Equals(namespacePrefix, StringComparison.Ordinal) &&
                           !type.Namespace.StartsWith(namespacePrefix + ".", StringComparison.Ordinal))
            .Select(type => type.FullName)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        Assert.True(violations.Length == 0,
            $"{assemblyName} exports types outside '{namespacePrefix}': {string.Join(", ", violations)}");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Metadata.Framework.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate Metadata.Framework.sln.");
    }
}
