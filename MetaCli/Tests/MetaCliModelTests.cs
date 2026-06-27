using System.Diagnostics;
using System.Xml.Linq;
using MetaCli.Core;

namespace MetaCli.Tests;

public sealed class MetaCliModelTests
{
    [Fact]
    public void Model_UsesAcceptedEntitySurface()
    {
        var modelPath = Path.Combine(FindRepositoryRoot(), "MetaCli", "Workspace", "model.xml");
        var document = XDocument.Load(modelPath);
        var entityNames = document
            .Descendants("Entity")
            .Select(element => (string?)element.Attribute("name") ?? string.Empty)
            .ToArray();
        var propertyNames = document
            .Descendants("Property")
            .Select(element => (string?)element.Attribute("name") ?? string.Empty)
            .ToArray();

        Assert.Contains("Application", entityNames);
        Assert.Contains("Command", entityNames);
        Assert.Contains("ExecutableCommand", entityNames);
        Assert.Contains("ApplicationDefaultCommand", entityNames);
        Assert.Contains("ApplicationParameter", entityNames);
        Assert.Contains("ExecutableCommandParameter", entityNames);
        Assert.Contains("Parameter", entityNames);
        Assert.Contains("Option", entityNames);
        Assert.Contains("OptionToken", entityNames);
        Assert.Contains("PositionalArgument", entityNames);
        Assert.Contains("ParameterGroup", entityNames);
        Assert.Contains("ParameterGroupMember", entityNames);
        Assert.DoesNotContain("CommandSegment", entityNames);
        Assert.DoesNotContain("CommandKind", entityNames);
        Assert.DoesNotContain("CommandGroup", entityNames);
        Assert.DoesNotContain("ApplicationRootCommand", entityNames);
        Assert.DoesNotContain("ValueCodec", entityNames);
        Assert.DoesNotContain("ParserPolicy", entityNames);
        Assert.DoesNotContain("DuplicateOptionBehavior", entityNames);
        Assert.DoesNotContain("UnknownTokenBehavior", entityNames);
        Assert.DoesNotContain("Output", entityNames);
        Assert.DoesNotContain("ExitCode", entityNames);
        Assert.DoesNotContain(entityNames, name => name.StartsWith("Cli", StringComparison.Ordinal));
        Assert.DoesNotContain(propertyNames, name => string.Equals(name, "Ordinal", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propertyNames, name => string.Equals(name, "Order", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propertyNames, name => string.Equals(name, "Kind", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Cli_HelpShowsCurrentAuthoringSurfaceAndDoesNotExposeDeletedConcepts()
    {
        var result = RunCli("help");
        var newWorkspace = RunCli("new-workspace --help");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(0, newWorkspace.ExitCode);
        Assert.Contains("meta-cli <command> [options]", result.Output);
        Assert.Contains("new-workspace", result.Output);
        Assert.Contains("--standard-cli-shapes", newWorkspace.Output);
        Assert.Contains("--default-help", newWorkspace.Output);
        Assert.DoesNotContain("from-syntax", result.Output);
        Assert.Contains("add-application-option", result.Output);
        Assert.Contains("add-option-token", result.Output);
        Assert.Contains("add-parameter-group-member", result.Output);
        Assert.DoesNotContain("add-root-command", result.Output);
        Assert.DoesNotContain("add-duplicate-option-behavior", result.Output);
        Assert.DoesNotContain("add-unknown-token-behavior", result.Output);
        Assert.DoesNotContain("add-parser-policy", result.Output);
        Assert.DoesNotContain("add-output", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("add-exit", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("init", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("check", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Cli_HelpIsDerivedFromAuthoredMetaCliWorkspace()
    {
        var result = RunCli("help add-option");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("meta-cli add-option", result.Output);
        Assert.Contains("--parameter-id <value>", result.Output);
        Assert.Contains("--token <token>", result.Output);

        var source = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "MetaCli", "Cli", "Program.cs"));
        Assert.Contains("MetaCliRuntime<MetaCliModel>", source);
        Assert.Contains(".UseDefaultHelp()", source);
        Assert.DoesNotContain("ParseOptions", source);
        Assert.DoesNotContain("PrintCommandHelp", source);
        Assert.DoesNotContain("WriteUsageAndOptions", source);
        Assert.DoesNotContain("WriteCommandCatalog", source);
    }

    [Fact]
    public void Cli_AuthoredCommandSurfacePreservesProviderIntegrity()
    {
        var workspace = Path.Combine(FindRepositoryRoot(), "MetaCli", "Cli", "meta-cli.MetaCli");

        AssertWorkspacePreservesProviderIntegrity(workspace);

        var newWorkspaceHelp = RunCli("new-workspace --help");
        Assert.Equal(0, newWorkspaceHelp.ExitCode);
        Assert.DoesNotContain("--workspace <path>", newWorkspaceHelp.Output);

        var showHelp = RunCli("show --help");
        Assert.Equal(0, showHelp.ExitCode);
        Assert.Contains("--workspace <path>", showHelp.Output);
    }

    [Fact]
    public void Cli_NewWorkspaceCanSeedStandardCliDefaults()
    {
        var root = Path.Combine(Path.GetTempPath(), "meta-cli-seed-", Guid.NewGuid().ToString("N"));
        var workspace = Path.Combine(root, "Demo.MetaCli");

        try
        {
            var result = RunCli($"new-workspace \"{workspace}\" --application demo --standard-cli-shapes --default-help");

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("Created MetaCli workspace", result.Output);
            Assert.Contains("applications: 1", result.Output);
            Assert.Contains("value shapes: 5", result.Output);
            Assert.Contains("commands: 1 (1 runnable)", result.Output);
            AssertWorkspacePreservesProviderIntegrity(workspace);

            var model = MetaCliModel.LoadFromXmlWorkspace(workspace, searchUpward: false);
            Assert.Contains(model.ApplicationList, application => application.Id == "app-demo" && application.ExecutableName == "demo");
            Assert.Contains(model.ValueArityList, arity => arity.Id == "arity-none" && arity.MinValueCount == "0" && arity.MaxValueCount == "0");
            Assert.Contains(model.ValueArityList, arity => arity.Id == "arity-one" && arity.MinValueCount == "1" && arity.MaxValueCount == "1");
            Assert.Contains(model.ValueShapeList, shape => shape.Id == "shape-flag" && shape.ValueArity.Id == "arity-none");
            Assert.Contains(model.ValueShapeList, shape => shape.Id == "shape-path" && shape.AllowsOptionLikeValue == "true");
            Assert.Contains(model.ValueShapeList, shape => shape.Id == "shape-text");
            Assert.Contains(model.ValueShapeList, shape => shape.Id == "shape-token" && shape.AllowsOptionLikeValue == "true");
            Assert.Contains(model.ValueShapeList, shape => shape.Id == "shape-bool");
            Assert.Contains(model.AllowedValueList, value => value.Value == "true" && value.ValueShape.Id == "shape-bool");
            Assert.Contains(model.AllowedValueList, value => value.Value == "false" && value.ValueShape.Id == "shape-bool");
            Assert.Contains(model.CommandList, command => command.Id == "cmd-help" && command.Token == "help");
            Assert.Contains(model.ApplicationDefaultCommandList, row => row.Application.Id == "app-demo" && row.ExecutableCommand.Id == "exec-help");

            var exitCode = -1;
            var error = new StringWriter();
            MetaCliInvocation? invocation = null;
            var runtime = new MetaCliRuntime<MetaCliModel>(
                    workspace,
                    error: error,
                    setExitCode: code => exitCode = code)
                .Bind("exec-help", command => invocation = command);

            runtime.Run();

            Assert.Equal(0, exitCode);
            Assert.Equal(string.Empty, error.ToString());
            Assert.NotNull(invocation);
            Assert.Equal("help", invocation.CommandRoute);
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public void Cli_AuthoringCoverageTouchesEveryAcceptedEntity()
    {
        var coverage = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Application"] = "add-application",
            ["Command"] = "add-command",
            ["ExecutableCommand"] = "add-executable-command",
            ["ApplicationDefaultCommand"] = "set-default-command",
            ["ValueArity"] = "add-value-arity",
            ["ValueShape"] = "add-value-shape",
            ["AllowedValue"] = "add-allowed-value",
            ["Parameter"] = "created by add-application-option, add-option, and add-positional",
            ["ApplicationParameter"] = "add-application-option",
            ["ExecutableCommandParameter"] = "add-option and add-positional",
            ["Option"] = "add-application-option and add-option",
            ["OptionToken"] = "add-option-token",
            ["ParameterGroup"] = "add-parameter-group",
            ["ParameterGroupMember"] = "add-parameter-group-member",
            ["PositionalArgument"] = "add-positional",
        };
        var modelPath = Path.Combine(FindRepositoryRoot(), "MetaCli", "Workspace", "model.xml");
        var entityNames = XDocument.Load(modelPath)
            .Descendants("Entity")
            .Select(element => (string?)element.Attribute("name") ?? string.Empty)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(entityNames, coverage.Keys.OrderBy(static name => name, StringComparer.Ordinal).ToArray());
        Assert.DoesNotContain(coverage.Values, value => string.Equals(value, "uncovered", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Cli_PublicMutatingCommandsPreserveProviderIntegrityAndCreateDemoWorkspace()
    {
        var root = Path.Combine(Path.GetTempPath(), "meta-cli-cli-", Guid.NewGuid().ToString("N"));
        var workspace = Path.Combine(root, "Demo.MetaCli");
        try
        {
            AssertCommandSucceeds($"new-workspace \"{workspace}\" --application demo --standard-cli-shapes --default-help");
            AssertWorkspacePreservesProviderIntegrity(workspace);

            AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-value-shape --id shape-visibility --name Visibility --value-arity arity-one --value-label \"<visibility>\"", workspace);
            AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-allowed-value --id visibility-public --value-shape shape-visibility --value public", workspace);
            AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-allowed-value --id visibility-internal --value-shape shape-visibility --value internal", workspace);

            AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-application-option --parameter-id param-workspace --option-id option-workspace --application app-demo --name workspace --value-shape shape-path --token-id token-workspace --token --workspace", workspace);
            AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-option-token --id token-workspace-short --option option-workspace --token -w --previous-token token-workspace", workspace);

            AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-command --id cmd-new-workspace --application app-demo --name new-workspace --token new-workspace", workspace);
            AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-executable-command --id exec-new-workspace --command cmd-new-workspace", workspace);
            AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-positional --parameter-id param-new-workspace --positional-id pos-new-workspace --executable-command exec-new-workspace --name Path --value-shape shape-path --required true", workspace);

            AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-command --id cmd-model --application app-demo --name model --token model", workspace);
            AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-command --id cmd-add-property --application app-demo --name add-property --token add-property --parent-command cmd-model", workspace);
            AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-executable-command --id exec-add-property --command cmd-add-property", workspace);
            AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-option --parameter-id param-visibility --option-id option-visibility --executable-command exec-add-property --name visibility --value-shape shape-visibility --token-id token-visibility --token --visibility", workspace);
            AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-positional --parameter-id param-entity --positional-id pos-entity --executable-command exec-add-property --name Entity --value-shape shape-text --required true", workspace);
            AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-positional --parameter-id param-property --positional-id pos-property --executable-command exec-add-property --name Property --value-shape shape-text --previous-argument pos-entity --required true", workspace);

            AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-command --id cmd-add-entity --application app-demo --name add-entity --token add-entity --parent-command cmd-model", workspace);
            AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-executable-command --id exec-add-entity --command cmd-add-entity", workspace);
            AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-positional --parameter-id param-id --positional-id pos-id --executable-command exec-add-entity --name Id --value-shape shape-text", workspace);
            AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-option --parameter-id param-auto-id --option-id option-auto-id --executable-command exec-add-entity --name auto-id --value-shape shape-flag --token-id token-auto-id --token --auto-id", workspace);
            AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-option-token --id token-auto-id-short --option option-auto-id --token -a --previous-token token-auto-id", workspace);
            AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-parameter-group --id group-id-choice --executable-command exec-add-entity --name IdChoice --member-id group-id-choice-id --parameter param-id --required true", workspace);
            AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-parameter-group-member --id group-id-choice-auto --parameter-group group-id-choice --parameter param-auto-id --previous-member group-id-choice-id", workspace);

            var show = RunCli("show", workspace);

            Assert.Equal(0, show.ExitCode);
            Assert.Contains("MetaCli workspace", show.Output);
            Assert.Contains("application: demo (app-demo)", show.Output);
            Assert.Contains("command surface:", show.Output);
            Assert.Contains("new-workspace [runnable]", show.Output);
            Assert.Contains("help [default, runnable]", show.Output);
            Assert.Contains("model add-property [runnable]", show.Output);
            Assert.DoesNotContain("Application:", show.Output);
            Assert.DoesNotContain("ExecutableCommand:", show.Output);

            var model = MetaCliModel.LoadFromXmlWorkspace(workspace, searchUpward: false);
            var addProperty = model.CommandList.Single(command => command.Id == "cmd-add-property");
            Assert.Equal("model add-property", MetaCliWorkspaceService.BuildRoute(addProperty));
            Assert.Equal("exec-add-property", model.ExecutableCommandList.Single(command => ReferenceEquals(command.Command, addProperty)).Id);
            Assert.Contains(model.ApplicationParameterList, scoped => scoped.Parameter.Id == "param-workspace");
            Assert.Contains(model.ExecutableCommandParameterList, scoped => scoped.Parameter.Id == "param-visibility" && scoped.ExecutableCommand.Id == "exec-add-property");
            Assert.Contains(model.OptionTokenList, token => token.Id == "token-workspace" && token.Option.Id == "option-workspace" && token.PreviousToken is null);
            Assert.Contains(model.OptionTokenList, token => token.Id == "token-workspace-short" && token.Option.Id == "option-workspace" && token.PreviousToken?.Id == "token-workspace");
            Assert.Same(
                model.PositionalArgumentList.Single(argument => argument.Id == "pos-entity"),
                model.PositionalArgumentList.Single(argument => argument.Id == "pos-property").PreviousArgument);
            Assert.Same(
                model.ParameterGroupMemberList.Single(member => member.Id == "group-id-choice-id"),
                model.ParameterGroupMemberList.Single(member => member.Id == "group-id-choice-auto").PreviousMember);
            Assert.DoesNotContain(model.GetType().GetProperties(), property => property.Name.Contains("ParserPolicy", StringComparison.Ordinal));
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public void Cli_AddOptionRequiresFirstTokenAggregateAndDoesNotPersistPartialRows()
    {
        var root = Path.Combine(Path.GetTempPath(), "meta-cli-cli-", Guid.NewGuid().ToString("N"));
        var workspace = Path.Combine(root, "Partial.MetaCli");
        try
        {
            CreateMinimalExecutableCommand(workspace);

            var addOption = RunCli("add-option --parameter-id param-workspace --option-id option-workspace --executable-command exec-show --name workspace --value-shape shape-path", workspace);

            Assert.Equal(2, addOption.ExitCode);
            Assert.Contains("Required parameter 'token-id' was not provided.", addOption.Output);

            AssertWorkspacePreservesProviderIntegrity(workspace);
            var model = MetaCliModel.LoadFromXmlWorkspace(workspace, searchUpward: false);
            Assert.Empty(model.OptionList);
            Assert.Empty(model.OptionTokenList);
            Assert.DoesNotContain(model.ParameterList, parameter => parameter.Id == "param-workspace");
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public void Cli_DuplicateIdFailsWithoutWritingSecondRow()
    {
        var root = Path.Combine(Path.GetTempPath(), "meta-cli-cli-", Guid.NewGuid().ToString("N"));
        var workspace = Path.Combine(root, "Duplicate.MetaCli");
        try
        {
            AssertCommandSucceeds($"new-workspace \"{workspace}\"");
            AssertCommandSucceeds("add-application --id app-demo --name demo", workspace);

            var duplicate = RunCli("add-application --id app-demo --name demo2", workspace);

            Assert.Equal(4, duplicate.ExitCode);
            Assert.Contains("already exists", duplicate.Output);
            var model = MetaCliModel.LoadFromXmlWorkspace(workspace, searchUpward: false);
            Assert.Single(model.ApplicationList);
            Assert.Equal("demo", model.ApplicationList[0].Name);
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public void Validation_CatchesStrictSafeguards()
    {
        var service = new MetaCliWorkspaceService();
        var model = service.CreateEmpty();
        var app = new Application { Id = "app", Name = "demo" };
        model.ApplicationList.Add(app);
        var command = new Command { Id = "cmd", Application = app, Name = "add", Token = "add" };
        model.CommandList.Add(command);
        var executable = new ExecutableCommand { Id = "exec", Command = command };
        model.ExecutableCommandList.Add(executable);
        var arity = new ValueArity { Id = "arity", Name = "One", MinValueCount = "01", MaxValueCount = "1" };
        model.ValueArityList.Add(arity);
        var shape = new ValueShape { Id = "shape", Name = "Text", ValueArity = arity };
        model.ValueShapeList.Add(shape);
        var optionParameter = AddParameter(model, executable, "param-option", "workspace", shape);
        var option = new Option { Id = "option", Parameter = optionParameter };
        model.OptionList.Add(option);
        var token1 = new OptionToken { Id = "token-1", Option = option, Token = "--workspace" };
        var token2 = new OptionToken { Id = "token-2", Option = option, Token = "-w", PreviousToken = token1 };
        var token3 = new OptionToken { Id = "token-3", Option = option, Token = "--workspace-alias", PreviousToken = token1 };
        model.OptionTokenList.Add(token1);
        model.OptionTokenList.Add(token2);
        model.OptionTokenList.Add(token3);
        var positionalParameter1 = AddParameter(model, executable, "param-entity", "Entity", shape);
        var positionalParameter2 = AddParameter(model, executable, "param-property", "Property", shape, required: true);
        var positional1 = new PositionalArgument { Id = "pos-entity", Parameter = positionalParameter1 };
        var positional2 = new PositionalArgument { Id = "pos-property", Parameter = positionalParameter2, PreviousArgument = positional1 };
        model.PositionalArgumentList.Add(positional1);
        model.PositionalArgumentList.Add(positional2);
        var duplicateOption = new Option { Id = "option-duplicate", Parameter = optionParameter };
        model.OptionList.Add(duplicateOption);
        model.ParameterGroupList.Add(new ParameterGroup { Id = "group-empty", ExecutableCommand = executable, Name = "Empty", IsRequired = "true" });
        model.ParameterList.Add(new Parameter { Id = "param-unscoped", ValueShape = shape, Name = "orphan" });

        var integrity = service.ValidateIntegrity(model);

        Assert.True(integrity.HasErrors);
        Assert.Contains(integrity.Issues, issue => issue.Code == "MCLI040");
        Assert.Contains(integrity.Issues, issue => issue.Code == "MCLI051");
        Assert.Contains(integrity.Issues, issue => issue.Code == "MCLI055");
        Assert.Contains(integrity.Issues, issue => issue.Code == "MCLI064" && issue.Message.Contains("forks", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(integrity.Issues, issue => issue.Code == "MCLI072");
        Assert.Contains(integrity.Issues, issue => issue.Code == "MCLI085");
    }

    private static void CreateMinimalExecutableCommand(string workspace)
    {
        AssertCommandSucceeds($"new-workspace \"{workspace}\" --application demo --standard-cli-shapes");
        AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-command --id cmd-show --application app-demo --name show --token show", workspace);
        AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-executable-command --id exec-show --command cmd-show", workspace);
    }

    private static Parameter AddParameter(
        MetaCliModel model,
        ExecutableCommand executable,
        string id,
        string name,
        ValueShape shape,
        bool required = false)
    {
        var parameter = new Parameter
        {
            Id = id,
            ValueShape = shape,
            Name = name,
            IsRequired = required ? "true" : null,
        };
        model.ParameterList.Add(parameter);
        model.ExecutableCommandParameterList.Add(new ExecutableCommandParameter
        {
            Id = executable.Id + ":parameter:" + parameter.Id,
            ExecutableCommand = executable,
            Parameter = parameter,
        });
        return parameter;
    }

    private static void AssertCommandSucceeds(string arguments, string? workingDirectory = null)
    {
        var result = RunCli(arguments, workingDirectory);
        Assert.Equal(0, result.ExitCode);
    }

    private static void AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity(string arguments, string workspace)
    {
        AssertCommandSucceeds(arguments, workspace);
        AssertWorkspacePreservesProviderIntegrity(workspace);
    }

    private static void AssertWorkspacePreservesProviderIntegrity(string workspace)
    {
        var integrity = new MetaCliWorkspaceService().ValidateIntegrity(workspace);
        Assert.False(
            integrity.HasErrors,
            string.Join(Environment.NewLine, integrity.Issues.Select(issue => $"{issue.Code}: {issue.Message} ({issue.Location})")));
    }

    private static (int ExitCode, string Output) RunCli(string arguments, string? workingDirectory = null)
    {
        var repoRoot = FindRepositoryRoot();
        var cliPath = Path.Combine(repoRoot, "MetaCli", "Cli", "bin", "Debug", "net8.0", "meta-cli.exe");
        if (!File.Exists(cliPath))
        {
            throw new FileNotFoundException($"Could not find compiled meta-cli executable at '{cliPath}'. Build MetaCli.Cli before running tests.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = cliPath,
            Arguments = arguments,
            WorkingDirectory = workingDirectory ?? repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException("Could not start meta-cli process.");
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
