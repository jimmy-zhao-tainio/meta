using Meta.Core.Operations;
using Meta.Core.Services;

internal sealed partial class CliRuntime
{
    async Task<int> ModelRenameRelationshipAsync(string[] commandArgs)
    {
        var options = ReadModelRenameRelationshipOptions(commandArgs, startIndex: 2);
        if (!options.Ok)
        {
            return PrintArgumentError(options.ErrorMessage);
        }

        var commandOptions = options.Options;

        Workspace? workspace = null;
        WorkspaceSnapshot? before = null;
        try
        {
            workspace = await LoadWorkspaceForCommandAsync(commandOptions.WorkspacePath).ConfigureAwait(false);
            PrintContractCompatibilityWarning(workspace.WorkspaceConfig);

            var fromEntity = RequireEntity(workspace, commandOptions.SourceEntityName);
            var matchingRelationships = fromEntity.Relationships
                .Where(item => string.Equals(item.Entity, commandOptions.TargetEntityName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (matchingRelationships.Count > 1)
            {
                return PrintDataError(
                    "E_RELATIONSHIP_AMBIGUOUS",
                    $"Relationship '{commandOptions.SourceEntityName}->{commandOptions.TargetEntityName}' is ambiguous because multiple relationships target '{commandOptions.TargetEntityName}'.");
            }

            var relationship = matchingRelationships.SingleOrDefault();
            if (relationship == null)
            {
                return PrintDataError(
                    "E_RELATIONSHIP_NOT_FOUND",
                    $"Relationship '{commandOptions.SourceEntityName}->{commandOptions.TargetEntityName}' does not exist.");
            }

            var currentRole = relationship.Role ?? string.Empty;
            before = WorkspaceSnapshotCloner.Capture(workspace);

            var result = services.ModelRefactorService.RenameRelationship(
                workspace,
                new RenameRelationshipRefactorOptions(
                    commandOptions.SourceEntityName,
                    commandOptions.TargetEntityName,
                    currentRole,
                    commandOptions.NewRole));
            ApplyImplicitNormalization(workspace);

            var diagnostics = services.ValidationService.Validate(workspace);
            workspace.Diagnostics = diagnostics;
            if (diagnostics.HasErrors || (globalStrict && diagnostics.WarningCount > 0))
            {
                WorkspaceSnapshotCloner.Restore(workspace, before);
                return PrintOperationValidationFailure(
                    "model rename-relationship",
                    Array.Empty<WorkspaceOp>(),
                    diagnostics);
            }

            await services.WorkspaceService.SaveAsync(workspace).ConfigureAwait(false);

            presenter.WriteOk(
                "relationship renamed",
                ("Workspace", Path.GetFullPath(workspace.WorkspaceRootPath)),
                ("Model", workspace.Model.Name),
                ("From", commandOptions.SourceEntityName + "." + result.OldUsageName),
                ("To", commandOptions.SourceEntityName + "." + result.NewUsageName),
                ("Target", result.TargetEntityName),
                ("OldRole", string.IsNullOrWhiteSpace(currentRole) ? "(none)" : result.OldRole),
                ("NewRole", string.Equals(result.NewUsageName, result.TargetEntityName + "Id", StringComparison.OrdinalIgnoreCase) ? "(none)" : result.NewRole),
                ("Rows touched", result.RowsTouched.ToString()));
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

    (bool Ok, RenameRelationshipCommandOptions Options, string ErrorMessage)
        ReadModelRenameRelationshipOptions(string[] commandArgs, int startIndex)
    {
        var sourceEntityName = RequiredValue("FromEntity").Trim();
        var targetEntityName = RequiredValue("ToEntity").Trim();
        var workspacePath = WorkspacePath();
        var newRole = OptionalValue("role").Trim();
        if (string.IsNullOrWhiteSpace(sourceEntityName) || string.IsNullOrWhiteSpace(targetEntityName))
        {
            return (false, default, "Error: missing required arguments <FromEntity> <ToEntity>.");
        }

        if (!string.IsNullOrWhiteSpace(newRole) && !ModelNamePattern.IsMatch(newRole))
        {
            return (false, default, "Error: --role must use identifier pattern [A-Za-z_][A-Za-z0-9_]*.");
        }

        return (true, new RenameRelationshipCommandOptions(
            WorkspacePath: workspacePath,
            SourceEntityName: sourceEntityName,
            TargetEntityName: targetEntityName,
            NewRole: newRole), string.Empty);
    }

    readonly record struct RenameRelationshipCommandOptions(
        string WorkspacePath,
        string SourceEntityName,
        string TargetEntityName,
        string NewRole);
}
