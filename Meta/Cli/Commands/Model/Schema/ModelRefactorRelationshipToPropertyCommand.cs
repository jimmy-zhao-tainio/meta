using Meta.Core.Domain;
using Meta.Core.Operations;
using Meta.Core.Services;

internal sealed partial class CliRuntime
{
    async Task<int> ModelRefactorRelationshipToPropertyAsync(string[] commandArgs)
    {
        var options = ParseModelRefactorRelationshipToPropertyOptions(commandArgs, startIndex: 3);
        if (!options.Ok)
        {
            return PrintArgumentError(options.ErrorMessage);
        }

        var commandOptions = options.Options;
        var refactorOptions = commandOptions.Refactor;

        Workspace? workspace = null;
        WorkspaceSnapshot? before = null;
        try
        {
            workspace = await LoadWorkspaceForCommandAsync(commandOptions.WorkspacePath).ConfigureAwait(false);
            PrintContractCompatibilityWarning(workspace.WorkspaceConfig);
            before = WorkspaceSnapshotCloner.Capture(workspace);

            var result = services.ModelRefactorService.RefactorRelationshipToProperty(workspace, refactorOptions);
            ApplyImplicitNormalization(workspace);

            var diagnostics = services.ValidationService.Validate(workspace);
            workspace.Diagnostics = diagnostics;
            if (diagnostics.HasErrors || (globalStrict && diagnostics.WarningCount > 0))
            {
                WorkspaceSnapshotCloner.Restore(workspace, before);
                return PrintOperationValidationFailure(
                    "model refactor relationship-to-property",
                    Array.Empty<WorkspaceOp>(),
                    diagnostics);
            }

            await services.WorkspaceService.SaveAsync(workspace).ConfigureAwait(false);

            presenter.WriteOk(
                "refactor relationship-to-property",
                ("Workspace", Path.GetFullPath(workspace.WorkspaceRootPath)),
                ("Model", workspace.Model.Name),
                ("Source", result.SourceEntityName),
                ("Target", result.TargetEntityName),
                ("Role", string.IsNullOrWhiteSpace(result.Role) ? "(none)" : result.Role),
                ("Property", result.PropertyName));
            presenter.WriteInfo($"Rows rewritten: {result.RowsRewritten}");
            presenter.WriteInfo("Relationship removed: yes");
            return 0;
        }
        catch (InvalidOperationException exception)
        {
            if (workspace != null && before != null)
            {
                WorkspaceSnapshotCloner.Restore(workspace, before);
            }

            return PrintDataError("E_OPERATION", exception.Message);
        }
        catch
        {
            if (workspace != null && before != null)
            {
                WorkspaceSnapshotCloner.Restore(workspace, before);
            }

            throw;
        }
    }

    (bool Ok, RelationshipToPropertyCommandOptions Options, string ErrorMessage)
        ParseModelRefactorRelationshipToPropertyOptions(string[] commandArgs, int startIndex)
    {
        var workspacePath = DefaultWorkspacePath();
        var source = string.Empty;
        var target = string.Empty;
        var role = string.Empty;
        var propertyName = string.Empty;

        for (var i = startIndex; i < commandArgs.Length; i++)
        {
            var arg = commandArgs[i];
            if (string.Equals(arg, "--workspace", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= commandArgs.Length)
                {
                    return (false, default, "Error: --workspace requires a path.");
                }

                workspacePath = commandArgs[++i];
                continue;
            }

            if (string.Equals(arg, "--source", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= commandArgs.Length)
                {
                    return (false, default, "Error: --source requires <Entity>.");
                }

                source = commandArgs[++i].Trim();
                continue;
            }

            if (string.Equals(arg, "--target", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= commandArgs.Length)
                {
                    return (false, default, "Error: --target requires <Entity>.");
                }

                target = commandArgs[++i].Trim();
                continue;
            }

            if (string.Equals(arg, "--role", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= commandArgs.Length)
                {
                    return (false, default, "Error: --role requires <Role>.");
                }

                role = commandArgs[++i].Trim();
                continue;
            }

            if (string.Equals(arg, "--property", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= commandArgs.Length)
                {
                    return (false, default, "Error: --property requires <PropertyName>.");
                }

                propertyName = commandArgs[++i].Trim();
                continue;
            }

            return (false, default, $"Error: unknown option '{arg}'.");
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            return (false, default, "Error: --source <Entity> is required.");
        }

        if (string.IsNullOrWhiteSpace(target))
        {
            return (false, default, "Error: --target <Entity> is required.");
        }

        if (!string.IsNullOrWhiteSpace(role) && !ModelNamePattern.IsMatch(role))
        {
            return (false, default, "Error: --role must use identifier pattern [A-Za-z_][A-Za-z0-9_]*.");
        }

        if (!string.IsNullOrWhiteSpace(propertyName) && !ModelNamePattern.IsMatch(propertyName))
        {
            return (false, default, "Error: --property must use identifier pattern [A-Za-z_][A-Za-z0-9_]*.");
        }

        var options = new RelationshipToPropertyCommandOptions(
            WorkspacePath: workspacePath,
            Refactor: new RelationshipToPropertyRefactorOptions(
                SourceEntityName: source,
                TargetEntityName: target,
                Role: role,
                PropertyName: propertyName));

        return (true, options, string.Empty);
    }

    readonly record struct RelationshipToPropertyCommandOptions(
        string WorkspacePath,
        RelationshipToPropertyRefactorOptions Refactor);
}
