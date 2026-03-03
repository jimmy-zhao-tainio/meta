using Meta.Core.Domain;

namespace MetaSchema.Core;

public static class MetaSchemaWorkspaces
{
    public static Workspace CreateEmptyMetaDataTypeWorkspace(string workspaceRootPath)
    {
        return MetaSchemaWorkspaceFactory.CreateEmptyWorkspace(
            workspaceRootPath,
            MetaSchemaModels.CreateMetaDataTypeModel());
    }

    public static Workspace CreateEmptyMetaSchemaWorkspace(string workspaceRootPath)
    {
        return MetaSchemaWorkspaceFactory.CreateEmptyWorkspace(
            workspaceRootPath,
            MetaSchemaModels.CreateMetaSchemaModel());
    }

    public static Workspace CreateEmptyMetaDataTypeConversionWorkspace(string workspaceRootPath)
    {
        return MetaSchemaWorkspaceFactory.CreateEmptyWorkspace(
            workspaceRootPath,
            MetaSchemaModels.CreateMetaDataTypeConversionModel());
    }

    public static Workspace CreateSeedMetaDataTypeConversionWorkspace(string workspaceRootPath)
    {
        return MetaDataTypeConversionSeed.CreateWorkspace(workspaceRootPath);
    }
}
