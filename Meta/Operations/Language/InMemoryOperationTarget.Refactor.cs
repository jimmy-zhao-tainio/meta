namespace Meta.Operations;

internal sealed partial class InMemoryOperationTarget
{
    internal PropertyToRelationshipResult Apply(
        InMemoryWorkspace state,
        Operation.PropertyToRelationship operation)
    {
        var sourceEntity = RequireEntity(state, operation.SourceEntityName);
        var targetEntity = RequireEntity(state, operation.TargetEntityName);
        var sourceProperty = RequireProperty(
            sourceEntity,
            operation.SourcePropertyName);
        var usesTargetId = MetaName.Comparer.Equals(
            operation.LookupPropertyName,
            "Id");
        var targetProperty = usesTargetId
            ? null
            : RequireProperty(targetEntity, operation.LookupPropertyName);
        var candidate = new GenericRelationship
        {
            Entity = targetEntity.Name,
            Role = operation.Role,
            IsNullable = sourceProperty.IsNullable,
        };
        var relationshipName = candidate.GetColumnName();
        var existingRelationship = sourceEntity.Relationships.FirstOrDefault(
            relationship =>
                MetaName.Comparer.Equals(
                    relationship.Entity,
                    targetEntity.Name) &&
                MetaName.Comparer.Equals(
                    relationship.GetRoleOrDefault(),
                    candidate.GetRoleOrDefault()));

        if (existingRelationship == null)
        {
            EnsureRelationshipNameAvailable(
                sourceEntity,
                candidate,
                replacedProperty: operation.PreserveProperty
                    ? null
                    : sourceProperty);
        }

        var lookupComparer = usesTargetId
            ? MetaIdentity.Comparer
            : StringComparer.Ordinal;
        var targetLookup = new Dictionary<string, string>(lookupComparer);
        foreach (var targetRecord in GetRecords(state, targetEntity.Name))
        {
            var lookupValue = usesTargetId
                ? targetRecord.Id
                : targetRecord.Values.TryGetValue(
                    targetProperty!.Name,
                    out var value)
                    ? value
                    : null;
            if (string.IsNullOrEmpty(lookupValue))
            {
                throw new InvalidOperationException(
                    $"Target lookup '{targetEntity.Name}.{operation.LookupPropertyName}' contains a missing or empty value at record '{targetRecord.Id}'.");
            }

            if (!targetLookup.TryAdd(lookupValue, targetRecord.Id))
            {
                throw new InvalidOperationException(
                    $"Target lookup '{targetEntity.Name}.{operation.LookupPropertyName}' contains duplicate value '{lookupValue}'.");
            }
        }

        var sourceRecords = GetRecords(state, sourceEntity.Name);
        var resolved = new List<(GenericRecord Record, string? TargetId)>(
            sourceRecords.Count);
        var comparableSourceCount = 0;
        foreach (var sourceRecord in sourceRecords)
        {
            var sourceValue = sourceRecord.Values.TryGetValue(
                sourceProperty.Name,
                out var value)
                ? value
                : null;
            if (string.IsNullOrEmpty(sourceValue))
            {
                if (!sourceProperty.IsNullable)
                {
                    throw new InvalidOperationException(
                        $"Required property '{sourceEntity.Name}.{sourceProperty.Name}' is missing or empty at record '{sourceRecord.Id}'.");
                }

                resolved.Add((sourceRecord, null));
                continue;
            }

            comparableSourceCount++;
            if (!targetLookup.TryGetValue(sourceValue, out var targetId))
            {
                throw new InvalidOperationException(
                    $"Value '{sourceValue}' from '{sourceEntity.Name}.{sourceProperty.Name}' does not resolve through '{targetEntity.Name}.{operation.LookupPropertyName}'.");
            }

            if (existingRelationship != null &&
                sourceRecord.RelationshipIds.TryGetValue(
                    relationshipName,
                    out var existingTargetId) &&
                !MetaIdentity.Comparer.Equals(existingTargetId, targetId))
            {
                throw new InvalidOperationException(
                    $"Existing relationship '{sourceEntity.Name}.{relationshipName}' conflicts at record '{sourceRecord.Id}'.");
            }

            resolved.Add((sourceRecord, targetId));
        }

        foreach (var item in resolved)
        {
            if (item.TargetId != null)
            {
                item.Record.RelationshipIds[relationshipName] = item.TargetId;
            }

            if (!operation.PreserveProperty)
            {
                item.Record.Values.Remove(sourceProperty.Name);
            }
        }

        if (existingRelationship == null)
        {
            sourceEntity.Relationships.Add(candidate);
        }
        else
        {
            existingRelationship.IsNullable = sourceProperty.IsNullable;
        }

        if (!operation.PreserveProperty)
        {
            sourceEntity.Properties.Remove(sourceProperty);
        }

        return new PropertyToRelationshipResult(
            sourceRecords.Count,
            comparableSourceCount,
            PropertyRemoved: !operation.PreserveProperty,
            relationshipName);
    }

    internal RelationshipToPropertyResult Apply(
        InMemoryWorkspace state,
        Operation.RelationshipToProperty operation)
    {
        var sourceEntity = RequireEntity(state, operation.SourceEntityName);
        var targetEntity = RequireEntity(state, operation.TargetEntityName);
        var expectedRole = string.IsNullOrEmpty(operation.Role)
            ? targetEntity.Name
            : operation.Role;
        var relationship = sourceEntity.Relationships.FirstOrDefault(item =>
            MetaName.Comparer.Equals(item.Entity, targetEntity.Name) &&
            MetaName.Comparer.Equals(item.GetRoleOrDefault(), expectedRole)) ??
            throw new InvalidOperationException(
                $"Relationship '{sourceEntity.Name}->{targetEntity.Name}' does not exist.");
        var relationshipName = relationship.GetColumnName();
        var propertyName = string.IsNullOrEmpty(operation.PropertyName)
            ? relationshipName
            : operation.PropertyName;
        var conflictingProperty = sourceEntity.Properties.Any(property =>
            MetaName.Comparer.Equals(property.Name, propertyName));
        var conflictingRelationship = sourceEntity.Relationships.Any(item =>
            !ReferenceEquals(item, relationship) &&
            (MetaName.Comparer.Equals(item.GetColumnName(), propertyName) ||
             MetaName.Comparer.Equals(item.GetNavigationName(), propertyName)));
        if (conflictingProperty || conflictingRelationship)
        {
            throw new InvalidOperationException(
                $"Property '{sourceEntity.Name}.{propertyName}' already exists.");
        }

        var sourceRecords = GetRecords(state, sourceEntity.Name);
        var propertyValueCount = 0;
        foreach (var sourceRecord in sourceRecords)
        {
            if (!sourceRecord.RelationshipIds.TryGetValue(
                    relationshipName,
                    out var targetId))
            {
                if (!relationship.IsNullable)
                {
                    throw new InvalidOperationException(
                        $"Required relationship '{sourceEntity.Name}.{relationshipName}' is missing at record '{sourceRecord.Id}'.");
                }

                continue;
            }

            sourceRecord.Values[propertyName] = targetId;
            sourceRecord.RelationshipIds.Remove(relationshipName);
            propertyValueCount++;
        }

        sourceEntity.Relationships.Remove(relationship);
        sourceEntity.Properties.Add(new GenericProperty
        {
            Name = propertyName,
            IsNullable = relationship.IsNullable,
        });

        return new RelationshipToPropertyResult(
            sourceRecords.Count,
            propertyValueCount,
            IsRequired: !relationship.IsNullable,
            propertyName);
    }
}
