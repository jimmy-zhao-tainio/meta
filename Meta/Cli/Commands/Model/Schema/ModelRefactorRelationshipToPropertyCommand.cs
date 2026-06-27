using Meta.Core.Domain;
using Meta.Core.Operations;
using Meta.Core.Services;

internal sealed partial class CliRuntime
{
    async Task<int> ModelRefactorRelationshipToPropertyAsync(string[] commandArgs)
    {
        var options = ReadModelRefactorRelationshipToPropertyOptions(commandArgs, startIndex: 3);
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
        ReadModelRefactorRelationshipToPropertyOptions(string[] commandArgs, int startIndex)
    {
        var workspacePath = WorkspacePath();
        var source = RequiredValue("source").Trim();
        var target = RequiredValue("target").Trim();
        var role = OptionalValue("role").Trim();
        var propertyName = OptionalValue("property").Trim();

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
