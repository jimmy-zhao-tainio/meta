using Meta.Core.Domain;
using Meta.Core.Services;
using System.Security.Cryptography;
using System.Text;
using MetaDocs;

namespace MetaDocs.Core;

public sealed class MetaDocsWorkspaceModelImporter
{
    private readonly WorkspaceService workspaceService = new();

    public async Task<DocumentationSubject> ImportWorkspaceModelAsync(
        MetaDocsModel model,
        string sourceWorkspacePath,
        string sourceId = "",
        string displayName = "",
        int ordinal = 100,
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
            ? $"source:workspace-model:{MetaDocsImportSession.NormalizeKey(normalizedDisplayName)}"
            : sourceId;
        var session = new MetaDocsImportSession(
            model,
            normalizedSourceId,
            "WorkspaceModel",
            normalizedDisplayName,
            Path.GetFullPath(sourceWorkspacePath),
            ComputeSourceFingerprint(workspace.Model.ComputeContractSignature()),
            "MetaDocs.WorkspaceModel",
            "1");

        var workspaceSubject = session.UpsertSubject(
            $"{normalizedSourceId}:workspace",
            "Workspace",
            "Workspace",
            workspace.WorkspaceRootPath,
            normalizedDisplayName,
            normalizedDisplayName,
            "Metadata workspace.",
            string.Empty,
            ordinal);
        session.UpsertFact(workspaceSubject, "Workspace", "RootPath", workspace.WorkspaceRootPath);
        session.UpsertFact(workspaceSubject, "Workspace", "ModelName", modelName);
        session.EnsureViewNode(workspaceSubject, normalizedDisplayName, ordinal);

        var modelSubject = session.UpsertSubject(
            $"{normalizedSourceId}:model:{MetaDocsImportSession.NormalizeKey(modelName)}",
            "Model",
            "GenericModel",
            modelName,
            modelName,
            $"{normalizedDisplayName}.{modelName}",
            $"Model {modelName}.",
            workspaceSubject.Id,
            10);
        session.UpsertFact(modelSubject, "Model", "Name", modelName);
        session.UpsertFact(modelSubject, "Model", "EntityCount", workspace.Model.Entities.Count.ToString(), "Number");
        session.UpsertFact(modelSubject, "Model", "ContractSignature", workspace.Model.ComputeContractSignature());
        session.UpsertRelationship(workspaceSubject.Id, "ContainsModel", modelSubject.Id, 10);

        var entities = workspace.Model.Entities
            .OrderBy(entity => entity.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        for (var i = 0; i < entities.Length; i++)
        {
            AddEntity(session, normalizedSourceId, normalizedDisplayName, modelSubject, entities[i], i + 1);
        }

        session.Complete();
        return workspaceSubject;
    }

    private static void AddEntity(
        MetaDocsImportSession session,
        string sourceId,
        string sourceDisplayName,
        DocumentationSubject modelSubject,
        GenericEntity entity,
        int ordinal)
    {
        var entityKey = BuildEntityKey(sourceId, entity.Name);
        var subject = session.UpsertSubject(
            entityKey,
            "Entity",
            "GenericEntity",
            entity.Name,
            entity.Name,
            $"{sourceDisplayName}.{entity.Name}",
            $"Entity {entity.Name}.",
            modelSubject.Id,
            ordinal);
        session.UpsertFact(subject, "Model", "Name", entity.Name);
        session.UpsertFact(subject, "Model", "ListName", entity.GetListName());
        session.UpsertFact(subject, "Model", "PropertyCount", entity.Properties.Count.ToString(), "Number");
        session.UpsertFact(subject, "Model", "RelationshipCount", entity.Relationships.Count.ToString(), "Number");
        session.UpsertRelationship(modelSubject.Id, "ContainsEntity", subject.Id, ordinal);

        var properties = entity.Properties
            .OrderBy(property => property.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        for (var i = 0; i < properties.Length; i++)
        {
            AddProperty(session, sourceId, sourceDisplayName, subject, entity, properties[i], i + 1);
        }

        var relationships = entity.Relationships
            .OrderBy(relationship => relationship.GetColumnName(), StringComparer.OrdinalIgnoreCase)
            .ThenBy(relationship => relationship.Entity, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        for (var i = 0; i < relationships.Length; i++)
        {
            AddRelationship(session, sourceId, sourceDisplayName, subject, entity, relationships[i], i + 1);
        }
    }

    private static void AddProperty(
        MetaDocsImportSession session,
        string sourceId,
        string sourceDisplayName,
        DocumentationSubject entitySubject,
        GenericEntity entity,
        GenericProperty property,
        int ordinal)
    {
        var subject = session.UpsertSubject(
            $"{BuildEntityKey(sourceId, entity.Name)}:property:{MetaDocsImportSession.NormalizeKey(property.Name)}",
            "Property",
            "GenericProperty",
            property.Name,
            property.Name,
            $"{sourceDisplayName}.{entity.Name}.{property.Name}",
            $"{(property.IsNullable ? "Optional" : "Required")} {property.DataType} property.",
            entitySubject.Id,
            ordinal);
        session.UpsertFact(subject, "Model", "Name", property.Name);
        session.UpsertFact(subject, "Model", "DataType", string.IsNullOrWhiteSpace(property.DataType) ? "string" : property.DataType);
        session.UpsertFact(subject, "Model", "Required", (!property.IsNullable).ToString(), "Boolean");
        session.UpsertFact(subject, "Model", "Nullable", property.IsNullable.ToString(), "Boolean");
        session.UpsertRelationship(entitySubject.Id, "ContainsProperty", subject.Id, ordinal);
    }

    private static void AddRelationship(
        MetaDocsImportSession session,
        string sourceId,
        string sourceDisplayName,
        DocumentationSubject entitySubject,
        GenericEntity entity,
        GenericRelationship relationship,
        int ordinal)
    {
        var selector = $"{relationship.GetRoleOrDefault()}:{relationship.Entity}";
        var subject = session.UpsertSubject(
            $"{BuildEntityKey(sourceId, entity.Name)}:relationship:{MetaDocsImportSession.NormalizeKey(selector)}",
            "Relationship",
            "GenericRelationship",
            selector,
            relationship.GetNavigationName(),
            $"{sourceDisplayName}.{entity.Name}.{relationship.GetNavigationName()}",
            $"{(relationship.IsNullable ? "Optional" : "Required")} relationship to {relationship.Entity}.",
            entitySubject.Id,
            1000 + ordinal);
        session.UpsertFact(subject, "Model", "TargetEntity", relationship.Entity);
        session.UpsertFact(subject, "Model", "Role", relationship.Role);
        session.UpsertFact(subject, "Model", "ColumnName", relationship.GetColumnName());
        session.UpsertFact(subject, "Model", "Required", (!relationship.IsNullable).ToString(), "Boolean");
        session.UpsertFact(subject, "Model", "Nullable", relationship.IsNullable.ToString(), "Boolean");
        session.UpsertRelationship(entitySubject.Id, "ContainsRelationship", subject.Id, ordinal);

        var targetKey = BuildEntityKey(sourceId, relationship.Entity);
        session.UpsertRelationship(subject.Id, "ReferencesEntity", targetKey, ordinal);
    }

    private static string BuildEntityKey(string sourceId, string entityName) =>
        $"{sourceId}:entity:{MetaDocsImportSession.NormalizeKey(entityName)}";

    private static string ComputeSourceFingerprint(string contractSignature)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(contractSignature));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
