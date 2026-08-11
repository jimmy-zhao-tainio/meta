using Meta.Operations;
using MetaCli.Core;

internal sealed partial class CliRuntime
{
    async Task<int> WorkspaceMergeAsync(string[] commandArgs)
    {
        var leftWorkspace = CurrentWorkspaces.Required("leftWorkspace");
        var rightWorkspace = CurrentWorkspaces.Required("rightWorkspace");
        var modelName = RequiredValue("model");

        WorkspaceMergePlan mergePlan;
        try
        {
            mergePlan = await services.WorkspaceMergeService.MergeAsync(
                    [leftWorkspace, rightWorkspace],
                    new WorkspaceMergeOptions(modelName))
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException exception)
        {
            return PrintDataError("E_OPERATION", exception.Message);
        }

        var diagnostics = WorkspaceValidator.Validate(
            mergePlan.Workspace.Model,
            mergePlan.Workspace.Instance);
        if (diagnostics.HasErrors || (globalStrict && diagnostics.WarningCount > 0))
        {
            return PrintOperationValidationFailure("workspace merge", Array.Empty<Operation>(), diagnostics);
        }

        await CurrentWorkspaces.CreateAsync("output", mergePlan.Workspace).ConfigureAwait(false);

        var mergeResult = mergePlan.Result;
        presenter.WriteOk(
            "workspace merged",
            ("Path", MetaCliWorkspace.OutputLocation(Invocation, "output-xml", "output-csharp", "output-sql")),
            ("Model", mergeResult.MergedModelName),
            ("SourceWorkspaces", mergeResult.SourceWorkspaceCount.ToString(CultureInfo.InvariantCulture)),
            ("Entities", mergeResult.EntitiesMerged.ToString(CultureInfo.InvariantCulture)),
            ("Rows", mergeResult.RowsMerged.ToString(CultureInfo.InvariantCulture)));

        return 0;
    }
}
