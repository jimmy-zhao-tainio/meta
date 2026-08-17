using System.Diagnostics;
using Meta.Integration;
using Meta.Operations;
using Meta.Operations.Domain;
using Meta.Surfaces;

namespace MetaWeave.Tests;

public sealed class MetaWeaveCliTests
{
    [Fact]
    public void Help_ExposesOnlyTheWeaveScriptWorkflow()
    {
        var result = RunCli("help");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("create", result.Output);
        Assert.Contains("add-direction", result.Output);
        Assert.Contains("add-string-parameter", result.Output);
        Assert.Contains("add-relation", result.Output);
        Assert.Contains("add-requirement", result.Output);
        Assert.Contains("add-transformation", result.Output);
        Assert.Contains("update-requirement", result.Output);
        Assert.Contains("update-relation", result.Output);
        Assert.Contains("update-transformation", result.Output);
        Assert.Contains("emit-requirement", result.Output);
        Assert.Contains("emit-relation", result.Output);
        Assert.Contains("emit-transformation", result.Output);
        Assert.Contains("show", result.Output);
        Assert.Contains("execute", result.Output);
        Assert.DoesNotContain("apply", result.Output);
        Assert.DoesNotContain("add-model", result.Output);
        Assert.DoesNotContain("add-binding", result.Output);
        Assert.DoesNotContain("suggest", result.Output);
        Assert.DoesNotContain("materialize", result.Output);

        var executeHelp = RunCli("help execute");
        Assert.Equal(0, executeHelp.ExitCode);
        Assert.Contains("Defaults to forward", executeHelp.Output);
        Assert.Contains("(--xml <path> | --csharp <path> | --sql <path>)", executeHelp.Output);
        Assert.Contains("new target workspace", executeHelp.Output);
        Assert.DoesNotContain("output-", executeHelp.Output);
    }

