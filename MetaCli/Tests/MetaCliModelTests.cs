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

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("meta-cli new-workspace <path>", result.Output);
        Assert.Contains("new-workspace", result.Output);
        Assert.Contains("from-syntax", result.Output);
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
    public void Cli_FromSyntaxCreatesProviderValidWorkspace()
    {
        var root = Path.Combine(Path.GetTempPath(), "meta-cli-syntax-", Guid.NewGuid().ToString("N"));
        var syntaxPath = Path.Combine(root, "demo.syntax");
        var workspace = Path.Combine(root, "Demo.MetaCli");
        Directory.CreateDirectory(root);
        File.WriteAllText(syntaxPath, """
application demo

option --workspace path

command help default

command new-workspace
  positional Path path required

command model add-entity
  positional Id text
  option --auto-id flag alias -a
  parameter-group IdChoice required members Id auto-id
""");

        try
        {
            var result = RunCli($"from-syntax \"{syntaxPath}\" --workspace \"{workspace}\"");

            Assert.Equal(0, result.ExitCode);
            Assert.Contains("Authored MetaCli workspace from syntax", result.Output);
            Assert.Contains("commands: 4 (3 runnable)", result.Output);
            AssertWorkspacePreservesProviderIntegrity(workspace);

            var model = MetaCliModel.LoadFromXmlWorkspace(workspace, searchUpward: false);
            Assert.Single(model.ApplicationParameterList);
            var addEntity = model.CommandList.Single(command => command.Name == "add-entity");
            Assert.Equal("model add-entity", MetaCliWorkspaceService.BuildRoute(addEntity));
            Assert.Single(model.ParameterGroupList);
            Assert.Contains(model.OptionTokenList, token => token.Token == "-a" && token.PreviousToken?.Token == "--auto-id");

            var exitCode = -1;
            var error = new StringWriter();
            MetaCliInvocation? invocation = null;
            var runtime = new MetaCliRuntime<MetaCliModel>(
                    workspace,
                    error: error,
                    setExitCode: code => exitCode = code)
                .Bind("exec-model-add-entity", (MetaCliInvocation command, MetaCliModel _) => invocation = command);

            runtime.Run("model", "add-entity", "--workspace", workspace, "--auto-id");

            Assert.Equal(0, exitCode);
            Assert.Equal(string.Empty, error.ToString());
            Assert.NotNull(invocation);
            Assert.Equal("model add-entity", invocation.CommandRoute);
            Assert.True(invocation.Flag("auto-id"));
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
            AssertCommandSucceeds($"new-workspace \"{workspace}\"");
            AssertWorkspacePreservesProviderIntegrity(workspace);

            AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-application --id app-demo --name demo --executable-name demo", workspace);
            AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-value-arity --id arity-none --name None --min-value-count 0 --max-value-count 0", workspace);
            AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-value-arity --id arity-one --name One --min-value-count 1 --max-value-count 1", workspace);
            AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-value-shape --id shape-flag --name Flag --value-arity arity-none", workspace);
            AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-value-shape --id shape-path --name Path --value-arity arity-one --value-label \"<path>\" --allows-option-like-value true", workspace);
            AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-value-shape --id shape-text --name Text --value-arity arity-one --value-label \"<value>\"", workspace);
            AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-value-shape --id shape-visibility --name Visibility --value-arity arity-one --value-label \"<visibility>\"", workspace);
            AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-allowed-value --id visibility-public --value-shape shape-visibility --value public", workspace);
            AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-allowed-value --id visibility-internal --value-shape shape-visibility --value internal", workspace);

            AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-application-option --parameter-id param-workspace --option-id option-workspace --application app-demo --name workspace --value-shape shape-path --token-id token-workspace --token --workspace", workspace);
            AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-option-token --id token-workspace-short --option option-workspace --token -w --previous-token token-workspace", workspace);

            AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-command --id cmd-new-workspace --application app-demo --name new-workspace --token new-workspace", workspace);
            AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-executable-command --id exec-new-workspace --command cmd-new-workspace", workspace);
            AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-positional --parameter-id param-new-workspace --positional-id pos-new-workspace --executable-command exec-new-workspace --name Path --value-shape shape-path --required true", workspace);
            AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-command --id cmd-help --application app-demo --name help --token help", workspace);
            AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-executable-command --id exec-help --command cmd-help", workspace);
            AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("set-default-command --application app-demo --executable-command exec-help", workspace);

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

            Assert.Equal(4, addOption.ExitCode);
            Assert.Contains("missing required option --token-id", addOption.Output);

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
        AssertCommandSucceeds($"new-workspace \"{workspace}\"");
        AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-application --id app-demo --name demo", workspace);
        AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-value-arity --id arity-one --name One --min-value-count 1 --max-value-count 1", workspace);
        AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-value-shape --id shape-path --name Path --value-arity arity-one --value-label \"<path>\" --allows-option-like-value true", workspace);
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
