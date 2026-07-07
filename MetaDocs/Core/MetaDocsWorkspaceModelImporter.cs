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
            string.Empty,
            string.Empty,
            null);
        session.UpsertFact(workspaceSubject, "Workspace", "RootPath", workspace.WorkspaceRootPath);
        session.UpsertFact(workspaceSubject, "Workspace", "ModelName", modelName);
        session.EnsureViewNode(workspaceSubject, normalizedDisplayName);

        var modelSubject = session.UpsertSubject(
            $"{normalizedSourceId}:model:{MetaDocsImportSession.NormalizeKey(modelName)}",
            "Model",
            "GenericModel",
            modelName,
            modelName,
            $"{normalizedDisplayName}.{modelName}",
            string.Empty,
            workspaceSubject.Id,
            null);
        session.UpsertFact(modelSubject, "Model", "Name", modelName);
        session.UpsertFact(modelSubject, "Model", "EntityCount", workspace.Model.Entities.Count.ToString(), "Number");
        session.UpsertFact(modelSubject, "Model", "ContractSignature", workspace.Model.ComputeContractSignature());
        session.UpsertRelationship(workspaceSubject.Id, "ContainsModel", modelSubject.Id);

        var entities = workspace.Model.Entities
            .OrderBy(entity => entity.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        DocumentationSubject? previousEntity = null;
        DocumentationRelationship? previousEntityRelationship = null;
        foreach (var entity in entities)
        {
            (previousEntity, previousEntityRelationship) = AddEntity(
                session,
                normalizedSourceId,
                normalizedDisplayName,
                modelSubject,
                entity,
                previousEntity,
                previousEntityRelationship);
        }

        session.Complete();
        return workspaceSubject;
    }

    private static (DocumentationSubject Entity, DocumentationRelationship ContainsRelationship) AddEntity(
        MetaDocsImportSession session,
        string sourceId,
        string sourceDisplayName,
        DocumentationSubject modelSubject,
        GenericEntity entity,
        DocumentationSubject? previousEntity,
        DocumentationRelationship? previousEntityRelationship)
    {
        var entityKey = BuildEntityKey(sourceId, entity.Name);
        var subject = session.UpsertSubject(
            entityKey,
            "Entity",
            "GenericEntity",
            entity.Name,
            entity.Name,
            $"{sourceDisplayName}.{entity.Name}",
            string.Empty,
            modelSubject.Id,
            previousEntity);
        session.UpsertFact(subject, "Model", "Name", entity.Name);
        session.UpsertFact(subject, "Model", "ListName", entity.GetListName());
        session.UpsertFact(subject, "Model", "PropertyCount", entity.Properties.Count.ToString(), "Number");
        session.UpsertFact(subject, "Model", "RelationshipCount", entity.Relationships.Count.ToString(), "Number");
        var containsRelationship = session.UpsertRelationship(
            modelSubject.Id,
            "ContainsEntity",
            subject.Id,
            previousEntityRelationship);

        var properties = entity.Properties
            .OrderBy(property => property.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        DocumentationSubject? previousProperty = null;
        DocumentationRelationship? previousPropertyRelationship = null;
        foreach (var property in properties)
        {
            (previousProperty, previousPropertyRelationship) = AddProperty(
                session,
                sourceId,
                sourceDisplayName,
                subject,
                entity,
                property,
                previousProperty,
                previousPropertyRelationship);
        }

        var relationships = entity.Relationships
            .OrderBy(relationship => relationship.GetColumnName(), StringComparer.OrdinalIgnoreCase)
            .ThenBy(relationship => relationship.Entity, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        DocumentationSubject? previousRelationshipSubject = null;
        DocumentationRelationship? previousContainsRelationship = null;
        foreach (var relationship in relationships)
        {
            (previousRelationshipSubject, previousContainsRelationship) = AddRelationship(
                session,
                sourceId,
                sourceDisplayName,
                subject,
                entity,
                relationship,
                previousRelationshipSubject,
                previousContainsRelationship);
        }

        return (subject, containsRelationship);
    }

    private static (DocumentationSubject Property, DocumentationRelationship ContainsRelationship) AddProperty(
        MetaDocsImportSession session,
        string sourceId,
        string sourceDisplayName,
        DocumentationSubject entitySubject,
        GenericEntity entity,
        GenericProperty property,
        DocumentationSubject? previousProperty,
        DocumentationRelationship? previousPropertyRelationship)
    {
        var subject = session.UpsertSubject(
            $"{BuildEntityKey(sourceId, entity.Name)}:property:{MetaDocsImportSession.NormalizeKey(property.Name)}",
            "Property",
            "GenericProperty",
            property.Name,
            property.Name,
            $"{sourceDisplayName}.{entity.Name}.{property.Name}",
            string.Empty,
            entitySubject.Id,
            previousProperty);
        session.UpsertFact(subject, "Model", "Name", property.Name);
        session.UpsertFact(subject, "Model", "DataType", string.IsNullOrWhiteSpace(property.DataType) ? "string" : property.DataType);
        session.UpsertFact(subject, "Model", "Required", (!property.IsNullable).ToString(), "Boolean");
        session.UpsertFact(subject, "Model", "Nullable", property.IsNullable.ToString(), "Boolean");
        var containsRelationship = session.UpsertRelationship(
            entitySubject.Id,
            "ContainsProperty",
            subject.Id,
            previousPropertyRelationship);
        return (subject, containsRelationship);
    }

    private static (DocumentationSubject Relationship, DocumentationRelationship ContainsRelationship) AddRelationship(
        MetaDocsImportSession session,
        string sourceId,
        string sourceDisplayName,
        DocumentationSubject entitySubject,
        GenericEntity entity,
        GenericRelationship relationship,
        DocumentationSubject? previousRelationshipSubject,
        DocumentationRelationship? previousContainsRelationship)
    {
        var selector = $"{relationship.GetRoleOrDefault()}:{relationship.Entity}";
        var subject = session.UpsertSubject(
            $"{BuildEntityKey(sourceId, entity.Name)}:relationship:{MetaDocsImportSession.NormalizeKey(selector)}",
            "Relationship",
            "GenericRelationship",
            selector,
            relationship.GetNavigationName(),
            $"{sourceDisplayName}.{entity.Name}.{relationship.GetNavigationName()}",
            string.Empty,
            entitySubject.Id,
            previousRelationshipSubject);
        session.UpsertFact(subject, "Model", "TargetEntity", relationship.Entity);
        session.UpsertFact(subject, "Model", "Role", relationship.Role);
        session.UpsertFact(subject, "Model", "ColumnName", relationship.GetColumnName());
        session.UpsertFact(subject, "Model", "Required", (!relationship.IsNullable).ToString(), "Boolean");
        session.UpsertFact(subject, "Model", "Nullable", relationship.IsNullable.ToString(), "Boolean");
        var containsRelationship = session.UpsertRelationship(
            entitySubject.Id,
            "ContainsRelationship",
            subject.Id,
            previousContainsRelationship);

        var targetKey = BuildEntityKey(sourceId, relationship.Entity);
        session.UpsertRelationship(subject.Id, "ReferencesEntity", targetKey);
        return (subject, containsRelationship);
    }

    private static string BuildEntityKey(string sourceId, string entityName) =>
        $"{sourceId}:entity:{MetaDocsImportSession.NormalizeKey(entityName)}";

    private static string ComputeSourceFingerprint(string contractSignature)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(contractSignature));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
