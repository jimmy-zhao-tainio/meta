using Meta.Core.Domain;

namespace MetaSchema.Core;

public static class MetaSchemaCatalogWorkspaces
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

    public static Workspace CreateEmptyTypeConversionCatalogWorkspace(string workspaceRootPath)
    {
        return MetaSchemaWorkspaceFactory.CreateEmptyWorkspace(
            workspaceRootPath,
            MetaSchemaModels.CreateTypeConversionCatalogModel());
    }

    public static Workspace CreateSeedTypeConversionCatalogWorkspace(string workspaceRootPath)
    {
        return TypeConversionCatalogSeed.CreateWorkspace(workspaceRootPath);
    }
}
