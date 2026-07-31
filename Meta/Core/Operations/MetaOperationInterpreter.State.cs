using Meta.Core.Domain;
using Meta.Core.Services;

namespace Meta.Core.Operations;

public sealed partial class MetaOperationInterpreter
{
    private void EnsureConforming(
        GenericMetadataState state,
        string message)
    {
        if (!string.Equals(
                state.Model.Name,
                state.Instance.ModelName,
                StringComparison.Ordinal))
        {
            throw new MetaOperationException(
                $"{message} Model '{state.Model.Name}' and instance '{state.Instance.ModelName}' do not match.");
        }

        EnsureNoUnknownInstanceMembers(state, message);

        var diagnostics = _validationService.Validate(new Workspace
        {
            Model = state.Model,
            Instance = state.Instance,
        });
        if (!diagnostics.HasErrors)
        {
            return;
        }

        var details = diagnostics.Issues
            .Where(issue => issue.Severity == IssueSeverity.Error)
            .Take(5)
            .Select(issue => $"{issue.Code} {issue.Location} - {issue.Message}");
        throw new MetaOperationException(
            $"{message} {string.Join(" | ", details)}",
            diagnostics: diagnostics);
    }

    private static void EnsureNoUnknownInstanceMembers(
        GenericMetadataState state,
        string message)
    {
        foreach (var entityRecords in state.Instance.RecordsByEntity)
        {
            var entity = state.Model.FindEntity(entityRecords.Key);
            if (entity == null)
            {
                throw new MetaOperationException(
                    $"{message} Instance entity '{entityRecords.Key}' is not modeled.");
            }

            var properties = entity.Properties
                .Select(property => property.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var relationships = entity.Relationships
                .Select(relationship => relationship.GetColumnName())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var record in entityRecords.Value)
            {
                var unknownProperty = record.Values.Keys.FirstOrDefault(
                    propertyName => !properties.Contains(propertyName));
                if (unknownProperty != null)
                {
                    throw new MetaOperationException(
                        $"{message} Record '{entity.Name}.{record.Id}' contains unknown property '{unknownProperty}'.");
                }

                var unknownRelationship = record.RelationshipIds.Keys.FirstOrDefault(
                    relationshipName => !relationships.Contains(relationshipName));
                if (unknownRelationship != null)
                {
                    throw new MetaOperationException(
                        $"{message} Record '{entity.Name}.{record.Id}' contains unknown relationship '{unknownRelationship}'.");
                }
            }
        }
    }

    private static GenericEntity RequireEntity(
        GenericModel model,
        string entityName)
    {
        var name = RequireName(entityName, nameof(entityName));
        return model.FindEntity(name)
               ?? throw new InvalidOperationException($"Entity '{name}' does not exist.");
    }

    private static GenericProperty RequireProperty(
        GenericEntity entity,
        string propertyName)
    {
        var name = RequireName(propertyName, nameof(propertyName));
        return entity.Properties.FirstOrDefault(property =>
                   string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
               ?? throw new InvalidOperationException(
                   $"Property '{entity.Name}.{name}' does not exist.");
    }

    private static void EnsureMemberNameAvailable(
        GenericEntity entity,
        string memberName)
    {
        if (string.Equals(memberName, "Id", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Member name 'Id' is reserved for the implicit identity.");
        }

        if (entity.Properties.Any(property =>
                string.Equals(
                    property.Name,
                    memberName,
                    StringComparison.OrdinalIgnoreCase)) ||
            entity.Relationships.Any(relationship =>
                string.Equals(
                    relationship.GetColumnName(),
                    memberName,
                    StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                $"Member '{entity.Name}.{memberName}' already exists.");
        }
    }

    private static GenericRecord RequireRecord(
        GenericInstance instance,
        GenericEntity entity,
        string id)
    {
        var requiredId = RequireIdentity(id, nameof(id));
        var records = instance.GetOrCreateEntityRecords(entity.Name);
        return FindRecord(records, requiredId)
               ?? throw new InvalidOperationException(
                   $"Entity '{entity.Name}' does not contain record '{requiredId}'.");
    }

    private static GenericRecord? FindRecord(
        IEnumerable<GenericRecord> records,
        string id)
    {
        return records.FirstOrDefault(record =>
            string.Equals(record.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    private static GenericRelationship ResolveRelationship(
        GenericEntity entity,
        string selector)
    {
        var name = RequireName(selector, nameof(selector));
        var matches = entity.Relationships
            .Where(relationship =>
                string.Equals(
                    relationship.GetRoleOrDefault(),
                    name,
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    relationship.GetColumnName(),
                    name,
                    StringComparison.OrdinalIgnoreCase))
            .Distinct()
            .ToList();

        if (matches.Count == 1)
        {
            return matches[0];
        }

        if (matches.Count > 1)
        {
            throw new InvalidOperationException(
                $"Relationship '{entity.Name}.{name}' is ambiguous.");
        }

        throw new InvalidOperationException(
            $"Relationship '{entity.Name}.{name}' does not exist.");
    }

    private static string ResolveTargetId(
        GenericMetadataState state,
        GenericRelationship relationship,
        string targetId)
    {
        var id = RequireIdentity(targetId, nameof(targetId));
        var targetEntity = RequireEntity(state.Model, relationship.Entity);
        var targetRecords = state.Instance.GetOrCreateEntityRecords(targetEntity.Name);
        var target = FindRecord(targetRecords, id)
                     ?? throw new InvalidOperationException(
                         $"Relationship target '{targetEntity.Name}.{id}' does not exist.");
        return target.Id;
    }

    private static string RequireName(
        string value,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"'{parameterName}' is required.");
        }

        if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"'{parameterName}' cannot contain leading or trailing whitespace.");
        }

        return value;
    }

    private static string RequireOptionalName(
        string value,
        string parameterName)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return RequireName(value, parameterName);
    }

    private static string RequireIdentity(
        string value,
        string parameterName)
    {
        return RequireName(value, parameterName);
    }

    private static string RequireText(
        string value,
        string parameterName)
    {
        return value ?? throw new InvalidOperationException($"'{parameterName}' cannot be null.");
    }
}
