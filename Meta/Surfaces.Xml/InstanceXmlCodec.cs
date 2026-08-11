using System.Globalization;
using System.Xml.Linq;
using Meta.Operations.Domain;

namespace Meta.Surfaces.Xml;

internal readonly record struct InstanceRecordIdentity(
    string EntityName,
    string RecordId);

public static class InstanceXmlCodec
{
    public static GenericInstance LoadFromPath(
        string instancePath,
        GenericModel model)
    {
        if (string.IsNullOrWhiteSpace(instancePath))
        {
            throw new ArgumentException("Instance path is required.", nameof(instancePath));
        }

        ArgumentNullException.ThrowIfNull(model);

        var document = XDocument.Load(instancePath, LoadOptions.None);
        var instance = new GenericInstance
        {
            ModelName = model.Name ?? string.Empty,
        };

        MergeDocument(instance, document, model);
        if (string.IsNullOrWhiteSpace(instance.ModelName))
        {
            instance.ModelName = model.Name ?? string.Empty;
        }

        return instance;
    }

    public static GenericInstance LoadFromPaths(
        IReadOnlyCollection<string> shardPaths,
        GenericModel model)
    {
        ArgumentNullException.ThrowIfNull(shardPaths);
        ArgumentNullException.ThrowIfNull(model);

        var instance = new GenericInstance
        {
            ModelName = model.Name ?? string.Empty,
        };

        foreach (var path in shardPaths)
        {
            var document = XDocument.Load(path, LoadOptions.None);
            MergeDocument(instance, document, model);
        }

        if (string.IsNullOrWhiteSpace(instance.ModelName))
        {
            instance.ModelName = model.Name ?? string.Empty;
        }

        return instance;
    }

    public static void MergeDocument(
        GenericInstance instance,
        XDocument document,
        GenericModel model)
    {
        MergeDocumentAndGetRecordIdentities(
            instance,
            document,
            model);
    }

    internal static IReadOnlyList<InstanceRecordIdentity>
        MergeDocumentAndGetRecordIdentities(
        GenericInstance instance,
        XDocument document,
        GenericModel model)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(model);

        var root = document.Root ?? throw new InvalidDataException("Instance XML has no root element.");
        if (string.IsNullOrWhiteSpace(instance.ModelName))
        {
            instance.ModelName = root.Name.LocalName;
        }

        var entityByContainer = BuildEntityByContainerLookup(model);
        var loadedRecords = new List<InstanceRecordIdentity>();

        foreach (var listElement in root.Elements())
        {
            var listName = listElement.Name.LocalName;
            if (!entityByContainer.TryGetValue(listName, out var modelEntity))
            {
                throw new InvalidDataException(
                    $"Instance XML list '{listName}' references unknown entity.");
            }

            var entityName = modelEntity.Name;
            var records = instance.GetOrCreateEntityRecords(entityName);
            foreach (var rowElement in listElement.Elements())
            {
                if (!string.Equals(rowElement.Name.LocalName, entityName, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        $"Instance XML list '{listName}' contains unexpected row element '{rowElement.Name.LocalName}'. Expected '{entityName}'.");
                }

                var record = ParseRecord(entityName, modelEntity, rowElement);
                records.Add(record);
                loadedRecords.Add(new InstanceRecordIdentity(
                    entityName,
                    record.Id));
            }
        }

