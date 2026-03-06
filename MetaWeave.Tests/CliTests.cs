using System.Diagnostics;
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

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
