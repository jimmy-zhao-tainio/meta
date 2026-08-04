using Meta.Core.Operations;
using MetaWorkspaceConfig = Meta.Core.WorkspaceConfig.Generated.MetaWorkspace;

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

    void PrintContractCompatibilityWarning(Meta.Core.WorkspaceConfig.Generated.MetaWorkspace workspaceConfig)
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

    bool WorkspaceLooksInitialized(string workspaceRoot, string metadataRoot)
    {
        return File.Exists(Path.Combine(workspaceRoot, "workspace.xml")) ||
               File.Exists(Path.Combine(metadataRoot, "model.xml")) ||
               Directory.Exists(Path.Combine(metadataRoot, "instances"));
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
