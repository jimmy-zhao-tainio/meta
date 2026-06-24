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
        Assert.Contains("Parameter", entityNames);
        Assert.Contains("ParameterGroup", entityNames);
        Assert.Contains("OptionToken", entityNames);
        Assert.Contains("ApplicationDefaultCommand", entityNames);
        Assert.DoesNotContain("CommandSegment", entityNames);
        Assert.DoesNotContain("ValueCodec", entityNames);
        Assert.DoesNotContain(entityNames, name => name.StartsWith("Cli", StringComparison.Ordinal));
        Assert.DoesNotContain(propertyNames, name => string.Equals(name, "Ordinal", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(propertyNames, name => string.Equals(name, "Order", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Cli_HelpShowsAuthoringSurfaceAndDoesNotExposeInit()
    {
        var result = RunCli("help");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("meta-cli --new-workspace <path>", result.Output);
        Assert.Contains("add-application", result.Output);
        Assert.Contains("add-executable-command", result.Output);
        Assert.Contains("add-option-token", result.Output);
        Assert.Contains("add-parameter-group-member", result.Output);
        Assert.Contains("add-duplicate-option-behavior", result.Output);
        Assert.Contains("add-unknown-token-behavior", result.Output);
        Assert.Contains("add-parser-policy", result.Output);
        Assert.Contains("add-output-format", result.Output);
        Assert.Contains("add-output-stream", result.Output);
        Assert.Contains("add-output", result.Output);
        Assert.Contains("add-exit-code", result.Output);
        Assert.DoesNotContain("init", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("check", result.Output, StringComparison.OrdinalIgnoreCase);
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
            ["Parameter"] = "created indirectly by add-option and add-positional",
            ["Option"] = "add-option",
            ["OptionToken"] = "add-option-token",
            ["ParameterGroup"] = "add-parameter-group",
            ["ParameterGroupMember"] = "add-parameter-group-member",
            ["PositionalArgument"] = "add-positional",
            ["ParserPolicy"] = "add-parser-policy",
            ["DuplicateOptionBehavior"] = "add-duplicate-option-behavior",
            ["UnknownTokenBehavior"] = "add-unknown-token-behavior",
            ["Output"] = "add-output",
            ["OutputFormat"] = "add-output-format",
            ["OutputStream"] = "add-output-stream",
            ["ExitCode"] = "add-exit-code",
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
            AssertCommandSucceeds($"--new-workspace \"{workspace}\"");
            AssertWorkspacePreservesProviderIntegrity(workspace);

            AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-application --id app-demo --name demo --executable-name demo", workspace);
            AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("set-default-command --application app-demo --command-id cmd-root --executable-command-id exec-root --name root", workspace);
            AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-value-arity --id arity-none --name None --min-value-count 0 --max-value-count 0", workspace);
            AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-value-arity --id arity-one --name One --min-value-count 1 --max-value-count 1", workspace);
            AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-value-shape --id shape-flag --name Flag --value-arity arity-none", workspace);
            AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-value-shape --id shape-path --name Path --value-arity arity-one --value-label \"<path>\" --allows-option-like-value true", workspace);
            AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-value-shape --id shape-text --name Text --value-arity arity-one --value-label \"<value>\"", workspace);
            AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-value-shape --id shape-visibility --name Visibility --value-arity arity-one --value-label \"<visibility>\"", workspace);
            AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-allowed-value --id visibility-public --value-shape shape-visibility --value public", workspace);
            AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-allowed-value --id visibility-internal --value-shape shape-visibility --value internal --previous-value visibility-public", workspace);

            AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-command --id cmd-model --application app-demo --name model --token model", workspace);
            AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-command --id cmd-add-property --application app-demo --name add-property --token add-property --parent-command cmd-model", workspace);
            AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-executable-command --id exec-add-property --command cmd-add-property", workspace);
            AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-option --parameter-id param-workspace --option-id option-workspace --executable-command exec-add-property --name workspace --value-shape shape-path --token-id token-workspace --token --workspace --required true", workspace);
            AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-option-token --id token-workspace-short --option option-workspace --token -w --previous-token token-workspace", workspace);
            AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-positional --parameter-id param-entity --positional-id pos-entity --executable-command exec-add-property --name Entity --value-shape shape-text --required true", workspace);
            AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-positional --parameter-id param-property --positional-id pos-property --executable-command exec-add-property --name Property --value-shape shape-text --previous-argument pos-entity --required true", workspace);

            AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-command --id cmd-add-entity --application app-demo --name add-entity --token add-entity --parent-command cmd-model", workspace);
            AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-executable-command --id exec-add-entity --command cmd-add-entity", workspace);
            AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-positional --parameter-id param-id --positional-id pos-id --executable-command exec-add-entity --name Id --value-shape shape-text", workspace);
            AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-option --parameter-id param-auto-id --option-id option-auto-id --executable-command exec-add-entity --name auto-id --value-shape shape-flag --token-id token-auto-id --token --auto-id", workspace);
            AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-option-token --id token-auto-id-short --option option-auto-id --token -a --previous-token token-auto-id", workspace);
            AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-parameter-group --id group-id-choice --executable-command exec-add-entity --name IdChoice --member-id group-id-choice-id --parameter param-id --required true", workspace);
            AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-parameter-group-member --id group-id-choice-auto --parameter-group group-id-choice --parameter param-auto-id --previous-member group-id-choice-id", workspace);
            AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-duplicate-option-behavior --id duplicate-error --name Error", workspace);
            AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-unknown-token-behavior --id unknown-error --name Error", workspace);
            AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-parser-policy --id parser-default --application app-demo --name Default --stop-parsing-token -- --allows-equals-value-syntax true --allows-options-after-positionals false --allows-short-option-clusters false --duplicate-option-behavior duplicate-error --unknown-token-behavior unknown-error", workspace);
            AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-output-format --id output-format-text --name Text --content-type text/plain", workspace);
            AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-output-stream --id output-stream-stdout --name Stdout", workspace);
            AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-output --id output-add-property-summary --executable-command exec-add-property --name Summary --output-format output-format-text --output-stream output-stream-stdout", workspace);
            AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-exit-code --id exit-add-property-ok --application app-demo --executable-command exec-add-property --code 0 --name Ok", workspace);
            AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-exit-code --id exit-usage --application app-demo --code 2 --name UsageError", workspace);

            var show = RunCli("show", workspace);

            Assert.Equal(0, show.ExitCode);
            Assert.Contains("MetaCli workspace", show.Output);
            Assert.Contains("application: demo (app-demo)", show.Output);
            Assert.Contains("command surface:", show.Output);
            Assert.Contains("model add-property [runnable]", show.Output);
            Assert.DoesNotContain("Application:", show.Output);
            Assert.DoesNotContain("ExecutableCommand:", show.Output);

            var model = MetaCliModel.LoadFromXmlWorkspace(workspace, searchUpward: false);
            var addProperty = model.CommandList.Single(command => command.Id == "cmd-add-property");
            Assert.Equal("model add-property", MetaCliWorkspaceService.BuildRoute(addProperty));
            Assert.Equal("exec-add-property", model.ExecutableCommandList.Single(command => ReferenceEquals(command.Command, addProperty)).Id);
            Assert.Contains(model.OptionTokenList, token => token.Id == "token-workspace" && token.Option.Id == "option-workspace" && token.IsPrimary == "true");
            Assert.Contains(model.OptionTokenList, token => token.Id == "token-workspace-short" && token.Option.Id == "option-workspace" && token.PreviousToken?.Id == "token-workspace");
            Assert.Same(
                model.PositionalArgumentList.Single(argument => argument.Id == "pos-entity"),
                model.PositionalArgumentList.Single(argument => argument.Id == "pos-property").PreviousArgument);
            Assert.Same(
                model.ParameterGroupMemberList.Single(member => member.Id == "group-id-choice-id"),
                model.ParameterGroupMemberList.Single(member => member.Id == "group-id-choice-auto").PreviousMember);
            Assert.Equal("duplicate-error", Assert.Single(model.DuplicateOptionBehaviorList).Id);
            Assert.Equal("unknown-error", Assert.Single(model.UnknownTokenBehaviorList).Id);
            var parserPolicy = Assert.Single(model.ParserPolicyList);
            Assert.Same(model.ApplicationList.Single(application => application.Id == "app-demo"), parserPolicy.Application);
            Assert.Equal("--", parserPolicy.StopParsingToken);
            Assert.Same(model.DuplicateOptionBehaviorList[0], parserPolicy.DuplicateOptionBehavior);
            Assert.Same(model.UnknownTokenBehaviorList[0], parserPolicy.UnknownTokenBehavior);
            var output = Assert.Single(model.OutputList);
            Assert.Same(model.ExecutableCommandList.Single(command => command.Id == "exec-add-property"), output.ExecutableCommand);
            Assert.Same(model.OutputFormatList.Single(), output.OutputFormat);
            Assert.Same(model.OutputStreamList.Single(), output.OutputStream);
            Assert.Contains(model.ExitCodeList, exitCode => exitCode.Id == "exit-add-property-ok" && ReferenceEquals(exitCode.ExecutableCommand, output.ExecutableCommand));
            Assert.Contains(model.ExitCodeList, exitCode => exitCode.Id == "exit-usage" && exitCode.ExecutableCommand is null);
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public void Cli_AddOptionRequiresPrimaryTokenAggregateAndDoesNotPersistPartialRows()
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
            AssertCommandSucceeds($"--new-workspace \"{workspace}\"");
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
        var optionParameter = new Parameter { Id = "param-option", ExecutableCommand = executable, ValueShape = shape, Name = "workspace" };
        model.ParameterList.Add(optionParameter);
        var option = new Option { Id = "option", Parameter = optionParameter };
        model.OptionList.Add(option);
        var token1 = new OptionToken { Id = "token-1", Option = option, Token = "--workspace", IsPrimary = "true" };
        var token2 = new OptionToken { Id = "token-2", Option = option, Token = "-w", PreviousToken = token1 };
        var token3 = new OptionToken { Id = "token-3", Option = option, Token = "--workspace-alias", PreviousToken = token1 };
        model.OptionTokenList.Add(token1);
        model.OptionTokenList.Add(token2);
        model.OptionTokenList.Add(token3);
        var positionalParameter1 = new Parameter { Id = "param-entity", ExecutableCommand = executable, ValueShape = shape, Name = "Entity" };
        var positionalParameter2 = new Parameter { Id = "param-property", ExecutableCommand = executable, ValueShape = shape, Name = "Property", IsRequired = "true" };
        model.ParameterList.Add(positionalParameter1);
        model.ParameterList.Add(positionalParameter2);
        var positional1 = new PositionalArgument { Id = "pos-entity", Parameter = positionalParameter1 };
        var positional2 = new PositionalArgument { Id = "pos-property", Parameter = positionalParameter2, PreviousArgument = positional1 };
        model.PositionalArgumentList.Add(positional1);
        model.PositionalArgumentList.Add(positional2);
        var duplicateOption = new Option { Id = "option-duplicate", Parameter = optionParameter };
        model.OptionList.Add(duplicateOption);
        model.ParameterGroupList.Add(new ParameterGroup { Id = "group-empty", ExecutableCommand = executable, Name = "Empty", IsRequired = "true" });

        var integrity = service.ValidateIntegrity(model);

        Assert.True(integrity.HasErrors);
        Assert.Contains(integrity.Issues, issue => issue.Code == "MCLI040");
        Assert.Contains(integrity.Issues, issue => issue.Code == "MCLI055");
        Assert.Contains(integrity.Issues, issue => issue.Code == "MCLI064" && issue.Message.Contains("forks", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(integrity.Issues, issue => issue.Code == "MCLI072");
        Assert.Contains(integrity.Issues, issue => issue.Code == "MCLI085");
    }

    [Fact]
    public void Validation_CatchesParserPolicyOutputAndExitCodeSafeguards()
    {
        var service = new MetaCliWorkspaceService();
        var model = service.CreateEmpty();
        var app = new Application { Id = "app", Name = "demo" };
        var otherApp = new Application { Id = "other-app", Name = "other" };
        model.ApplicationList.Add(app);
        model.ApplicationList.Add(otherApp);
        var command = new Command { Id = "cmd", Application = app, Name = "run", Token = "run" };
        var otherCommand = new Command { Id = "other-cmd", Application = otherApp, Name = "run", Token = "run" };
        model.CommandList.Add(command);
        model.CommandList.Add(otherCommand);
        var executable = new ExecutableCommand { Id = "exec", Command = command };
        var otherExecutable = new ExecutableCommand { Id = "other-exec", Command = otherCommand };
        model.ExecutableCommandList.Add(executable);
        model.ExecutableCommandList.Add(otherExecutable);

        var duplicateBehavior = new DuplicateOptionBehavior { Id = "duplicate-error", Name = "Error" };
        var duplicateBehavior2 = new DuplicateOptionBehavior { Id = "duplicate-error-2", Name = "Error" };
        model.DuplicateOptionBehaviorList.Add(duplicateBehavior);
        model.DuplicateOptionBehaviorList.Add(duplicateBehavior2);
        var unknownBehavior = new UnknownTokenBehavior { Id = "unknown-error", Name = "Error" };
        var unknownBehavior2 = new UnknownTokenBehavior { Id = "unknown-error-2", Name = "Error" };
        model.UnknownTokenBehaviorList.Add(unknownBehavior);
        model.UnknownTokenBehaviorList.Add(unknownBehavior2);

        model.ParserPolicyList.Add(new ParserPolicy
        {
            Id = "parser-1",
            Application = app,
            Name = "Default",
            AllowsEqualsValueSyntax = "maybe",
            DuplicateOptionBehavior = duplicateBehavior,
            UnknownTokenBehavior = unknownBehavior,
        });
        model.ParserPolicyList.Add(new ParserPolicy { Id = "parser-2", Application = app, Name = "Other" });
        model.ParserPolicyList.Add(new ParserPolicy
        {
            Id = "parser-3",
            Application = new Application { Id = "ghost-app", Name = "ghost" },
            Name = "Ghost",
            DuplicateOptionBehavior = new DuplicateOptionBehavior { Id = "ghost-duplicate", Name = "Ghost" },
            UnknownTokenBehavior = new UnknownTokenBehavior { Id = "ghost-unknown", Name = "Ghost" },
        });

        var format = new OutputFormat { Id = "format-text", Name = "Text", ContentType = " " };
        var format2 = new OutputFormat { Id = "format-text-2", Name = "Text" };
        model.OutputFormatList.Add(format);
        model.OutputFormatList.Add(format2);
        var stream = new OutputStream { Id = "stream-stdout", Name = "Stdout" };
        var stream2 = new OutputStream { Id = "stream-stdout-2", Name = "Stdout" };
        model.OutputStreamList.Add(stream);
        model.OutputStreamList.Add(stream2);
        model.OutputList.Add(new Output { Id = "output-1", ExecutableCommand = executable, Name = "Summary", OutputFormat = format, OutputStream = stream });
        model.OutputList.Add(new Output { Id = "output-2", ExecutableCommand = executable, Name = "Summary" });
        model.OutputList.Add(new Output
        {
            Id = "output-3",
            ExecutableCommand = new ExecutableCommand { Id = "ghost-exec", Command = command },
            Name = "Ghost",
            OutputFormat = new OutputFormat { Id = "ghost-format", Name = "Ghost" },
            OutputStream = new OutputStream { Id = "ghost-stream", Name = "Ghost" },
        });

        model.ExitCodeList.Add(new ExitCode { Id = "exit-usage", Application = app, Code = "2", Name = "Usage" });
        model.ExitCodeList.Add(new ExitCode { Id = "exit-usage-2", Application = app, Code = "2", Name = "UsageAgain" });
        model.ExitCodeList.Add(new ExitCode { Id = "exit-ok", Application = app, ExecutableCommand = executable, Code = "0", Name = "Ok" });
        model.ExitCodeList.Add(new ExitCode { Id = "exit-ok-2", Application = app, ExecutableCommand = executable, Code = "0", Name = "OkAgain" });
        model.ExitCodeList.Add(new ExitCode { Id = "exit-wrong-app", Application = app, ExecutableCommand = otherExecutable, Code = "3", Name = "WrongApp" });
        model.ExitCodeList.Add(new ExitCode { Id = "exit-ghost-exec", Application = app, ExecutableCommand = new ExecutableCommand { Id = "ghost-exec", Command = command }, Code = "5", Name = "GhostExec" });
        model.ExitCodeList.Add(new ExitCode { Id = "exit-ghost-app", Application = new Application { Id = "ghost-app", Name = "ghost" }, Code = "4", Name = "GhostApp" });

        var integrity = service.ValidateIntegrity(model);

        Assert.True(integrity.HasErrors);
        Assert.Contains(integrity.Issues, issue => issue.Code == "MCLI100");
        Assert.Contains(integrity.Issues, issue => issue.Code == "MCLI110");
        Assert.Contains(integrity.Issues, issue => issue.Code == "MCLI120");
        Assert.Contains(integrity.Issues, issue => issue.Code == "MCLI121");
        Assert.Contains(integrity.Issues, issue => issue.Code == "MCLI122");
        Assert.Contains(integrity.Issues, issue => issue.Code == "MCLI123");
        Assert.Contains(integrity.Issues, issue => issue.Code == "MCLI124");
        Assert.Contains(integrity.Issues, issue => issue.Code == "MCLI130");
        Assert.Contains(integrity.Issues, issue => issue.Code == "MCLI131");
        Assert.Contains(integrity.Issues, issue => issue.Code == "MCLI140");
        Assert.Contains(integrity.Issues, issue => issue.Code == "MCLI150");
        Assert.Contains(integrity.Issues, issue => issue.Code == "MCLI151");
        Assert.Contains(integrity.Issues, issue => issue.Code == "MCLI152");
        Assert.Contains(integrity.Issues, issue => issue.Code == "MCLI153");
        Assert.Contains(integrity.Issues, issue => issue.Code == "MCLI160");
        Assert.Contains(integrity.Issues, issue => issue.Code == "MCLI161");
        Assert.Contains(integrity.Issues, issue => issue.Code == "MCLI162");
        Assert.Contains(integrity.Issues, issue => issue.Code == "MCLI163");
        Assert.Contains(integrity.Issues, issue => issue.Code == "MCLI164");
    }

    private static void CreateMinimalExecutableCommand(string workspace)
    {
        AssertCommandSucceeds($"--new-workspace \"{workspace}\"");
        AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-application --id app-demo --name demo", workspace);
        AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-value-arity --id arity-one --name One --min-value-count 1 --max-value-count 1", workspace);
        AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-value-shape --id shape-path --name Path --value-arity arity-one --value-label \"<path>\" --allows-option-like-value true", workspace);
        AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-command --id cmd-show --application app-demo --name show --token show", workspace);
        AssertCommandSucceedsAndWorkspacePreservesProviderIntegrity("add-executable-command --id exec-show --command cmd-show", workspace);
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
