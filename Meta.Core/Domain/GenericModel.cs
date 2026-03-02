using System;
using System.Collections.Generic;
using System.Linq;

namespace Meta.Core.Domain;

public sealed class GenericModel
{
    public string Name { get; set; } = string.Empty;
    public List<GenericEntity> Entities { get; } = new();

    public GenericEntity? FindEntity(string entityName)
    {
        return Entities.Find(entity => string.Equals(entity.Name, entityName, StringComparison.OrdinalIgnoreCase));
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
