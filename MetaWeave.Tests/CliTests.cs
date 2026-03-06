using System.Diagnostics;
using Meta.Core.Domain;
using Meta.Core.Services;
using MetaSchema.Core;
using MetaType.Core;
using MetaWeave.Core;

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
    public async Task FacadeCommands_CanCreateAWorkingWeaveWorkspace()
    {
        var root = Path.Combine(Path.GetTempPath(), "metaweave-facade-tests", Guid.NewGuid().ToString("N"));
        var metaTypePath = Path.Combine(root, "MetaType");
        var metaSchemaPath = Path.Combine(root, "MetaSchema");
        var metaWeavePath = Path.Combine(root, "MetaWeave");
        Directory.CreateDirectory(root);
        try
        {
            var workspaceService = new WorkspaceService();

            var metaType = MetaTypeWorkspaces.CreateMetaTypeWorkspace(metaTypePath);
            await workspaceService.SaveAsync(metaType);

            var metaSchema = MetaSchemaWorkspaces.CreateEmptyMetaSchemaWorkspace(metaSchemaPath);
            metaSchema.Instance.GetOrCreateEntityRecords("System").Add(new GenericRecord
            {
                Id = "sqlserver:system:test",
                Values = { ["Name"] = "TestSystem" }
            });
            metaSchema.Instance.GetOrCreateEntityRecords("Schema").Add(new GenericRecord
            {
                Id = "sqlserver:test:schema:dbo",
                Values = { ["Name"] = "dbo" },
                RelationshipIds = { ["SystemId"] = "sqlserver:system:test" }
            });
            metaSchema.Instance.GetOrCreateEntityRecords("Table").Add(new GenericRecord
            {
                Id = "sqlserver:test:schema:dbo:table:Cube",
                Values = { ["Name"] = "Cube", ["ObjectType"] = "Table" },
                RelationshipIds = { ["SchemaId"] = "sqlserver:test:schema:dbo" }
            });
            metaSchema.Instance.GetOrCreateEntityRecords("Field").Add(new GenericRecord
            {
                Id = "sqlserver:test:schema:dbo:table:Cube:field:CubeName",
                Values =
                {
                    ["Name"] = "CubeName",
                    ["TypeId"] = "sqlserver:type:nvarchar"
                },
                RelationshipIds = { ["TableId"] = "sqlserver:test:schema:dbo:table:Cube" }
            });
            await workspaceService.SaveAsync(metaSchema);

            var init = RunCli($"init --new-workspace \"{metaWeavePath}\"");
            Assert.Equal(0, init.ExitCode);

            var addSource = RunCli($"add-model --workspace \"{metaWeavePath}\" --alias MetaSchema --model MetaSchema --workspace-path \"{metaSchemaPath}\"");
            Assert.Equal(0, addSource.ExitCode);

            var addTarget = RunCli($"add-model --workspace \"{metaWeavePath}\" --alias MetaType --model MetaType --workspace-path \"{metaTypePath}\"");
            Assert.Equal(0, addTarget.ExitCode);

            var addBinding = RunCli($"add-binding --workspace \"{metaWeavePath}\" --name \"MetaSchema.Field.TypeId -> MetaType.Type.Id\" --source-model MetaSchema --source-entity Field --source-property TypeId --target-model MetaType --target-entity Type --target-property Id");
            Assert.Equal(0, addBinding.ExitCode);

            var check = RunCli($"check --workspace \"{metaWeavePath}\"");
            Assert.Equal(0, check.ExitCode);
            Assert.Contains("OK: weave check", check.Output);
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
        var metaTypePath = Path.Combine(root, "MetaType");
        var metaWeavePath = Path.Combine(root, "MetaWeave");
        Directory.CreateDirectory(root);
        try
        {
            var workspaceService = new WorkspaceService();
            var metaType = MetaTypeWorkspaces.CreateMetaTypeWorkspace(metaTypePath);
            await workspaceService.SaveAsync(metaType);

            var init = RunCli($"init --new-workspace \"{metaWeavePath}\"");
            Assert.Equal(0, init.ExitCode);

            var addModel = RunCli($"add-model --workspace \"{metaWeavePath}\" --alias MetaSchema --model MetaSchema --workspace-path \"{metaTypePath}\"");
            Assert.Equal(4, addModel.ExitCode);
            Assert.Contains("contained model 'MetaType', not 'MetaSchema'", addModel.Output);

            var weave = await workspaceService.LoadAsync(metaWeavePath, searchUpward: false);
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
        var metaTypePath = Path.Combine(root, "MetaType");
        var metaSchemaPath = Path.Combine(root, "MetaSchema");
        var metaWeavePath = Path.Combine(root, "MetaWeave");
        Directory.CreateDirectory(root);
        try
        {
            var workspaceService = new WorkspaceService();

            var metaType = MetaTypeWorkspaces.CreateMetaTypeWorkspace(metaTypePath);
            await workspaceService.SaveAsync(metaType);

            var metaSchema = MetaSchemaWorkspaces.CreateEmptyMetaSchemaWorkspace(metaSchemaPath);
            metaSchema.Instance.GetOrCreateEntityRecords("System").Add(new GenericRecord
            {
                Id = "sqlserver:system:test",
                Values = { ["Name"] = "TestSystem" }
            });
            metaSchema.Instance.GetOrCreateEntityRecords("Schema").Add(new GenericRecord
            {
                Id = "sqlserver:test:schema:dbo",
                Values = { ["Name"] = "dbo" },
                RelationshipIds = { ["SystemId"] = "sqlserver:system:test" }
            });
            metaSchema.Instance.GetOrCreateEntityRecords("Table").Add(new GenericRecord
            {
                Id = "sqlserver:test:schema:dbo:table:Cube",
                Values = { ["Name"] = "Cube", ["ObjectType"] = "Table" },
                RelationshipIds = { ["SchemaId"] = "sqlserver:test:schema:dbo" }
            });
            metaSchema.Instance.GetOrCreateEntityRecords("Field").Add(new GenericRecord
            {
                Id = "sqlserver:test:schema:dbo:table:Cube:field:CubeName",
                Values =
                {
                    ["Name"] = "CubeName",
                    ["TypeId"] = "sqlserver:type:nvarchar"
                },
                RelationshipIds = { ["TableId"] = "sqlserver:test:schema:dbo:table:Cube" }
            });
            await workspaceService.SaveAsync(metaSchema);

            Assert.Equal(0, RunCli($"init --new-workspace \"{metaWeavePath}\"").ExitCode);
            Assert.Equal(0, RunCli($"add-model --workspace \"{metaWeavePath}\" --alias MetaSchema --model MetaSchema --workspace-path \"{metaSchemaPath}\"").ExitCode);
            Assert.Equal(0, RunCli($"add-model --workspace \"{metaWeavePath}\" --alias MetaType --model MetaType --workspace-path \"{metaTypePath}\"").ExitCode);

            var addBinding = RunCli($"add-binding --workspace \"{metaWeavePath}\" --name \"BrokenBinding\" --source-model MetaSchema --source-entity Field --source-property MissingTypeId --target-model MetaType --target-entity Type --target-property Id");
            Assert.Equal(4, addBinding.ExitCode);
            Assert.Contains("source property 'Field.MissingTypeId' was not found", addBinding.Output);

            var weave = await workspaceService.LoadAsync(metaWeavePath, searchUpward: false);
            Assert.Empty(weave.Instance.GetOrCreateEntityRecords("PropertyBinding"));
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task Check_Resolves_MetaSchema_Field_TypeId_To_MetaType_Type_Id()
    {
        var root = Path.Combine(Path.GetTempPath(), "metaweave-tests", Guid.NewGuid().ToString("N"));
        var metaTypePath = Path.Combine(root, "MetaType");
        var metaSchemaPath = Path.Combine(root, "MetaSchema");
        var metaWeavePath = Path.Combine(root, "MetaWeave");
        Directory.CreateDirectory(root);
        try
        {
            var workspaceService = new WorkspaceService();

            var metaType = MetaTypeWorkspaces.CreateMetaTypeWorkspace(metaTypePath);
            await workspaceService.SaveAsync(metaType);

            var metaSchema = MetaSchemaWorkspaces.CreateEmptyMetaSchemaWorkspace(metaSchemaPath);
            metaSchema.Instance.GetOrCreateEntityRecords("System").Add(new GenericRecord
            {
                Id = "sqlserver:system:test",
                Values = { ["Name"] = "TestSystem" }
            });
            metaSchema.Instance.GetOrCreateEntityRecords("Schema").Add(new GenericRecord
            {
                Id = "sqlserver:test:schema:dbo",
                Values = { ["Name"] = "dbo" },
                RelationshipIds = { ["SystemId"] = "sqlserver:system:test" }
            });
            metaSchema.Instance.GetOrCreateEntityRecords("Table").Add(new GenericRecord
            {
                Id = "sqlserver:test:schema:dbo:table:Cube",
                Values = { ["Name"] = "Cube", ["ObjectType"] = "Table" },
                RelationshipIds = { ["SchemaId"] = "sqlserver:test:schema:dbo" }
            });
            metaSchema.Instance.GetOrCreateEntityRecords("Field").Add(new GenericRecord
            {
                Id = "sqlserver:test:schema:dbo:table:Cube:field:CubeName",
                Values =
                {
                    ["Name"] = "CubeName",
                    ["TypeId"] = "sqlserver:type:nvarchar",
                    ["Ordinal"] = "1",
                    ["IsNullable"] = "false"
                },
                RelationshipIds = { ["TableId"] = "sqlserver:test:schema:dbo:table:Cube" }
            });
            await workspaceService.SaveAsync(metaSchema);

            var weave = MetaWeaveWorkspaces.CreateEmptyMetaWeaveWorkspace(metaWeavePath);
            weave.Instance.GetOrCreateEntityRecords("ModelReference").Add(new GenericRecord
            {
                Id = "1",
                Values =
                {
                    ["Alias"] = "MetaSchema",
                    ["WorkspacePath"] = "..\\MetaSchema",
                    ["ModelName"] = "MetaSchema"
                }
            });
            weave.Instance.GetOrCreateEntityRecords("ModelReference").Add(new GenericRecord
            {
                Id = "2",
                Values =
                {
                    ["Alias"] = "MetaType",
                    ["WorkspacePath"] = "..\\MetaType",
                    ["ModelName"] = "MetaType"
                }
            });
            weave.Instance.GetOrCreateEntityRecords("PropertyBinding").Add(new GenericRecord
            {
                Id = "1",
                Values =
                {
                    ["Name"] = "MetaSchema.Field.TypeId -> MetaType.Type.Id",
                    ["SourceEntity"] = "Field",
                    ["SourceProperty"] = "TypeId",
                    ["TargetEntity"] = "Type",
                    ["TargetProperty"] = "Id"
                },
                RelationshipIds =
                {
                    ["SourceModelId"] = "1",
                    ["TargetModelId"] = "2"
                }
            });
            await workspaceService.SaveAsync(weave);

            var result = RunCli($"check --workspace \"{metaWeavePath}\"");
            Assert.Equal(0, result.ExitCode);
            Assert.Contains("OK: weave check", result.Output);
            Assert.Contains("Bindings: 1", result.Output);
            Assert.Contains("Errors: 0", result.Output);
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }


    [Fact]
    public void Check_SanctionedWeaveInstance_Passes()
    {
        var result = RunCli("check --workspace \".\\MetaWeave.Instances\\Weave-MetaSchema-MetaType\"");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("OK: weave check", result.Output);
        Assert.Contains("Bindings: 1", result.Output);
        Assert.Contains("Errors: 0", result.Output);
    }

    [Fact]
    public void Check_SanctionedMetaTypeConversionWeaveInstance_Passes()
    {
        var result = RunCli("check --workspace \".\\MetaWeave.Instances\\Weave-MetaTypeConversion-MetaType\"");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("OK: weave check", result.Output);
        Assert.Contains("Bindings: 2", result.Output);
        Assert.Contains("Errors: 0", result.Output);
    }

    [Fact]
    public async Task Materialize_SanctionedMetaSchemaWeave_CreatesMergedWorkspaceWithRelationship()
    {
        var root = Path.Combine(Path.GetTempPath(), "metaweave-merge-schema", Guid.NewGuid().ToString("N"));
        var mergedPath = Path.Combine(root, "Merged");
        Directory.CreateDirectory(root);
        try
        {
            var materialize = RunCli($"materialize --workspace \".\\MetaWeave.Instances\\Weave-MetaSchema-MetaType\" --new-workspace \"{mergedPath}\" --model MetaSchemaMetaTypeMaterialized");
            Assert.Equal(0, materialize.ExitCode);
            Assert.Contains("OK: weave materialized", materialize.Output);

            var workspace = await new WorkspaceService().LoadAsync(mergedPath, searchUpward: false);
            var diagnostics = new ValidationService().Validate(workspace);
            Assert.False(diagnostics.HasErrors);

            var fieldEntity = workspace.Model.FindEntity("Field");
            Assert.NotNull(fieldEntity);
            Assert.DoesNotContain(fieldEntity!.Properties, item => string.Equals(item.Name, "TypeId", StringComparison.Ordinal));
            Assert.Contains(fieldEntity.Relationships, item =>
                string.Equals(item.Entity, "Type", StringComparison.Ordinal) &&
                string.Equals(item.GetColumnName(), "TypeId", StringComparison.Ordinal));

            var fieldRow = workspace.Instance.GetOrCreateEntityRecords("Field").Single();
            Assert.False(fieldRow.Values.ContainsKey("TypeId"));
            Assert.True(fieldRow.RelationshipIds.ContainsKey("TypeId"));
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task Materialize_SanctionedMetaTypeConversionWeave_CreatesRoleBasedRelationships()
    {
        var root = Path.Combine(Path.GetTempPath(), "metaweave-merge-conversion", Guid.NewGuid().ToString("N"));
        var mergedPath = Path.Combine(root, "Merged");
        Directory.CreateDirectory(root);
        try
        {
            var materialize = RunCli($"materialize --workspace \".\\MetaWeave.Instances\\Weave-MetaTypeConversion-MetaType\" --new-workspace \"{mergedPath}\" --model MetaTypeConversionMetaTypeMaterialized");
            Assert.Equal(0, materialize.ExitCode);
            Assert.Contains("OK: weave materialized", materialize.Output);

            var workspace = await new WorkspaceService().LoadAsync(mergedPath, searchUpward: false);
            var diagnostics = new ValidationService().Validate(workspace);
            Assert.False(diagnostics.HasErrors);

            var typeMappingEntity = workspace.Model.FindEntity("TypeMapping");
            Assert.NotNull(typeMappingEntity);
            Assert.DoesNotContain(typeMappingEntity!.Properties, item => string.Equals(item.Name, "SourceTypeId", StringComparison.Ordinal));
            Assert.DoesNotContain(typeMappingEntity.Properties, item => string.Equals(item.Name, "TargetTypeId", StringComparison.Ordinal));
            Assert.Contains(typeMappingEntity.Relationships, item => string.Equals(item.GetColumnName(), "SourceTypeId", StringComparison.Ordinal));
            Assert.Contains(typeMappingEntity.Relationships, item => string.Equals(item.GetColumnName(), "TargetTypeId", StringComparison.Ordinal));
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
            Assert.Equal(0, RunCli($"add-model --workspace \"{metaWeavePath}\" --alias Left --model MetaType --workspace-path \".\\MetaType.Instances\\MetaType\"").ExitCode);
            Assert.Equal(0, RunCli($"add-model --workspace \"{metaWeavePath}\" --alias Right --model MetaType --workspace-path \".\\MetaType.Instances\\MetaType\"").ExitCode);

            var materialize = RunCli($"materialize --workspace \"{metaWeavePath}\" --new-workspace \"{Path.Combine(root, "Merged")}\" --model DuplicateEntities");
            Assert.Equal(4, materialize.ExitCode);
            Assert.Contains("entity 'Type' already exists", materialize.Output);
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
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

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}

