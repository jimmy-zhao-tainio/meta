using System.Globalization;
using Meta.Operations.Domain;
using MetaWorkspaceCanonicalOrder = Meta.Surfaces.Configuration.CanonicalOrder;
using MetaWorkspaceEntityStorage = Meta.Surfaces.Configuration.EntityStorage;
using MetaWorkspaceGenerated = Meta.Surfaces.Configuration.MetaWorkspace;

namespace Meta.Surfaces.Xml;

internal readonly record struct XmlWorkspaceMergeSource(
    string RootPath,
    InMemoryWorkspace State,
    MetaWorkspaceGenerated Configuration,
    XmlWorkspaceLayout Layout);

internal readonly record struct XmlWorkspaceMergeLayoutResult(
    MetaWorkspaceGenerated Configuration,
    XmlWorkspaceLayout Layout);

internal static class XmlWorkspaceMergeLayout
{
    public static XmlWorkspaceMergeLayoutResult Build(
        IReadOnlyList<XmlWorkspaceMergeSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Count < 2)
        {
            throw new InvalidOperationException(
                "Workspace merge requires at least two source workspaces.");
        }

        ValidateConfigurationCompatibility(sources);

        var baseline = MetaWorkspaceGenerated.Normalize(
            sources[0].Configuration,
            "workspace-config");
        var configuration = MetaWorkspaceGenerated.CreateDefault();
        ApplyConfigurationSettings(configuration, baseline);
        foreach (var source in sources)
        {
            MergeEntityStorage(configuration, source.Configuration);
        }

