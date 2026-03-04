using Meta.Core.Domain;

namespace MetaType.Core;

public static class MetaTypeWorkspaces
{
    public static Workspace CreateMetaTypeWorkspace(string workspaceRootPath)
    {
        var workspace = MetaTypeWorkspaceFactory.CreateEmptyWorkspace(
            workspaceRootPath,
            MetaTypeModels.CreateMetaTypeModel());

        MetaTypeSeed.Populate(workspace.Instance);
        return workspace;
    }
}
