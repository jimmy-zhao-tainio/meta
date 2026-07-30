using Meta.Core.Domain;

namespace Meta.Core.Operations;

public sealed partial class MetaOperationInterpreter
{
    private static void ApplyInsertRecord(
        GenericMetadataState state,
        InsertRecordOperation operation)
    {
        var entity = RequireEntity(state.Model, operation.EntityName);
        var id = RequireIdentity(operation.Id, nameof(operation.Id));
        var records = state.Instance.GetOrCreateEntityRecords(entity.Name);
        if (FindRecord(records, id) != null)
        {
            throw new InvalidOperationException(
                $"Entity '{entity.Name}' already contains record '{id}'.");
        }

        var record = new GenericRecord
        {
            Id = id,
        };

        foreach (var value in operation.Values)
        {
            var property = RequireProperty(entity, value.Key);
            if (string.Equals(property.Name, "Id", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Property 'Id' is supplied separately.");
            }

            record.Values.Add(property.Name, RequireText(value.Value, value.Key));
        }

        foreach (var relationshipValue in operation.RelationshipIds)
        {
            var relationship = ResolveRelationship(entity, relationshipValue.Key);
            var targetId = ResolveTargetId(
                state,
                relationship,
                relationshipValue.Value);
            record.RelationshipIds.Add(relationship.GetColumnName(), targetId);
        }

        records.Add(record);
    }

    private static void ApplySetProperty(
        GenericMetadataState state,
        SetPropertyOperation operation)
    {
        var entity = RequireEntity(state.Model, operation.EntityName);
        var property = RequireProperty(entity, operation.PropertyName);
        var record = RequireRecord(state.Instance, entity, operation.Id);
        record.Values[property.Name] = RequireText(operation.Value, nameof(operation.Value));
    }

    private static void ApplyClearProperty(
        GenericMetadataState state,
        ClearPropertyOperation operation)
    {
        var entity = RequireEntity(state.Model, operation.EntityName);
        var property = RequireProperty(entity, operation.PropertyName);
        if (!property.IsNullable)
        {
            throw new InvalidOperationException(
                $"Required property '{entity.Name}.{property.Name}' cannot be cleared.");
        }

        var record = RequireRecord(state.Instance, entity, operation.Id);
        record.Values.Remove(property.Name);
    }

    private static void ApplySetRelationship(
        GenericMetadataState state,
        SetRelationshipOperation operation)
    {
        var entity = RequireEntity(state.Model, operation.EntityName);
        var relationship = ResolveRelationship(entity, operation.RelationshipName);
        var record = RequireRecord(state.Instance, entity, operation.Id);
        var targetId = ResolveTargetId(state, relationship, operation.TargetId);
        record.RelationshipIds[relationship.GetColumnName()] = targetId;
    }

    private static void ApplyClearRelationship(
        GenericMetadataState state,
        ClearRelationshipOperation operation)
    {
        var entity = RequireEntity(state.Model, operation.EntityName);
        var relationship = ResolveRelationship(entity, operation.RelationshipName);
        if (!relationship.IsNullable)
        {
            throw new InvalidOperationException(
                $"Required relationship '{entity.Name}.{relationship.GetColumnName()}' cannot be cleared.");
        }

        var record = RequireRecord(state.Instance, entity, operation.Id);
        record.RelationshipIds.Remove(relationship.GetColumnName());
    }

    private static void ApplyDeleteRecord(
        GenericMetadataState state,
        DeleteRecordOperation operation)
    {
        var entity = RequireEntity(state.Model, operation.EntityName);
        var records = state.Instance.GetOrCreateEntityRecords(entity.Name);
        var record = FindRecord(records, RequireIdentity(operation.Id, nameof(operation.Id)))
                     ?? throw new InvalidOperationException(
                         $"Entity '{entity.Name}' does not contain record '{operation.Id}'.");
        records.Remove(record);
    }
}
