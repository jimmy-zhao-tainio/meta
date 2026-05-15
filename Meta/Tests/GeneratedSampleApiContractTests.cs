using System;
using System.IO;
using Meta.Adapters;
using Meta.Core.Services;

namespace Meta.Core.Tests;

public sealed class GeneratedSampleApiContractTests
{
    [Fact]
    public async Task GeneratedCSharp_FromCanonicalSample_UsesStaticFacadeAndReferenceRelationships()
    {
        var services = new ServiceCollection();
        var workspaceRoot = await TestWorkspaceFactory.CreateTempCanonicalWorkspaceFromCanonicalSampleAsync();
        var outputRoot = Path.Combine(Path.GetTempPath(), "metadata-generated-sample-api", Guid.NewGuid().ToString("N"));

        try
        {
            var workspace = await services.WorkspaceService.LoadAsync(workspaceRoot);
            GenerationService.GenerateCSharp(workspace, outputRoot);

            var modelPath = Path.Combine(outputRoot, "EnterpriseBIPlatform.cs");
            var entityPath = Path.Combine(outputRoot, "Measure.cs");
            var modelCode = File.ReadAllText(modelPath);
            var entityCode = File.ReadAllText(entityPath);

            Assert.Contains("namespace EnterpriseBIPlatform", modelCode, StringComparison.Ordinal);
            Assert.Contains("public static partial class EnterpriseBIPlatform", modelCode, StringComparison.Ordinal);
            Assert.Contains("private static readonly EnterpriseBIPlatformInstance _builtIn", modelCode, StringComparison.Ordinal);
            Assert.Contains("public static EnterpriseBIPlatformInstance BuiltIn", modelCode, StringComparison.Ordinal);
            Assert.Contains("public static IReadOnlyList<Measure> MeasureList", modelCode, StringComparison.Ordinal);
            Assert.Contains("Enterprise Analytics Platform", modelCode, StringComparison.Ordinal);
            Assert.DoesNotContain("Signature", modelCode, StringComparison.Ordinal);
            Assert.DoesNotContain("ModelDefinitionXml", modelCode, StringComparison.Ordinal);
            Assert.DoesNotContain("CreateFromWorkspace", modelCode, StringComparison.Ordinal);
            Assert.DoesNotContain("LoadFromXmlWorkspace", modelCode, StringComparison.Ordinal);
            Assert.DoesNotContain("SaveToXmlWorkspace", modelCode, StringComparison.Ordinal);
            Assert.DoesNotContain("GetId(int id)", modelCode, StringComparison.Ordinal);

            Assert.Contains("namespace EnterpriseBIPlatform", entityCode, StringComparison.Ordinal);
            Assert.Contains("public string Id { get; set; }", entityCode, StringComparison.Ordinal);
            Assert.Contains("public Cube Cube { get; set; } = null!;", entityCode, StringComparison.Ordinal);
            Assert.DoesNotContain("using System.Xml.Serialization;", entityCode, StringComparison.Ordinal);
            Assert.DoesNotContain("using Meta.Core.Serialization;", entityCode, StringComparison.Ordinal);
            Assert.DoesNotContain("[XmlAttribute", entityCode, StringComparison.Ordinal);
            Assert.DoesNotContain("[XmlElement", entityCode, StringComparison.Ordinal);
            Assert.DoesNotContain("[XmlIgnore", entityCode, StringComparison.Ordinal);
            Assert.DoesNotContain("[MetaRelationship", entityCode, StringComparison.Ordinal);
            Assert.DoesNotContain("[XmlAttribute(\"CubeId\")]", entityCode, StringComparison.Ordinal);
            Assert.DoesNotContain("private string _cubeId", entityCode, StringComparison.Ordinal);
            Assert.DoesNotContain("private Cube? _cube", entityCode, StringComparison.Ordinal);
            Assert.DoesNotContain("public string CubeId", entityCode, StringComparison.Ordinal);
            Assert.DoesNotContain("__MetaCubeReference", entityCode, StringComparison.Ordinal);
            Assert.DoesNotContain("public int Id { get; }", entityCode, StringComparison.Ordinal);
            Assert.DoesNotContain("public int CubeId { get; }", entityCode, StringComparison.Ordinal);
            Assert.DoesNotContain("new Cube()", entityCode, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectorySafe(workspaceRoot);
            DeleteDirectorySafe(outputRoot);
        }
    }

    private static void DeleteDirectorySafe(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
