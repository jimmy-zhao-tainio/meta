using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Meta.Operations.Domain;
using Meta.Operations;
using Meta.Integration;
using Meta.Surfaces.CSharp;
using Meta.Surfaces.Sql;
using Meta.Surfaces.Xml;
using Meta.Core.Services;
using Meta.Surfaces;

namespace Meta.Core.Tests;

public sealed class XmlWorkspaceTests
{
    [Fact]
    public async Task OpenedXmlWorkspace_RejectsCommitAfterExternalChange()
    {
        var (original, sampleRoot) = await TestWorkspaceFactory.LoadCanonicalSampleWorkspaceAsync();
        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            "metadata-studio-tests",
            Guid.NewGuid().ToString("N"));
        try
        {
            await XmlWorkspaceWriter.WriteNewAsync(original.State, tempRoot);
            var opened = await XmlWorkspaceReader.OpenAsync(tempRoot);

            var external = await XmlWorkspaceReader.OpenAsync(tempRoot);
            var externalCandidate = InMemoryOperations.Execute(
                external.State,
                new Operation.AddEntity("External"));
            await XmlWorkspaceWriter.WriteAsync(
                external,
                externalCandidate.Workspace,
                externalCandidate.Results);

            var candidate = InMemoryOperations.Execute(
                opened.State,
                new Operation.AddEntity("Candidate"));
            await Assert.ThrowsAsync<WorkspaceConflictException>(() =>
                XmlWorkspaceWriter.WriteAsync(
                    opened,
                    candidate.Workspace,
                    candidate.Results));

            Assert.Null(opened.Model.FindEntity("Candidate"));
            var reloaded = await XmlWorkspaceReader.OpenAsync(tempRoot);
            Assert.NotNull(reloaded.Model.FindEntity("External"));
            Assert.Null(reloaded.Model.FindEntity("Candidate"));
        }
        finally
        {
            TestWorkspaceFactory.DeleteDirectorySafe(sampleRoot);
            TestWorkspaceFactory.DeleteDirectorySafe(tempRoot);
        }
    }

    [Fact]
    public async Task WorkspaceHash_IsStable_AfterRoundTripSaveLoad()
    {
        var (original, sampleRoot) = await TestWorkspaceFactory.LoadCanonicalSampleWorkspaceAsync();
        var originalHash = original.Fingerprint;

        var tempRoot = Path.Combine(Path.GetTempPath(), "metadata-studio-tests", Guid.NewGuid().ToString("N"));
        try
        {
            await XmlWorkspaceWriter.WriteNewAsync(original.State, tempRoot);
            var reloaded = await XmlWorkspaceReader.OpenAsync(tempRoot);
            var reloadedHash = reloaded.Fingerprint;

            Assert.Equal(originalHash, reloadedHash);
        }
        finally
        {
            TestWorkspaceFactory.DeleteDirectorySafe(sampleRoot);
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Save_WritesWorkspaceConfigAndShardedInstances()
    {
        var (workspace, sampleRoot) = await TestWorkspaceFactory.LoadCanonicalSampleWorkspaceAsync();
        var expectedRows = workspace.Instance.RecordsByEntity.Values.Sum(records => records.Count);

        var tempRoot = Path.Combine(Path.GetTempPath(), "metadata-studio-tests", Guid.NewGuid().ToString("N"));
        try
        {
            await XmlWorkspaceWriter.WriteNewAsync(workspace.State, tempRoot);

            var workspaceConfigPath = Path.Combine(tempRoot, "workspace.meta");
            var modelPath = Path.Combine(tempRoot, "model.xml");
            var instanceDir = Path.Combine(tempRoot, "instances");
            Assert.True(File.Exists(workspaceConfigPath), "workspace.meta should exist after save.");
            var workspaceMetadata = WorkspaceMetaFile.Read(tempRoot);
            Assert.Equal("xml", workspaceMetadata.Representation);
            Assert.Equal(".", workspaceMetadata.Location);
            Assert.Equal(
                "model.xml",
                Meta.Surfaces.Configuration.MetaWorkspace.GetModelFile(workspaceMetadata.Configuration));
            Assert.Equal(
                "instances",
                Meta.Surfaces.Configuration.MetaWorkspace.GetInstanceDir(workspaceMetadata.Configuration));
            Assert.True(File.Exists(modelPath), "model.xml should exist after save.");
            Assert.True(Directory.Exists(instanceDir), "instance shard directory should exist after save.");
            Assert.True(Directory.GetFiles(instanceDir, "*.xml").Length > 0, "instance shard directory should contain XML files.");

            var reloaded = await XmlWorkspaceReader.OpenAsync(tempRoot);
            var reloadedRows = reloaded.Instance.RecordsByEntity.Values.Sum(records => records.Count);
            Assert.Equal(expectedRows, reloadedRows);
            Assert.Equal("1.0", reloaded.ContractVersion);
        }
        finally
        {
            TestWorkspaceFactory.DeleteDirectorySafe(sampleRoot);
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Save_WritesLfTerminatedXml()
    {
        var (workspace, sampleRoot) = await TestWorkspaceFactory.LoadCanonicalSampleWorkspaceAsync();
        var tempRoot = Path.Combine(Path.GetTempPath(), "metadata-studio-tests", Guid.NewGuid().ToString("N"));
        try
        {
            await XmlWorkspaceWriter.WriteNewAsync(workspace.State, tempRoot);

            var xmlPaths = Directory.GetFiles(tempRoot, "*.xml", SearchOption.AllDirectories);
            Assert.NotEmpty(xmlPaths);
            var metadataPaths = new[] { Path.Combine(tempRoot, "workspace.meta") };
            Assert.All(xmlPaths.Concat(metadataPaths), path =>
            {
                var bytes = File.ReadAllBytes(path);
                Assert.NotEmpty(bytes);
                Assert.False(HasUtf8Bom(bytes));
                Assert.DoesNotContain((byte)'\r', bytes);
                Assert.Equal((byte)'\n', bytes[^1]);
            });
        }
        finally
        {
            TestWorkspaceFactory.DeleteDirectorySafe(sampleRoot);
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Load_MissingWorkspaceMetadata_Fails()
    {
        var (workspace, sampleRoot) = await TestWorkspaceFactory.LoadCanonicalSampleWorkspaceAsync();
        var tempRoot = Path.Combine(Path.GetTempPath(), "metadata-studio-tests", Guid.NewGuid().ToString("N"));
        try
        {
            await XmlWorkspaceWriter.WriteNewAsync(workspace.State, tempRoot);
            var workspaceMetaPath = Path.Combine(tempRoot, "workspace.meta");
            Assert.True(File.Exists(workspaceMetaPath));
            File.Delete(workspaceMetaPath);

            await Assert.ThrowsAsync<FileNotFoundException>(async () =>
                await XmlWorkspaceReader.OpenAsync(tempRoot));
        }
        finally
        {
            TestWorkspaceFactory.DeleteDirectorySafe(sampleRoot);
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Load_WorkspaceMetadata_UsesCanonicalRootLayout()
    {
        var (workspace, sampleRoot) = await TestWorkspaceFactory.LoadCanonicalSampleWorkspaceAsync();
        var tempRoot = Path.Combine(Path.GetTempPath(), "metadata-studio-tests", Guid.NewGuid().ToString("N"));
        try
        {
            await XmlWorkspaceWriter.WriteNewAsync(workspace.State, tempRoot);

            var loaded = await XmlWorkspaceReader.OpenAsync(tempRoot);
            Assert.Equal(Path.GetFullPath(tempRoot), loaded.RootPath);
            Assert.NotEmpty(loaded.Instance.RecordsByEntity);
            Assert.Equal(Path.Combine(tempRoot, "model.xml"), loaded.ModelFilePath);
            Assert.Equal(Path.Combine(tempRoot, "instances"), loaded.InstanceDirectoryPath);
        }
        finally
        {
            TestWorkspaceFactory.DeleteDirectorySafe(sampleRoot);
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Load_AndSave_PreservesSplitEntityShardLayout()
    {
        var (workspace, sampleRoot) = await TestWorkspaceFactory.LoadCanonicalSampleWorkspaceAsync();
        var tempRoot = Path.Combine(Path.GetTempPath(), "metadata-studio-tests", Guid.NewGuid().ToString("N"));
        try
        {
            await XmlWorkspaceWriter.WriteNewAsync(workspace.State, tempRoot);
            SplitEntityShard(tempRoot, "Cube", "Cube.part-a.xml", "Cube.part-b.xml");

            var instanceDir = Path.Combine(tempRoot, "instances");
            var originalPartA = ReadRecordIds(
                Path.Combine(instanceDir, "Cube.part-a.xml"),
                "Cube");
            var originalPartB = ReadRecordIds(
                Path.Combine(instanceDir, "Cube.part-b.xml"),
                "Cube");
            var splitLoaded = await XmlWorkspaceReader.OpenAsync(tempRoot);
            var cubeRows = splitLoaded.Instance.GetOrCreateEntityRecords("Cube");
            Assert.NotEmpty(cubeRows);

            await XmlWorkspaceWriter.WriteAsync(
                splitLoaded,
                splitLoaded.State.Clone(),
                Array.Empty<OperationResult>());

            Assert.True(File.Exists(Path.Combine(instanceDir, "Cube.part-a.xml")));
            Assert.True(File.Exists(Path.Combine(instanceDir, "Cube.part-b.xml")));
            Assert.False(File.Exists(Path.Combine(instanceDir, "Cube.xml")));
            Assert.Equal(
                originalPartA,
                ReadRecordIds(
                    Path.Combine(instanceDir, "Cube.part-a.xml"),
                    "Cube"));
            Assert.Equal(
                originalPartB,
                ReadRecordIds(
                    Path.Combine(instanceDir, "Cube.part-b.xml"),
                    "Cube"));

            var reloaded = await XmlWorkspaceReader.OpenAsync(tempRoot);
            Assert.Equal(
                splitLoaded.Instance.GetOrCreateEntityRecords("Cube").Count,
                reloaded.Instance.GetOrCreateEntityRecords("Cube").Count);
        }
        finally
        {
            TestWorkspaceFactory.DeleteDirectorySafe(sampleRoot);
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static string[] ReadRecordIds(
        string path,
        string entityName)
    {
        return XDocument.Load(path)
            .Descendants(entityName)
            .Select(element => (string?)element.Attribute("Id"))
            .Where(id => id != null)
            .Select(id => id!)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ThenBy(id => id, StringComparer.Ordinal)
            .ToArray();
    }

    [Fact]
    public async Task Save_NewRowsGoToPrimarySplitShardForEntity()
    {
        var (workspace, sampleRoot) = await TestWorkspaceFactory.LoadCanonicalSampleWorkspaceAsync();
        var tempRoot = Path.Combine(Path.GetTempPath(), "metadata-studio-tests", Guid.NewGuid().ToString("N"));
        try
        {
            await XmlWorkspaceWriter.WriteNewAsync(workspace.State, tempRoot);
            SplitEntityShard(tempRoot, "Cube", "Cube.part-a.xml", "Cube.part-b.xml");

            var splitLoaded = await XmlWorkspaceReader.OpenAsync(tempRoot);
            var candidate = splitLoaded.State.Clone();
            candidate.Instance.GetOrCreateEntityRecords("Cube").Add(new GenericRecord
            {
                Id = "999",
                Values =
                {
                    ["CubeName"] = "Split Layout Insert",
                    ["Purpose"] = "Test row",
                    ["RefreshMode"] = "Manual",
                },
            });

            await XmlWorkspaceWriter.WriteAsync(
                splitLoaded,
                candidate,
                Array.Empty<OperationResult>());

            var primaryShard = XDocument.Load(Path.Combine(tempRoot, "instances", "Cube.part-a.xml"));
            var secondaryShard = XDocument.Load(Path.Combine(tempRoot, "instances", "Cube.part-b.xml"));

            Assert.NotNull(primaryShard
                .Descendants("Cube")
                .SingleOrDefault(element => string.Equals((string?)element.Attribute("Id"), "999", StringComparison.OrdinalIgnoreCase)));
            Assert.Null(secondaryShard
                .Descendants("Cube")
                .SingleOrDefault(element => string.Equals((string?)element.Attribute("Id"), "999", StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            TestWorkspaceFactory.DeleteDirectorySafe(sampleRoot);
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Load_DoesNotDiscoverWorkspaceRoot_FromNestedDirectory()
    {
        var (workspace, sampleRoot) = await TestWorkspaceFactory.LoadCanonicalSampleWorkspaceAsync();
        var tempRoot = Path.Combine(Path.GetTempPath(), "metadata-studio-tests", Guid.NewGuid().ToString("N"));
        try
        {
            await XmlWorkspaceWriter.WriteNewAsync(workspace.State, tempRoot);
            var nestedPath = Path.Combine(tempRoot, "a", "b", "c");
            Directory.CreateDirectory(nestedPath);

            await Assert.ThrowsAsync<FileNotFoundException>(async () =>
                await XmlWorkspaceReader.OpenAsync(nestedPath));
        }
        finally
        {
            TestWorkspaceFactory.DeleteDirectorySafe(sampleRoot);
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Load_Fails_ForUnsupportedContractMajorVersion()
    {
        var (workspace, sampleRoot) = await TestWorkspaceFactory.LoadCanonicalSampleWorkspaceAsync();
        var tempRoot = Path.Combine(Path.GetTempPath(), "metadata-studio-tests", Guid.NewGuid().ToString("N"));
        try
        {
            await XmlWorkspaceWriter.WriteNewAsync(workspace.State, tempRoot);
            var workspaceConfig = WorkspaceMetaFile.Read(tempRoot).Configuration;
            workspaceConfig.Workspace.Single().FormatVersion = "2.0";
            WorkspaceMetaFile.WriteXml(tempRoot, workspaceConfig);

            var exception = await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await XmlWorkspaceReader.OpenAsync(tempRoot));
            Assert.Contains("Unsupported contract major version", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TestWorkspaceFactory.DeleteDirectorySafe(sampleRoot);
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Load_AllowsNewerMinorContractVersion()
    {
        var (workspace, sampleRoot) = await TestWorkspaceFactory.LoadCanonicalSampleWorkspaceAsync();
        var tempRoot = Path.Combine(Path.GetTempPath(), "metadata-studio-tests", Guid.NewGuid().ToString("N"));
        try
        {
            await XmlWorkspaceWriter.WriteNewAsync(workspace.State, tempRoot);
            var workspaceConfig = WorkspaceMetaFile.Read(tempRoot).Configuration;
            workspaceConfig.Workspace.Single().FormatVersion = "1.7";
            WorkspaceMetaFile.WriteXml(tempRoot, workspaceConfig);

            var loaded = await XmlWorkspaceReader.OpenAsync(tempRoot);
            Assert.Equal("1.7", loaded.ContractVersion);
        }
        finally
        {
            TestWorkspaceFactory.DeleteDirectorySafe(sampleRoot);
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Save_RejectsInvalidWorkspace()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "metadata-studio-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var workspace = new InMemoryWorkspace(
                new GenericModel
                {
                    Name = "MetadataModel",
                },
                new GenericInstance
                {
                    ModelName = "MetadataModel",
                });

            var invalidEntity = new Meta.Operations.Domain.GenericEntity
            {
                Name = "Bad Name",
            };
            workspace.Model.Entities.Add(invalidEntity);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await XmlWorkspaceWriter.WriteNewAsync(workspace, tempRoot));

            Assert.Contains("validation failed", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Load_RejectsWorkspaceConfigPathsOutsideWorkspaceRoot()
    {
        var (workspace, sampleRoot) = await TestWorkspaceFactory.LoadCanonicalSampleWorkspaceAsync();
        var tempRoot = Path.Combine(Path.GetTempPath(), "metadata-studio-tests", Guid.NewGuid().ToString("N"));
        try
        {
            await XmlWorkspaceWriter.WriteNewAsync(workspace.State, tempRoot);
            var workspaceConfig = WorkspaceMetaFile.Read(tempRoot).Configuration;
            workspaceConfig.WorkspaceLayout.Single().ModelFilePath = "../outside-model.xml";
            WorkspaceMetaFile.WriteXml(tempRoot, workspaceConfig);

            var exception = await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await XmlWorkspaceReader.OpenAsync(tempRoot));
            Assert.Contains("outside workspace root", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TestWorkspaceFactory.DeleteDirectorySafe(sampleRoot);
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Save_RejectsRelationshipCycles()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "metadata-studio-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var workspace = new InMemoryWorkspace(
                new GenericModel
                {
                    Name = "MetadataModel",
                },
                new GenericInstance
                {
                    ModelName = "MetadataModel",
                });

            var entityA = new Meta.Operations.Domain.GenericEntity
            {
                Name = "EntityA",
            };
            entityA.Relationships.Add(new Meta.Operations.Domain.GenericRelationship
            {
                Entity = "EntityB",
            });

            var entityB = new Meta.Operations.Domain.GenericEntity
            {
                Name = "EntityB",
            };
            entityB.Relationships.Add(new Meta.Operations.Domain.GenericRelationship
            {
                Entity = "EntityA",
            });

            workspace.Model.Entities.Add(entityA);
            workspace.Model.Entities.Add(entityB);

            workspace.Instance.GetOrCreateEntityRecords("EntityA").Add(new Meta.Operations.Domain.GenericRecord
            {
                Id = "1",
            });
            workspace.Instance.GetOrCreateEntityRecords("EntityA")[0].RelationshipIds["EntityBId"] = "1";

            workspace.Instance.GetOrCreateEntityRecords("EntityB").Add(new Meta.Operations.Domain.GenericRecord
            {
                Id = "1",
            });
            workspace.Instance.GetOrCreateEntityRecords("EntityB")[0].RelationshipIds["EntityAId"] = "1";

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await XmlWorkspaceWriter.WriteNewAsync(workspace, tempRoot));

            Assert.Contains("relationship.cycle", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Save_CleansUpAtomicStagingDirectories()
    {
        var (workspace, sampleRoot) = await TestWorkspaceFactory.LoadCanonicalSampleWorkspaceAsync();

        var tempRoot = Path.Combine(Path.GetTempPath(), "metadata-studio-tests", Guid.NewGuid().ToString("N"));
        try
        {
            await XmlWorkspaceWriter.WriteNewAsync(workspace.State, tempRoot);
            await XmlWorkspaceWriter.WriteNewAsync(workspace.State, tempRoot);

            var leftovers = Directory.Exists(tempRoot)
                ? Directory.GetDirectories(tempRoot, ".__workspace-*")
                    .Concat(Directory.GetDirectories(tempRoot, "metadata.__*"))
                    .ToArray()
                : Array.Empty<string>();
            Assert.Empty(leftovers);
            Assert.True(File.Exists(Path.Combine(tempRoot, "workspace.meta")));
            Assert.True(File.Exists(Path.Combine(tempRoot, "model.xml")));
        }
        finally
        {
            TestWorkspaceFactory.DeleteDirectorySafe(sampleRoot);
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Save_RootWorkspaceConfigWriteFailure_DoesNotPersistMetadataChanges()
    {
        var (workspace, sampleRoot) = await TestWorkspaceFactory.LoadCanonicalSampleWorkspaceAsync();

        var tempRoot = Path.Combine(Path.GetTempPath(), "metadata-studio-tests", Guid.NewGuid().ToString("N"));
        try
        {
            await XmlWorkspaceWriter.WriteNewAsync(workspace.State, tempRoot);
            var persisted = await XmlWorkspaceReader.OpenAsync(tempRoot);
            var originalHash = persisted.Fingerprint;

            var candidate = persisted.State.Clone();
            var cube = candidate.Instance.GetOrCreateEntityRecords("Cube").First();
            cube.Values["CubeName"] = "Changed During Failed Save";

            var workspaceConfigPath = Path.Combine(tempRoot, "workspace.meta");
            using (var lockedConfig = new FileStream(workspaceConfigPath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                await Assert.ThrowsAnyAsync<IOException>(async () =>
                    await XmlWorkspaceWriter.WriteAsync(
                        persisted,
                        candidate,
                        Array.Empty<OperationResult>()));
            }

            var reloaded = await XmlWorkspaceReader.OpenAsync(tempRoot);
            var reloadedHash = reloaded.Fingerprint;
            Assert.Equal(originalHash, reloadedHash);
        }
        finally
        {
            TestWorkspaceFactory.DeleteDirectorySafe(sampleRoot);
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Save_RejectsWhenWorkspaceLockIsActive()
    {
        var (workspace, sampleRoot) = await TestWorkspaceFactory.LoadCanonicalSampleWorkspaceAsync();

        var tempRoot = Path.Combine(Path.GetTempPath(), "metadata-studio-tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempRoot);

            var process = System.Diagnostics.Process.GetCurrentProcess();
            var lockContent = string.Join(
                Environment.NewLine,
                new[]
                {
                    $"Pid={Environment.ProcessId}",
                    $"MachineName={Environment.MachineName}",
                    "ToolVersion=test",
                    $"ProcessStartTimeUtc={process.StartTime.ToUniversalTime():o}",
                    $"AcquiredUtc={DateTime.UtcNow:o}",
                }) + Environment.NewLine;
            File.WriteAllText(Path.Combine(tempRoot, ".meta.lock"), lockContent);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await XmlWorkspaceWriter.WriteNewAsync(workspace.State, tempRoot));
            Assert.Contains("locked", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TestWorkspaceFactory.DeleteDirectorySafe(sampleRoot);
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Save_RemovesStaleWorkspaceLockAndContinues()
    {
        var (workspace, sampleRoot) = await TestWorkspaceFactory.LoadCanonicalSampleWorkspaceAsync();

        var tempRoot = Path.Combine(Path.GetTempPath(), "metadata-studio-tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempRoot);

            var staleLockContent = string.Join(
                Environment.NewLine,
                new[]
                {
                    "Pid=999999",
                    $"MachineName={Environment.MachineName}",
                    "ToolVersion=test",
                    $"ProcessStartTimeUtc={DateTime.UtcNow.AddDays(-1):o}",
                    $"AcquiredUtc={DateTime.UtcNow.AddDays(-1):o}",
                }) + Environment.NewLine;
            var lockPath = Path.Combine(tempRoot, ".meta.lock");
            File.WriteAllText(lockPath, staleLockContent);

            await XmlWorkspaceWriter.WriteNewAsync(workspace.State, tempRoot);

            Assert.False(File.Exists(lockPath), "Stale lock should be removed after successful save.");
            Assert.True(File.Exists(Path.Combine(tempRoot, "workspace.meta")));
        }
        finally
        {
            TestWorkspaceFactory.DeleteDirectorySafe(sampleRoot);
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SaveLoad_NullableMissingProperty_StaysMissing()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "metadata-studio-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var workspace = BuildWorkspaceWithOptionalProperty(includeOptionalProp: false, optionalPropValue: null);
            await XmlWorkspaceWriter.WriteNewAsync(workspace, tempRoot);

            var itemShardPath = Path.Combine(tempRoot, "instances", "Item.xml");
            var shardXml = await File.ReadAllTextAsync(itemShardPath);
            Assert.DoesNotContain("<OptionalProp", shardXml, StringComparison.Ordinal);

            var reloaded = await XmlWorkspaceReader.OpenAsync(tempRoot);
            var row = reloaded.Instance.GetOrCreateEntityRecords("Item").Single();
            Assert.False(row.Values.ContainsKey("OptionalProp"));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SaveLoad_ExplicitEmptyStringProperty_StaysPresent()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "metadata-studio-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var workspace = BuildWorkspaceWithOptionalProperty(includeOptionalProp: true, optionalPropValue: string.Empty);
            await XmlWorkspaceWriter.WriteNewAsync(workspace, tempRoot);

            var itemShardPath = Path.Combine(tempRoot, "instances", "Item.xml");
            var shardDoc = XDocument.Load(itemShardPath);
            Assert.Contains(
                shardDoc.Descendants("OptionalProp"),
                element => string.Equals(element.Value, string.Empty, StringComparison.Ordinal));

            var reloaded = await XmlWorkspaceReader.OpenAsync(tempRoot);
            var row = reloaded.Instance.GetOrCreateEntityRecords("Item").Single();
            Assert.True(row.Values.ContainsKey("OptionalProp"));
            Assert.Equal(string.Empty, row.Values["OptionalProp"]);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SaveLoad_NullPropertyValue_IsNotSerializedAsEmptyString()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "metadata-studio-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var workspace = BuildWorkspaceWithOptionalProperty(includeOptionalProp: true, optionalPropValue: null);
            await XmlWorkspaceWriter.WriteNewAsync(workspace, tempRoot);

            var itemShardPath = Path.Combine(tempRoot, "instances", "Item.xml");
            var shardXml = await File.ReadAllTextAsync(itemShardPath);
            Assert.DoesNotContain("<OptionalProp", shardXml, StringComparison.Ordinal);

            var reloaded = await XmlWorkspaceReader.OpenAsync(tempRoot);
            var row = reloaded.Instance.GetOrCreateEntityRecords("Item").Single();
            Assert.False(row.Values.ContainsKey("OptionalProp"));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SaveLoad_RelationshipSerialization_DoesNotWriteNullOrBlankPlaceholders()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "metadata-studio-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var workspace = BuildWorkspaceWithRelationship();
            await XmlWorkspaceWriter.WriteNewAsync(workspace, tempRoot);

            var childShardPath = Path.Combine(tempRoot, "instances", "Child.xml");
            var childShardXml = await File.ReadAllTextAsync(childShardPath);
            Assert.Contains("ParentId=\"1\"", childShardXml, StringComparison.Ordinal);
            Assert.DoesNotContain("Ghost", childShardXml, StringComparison.Ordinal);
            Assert.DoesNotContain("BlankRel", childShardXml, StringComparison.Ordinal);
            Assert.DoesNotContain("Id=\"\" />", childShardXml, StringComparison.Ordinal);

            var reloaded = await XmlWorkspaceReader.OpenAsync(tempRoot);
            var child = reloaded.Instance.GetOrCreateEntityRecords("Child").Single();
            Assert.Equal("1", child.RelationshipIds["ParentId"]);
            Assert.False(child.RelationshipIds.ContainsKey("Ghost"));
            Assert.False(child.RelationshipIds.ContainsKey("BlankRel"));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SaveLoad_NullableMissingRelationship_StaysMissing()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "metadata-studio-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var workspace = BuildWorkspaceWithOptionalRelationship();
            await XmlWorkspaceWriter.WriteNewAsync(workspace, tempRoot);

            var childShardPath = Path.Combine(tempRoot, "instances", "Child.xml");
            var childShardXml = await File.ReadAllTextAsync(childShardPath);
            Assert.DoesNotContain("OptionalParentId", childShardXml, StringComparison.Ordinal);

            var reloaded = await XmlWorkspaceReader.OpenAsync(tempRoot);
            var child = reloaded.Instance.GetOrCreateEntityRecords("Child").Single();
            Assert.False(child.RelationshipIds.ContainsKey("OptionalParentId"));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SaveLoad_DoesNotLeaveNullValuesInMemory()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "metadata-studio-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var workspace = BuildWorkspaceWithRelationship();
            var item = new GenericRecord
            {
                Id = "1",
            };
            item.Values["OptionalProp"] = null!;
            workspace.Instance.GetOrCreateEntityRecords("Item").Add(item);
            await XmlWorkspaceWriter.WriteNewAsync(workspace, tempRoot);

            var reloaded = await XmlWorkspaceReader.OpenAsync(tempRoot);
            foreach (var entityRows in reloaded.Instance.RecordsByEntity.Values)
            {
                foreach (var row in entityRows)
                {
                    foreach (var value in row.Values.Values)
                    {
                        Assert.NotNull(value);
                    }

                    foreach (var relationshipTarget in row.RelationshipIds.Values)
                    {
                        Assert.False(string.IsNullOrWhiteSpace(relationshipTarget));
                    }
                }
            }
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SaveLoadSave_NullableMissingProperty_RemainsMissingWithoutDrift()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "metadata-studio-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var workspace = BuildWorkspaceWithOptionalProperty(includeOptionalProp: false, optionalPropValue: null);
            await XmlWorkspaceWriter.WriteNewAsync(workspace, tempRoot);

            var itemShardPath = Path.Combine(tempRoot, "instances", "Item.xml");
            var firstXml = await File.ReadAllTextAsync(itemShardPath);
            Assert.DoesNotContain("<OptionalProp", firstXml, StringComparison.Ordinal);

            var reloaded = await XmlWorkspaceReader.OpenAsync(tempRoot);
            await XmlWorkspaceWriter.WriteAsync(
                reloaded,
                reloaded.State.Clone(),
                Array.Empty<OperationResult>());

            var secondXml = await File.ReadAllTextAsync(itemShardPath);
            Assert.Equal(firstXml, secondXml);
            Assert.DoesNotContain("<OptionalProp", secondXml, StringComparison.Ordinal);

            var loadedAgain = await XmlWorkspaceReader.OpenAsync(tempRoot);
            var row = loadedAgain.Instance.GetOrCreateEntityRecords("Item").Single();
            Assert.False(row.Values.ContainsKey("OptionalProp"));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task SaveLoadSave_ExplicitEmptyStringProperty_RemainsExplicitWithoutDrift()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "metadata-studio-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var workspace = BuildWorkspaceWithOptionalProperty(includeOptionalProp: true, optionalPropValue: string.Empty);
            await XmlWorkspaceWriter.WriteNewAsync(workspace, tempRoot);

            var itemShardPath = Path.Combine(tempRoot, "instances", "Item.xml");
            var firstDoc = XDocument.Load(itemShardPath);
            Assert.Contains(
                firstDoc.Descendants("OptionalProp"),
                element => string.Equals(element.Value, string.Empty, StringComparison.Ordinal));

            var reloaded = await XmlWorkspaceReader.OpenAsync(tempRoot);
            await XmlWorkspaceWriter.WriteAsync(
                reloaded,
                reloaded.State.Clone(),
                Array.Empty<OperationResult>());

            var secondDoc = XDocument.Load(itemShardPath);
            Assert.Equal(firstDoc.ToString(SaveOptions.DisableFormatting), secondDoc.ToString(SaveOptions.DisableFormatting));
            Assert.Contains(
                secondDoc.Descendants("OptionalProp"),
                element => string.Equals(element.Value, string.Empty, StringComparison.Ordinal));

            var loadedAgain = await XmlWorkspaceReader.OpenAsync(tempRoot);
            var row = loadedAgain.Instance.GetOrCreateEntityRecords("Item").Single();
            Assert.True(row.Values.ContainsKey("OptionalProp"));
            Assert.Equal(string.Empty, row.Values["OptionalProp"]);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Load_BlankRelationshipAttribute_FailsHard()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "metadata-studio-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var workspace = BuildWorkspaceWithRelationship();
            await XmlWorkspaceWriter.WriteNewAsync(workspace, tempRoot);

            var childShardPath = Path.Combine(tempRoot, "instances", "Child.xml");
            var childShard = XDocument.Load(childShardPath);
            var childRow = childShard
                .Descendants("Child")
                .Single(element => string.Equals((string?)element.Attribute("Id"), "1", StringComparison.OrdinalIgnoreCase));
            childRow.SetAttributeValue("ParentId", string.Empty);
            childShard.Save(childShardPath);

            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await XmlWorkspaceReader.OpenAsync(tempRoot));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static InMemoryWorkspace BuildWorkspaceWithOptionalProperty(
        bool includeOptionalProp,
        string? optionalPropValue)
    {
        var workspace = new InMemoryWorkspace(
            new GenericModel
            {
                Name = "RoundTripModel",
            },
            new GenericInstance
            {
                ModelName = "RoundTripModel",
            });

        var item = new GenericEntity
        {
            Name = "Item",
        };
        item.Properties.Add(new GenericProperty
        {
            Name = "OptionalProp",
            IsNullable = true,
        });
        workspace.Model.Entities.Add(item);

        var row = new GenericRecord
        {
            Id = "1",
        };
        if (includeOptionalProp)
        {
            row.Values["OptionalProp"] = optionalPropValue!;
        }

        workspace.Instance.GetOrCreateEntityRecords("Item").Add(row);
        return workspace;
    }


    [Fact]
    public async Task Save_ModelOnlyWorkspace_OmitsInstanceDirectory_AndReloads()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "metadata-studio-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var workspace = BuildModelOnlyWorkspace();
            await XmlWorkspaceWriter.WriteNewAsync(workspace, tempRoot);

            Assert.True(File.Exists(Path.Combine(tempRoot, "workspace.meta")));
            Assert.True(File.Exists(Path.Combine(tempRoot, "model.xml")));
            Assert.False(Directory.Exists(Path.Combine(tempRoot, "instances")));

            var reloaded = await XmlWorkspaceReader.OpenAsync(tempRoot);
            Assert.Equal("ModelOnly", reloaded.Model.Name);
            Assert.Equal("ModelOnly", reloaded.Instance.ModelName);
            Assert.Empty(reloaded.Instance.RecordsByEntity);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }
    private static InMemoryWorkspace BuildModelOnlyWorkspace()
    {
        var workspace = new InMemoryWorkspace(
            new GenericModel
            {
                Name = "ModelOnly",
            },
            new GenericInstance
            {
                ModelName = "ModelOnly",
            });

        workspace.Model.Entities.Add(new GenericEntity
        {
            Name = "Capability",
        });

        return workspace;
    }
    private static void SplitEntityShard(
        string workspaceRoot,
        string entityName,
        string firstShardFileName,
        string secondShardFileName)
    {
        var shardPath = Path.Combine(workspaceRoot, "instances", entityName + ".xml");
        var shard = XDocument.Load(shardPath);
        var root = shard.Root ?? throw new InvalidDataException("Entity shard has no root.");
        var listElement = root.Elements().Single();
        var rows = listElement.Elements().ToList();
        Assert.True(rows.Count >= 2, $"Expected at least two rows in '{entityName}' shard for split test.");

        var midpoint = rows.Count / 2;
        if (midpoint == 0)
        {
            midpoint = 1;
        }

        WriteEntityShard(
            workspaceRoot,
            firstShardFileName,
            root.Name.LocalName,
            listElement.Name.LocalName,
            rows.Take(midpoint));
        WriteEntityShard(
            workspaceRoot,
            secondShardFileName,
            root.Name.LocalName,
            listElement.Name.LocalName,
            rows.Skip(midpoint));
        File.Delete(shardPath);
    }

    private static void WriteEntityShard(
        string workspaceRoot,
        string shardFileName,
        string rootName,
        string listName,
        IEnumerable<XElement> rows)
    {
        var rowCopies = rows.Select(row => new XElement(row)).ToList();
        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(
                rootName,
                new XElement(listName, rowCopies)));
        document.Save(Path.Combine(workspaceRoot, "instances", shardFileName));
    }

    private static InMemoryWorkspace BuildWorkspaceWithRelationship()
    {
        var workspace = new InMemoryWorkspace(
            new GenericModel
            {
                Name = "RoundTripModel",
            },
            new GenericInstance
            {
                ModelName = "RoundTripModel",
            });

        var item = new GenericEntity
        {
            Name = "Item",
        };
        item.Properties.Add(new GenericProperty
        {
            Name = "OptionalProp",
            IsNullable = true,
        });
        workspace.Model.Entities.Add(item);

        var parent = new GenericEntity
        {
            Name = "Parent",
        };
        workspace.Model.Entities.Add(parent);

        var child = new GenericEntity
        {
            Name = "Child",
        };
        child.Relationships.Add(new GenericRelationship
        {
            Entity = "Parent",
        });
        workspace.Model.Entities.Add(child);

        var parentRow = new GenericRecord
        {
            Id = "1",
        };
        workspace.Instance.GetOrCreateEntityRecords("Parent").Add(parentRow);

        var childRow = new GenericRecord
        {
            Id = "1",
        };
        childRow.RelationshipIds["ParentId"] = "1";
        workspace.Instance.GetOrCreateEntityRecords("Child").Add(childRow);

        return workspace;
    }

    private static bool HasUtf8Bom(IReadOnlyList<byte> bytes)
    {
        return bytes.Count >= 3 &&
               bytes[0] == 0xef &&
               bytes[1] == 0xbb &&
               bytes[2] == 0xbf;
    }

    private static InMemoryWorkspace BuildWorkspaceWithOptionalRelationship()
    {
        var workspace = BuildWorkspaceWithRelationship();
        var child = workspace.Model.Entities.Single(entity => string.Equals(entity.Name, "Child", StringComparison.Ordinal));
        child.Relationships.Add(new GenericRelationship
        {
            Entity = "Parent",
            Role = "OptionalParent",
            IsNullable = true,
        });

        return workspace;
    }

}










