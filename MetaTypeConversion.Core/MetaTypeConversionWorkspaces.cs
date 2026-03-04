using Meta.Core.Domain;

namespace MetaTypeConversion.Core;

public static class MetaTypeConversionWorkspaces
{
    public static Workspace CreateMetaTypeConversionWorkspace(string workspaceRootPath)
    {
        var workspace = MetaTypeConversionWorkspaceFactory.CreateEmptyWorkspace(
            workspaceRootPath,
            MetaTypeConversionModels.CreateMetaTypeConversionModel());

        MetaTypeConversionSeed.Populate(workspace.Instance);
        return workspace;
    }

    public static Workspace CreateEmptyMetaTypeConversionWorkspace(string workspaceRootPath)
    {
        return MetaTypeConversionWorkspaceFactory.CreateEmptyWorkspace(
            workspaceRootPath,
            MetaTypeConversionModels.CreateMetaTypeConversionModel());
    }
}
