using System.Text;
using System.Security.Cryptography;
using Meta.Core.Domain;
using Meta.Core.Services;
using MetaDocs;

namespace MetaDocs.Core;

public sealed class MetaDocsWorkspaceInstanceImporter
{
    private readonly WorkspaceService workspaceService = new();

    public async Task<MetaDocsWorkspaceInstanceImportResult> ImportWorkspaceInstancesAsync(
        MetaDocsModel model,
        string sourceWorkspacePath,
        string sourceId = "",
        string modelSourceId = "",
        string displayName = "",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceWorkspacePath);

        var workspace = await workspaceService.LoadAsync(
            sourceWorkspacePath,
            searchUpward: false,
            cancellationToken).ConfigureAwait(false);
        var modelName = string.IsNullOrWhiteSpace(workspace.Model.Name) ? "Model" : workspace.Model.Name;
        var normalizedDisplayName = string.IsNullOrWhiteSpace(displayName)
            ? modelName
            : displayName;
        var normalizedSourceId = string.IsNullOrWhiteSpace(sourceId)
            ? $"source:workspace-instances:{MetaDocsImportSession.NormalizeKey(normalizedDisplayName)}"
            : sourceId;
        var normalizedModelSourceId = string.IsNullOrWhiteSpace(modelSourceId)
            ? $"source:workspace-model:{MetaDocsImportSession.NormalizeKey(normalizedDisplayName)}"
            : modelSourceId;

        var policy = new MetaDocsInstanceImportPolicy(model);
        var includedEntities = workspace.Model.Entities
            .Where(entity => policy.IncludesEntity(entity.Name, normalizedModelSourceId))
            .OrderBy(entity => entity.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var session = new MetaDocsImportSession(
            model,
            normalizedSourceId,
            "WorkspaceInstances",
            normalizedDisplayName + " instances",
            Path.GetFullPath(sourceWorkspacePath),
            ComputeSourceFingerprint(workspace),
            "MetaDocs.WorkspaceInstances",
            "1");

        var root = session.UpsertSubject(
            $"{normalizedSourceId}:instances",
            "WorkspaceInstances",
            "Workspace",
            workspace.WorkspaceRootPath,
            normalizedDisplayName + " instances",
            $"{normalizedDisplayName}.Instances",
            "Selected workspace instances.",
            string.Empty,
            null);
        session.UpsertFact(root, "InstanceImport", "IncludedEntityCount", includedEntities.Length.ToString(), "Number");
        session.EnsureViewNode(root, root.DisplayName);

        var importedInstances = 0;
        var importedPropertyFacts = 0;
        var importedRelationships = 0;

        var instanceSubjects = new Dictionary<(string EntityName, string RecordId), DocumentationSubject>(StringComparerTuple.OrdinalIgnoreCase);
        foreach (var entity in includedEntities)
        {
            if (!workspace.Instance.RecordsByEntity.TryGetValue(entity.Name, out var records))
            {
                continue;
            }

            var entitySubject = FindModelEntitySubject(model, normalizedModelSourceId, entity.Name);
            var parentKey = entitySubject?.Id ?? root.Id;
            var orderedRecords = records
                .OrderBy(record => record.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            DocumentationSubject? previousInstance = null;
            foreach (var record in orderedRecords)
            {
                var subject = ImportInstanceSubject(
                    session,
                    policy,
                    normalizedModelSourceId,
                    normalizedSourceId,
                    normalizedDisplayName,
                    entity,
                    record,
                    parentKey,
                    previousInstance);
                instanceSubjects[(entity.Name, record.Id)] = subject;
                previousInstance = subject;
                importedInstances++;
            }
        }

        foreach (var entity in includedEntities)
        {
            if (!workspace.Instance.RecordsByEntity.TryGetValue(entity.Name, out var records))
            {
                continue;
            }

            foreach (var record in records.OrderBy(record => record.Id, StringComparer.OrdinalIgnoreCase))
            {
                if (!instanceSubjects.TryGetValue((entity.Name, record.Id), out var subject))
                {
                    continue;
                }

                importedPropertyFacts += ImportPropertyFacts(
                    session,
                    policy,
                    model,
                    normalizedModelSourceId,
                    entity,
                    record,
                    subject);
                importedRelationships += ImportRelationships(
                    session,
                    policy,
                    model,
                    normalizedModelSourceId,
                    instanceSubjects,
                    entity,
                    record,
                    subject);
            }
        }

        session.Complete();
        return new MetaDocsWorkspaceInstanceImportResult(
            root,
            importedInstances,
            importedPropertyFacts,
            importedRelationships);
    }

    private static DocumentationSubject ImportInstanceSubject(
        MetaDocsImportSession session,
        MetaDocsInstanceImportPolicy policy,
        string modelSourceId,
        string instanceSourceId,
        string sourceDisplayName,
        GenericEntity entity,
        GenericRecord record,
        string parentKey,
        DocumentationSubject? previousInstance)
    {
        var entitySpec = policy.FindEntitySpec(entity.Name, modelSourceId);
        var displayName = ResolveRecordText(record, entitySpec?.DisplayNameProperty);
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = record.Id;
        }

        var summary = ResolveRecordText(record, entitySpec?.SummaryProperty);
        if (string.IsNullOrWhiteSpace(summary))
        {
            summary = $"{entity.Name} instance {record.Id}.";
        }

        var subject = session.UpsertSubject(
            BuildInstanceKey(instanceSourceId, entity.Name, record.Id),
            "Instance",
            entity.Name,
            record.Id,
            displayName,
            $"{sourceDisplayName}.{entity.Name}.{displayName}",
            summary,
            parentKey,
            previousInstance);
        session.UpsertFact(subject, "Instance", "EntityName", entity.Name);
        session.UpsertFact(subject, "Instance", "NativeId", record.Id);
        if (!string.IsNullOrWhiteSpace(record.SourceShardFileName))
        {
            session.UpsertFact(subject, "Instance", "SourceShard", record.SourceShardFileName);
        }

        return subject;
    }