        return new XmlWorkspaceMergeLayoutResult(
            configuration,
            MergeShardAssignments(sources));
    }

    private static void ValidateConfigurationCompatibility(
        IReadOnlyList<XmlWorkspaceMergeSource> sources)
    {
        var baseline = BuildConfigurationSignature(
            sources[0].Configuration);
        foreach (var source in sources.Skip(1))
        {
            var candidate = BuildConfigurationSignature(
                source.Configuration);
            if (!string.Equals(candidate, baseline, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Cannot merge workspace '{source.RootPath}' because workspace config settings differ.");
            }
        }
    }

    private static string BuildConfigurationSignature(
        MetaWorkspaceGenerated configuration)
    {
        var normalized = MetaWorkspaceGenerated.Normalize(
            configuration,
            "workspace-config");
        var workspace = normalized.Workspace.Single();
        var canonicalOrderById = normalized.CanonicalOrder.ToDictionary(
            item => item.Id,
            item => item.Name,
            StringComparer.Ordinal);

        return string.Join(
            "\n",
            normalized.Encoding.Single().Name,
            normalized.Newlines.Single().Name,
            canonicalOrderById[workspace.EntitiesOrderId],
            canonicalOrderById[workspace.PropertiesOrderId],
            canonicalOrderById[workspace.RelationshipsOrderId],
            canonicalOrderById[workspace.RowsOrderId],
            canonicalOrderById[workspace.AttributesOrderId]);
    }

    private static void ApplyConfigurationSettings(
        MetaWorkspaceGenerated target,
        MetaWorkspaceGenerated source)
    {
        var normalizedSource = MetaWorkspaceGenerated.Normalize(
            source,
            "workspace-config");
        var sourceWorkspace = normalizedSource.Workspace.Single();
        var sourceCanonicalOrderById = normalizedSource.CanonicalOrder.ToDictionary(
            item => item.Id,
            item => item.Name,
            StringComparer.Ordinal);

        var normalizedTarget = MetaWorkspaceGenerated.Normalize(
            target,
            "workspace-config");
        normalizedTarget.Encoding.Single().Name =
            normalizedSource.Encoding.Single().Name;
        normalizedTarget.Newlines.Single().Name =
            normalizedSource.Newlines.Single().Name;

        var targetWorkspace = normalizedTarget.Workspace.Single();
        targetWorkspace.EntitiesOrderId = ResolveCanonicalOrderId(
            normalizedTarget.CanonicalOrder,
            sourceCanonicalOrderById[sourceWorkspace.EntitiesOrderId]);
        targetWorkspace.PropertiesOrderId = ResolveCanonicalOrderId(
            normalizedTarget.CanonicalOrder,
            sourceCanonicalOrderById[sourceWorkspace.PropertiesOrderId]);
        targetWorkspace.RelationshipsOrderId = ResolveCanonicalOrderId(
            normalizedTarget.CanonicalOrder,
            sourceCanonicalOrderById[sourceWorkspace.RelationshipsOrderId]);
        targetWorkspace.RowsOrderId = ResolveCanonicalOrderId(
            normalizedTarget.CanonicalOrder,
            sourceCanonicalOrderById[sourceWorkspace.RowsOrderId]);
        targetWorkspace.AttributesOrderId = ResolveCanonicalOrderId(
            normalizedTarget.CanonicalOrder,
            sourceCanonicalOrderById[sourceWorkspace.AttributesOrderId]);

        target.Workspace = normalizedTarget.Workspace;
        target.WorkspaceLayout = normalizedTarget.WorkspaceLayout;
        target.Encoding = normalizedTarget.Encoding;
        target.Newlines = normalizedTarget.Newlines;
        target.CanonicalOrder = normalizedTarget.CanonicalOrder;
        target.EntityStorage = normalizedTarget.EntityStorage;
    }

    private static string ResolveCanonicalOrderId(
        IReadOnlyCollection<MetaWorkspaceCanonicalOrder> orders,
        string name)
    {
        var match = orders.SingleOrDefault(item =>
                string.Equals(item.Name, name, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"Workspace config is missing canonical order '{name}'.");
        return match.Id;
    }

    private static void MergeEntityStorage(
        MetaWorkspaceGenerated targetConfiguration,
        MetaWorkspaceGenerated sourceConfiguration)
    {
        var normalizedSource = MetaWorkspaceGenerated.Normalize(
            sourceConfiguration,
            "workspace-config");
        var targetWorkspace = targetConfiguration.Workspace.Single();
        var nextId = targetConfiguration.EntityStorage.Count == 0
            ? 1
            : targetConfiguration.EntityStorage
                .Select(item => int.TryParse(item.Id, out var value)
                    ? value
                    : 0)
                .Max() + 1;

        foreach (var item in normalizedSource.EntityStorage
                     .OrderBy(storage => storage.EntityName, StringComparer.Ordinal)
                     .ThenBy(storage => storage.Id, StringComparer.Ordinal))
        {
            targetConfiguration.EntityStorage.Add(
                new MetaWorkspaceEntityStorage
                {
                    Id = nextId.ToString(CultureInfo.InvariantCulture),
                    WorkspaceId = targetWorkspace.Id,
                    Workspace = targetWorkspace,
                    EntityName = item.EntityName,
                    StorageKind = item.StorageKind,
                    DirectoryPath = item.DirectoryPath,
                    FilePath = item.FilePath,
                    Pattern = item.Pattern,
                });
            nextId++;
        }
    }

    private static XmlWorkspaceLayout MergeShardAssignments(
        IReadOnlyList<XmlWorkspaceMergeSource> sources)
    {
        var result = new XmlWorkspaceLayout();
        foreach (var source in sources)
        {
            foreach (var entityRecords in source.State.Instance.RecordsByEntity)
            {
                foreach (var record in entityRecords.Value)
                {
                    var shardFileName = source.Layout.FindShard(
                        entityRecords.Key,
                        record.Id);
                    if (!string.IsNullOrWhiteSpace(shardFileName))
                    {
                        result.AssignShard(
                            entityRecords.Key,
                            record.Id,
                            shardFileName);
                    }
                }
            }
        }

        return result;
    }
}
