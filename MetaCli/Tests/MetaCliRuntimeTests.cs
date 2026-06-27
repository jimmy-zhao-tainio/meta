using MetaCli.Core;

namespace MetaCli.Tests;

public sealed class MetaCliRuntimeTests
{
    [Fact]
    public void Runtime_LoadsCommandSurfaceAndDomainWorkspaceForWorkspaceHandler()
    {
        using var temp = TempDirectory.Create();
        var commandWorkspace = temp.SaveCommandSurface(CreateRuntimeModel());
        var domainWorkspace = temp.SaveDomainModel("Domain.MetaCli");
        var error = new StringWriter();
        var exitCode = -1;
        MetaCliModel? handlerModel = null;
        MetaCliInvocation? invocation = null;

        var runtime = new MetaCliRuntime<MetaCliModel>(
                commandWorkspace,
                error: error,
                setExitCode: code => exitCode = code)
            .Bind("exec-add-property", (MetaCliInvocation command, MetaCliModel domainModel) =>
            {
                invocation = command;
                handlerModel = domainModel;
            });

        runtime.Run("model", "add-property", "--workspace", domainWorkspace, "Customer", "Name");

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error.ToString());
        Assert.NotNull(handlerModel);
        Assert.Contains(handlerModel.ApplicationList, application => application.Id == "app-domain");
        Assert.NotNull(invocation);
        Assert.Equal("model add-property", invocation.CommandRoute);
        Assert.Equal("Customer", invocation.Required("Entity"));
        Assert.Equal("Name", invocation.Required("Property"));
    }

    [Fact]
    public void Runtime_DefaultsDomainWorkspaceToCurrentDirectory()
    {
        using var temp = TempDirectory.Create();
        var commandWorkspace = temp.SaveCommandSurface(CreateRuntimeModel());
        temp.SaveDomainModel(".");
        var exitCode = -1;
        MetaCliModel? handlerModel = null;
        var previousCurrentDirectory = Directory.GetCurrentDirectory();

        var runtime = new MetaCliRuntime<MetaCliModel>(
                commandWorkspace,
                error: new StringWriter(),
                setExitCode: code => exitCode = code)
            .Bind("exec-add-property", (MetaCliInvocation _, MetaCliModel domainModel) => handlerModel = domainModel);

        try
        {
            Directory.SetCurrentDirectory(temp.Path);
            runtime.Run("model", "add-property", "Customer", "Name");
        }
        finally
        {
            Directory.SetCurrentDirectory(previousCurrentDirectory);
        }

        Assert.Equal(0, exitCode);
        Assert.NotNull(handlerModel);
        Assert.Contains(handlerModel.ApplicationList, application => application.Id == "app-domain");
    }

    [Fact]
    public void Runtime_CanDispatchCommandWithoutDomainWorkspace()
    {
        using var temp = TempDirectory.Create();
        var commandWorkspace = temp.SaveCommandSurface(CreateRuntimeModel());
        var exitCode = -1;
        MetaCliInvocation? invocation = null;

        var runtime = new MetaCliRuntime<MetaCliModel>(
                commandWorkspace,
                error: new StringWriter(),
                setExitCode: code => exitCode = code)
            .Bind("exec-help", command => invocation = command);

        runtime.Run();

        Assert.Equal(0, exitCode);
        Assert.NotNull(invocation);
        Assert.Equal("help", invocation.CommandRoute);
    }

    [Fact]
    public void Runtime_ParsesAuthoredMetaCliWorkspace()
    {
        using var temp = TempDirectory.Create();
        var workspace = Path.Combine(FindRepositoryRoot(), "MetaCli", "Cli", "meta-cli.MetaCli");
        var domainWorkspace = temp.SaveDomainModel("Demo.MetaCli");
        var error = new StringWriter();
        var exitCode = -1;
        MetaCliModel? handlerModel = null;
        MetaCliInvocation? invocation = null;

        var runtime = new MetaCliRuntime<MetaCliModel>(
                workspace,
                error: error,
                setExitCode: code => exitCode = code)
            .Bind("exec-add-option", (MetaCliInvocation command, MetaCliModel domainModel) =>
            {
                invocation = command;
                handlerModel = domainModel;
            });

        runtime.Run(
            "add-option",
            "--workspace",
            domainWorkspace,
            "--parameter-id",
            "param-workspace",
            "--option-id",
            "option-workspace",
            "--executable-command",
            "exec-show",
            "--name",
            "workspace",
            "--value-shape",
            "shape-path",
            "--token-id",
            "token-workspace",
            "--token",
            "--workspace",
            "--required",
            "true");

        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error.ToString());
        Assert.NotNull(handlerModel);
        Assert.Contains(handlerModel.ApplicationList, application => application.Id == "app-domain");
        Assert.NotNull(invocation);
        Assert.Equal("add-option", invocation.CommandRoute);
        Assert.Equal("exec-add-option", invocation.ExecutableCommand.Id);
        Assert.Equal(domainWorkspace, invocation.Required("workspace"));
        Assert.Equal("--workspace", invocation.Required("token"));
        Assert.Equal("true", invocation.Required("required"));
    }

    [Fact]
    public void Runtime_WritesParseFailureAndExitCode()
    {
        using var temp = TempDirectory.Create();
        var commandWorkspace = temp.SaveCommandSurface(CreateRuntimeModel());
        var error = new StringWriter();
        var exitCode = -1;

        var runtime = new MetaCliRuntime<MetaCliModel>(
            commandWorkspace,
            error: error,
            setExitCode: code => exitCode = code);

        runtime.Run("banana");

        Assert.Equal(2, exitCode);
        Assert.Contains("Unknown command 'banana'.", error.ToString());
    }

    [Fact]
    public void Runtime_WritesMissingHandlerFailureAndExitCode()
    {
        using var temp = TempDirectory.Create();
        var commandWorkspace = temp.SaveCommandSurface(CreateRuntimeModel());
        var error = new StringWriter();
        var exitCode = -1;

        var runtime = new MetaCliRuntime<MetaCliModel>(
            commandWorkspace,
            error: error,
            setExitCode: code => exitCode = code);

        runtime.Run("model", "add-property", "Customer", "Name");

        Assert.Equal(4, exitCode);
        Assert.Contains("Command 'model add-property' is modeled but has no implementation.", error.ToString());
    }

    [Fact]
    public void Runtime_WritesWorkspaceLoadFailureAndExitCode()
    {
        using var temp = TempDirectory.Create();
        var commandWorkspace = temp.SaveCommandSurface(CreateRuntimeModel());
        var error = new StringWriter();
        var exitCode = -1;

        var runtime = new MetaCliRuntime<MetaCliModel>(
                commandWorkspace,
                error: error,
                setExitCode: code => exitCode = code)
            .Bind("exec-add-property", static (_, _) => { });

        runtime.Run("model", "add-property", "--workspace", Path.Combine(temp.Path, "Missing.MetaCli"), "Customer", "Name");

        Assert.Equal(4, exitCode);
        Assert.Contains("Command 'model add-property' failed.", error.ToString());
        Assert.Contains("Workspace", error.ToString());
    }

    [Fact]
    public void Runtime_AllowsHandlerToSetExitCodeWithoutGenericFailure()
    {
        using var temp = TempDirectory.Create();
        var commandWorkspace = temp.SaveCommandSurface(CreateRuntimeModel());
        var error = new StringWriter();
        var exitCode = -1;

        var runtime = new MetaCliRuntime<MetaCliModel>(
                commandWorkspace,
                error: error,
                setExitCode: code => exitCode = code)
            .Bind("exec-add-property", _ => throw new MetaCliExitException(2));

        runtime.Run("model", "add-property", "Customer", "Name");

        Assert.Equal(2, exitCode);
        Assert.Equal(string.Empty, error.ToString());
    }

    [Fact]
    public void Runtime_WritesCommandSurfaceLoadFailureAndExitCode()
    {
        using var temp = TempDirectory.Create();
        var error = new StringWriter();
        var exitCode = -1;

        var runtime = new MetaCliRuntime<MetaCliModel>(
            Path.Combine(temp.Path, "Missing.MetaCli"),
            error: error,
            setExitCode: code => exitCode = code);

        runtime.Run("help");

        Assert.Equal(4, exitCode);
        Assert.Contains("Cannot load command surface workspace", error.ToString());
    }

    [Fact]
    public void Runtime_WritesCommonCommandAndArgumentFailures()
    {
        using var temp = TempDirectory.Create();
        var commandWorkspace = temp.SaveCommandSurface(CreateRuntimeModel());

        AssertRunFails(commandWorkspace, 2, "Command 'model' is not runnable.", "model");
        AssertRunFails(commandWorkspace, 2, "Option '--workspace' was provided more than once.", "model", "add-property", "--workspace", "Demo.Meta", "--workspace", "Other.Meta", "Customer", "Name");
        AssertRunFails(commandWorkspace, 2, "Option '--workspace' requires a value.", "model", "add-property", "--workspace");
        AssertRunSucceeds(commandWorkspace, "model", "add-property", "Customer", "--workspace", "Demo.Meta", "Name");
        AssertRunFails(commandWorkspace, 2, "Unexpected argument 'Two.MetaCli'.", "new-workspace", "One.MetaCli", "Two.MetaCli");
    }

    [Fact]
    public void Runtime_DefaultHelp_WritesApplicationHelpFromMetaCliModel()
    {
        using var temp = TempDirectory.Create();
        var commandWorkspace = temp.SaveCommandSurface(CreateRuntimeModel());

        var noArguments = RunDefaultHelp(commandWorkspace);
        var longOption = RunDefaultHelp(commandWorkspace, "--help");
        var shortOption = RunDefaultHelp(commandWorkspace, "-h");
        var helpCommand = RunDefaultHelp(commandWorkspace, "help");

        foreach (var result in new[] { noArguments, longOption, shortOption, helpCommand })
        {
            Assert.Equal(0, result.ExitCode);
            Assert.Equal(string.Empty, result.Error);
            Assert.Contains("demo <command> [options]", result.Output);
            Assert.Contains("model", result.Output);
            Assert.Contains("new-workspace", result.Output);
            Assert.Contains("Next: demo help <command>", result.Output);
        }
    }

    [Fact]
    public void Runtime_DefaultHelp_WritesCommandHelpFromMetaCliModel()
    {
        using var temp = TempDirectory.Create();
        var commandWorkspace = temp.SaveCommandSurface(CreateRuntimeModel());

        var forms = new[]
        {
            new[] { "help", "model", "add-property" },
            new[] { "model", "add-property", "help" },
            new[] { "model", "add-property", "--help" },
            new[] { "model", "add-property", "-h" },
        };

        foreach (var arguments in forms)
        {
            var result = RunDefaultHelp(commandWorkspace, arguments);

            Assert.Equal(0, result.ExitCode);
            Assert.Equal(string.Empty, result.Error);
            Assert.Contains("demo model add-property", result.Output);
            Assert.Contains("<Entity>", result.Output);
            Assert.Contains("<Property>", result.Output);
            Assert.Contains("--workspace <value>", result.Output);
            Assert.Contains("--visibility <value>", result.Output);
        }
    }

    [Fact]
    public void Runtime_DefaultHelp_ReportsUnknownRoutesAndWritesCommandGroupHelp()
    {
        using var temp = TempDirectory.Create();
        var commandWorkspace = temp.SaveCommandSurface(CreateRuntimeModel());

        var unknown = RunDefaultHelp(commandWorkspace, "help", "missing");
        var groupHelp = RunDefaultHelp(commandWorkspace, "model", "--help");

        Assert.Equal(2, unknown.ExitCode);
        Assert.Equal(string.Empty, unknown.Output);
        Assert.Contains("Unknown command 'missing'.", unknown.Error);
        Assert.Equal(0, groupHelp.ExitCode);
        Assert.Equal(string.Empty, groupHelp.Error);
        Assert.Contains("demo model <command> [options]", groupHelp.Output);
        Assert.Contains("add-property", groupHelp.Output);
    }

    [Fact]
    public void Runtime_WritesMissingDefaultCommandFailure()
    {
        using var temp = TempDirectory.Create();
        var commandWorkspace = temp.SaveCommandSurface(CreateRuntimeModel(includeDefaultCommand: false));

        AssertRunFails(commandWorkspace, 2, "No command was provided.");
    }

    [Fact]
    public void Runtime_SelectsApplicationWhenModelContainsSeveral()
    {
        using var temp = TempDirectory.Create();
        var model = CreateRuntimeModel();
        model.ApplicationList.Add(new Application { Id = "app-other", Name = "other", ExecutableName = "other" });
        var commandWorkspace = temp.SaveCommandSurface(model);

        AssertRunFails(commandWorkspace, 2, "The MetaCli model has more than one application; select one before running.", "help");

        var selectedExitCode = -1;
        MetaCliInvocation? selectedInvocation = null;
        var selected = new MetaCliRuntime<MetaCliModel>(
                commandWorkspace,
                applicationId: "app-demo",
                error: new StringWriter(),
                setExitCode: code => selectedExitCode = code)
            .Bind("exec-help", command => selectedInvocation = command);

        selected.Run("help");

        Assert.Equal(0, selectedExitCode);
        Assert.NotNull(selectedInvocation);
        Assert.Equal("app-demo", selectedInvocation.Application.Id);
        Assert.Equal("help", selectedInvocation.CommandRoute);

        AssertRunFailsForApplication(commandWorkspace, 2, "Application 'missing-app' does not exist.", applicationId: "missing-app", arguments: new[] { "help" });
    }

    [Fact]
    public void Runtime_EnforcesRequiredParameterGroup()
    {
        using var temp = TempDirectory.Create();
        var commandWorkspace = temp.SaveCommandSurface(CreateRuntimeModel());
        var domainWorkspace = temp.SaveDomainModel("Domain.MetaCli");
        var exitCode = -1;
        var callCount = 0;

        var runtime = new MetaCliRuntime<MetaCliModel>(
                commandWorkspace,
                error: new StringWriter(),
                setExitCode: code => exitCode = code)
            .Bind("exec-add-entity", (MetaCliInvocation _, MetaCliModel _) => callCount++);

        runtime.Run("model", "add-entity", "--workspace", domainWorkspace, "--auto-id");

        Assert.Equal(0, exitCode);
        Assert.Equal(1, callCount);
        AssertRunFails(commandWorkspace, 2, "Parameter group 'IdChoice' requires one of: Id, auto-id.", "model", "add-entity");
        AssertRunFails(commandWorkspace, 2, "Parameter group 'IdChoice' accepts only one member.", "model", "add-entity", "--auto-id", "Customer");
    }

    [Fact]
    public void Runtime_EnforcesAllowedValues()
    {
        using var temp = TempDirectory.Create();
        var commandWorkspace = temp.SaveCommandSurface(CreateRuntimeModel());

        AssertRunFails(commandWorkspace, 2, "Parameter 'visibility' does not allow value 'private'.", "model", "add-property", "--workspace", "Demo.Meta", "--visibility", "private", "Customer", "Name");
    }

    private static void AssertRunFails(
        string commandWorkspace,
        int expectedExitCode,
        string expectedMessage,
        params string[] arguments)
    {
        AssertRunFailsForApplication(commandWorkspace, expectedExitCode, expectedMessage, applicationId: null, arguments);
    }

    private static void AssertRunFailsForApplication(
        string commandWorkspace,
        int expectedExitCode,
        string expectedMessage,
        string? applicationId,
        IReadOnlyList<string> arguments)
    {
        var error = new StringWriter();
        var exitCode = -1;
        var runtime = new MetaCliRuntime<MetaCliModel>(
            commandWorkspace,
            applicationId: applicationId,
            error: error,
            setExitCode: code => exitCode = code);

        runtime.Run(arguments);

        Assert.Equal(expectedExitCode, exitCode);
        Assert.Contains(expectedMessage, error.ToString());
    }

    private static void AssertRunSucceeds(
        string commandWorkspace,
        params string[] arguments)
    {
        var error = new StringWriter();
        var exitCode = -1;
        var ran = false;
        var runtime = new MetaCliRuntime<MetaCliModel>(
                commandWorkspace,
                error: error,
                setExitCode: code => exitCode = code)
            .Bind("exec-add-property", invocation =>
            {
                ran = true;
                Assert.Equal("Demo.Meta", invocation.Required("workspace"));
                Assert.Equal("Customer", invocation.Required("Entity"));
                Assert.Equal("Name", invocation.Required("Property"));
            });

        runtime.Run(arguments);

        Assert.True(ran);
        Assert.Equal(0, exitCode);
        Assert.Equal(string.Empty, error.ToString());
    }

    private static (int ExitCode, string Output, string Error) RunDefaultHelp(
        string commandWorkspace,
        params string[] arguments)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = -1;
        var runtime = new MetaCliRuntime<MetaCliModel>(
                commandWorkspace,
                error: error,
                setExitCode: code => exitCode = code)
            .UseDefaultHelp(output, error);

        runtime.Run(arguments);

        return (exitCode, output.ToString(), error.ToString());
    }

    private static MetaCliModel CreateRuntimeModel(bool includeDefaultCommand = true)
    {
        var model = MetaCliModel.CreateEmpty();
        var app = new Application { Id = "app-demo", Name = "demo", ExecutableName = "demo" };
        model.ApplicationList.Add(app);

        var none = new ValueArity { Id = "arity-none", Name = "None", MinValueCount = "0", MaxValueCount = "0" };
        var one = new ValueArity { Id = "arity-one", Name = "One", MinValueCount = "1", MaxValueCount = "1" };
        model.ValueArityList.Add(none);
        model.ValueArityList.Add(one);
        var flag = new ValueShape { Id = "shape-flag", Name = "Flag", ValueArity = none };
        var text = new ValueShape { Id = "shape-text", Name = "Text", ValueArity = one };
        var path = new ValueShape { Id = "shape-path", Name = "Path", ValueArity = one, AllowsOptionLikeValue = "true" };
        var visibility = new ValueShape { Id = "shape-visibility", Name = "Visibility", ValueArity = one };
        model.ValueShapeList.Add(flag);
        model.ValueShapeList.Add(text);
        model.ValueShapeList.Add(path);
        model.ValueShapeList.Add(visibility);
        model.AllowedValueList.Add(new AllowedValue { Id = "visibility-public", ValueShape = visibility, Value = "public" });
        model.AllowedValueList.Add(new AllowedValue { Id = "visibility-internal", ValueShape = visibility, Value = "internal" });

        AddApplicationOption(model, app, "param-workspace", "option-workspace", "token-workspace", "workspace", path, "--workspace");
        AddOptionAlias(model, "option-workspace", "token-workspace-short", "-w", "token-workspace");

        var helpCommand = AddCommand(model, app, "cmd-help", "help", "help", null);
        var helpExecutable = AddExecutable(model, "exec-help", helpCommand);
        if (includeDefaultCommand)
        {
            model.ApplicationDefaultCommandList.Add(new ApplicationDefaultCommand { Id = "app-demo:default", Application = app, ExecutableCommand = helpExecutable });
        }

        var newWorkspaceCommand = AddCommand(model, app, "cmd-new-workspace", "new-workspace", "new-workspace", null);
        var newWorkspaceExecutable = AddExecutable(model, "exec-new-workspace", newWorkspaceCommand);
        var newWorkspacePath = AddParameter(model, newWorkspaceExecutable, "param-new-workspace", "Path", path, required: true);
        model.PositionalArgumentList.Add(new PositionalArgument { Id = "pos-new-workspace", Parameter = newWorkspacePath });

        var modelCommand = AddCommand(model, app, "cmd-model", "model", "model", null);
        var addPropertyCommand = AddCommand(model, app, "cmd-add-property", "add-property", "add-property", modelCommand);
        var addPropertyExecutable = AddExecutable(model, "exec-add-property", addPropertyCommand);
        AddOption(model, addPropertyExecutable, "param-visibility", "option-visibility", "token-visibility", "visibility", visibility, "--visibility", defaultValue: "public");
        var entityParameter = AddParameter(model, addPropertyExecutable, "param-entity", "Entity", text, required: true);
        var propertyParameter = AddParameter(model, addPropertyExecutable, "param-property", "Property", text, required: true);
        var entityArgument = new PositionalArgument { Id = "pos-entity", Parameter = entityParameter };
        model.PositionalArgumentList.Add(entityArgument);
        model.PositionalArgumentList.Add(new PositionalArgument { Id = "pos-property", Parameter = propertyParameter, PreviousArgument = entityArgument });

        var addEntityCommand = AddCommand(model, app, "cmd-add-entity", "add-entity", "add-entity", modelCommand);
        var addEntityExecutable = AddExecutable(model, "exec-add-entity", addEntityCommand);
        var idParameter = AddParameter(model, addEntityExecutable, "param-id", "Id", text);
        var autoIdParameter = AddOption(model, addEntityExecutable, "param-auto-id", "option-auto-id", "token-auto-id", "auto-id", flag, "--auto-id");
        model.PositionalArgumentList.Add(new PositionalArgument { Id = "pos-id", Parameter = idParameter });
        var group = new ParameterGroup { Id = "group-id-choice", ExecutableCommand = addEntityExecutable, Name = "IdChoice", IsRequired = "true", AllowsMultiple = "false" };
        var idMember = new ParameterGroupMember { Id = "group-id-choice-id", ParameterGroup = group, Parameter = idParameter };
        model.ParameterGroupList.Add(group);
        model.ParameterGroupMemberList.Add(idMember);
        model.ParameterGroupMemberList.Add(new ParameterGroupMember { Id = "group-id-choice-auto", ParameterGroup = group, Parameter = autoIdParameter, PreviousMember = idMember });

        return model;
    }

    private static Command AddCommand(
        MetaCliModel model,
        Application application,
        string id,
        string name,
        string? token,
        Command? parent)
    {
        var command = new Command
        {
            Id = id,
            Application = application,
            Name = name,
            Token = token,
            ParentCommand = parent,
        };
        model.CommandList.Add(command);
        return command;
    }

    private static ExecutableCommand AddExecutable(MetaCliModel model, string id, Command command)
    {
        var executable = new ExecutableCommand { Id = id, Command = command };
        model.ExecutableCommandList.Add(executable);
        return executable;
    }

    private static Parameter AddApplicationOption(
        MetaCliModel model,
        Application application,
        string parameterId,
        string optionId,
        string tokenId,
        string name,
        ValueShape valueShape,
        string token)
    {
        var parameter = AddParameterRow(model, parameterId, name, valueShape, required: false);
        model.ApplicationParameterList.Add(new ApplicationParameter
        {
            Id = application.Id + ":parameter:" + parameter.Id,
            Application = application,
            Parameter = parameter,
        });
        AddOptionRow(model, parameter, optionId, tokenId, token);
        return parameter;
    }

    private static Parameter AddOption(
        MetaCliModel model,
        ExecutableCommand executableCommand,
        string parameterId,
        string optionId,
        string tokenId,
        string name,
        ValueShape valueShape,
        string token,
        bool required = false,
        string? defaultValue = null)
    {
        var parameter = AddParameter(model, executableCommand, parameterId, name, valueShape, required, defaultValue);
        AddOptionRow(model, parameter, optionId, tokenId, token);
        return parameter;
    }

    private static Parameter AddParameter(
        MetaCliModel model,
        ExecutableCommand executableCommand,
        string id,
        string name,
        ValueShape valueShape,
        bool required = false,
        string? defaultValue = null)
    {
        var parameter = AddParameterRow(model, id, name, valueShape, required, defaultValue);
        model.ExecutableCommandParameterList.Add(new ExecutableCommandParameter
        {
            Id = executableCommand.Id + ":parameter:" + parameter.Id,
            ExecutableCommand = executableCommand,
            Parameter = parameter,
        });
        return parameter;
    }

    private static Parameter AddParameterRow(
        MetaCliModel model,
        string id,
        string name,
        ValueShape valueShape,
        bool required,
        string? defaultValue = null)
    {
        var parameter = new Parameter
        {
            Id = id,
            ValueShape = valueShape,
            Name = name,
            IsRequired = required ? "true" : null,
            DefaultValue = defaultValue,
        };
        model.ParameterList.Add(parameter);
        return parameter;
    }

    private static void AddOptionRow(
        MetaCliModel model,
        Parameter parameter,
        string optionId,
        string tokenId,
        string token)
    {
        var option = new Option { Id = optionId, Parameter = parameter };
        model.OptionList.Add(option);
        model.OptionTokenList.Add(new OptionToken { Id = tokenId, Option = option, Token = token });
    }

    private static void AddOptionAlias(
        MetaCliModel model,
        string optionId,
        string tokenId,
        string token,
        string previousTokenId)
    {
        var option = model.OptionList.Single(item => item.Id == optionId);
        var previousToken = model.OptionTokenList.Single(item => item.Id == previousTokenId);
        model.OptionTokenList.Add(new OptionToken
        {
            Id = tokenId,
            Option = option,
            Token = token,
            PreviousToken = previousToken,
        });
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

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TempDirectory Create()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MetaCli.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return new TempDirectory(path);
        }

        public string SaveCommandSurface(MetaCliModel model)
        {
            var workspace = System.IO.Path.Combine(Path, "CommandSurface.MetaCli");
            model.SaveToXmlWorkspace(workspace);
            return workspace;
        }

        public string SaveDomainModel(string relativePath)
        {
            var workspace = System.IO.Path.GetFullPath(System.IO.Path.Combine(Path, relativePath));
            var model = MetaCliModel.CreateEmpty();
            model.ApplicationList.Add(new Application
            {
                Id = "app-domain",
                Name = "domain",
                ExecutableName = "domain",
            });
            model.SaveToXmlWorkspace(workspace);
            return workspace;
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
