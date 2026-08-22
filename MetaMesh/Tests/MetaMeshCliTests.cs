using System.Diagnostics;

namespace MetaMesh.Tests;

public sealed class MetaMeshCliTests
{
    [Fact]
    public void Help_ShowsDeclaredWorkspaceOperationSurface()
    {
        var result = RunCli("help");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("meta-mesh <command>", result.Output);
        Assert.Contains("workspaces", result.Output);
        Assert.Contains("operations", result.Output);
        Assert.Contains("steps", result.Output);
        Assert.Contains("validate", result.Output);
        Assert.Contains("add-workspace", result.Output);
        Assert.Contains("add-operation", result.Output);
        Assert.Contains("add-step", result.Output);
        Assert.Contains("update-step", result.Output);
        Assert.Contains("remove-step", result.Output);
        Assert.Contains("run", result.Output);
        Assert.Contains("show", result.Output);
        Assert.Contains("create", result.Output);
    }

    [Fact]
    public void AddStep_Help_UsesApplicationWorkspaceAndOperationArguments()
    {
        var result = RunCli("add-step --help");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("--workspace <workspace>", result.Output);
        Assert.DoesNotContain("--workspace-surface", result.Output);
        Assert.Contains("--operation <value>", result.Output);
        Assert.Contains("--executable <path>", result.Output);
        Assert.Contains("--arguments <arguments>", result.Output);
        Assert.Contains("--arguments-stdin", result.Output);
        Assert.Contains("--expected-exit-code <value>", result.Output);
    }

