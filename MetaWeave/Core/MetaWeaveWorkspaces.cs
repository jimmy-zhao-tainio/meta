using Meta.Core.Domain;

namespace MetaWeave.Core;

public static class MetaWeaveWorkspaces
{
    public static Workspace CreateEmptyMetaWeaveWorkspace(string workspaceRootPath)
    {
        return MetaWeaveWorkspaceFactory.CreateEmptyWorkspace(
            workspaceRootPath,
            MetaWeaveModels.CreateMetaWeaveModel());
    }
}