    private static int ImportPropertyFacts(
        MetaDocsImportSession session,
        MetaDocsInstanceImportPolicy policy,
        MetaDocsModel docsModel,
        string modelSourceId,
        GenericEntity entity,
        GenericRecord record,
        DocumentationSubject instanceSubject)
    {
        var count = 0;
        DocumentationRelationship? previousRelationship = null;
        foreach (var property in entity.Properties
                     .Where(property => !string.Equals(property.Name, "Id", StringComparison.OrdinalIgnoreCase))
                     .OrderBy(property => property.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (!policy.IncludesProperty(entity.Name, property.Name, modelSourceId))
            {
                continue;
            }

            if (!record.Values.TryGetValue(property.Name, out var value))
            {
                continue;
            }

            session.UpsertFact(
                instanceSubject,
                "InstancePropertyValue",
                property.Name,
                value,
                InferValueKind(property.DataType, value));
            var propertySubject = FindModelPropertySubject(docsModel, modelSourceId, entity.Name, property.Name);
            if (propertySubject is not null)
            {
                previousRelationship = session.UpsertRelationship(
                    instanceSubject.Id,
                    "DocumentsProperty",
                    propertySubject.Id,
                    previousRelationship);
            }

            count++;
        }

        return count;
    }

    private static int ImportRelationships(
        MetaDocsImportSession session,
        MetaDocsInstanceImportPolicy policy,
        MetaDocsModel docsModel,
        string modelSourceId,
        IReadOnlyDictionary<(string EntityName, string RecordId), DocumentationSubject> instanceSubjects,
        GenericEntity entity,
        GenericRecord record,
        DocumentationSubject instanceSubject)
    {
        var count = 0;
        DocumentationRelationship? previousRelationship = null;
        foreach (var relationship in entity.Relationships
                     .OrderBy(relationship => relationship.GetColumnName(), StringComparer.OrdinalIgnoreCase))
        {
            if (!IsRelationshipIncluded(policy, entity.Name, relationship, modelSourceId))
            {
                continue;
            }

            var columnName = relationship.GetColumnName();
            if (!record.RelationshipIds.TryGetValue(columnName, out var targetRecordId) ||
                string.IsNullOrWhiteSpace(targetRecordId))
            {
                continue;
            }

            if (instanceSubjects.TryGetValue((relationship.Entity, targetRecordId), out var targetSubject))
            {
                previousRelationship = session.UpsertRelationship(
                    instanceSubject.Id,
                    $"InstanceRelationship:{relationship.GetNavigationName()}",
                    targetSubject.Id,
                    previousRelationship);
            }
            else
            {
                session.UpsertFact(
                    instanceSubject,
                    "InstanceRelationshipTarget",
                    relationship.GetNavigationName(),
                    $"{relationship.Entity}:{targetRecordId}");
            }

            var relationshipSubject = FindModelRelationshipSubject(docsModel, modelSourceId, entity.Name, relationship);
            if (relationshipSubject is not null)
            {
                previousRelationship = session.UpsertRelationship(
                    instanceSubject.Id,
                    "DocumentsRelationship",
                    relationshipSubject.Id,
                    previousRelationship);
            }

            count++;
        }

        return count;
    }

    private static bool IsRelationshipIncluded(
        MetaDocsInstanceImportPolicy policy,
        string entityName,
        GenericRelationship relationship,
        string sourceId) =>
        policy.IncludesRelationship(entityName, relationship.GetNavigationName(), sourceId) ||
        policy.IncludesRelationship(entityName, relationship.GetColumnName(), sourceId) ||
        policy.IncludesRelationship(entityName, BuildRelationshipSelector(relationship), sourceId);

    private static DocumentationSubject? FindModelEntitySubject(MetaDocsModel model, string sourceId, string entityName) =>
        model.DocumentationSubjectList.FirstOrDefault(subject =>
            string.Equals(subject.Id, BuildEntityKey(sourceId, entityName), StringComparison.OrdinalIgnoreCase));

    private static DocumentationSubject? FindModelPropertySubject(
        MetaDocsModel model,
        string sourceId,
        string entityName,
        string propertyName) =>
        model.DocumentationSubjectList.FirstOrDefault(subject =>
            string.Equals(subject.Id, $"{BuildEntityKey(sourceId, entityName)}:property:{MetaDocsImportSession.NormalizeKey(propertyName)}", StringComparison.OrdinalIgnoreCase));

    private static DocumentationSubject? FindModelRelationshipSubject(
        MetaDocsModel model,
        string sourceId,
        string entityName,
        GenericRelationship relationship) =>
        model.DocumentationSubjectList.FirstOrDefault(subject =>
            string.Equals(subject.Id, $"{BuildEntityKey(sourceId, entityName)}:relationship:{MetaDocsImportSession.NormalizeKey(BuildRelationshipSelector(relationship))}", StringComparison.OrdinalIgnoreCase));

    private static string BuildEntityKey(string sourceId, string entityName) =>
        $"{sourceId}:entity:{MetaDocsImportSession.NormalizeKey(entityName)}";

    private static string BuildInstanceKey(string sourceId, string entityName, string recordId) =>
        $"{sourceId}:instance:{MetaDocsImportSession.NormalizeKey(entityName)}:{MetaDocsImportSession.NormalizeKey(recordId)}";

    private static string BuildRelationshipSelector(GenericRelationship relationship) =>
        $"{relationship.GetRoleOrDefault()}:{relationship.Entity}";

    private static string ResolveRecordText(GenericRecord record, string? propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return string.Empty;
        }

        return record.Values.TryGetValue(propertyName, out var value)
            ? value
            : string.Empty;
    }

