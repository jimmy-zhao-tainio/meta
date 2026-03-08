using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Meta.Core.Domain;
using Meta.Core.Services;

namespace MetaWeave.Tests;

public sealed class CliTests
{
    [Fact]
    public void Help_ShowsCheckCommand()
    {
        var result = RunCli("help");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("MetaWeave CLI", result.Output);
        Assert.Contains("check", result.Output);
        Assert.Contains("suggest", result.Output);
        Assert.Contains("materialize", result.Output);
    }

    [Fact]
    public void Check_Help_ShowsWorkspaceOption()
    {
        var result = RunCli("check --help");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("--workspace <path>", result.Output);
    }

    [Fact]
    public void Suggest_Help_ShowsWorkspaceOption()
    {
        var result = RunCli("suggest --help");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("--workspace <path>", result.Output);
    }

    [Fact]
    public void Suggest_ShowsWeakSuggestions_WhenMatchesAreAmbiguous()
    {
        var root = Path.Combine(Path.GetTempPath(), "metaweave-cli-suggest-ambiguous", Guid.NewGuid().ToString("N"));
        var metaWeavePath = Path.Combine(root, "MetaWeave");
        Directory.CreateDirectory(root);
        try
        {
            var sourcePath = CreateSourceWorkspace(
                root,
                "Source",
                "SampleReferenceBindingCatalog",
                ("ReferenceTypeId", new[] { "type:string", "type:int", "type:string" }));
            var referenceAPath = CreateReferenceWorkspace(root, "ReferenceA");
            var referenceBPath = CreateReferenceWorkspace(root, "ReferenceB");

            Assert.Equal(0, RunCli($"init --new-workspace \"{metaWeavePath}\"").ExitCode);
            Assert.Equal(0, RunCli($"add-model --workspace \"{metaWeavePath}\" --alias Source --model SampleReferenceBindingCatalog --workspace-path \"{sourcePath}\"").ExitCode);
            Assert.Equal(0, RunCli($"add-model --workspace \"{metaWeavePath}\" --alias ReferenceA --model SampleReferenceCatalog --workspace-path \"{referenceAPath}\"").ExitCode);
            Assert.Equal(0, RunCli($"add-model --workspace \"{metaWeavePath}\" --alias ReferenceB --model SampleReferenceCatalog --workspace-path \"{referenceBPath}\"").ExitCode);

            var result = RunCli($"suggest --workspace \"{metaWeavePath}\"");
            Assert.Equal(0, result.ExitCode);
            Assert.Contains("Suggestions: 0", result.Output);
            Assert.Contains("WeakSuggestions: 2", result.Output);
            Assert.Contains("Binding suggestions", result.Output);
            Assert.Contains("  (none)", result.Output);
            Assert.Contains("Weak binding suggestions", result.Output);
            Assert.Contains("1) Source.Mapping.ReferenceTypeId -> ReferenceA.ReferenceType.Id", result.Output);
            Assert.Contains("2) Source.Mapping.ReferenceTypeId -> ReferenceB.ReferenceType.Id", result.Output);
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public void Suggest_ShowsWeakRoleSuggestions_WhenNamesAreRoleStyled()
    {
        var root = Path.Combine(Path.GetTempPath(), "metaweave-cli-suggest-weak-role", Guid.NewGuid().ToString("N"));
        var metaWeavePath = Path.Combine(root, "MetaWeave");
        Directory.CreateDirectory(root);
        try
        {
            var sourcePath = CreateSourceWorkspace(
                root,
                "Source",
                "SampleReferenceBindingCatalog",
                ("SourceReferenceTypeId", new[] { "type:string", "type:int", "type:string" }));
            var referencePath = CreateReferenceWorkspace(root, "Reference");

            Assert.Equal(0, RunCli($"init --new-workspace \"{metaWeavePath}\"").ExitCode);
            Assert.Equal(0, RunCli($"add-model --workspace \"{metaWeavePath}\" --alias Source --model SampleReferenceBindingCatalog --workspace-path \"{sourcePath}\"").ExitCode);
            Assert.Equal(0, RunCli($"add-model --workspace \"{metaWeavePath}\" --alias Reference --model SampleReferenceCatalog --workspace-path \"{referencePath}\"").ExitCode);

            var result = RunCli($"suggest --workspace \"{metaWeavePath}\"");
            Assert.Equal(0, result.ExitCode);
            Assert.Contains("Suggestions: 0", result.Output);
            Assert.Contains("WeakSuggestions: 1", result.Output);
            Assert.Contains("Weak binding suggestions", result.Output);
            Assert.Contains("Reference.ReferenceType.Id (role: SourceReferenceType)", result.Output);
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task FacadeCommands_CanCreateAWorkingWeaveWorkspace()
    {
        var root = Path.Combine(Path.GetTempPath(), "metaweave-facade-tests", Guid.NewGuid().ToString("N"));
        var metaWeavePath = Path.Combine(root, "MetaWeave");
        Directory.CreateDirectory(root);
        try
        {
            var init = RunCli($"init --new-workspace \"{metaWeavePath}\"");
            Assert.Equal(0, init.ExitCode);

            var addSource = RunCli($"add-model --workspace \"{metaWeavePath}\" --alias Source --model SampleSourceCatalog --workspace-path \"{GetFixtureWorkspacePath("SampleSourceCatalog")}\"");
            Assert.Equal(0, addSource.ExitCode);

            var addTarget = RunCli($"add-model --workspace \"{metaWeavePath}\" --alias Reference --model SampleReferenceCatalog --workspace-path \"{GetFixtureWorkspacePath("SampleReferenceCatalog")}\"");
            Assert.Equal(0, addTarget.ExitCode);

            var addBinding = RunCli($"add-binding --workspace \"{metaWeavePath}\" --name \"SampleSourceCatalog.Attribute.TypeId -> SampleReferenceCatalog.ReferenceType.Id\" --source-model Source --source-entity Attribute --source-property TypeId --target-model Reference --target-entity ReferenceType --target-property Id");
            Assert.Equal(0, addBinding.ExitCode);

            var check = RunCli($"check --workspace \"{metaWeavePath}\"");
            Assert.Equal(0, check.ExitCode);
            Assert.Contains("OK: weave check", check.Output);

            var weave = await new WorkspaceService().LoadAsync(metaWeavePath, searchUpward: false);
            Assert.Equal(2, weave.Instance.GetOrCreateEntityRecords("ModelReference").Count);
            Assert.Single(weave.Instance.GetOrCreateEntityRecords("PropertyBinding"));
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task AddModel_Fails_WhenReferencedWorkspaceDoesNotContainDeclaredModel()
    {
        var root = Path.Combine(Path.GetTempPath(), "metaweave-add-model-fail", Guid.NewGuid().ToString("N"));
        var metaWeavePath = Path.Combine(root, "MetaWeave");
        Directory.CreateDirectory(root);
        try
        {
            var init = RunCli($"init --new-workspace \"{metaWeavePath}\"");
            Assert.Equal(0, init.ExitCode);

            var addModel = RunCli($"add-model --workspace \"{metaWeavePath}\" --alias Source --model SampleSourceCatalog --workspace-path \"{GetFixtureWorkspacePath("SampleReferenceCatalog")}\"");
            Assert.Equal(4, addModel.ExitCode);
            Assert.Contains("contained model 'SampleReferenceCatalog', not 'SampleSourceCatalog'", addModel.Output);

            var weave = await new WorkspaceService().LoadAsync(metaWeavePath, searchUpward: false);
            Assert.Empty(weave.Instance.GetOrCreateEntityRecords("ModelReference"));
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task AddBinding_Fails_WhenSourcePropertyDoesNotExist()
    {
        var root = Path.Combine(Path.GetTempPath(), "metaweave-add-binding-fail", Guid.NewGuid().ToString("N"));
        var metaWeavePath = Path.Combine(root, "MetaWeave");
        Directory.CreateDirectory(root);
        try
        {
            Assert.Equal(0, RunCli($"init --new-workspace \"{metaWeavePath}\"").ExitCode);
            Assert.Equal(0, RunCli($"add-model --workspace \"{metaWeavePath}\" --alias Source --model SampleSourceCatalog --workspace-path \"{GetFixtureWorkspacePath("SampleSourceCatalog")}\"").ExitCode);
            Assert.Equal(0, RunCli($"add-model --workspace \"{metaWeavePath}\" --alias Reference --model SampleReferenceCatalog --workspace-path \"{GetFixtureWorkspacePath("SampleReferenceCatalog")}\"").ExitCode);

            var addBinding = RunCli($"add-binding --workspace \"{metaWeavePath}\" --name \"BrokenBinding\" --source-model Source --source-entity Attribute --source-property MissingTypeId --target-model Reference --target-entity ReferenceType --target-property Id");
            Assert.Equal(4, addBinding.ExitCode);
            Assert.Contains("source property 'Attribute.MissingTypeId' was not found", addBinding.Output);

            var weave = await new WorkspaceService().LoadAsync(metaWeavePath, searchUpward: false);
            Assert.Empty(weave.Instance.GetOrCreateEntityRecords("PropertyBinding"));
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public void Check_SanctionedAttributeReferenceWeave_Passes()
    {
        var result = RunCli($"check --workspace \"{GetFixtureWorkspacePath("Weave-Attribute-ReferenceType")}\"");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("OK: weave check", result.Output);
        Assert.Contains("Bindings: 1", result.Output);
        Assert.Contains("Errors: 0", result.Output);
    }

    [Fact]
    public void Check_SanctionedMappingReferenceWeave_Passes()
    {
        var result = RunCli($"check --workspace \"{GetFixtureWorkspacePath("Weave-Mapping-ReferenceType")}\"");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("OK: weave check", result.Output);
        Assert.Contains("Bindings: 2", result.Output);
        Assert.Contains("Errors: 0", result.Output);
    }

    [Fact]
    public async Task Materialize_SanctionedAttributeReferenceWeave_CreatesMergedWorkspaceWithRelationship()
    {
        var root = Path.Combine(Path.GetTempPath(), "metaweave-merge-attribute", Guid.NewGuid().ToString("N"));
        var mergedPath = Path.Combine(root, "Merged");
        Directory.CreateDirectory(root);
        try
        {
            var materialize = RunCli($"materialize --workspace \"{GetFixtureWorkspacePath("Weave-Attribute-ReferenceType")}\" --new-workspace \"{mergedPath}\" --model AttributeReferenceTypeMaterialized");
            Assert.Equal(0, materialize.ExitCode);
            Assert.Contains("OK: weave materialized", materialize.Output);

            var workspace = await new WorkspaceService().LoadAsync(mergedPath, searchUpward: false);
            var diagnostics = new ValidationService().Validate(workspace);
            Assert.False(diagnostics.HasErrors);

            var attributeEntity = workspace.Model.FindEntity("Attribute");
            Assert.NotNull(attributeEntity);
            Assert.DoesNotContain(attributeEntity!.Properties, item => string.Equals(item.Name, "TypeId", StringComparison.Ordinal));
            Assert.Contains(attributeEntity.Relationships, item =>
                string.Equals(item.Entity, "ReferenceType", StringComparison.Ordinal) &&
                string.Equals(item.GetColumnName(), "TypeId", StringComparison.Ordinal));

            var attributeRow = workspace.Instance.GetOrCreateEntityRecords("Attribute").Single(record => string.Equals(record.Id, "attribute:CustomerName", StringComparison.Ordinal));
            Assert.False(attributeRow.Values.ContainsKey("TypeId"));
            Assert.True(attributeRow.RelationshipIds.ContainsKey("TypeId"));
            Assert.Equal("type:string", attributeRow.RelationshipIds["TypeId"]);
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task Materialize_SanctionedMappingReferenceWeave_CreatesRoleBasedRelationships()
    {
        var root = Path.Combine(Path.GetTempPath(), "metaweave-merge-mapping", Guid.NewGuid().ToString("N"));
        var mergedPath = Path.Combine(root, "Merged");
        Directory.CreateDirectory(root);
        try
        {
            var materialize = RunCli($"materialize --workspace \"{GetFixtureWorkspacePath("Weave-Mapping-ReferenceType")}\" --new-workspace \"{mergedPath}\" --model MappingReferenceTypeMaterialized");
            Assert.Equal(0, materialize.ExitCode);
            Assert.Contains("OK: weave materialized", materialize.Output);

            var workspace = await new WorkspaceService().LoadAsync(mergedPath, searchUpward: false);
            var diagnostics = new ValidationService().Validate(workspace);
            Assert.False(diagnostics.HasErrors);

            var mappingEntity = workspace.Model.FindEntity("Mapping");
            Assert.NotNull(mappingEntity);
            Assert.DoesNotContain(mappingEntity!.Properties, item => string.Equals(item.Name, "SourceTypeId", StringComparison.Ordinal));
            Assert.DoesNotContain(mappingEntity.Properties, item => string.Equals(item.Name, "TargetTypeId", StringComparison.Ordinal));
            Assert.Contains(mappingEntity.Relationships, item => string.Equals(item.GetColumnName(), "SourceTypeId", StringComparison.Ordinal));
            Assert.Contains(mappingEntity.Relationships, item => string.Equals(item.GetColumnName(), "TargetTypeId", StringComparison.Ordinal));
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public void Materialize_Fails_WhenReferencedWorkspacesContainDuplicateEntityNames()
    {
        var root = Path.Combine(Path.GetTempPath(), "metaweave-materialize-collision", Guid.NewGuid().ToString("N"));
        var metaWeavePath = Path.Combine(root, "MetaWeave");
        Directory.CreateDirectory(root);
        try
        {
            Assert.Equal(0, RunCli($"init --new-workspace \"{metaWeavePath}\"").ExitCode);
            Assert.Equal(0, RunCli($"add-model --workspace \"{metaWeavePath}\" --alias Left --model SampleReferenceCatalog --workspace-path \"{GetFixtureWorkspacePath("SampleReferenceCatalog")}\"").ExitCode);
            Assert.Equal(0, RunCli($"add-model --workspace \"{metaWeavePath}\" --alias Right --model SampleReferenceCatalog --workspace-path \"{GetFixtureWorkspacePath("SampleReferenceCatalog")}\"").ExitCode);

            var materialize = RunCli($"materialize --workspace \"{metaWeavePath}\" --new-workspace \"{Path.Combine(root, "Merged")}\" --model DuplicateEntities");
            Assert.Equal(4, materialize.ExitCode);
            Assert.Contains("entity 'ReferenceType' already exists", materialize.Output);
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    private static string CreateReferenceWorkspace(string root, string folderName)
    {
        var path = Path.Combine(root, folderName);
        var workspace = new Workspace
        {
            WorkspaceRootPath = path,
            MetadataRootPath = Path.Combine(path, "metadata"),
            WorkspaceConfig = Meta.Core.WorkspaceConfig.Generated.MetaWorkspace.CreateDefault(),
            Model = new GenericModel
            {
                Name = "SampleReferenceCatalog",
                Entities =
                {
                    new GenericEntity
                    {
                        Name = "ReferenceType",
                        Properties =
                        {
                            new GenericProperty { Name = "Name", DataType = "string", IsNullable = false },
                        },
                    },
                },
            },
            Instance = new GenericInstance
            {
                ModelName = "SampleReferenceCatalog",
            },
            IsDirty = true,
        };
        AddRow(workspace.Instance, "ReferenceType", "type:decimal", ("Name", "decimal"));
        AddRow(workspace.Instance, "ReferenceType", "type:int", ("Name", "int"));
        AddRow(workspace.Instance, "ReferenceType", "type:string", ("Name", "string"));
        new WorkspaceService().SaveAsync(workspace).GetAwaiter().GetResult();
        return path;
    }

    private static string CreateSourceWorkspace(string root, string folderName, string modelName, params (string PropertyName, string[] Values)[] propertySets)
    {
        var path = Path.Combine(root, folderName);
        var entity = new GenericEntity
        {
            Name = "Mapping",
        };
        entity.Properties.Add(new GenericProperty { Name = "Name", DataType = "string", IsNullable = false });
        foreach (var propertySet in propertySets)
        {
            entity.Properties.Add(new GenericProperty { Name = propertySet.PropertyName, DataType = "string", IsNullable = false });
        }

        var workspace = new Workspace
        {
            WorkspaceRootPath = path,
            MetadataRootPath = Path.Combine(path, "metadata"),
            WorkspaceConfig = Meta.Core.WorkspaceConfig.Generated.MetaWorkspace.CreateDefault(),
            Model = new GenericModel
            {
                Name = modelName,
                Entities = { entity },
            },
            Instance = new GenericInstance
            {
                ModelName = modelName,
            },
            IsDirty = true,
        };

        var rowCount = propertySets.Max(item => item.Values.Length);
        for (var index = 0; index < rowCount; index++)
        {
            var values = new List<(string Key, string Value)>
            {
                ("Name", $"Mapping{index + 1}")
            };
            foreach (var propertySet in propertySets)
            {
                values.Add((propertySet.PropertyName, propertySet.Values[index]));
            }

            AddRow(workspace.Instance, "Mapping", $"mapping:{index + 1}", values.ToArray());
        }

        new WorkspaceService().SaveAsync(workspace).GetAwaiter().GetResult();
        return path;
    }

    private static void AddRow(GenericInstance instance, string entityName, string id, params (string Key, string Value)[] values)
    {
        var row = new GenericRecord
        {
            Id = id,
        };

        foreach (var (key, value) in values)
        {
            row.Values[key] = value;
        }

        instance.GetOrCreateEntityRecords(entityName).Add(row);
    }

    private static (int ExitCode, string Output) RunCli(string arguments)
    {
        var repoRoot = FindRepositoryRoot();
        var cliPath = ResolveCliPath(repoRoot);
        var startInfo = new ProcessStartInfo
        {
            FileName = cliPath,
            Arguments = arguments,
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        return RunProcess(startInfo, "Could not start meta-weave CLI process.");
    }

    private static (int ExitCode, string Output) RunProcess(ProcessStartInfo startInfo, string errorMessage)
    {
        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException(errorMessage);
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            try
            {
                process.WaitForExitAsync(timeout.Token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException exception)
            {
                TryKillProcessTree(process);
                process.WaitForExit();
                throw new TimeoutException($"Timed out waiting for process: {startInfo.FileName} {startInfo.Arguments}", exception);
            }

            var stdout = stdoutTask.GetAwaiter().GetResult();
            var stderr = stderrTask.GetAwaiter().GetResult();
            return (process.ExitCode, stdout + stderr);
        }
        finally
        {
            if (!process.HasExited)
            {
                TryKillProcessTree(process);
                process.WaitForExit();
            }
        }
    }

    private static string GetFixtureWorkspacePath(string name)
    {
        return Path.Combine(FindRepositoryRoot(), "MetaWeave.Workspaces", name);
    }

    private static string ResolveCliPath(string repoRoot)
    {
        var cliPath = Path.Combine(repoRoot, "MetaWeave.Cli", "bin", "Debug", "net8.0", "meta-weave.exe");
        if (!File.Exists(cliPath))
        {
            throw new FileNotFoundException($"Could not find compiled MetaWeave CLI at '{cliPath}'. Build MetaWeave.Cli before running tests.");
        }

        return cliPath;
    }

    private static void TryKillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (NotSupportedException)
        {
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(directory))
        {
            if (File.Exists(Path.Combine(directory, "Metadata.Framework.sln")))
            {
                return directory;
            }

            var parent = Directory.GetParent(directory);
            if (parent == null)
            {
                break;
            }

            directory = parent.FullName;
        }

        throw new InvalidOperationException("Could not locate repository root from test base directory.");
    }

    private static void CopyDirectory(string sourcePath, string targetPath)
    {
        Directory.CreateDirectory(targetPath);
        foreach (var directory in Directory.GetDirectories(sourcePath, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourcePath, directory);
            Directory.CreateDirectory(Path.Combine(targetPath, relative));
        }

        foreach (var file in Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourcePath, file);
            var targetFile = Path.Combine(targetPath, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
            File.Copy(file, targetFile, overwrite: true);
        }
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
