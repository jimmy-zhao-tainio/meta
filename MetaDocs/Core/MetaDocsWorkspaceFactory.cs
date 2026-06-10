using Meta.Core.Domain;
using MetaWorkspaceConfig = Meta.Core.WorkspaceConfig.Generated.MetaWorkspace;

namespace MetaDocs.Core;

public static class MetaDocsWorkspaceFactory
{
    public static Workspace CreateEmptyWorkspace(string workspaceRootPath, GenericModel model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRootPath);
        ArgumentNullException.ThrowIfNull(model);

        var rootPath = Path.GetFullPath(workspaceRootPath);

        return new Workspace
        {
            WorkspaceRootPath = rootPath,
            MetadataRootPath = rootPath,
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
