using System.Text.RegularExpressions;
using Meta.Core.Operations;
using Meta.Core.Services;

internal sealed partial class CliRuntime
{
    private static readonly Regex ModelNamePattern = new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    async Task<int> ModelRefactorPropertyToRelationshipAsync(string[] commandArgs)
    {
        var options = ReadModelRefactorPropertyToRelationshipOptions(commandArgs, startIndex: 3);
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

            var result = services.ModelRefactorService.RefactorPropertyToRelationship(workspace, refactorOptions);
            ApplyImplicitNormalization(workspace);

            var diagnostics = services.ValidationService.Validate(workspace);
            workspace.Diagnostics = diagnostics;
            if (diagnostics.HasErrors || (globalStrict && diagnostics.WarningCount > 0))
            {
                WorkspaceSnapshotCloner.Restore(workspace, before);
                return PrintOperationValidationFailure(
                    "model refactor property-to-relationship",
                    Array.Empty<WorkspaceOp>(),
                    diagnostics);
            }

            await services.WorkspaceService.SaveAsync(workspace).ConfigureAwait(false);

            presenter.WriteOk(
                "refactor property-to-relationship",
                ("Workspace", Path.GetFullPath(workspace.WorkspaceRootPath)),
                ("Model", workspace.Model.Name),
                ("Source", result.SourceAddress),
                ("Target", result.TargetEntityName),
                ("Lookup", result.LookupAddress),
                ("Role", string.IsNullOrWhiteSpace(result.Role) ? "(none)" : result.Role),
                ("Preserve property", refactorOptions.DropSourceProperty ? "no" : "yes"));
            presenter.WriteInfo($"Rows rewritten: {result.RowsRewritten}");
            presenter.WriteInfo($"Property dropped: {(result.PropertyDropped ? "yes" : "no")}");
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

    (bool Ok, PropertyToRelationshipCommandOptions Options, string ErrorMessage)
        ReadModelRefactorPropertyToRelationshipOptions(string[] commandArgs, int startIndex)
    {
        var workspacePath = WorkspacePath();
        var source = RequiredValue("source").Trim();
        var target = RequiredValue("target").Trim();
        var lookup = RequiredValue("lookup").Trim();
        var role = OptionalValue("role").Trim();
        var preserveProperty = Flag("preserve-property");

        var separatorIndex = source.IndexOf('.', StringComparison.Ordinal);
        if (separatorIndex <= 0 || separatorIndex == source.Length - 1 || source.IndexOf('.', separatorIndex + 1) >= 0)
        {
            return (false, default, "Error: --source must be in format <Entity.Property>.");
        }

        var sourceEntityName = source[..separatorIndex].Trim();
        var sourcePropertyName = source[(separatorIndex + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(sourceEntityName) || string.IsNullOrWhiteSpace(sourcePropertyName))
        {
            return (false, default, "Error: --source must be in format <Entity.Property>.");
        }

        var options = new PropertyToRelationshipCommandOptions(
            WorkspacePath: workspacePath,
            Refactor: new PropertyToRelationshipRefactorOptions(
                SourceEntityName: sourceEntityName,
                SourcePropertyName: sourcePropertyName,
                TargetEntityName: target,
                LookupPropertyName: lookup,
                Role: role,
                DropSourceProperty: !preserveProperty));

        return (true, options, string.Empty);
    }

    readonly record struct PropertyToRelationshipCommandOptions(
        string WorkspacePath,
        PropertyToRelationshipRefactorOptions Refactor);
}
