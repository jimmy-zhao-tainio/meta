using Meta.Operations;
using MetaWorkspaceConfig = Meta.Surfaces.Configuration.MetaWorkspace;

internal sealed partial class CliRuntime
{
    async Task PrintWorkspaceSummaryAsync(IMetaWorkspaceSource workspace)
    {
        var modelName = await workspace.ReadModelNameAsync().ConfigureAwait(false);
        var entityCount = 0;
        var rowCount = 0L;
        await foreach (var entityName in workspace.ReadEntityNamesAsync())
        {
            entityCount++;
            rowCount += await workspace.CountRecordsAsync(entityName).ConfigureAwait(false);
        }

        presenter.WriteInfo("Status: ok");
        presenter.WriteKeyValueBlock(
            "Model",
            new[]
            {
                ("Name", modelName),
                ("Entities", entityCount.ToString(CultureInfo.InvariantCulture)),
                ("Rows", rowCount.ToString(CultureInfo.InvariantCulture)),
            });
    }

    void PrintContractCompatibilityWarning(Meta.Surfaces.Configuration.MetaWorkspace workspaceConfig)
    {
        PrintContractCompatibilityWarning(
            MetaWorkspaceConfig.GetContractVersion(workspaceConfig));
    }

    void PrintContractCompatibilityWarning(string contractVersion)
    {
        if (!MetaWorkspaceConfig.TryParseContractVersion(contractVersion, out var major, out var minor))
        {
            return;
        }

        if (major == SupportedContractMajorVersion && minor > SupportedContractMinorVersion)
        {
            presenter.WriteWarning(
                $"workspace contractVersion '{contractVersion}' is newer than tool baseline '{SupportedContractMajorVersion}.{SupportedContractMinorVersion}'.");
        }
    }

    (string WorkspaceRootPath, string MetadataRootPath) ResolveWorkspaceFilesystemContext(string workspacePath)
    {
        var absolutePath = Path.GetFullPath(workspacePath);
        if (string.Equals(Path.GetFileName(absolutePath), "instances", StringComparison.OrdinalIgnoreCase))
        {
            var workspaceRoot = Directory.GetParent(absolutePath)?.FullName ?? absolutePath;
            return (workspaceRoot, workspaceRoot);
        }

        return (absolutePath, absolutePath);
    }
}
