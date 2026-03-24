using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Meta.Core.Domain;

public sealed class GenericModel
{
    public string Name { get; set; } = string.Empty;
    public List<GenericEntity> Entities { get; } = new();

    public GenericEntity? FindEntity(string entityName)
    {
        return Entities.Find(entity => string.Equals(entity.Name, entityName, StringComparison.OrdinalIgnoreCase));
    }

    public string ComputeContractSignature()
    {
        var builder = new StringBuilder();
        AppendCanonicalLine(builder, "model", Name);

        foreach (var entity in Entities.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            AppendCanonicalLine(builder, "entity", entity.Name, entity.GetListName());

            foreach (var property in entity.Properties.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
            {
                AppendCanonicalLine(
                    builder,
                    "property",
                    entity.Name,
                    property.Name,
                    property.DataType,
                    property.IsNullable ? "nullable" : "required");
            }

            foreach (var relationship in entity.Relationships
                         .OrderBy(item => item.GetColumnName(), StringComparer.OrdinalIgnoreCase)
                         .ThenBy(item => item.Entity, StringComparer.OrdinalIgnoreCase))
            {
                AppendCanonicalLine(
                    builder,
                    "relationship",
                    entity.Name,
                    relationship.Entity,
                    relationship.GetColumnName());
            }
        }

        return Convert.ToHexString(Encoding.UTF8.GetBytes(builder.ToString())).ToLowerInvariant();
    }

    public GenericModel Clone()
    {
        var clone = new GenericModel
        {
            Name = Name ?? string.Empty,
        };

        foreach (var entity in Entities)
        {
            var entityClone = new GenericEntity
            {
                Name = entity.Name ?? string.Empty,
            };

            foreach (var property in entity.Properties)
            {
                entityClone.Properties.Add(new GenericProperty
                {
                    Name = property.Name ?? string.Empty,
                    DataType = property.DataType ?? string.Empty,
                    IsNullable = property.IsNullable,
                });
            }

            foreach (var relationship in entity.Relationships)
            {
                entityClone.Relationships.Add(new GenericRelationship
                {
                    Entity = relationship.Entity ?? string.Empty,
                    Role = relationship.Role ?? string.Empty,
                });
            }

            clone.Entities.Add(entityClone);
        }

        return clone;
    }

    private static void AppendCanonicalLine(StringBuilder builder, params string?[] parts)
    {
        if (builder.Length > 0)
        {
            builder.Append('\n');
        }

        for (var i = 0; i < parts.Length; i++)
        {
            if (i > 0)
            {
                builder.Append('|');
            }

            builder.Append(EscapeCanonicalPart(parts[i]));
        }
    }

    private static string EscapeCanonicalPart(string? value)
    {
        return (value ?? string.Empty)
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
    }
}

public sealed class GenericEntity
{
    public string Name { get; set; } = string.Empty;
    public List<GenericProperty> Properties { get; } = new();
    public List<GenericRelationship> Relationships { get; } = new();

    public string GetListName()
    {
        return Name + "List";
    }

    public GenericRelationship? FindRelationshipByRole(string role)
    {
        if (string.IsNullOrWhiteSpace(role))
        {
            return null;
        }

        return Relationships.FirstOrDefault(relationship =>
            string.Equals(relationship.GetRoleOrDefault(), role, StringComparison.OrdinalIgnoreCase));
    }

    public GenericRelationship? FindRelationshipByColumnName(string columnName)
    {
        if (string.IsNullOrWhiteSpace(columnName))
        {
            return null;
        }

        return Relationships.FirstOrDefault(relationship =>
            string.Equals(relationship.GetColumnName(), columnName, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class GenericProperty
{
    public string Name { get; set; } = string.Empty;
    public string DataType { get; set; } = "string";
    public bool IsNullable { get; set; }
}

public sealed class GenericRelationship
{
    public string Entity { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;

    public string GetRoleOrDefault()
    {
        return string.IsNullOrWhiteSpace(Role) ? Entity : Role;
    }

    public string GetColumnName()
    {
        return GetRoleOrDefault() + "Id";
    }

    public string GetNavigationName()
    {
        return GetRoleOrDefault();
    }
}
