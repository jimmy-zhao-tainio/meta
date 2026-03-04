using Meta.Core.Domain;

namespace MetaTypeConversion.Core;

public static class MetaTypeConversionWorkspaces
{
    public static Workspace CreateEmptyMetaTypeConversionWorkspace(string workspaceRootPath)
    {
        return MetaTypeConversionWorkspaceFactory.CreateEmptyWorkspace(
            workspaceRootPath,
            MetaTypeConversionModels.CreateMetaTypeConversionModel());
    }
}
