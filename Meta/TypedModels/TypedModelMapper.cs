using System.Collections;
using System.Reflection;
using System.Xml.Serialization;
using Meta.Operations;
using Meta.Operations.Domain;

namespace Meta.TypedModels;

public static class TypedModelMapper
{
    private static readonly object CacheLock = new();
    private static readonly Dictionary<Type, ModelMap> ModelMaps = new();

    public static InMemoryWorkspace ToWorkspace<TModel>(TModel model)
        where TModel : class
    {
        ArgumentNullException.ThrowIfNull(model);
        var map = GetModelMap(typeof(TModel));
        var indexes = BuildIndexes(model, map);
        var genericModel = BuildGenericModel(map);
        var instance = new GenericInstance { ModelName = genericModel.Name };

        foreach (var entity in map.Entities)
        {
            var targetRows = instance.GetOrCreateEntityRecords(entity.Name);
            foreach (var typedRow in entity.Collection.GetList(model)
                         .Cast<object>()
                         .OrderBy(row => GetId(entity, row), MetaIdentity.Comparer)
                         .ThenBy(row => GetId(entity, row), StringComparer.Ordinal))
            {
                var row = new GenericRecord
                {
                    Id = GetRequiredId(
                        entity,
                        typedRow,
                        $"Entity '{entity.Name}' contains a row with empty Id."),
                };

                foreach (var scalar in entity.Scalars)
                {
                    if (scalar.Property.GetValue(typedRow) is string value)
                    {
                        row.Values.Add(scalar.Name, value);
                    }
                }

                foreach (var relationship in entity.Relationships)
                {
                    var target = relationship.Property.GetValue(typedRow);
                    if (target == null)
                    {
                        continue;
                    }

                    var targetEntity = map.EntitiesByName[relationship.TargetEntityName];
                    var targetId = GetRequiredId(
                        targetEntity,
                        target,
                        $"Relationship '{entity.Name}.{relationship.ColumnName}' on row '{row.Id}' references a target with empty Id.");
                    if (!indexes[relationship.TargetEntityName].TryGetValue(targetId, out var canonical) ||
                        !ReferenceEquals(canonical, target))
                    {
                        throw new InvalidOperationException(
                            $"Relationship '{entity.Name}.{relationship.ColumnName}' on row '{row.Id}' does not reference its canonical target.");
                    }

                    row.RelationshipIds.Add(relationship.ColumnName, targetId);
                }

                targetRows.Add(row);
            }
        }

        var workspace = new InMemoryWorkspace(genericModel, instance);
        EnsureValid(workspace, "Typed model produced invalid metadata.");
        return workspace;
    }

    public static TModel FromWorkspace<TModel>(
        InMemoryWorkspace workspace,
        Func<TModel> createModel)
        where TModel : class
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(createModel);
        EnsureValid(workspace, "Cannot map invalid metadata to a typed model.");

        var map = GetModelMap(typeof(TModel));
        EnsureMatchingModel(workspace.Model, map);
        var model = createModel() ??
            throw new InvalidOperationException($"Could not create typed model '{typeof(TModel).FullName}'.");
        var typedRowsByEntity = new Dictionary<string, Dictionary<string, object>>(MetaName.Comparer);
        var pending = new List<PendingRelationship>();

        foreach (var entity in map.Entities)
        {
            var rowsById = new Dictionary<string, object>(MetaIdentity.Comparer);
            typedRowsByEntity.Add(entity.Name, rowsById);
            var targetList = entity.Collection.GetList(model);
            var sourceRows = workspace.Instance.RecordsByEntity.TryGetValue(entity.Name, out var records)
                ? records
                : [];

            foreach (var sourceRow in sourceRows
                         .OrderBy(row => row.Id, MetaIdentity.Comparer)
                         .ThenBy(row => row.Id, StringComparer.Ordinal))
            {
                var typedRow = Activator.CreateInstance(entity.ItemType) ??
                    throw new InvalidOperationException($"Could not create entity '{entity.ItemType.FullName}'.");
                entity.IdProperty.SetValue(typedRow, sourceRow.Id);
                foreach (var scalar in entity.Scalars)
                {
                    scalar.Property.SetValue(
                        typedRow,
                        sourceRow.Values.TryGetValue(scalar.Name, out var value) ? value : null);
                }

                targetList.Add(typedRow);
                rowsById.Add(sourceRow.Id, typedRow);
                pending.Add(new PendingRelationship(entity, typedRow, sourceRow));
            }
        }