    [Fact]
    public void CreateAddAndShow_AuthorACompleteWeaveFromStandardInput()
    {
        var root = TemporaryDirectory("metaweave-cli-author");
        var directionPath = Path.Combine(root, "Direction");
        try
        {
            var create = RunCli(
                $"create --xml \"{directionPath}\" --name Catalog");
            Assert.Equal(0, create.ExitCode);

            var addDirection = RunCli(
                $"add-direction --workspace \"{directionPath}\" --name forward --source source=SourceModel --target-model TargetModel");
            Assert.Equal(0, addDirection.ExitCode);

            var addParameter = RunCli(
                $"add-string-parameter --workspace \"{directionPath}\" --direction forward --name databaseName");
            Assert.Equal(0, addParameter.ExitCode);

            var add = RunCli(
                $"add-transformation --workspace \"{directionPath}\" --direction forward --name Target --target-entity Target",
                standardInput: "SELECT s.Id AS Id FROM Source AS s;");
            Assert.Equal(0, add.ExitCode);

            var addRelation = RunCli(
                $"add-relation --workspace \"{directionPath}\" --direction forward --name SourceNames",
                standardInput: "SELECT s.Id AS Id, UPPER(s.Name) AS Name FROM Source AS s;");
            Assert.Equal(0, addRelation.ExitCode);

            var addRequirement = RunCli(
                $"add-requirement --workspace \"{directionPath}\" --direction forward --name SourceNamesPresent --code SourceNameMissing --message \"Every source requires a name.\"",
                standardInput: "SELECT s.Id AS SourceId FROM Source AS s WHERE s.Name IS NULL;");
            Assert.Equal(0, addRequirement.ExitCode);

            var show = RunCli($"show --workspace \"{directionPath}\"");
            Assert.Equal(0, show.ExitCode);
            Assert.Contains("Weave: Catalog", show.Output);
            Assert.Contains("forward: source:SourceModel -> TargetModel", show.Output);
            Assert.Contains("string @databaseName", show.Output);
            Assert.Contains("require SourceNamesPresent [SourceNameMissing]", show.Output);
            Assert.Contains("relation SourceNames", show.Output);
            Assert.Contains("Target -> Target", show.Output);

            var emitRequirement = RunCli(
                $"emit-requirement --workspace \"{directionPath}\" --direction forward --name SourceNamesPresent");
            Assert.Equal(0, emitRequirement.ExitCode);
            Assert.Contains("s.Name IS NULL", emitRequirement.Output);

            var emitRelation = RunCli(
                $"emit-relation --workspace \"{directionPath}\" --direction forward --name SourceNames");
            Assert.Equal(0, emitRelation.ExitCode);
            Assert.Contains("UPPER(s.Name) AS Name", emitRelation.Output);

            var updateRelation = RunCli(
                $"update-relation --workspace \"{directionPath}\" --direction forward --name SourceNames",
                standardInput: "SELECT s.Id AS Id, LOWER(s.Name) AS Name FROM Source AS s;");
            Assert.Equal(0, updateRelation.ExitCode);

            var updateRequirement = RunCli(
                $"update-requirement --workspace \"{directionPath}\" --direction forward --name SourceNamesPresent",
                standardInput: "SELECT s.Id AS SourceId FROM Source AS s WHERE TRIM(s.Name) = '';");
            Assert.Equal(0, updateRequirement.ExitCode);

            var emit = RunCli(
                $"emit-transformation --workspace \"{directionPath}\" --direction forward --name Target");
            Assert.Equal(0, emit.ExitCode);
            Assert.Contains("SELECT", emit.Output);
            Assert.Contains("s.Id AS Id", emit.Output);

            var update = RunCli(
                $"update-transformation --workspace \"{directionPath}\" --direction forward --name Target",
                standardInput: "SELECT s.Id AS Id, UPPER(s.Name) AS Name FROM Source AS s;");
            Assert.Equal(0, update.ExitCode);

            var updatedModel = TypedWorkspaceModelMapper.Load<MetaWeaveModel>(directionPath);
            Assert.Single(updatedModel.TransformationList);
            Assert.Single(updatedModel.DirectionRequirementList);
            Assert.Single(updatedModel.DirectionRelationList);
            Assert.Single(updatedModel.DirectionSourceWorkspaceList);
            Assert.Single(updatedModel.DirectionStringParameterList);
            Assert.Equal(3, updatedModel.SelectStatementList.Count);
            Assert.Equal(3, updatedModel.QuerySpecificationList.Count);
            Assert.Equal(3, updatedModel.NamedTableReferenceList.Count);

            var updatedRelationEmit = RunCli(
                $"emit-relation --workspace \"{directionPath}\" --direction forward --name SourceNames");
            Assert.Equal(0, updatedRelationEmit.ExitCode);
            Assert.Contains("LOWER(s.Name) AS Name", updatedRelationEmit.Output);

            var updatedEmit = RunCli(
                $"emit-transformation --workspace \"{directionPath}\" --direction forward --name Target");
            Assert.Equal(0, updatedEmit.ExitCode);
            Assert.Contains("UPPER(s.Name) AS Name", updatedEmit.Output);

            var invalidUpdate = RunCli(
                $"update-transformation --workspace \"{directionPath}\" --direction forward --name Target",
                standardInput: "SELECT * FROM Source AS s;");
            Assert.NotEqual(0, invalidUpdate.ExitCode);
            var emitAfterFailure = RunCli(
                $"emit-transformation --workspace \"{directionPath}\" --direction forward --name Target");
            Assert.Equal(updatedEmit.Output, emitAfterFailure.Output);

            var updatedRequirementEmit = RunCli(
                $"emit-requirement --workspace \"{directionPath}\" --direction forward --name SourceNamesPresent");
            var invalidRequirementUpdate = RunCli(
                $"update-requirement --workspace \"{directionPath}\" --direction forward --name SourceNamesPresent",
                standardInput: "SELECT * FROM Source AS s;");
            Assert.NotEqual(0, invalidRequirementUpdate.ExitCode);
            var requirementEmitAfterFailure = RunCli(
                $"emit-requirement --workspace \"{directionPath}\" --direction forward --name SourceNamesPresent");
            Assert.Equal(updatedRequirementEmit.Output, requirementEmitAfterFailure.Output);
            Assert.DoesNotContain(".sql", string.Join("\n", Directory.EnumerateFiles(directionPath, "*", SearchOption.AllDirectories)));
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public void OneWorkspace_CarriesBothIndependentDirections()
    {
        var root = TemporaryDirectory("metaweave-cli-bidirectional");
        var weavePath = Path.Combine(root, "Weave");
        try
        {
            Assert.Equal(0, RunCli(
                $"create --xml \"{weavePath}\" --name Catalog").ExitCode);
            Assert.Equal(0, RunCli(
                $"add-direction --workspace \"{weavePath}\" --name forward --source source=LeftModel --target-model RightModel").ExitCode);
            Assert.Equal(0, RunCli(
                $"add-direction --workspace \"{weavePath}\" --name reverse --source source=RightModel --target-model LeftModel").ExitCode);
            Assert.Equal(0, RunCli(
                $"add-transformation --workspace \"{weavePath}\" --direction forward --name Entity --target-entity Entity",
                standardInput: "SELECT s.Id AS Id FROM Source AS s;").ExitCode);
            Assert.Equal(0, RunCli(
                $"add-transformation --workspace \"{weavePath}\" --direction reverse --name Entity --target-entity Entity",
                standardInput: "SELECT s.Id AS Id FROM Source AS s;").ExitCode);

            var model = TypedWorkspaceModelMapper.Load<MetaWeaveModel>(weavePath);
            Assert.Single(model.WeaveList);
            Assert.Equal(2, model.DirectionList.Count);
            Assert.Equal(2, model.DirectionSourceWorkspaceList.Count);
            Assert.Equal(2, model.TransformationList.Count);
            Assert.Equal(2, model.SelectStatementList.Count);
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task Execute_DefaultsToForwardAndCreatesANewTargetWorkspace()
    {
        var root = TemporaryDirectory("metaweave-cli-execute");
        var targetPath = Path.Combine(root, "Target");
        var repositoryRoot = FindRepositoryRoot();
        var contractsRoot = Path.Combine(
            repositoryRoot,
            "MetaWeave",
            "Script",
            "Samples",
            "Contracts");
        var directionPath = Path.Combine(
            repositoryRoot,
            "MetaWeave",
            "Script",
            "Samples",
            "ScopedCatalog");
        var sourcePath = Path.Combine(contractsRoot, "SampleScopedSourceCatalog");
        var expectedPath = Path.Combine(contractsRoot, "SampleScopedReferenceCatalog");

        try
        {
            var execute = RunCli(
                $"execute --workspace \"{directionPath}\" --source-workspace \"{sourcePath}\" --target-workspace \"{expectedPath}\" --xml \"{targetPath}\"");

            Assert.True(execute.ExitCode == 0, execute.Output);
            var expected = await TypedWorkspaceModelMapper.LoadStateAsync(expectedPath);
            var actual = await TypedWorkspaceModelMapper.LoadStateAsync(targetPath);
            Assert.Null(InMemoryWorkspaceComparer.FindDifference(expected, actual));
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public void Execute_ReportsContractFailureWithoutCreatingOutput()
    {
        var root = TemporaryDirectory("metaweave-cli-execute-failure");
        var targetPath = Path.Combine(root, "Target");
        var repositoryRoot = FindRepositoryRoot();
        var contractsRoot = Path.Combine(
            repositoryRoot,
            "MetaWeave",
            "Script",
            "Samples",
            "Contracts");
        var directionPath = Path.Combine(
            repositoryRoot,
            "MetaWeave",
            "Script",
            "Samples",
            "ScopedCatalog");
        var wrongSourcePath = Path.Combine(contractsRoot, "SampleSourceCatalog");
        var expectedPath = Path.Combine(contractsRoot, "SampleScopedReferenceCatalog");

        try
        {
            var execute = RunCli(
                $"execute --workspace \"{directionPath}\" --source-workspace \"{wrongSourcePath}\" --target-workspace \"{expectedPath}\" --xml \"{targetPath}\"");

            Assert.Equal(4, execute.ExitCode);
            Assert.Contains("SourceModelMismatch", execute.Output);
            Assert.False(Directory.Exists(targetPath));
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task Execute_ReportsRequirementViolationsWithoutCreatingOutput()
    {
        var root = TemporaryDirectory("metaweave-cli-requirement-failure");
        var outputPath = Path.Combine(root, "Output");
        var weavePath = Path.Combine(root, "Weave");
        var repositoryRoot = FindRepositoryRoot();
        var contractsRoot = Path.Combine(
            repositoryRoot,
            "MetaWeave",
            "Script",
            "Samples",
            "Contracts");
        var sourceWeavePath = Path.Combine(
            repositoryRoot,
            "MetaWeave",
            "Script",
            "Samples",
            "ScopedCatalog");
        var sourcePath = Path.Combine(contractsRoot, "SampleScopedSourceCatalog");
        var targetContractPath = Path.Combine(contractsRoot, "SampleScopedReferenceCatalog");

        try
        {
            var weave = TypedWorkspaceModelMapper.Load<MetaWeaveModel>(sourceWeavePath);
            _ = new MetaWeave.Core.MetaWeaveAuthoringService().AddRequirement(
                weave,
                "forward",
                "NoGroups",
                "SourceGroupsPresent",
                "The source must not contain groups.",
                "SELECT g.Id AS GroupId FROM [Group] AS g;");
            await WorkspaceSurface.CreateAsync(
                TypedWorkspaceModelMapper.ToInMemoryWorkspace(weave),
                weavePath,
                "xml");

            var execute = RunCli(
                $"execute --workspace \"{weavePath}\" --source-workspace \"{sourcePath}\" --target-workspace \"{targetContractPath}\" --xml \"{outputPath}\"");

            Assert.Equal(4, execute.ExitCode);
            Assert.Contains("SourceGroupsPresent", execute.Output);
            Assert.Contains("requirement NoGroups", execute.Output);
            Assert.Contains("GroupId=group:alpha", execute.Output);
            Assert.False(Directory.Exists(outputPath));
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task Execute_AcceptsNamedSourceWorkspacesAndStringParameters()
    {
        var root = TemporaryDirectory("metaweave-cli-multi-source");
        var weavePath = Path.Combine(root, "Weave");
        var warehousePath = Path.Combine(root, "Warehouse");
        var implementationPath = Path.Combine(root, "Implementation");
        var targetContractPath = Path.Combine(root, "TargetContract");
        var outputPath = Path.Combine(root, "Output");
        try
        {
            var warehouseModel = new GenericModel { Name = "WarehouseModel" };
            var fact = new GenericEntity { Name = "Fact" };
            fact.Properties.Add(new GenericProperty { Name = "Name" });
            warehouseModel.Entities.Add(fact);
            var warehouseInstance = new GenericInstance { ModelName = warehouseModel.Name };
            var factRecord = new GenericRecord { Id = "fact:sales" };
            factRecord.Values.Add("Name", "Sales");
            warehouseInstance.GetOrCreateEntityRecords("Fact").Add(factRecord);

            var implementationModel = new GenericModel { Name = "ImplementationModel" };
            var mapping = new GenericEntity { Name = "FactTableImplementation" };
            mapping.Properties.Add(new GenericProperty { Name = "FactId" });
            mapping.Properties.Add(new GenericProperty { Name = "TableName" });
            implementationModel.Entities.Add(mapping);
            var implementationInstance = new GenericInstance { ModelName = implementationModel.Name };
            var mappingRecord = new GenericRecord { Id = "mapping:sales" };
            mappingRecord.Values.Add("FactId", "fact:sales");
            mappingRecord.Values.Add("TableName", "FactSales");
            implementationInstance.GetOrCreateEntityRecords("FactTableImplementation").Add(mappingRecord);

            var targetModel = new GenericModel { Name = "SqlModel" };
            var table = new GenericEntity { Name = "Table" };
            table.Properties.Add(new GenericProperty { Name = "Name" });
            targetModel.Entities.Add(table);

            await WorkspaceSurface.CreateAsync(
                new InMemoryWorkspace(warehouseModel, warehouseInstance),
                warehousePath,
                "xml");
            await WorkspaceSurface.CreateAsync(
                new InMemoryWorkspace(implementationModel, implementationInstance),
                implementationPath,
                "xml");
            await WorkspaceSurface.CreateAsync(
                new InMemoryWorkspace(
                    targetModel,
                    new GenericInstance { ModelName = targetModel.Name }),
                targetContractPath,
                "xml");

            Assert.Equal(0, RunCli($"create --xml \"{weavePath}\" --name WarehouseToSql").ExitCode);
            Assert.Equal(0, RunCli(
                $"add-direction --workspace \"{weavePath}\" --name forward --source warehouse=WarehouseModel --source implementation=ImplementationModel --target-model SqlModel").ExitCode);
            Assert.Equal(0, RunCli(
                $"add-string-parameter --workspace \"{weavePath}\" --direction forward --name databaseName").ExitCode);
            Assert.Equal(0, RunCli(
                $"add-transformation --workspace \"{weavePath}\" --direction forward --name Table --target-entity Table",
                standardInput: "SELECT f.Id AS Id, CONCAT(@databaseName, '.', i.TableName) AS Name FROM warehouse.Fact AS f INNER JOIN implementation.FactTableImplementation AS i ON f.Id = i.FactId;").ExitCode);

            var execute = RunCli(
                $"execute --workspace \"{weavePath}\" --source-workspace \"warehouse={warehousePath}\" --source-workspace \"implementation={implementationPath}\" --parameter databaseName=AdventureWorks --target-workspace \"{targetContractPath}\" --xml \"{outputPath}\"");

            Assert.True(execute.ExitCode == 0, execute.Output);
            var output = await TypedWorkspaceModelMapper.LoadStateAsync(outputPath);
            var outputRecord = Assert.Single(output.Instance.RecordsByEntity["Table"]);
            Assert.Equal("fact:sales", outputRecord.Id);
            Assert.Equal("AdventureWorks.FactSales", outputRecord.Values["Name"]);
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public async Task Execute_CreatesTheSelectedCSharpSurfaceFromAnXmlContract()
    {
        var root = TemporaryDirectory("metaweave-cli-execute-surface");
        var targetPath = Path.Combine(root, "Target");
        var repositoryRoot = FindRepositoryRoot();
        var contractsRoot = Path.Combine(
            repositoryRoot,
            "MetaWeave",
            "Script",
            "Samples",
            "Contracts");
        var directionPath = Path.Combine(
            repositoryRoot,
            "MetaWeave",
            "Script",
            "Samples",
            "ScopedCatalog");
        var sourcePath = Path.Combine(contractsRoot, "SampleScopedSourceCatalog");
        var expectedPath = Path.Combine(contractsRoot, "SampleScopedReferenceCatalog");

        try
        {
            var execute = RunCli(
                $"execute --workspace \"{directionPath}\" --source-workspace \"{sourcePath}\" --target-workspace \"{expectedPath}\" --csharp \"{targetPath}\"");

            Assert.Equal(0, execute.ExitCode);
            Assert.Equal("csharp", WorkspaceMetaFile.Read(targetPath).Representation);
            var expected = await TypedWorkspaceModelMapper.LoadStateAsync(expectedPath);
            var actual = await TypedWorkspaceModelMapper.LoadStateAsync(targetPath);
            Assert.Null(InMemoryWorkspaceComparer.FindDifference(expected, actual));
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    private static (int ExitCode, string Output) RunCli(
        string arguments,
        string? workingDirectory = null,
        string? standardInput = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = CliTestHost.DotNetHost,
            Arguments = CliTestHost.BuildArguments("meta-weave", arguments),
            WorkingDirectory = workingDirectory ?? FindRepositoryRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = standardInput is not null,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException("Could not start meta-weave CLI process.");
        if (standardInput is not null)
        {
            process.StandardInput.Write(standardInput);
            process.StandardInput.Close();
        }
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try
        {
            process.WaitForExitAsync(timeout.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException exception)
        {
            TryKillProcessTree(process);
            process.WaitForExit();
            throw new TimeoutException(
                $"Timed out waiting for process: {startInfo.FileName} {startInfo.Arguments}",
                exception);
        }

        return (
            process.ExitCode,
            stdoutTask.GetAwaiter().GetResult() + stderrTask.GetAwaiter().GetResult());
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

            directory = Directory.GetParent(directory)?.FullName;
        }

        throw new InvalidOperationException(
            "Could not locate repository root from test base directory.");
    }

    private static string TemporaryDirectory(string prefix) =>
        Path.Combine(Path.GetTempPath(), prefix, Guid.NewGuid().ToString("N"));

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
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
        catch
        {
            // Best effort cleanup after a timed-out test process.
        }
    }
}
