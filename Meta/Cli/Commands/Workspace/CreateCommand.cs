internal sealed partial class CliRuntime
{
    async Task<int> CreateWorkspaceAsync(
        string[] commandArgs,
        MetaCli.Core.MetaCliWorkspaces workspaces)
    {
        var source = workspaces.Optional("source-workspace");
        if (source is null)
        {
            if (Flag("with-instances"))
            {
                return PrintArgumentError(
                    "Error: --with-instances requires --source-workspace.");
            }

            var model = new GenericModel
            {
                Name = "MetadataModel",
            };
            var emptyInstance = new GenericInstance
            {
                ModelName = model.Name,
            };
            var emptyWorkspace = new InMemoryWorkspace(model, emptyInstance);
            await workspaces.CreateAsync("output", emptyWorkspace).ConfigureAwait(false);
        }
        else if (Flag("with-instances"))
        {
            var workspace = await WorkspaceComposition.MaterializeAsync(source)
                .ConfigureAwait(false);
            await workspaces.CreateAsync("output", workspace).ConfigureAwait(false);
        }
        else
        {
            var model = await WorkspaceComposition.MaterializeModelAsync(source)
                .ConfigureAwait(false);
            var workspace = new InMemoryWorkspace(
                model,
                new GenericInstance
                {
                    ModelName = model.Name,
                });
            await workspaces.CreateAsync("output", workspace).ConfigureAwait(false);
        }

        presenter.WriteOk(
            "workspace created",
            ("Path", MetaCli.Core.MetaCliWorkspace.OutputLocation(Invocation)));

        return 0;
    }
}
