using Meta.Operations;
using MetaCli.Core;

internal sealed partial class CliRuntime
{
    public void BindCommandHandlers(
        MetaCliRuntime<MetaCli.MetaCliModel> runtime,
        IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(arguments);

        runtime.Bind(
            "exec-create",
            [
                MetaCliWorkspace.OpenOptional("source-workspace"),
                MetaCliWorkspace.Create("output", "xml", "csharp", "sql"),
            ],
            (invocation, workspaces) => ExecuteBoundAsync(
                invocation,
                arguments,
                commandArgs => CreateWorkspaceAsync(commandArgs, workspaces)));
        RegisterWorkspace("exec-status", StatusWorkspaceAsync);
        RegisterOutput(
            "exec-workspace-merge",
            [MetaCliWorkspace.Open("leftWorkspace"), MetaCliWorkspace.Open("rightWorkspace")],
            WorkspaceMergeAsync);

        RegisterWorkspace("exec-graph-stats", GraphStatsAsync);
        RegisterWorkspace("exec-graph-inbound", GraphInboundAsync);
        RegisterWorkspace("exec-list-entities", ListEntitiesAsync);
        RegisterWorkspace("exec-list-properties", ListPropertiesAsync);
        RegisterWorkspace("exec-list-relationships", ListRelationshipsAsync);
        RegisterWorkspace("exec-view-entity", ViewEntityAsync);
        RegisterWorkspace("exec-view-instance", ViewInstanceAsync);

        RegisterWorkspace("exec-model-add-entity", ModelAddEntityAsync);
        RegisterWorkspace("exec-model-rename-model", ModelRenameModelAsync);
        RegisterWorkspace("exec-model-rename-entity", ModelRenameEntityAsync);
        RegisterWorkspace("exec-model-add-property", ModelAddPropertyAsync);
        RegisterWorkspace("exec-model-rename-property", ModelRenamePropertyAsync);
        RegisterWorkspace("exec-model-set-property-required", ModelSetPropertyRequiredAsync);
        RegisterWorkspace("exec-model-rename-relationship", ModelRenameRelationshipAsync);
        RegisterWorkspace("exec-model-add-relationship", ModelAddRelationshipAsync);
        RegisterWorkspace("exec-model-refactor-property-to-relationship", ModelRefactorPropertyToRelationshipAsync);
        RegisterWorkspace("exec-model-refactor-relationship-to-property", ModelRefactorRelationshipToPropertyAsync);
        RegisterWorkspace("exec-model-drop-property", ModelDropPropertyAsync);
        RegisterWorkspace("exec-model-drop-relationship", ModelDropRelationshipAsync);
        RegisterWorkspace("exec-model-drop-entity", ModelDropEntityAsync);
        RegisterWorkspace("exec-model-suggest", ModelSuggestAsync);

        RegisterWorkspace("exec-insert", InsertAsync);
        RegisterWorkspace("exec-delete", DeleteAsync);
        RegisterWorkspace("exec-query", QueryAsync);
        RegisterWorkspace("exec-bulk-insert", BulkInsertAsync);
        RegisterOutput(
            "exec-instance-diff",
            [MetaCliWorkspace.Open("leftWorkspace"), MetaCliWorkspace.Open("rightWorkspace")],
            InstanceDiffAsync);
        RegisterWorkspaces(
            "exec-instance-merge",
            [MetaCliWorkspace.Open("targetWorkspace"), MetaCliWorkspace.Open("diffWorkspace")],
            InstanceMergeAsync);
        RegisterOutput(
            "exec-instance-diff-aligned",
            [
                MetaCliWorkspace.Open("leftWorkspace"),
                MetaCliWorkspace.Open("rightWorkspace"),
                MetaCliWorkspace.Open("alignmentWorkspace"),
            ],
            InstanceDiffAlignedAsync);
        RegisterWorkspaces(
            "exec-instance-merge-aligned",
            [MetaCliWorkspace.Open("targetWorkspace"), MetaCliWorkspace.Open("diffWorkspace")],
            InstanceMergeAlignedAsync);
        RegisterWorkspace("exec-instance-update", InstanceUpdateAsync);
        RegisterWorkspace("exec-instance-rename-id", InstanceRenameIdAsync);
        RegisterWorkspace("exec-instance-relationship-set", InstanceRelationshipSetAsync);
        RegisterWorkspace("exec-instance-relationship-list", InstanceRelationshipListAsync);

        RegisterOutput("exec-import-sql", [], ImportAsync);
        RegisterOutput("exec-import-csv", [MetaCliWorkspace.Target("workspace")], ImportAsync);
        RegisterWorkspace("exec-export-csv", ExportAsync);
        Register("exec-deploy-sqlserver", DeployAsync);

        void Register(string executableCommandId, Func<string[], Task<int>> handler)
        {
            runtime.Bind(
                executableCommandId,
                invocation => ExecuteBoundAsync(invocation, arguments, handler));
        }

        void RegisterWorkspace(string executableCommandId, Func<string[], Task<int>> handler)
        {
            runtime.Bind(
                executableCommandId,
                (MetaCliInvocation invocation, IMetaWorkspace workspace) => ExecuteBoundAsync(
                    invocation,
                    arguments,
                    workspace,
                    handler));
        }

        void RegisterWorkspaces(
            string executableCommandId,
            IReadOnlyList<MetaCliWorkspaceParameter> workspaces,
            Func<string[], Task<int>> handler)
        {
            runtime.Bind(
                executableCommandId,
                workspaces,
                (invocation, boundWorkspaces) => ExecuteBoundAsync(
                    invocation,
                    arguments,
                    boundWorkspaces,
                    handler));
        }

        void RegisterOutput(
            string executableCommandId,
            IReadOnlyList<MetaCliWorkspaceParameter> inputs,
            Func<string[], Task<int>> handler)
        {
            var workspaces = inputs
                .Append(MetaCliWorkspace.Create(
                    "output",
                    "output-xml",
                    "output-csharp",
                    "output-sql",
                    "output-connection-env"))
                .ToArray();
            runtime.Bind(
                executableCommandId,
                workspaces,
                (invocation, boundWorkspaces) => ExecuteBoundAsync(
                    invocation,
                    arguments,
                    boundWorkspaces,
                    handler));
        }
    }
}
