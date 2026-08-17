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
            .Bind("exec-add-string-parameter", RunAddStringParameter)
            .Bind("exec-add-relation", RunAddRelation)
            .Bind("exec-add-requirement", RunAddRequirement)
            .Bind("exec-add-transformation", RunAddTransformation)
            .Bind("exec-update-requirement", RunUpdateRequirement)
            .Bind("exec-update-relation", RunUpdateRelation)
            .Bind("exec-update-transformation", RunUpdateTransformation)
            .BindReadOnly("exec-show", RunShow)
            .BindReadOnly("exec-emit-requirement", RunEmitRequirement)
            .BindReadOnly("exec-emit-relation", RunEmitRelation)
            .BindReadOnly("exec-emit-transformation", RunEmitTransformation)
            .Bind(
                "exec-execute",
                [
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
            invocation.Required("name"));
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
            ParseAssignments(invocation.Values("source"), "source model")
                .Select(source => new MetaWeaveSourceWorkspaceDefinition(source.Key, source.Value))
                .ToArray(),
            invocation.Required("target-model"));
        Presenter.WriteOk("MetaWeave direction added");
    }

    private static void RunAddStringParameter(
        MetaCliInvocation invocation,
        MetaWeaveModel model)
    {
        _ = new MetaWeaveAuthoringService().AddStringParameter(
            model,
            invocation.Required("direction"),
            invocation.Required("name"));
        Presenter.WriteOk("MetaWeave string parameter added");
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

    private static void RunAddRelation(
        MetaCliInvocation invocation,
        MetaWeaveModel model)
    {
        _ = new MetaWeaveAuthoringService().AddRelation(
            model,
            invocation.Required("direction"),
            invocation.Required("name"),
            MetaCliStandardInput.ReadToEnd());
        Presenter.WriteOk("MetaWeave named relation added");
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

    private static void RunUpdateRelation(
        MetaCliInvocation invocation,
        MetaWeaveModel model)
    {
        _ = new MetaWeaveAuthoringService().UpdateRelation(
            model,
            invocation.Required("direction"),
            invocation.Required("name"),
            MetaCliStandardInput.ReadToEnd());
        Presenter.WriteOk("MetaWeave named relation updated");
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
        Presenter.WriteInfo($"Directions: {model.DirectionList.Count}");
        foreach (var direction in model.DirectionList)
        {
            var sources = model.DirectionSourceWorkspaceList.Where(source =>
                string.Equals(
                    source.Direction?.Id,
                    direction.Id,
                    StringComparison.OrdinalIgnoreCase)).ToArray();
            var parameters = model.DirectionStringParameterList.Where(parameter =>
                string.Equals(
                    parameter.Direction?.Id,
                    direction.Id,
                    StringComparison.OrdinalIgnoreCase)).ToArray();
            var requirements = model.DirectionRequirementList.Where(requirement =>
                string.Equals(
                    requirement.Direction?.Id,
                    direction.Id,
                    StringComparison.OrdinalIgnoreCase)).ToArray();
            var relations = model.DirectionRelationList.Where(relation =>
                string.Equals(
                    relation.Direction?.Id,
                    direction.Id,
                    StringComparison.OrdinalIgnoreCase)).ToArray();
            var transformations = model.TransformationList.Where(transformation =>
                string.Equals(
                    transformation.Direction?.Id,
                    direction.Id,
                    StringComparison.OrdinalIgnoreCase)).ToArray();
            Presenter.WriteInfo(
                $"  {direction.Id}: {string.Join(" + ", sources.Select(source => source.Name + ":" + source.ModelName))} -> {direction.TargetModelName} ({requirements.Length} requirements, {relations.Length} relations, {transformations.Length} transformations)");
            foreach (var parameter in parameters)
            {
                Presenter.WriteInfo($"    string @{parameter.Name}");
            }
            foreach (var requirement in requirements)
            {
                Presenter.WriteInfo(
                    $"    require {MetaWeaveAuthoringService.GetRequirementName(requirement)} [{requirement.Code}]");
            }
            foreach (var relation in relations.OrderBy(
                         MetaWeaveAuthoringService.GetRelationName,
                         StringComparer.OrdinalIgnoreCase))
            {
                Presenter.WriteInfo(
                    $"    relation {MetaWeaveAuthoringService.GetRelationName(relation)}");
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

    private static void RunEmitRelation(
        MetaCliInvocation invocation,
        MetaWeaveModel model)
    {
        var directionName = invocation.Required("direction");
        var relationName = invocation.Required("name");
        var relation = model.DirectionRelationList.SingleOrDefault(item =>
            string.Equals(item.Direction?.Id, directionName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                MetaWeaveAuthoringService.GetRelationName(item),
                relationName,
                StringComparison.OrdinalIgnoreCase))
            ?? throw new MetaCliExitException(
                4,
                $"Relation '{relationName}' was not found in direction '{directionName}'.");
        Console.Out.WriteLine(new MetaWeaveScript.Sql.MetaWeaveScriptSqlService()
            .ExportToSqlCode(model, relation.SelectStatement));
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
        var sourceLocations = ParseSourceWorkspaceLocations(
            invocation.Values("source-workspace"),
            direction.SourceWorkspaces);
        var openedSources = new List<IAsyncDisposable>();
        var sourceWorkspaces = new Dictionary<string, InMemoryWorkspace>(StringComparer.OrdinalIgnoreCase);
        using var progress = MetaCliProgressMeter.TryStart(initialDetail: "preparing");
        try
        {
            try
            {
                foreach (var source in sourceLocations)
                {
                    var opened = await MetaCliWorkspace.OpenAsync(source.Value).ConfigureAwait(false);
                    openedSources.Add(opened);
                    sourceWorkspaces.Add(
                        source.Key,
                        await WorkspaceComposition.MaterializeAsync(opened).ConfigureAwait(false));
                }

                var targetContract = await WorkspaceComposition.MaterializeAsync(
                        workspaces.Required("target-workspace"))
                    .ConfigureAwait(false);
                var targetWorkspace = new InMemoryWorkspace(
                    targetContract.Model.Clone(),
                    new GenericInstance { ModelName = targetContract.Model.Name });
                var parameters = ParseAssignments(invocation.Values("parameter"), "parameter");
                var result = new MetaWeaveScriptExecutionService().ExecuteDirection(
                    direction,
                    sourceWorkspaces,
                    targetWorkspace,
                    parameters,
                    progress: progress is null
                        ? null
                        : value => progress.Report(
                            value.CompletedTaskCount,
                            value.TotalTaskCount,
                            FormatExecutionTask(value)));
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
            }
            finally
            {
                foreach (var openedSource in openedSources.AsEnumerable().Reverse())
                {
                    await openedSource.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
        catch
        {
            progress?.Fail();
            throw;
        }

        progress?.Succeed();
        Presenter.WriteOk("MetaWeave direction executed into a new workspace");
    }

    private static IReadOnlyDictionary<string, string> ParseSourceWorkspaceLocations(
        IReadOnlyList<string> values,
        IReadOnlyList<MetaWeaveScriptSourceWorkspace> declaredSources)
    {
        if (values.Count == 1 && !values[0].Contains('=') && declaredSources.Count == 1)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [declaredSources[0].Name] = Path.GetFullPath(values[0])
            };
        }

        return ParseAssignments(values, "source workspace")
            .ToDictionary(
                source => source.Key,
                source => Path.GetFullPath(source.Value),
                StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, string> ParseAssignments(
        IReadOnlyList<string> values,
        string description)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            var separator = value.IndexOf('=');
            if (separator <= 0 || separator == value.Length - 1)
            {
                throw new MetaCliExitException(
                    2,
                    $"Each {description} must use name=value syntax; received '{value}'.");
            }

            var name = value[..separator].Trim();
            var assignedValue = value[(separator + 1)..].Trim();
            if (!result.TryAdd(name, assignedValue))
            {
                throw new MetaCliExitException(
                    2,
                    $"{description} name '{name}' was supplied more than once.");
            }
        }

        return result;
    }

    private static string FormatIssue(MetaWeaveScriptExecutionIssue issue)
    {
        var owner = !string.IsNullOrWhiteSpace(issue.RelationName)
            ? $" [relation {issue.RelationName}]"
            : !string.IsNullOrWhiteSpace(issue.RequirementName)
            ? $" [requirement {issue.RequirementName}]"
            : !string.IsNullOrWhiteSpace(issue.TransformationName)
                ? $" [transformation {issue.TransformationName}]"
                : string.Empty;
        return $"{issue.Code}{owner}: {issue.Message}";
    }

    private static string? FormatExecutionTask(MetaWeaveScriptExecutionProgress value)
    {
        if (string.IsNullOrWhiteSpace(value.CompletedTaskName))
        {
            return null;
        }

        return value.CompletedTaskKind switch
        {
            MetaWeaveScriptExecutionTaskKind.Requirement => $"requirement {value.CompletedTaskName}",
            MetaWeaveScriptExecutionTaskKind.Relation => $"relation {value.CompletedTaskName}",
            MetaWeaveScriptExecutionTaskKind.TargetEntity => $"target {value.CompletedTaskName}",
            _ => value.CompletedTaskName,
        };
    }
}
