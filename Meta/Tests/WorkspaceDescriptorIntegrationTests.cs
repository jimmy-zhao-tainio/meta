using Meta.Integration;
using Meta.Operations.Domain;
using Meta.Surfaces;
using Meta.Surfaces.Xml;

namespace Meta.Core.Tests;

public sealed class WorkspaceDescriptorIntegrationTests
{
    [Fact]
    public async Task XmlAndSqlDescriptorReadsDoNotCreateAWriteLock()
    {
        var root = Path.Combine(Path.GetTempPath(), "meta-descriptor-tests", Guid.NewGuid().ToString("N"));
        var xmlPath = Path.Combine(root, "xml");
        var sqlPath = Path.Combine(root, "sql");
        try
        {
            await XmlWorkspaceWriter.WriteNewAsync(CreateState(), xmlPath);
            await using (var workspace = await WorkspaceSurface.OpenAsync(xmlPath))
            {
                Assert.Equal("Demo", await workspace.ReadModelNameAsync());
            }

            Assert.False(File.Exists(Path.Combine(xmlPath, ".meta.lock")));
            Directory.CreateDirectory(sqlPath);
            await File.WriteAllTextAsync(
                Path.Combine(sqlPath, WorkspaceMetaFile.FileName),
                "representation sql\nlocation META_TEST_SQL\n");
            var metadata = WorkspaceMetaFile.Read(sqlPath);
            Assert.Equal("sql", metadata.Representation);
            Assert.Equal("META_TEST_SQL", metadata.Location);
            Assert.False(File.Exists(Path.Combine(sqlPath, ".meta.lock")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static InMemoryWorkspace CreateState() => new(
        new GenericModel { Name = "Demo" },
        new GenericInstance { ModelName = "Demo" });
}