    private static string InferValueKind(string? dataType, string value)
    {
        if (bool.TryParse(value, out _))
        {
            return "Boolean";
        }

        if (decimal.TryParse(value, out _))
        {
            return "Number";
        }

        return string.Equals(dataType, "string", StringComparison.OrdinalIgnoreCase)
            ? "String"
            : string.IsNullOrWhiteSpace(dataType)
                ? "String"
                : dataType!;
    }

    private static string ComputeSourceFingerprint(Workspace workspace)
    {
        var builder = new StringBuilder();
        builder.Append(workspace.Model.ComputeContractSignature());
        foreach (var entity in workspace.Instance.RecordsByEntity.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append('|').Append(entity.Key).Append(':').Append(entity.Value.Count);
            foreach (var record in entity.Value.OrderBy(record => record.Id, StringComparer.OrdinalIgnoreCase))
            {
                builder.Append('|').Append(record.Id);
                foreach (var value in record.Values.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
                {
                    builder.Append('|').Append(value.Key).Append('=').Append(value.Value);
                }

                foreach (var relationship in record.RelationshipIds.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
                {
                    builder.Append('|').Append(relationship.Key).Append("->").Append(relationship.Value);
                }
            }
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private sealed class StringComparerTuple : IEqualityComparer<(string EntityName, string RecordId)>
    {
        public static readonly StringComparerTuple OrdinalIgnoreCase = new();

        public bool Equals((string EntityName, string RecordId) x, (string EntityName, string RecordId) y) =>
            string.Equals(x.EntityName, y.EntityName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.RecordId, y.RecordId, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string EntityName, string RecordId) obj) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.EntityName),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.RecordId));
    }
}

public sealed record MetaDocsWorkspaceInstanceImportResult(
    DocumentationSubject RootSubject,
    int ImportedInstanceCount,
    int ImportedPropertyFactCount,
    int ImportedRelationshipCount);
