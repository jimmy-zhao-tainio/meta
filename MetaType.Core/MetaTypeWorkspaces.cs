using Meta.Core.Domain;

namespace MetaType.Core;

public static class MetaTypeWorkspaces
{
    public static Workspace CreateEmptyMetaTypeWorkspace(string workspaceRootPath)
    {
        return MetaTypeWorkspaceFactory.CreateEmptyWorkspace(
            workspaceRootPath,
            MetaTypeModels.CreateMetaTypeModel());
    }
}