    [Fact]
    public void UpdateAndRemoveStep_MaintainTheOperationChain()
    {
        var root = Path.Combine(Path.GetTempPath(), "metamesh-cli-step-maintenance", Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Equal(0, RunCli($"create --xml \"{root}\" --name Chain").ExitCode);
            Assert.Equal(0, RunCli($"add-operation --workspace \"{root}\" --name refresh").ExitCode);
            Assert.Equal(0, RunCli($"add-step --workspace \"{root}\" --operation refresh --name first --executable cmd.exe --arguments \"/c echo first\"").ExitCode);
            Assert.Equal(0, RunCli($"add-step --workspace \"{root}\" --operation refresh --name middle --executable cmd.exe --arguments \"/c echo old\" --previous-step first").ExitCode);
            Assert.Equal(0, RunCli($"add-step --workspace \"{root}\" --operation refresh --name last --executable cmd.exe --arguments \"/c echo last\" --previous-step middle").ExitCode);

            var update = RunCli($"update-step --workspace \"{root}\" --operation refresh --name middle --arguments \"/c echo updated\"");
            Assert.Equal(0, update.ExitCode);

            var updatedSteps = RunCli($"steps --workspace \"{root}\" --operation refresh");
            Assert.Equal(0, updatedSteps.ExitCode);
            Assert.Contains("cmd.exe /c echo updated", updatedSteps.Output);

            var remove = RunCli($"remove-step --workspace \"{root}\" --operation refresh --name middle");
            Assert.Equal(0, remove.ExitCode);

            var model = Meta.Surfaces.Xml.TypedWorkspaceXmlSerializer.Load<MetaMesh.MetaMeshModel>(root, searchUpward: false);
            var first = Assert.Single(model.OperationStepList, step => step.Name == "first");
            var last = Assert.Single(model.OperationStepList, step => step.Name == "last");
            Assert.DoesNotContain(model.OperationStepList, step => step.Name == "middle");
            Assert.Same(first, last.PreviousStep);
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public void AddWorkspace_HelpAndParserRequireExactlyOneRepresentation()
    {
        var help = RunCli("add-workspace --help");

        Assert.Equal(0, help.ExitCode);
        Assert.Contains(
            "(--xml-path <path> | --csharp-path <path> | --sql-connection-env <environment-variable>)",
            help.Output);

        var root = Path.Combine(Path.GetTempPath(), "metamesh-cli-representation", Guid.NewGuid().ToString("N"));
        var meshPath = Path.Combine(root, "Docs.MetaMesh");
        try
        {
            Assert.Equal(0, RunCli($"create --xml \"{meshPath}\" --name Docs --root .").ExitCode);

            var missing = RunCli($"add-workspace --workspace \"{meshPath}\" --name docs");
            Assert.NotEqual(0, missing.ExitCode);
            Assert.Contains("Parameter group 'representation' requires one of", missing.Output);

            var multiple = RunCli(
                $"add-workspace --workspace \"{meshPath}\" --name docs --xml-path . --csharp-path .");
            Assert.NotEqual(0, multiple.ExitCode);
            Assert.Contains("Parameter group 'representation' accepts only one member", multiple.Output);
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public void Help_Forms_AreServedByMetaCliRuntime()
    {
        foreach (var arguments in new[] { "--help", "-h", "help", "help run", "run help", "run -h" })
        {
            var result = RunCli(arguments);

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("meta-mesh", result.Output);
        }

        var commandHelp = RunCli("help run");
        Assert.Contains("meta-mesh run", commandHelp.Output);
        Assert.Contains("--operation <value>", commandHelp.Output);
        Assert.Contains("--workspace <workspace>", commandHelp.Output);
        Assert.DoesNotContain("--workspace-surface", commandHelp.Output);
    }

    [Fact]
    public void Create_Help_DeclaresItsOutputSurfaceWithoutAnInputWorkspace()
    {
        var result = RunCli("help create");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("--xml <path>", result.Output);
        Assert.Contains("--csharp <path>", result.Output);
        Assert.Contains("--sql <path>", result.Output);
        Assert.DoesNotContain("--workspace <workspace>", result.Output);
    }

    [Fact]
    public void Authoring_DeclaresWorkspacesOperationsAndExecutableSteps()
    {
        var root = Path.Combine(Path.GetTempPath(), "metamesh-cli", Guid.NewGuid().ToString("N"));
        var meshPath = Path.Combine(root, "Docs.MetaMesh");
        try
        {
            var create = RunCli($"create --xml \"{meshPath}\" --name Docs --root .");
            Assert.Equal(0, create.ExitCode);

            var workspace = RunCli($"add-workspace --workspace \"{meshPath}\" --name docs --xml-path . --model MetaDocs");
            Assert.Equal(0, workspace.ExitCode);

            var operation = RunCli($"add-operation --workspace \"{meshPath}\" --name refresh-docs --description \"Refresh docs\"");
            Assert.Equal(0, operation.ExitCode);

            var step = RunCli($"add-step --workspace \"{meshPath}\" --operation refresh-docs --name echo --executable cmd.exe --arguments \"/c echo hello {{workspace:docs.location}}\"");
            Assert.Equal(0, step.ExitCode);
            Assert.Contains("Ok", step.Output);
            Assert.DoesNotContain("Operations:", step.Output);
            Assert.DoesNotContain("cmd.exe /c echo hello {workspace:docs.location}", step.Output);

            var show = RunCli($"show --workspace \"{meshPath}\"");
            Assert.Equal(0, show.ExitCode);
            Assert.Contains("MetaMesh:", show.Output);
            Assert.Contains("Workspaces: 1 (0 missing)", show.Output);
            Assert.Contains("Operations: 1", show.Output);
            Assert.Contains("Run `meta-mesh operations`", show.Output);
            Assert.DoesNotContain("refresh-docs", show.Output);
            Assert.DoesNotContain("cmd.exe /c echo hello {workspace:docs.location}", show.Output);

            var workspaces = RunCli($"workspaces --workspace \"{meshPath}\"");
            Assert.Equal(0, workspaces.ExitCode);
            Assert.Contains("docs (MetaDocs) - ok", workspaces.Output);

            var operations = RunCli($"operations --workspace \"{meshPath}\"");
            Assert.Equal(0, operations.ExitCode);
            Assert.Contains("refresh-docs", operations.Output);
            Assert.Contains("1 step", operations.Output);
            Assert.DoesNotContain("cmd.exe /c echo hello {workspace:docs.location}", operations.Output);

            var steps = RunCli($"steps --workspace \"{meshPath}\" --operation refresh-docs");
            Assert.Equal(0, steps.ExitCode);
            Assert.Contains("Operation: refresh-docs", steps.Output);
            Assert.Contains("1. echo", steps.Output);
            Assert.Contains("cmd.exe /c echo hello {workspace:docs.location}", steps.Output);

            var validate = RunCli($"validate --workspace \"{meshPath}\" --operation refresh-docs");
            Assert.Equal(0, validate.ExitCode);
            Assert.Contains("Operation: refresh-docs", validate.Output);
            Assert.Contains("Validation: OK", validate.Output);
            Assert.Contains("1 step ready.", validate.Output);

            var verboseShow = RunCli($"show --workspace \"{meshPath}\" --verbose");
            Assert.Equal(0, verboseShow.ExitCode);
            Assert.Contains("Workspaces:", verboseShow.Output);
            Assert.Contains("Operations:", verboseShow.Output);
            Assert.Contains("cmd.exe /c echo hello {workspace:docs.location}", verboseShow.Output);

            var run = RunCli($"run --workspace \"{meshPath}\" --operation refresh-docs");
            Assert.Equal(0, run.ExitCode);
            Assert.Contains("Operation: refresh-docs", run.Output);
            Assert.Contains("  echo", run.Output);
            Assert.DoesNotContain("exit-code", run.Output);
            Assert.DoesNotContain("elapsed", run.Output);
            Assert.Contains("hello", run.Output);
            Assert.Contains("1 step completed.", run.Output);

            var verboseRun = RunCli($"run --workspace \"{meshPath}\" --operation refresh-docs --verbose");
            Assert.Equal(0, verboseRun.ExitCode);
            Assert.Contains("Operation: refresh-docs", verboseRun.Output);
            Assert.Contains("hello", verboseRun.Output);
            Assert.DoesNotContain("      hello", verboseRun.Output);
            Assert.Contains("1 step completed.", verboseRun.Output);

            Assert.True(File.Exists(Path.Combine(meshPath, "instances", "Workspace.xml")));
            Assert.True(File.Exists(Path.Combine(meshPath, "instances", "Operation.xml")));
            Assert.True(File.Exists(Path.Combine(meshPath, "instances", "OperationStep.xml")));

            var model = Meta.Surfaces.Xml.TypedWorkspaceXmlSerializer.Load<MetaMesh.MetaMeshModel>(meshPath, searchUpward: false);
            Assert.Equal("Docs", Assert.Single(model.MeshList).Name);
            Assert.Contains(model.WorkspaceList, item => item.Name == "docs" && item.ModelName == "MetaDocs");
            Assert.Contains(model.XmlWorkspaceList, item => item.Workspace.Name == "docs" && item.Path == ".");
            Assert.Contains(model.OperationList, item => item.Name == "refresh-docs");
            Assert.Contains(model.OperationStepList, item => item.Name == "echo" && item.Executable == "cmd.exe");
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public void Authoring_CreatesMutatesAndReadsCSharpMeshWorkspace()
    {
        var root = Path.Combine(Path.GetTempPath(), "metamesh-cli-csharp", Guid.NewGuid().ToString("N"));
        var meshPath = Path.Combine(root, "Docs.MetaMesh");
        try
        {
            var create = RunCli(
                $"create --csharp \"{meshPath}\" --name Docs --root .");
            Assert.Equal(0, create.ExitCode);

            var workspace = RunCli(
                $"add-workspace --workspace \"{meshPath}\" --name self --csharp-path . --model MetaMesh");
            Assert.Equal(0, workspace.ExitCode);

            var add = RunCli(
                $"add-operation --workspace \"{meshPath}\" --name refresh-docs --description \"Refresh docs\"");
            Assert.Equal(0, add.ExitCode);

            var step = RunCli(
                $"add-step --workspace \"{meshPath}\" --operation refresh-docs --name record-workspace --executable cmd.exe --arguments \"/c echo {{workspace:self.surface}} {{workspace:self.location}}>workspace.txt\"");
            Assert.Equal(0, step.ExitCode);

            var operations = RunCli(
                $"operations --workspace \"{meshPath}\"");
            Assert.Equal(0, operations.ExitCode);
            Assert.Contains("refresh-docs", operations.Output);
            Assert.Contains("1 step", operations.Output);

            var workspaces = RunCli(
                $"workspaces --workspace \"{meshPath}\"");
            Assert.Equal(0, workspaces.ExitCode);
            Assert.Contains("self (MetaMesh) - ok", workspaces.Output);
            Assert.Contains("C# workspace", workspaces.Output);

            var validate = RunCli(
                $"validate --workspace \"{meshPath}\" --operation refresh-docs");
            Assert.Equal(0, validate.ExitCode);

            var run = RunCli(
                $"run --workspace \"{meshPath}\" --operation refresh-docs");
            Assert.Equal(0, run.ExitCode);
            var recorded = File.ReadAllText(Path.Combine(meshPath, "workspace.txt"));
            Assert.Contains("csharp", recorded);
            Assert.Contains(meshPath, recorded, StringComparison.OrdinalIgnoreCase);

            Assert.True(File.Exists(Path.Combine(meshPath, "MetaMesh.meta.cs")));
            Assert.Equal(
                ["MetaMesh.meta.cs"],
                Directory.EnumerateFiles(meshPath, "*.cs")
                    .Select(path => Path.GetFileName(path) ?? string.Empty)
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToArray());
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public void Validation_UsesCurrentDirectoryForNonFileSystemMeshWorkspace()
    {
        var root = Path.Combine(Path.GetTempPath(), "metamesh-sql-context", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var service = new MetaMesh.Core.MetaMeshWorkspaceService();
            var model = service.CreateEmpty("SqlMesh", null, null);
            service.AddOperation(model, "echo", null);
            service.AddStep(model, "echo", "say-hello", "cmd.exe", "/c echo hello", null, null, null, null);

            var result = service.ValidateOperation(
                model,
                "echo",
                new MetaMesh.Core.MetaMeshWorkspaceContext(
                    "META_MESH_TEST_SQL",
                    WorkspaceDirectory: null,
                    CurrentDirectory: root));

            Assert.Equal("echo", result.OperationName);
            Assert.Single(result.Steps);
            Assert.Equal(root, result.Steps[0].WorkingDirectory);
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public void Show_ReportsMissingSqlWorkspaceEnvironmentWithoutOpeningAConnection()
    {
        var root = Path.Combine(Path.GetTempPath(), "metamesh-sql-child", Guid.NewGuid().ToString("N"));
        var environmentVariable = "META_MESH_TEST_" + Guid.NewGuid().ToString("N").ToUpperInvariant();
        Directory.CreateDirectory(root);
        try
        {
            var service = new MetaMesh.Core.MetaMeshWorkspaceService();
            var model = service.CreateEmpty("SqlChild", null, null);
            var mesh = Assert.Single(model.MeshList);
            var workspace = new global::MetaMesh.Workspace
            {
                Id = "workspace:sql",
                Mesh = mesh,
                Name = "sql"
            };
            model.WorkspaceList.Add(workspace);
            model.SqlWorkspaceList.Add(new global::MetaMesh.SqlWorkspace
            {
                Id = "sql-workspace:sql",
                Workspace = workspace,
                ConnectionEnvironmentVariable = environmentVariable
            });

            var result = service.Show(
                model,
                new MetaMesh.Core.MetaMeshWorkspaceContext(root, root, root));

            var summary = Assert.Single(result.Workspaces);
            Assert.Equal("sql", summary.Surface);
            Assert.Equal(environmentVariable, summary.Location);
            var issue = Assert.Single(result.WorkspaceIssues);
            Assert.Contains(environmentVariable, issue.Reason);
            Assert.DoesNotContain("Server=", issue.Reason, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public void Show_RejectsWorkspaceWithoutExactlyOneRepresentation()
    {
        var root = Path.Combine(Path.GetTempPath(), "metamesh-missing-representation", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var service = new MetaMesh.Core.MetaMeshWorkspaceService();
            var model = service.CreateEmpty("MissingRepresentation", null, null);
            model.WorkspaceList.Add(new global::MetaMesh.Workspace
            {
                Id = "workspace:missing",
                Mesh = Assert.Single(model.MeshList),
                Name = "missing"
            });

            var exception = Assert.Throws<InvalidOperationException>(() => service.Show(
                model,
                new MetaMesh.Core.MetaMeshWorkspaceContext(root, root, root)));

            Assert.Contains("must have exactly one XML, C#, or SQL representation; found 0", exception.Message);
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public void Validation_RejectsSqlWorkspaceAsWorkingDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "metamesh-sql-working-directory", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var service = new MetaMesh.Core.MetaMeshWorkspaceService();
            var model = service.CreateEmpty("SqlWorkingDirectory", null, null);
            var mesh = Assert.Single(model.MeshList);
            var workspace = new global::MetaMesh.Workspace
            {
                Id = "workspace:sql",
                Mesh = mesh,
                Name = "sql"
            };
            model.WorkspaceList.Add(workspace);
            model.SqlWorkspaceList.Add(new global::MetaMesh.SqlWorkspace
            {
                Id = "sql-workspace:sql",
                Workspace = workspace,
                ConnectionEnvironmentVariable = "META_MESH_SQL_WORKING_DIRECTORY"
            });
            service.AddOperation(model, "run", null);
            service.AddStep(
                model,
                "run",
                "invalid-working-directory",
                "cmd.exe",
                null,
                "{workspace:sql.location}",
                null,
                null,
                null);

            var exception = Assert.Throws<InvalidOperationException>(() => service.ValidateOperation(
                model,
                "run",
                new MetaMesh.Core.MetaMeshWorkspaceContext(root, root, root)));

            Assert.Contains("SQL workspace 'sql' cannot be used as an operation working directory", exception.Message);
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public void AddStep_CanReadArgumentsFromStandardInput()
    {
        var root = Path.Combine(Path.GetTempPath(), "metamesh-cli-stdin-arguments", Guid.NewGuid().ToString("N"));
        var meshPath = Path.Combine(root, "Docs.MetaMesh");
        const string childArguments = "/c echo \"hello quoted child cli\"";
        try
        {
            Assert.Equal(0, RunCli($"create --xml \"{meshPath}\" --name Docs --root .").ExitCode);
            Assert.Equal(0, RunCli($"add-operation --workspace \"{meshPath}\" --name stdin-arguments").ExitCode);

            var add = RunCli(
                $"add-step --workspace \"{meshPath}\" --operation stdin-arguments --name echo --executable cmd.exe --arguments-stdin",
                standardInput: "\u00EF\u00BB\u00BF" + childArguments + Environment.NewLine);

            Assert.Equal(0, add.ExitCode);

            var steps = RunCli($"steps --workspace \"{meshPath}\" --operation stdin-arguments");
            Assert.Equal(0, steps.ExitCode);
            Assert.Contains("cmd.exe " + childArguments, steps.Output);

            var model = Meta.Surfaces.Xml.TypedWorkspaceXmlSerializer.Load<MetaMesh.MetaMeshModel>(meshPath, searchUpward: false);
            Assert.Equal(childArguments, Assert.Single(model.OperationStepList).Arguments);
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public void Run_TreatsModeledExpectedExitCodeAsSuccess()
    {
        var root = Path.Combine(Path.GetTempPath(), "metamesh-cli-expected-exit", Guid.NewGuid().ToString("N"));
        var meshPath = Path.Combine(root, "Docs.MetaMesh");
        try
        {
            Assert.Equal(0, RunCli($"create --xml \"{meshPath}\" --name Docs --root .").ExitCode);
            Assert.Equal(0, RunCli($"add-workspace --workspace \"{meshPath}\" --name docs --xml-path . --model MetaDocs").ExitCode);
            Assert.Equal(0, RunCli($"add-operation --workspace \"{meshPath}\" --name expected-exit").ExitCode);
            Assert.Equal(
                0,
                RunCli($"add-step --workspace \"{meshPath}\" --operation expected-exit --name expected-four --executable cmd.exe --arguments \"/c exit 4\" --expected-exit-code 4").ExitCode);

            var steps = RunCli($"steps --workspace \"{meshPath}\" --operation expected-exit");
            Assert.Equal(0, steps.ExitCode);
            Assert.Contains("expects exit code 4", steps.Output);

            var run = RunCli($"run --workspace \"{meshPath}\" --operation expected-exit");
            Assert.Equal(0, run.ExitCode);
            Assert.Contains("1 step completed.", run.Output);
            Assert.DoesNotContain("Failed step", run.Output);

            var model = Meta.Surfaces.Xml.TypedWorkspaceXmlSerializer.Load<MetaMesh.MetaMeshModel>(meshPath, searchUpward: false);
            Assert.Equal("4", Assert.Single(model.OperationStepList).ExpectedExitCode);
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public void Run_PreflightsEveryStepBeforeExecuting()
    {
        var root = Path.Combine(Path.GetTempPath(), "metamesh-cli-preflight", Guid.NewGuid().ToString("N"));
        var meshPath = Path.Combine(root, "Docs.MetaMesh");
        var markerPath = Path.Combine(meshPath, "marker.txt");
        try
        {
            Assert.Equal(0, RunCli($"create --xml \"{meshPath}\" --name Docs --root .").ExitCode);
            Assert.Equal(0, RunCli($"add-workspace --workspace \"{meshPath}\" --name docs --xml-path . --model MetaDocs").ExitCode);
            Assert.Equal(0, RunCli($"add-operation --workspace \"{meshPath}\" --name preflight").ExitCode);
            Assert.Equal(0, RunCli($"add-step --workspace \"{meshPath}\" --operation preflight --name touch --executable cmd.exe --arguments \"/c echo touched>marker.txt\"").ExitCode);
            Assert.Equal(0, RunCli($"add-step --workspace \"{meshPath}\" --operation preflight --name missing --executable definitely-not-a-metamesh-executable --previous-step touch").ExitCode);

            var validate = RunCli($"validate --workspace \"{meshPath}\" --operation preflight");

            Assert.NotEqual(0, validate.ExitCode);
            Assert.Contains("Executable 'definitely-not-a-metamesh-executable' for step 'missing' was not found.", validate.Output);
            Assert.False(File.Exists(markerPath));

            var run = RunCli($"run --workspace \"{meshPath}\" --operation preflight");

            Assert.NotEqual(0, run.ExitCode);
            Assert.Contains("Executable 'definitely-not-a-metamesh-executable' for step 'missing' was not found.", run.Output);
            Assert.False(File.Exists(markerPath));
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public void Run_RequiresEnvironmentTokensToResolveBeforeExecuting()
    {
        const string variableName = "METAMESH_TEST_EMPTY_ENV";
        var previousValue = Environment.GetEnvironmentVariable(variableName);
        var root = Path.Combine(Path.GetTempPath(), "metamesh-cli-env", Guid.NewGuid().ToString("N"));
        var meshPath = Path.Combine(root, "Docs.MetaMesh");
        try
        {
            Environment.SetEnvironmentVariable(variableName, null);

            Assert.Equal(0, RunCli($"create --xml \"{meshPath}\" --name Docs --root .").ExitCode);
            Assert.Equal(0, RunCli($"add-workspace --workspace \"{meshPath}\" --name docs --xml-path . --model MetaDocs").ExitCode);
            Assert.Equal(0, RunCli($"add-operation --workspace \"{meshPath}\" --name env").ExitCode);
            Assert.Equal(0, RunCli($"add-step --workspace \"{meshPath}\" --operation env --name echo --executable cmd.exe --arguments \"/c echo {{env:{variableName}}}\"").ExitCode);

            var run = RunCli($"run --workspace \"{meshPath}\" --operation env");

            Assert.NotEqual(0, run.ExitCode);
            Assert.Contains($"Environment variable '{variableName}' is not set or empty.", run.Output);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variableName, previousValue);
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public void Run_EnvironmentTokensValidateAndExpandToVariableName()
    {
        const string variableName = "METAMESH_TEST_ENV_NAME";
        const string secretValue = "metamesh-secret-value";
        var previousValue = Environment.GetEnvironmentVariable(variableName);
        var root = Path.Combine(Path.GetTempPath(), "metamesh-cli-env-name", Guid.NewGuid().ToString("N"));
        var meshPath = Path.Combine(root, "Docs.MetaMesh");
        try
        {
            Environment.SetEnvironmentVariable(variableName, secretValue);

            Assert.Equal(0, RunCli($"create --xml \"{meshPath}\" --name Docs --root .").ExitCode);
            Assert.Equal(0, RunCli($"add-workspace --workspace \"{meshPath}\" --name docs --xml-path . --model MetaDocs").ExitCode);
            Assert.Equal(0, RunCli($"add-operation --workspace \"{meshPath}\" --name env").ExitCode);
            Assert.Equal(0, RunCli($"add-step --workspace \"{meshPath}\" --operation env --name echo --executable cmd.exe --arguments \"/c echo {{env:{variableName}}}\"").ExitCode);

            var run = RunCli($"run --workspace \"{meshPath}\" --operation env");

            Assert.Equal(0, run.ExitCode);
            Assert.Contains(variableName, run.Output);
            Assert.DoesNotContain(secretValue, run.Output);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variableName, previousValue);
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public void Show_ReportsAllMissingDeclaredWorkspaces_AndRunAllowsUnusedMissingWorkspaces()
    {
        var root = Path.Combine(Path.GetTempPath(), "metamesh-cli-missing-workspaces", Guid.NewGuid().ToString("N"));
        var meshPath = Path.Combine(root, "Docs.MetaMesh");
        var markerPath = Path.Combine(meshPath, "marker.txt");
        try
        {
            Assert.Equal(0, RunCli($"create --xml \"{meshPath}\" --name Docs --root .").ExitCode);
            Assert.Equal(0, RunCli($"add-workspace --workspace \"{meshPath}\" --name docs --xml-path MissingDocs --model MetaDocs").ExitCode);
            Assert.Equal(0, RunCli($"add-workspace --workspace \"{meshPath}\" --name pipeline --xml-path MissingPipeline --model MetaPipeline").ExitCode);
            Assert.Equal(0, RunCli($"add-operation --workspace \"{meshPath}\" --name preflight").ExitCode);
            Assert.Equal(0, RunCli($"add-step --workspace \"{meshPath}\" --operation preflight --name touch --executable cmd.exe --arguments \"/c echo touched>marker.txt\"").ExitCode);

            var show = RunCli($"show --workspace \"{meshPath}\"");

            Assert.Equal(0, show.ExitCode);
            Assert.Contains("Workspaces: 2 (2 missing)", show.Output);
            Assert.Contains("Run `meta-mesh workspaces`", show.Output);
            Assert.DoesNotContain("directory does not exist", show.Output);

            var workspaces = RunCli($"workspaces --workspace \"{meshPath}\"");
            Assert.Equal(0, workspaces.ExitCode);
            Assert.Contains("docs (MetaDocs) - missing", workspaces.Output);
            Assert.Contains("pipeline (MetaPipeline) - missing", workspaces.Output);
            Assert.Contains("directory does not exist", workspaces.Output);

            var run = RunCli($"run --workspace \"{meshPath}\" --operation preflight");

            Assert.Equal(0, run.ExitCode);
            Assert.Contains("1 step completed.", run.Output);
            Assert.True(File.Exists(markerPath));
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public void Run_PreflightsMissingWorkspaceUsedAsWorkingDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "metamesh-cli-missing-operation-workspace", Guid.NewGuid().ToString("N"));
        var meshPath = Path.Combine(root, "Docs.MetaMesh");
        var markerPath = Path.Combine(meshPath, "marker.txt");
        try
        {
            Assert.Equal(0, RunCli($"create --xml \"{meshPath}\" --name Docs --root .").ExitCode);
            Assert.Equal(0, RunCli($"add-workspace --workspace \"{meshPath}\" --name docs --xml-path MissingDocs --model MetaDocs").ExitCode);
            Assert.Equal(0, RunCli($"add-operation --workspace \"{meshPath}\" --name preflight").ExitCode);
            Assert.Equal(0, RunCli($"add-step --workspace \"{meshPath}\" --operation preflight --name touch --executable cmd.exe --arguments \"/c echo touched>marker.txt\"").ExitCode);
            Assert.Equal(0, RunCli($"add-step --workspace \"{meshPath}\" --operation preflight --name missing-workspace --executable cmd.exe --arguments \"/c echo unreachable\" --working-directory \"{{workspace:docs.location}}\" --previous-step touch").ExitCode);

            var run = RunCli($"run --workspace \"{meshPath}\" --operation preflight");

            Assert.NotEqual(0, run.ExitCode);
            Assert.Contains("Missing workspaces:", run.Output);
            Assert.Contains("docs", run.Output);
            Assert.Contains("Operation uses missing or invalid workspaces.", run.Output);
            Assert.False(File.Exists(markerPath));
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public void Show_DefaultsWorkspaceToCurrentDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "metamesh-cli-cwd", Guid.NewGuid().ToString("N"));
        var meshPath = Path.Combine(root, "Mesh");
        try
        {
            var create = RunCli($"create --xml \"{meshPath}\" --name CurrentDirectoryMesh");
            Assert.Equal(0, create.ExitCode);

            var show = RunCli("show", workingDirectory: meshPath);

            Assert.Equal(0, show.ExitCode);
            Assert.Contains("CurrentDirectoryMesh", show.Output);
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
        var repoRoot = FindRepositoryRoot();
        var startInfo = new ProcessStartInfo
        {
            FileName = CliTestHost.DotNetHost,
            Arguments = CliTestHost.BuildArguments("meta-mesh", arguments),
            WorkingDirectory = workingDirectory ?? repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException("Could not start meta-mesh CLI process.");
        if (standardInput is not null)
        {
            process.StandardInput.Write(standardInput);
        }

        process.StandardInput.Close();
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
            throw new TimeoutException($"Timed out waiting for process: {startInfo.FileName} {startInfo.Arguments}", exception);
        }

        return (process.ExitCode, stdoutTask.GetAwaiter().GetResult() + stderrTask.GetAwaiter().GetResult());
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

        throw new InvalidOperationException("Could not locate repository root from test base directory.");
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

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
