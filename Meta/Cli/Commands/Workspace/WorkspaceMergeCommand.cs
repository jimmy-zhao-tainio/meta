using Meta.Core.Domain;
using Meta.Core.Operations;
using MetaWorkspaceConfig = Meta.Core.WorkspaceConfig.Generated.MetaWorkspace;

internal sealed partial class CliRuntime
{
    async Task<int> WorkspaceMergeAsync(string[] commandArgs)
    {
        if (commandArgs.Length == 2 || IsHelpToken(commandArgs[2]))
        {
            return PrintUsageError("Usage: workspace merge <leftWorkspace> <rightWorkspace> --new-workspace <path> --model <name>");
        }

        if (commandArgs.Length < 6)
        {
            return PrintUsageError("Usage: workspace merge <leftWorkspace> <rightWorkspace> --new-workspace <path> --model <name>");
        }

        var leftWorkspacePath = Path.GetFullPath(commandArgs[2]);
        var rightWorkspacePath = Path.GetFullPath(commandArgs[3]);
        var parse = ParseNewWorkspaceAndModelOptions(commandArgs, startIndex: 4);
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
            MetadataRootPath = Path.Combine(newWorkspacePath, "metadata"),
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

    private static (bool Ok, string NewWorkspacePath, string ModelName, string ErrorMessage) ParseNewWorkspaceAndModelOptions(string[] commandArgs, int startIndex)
    {
        var newWorkspacePath = string.Empty;
        var modelName = string.Empty;

        for (var i = startIndex; i < commandArgs.Length; i++)
        {
            var arg = commandArgs[i];
            if (i + 1 >= commandArgs.Length)
            {
                return (false, newWorkspacePath, modelName, $"missing value for {arg}.");
            }

            switch (arg.ToLowerInvariant())
            {
                case "--new-workspace":
                    newWorkspacePath = EnsureUnsetThenAssignLocal(newWorkspacePath, commandArgs[++i], "--new-workspace");
                    break;
                case "--model":
                    modelName = EnsureUnsetThenAssignLocal(modelName, commandArgs[++i], "--model");
                    break;
                default:
                    return (false, newWorkspacePath, modelName, $"unknown option '{arg}'.");
            }
        }

        if (string.IsNullOrWhiteSpace(newWorkspacePath))
        {
            return (false, newWorkspacePath, modelName, "missing required option --new-workspace <path>.");
        }

        if (string.IsNullOrWhiteSpace(modelName))
        {
            return (false, newWorkspacePath, modelName, "missing required option --model <name>.");
        }

        return (true, newWorkspacePath, modelName, string.Empty);
    }

    private static string EnsureUnsetThenAssignLocal(string existingValue, string newValue, string optionName)
    {
        if (!string.IsNullOrWhiteSpace(existingValue))
        {
            throw new InvalidOperationException($"{optionName} may only be specified once.");
        }

        return newValue;
    }
}
