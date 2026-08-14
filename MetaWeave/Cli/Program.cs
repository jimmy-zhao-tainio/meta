using Meta.Core.Presentation;
using Meta.Core.Presentation.Cli;
using Meta.Operations;
using Meta.Operations.Domain;
using Meta.Surfaces;
using MetaCli.Core;
using MetaWeave;
using MetaWeave.Core;
using MetaWeaveScript.Execution;

internal static class Program
{
    private const string AppName = "meta-weave";
    private const string ApplicationId = "app-meta-weave";
    private const string CommandWorkspaceDirectoryName = "meta-weave.MetaCli";
    private static readonly ConsolePresenter Presenter = new();

    private static int Main(string[] args)
    {
        if (CliVersion.TryWriteVersion(Presenter, AppName, args, out var versionExitCode))
        {
            return versionExitCode;
        }

        var exitCode = 0;
        var runtime = new MetaCliRuntime<MetaWeaveModel>(
                CommandWorkspacePath,
                ApplicationId,
                setExitCode: code => exitCode = code)
            .UseDefaultHelp(options: new MetaCliHelpOptions("meta-weave show"))
            .Bind(
                "exec-create",
                [MetaCliWorkspace.Create("output", "xml", "csharp", "sql")],
                RunCreate)
            .Bind("exec-add-direction", RunAddDirection)
            .Bind("exec-add-requirement", RunAddRequirement)
            .Bind("exec-add-transformation", RunAddTransformation)
            .Bind("exec-update-requirement", RunUpdateRequirement)
            .Bind("exec-update-transformation", RunUpdateTransformation)
            .BindReadOnly("exec-show", RunShow)
            .BindReadOnly("exec-emit-requirement", RunEmitRequirement)
            .BindReadOnly("exec-emit-transformation", RunEmitTransformation)
            .Bind(
                "exec-execute",
                [
                    MetaCliWorkspace.Open("source-workspace"),
                    MetaCliWorkspace.Open("target-workspace"),
                    MetaCliWorkspace.Create("output", "xml", "csharp", "sql"),
                ],
                RunExecute);

        runtime.Run(args);
        return exitCode;
    }

    private static string CommandWorkspacePath =>
        Path.Combine(AppContext.BaseDirectory, CommandWorkspaceDirectoryName);

    private static async Task RunCreate(
        MetaCliInvocation invocation,
        MetaCliWorkspaces workspaces)
    {
        var model = new MetaWeaveAuthoringService().Create(
            invocation.Required("name"),
            invocation.Required("left-model"),
            invocation.Required("right-model"));
        await workspaces.CreateAsync("output", model).ConfigureAwait(false);
        Presenter.WriteOk("MetaWeave workspace created");
    }

    private static void RunAddDirection(
        MetaCliInvocation invocation,
        MetaWeaveModel model)
    {
        _ = new MetaWeaveAuthoringService().AddDirection(
            model,
            invocation.Required("name"),
            invocation.Required("source-model"),
            invocation.Required("target-model"));
        Presenter.WriteOk("MetaWeave direction added");
    }

    private static void RunAddTransformation(
        MetaCliInvocation invocation,
        MetaWeaveModel model)
    {
        _ = new MetaWeaveAuthoringService().AddTransformation(
            model,
            invocation.Required("direction"),
            invocation.Required("name"),
            invocation.Required("target-entity"),
            MetaCliStandardInput.ReadToEnd());
        Presenter.WriteOk("MetaWeave transformation added");
    }

    private static void RunAddRequirement(
        MetaCliInvocation invocation,
        MetaWeaveModel model)
    {
        _ = new MetaWeaveAuthoringService().AddRequirement(
            model,
            invocation.Required("direction"),
            invocation.Required("name"),
            invocation.Required("code"),
            invocation.Required("message"),
            MetaCliStandardInput.ReadToEnd());
        Presenter.WriteOk("MetaWeave direction requirement added");
    }

    private static void RunUpdateTransformation(
        MetaCliInvocation invocation,
        MetaWeaveModel model)
    {
        _ = new MetaWeaveAuthoringService().UpdateTransformation(
            model,
            invocation.Required("direction"),
            invocation.Required("name"),
            MetaCliStandardInput.ReadToEnd());
        Presenter.WriteOk("MetaWeave transformation updated");
    }

    private static void RunUpdateRequirement(
        MetaCliInvocation invocation,
        MetaWeaveModel model)
    {
        _ = new MetaWeaveAuthoringService().UpdateRequirement(
            model,
            invocation.Required("direction"),
            invocation.Required("name"),
            MetaCliStandardInput.ReadToEnd());
        Presenter.WriteOk("MetaWeave direction requirement updated");
    }

