using MetaCli.Core;

namespace MetaCli.Tests;

public sealed class MetaCliRuntimeTests
{
    [Fact]
    public void Runtime_DispatchesModeledCommandAndBindsArguments()
    {
        var model = CreateRuntimeModel();
        MetaCliInvocation? captured = null;
        var runtime = new MetaCliRuntimeBuilder(model)
            .Bind("exec-add-property", invocation =>
            {
                captured = invocation;
                return 0;
            })
            .Build();

        var result = runtime.Run("model", "add-property", "--workspace=Demo.Meta", "Customer", "Name");

        Assert.True(result.Succeeded, result.Message);
        Assert.Same(captured, result.Invocation);
        Assert.NotNull(captured);
        Assert.Equal("model add-property", captured.CommandRoute);
        Assert.Equal("Demo.Meta", captured.Required("workspace"));
        Assert.Equal("Customer", captured.Required("Entity"));
        Assert.Equal("Name", captured.Required("Property"));
        Assert.True(captured.IsPresent("param-workspace"));
    }

    [Fact]
    public void Runtime_DispatchesDefaultCommand()
    {
        var model = CreateRuntimeModel();
        MetaCliInvocation? captured = null;
        var runtime = new MetaCliRuntimeBuilder(model)
            .Bind("exec-root", invocation =>
            {
                captured = invocation;
                return 0;
            })
            .Build();

        var result = runtime.Run("--new-workspace", "Demo.MetaCli");

        Assert.True(result.Succeeded, result.Message);
        Assert.NotNull(captured);
        Assert.Equal("root", captured.CommandRoute);
        Assert.Equal("Demo.MetaCli", captured.Required("new-workspace"));
    }

    [Fact]
    public void Runtime_FailsWhenRunnableCommandHasNoImplementation()
    {
        var model = CreateRuntimeModel();
        var runtime = new MetaCliRuntimeBuilder(model).Build();

        var result = runtime.Run("model", "add-property", "--workspace", "Demo.Meta", "Customer", "Name");

        Assert.Equal(3, result.ExitCode);
        Assert.Contains("Command 'model add-property' has no registered implementation", result.Message);
        Assert.Contains("exec-add-property", result.Message);
        Assert.NotNull(result.Invocation);
    }

    [Fact]
    public void Runtime_RejectsUnknownOption()
    {
        var model = CreateRuntimeModel();
        var runtime = new MetaCliRuntimeBuilder(model)
            .Bind("exec-add-property", static _ => 0)
            .Build();

        var result = runtime.Run("model", "add-property", "--banana", "Demo.Meta", "Customer", "Name");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Option '--banana' is not recognized.", result.Message);
    }

    [Fact]
    public void Runtime_EnforcesRequiredParameterGroup()
    {
        var model = CreateRuntimeModel();
        var callCount = 0;
        var runtime = new MetaCliRuntimeBuilder(model)
            .Bind("exec-add-entity", _ =>
            {
                callCount++;
                return 0;
            })
            .Build();

        var missing = runtime.Run("model", "add-entity");
        var provided = runtime.Run("model", "add-entity", "--auto-id");
        var multiple = runtime.Run("model", "add-entity", "Customer", "--auto-id");

        Assert.Equal(2, missing.ExitCode);
        Assert.Contains("Parameter group 'IdChoice' requires one of: Id, auto-id.", missing.Message);
        Assert.True(provided.Succeeded, provided.Message);
        Assert.Equal(2, multiple.ExitCode);
        Assert.Contains("Parameter group 'IdChoice' accepts only one member.", multiple.Message);
        Assert.Equal(1, callCount);
    }

    [Fact]
    public void Runtime_EnforcesAllowedValues()
    {
        var model = CreateRuntimeModel();
        var runtime = new MetaCliRuntimeBuilder(model)
            .Bind("exec-add-property", static _ => 0)
            .Build();

        var result = runtime.Run("model", "add-property", "--workspace", "Demo.Meta", "--visibility", "private", "Customer", "Name");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Parameter 'visibility' does not allow value 'private'.", result.Message);
    }

    private static MetaCliModel CreateRuntimeModel()
    {
        var model = MetaCliModel.CreateEmpty();
        var app = new Application { Id = "app-demo", Name = "demo", ExecutableName = "demo" };
        model.ApplicationList.Add(app);

        var duplicateBehavior = new DuplicateOptionBehavior { Id = "duplicate-error", Name = "Error" };
        var unknownBehavior = new UnknownTokenBehavior { Id = "unknown-error", Name = "Error" };
        model.DuplicateOptionBehaviorList.Add(duplicateBehavior);
        model.UnknownTokenBehaviorList.Add(unknownBehavior);
        model.ParserPolicyList.Add(new ParserPolicy
        {
            Id = "parser-default",
            Application = app,
            Name = "Default",
            StopParsingToken = "--",
            AllowsEqualsValueSyntax = "true",
            AllowsOptionsAfterPositionals = "true",
            AllowsShortOptionClusters = "false",
            DuplicateOptionBehavior = duplicateBehavior,
            UnknownTokenBehavior = unknownBehavior,
        });

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
        var publicValue = new AllowedValue { Id = "visibility-public", ValueShape = visibility, Value = "public" };
        var internalValue = new AllowedValue { Id = "visibility-internal", ValueShape = visibility, Value = "internal", PreviousValue = publicValue };
        model.AllowedValueList.Add(publicValue);
        model.AllowedValueList.Add(internalValue);

        var rootCommand = AddCommand(model, app, "cmd-root", "root", null, null);
        var rootExecutable = AddExecutable(model, "exec-root", rootCommand);
        model.ApplicationDefaultCommandList.Add(new ApplicationDefaultCommand { Id = "app-demo:default", Application = app, ExecutableCommand = rootExecutable });
        AddOption(model, rootExecutable, "param-new-workspace", "option-new-workspace", "token-new-workspace", "new-workspace", path, "--new-workspace");

        var modelCommand = AddCommand(model, app, "cmd-model", "model", "model", null);
        var addPropertyCommand = AddCommand(model, app, "cmd-add-property", "add-property", "add-property", modelCommand);
        var addPropertyExecutable = AddExecutable(model, "exec-add-property", addPropertyCommand);
        AddOption(model, addPropertyExecutable, "param-workspace", "option-workspace", "token-workspace", "workspace", path, "--workspace", required: true);
        AddOption(model, addPropertyExecutable, "param-visibility", "option-visibility", "token-visibility", "visibility", visibility, "--visibility");
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

    private static Parameter AddOption(
        MetaCliModel model,
        ExecutableCommand executableCommand,
        string parameterId,
        string optionId,
        string tokenId,
        string name,
        ValueShape valueShape,
        string token,
        bool required = false)
    {
        var parameter = AddParameter(model, executableCommand, parameterId, name, valueShape, required);
        var option = new Option { Id = optionId, Parameter = parameter };
        model.OptionList.Add(option);
        model.OptionTokenList.Add(new OptionToken { Id = tokenId, Option = option, Token = token, IsPrimary = "true" });
        return parameter;
    }

    private static Parameter AddParameter(
        MetaCliModel model,
        ExecutableCommand executableCommand,
        string id,
        string name,
        ValueShape valueShape,
        bool required = false)
    {
        var parameter = new Parameter
        {
            Id = id,
            ExecutableCommand = executableCommand,
            ValueShape = valueShape,
            Name = name,
            IsRequired = required ? "true" : null,
        };
        model.ParameterList.Add(parameter);
        return parameter;
    }
}
