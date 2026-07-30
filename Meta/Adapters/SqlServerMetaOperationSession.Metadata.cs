using Meta.Core.Ddl;
using Meta.Core.Domain;

namespace Meta.Adapters;

public sealed partial class SqlServerMetaOperationSession
{
    private GenericEntity RequireEntity(string entityName)
    {
        var name = RequireIdentifier(entityName, nameof(entityName));
        return _model.FindEntity(name)
               ?? throw new InvalidOperationException(
                   $"Entity '{name}' does not exist.");
    }

    private static GenericProperty RequireProperty(
        GenericEntity entity,
        string propertyName)
    {
        var name = RequireIdentifier(propertyName, nameof(propertyName));
        return entity.Properties.FirstOrDefault(property =>
                   string.Equals(
                       property.Name,
                       name,
                       StringComparison.OrdinalIgnoreCase))
               ?? throw new InvalidOperationException(
                   $"Property '{entity.Name}.{name}' does not exist.");
    }

    private static GenericRelationship ResolveRelationship(
        GenericEntity entity,
        string selector)
    {
        var name = RequireIdentifier(selector, nameof(selector));
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

        return matches.Count switch
        {
            1 => matches[0],
            > 1 => throw new InvalidOperationException(
                $"Relationship '{entity.Name}.{name}' is ambiguous."),
            _ => throw new InvalidOperationException(
                $"Relationship '{entity.Name}.{name}' does not exist."),
        };
    }

    private static void EnsureMemberNameAvailable(
        GenericEntity entity,
        string memberName)
    {
        if (string.Equals(
                memberName,
                "Id",
                StringComparison.OrdinalIgnoreCase))
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

    private static string RequireIdentifier(
        string value,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"'{parameterName}' is required.");
        }

        if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"'{parameterName}' cannot contain leading or trailing whitespace.");
        }

        SqlServerMetaModelReader.ValidateIdentifier(value, parameterName);
        return value;
    }

    private static string RequireOptionalIdentifier(
        string value,
        string parameterName)
    {
        return string.IsNullOrEmpty(value)
            ? string.Empty
            : RequireIdentifier(value, parameterName);
    }

    private static string RequireIdentity(
        string value,
        string parameterName)
    {
        return MetaSqlStorageContract.RequireRepresentableIdentity(
            value,
            parameterName);
    }
}