        foreach (var item in pending)
        {
            foreach (var relationship in item.Entity.Relationships)
            {
                if (!item.Source.RelationshipIds.TryGetValue(relationship.ColumnName, out var targetId))
                {
                    relationship.Property.SetValue(item.Target, null);
                    continue;
                }

                if (!typedRowsByEntity[relationship.TargetEntityName].TryGetValue(targetId, out var target))
                {
                    throw new InvalidOperationException(
                        $"Relationship '{item.Entity.Name}.{relationship.ColumnName}' on row '{item.Source.Id}' points to missing Id '{targetId}'.");
                }

                relationship.Property.SetValue(item.Target, target);
            }
        }

        BuildIndexes(model, map);
        return model;
    }

    public static GenericModel Describe<TModel>()
        where TModel : class => BuildGenericModel(GetModelMap(typeof(TModel)));

    private static ModelMap GetModelMap(Type modelType)
    {
        lock (CacheLock)
        {
            if (!ModelMaps.TryGetValue(modelType, out var map))
            {
                map = BuildModelMap(modelType);
                ModelMaps.Add(modelType, map);
            }

            return map;
        }
    }

    private static ModelMap BuildModelMap(Type modelType)
    {
        var rootAttribute = modelType.GetCustomAttribute<XmlRootAttribute>();
        var modelName = string.IsNullOrWhiteSpace(rootAttribute?.ElementName)
            ? InferModelName(modelType)
            : rootAttribute!.ElementName;
        var collections = modelType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.CanRead && property.CanWrite)
            .Select(property => EntityCollection.TryCreate(modelType, property))
            .Where(collection => collection != null)
            .Cast<EntityCollection>()
            .OrderBy(collection => collection.EntityName, MetaName.Comparer)
            .ThenBy(collection => collection.EntityName, StringComparer.Ordinal)
            .ToArray();

        var entityNameByType = new Dictionary<Type, string>();
        foreach (var collection in collections)
        {
            if (!entityNameByType.TryAdd(collection.ItemType, collection.EntityName))
            {
                throw new InvalidOperationException(
                    $"Model type '{modelType.FullName}' contains more than one collection for entity type '{collection.ItemType.FullName}'.");
            }
        }

        var entityTypes = entityNameByType.Keys.ToHashSet();
        var entities = collections
            .Select(collection => BuildEntityMap(modelType, collection, entityTypes, entityNameByType))
            .ToArray();
        var entitiesByName = new Dictionary<string, EntityMap>(MetaName.Comparer);
        foreach (var entity in entities)
        {
            if (!entitiesByName.TryAdd(entity.Name, entity))
            {
                throw new InvalidOperationException(
                    $"Model type '{modelType.FullName}' contains duplicate entity name '{entity.Name}'.");
            }
        }

        return new ModelMap(modelName, entities, entitiesByName);
    }

    private static EntityMap BuildEntityMap(
        Type modelType,
        EntityCollection collection,
        ISet<Type> entityTypes,
        IReadOnlyDictionary<Type, string> entityNameByType)
    {
        var idProperty = collection.ItemType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(property => property.CanRead && property.CanWrite &&
                                        property.GetIndexParameters().Length == 0 &&
                                        property.PropertyType == typeof(string) &&
                                        string.Equals(property.Name, "Id", StringComparison.OrdinalIgnoreCase));
        if (idProperty == null)
        {
            throw new InvalidOperationException(
                $"Entity type '{collection.ItemType.FullName}' in model '{modelType.FullName}' must declare public string Id {{ get; set; }}.");
        }

        var scalars = new List<ScalarMap>();
        var relationships = new List<RelationshipMap>();
        foreach (var property in collection.ItemType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                     .Where(property => property.CanRead && property.CanWrite && property.GetIndexParameters().Length == 0)
                     .OrderBy(property => property.Name, MetaName.Comparer)
                     .ThenBy(property => property.Name, StringComparer.Ordinal))
        {
            if (property == idProperty || property.GetCustomAttribute<XmlIgnoreAttribute>() != null)
            {
                continue;
            }

            if (entityTypes.Contains(property.PropertyType))
            {
                var role = property.Name;
                relationships.Add(new RelationshipMap(
                    property,
                    role,
                    role + "Id",
                    entityNameByType[property.PropertyType],
                    IsNullableProperty(property)));
            }
            else if (property.PropertyType == typeof(string))
            {
                var element = property.GetCustomAttribute<XmlElementAttribute>();
                scalars.Add(new ScalarMap(
                    property,
                    string.IsNullOrWhiteSpace(element?.ElementName) ? property.Name : element!.ElementName,
                    IsNullableProperty(property)));
            }
            else
            {
                throw new InvalidOperationException(
                    $"Property '{collection.ItemType.FullName}.{property.Name}' is neither a string scalar nor an entity relationship.");
            }
        }

        return new EntityMap(
            collection,
            collection.ItemType,
            collection.EntityName,
            idProperty,
            scalars.OrderBy(item => item.Name, MetaName.Comparer).ThenBy(item => item.Name, StringComparer.Ordinal).ToArray(),
            relationships.OrderBy(item => item.ColumnName, MetaName.Comparer).ThenBy(item => item.ColumnName, StringComparer.Ordinal).ToArray());
    }

    private static Dictionary<string, Dictionary<string, object>> BuildIndexes<TModel>(
        TModel model,
        ModelMap map)
        where TModel : class
    {
        var indexes = new Dictionary<string, Dictionary<string, object>>(MetaName.Comparer);
        foreach (var entity in map.Entities)
        {
            var rowsById = new Dictionary<string, object>(MetaIdentity.Comparer);
            foreach (var row in entity.Collection.GetList(model))
            {
                if (row == null)
                {
                    throw new InvalidOperationException($"Entity '{entity.Name}' contains a null row.");
                }

                var id = GetRequiredId(entity, row, $"Entity '{entity.Name}' contains a row with empty Id.");
                if (!rowsById.TryAdd(id, row))
                {
                    throw new InvalidOperationException($"Entity '{entity.Name}' contains duplicate Id '{id}'.");
                }

                foreach (var scalar in entity.Scalars.Where(scalar => !scalar.IsNullable))
                {
                    if (string.IsNullOrWhiteSpace(scalar.Property.GetValue(row) as string))
                    {
                        throw new InvalidOperationException(
                            $"Entity '{entity.Name}' row '{id}' is missing required property '{scalar.Property.Name}'.");
                    }
                }
            }

            indexes.Add(entity.Name, rowsById);
        }

        foreach (var entity in map.Entities)
        {
            foreach (var row in entity.Collection.GetList(model).Cast<object>())
            {
                foreach (var relationship in entity.Relationships)
                {
                    var target = relationship.Property.GetValue(row);
                    if (target == null)
                    {
                        if (!relationship.IsNullable)
                        {
                            throw new InvalidOperationException(
                                $"Relationship '{entity.Name}.{relationship.ColumnName}' on row '{GetId(entity, row)}' is empty.");
                        }

                        continue;
                    }

                    var targetEntity = map.EntitiesByName[relationship.TargetEntityName];
                    var targetId = GetRequiredId(targetEntity, target,
                        $"Relationship '{entity.Name}.{relationship.ColumnName}' references a target with empty Id.");
                    if (!indexes[relationship.TargetEntityName].TryGetValue(targetId, out var canonical) ||
                        !ReferenceEquals(canonical, target))
                    {
                        throw new InvalidOperationException(
                            $"Relationship '{entity.Name}.{relationship.ColumnName}' on row '{GetId(entity, row)}' does not reference its canonical target.");
                    }
                }
            }
        }

        return indexes;
    }

    private static GenericModel BuildGenericModel(ModelMap map)
    {
        var model = new GenericModel { Name = map.ModelName };
        foreach (var entityMap in map.Entities)
        {
            var entity = new GenericEntity { Name = entityMap.Name };
            foreach (var scalar in entityMap.Scalars)
            {
                entity.Properties.Add(new GenericProperty { Name = scalar.Name, IsNullable = scalar.IsNullable });
            }

            foreach (var relationship in entityMap.Relationships)
            {
                entity.Relationships.Add(new GenericRelationship
                {
                    Entity = relationship.TargetEntityName,
                    Role = string.Equals(relationship.Role, relationship.TargetEntityName, StringComparison.Ordinal)
                        ? string.Empty
                        : relationship.Role,
                    IsNullable = relationship.IsNullable,
                });
            }

            model.Entities.Add(entity);
        }

        return model;
    }

    private static void EnsureMatchingModel(GenericModel actual, ModelMap map)
    {
        var expected = new InMemoryWorkspace(
            BuildGenericModel(map),
            new GenericInstance { ModelName = map.ModelName });
        var observed = new InMemoryWorkspace(
            actual,
            new GenericInstance { ModelName = actual.Name });
        var difference = InMemoryWorkspaceComparer.FindDifference(expected, observed);
        if (difference != null)
        {
            throw new InvalidOperationException(
                $"Typed model '{map.ModelName}' does not match the workspace model. {difference}");
        }
    }

    private static void EnsureValid(InMemoryWorkspace workspace, string message)
    {
        var diagnostics = WorkspaceValidator.Validate(workspace.Model, workspace.Instance);
        if (!diagnostics.HasErrors)
        {
            return;
        }

        var errors = diagnostics.Issues
            .Where(issue => issue.Severity == IssueSeverity.Error)
            .Take(5)
            .Select(issue => $"{issue.Code} {issue.Location} - {issue.Message}");
        throw new InvalidOperationException(message + " " + string.Join(" | ", errors));
    }

    private static bool IsNullableProperty(PropertyInfo property)
    {
        if (Nullable.GetUnderlyingType(property.PropertyType) != null)
        {
            return true;
        }

        if (property.PropertyType.IsValueType)
        {
            return false;
        }

        var nullability = new NullabilityInfoContext().Create(property);
        return nullability.WriteState == NullabilityState.Nullable ||
               nullability.ReadState == NullabilityState.Nullable;
    }

    private static string InferModelName(Type modelType) =>
        modelType.Name.EndsWith("Model", StringComparison.Ordinal) && modelType.Name.Length > "Model".Length
            ? modelType.Name[..^"Model".Length]
            : modelType.Name;

    private static string GetId(EntityMap entity, object row) =>
        entity.IdProperty.GetValue(row) as string ?? string.Empty;

    private static string GetRequiredId(EntityMap entity, object row, string message)
    {
        var id = GetId(entity, row);
        if (!MetaIdentity.TryValidate(id, out var error))
        {
            throw new InvalidOperationException($"{message} {error}");
        }

        return id;
    }

    private sealed record ModelMap(
        string ModelName,
        IReadOnlyList<EntityMap> Entities,
        IReadOnlyDictionary<string, EntityMap> EntitiesByName);

    private sealed record EntityMap(
        EntityCollection Collection,
        Type ItemType,
        string Name,
        PropertyInfo IdProperty,
        IReadOnlyList<ScalarMap> Scalars,
        IReadOnlyList<RelationshipMap> Relationships);

    private sealed record ScalarMap(PropertyInfo Property, string Name, bool IsNullable);

    private sealed record RelationshipMap(
        PropertyInfo Property,
        string Role,
        string ColumnName,
        string TargetEntityName,
        bool IsNullable);

    private sealed record PendingRelationship(EntityMap Entity, object Target, GenericRecord Source);

    private sealed class EntityCollection
    {
        private EntityCollection(PropertyInfo property, Type itemType, string entityName)
        {
            Property = property;
            ItemType = itemType;
            EntityName = entityName;
        }

        public PropertyInfo Property { get; }
        public Type ItemType { get; }
        public string EntityName { get; }

        public static EntityCollection? TryCreate(Type modelType, PropertyInfo property)
        {
            var array = property.GetCustomAttribute<XmlArrayAttribute>();
            var item = property.GetCustomAttribute<XmlArrayItemAttribute>();
            if (!property.PropertyType.IsGenericType ||
                property.PropertyType.GetGenericTypeDefinition() != typeof(List<>))
            {
                if (array != null || item != null)
                {
                    throw new InvalidOperationException(
                        $"Model type '{modelType.FullName}' property '{property.Name}' must be List<T> when marked with collection attributes.");
                }

                return null;
            }

            var itemType = property.PropertyType.GetGenericArguments()[0];
            var entityName = string.IsNullOrWhiteSpace(item?.ElementName) ? itemType.Name : item!.ElementName;
            return new EntityCollection(property, itemType, entityName);
        }

        public IList GetList(object owner)
        {
            if (Property.GetValue(owner) is IList list)
            {
                return list;
            }

            var created = (IList?)Activator.CreateInstance(Property.PropertyType) ??
                throw new InvalidOperationException(
                    $"Could not create list instance for '{Property.DeclaringType?.FullName}.{Property.Name}'.");
            Property.SetValue(owner, created);
            return created;
        }
    }
}
