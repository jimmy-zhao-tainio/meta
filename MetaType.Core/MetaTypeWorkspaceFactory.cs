using Meta.Core.Domain;
using MetaWorkspaceConfig = Meta.Core.WorkspaceConfig.Generated.MetaWorkspace;

namespace MetaType.Core;

public static class MetaTypeWorkspaceFactory
{
    public static Workspace CreateEmptyWorkspace(string workspaceRootPath, GenericModel model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRootPath);
        ArgumentNullException.ThrowIfNull(model);

        var rootPath = Path.GetFullPath(workspaceRootPath);
        var metadataRootPath = Path.Combine(rootPath, "metadata");

        return new Workspace
        {
            WorkspaceRootPath = rootPath,
            MetadataRootPath = metadataRootPath,
            WorkspaceConfig = MetaWorkspaceConfig.CreateDefault(),
            Model = model,
            Instance = new GenericInstance
            {
                ModelName = model.Name,
            },
            IsDirty = true,
        };
    }
}