        return loadedRecords;
    }

    public static XDocument BuildDocument(
        GenericModel model,
        GenericInstance instance,
        string? modelName = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(instance);

        var rootName = ResolveRootName(model, instance, modelName);
        var root = new XElement(rootName);

        var modelEntityNames = model.Entities
            .Where(entity => !string.IsNullOrWhiteSpace(entity.Name))
            .Select(entity => entity.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var unknownEntity in instance.RecordsByEntity.Keys
                     .Where(name => !modelEntityNames.Contains(name))
                     .OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Cannot write instance document for unknown entity '{unknownEntity}'.");
        }

        foreach (var modelEntity in model.Entities.Where(entity => !string.IsNullOrWhiteSpace(entity.Name)))
        {
            IReadOnlyCollection<GenericRecord> records = instance.RecordsByEntity.TryGetValue(modelEntity.Name, out var entityRecords)
                ? entityRecords
                : Array.Empty<GenericRecord>();
            AppendEntityContainer(root, modelEntity, records);
        }

        return new XDocument(new XDeclaration("1.0", "utf-8", null), root);
    }

    public static XDocument BuildEntityDocument(
        GenericModel model,
        string entityName,
        IReadOnlyCollection<GenericRecord> records,
        string? modelName = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(records);

        var modelEntity = model.Entities.FirstOrDefault(entity =>
            string.Equals(entity.Name, entityName, StringComparison.OrdinalIgnoreCase));
        if (modelEntity == null)
        {
            throw new InvalidOperationException($"Cannot write instance document for unknown entity '{entityName}'.");
        }

        var rootName = ResolveRootName(model, instance: null, modelName);
        var root = new XElement(rootName);
        AppendEntityContainer(root, modelEntity, records);
        return new XDocument(new XDeclaration("1.0", "utf-8", null), root);
    }

    private static void AppendEntityContainer(
        XElement root,
        GenericEntity modelEntity,
        IReadOnlyCollection<GenericRecord> records)
    {
        var entityName = modelEntity.Name;
        var propertyByName = modelEntity.Properties
            .Where(property => !string.Equals(property.Name, "Id", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(property => property.Name, StringComparer.OrdinalIgnoreCase);
        var orderedPropertyNames = modelEntity.Properties
            .Where(property => !string.Equals(property.Name, "Id", StringComparison.OrdinalIgnoreCase))
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var orderedRelationships = modelEntity.Relationships
            .OrderBy(relationship => relationship.GetColumnName(), StringComparer.OrdinalIgnoreCase)
            .ThenBy(relationship => relationship.Entity, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var listElement = new XElement(modelEntity.GetListName());
        root.Add(listElement);

        foreach (var record in records.OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase))
        {
            var recordId = MetaIdentity.Require(
                record.Id,
                $"Cannot write entity '{entityName}' row with Id '{record.Id}'.");

            var recordElement = new XElement(entityName, new XAttribute("Id", recordId));

            foreach (var unknownPropertyName in record.Values.Keys
                         .Where(key => !propertyByName.ContainsKey(key))
                         .OrderBy(key => key, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Entity '{entityName}' row '{record.Id}' contains unknown property '{unknownPropertyName}'.");
            }

            foreach (var relationship in orderedRelationships)
            {
                var relationshipName = relationship.GetColumnName();
                if (!record.RelationshipIds.TryGetValue(relationshipName, out var relationshipId) ||
                    string.IsNullOrWhiteSpace(relationshipId))
                {
                    if (relationship.IsNullable)
                    {
                        continue;
                    }

                    throw new InvalidOperationException(
                        $"Entity '{entityName}' row '{recordId}' is missing required relationship '{relationshipName}'.");
                }

                var normalizedRelationshipId = MetaIdentity.Require(
                    relationshipId,
                    $"Entity '{entityName}' row '{recordId}' has invalid relationship '{relationshipName}' value '{relationshipId}'.");

                recordElement.Add(new XAttribute(relationshipName, normalizedRelationshipId));
            }

            var knownRelationshipNames = orderedRelationships
                .Select(relationship => relationship.GetColumnName())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var unknownRelationshipName in record.RelationshipIds.Keys
                         .Where(key => !knownRelationshipNames.Contains(key))
                         .OrderBy(key => key, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Entity '{entityName}' row '{record.Id}' contains unknown relationship '{unknownRelationshipName}'.");
            }

            foreach (var propertyName in orderedPropertyNames)
            {
                if (!record.Values.TryGetValue(propertyName, out var value) || value == null)
                {
                    continue;
                }

                recordElement.Add(new XElement(propertyName, value));
            }

            listElement.Add(recordElement);
        }
    }

    private static string ResolveRootName(
        GenericModel model,
        GenericInstance? instance,
        string? modelNameOverride)
    {
        var modelName = !string.IsNullOrWhiteSpace(modelNameOverride)
            ? modelNameOverride
            : !string.IsNullOrWhiteSpace(model.Name)
                ? model.Name
                : instance?.ModelName;
        return string.IsNullOrWhiteSpace(modelName) ? "MetadataModel" : modelName;
    }

    private static GenericRecord ParseRecord(
        string entityName,
        GenericEntity modelEntity,
        XElement rowElement)
    {
        var id = (string?)rowElement.Attribute("Id");
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new InvalidDataException(
                $"Entity '{entityName}' row is missing valid Id.");
        }

        if (!MetaIdentity.TryValidate(id, out var identityError))
        {
            throw new InvalidDataException(
                $"Entity '{entityName}' row has invalid Id. {identityError}");
        }

        var record = new GenericRecord
        {
            Id = id!,
        };

        var propertyByName = modelEntity.Properties
            .Where(property => !string.Equals(property.Name, "Id", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(property => property.Name, StringComparer.OrdinalIgnoreCase);
        var relationshipByName = modelEntity.Relationships
            .ToDictionary(relationship => relationship.GetColumnName(), StringComparer.OrdinalIgnoreCase);
        var relationshipByAttributeName = relationshipByName;

        foreach (var attribute in rowElement.Attributes())
        {
            var attributeName = attribute.Name.LocalName;
            if (string.Equals(attributeName, "Id", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (relationshipByAttributeName.TryGetValue(attributeName, out var relationship))
            {
                var relationshipId = attribute.Value;
                if (!MetaIdentity.TryValidate(relationshipId, out var relationshipIdentityError))
                {
                    throw new InvalidDataException(
                        $"Entity '{entityName}' row '{record.Id}' has invalid relationship '{relationship.GetColumnName()}' value '{attribute.Value}'. {relationshipIdentityError}");
                }

                if (!record.RelationshipIds.TryAdd(relationship.GetColumnName(), relationshipId))
                {
                    throw new InvalidDataException(
                        $"Entity '{entityName}' row '{record.Id}' supplies relationship '{relationship.GetColumnName()}' more than once.");
                }
                continue;
            }

            throw new InvalidDataException(
                $"Entity '{entityName}' row '{record.Id}' has unsupported attribute '{attributeName}'.");
        }

        var seenProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var element in rowElement.Elements())
        {
            var elementName = element.Name.LocalName;
            if (relationshipByName.TryGetValue(elementName, out var relationship))
            {
                throw new InvalidDataException(
                    $"Entity '{entityName}' row '{record.Id}' has relationship element '{relationship.GetColumnName()}'. Relationships must be attributes.");
            }

            if (!propertyByName.TryGetValue(elementName, out var property))
            {
                throw new InvalidDataException(
                    $"Entity '{entityName}' row '{record.Id}' has unknown property element '{elementName}'.");
            }

            if (!seenProperties.Add(property.Name))
            {
                throw new InvalidDataException(
                    $"Entity '{entityName}' row '{record.Id}' has duplicate property element '{property.Name}'.");
            }

            record.Values[property.Name] = element.Value;
        }

        foreach (var relationship in modelEntity.Relationships)
        {
            var relationshipName = relationship.GetColumnName();
            if (!record.RelationshipIds.TryGetValue(relationshipName, out var relationshipId) ||
                string.IsNullOrWhiteSpace(relationshipId))
            {
                if (relationship.IsNullable)
                {
                    continue;
                }

                throw new InvalidDataException(
                    $"Entity '{entityName}' row '{record.Id}' is missing required relationship '{relationship.GetColumnName()}'.");
            }
        }

        return record;
    }

    private static Dictionary<string, GenericEntity> BuildEntityByContainerLookup(GenericModel model)
    {
        var lookup = new Dictionary<string, GenericEntity>(StringComparer.OrdinalIgnoreCase);
        foreach (var entity in model.Entities.Where(item => !string.IsNullOrWhiteSpace(item.Name)))
        {
            var containerName = entity.GetListName();
            if (!lookup.TryAdd(containerName, entity))
            {
                throw new InvalidDataException(
                    $"Model has duplicate instance container name '{containerName}' for multiple entities.");
            }
        }

        return lookup;
    }

}


