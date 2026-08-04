internal sealed partial class CliRuntime
{
    async Task<int> CreateWorkspaceAsync(
        string[] commandArgs,
        MetaCli.Core.MetaCliWorkspaces workspaces)
    {
        var workspace = new InMemoryWorkspace(
            new GenericModel
            {
                Name = "MetadataModel",
            },
            new GenericInstance
            {
                ModelName = "MetadataModel",
            });

        await workspaces.CreateAsync("output", workspace).ConfigureAwait(false);
        presenter.WriteOk(
            "workspace created",
            ("Path", MetaCli.Core.MetaCliWorkspace.OutputLocation(currentInvocation!)));

        return 0;
    }
}