    private static void RunShow(
        MetaCliInvocation invocation,
        MetaWeaveModel model)
    {
        var weave = model.WeaveList.Single();
        Presenter.WriteInfo($"Weave: {weave.Id}");
        Presenter.WriteInfo($"Left model: {weave.LeftModelName}");
        Presenter.WriteInfo($"Right model: {weave.RightModelName}");
        Presenter.WriteInfo($"Directions: {model.DirectionList.Count}");
        foreach (var direction in model.DirectionList)
        {
            var requirements = model.DirectionRequirementList.Where(requirement =>
                string.Equals(
                    requirement.Direction?.Id,
                    direction.Id,
                    StringComparison.OrdinalIgnoreCase)).ToArray();
            var transformations = model.TransformationList.Where(transformation =>
                string.Equals(
                    transformation.Direction?.Id,
                    direction.Id,
                    StringComparison.OrdinalIgnoreCase)).ToArray();
            Presenter.WriteInfo(
                $"  {direction.Id}: {direction.SourceModelName} -> {direction.TargetModelName} ({requirements.Length} requirements, {transformations.Length} transformations)");
            foreach (var requirement in requirements)
            {
                Presenter.WriteInfo(
                    $"    require {MetaWeaveAuthoringService.GetRequirementName(requirement)} [{requirement.Code}]");
            }

            foreach (var transformation in transformations)
            {
                Presenter.WriteInfo(
                    $"    transform {MetaWeaveAuthoringService.GetTransformationName(transformation)} -> {transformation.TargetEntityName}");
            }
        }
    }

    private static void RunEmitRequirement(
        MetaCliInvocation invocation,
        MetaWeaveModel model)
    {
        var directionName = invocation.Required("direction");
        var requirementName = invocation.Required("name");
        var requirement = model.DirectionRequirementList.SingleOrDefault(item =>
            string.Equals(item.Direction?.Id, directionName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                MetaWeaveAuthoringService.GetRequirementName(item),
                requirementName,
                StringComparison.OrdinalIgnoreCase))
            ?? throw new MetaCliExitException(
                4,
                $"Requirement '{requirementName}' was not found in direction '{directionName}'.");
        Console.Out.WriteLine(new MetaWeaveScript.Sql.MetaWeaveScriptSqlService()
            .ExportToSqlCode(model, requirement.SelectStatement));
    }

    private static void RunEmitTransformation(
        MetaCliInvocation invocation,
        MetaWeaveModel model)
    {
        var directionName = invocation.Required("direction");
        var transformationName = invocation.Required("name");
        var transformation = model.TransformationList.SingleOrDefault(item =>
            string.Equals(item.Direction?.Id, directionName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                MetaWeaveAuthoringService.GetTransformationName(item),
                transformationName,
                StringComparison.OrdinalIgnoreCase))
            ?? throw new MetaCliExitException(
                4,
                $"Transformation '{transformationName}' was not found in direction '{directionName}'.");
        Console.Out.WriteLine(new MetaWeaveScript.Sql.MetaWeaveScriptSqlService()
            .ExportToSqlCode(model, transformation.SelectStatement));
    }

    private static async Task RunExecute(
        MetaCliInvocation invocation,
        MetaWeaveModel model,
        MetaCliWorkspaces workspaces)
    {
        var direction = new MetaWeaveScriptDirectionLoader().Load(
            model,
            invocation.Optional("direction") ?? "forward");
        var sourceWorkspace = await WorkspaceComposition.MaterializeAsync(
                workspaces.Required("source-workspace"))
            .ConfigureAwait(false);
        var targetContract = await WorkspaceComposition.MaterializeAsync(
                workspaces.Required("target-workspace"))
            .ConfigureAwait(false);
        var targetWorkspace = new InMemoryWorkspace(
            targetContract.Model.Clone(),
            new GenericInstance { ModelName = targetContract.Model.Name });
        var result = new MetaWeaveScriptExecutionService().ExecuteDirection(
            direction,
            sourceWorkspace,
            targetWorkspace);
        if (!result.IsSuccess)
        {
            throw new MetaCliExitException(
                4,
                string.Join(
                    Environment.NewLine,
                    result.Issues.Select(FormatIssue)));
        }

        await workspaces.CreateAsync("output", result.OutputWorkspace!)
            .ConfigureAwait(false);

        Presenter.WriteOk("MetaWeave direction executed into a new workspace");
    }

    private static string FormatIssue(MetaWeaveScriptExecutionIssue issue)
    {
        var owner = !string.IsNullOrWhiteSpace(issue.RequirementName)
            ? $" [requirement {issue.RequirementName}]"
            : !string.IsNullOrWhiteSpace(issue.TransformationName)
                ? $" [transformation {issue.TransformationName}]"
                : string.Empty;
        return $"{issue.Code}{owner}: {issue.Message}";
    }
}
