internal sealed partial class CliRuntime
{
    Dictionary<string, CliCommandRegistration> BuildCommandRegistry()
    {
        var registry = new Dictionary<string, CliCommandRegistration>(StringComparer.OrdinalIgnoreCase);

        Register("init", "Workspace", "Initialize workspace.", InitWorkspaceAsync);
        Register("status", "Workspace", "Show workspace summary.", StatusWorkspaceAsync);
        Register("workspace", "Workspace", "Merge workspaces and inspect workspace-level operations.", WorkspaceAsync);

        Register("check", "Model", "Check model and instance integrity.", CheckWorkspaceAsync);
        Register("graph", "Model", "Graph stats and inbound relationships.", GraphAsync);
        Register("list", "Model", "List entities, properties, and relationships.", ListAsync);
        Register("model", "Model", "Inspect and mutate model entities, properties, and relationships.", ModelAsync);
        Register("view", "Model", "View entity or instance details.", ViewAsync);

        Register("instance", "Instance", "Diff and merge instance artifacts.", InstanceAsync);
        Register("insert", "Instance", "Insert one instance: <Entity> <Id> or --auto-id for brand-new rows.", InsertAsync);
        Register("delete", "Instance", "Delete one instance: <Entity> <Id>.", DeleteAsync);
        Register("query", "Instance", "Search instances with equals/contains filters.", QueryAsync);
        Register("bulk-insert", "Instance", "Insert many instances from tsv/csv input (supports --auto-id for new rows only).", BulkInsertAsync);

        Register("import", "Pipeline", "Import xml/sql into NEW workspace or csv into NEW/existing workspace.", ImportAsync);
        Register("export", "Pipeline", "Export workspace data to external formats.", ExportAsync);
        Register("generate", "Pipeline", "Generate artifacts from the workspace.", GenerateAsync);
        Register("deploy", "Pipeline", "Deploy generated artifacts to external targets.", DeployAsync);

        return registry;

        void Register(string commandName, string domain, string description, Func<string[], Task<int>> handler)
        {
            registry[commandName] = new CliCommandRegistration(domain, description, handler);
            HelpTopics.RegisterCommand(commandName, domain, description);
        }
    }

    readonly record struct CliCommandRegistration(
        string Domain,
        string Description,
        Func<string[], Task<int>> Handler);
}
