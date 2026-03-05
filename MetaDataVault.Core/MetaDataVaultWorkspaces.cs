using Meta.Core.Domain;

namespace MetaDataVault.Core;

public static class MetaDataVaultWorkspaces
{
    public static Workspace CreateEmptyMetaRawDataVaultWorkspace(string workspaceRootPath)
    {
        return MetaDataVaultWorkspaceFactory.CreateEmptyWorkspace(
            workspaceRootPath,
            MetaDataVaultModels.CreateMetaRawDataVaultModel());
    }
}
