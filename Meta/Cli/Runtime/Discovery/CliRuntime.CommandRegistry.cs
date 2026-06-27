internal sealed partial class CliRuntime
{
    Dictionary<string, CliCommandRegistration> BuildCommandRegistry()
    {
        var registry = new Dictionary<string, CliCommandRegistration>(StringComparer.OrdinalIgnoreCase);

        Register("exec-init", InitWorkspaceAsync);
        Register("exec-status", StatusWorkspaceAsync);
        Register("exec-workspace-merge", WorkspaceMergeAsync);

        Register("exec-check", CheckWorkspaceAsync);
        Register("exec-graph-stats", GraphStatsAsync);
        Register("exec-graph-inbound", GraphInboundAsync);
        Register("exec-list-entities", ListEntitiesAsync);
        Register("exec-list-properties", ListPropertiesAsync);
        Register("exec-list-relationships", ListRelationshipsAsync);
        Register("exec-view-entity", ViewEntityAsync);
        Register("exec-view-instance", ViewInstanceAsync);

        Register("exec-model-add-entity", ModelAddEntityAsync);
        Register("exec-model-rename-model", ModelRenameModelAsync);
        Register("exec-model-rename-entity", ModelRenameEntityAsync);
        Register("exec-model-add-property", ModelAddPropertyAsync);
        Register("exec-model-rename-property", ModelRenamePropertyAsync);
        Register("exec-model-set-property-required", ModelSetPropertyRequiredAsync);
        Register("exec-model-rename-relationship", ModelRenameRelationshipAsync);
        Register("exec-model-add-relationship", ModelAddRelationshipAsync);
        Register("exec-model-refactor-property-to-relationship", ModelRefactorPropertyToRelationshipAsync);
        Register("exec-model-refactor-relationship-to-property", ModelRefactorRelationshipToPropertyAsync);
        Register("exec-model-drop-property", ModelDropPropertyAsync);
        Register("exec-model-drop-relationship", ModelDropRelationshipAsync);
        Register("exec-model-drop-entity", ModelDropEntityAsync);
        Register("exec-model-suggest", ModelSuggestAsync);

        Register("exec-insert", InsertAsync);
        Register("exec-delete", DeleteAsync);
        Register("exec-query", QueryAsync);
        Register("exec-bulk-insert", BulkInsertAsync);
        Register("exec-instance-diff", InstanceDiffAsync);
        Register("exec-instance-merge", InstanceMergeAsync);
        Register("exec-instance-diff-aligned", InstanceDiffAlignedAsync);
        Register("exec-instance-merge-aligned", InstanceMergeAlignedAsync);
        Register("exec-instance-update", InstanceUpdateAsync);
        Register("exec-instance-rename-id", InstanceRenameIdAsync);
        Register("exec-instance-relationship-set", InstanceRelationshipSetAsync);
        Register("exec-instance-relationship-list", InstanceRelationshipListAsync);

        Register("exec-import-sql", ImportAsync);
        Register("exec-import-csv", ImportAsync);
        Register("exec-export-csv", ExportAsync);
        Register("exec-generate-sql", GenerateAsync);
        Register("exec-generate-csharp", GenerateAsync);
        Register("exec-generate-ssdt", GenerateAsync);
        Register("exec-deploy-sqlserver", DeployAsync);

        return registry;

        void Register(string executableCommandId, Func<string[], Task<int>> handler)
        {
            registry[executableCommandId] = new CliCommandRegistration(handler);
        }
    }

    readonly record struct CliCommandRegistration(Func<string[], Task<int>> Handler);
}
