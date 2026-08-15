using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Meta.Integration;
using Xunit.Sdk;

namespace Meta.Architecture.Tests;

public sealed class FoundationBoundaryTests
{
    private static readonly string[] FoundationAssemblies =
    {
        "Meta.Operations",
        "Meta.Core",
        "Meta.Surfaces",
        "Meta.TypedModels",
        "Meta.Surfaces.Xml",
        "Meta.Surfaces.CSharp",
        "Meta.Surfaces.Sql",
        "Meta.Integration",
    };

    [Fact]
    public void DeclaredProductionReferencesRespectTheFoundationDag()
    {
        var root = FindRepositoryRoot();

        AssertProject(root, "Meta/Operations/Meta.Operations.csproj", [], []);
        AssertProject(root, "Meta/Core/Meta.Core.csproj", ["Meta.Operations"], []);
        AssertProject(root, "Meta/Surfaces/Meta.Surfaces.csproj", [], []);
        AssertProject(root, "Meta/TypedModels/Meta.TypedModels.csproj", ["Meta.Operations"], []);
        AssertProject(root, "Meta/Surfaces.Xml/Meta.Surfaces.Xml.csproj",
            ["Meta.Operations", "Meta.Surfaces", "Meta.TypedModels"], []);
        AssertProject(root, "Meta/Surfaces.CSharp/Meta.Surfaces.CSharp.csproj",
            ["Meta.Operations", "Meta.Surfaces"], ["Microsoft.CodeAnalysis.CSharp"]);
        AssertProject(root, "Meta/Surfaces.Sql/Meta.Surfaces.Sql.csproj",
            ["Meta.Operations"], ["Microsoft.Data.SqlClient"]);
        AssertProject(root, "Meta/Integration/Meta.Integration.csproj",
            ["Meta.Core", "Meta.Operations", "Meta.Surfaces", "Meta.Surfaces.Xml", "Meta.Surfaces.CSharp", "Meta.Surfaces.Sql", "Meta.TypedModels"],
            ["Microsoft.Data.SqlClient"]);
    }

    [Theory]
    [InlineData("MetaCli")]
    [InlineData("MetaDocs")]
    [InlineData("MetaMesh")]
    [InlineData("MetaWeave")]
    public async Task ProductModelHasOneAuthoritativeCSharpWorkspace(string product)
    {
        var root = FindRepositoryRoot();
        var workspace = Path.Combine(root, product, "Workspace");
        var descriptor = await File.ReadAllLinesAsync(Path.Combine(workspace, "workspace.meta"));
        var state = await TypedWorkspaceModelMapper.LoadStateAsync(workspace);

        Assert.Contains("representation csharp", descriptor);
        Assert.Contains($"source {product}.meta.cs", descriptor);
        Assert.True(state.Model.Entities.Count > 0, $"{product} workspace has no model entities.");
        Assert.False(
            Directory.Exists(Path.Combine(root, product, "Model")),
            $"{product}/Model duplicates the authoritative {product}/Workspace.");
    }

    [Fact]
    public void DeclaredTestReferencesRespectTheirLayerBoundaries()
    {
        var root = FindRepositoryRoot();

        AssertProject(root, "Meta/Operations.Tests/Meta.Operations.Tests.csproj",
            ["Meta.Operations"], []);
        AssertProject(root, "Meta/Tests/Meta.Core.Tests.csproj",
            ["Meta.Core", "Meta.Operations"], []);
        AssertProject(root, "Meta/Surfaces.Xml.Tests/Meta.Surfaces.Xml.Tests.csproj",
            ["Meta.Core", "Meta.Operations", "Meta.Surfaces", "Meta.Surfaces.Xml"], []);
        AssertProject(root, "Meta/Surfaces.CSharp.Tests/Meta.Surfaces.CSharp.Tests.csproj",
            ["Meta.Operations", "Meta.Surfaces", "Meta.Surfaces.CSharp"], []);
        AssertProject(root, "Meta/Surfaces.Sql.Tests/Meta.Surfaces.Sql.Tests.csproj",
            ["Meta.Surfaces.Sql"], []);
        AssertProject(root, "Meta/Integration.Tests/Meta.Integration.Tests.csproj",
            ["Meta.Core", "Meta.Integration", "Meta.Operations", "Meta.Surfaces", "Meta.Surfaces.CSharp", "Meta.Surfaces.Sql", "Meta.Surfaces.Xml", "Meta.TypedModels"], []);

        // Architecture tests deliberately load and inspect every foundation assembly.
        AssertProject(root, "Meta/Architecture.Tests/Meta.Architecture.Tests.csproj",
            FoundationAssemblies, []);
    }

    [Fact]
    public void BuiltAssemblyClosuresContainOnlyTheirOwnedTechnologyStacks()
    {
        AssertClosure("Meta.Operations", hasRoslyn: false, hasSqlClient: false);
        AssertClosure("Meta.Core", hasRoslyn: false, hasSqlClient: false);
        AssertClosure("Meta.Surfaces", hasRoslyn: false, hasSqlClient: false);
        AssertClosure("Meta.TypedModels", hasRoslyn: false, hasSqlClient: false);
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
        AssertNamespaces("Meta.TypedModels", "Meta.TypedModels");
        AssertNamespaces("Meta.Surfaces.Xml", "Meta.Surfaces.Xml");
        AssertNamespaces("Meta.Surfaces.CSharp", "Meta.Surfaces.CSharp");
        AssertNamespaces("Meta.Surfaces.Sql", "Meta.Surfaces.Sql");
        AssertNamespaces("Meta.Integration", "Meta.Integration");
    }

