using Meta.Core.Domain;
using Meta.Core.Operations;
using MetaWorkspaceConfig = Meta.Core.WorkspaceConfig.Generated.MetaWorkspace;

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

        var leftWorkspace = await services.WorkspaceService.LoadAsync(leftWorkspacePath, searchUpward: false).ConfigureAwait(false);
        var rightWorkspace = await services.WorkspaceService.LoadAsync(rightWorkspacePath, searchUpward: false).ConfigureAwait(false);

        var mergedWorkspace = new Meta.Core.Domain.Workspace
        {
            WorkspaceRootPath = newWorkspacePath,
            MetadataRootPath = newWorkspacePath,
            WorkspaceConfig = MetaWorkspaceConfig.CreateDefault(),
            Model = new GenericModel { Name = parse.ModelName },
            Instance = new GenericInstance { ModelName = parse.ModelName },
            IsDirty = true,
        };

        WorkspaceMergeResult mergeResult;
        try
        {
            mergeResult = services.WorkspaceMergeService.MergeInto(
                mergedWorkspace,
                new[] { leftWorkspace, rightWorkspace },
                new WorkspaceMergeOptions(parse.ModelName));
        }
        catch (InvalidOperationException exception)
        {
            return PrintDataError("E_OPERATION", exception.Message);
        }

        var diagnostics = services.ValidationService.Validate(mergedWorkspace);
        mergedWorkspace.Diagnostics = diagnostics;
        if (diagnostics.HasErrors || (globalStrict && diagnostics.WarningCount > 0))
        {
            return PrintOperationValidationFailure("workspace merge", Array.Empty<WorkspaceOp>(), diagnostics);
        }

        await services.WorkspaceService.SaveAsync(mergedWorkspace).ConfigureAwait(false);
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
