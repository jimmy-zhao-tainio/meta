using Meta.Core.Operations;

internal sealed partial class CliRuntime
{
    async Task<int> WorkspaceMergeAsync(string[] commandArgs)
    {
        var leftWorkspacePath = Path.GetFullPath(RequiredValue("leftWorkspace"));
        var rightWorkspacePath = Path.GetFullPath(RequiredValue("rightWorkspace"));
        var parse = ReadNewWorkspaceAndModelOptions(commandArgs, startIndex: 4);
        if (!parse.Ok)
        {
            return PrintArgumentError(parse.ErrorMessage);
        }

        var newWorkspacePath = Path.GetFullPath(parse.NewWorkspacePath);
        if (Directory.Exists(newWorkspacePath) && Directory.EnumerateFileSystemEntries(newWorkspacePath).Any())
        {
            return PrintDataError("E_OPERATION", $"target directory '{newWorkspacePath}' must be empty.");
        }

        var leftWorkspace = await OpenXmlWorkspaceForCommandAsync(
                leftWorkspacePath)
            .ConfigureAwait(false);
        var rightWorkspace = await OpenXmlWorkspaceForCommandAsync(
                rightWorkspacePath)
            .ConfigureAwait(false);

        WorkspaceMergePlan mergePlan;
        try
        {
            mergePlan = await services.WorkspaceMergeService.MergeAsync(
                    new IMetaWorkspaceSource[]
                    {
                        CreateWorkspaceSource(leftWorkspace.State),
                        CreateWorkspaceSource(rightWorkspace.State),
                    },
                    new WorkspaceMergeOptions(parse.ModelName))
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

        try
        {
            await XmlWorkspaceWriter.WriteMergedAsync(
                    mergePlan.Workspace,
                    newWorkspacePath,
                    new[] { leftWorkspace, rightWorkspace })
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException exception)
        {
            return PrintDataError("E_OPERATION", exception.Message);
        }

        var mergeResult = mergePlan.Result;
        presenter.WriteOk(
            "workspace merged",
            ("Path", newWorkspacePath),
            ("Model", mergeResult.MergedModelName),
            ("SourceWorkspaces", mergeResult.SourceWorkspaceCount.ToString(CultureInfo.InvariantCulture)),
            ("Entities", mergeResult.EntitiesMerged.ToString(CultureInfo.InvariantCulture)),
            ("Rows", mergeResult.RowsMerged.ToString(CultureInfo.InvariantCulture)));

        return 0;
    }

    private (bool Ok, string NewWorkspacePath, string ModelName, string ErrorMessage) ReadNewWorkspaceAndModelOptions(string[] commandArgs, int startIndex)
    {
        return (true, RequiredValue("new-workspace"), RequiredValue("model"), string.Empty);
    }
}