    [Fact]
    public void XmlDoesNotExposeTypedModelMappingToIntegrationThroughFriendship()
    {
        var xml = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "Meta.Surfaces.Xml.dll"));
        var friends = xml.GetCustomAttributes<InternalsVisibleToAttribute>()
            .Select(attribute => attribute.AssemblyName.Split(',')[0])
            .ToArray();

        Assert.DoesNotContain("Meta.Integration", friends, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            xml.ExportedTypes,
            type => type.Name.Contains("TypedModelMapper", StringComparison.Ordinal));
    }

    [Fact]
    public void FoundationPackageReferenceCannotBypassTheDeclaredDag()
    {
        var document = XDocument.Parse("""
            <Project>
              <ItemGroup>
                <PackageReference Include="Meta.Integration" Version="1.0.0" />
              </ItemGroup>
            </Project>
            """);

        var error = Assert.Throws<XunitException>(() =>
            AssertDeclaredDependencies("mutated production project", document, [], []));

        Assert.Contains("Meta.Integration", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ForbiddenTestProjectEdgeIsRejected()
    {
        var document = XDocument.Parse("""
            <Project>
              <ItemGroup>
                <ProjectReference Include="..\Integration\Meta.Integration.csproj" />
              </ItemGroup>
            </Project>
            """);

        var error = Assert.Throws<XunitException>(() =>
            AssertDeclaredDependencies("mutated Core test project", document, ["Meta.Core", "Meta.Operations"], []));

        Assert.Contains("Meta.Integration", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MicrosoftCodeAnalysisCommonIsClassifiedAsRoslyn()
    {
        var document = XDocument.Parse("""
            <Project>
              <ItemGroup>
                <PackageReference Include="Microsoft.CodeAnalysis.Common" Version="1.0.0" />
              </ItemGroup>
            </Project>
            """);

        var dependencies = ReadDeclaredDependencies(document);

        Assert.Equal(["Microsoft.CodeAnalysis.Common"], dependencies.TechnologyPackages);
    }

    private static void AssertProject(
        string root,
        string relativePath,
        string[] expectedFoundationReferences,
        string[] expectedTechnologyPackages)
    {
        var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        AssertDeclaredDependencies(relativePath, XDocument.Load(path), expectedFoundationReferences, expectedTechnologyPackages);
    }

    private static void AssertDeclaredDependencies(
        string project,
        XDocument document,
        string[] expectedFoundationReferences,
        string[] expectedTechnologyPackages)
    {
        var dependencies = ReadDeclaredDependencies(document);
        AssertExact(project, "foundation references", expectedFoundationReferences, dependencies.FoundationReferences);
        AssertExact(project, "technology packages", expectedTechnologyPackages, dependencies.TechnologyPackages);
    }

    private static DeclaredDependencies ReadDeclaredDependencies(XDocument document)
    {
        var projectReferences = document.Descendants("ProjectReference")
            .Select(element => Path.GetFileNameWithoutExtension((string)element.Attribute("Include")!));
        var packageReferences = document.Descendants("PackageReference")
            .Select(element => (string)element.Attribute("Include")!);

        var foundationReferences = projectReferences
            .Concat(packageReferences)
            .Where(IsFoundationAssembly)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        var technologyPackages = packageReferences
            .Where(value => IsRoslyn(value) || IsSqlClient(value))
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        return new DeclaredDependencies(foundationReferences, technologyPackages);
    }

    private static void AssertExact(string project, string dependencyKind, string[] expected, string[] actual)
    {
        var orderedExpected = expected.OrderBy(value => value, StringComparer.Ordinal).ToArray();
        if (orderedExpected.SequenceEqual(actual, StringComparer.Ordinal))
        {
            return;
        }

        throw new XunitException(
            $"{project} {dependencyKind} differ. " +
            $"Expected: [{string.Join(", ", orderedExpected)}]. " +
            $"Actual: [{string.Join(", ", actual)}].");
    }

    private static bool IsFoundationAssembly(string value) =>
        FoundationAssemblies.Contains(value, StringComparer.OrdinalIgnoreCase);

    private static bool IsRoslyn(string value) =>
        value.Equals("Microsoft.CodeAnalysis", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("Microsoft.CodeAnalysis.", StringComparison.OrdinalIgnoreCase);

    private static bool IsSqlClient(string value) =>
        value.Equals("Microsoft.Data.SqlClient", StringComparison.OrdinalIgnoreCase);

    private static void AssertClosure(string assemblyName, bool hasRoslyn, bool hasSqlClient)
    {
        var closure = ReadClosure(assemblyName);
        Assert.Equal(hasRoslyn, closure.Any(IsRoslyn));
        Assert.Equal(hasSqlClient, closure.Any(IsSqlClient));
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

    private sealed record DeclaredDependencies(
        string[] FoundationReferences,
        string[] TechnologyPackages);
}
