using Meta.Operations.Domain;
using Meta.Core.Presentation;
using Meta.Core.Services;
using MetaCli.Core;
using MetaWeave.Core;
using MetaWeaveModel = global::MetaWeave.MetaWeaveModel;

internal static class Program
{
    private const string ApplicationId = "app-meta-weave";
    private const string CommandWorkspaceDirectoryName = "meta-weave.MetaCli";
    private static readonly ConsolePresenter Presenter = new();

    static int Main(string[] args)
    {
        if (Meta.Core.Presentation.Cli.CliVersion.TryWriteVersion(Presenter, "meta-weave", args, out var versionExitCode))
        {
            return versionExitCode;
        }

        Environment.ExitCode = 0;
        var runtime = new MetaCliRuntime<MetaWeaveModel>(CommandWorkspacePath, ApplicationId)
            .UseDefaultHelp()
            .Bind(
                "exec-create",
                [MetaCliWorkspace.Create("output", "xml", "csharp", "sql")],
                RunCreate)
            .Bind("exec-add-model", RunAddModel)
            .Bind("exec-add-binding", RunAddBinding)
            .BindReadOnly("exec-suggest", RunSuggest)
            .BindReadOnly("exec-check", RunCheck)
            .Bind(
                "exec-materialize",
                [MetaCliWorkspace.Create(
                    "output",
                    "output-xml",
                    "output-csharp",
                    "output-sql",
                    "output-connection-env")],
                RunMaterialize);

        runtime.Run(args);
        return Environment.ExitCode;
    }

    private static string CommandWorkspacePath =>
        Path.Combine(AppContext.BaseDirectory, CommandWorkspaceDirectoryName);

    private static async Task RunCreate(
        MetaCliInvocation invocation,
        MetaCliWorkspaces workspaces)
    {
        var workspacePath = invocation.Optional("xml") ??
            invocation.Optional("csharp") ??
            invocation.Optional("sql") ??
            throw new InvalidOperationException("A workspace output is required.");
        await workspaces.CreateAsync("output", MetaWeaveModel.CreateEmpty()).ConfigureAwait(false);

        Presenter.WriteOk(
            "weave workspace created",
            ("Workspace", workspacePath),
            ("Model", "MetaWeave"));
    }

    private static void RunAddModel(MetaCliInvocation invocation, MetaWeaveModel weaveModel)
    {
        var workspacePath = ResolveWorkspacePath(invocation);
        new MetaWeaveAuthoringService().AddModelReferenceAsync(
            weaveModel,
            workspacePath,
            invocation.Required("alias"),
            invocation.Required("model"),
            invocation.Required("workspace-path")).GetAwaiter().GetResult();
        Presenter.WriteOk(
            "weave model reference added",
            ("Workspace", workspacePath),
            ("Alias", invocation.Required("alias")),
            ("Model", invocation.Required("model")),
            ("WorkspacePath", invocation.Required("workspace-path")));
    }

    private static void RunAddBinding(MetaCliInvocation invocation, MetaWeaveModel weaveModel)
    {
        var workspacePath = ResolveWorkspacePath(invocation);
        new MetaWeaveAuthoringService().AddPropertyBindingAsync(
            weaveModel,
            workspacePath,
            invocation.Required("name"),
            invocation.Required("source-model"),
            invocation.Required("source-entity"),
            invocation.Required("source-property"),
            invocation.Required("target-model"),
            invocation.Required("target-entity"),
            invocation.Required("target-property")).GetAwaiter().GetResult();
        Presenter.WriteOk(
            "weave property binding added",
            ("Workspace", workspacePath),
            ("Binding", invocation.Required("name")));
    }

    private static void RunSuggest(MetaCliInvocation invocation, MetaWeaveModel weaveModel)
    {
        var workspacePath = ResolveWorkspacePath(invocation);
        var result = new MetaWeaveSuggestService().SuggestAsync(weaveModel, workspacePath).GetAwaiter().GetResult();

        Line("Binding suggestions");
        if (result.Suggestions.Count == 0)
        {
            Line("  (none)");
        }
        else
        {
            for (var index = 0; index < result.Suggestions.Count; index++)
            {
                var suggestion = result.Suggestions[index];
                var roleSuffix = string.IsNullOrWhiteSpace(suggestion.InferredRole)
                    ? string.Empty
                    : $" (role: {suggestion.InferredRole})";
                Line($"  {index + 1}) {suggestion.SourceModelAlias}.{suggestion.SourceEntity}.{suggestion.SourceProperty} -> {suggestion.TargetModelAlias}.{suggestion.TargetEntity}.{suggestion.TargetProperty}{roleSuffix}");
            }
        }

        if (result.WeakSuggestions.Count > 0)
        {
            Line(string.Empty);
            Line("Weak binding suggestions");
            var weakIndex = 1;
            foreach (var weakSuggestion in result.WeakSuggestions)
            {
                foreach (var candidate in weakSuggestion.Candidates)
                {
                    var roleSuffix = string.IsNullOrWhiteSpace(candidate.InferredRole)
                        ? string.Empty
                        : $" (role: {candidate.InferredRole})";
                    Line($"  {weakIndex}) {weakSuggestion.SourceModelAlias}.{weakSuggestion.SourceEntity}.{weakSuggestion.SourceProperty} -> {candidate.TargetModelAlias}.{candidate.TargetEntity}.{candidate.TargetProperty}{roleSuffix}");
                    weakIndex++;
                }
            }
        }
    }

    private static void RunCheck(MetaCliInvocation invocation, MetaWeaveModel weaveModel)
    {
        var workspacePath = ResolveWorkspacePath(invocation);
        var result = new MetaWeaveService().CheckAsync(weaveModel, workspacePath).GetAwaiter().GetResult();
        if (result.HasErrors)
        {
            throw new InvalidOperationException(string.Join(
                Environment.NewLine,
                result.Bindings.SelectMany(binding => binding.Errors.Select(error => $"{binding.BindingName}: {error}"))));
        }

        Presenter.WriteOk(
            "weave check",
            ("Workspace", workspacePath),
            ("Bindings", result.BindingCount.ToString()),
            ("ResolvedRows", result.ResolvedRowCount.ToString()),
            ("Errors", result.ErrorCount.ToString()));
    }

    private static async Task RunMaterialize(
        MetaCliInvocation invocation,
        MetaWeaveModel weaveModel,
        MetaCliWorkspaces workspaces)
    {
        var weaveWorkspacePath = ResolveWorkspacePath(invocation);
        var output = MetaCliWorkspace.OutputLocation(
            invocation,
            "output-xml",
            "output-csharp",
            "output-sql");

        var result = await new MetaWeaveService()
            .MaterializeAsync(
                weaveModel,
                weaveWorkspacePath,
                invocation.Required("model"))
            .ConfigureAwait(false);
        await workspaces.CreateAsync("output", result.Workspace).ConfigureAwait(false);

        Presenter.WriteOk(
            "weave materialize",
            ("Weave", weaveWorkspacePath),
            ("Workspace", output),
            ("Model", result.Workspace.Model.Name),
            ("Entities", result.Workspace.Model.Entities.Count.ToString()),
            ("BindingsMaterialized", result.BindingsMaterialized.ToString()));
    }

    private static string ResolveWorkspacePath(MetaCliInvocation invocation)
    {
        var workspace = invocation.Optional("workspace");
        return Path.GetFullPath(string.IsNullOrWhiteSpace(workspace)
            ? Directory.GetCurrentDirectory()
            : workspace);
    }

    private static void Line(string message)
    {
        Presenter.WriteInfo(message);
    }
}
