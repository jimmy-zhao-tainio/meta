using System.Xml.Linq;
using Meta.Core.Domain;

namespace Meta.Core.Serialization;

public static class ModelXmlCodec
{
    public static GenericModel LoadFromPath(string modelPath)
    {
        if (string.IsNullOrWhiteSpace(modelPath))
        {
            throw new ArgumentException("Model path is required.", nameof(modelPath));
        }

        var document = XDocument.Load(modelPath, LoadOptions.None);
        return Load(document);
    }

    public static GenericModel Load(XDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var root = document.Root ?? throw new InvalidDataException("Model XML has no root element.");
        if (!string.Equals(root.Name.LocalName, "Model", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Model XML root must be <Model>.");
        }

        var model = new GenericModel
        {
            Name = (string?)root.Attribute("name") ?? string.Empty,
        };

        var entitiesElement = root.Element("Entities");
        if (entitiesElement == null)
        {
            return model;
        }

        foreach (var entityElement in entitiesElement.Elements("Entity"))
        {
            var displayKeyAttribute = entityElement.Attribute("displayKey");
            if (displayKeyAttribute != null)
            {
                throw new InvalidDataException(
                    $"Model entity '{(string?)entityElement.Attribute("name") ?? string.Empty}' uses unsupported attribute 'displayKey'.");
            }

            var entity = new GenericEntity
            {
                Name = (string?)entityElement.Attribute("name") ?? string.Empty,
            };
            if (entityElement.Attribute("plural") != null)
            {
                throw new InvalidDataException(
                    $"Model entity '{entity.Name}' uses unsupported attribute 'plural'. Use singular entity/list naming only.");
            }

            var propertiesElement = entityElement.Element("Properties");
            if (propertiesElement != null)
            {
                foreach (var propertyElement in propertiesElement.Elements("Property"))
                {
                    var propertyName = ((string?)propertyElement.Attribute("name") ?? string.Empty).Trim();
                    if (string.Equals(propertyName, "Id", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException(
                            $"Entity '{entity.Name}' must not define explicit property 'Id'. It is implicit.");
                    }

                    if (propertyElement.Attribute("isNullable") != null)
                    {
                        throw new InvalidDataException(
                            $"Property '{entity.Name}.{propertyName}' uses unsupported attribute 'isNullable'. Use 'isRequired'.");
                    }

                    var property = new GenericProperty
                    {
                        Name = propertyName,
                        DataType = ParseDataType((string?)propertyElement.Attribute("dataType")),
                        IsNullable = !ParseRequired((string?)propertyElement.Attribute("isRequired")),
                    };
                    entity.Properties.Add(property);
                }
            }

            var relationshipsElement = entityElement.Element("Relationships");
            if (relationshipsElement != null)
            {
                foreach (var relationshipElement in relationshipsElement.Elements("Relationship"))
                {
                    if (relationshipElement.Attribute("name") != null)
                    {
                        throw new InvalidDataException(
                            $"Entity '{entity.Name}' relationship to '{((string?)relationshipElement.Attribute("entity") ?? string.Empty).Trim()}' uses unsupported attribute 'name'. Use 'role'.");
                    }

                    if (relationshipElement.Attribute("column") != null)
                    {
                        throw new InvalidDataException(
                            $"Entity '{entity.Name}' relationship to '{((string?)relationshipElement.Attribute("entity") ?? string.Empty).Trim()}' uses unsupported attribute 'column'. Use 'role'.");
                    }

                    entity.Relationships.Add(new GenericRelationship
                    {
                        Entity = ((string?)relationshipElement.Attribute("entity") ?? string.Empty).Trim(),
                        Role = ((string?)relationshipElement.Attribute("role") ?? string.Empty).Trim(),
                    });
                }
            }

            model.Entities.Add(entity);
        }

        return model;
    }

    public static XDocument BuildDocument(GenericModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var root = new XElement("Model", new XAttribute("name", model.Name ?? string.Empty));
        var entitiesElement = new XElement("Entities");
        root.Add(entitiesElement);

        foreach (var entity in model.Entities.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
        {
            var entityElement = new XElement("Entity", new XAttribute("name", entity.Name ?? string.Empty));

            var nonIdProperties = entity.Properties
                .OrderBy(item => string.Equals(item.Name, "Id", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .Where(item => !string.Equals(item.Name, "Id", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (nonIdProperties.Count > 0)
            {
                var propertiesElement = new XElement("Properties");
                foreach (var property in nonIdProperties)
                {
                    var propertyElement = new XElement("Property",
                        new XAttribute("name", property.Name ?? string.Empty));
                    var dataType = string.IsNullOrWhiteSpace(property.DataType) ? "string" : property.DataType;
                    if (!string.Equals(dataType, "string", StringComparison.OrdinalIgnoreCase))
                    {
                        propertyElement.Add(new XAttribute("dataType", dataType));
                    }

                    if (property.IsNullable)
                    {
                        propertyElement.Add(new XAttribute("isRequired", "false"));
                    }

                    propertiesElement.Add(propertyElement);
                }

                entityElement.Add(propertiesElement);
            }

            if (entity.Relationships.Count > 0)
            {
                var relationshipsElement = new XElement("Relationships");
                foreach (var relationship in entity.Relationships
                             .OrderBy(item => item.GetColumnName(), StringComparer.OrdinalIgnoreCase)
                             .ThenBy(item => item.Entity, StringComparer.OrdinalIgnoreCase))
                {
                    var relationshipElement = new XElement("Relationship",
                        new XAttribute("entity", relationship.Entity ?? string.Empty));
                    var defaultRole = relationship.Entity ?? string.Empty;
                    var role = relationship.GetRoleOrDefault();
                    if (!string.Equals(role, defaultRole, StringComparison.Ordinal))
                    {
                        relationshipElement.Add(new XAttribute("role", role));
                    }

                    relationshipsElement.Add(relationshipElement);
                }

                entityElement.Add(relationshipsElement);
            }

            entitiesElement.Add(entityElement);
        }

        return new XDocument(new XDeclaration("1.0", "utf-8", null), root);
    }

    private static string ParseDataType(string? dataTypeValue)
    {
        if (string.IsNullOrWhiteSpace(dataTypeValue))
        {
            return "string";
        }

        var trimmed = dataTypeValue.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? "string" : trimmed;
    }

    private static bool ParseRequired(string? isRequiredValue)
    {
        if (string.IsNullOrWhiteSpace(isRequiredValue))
        {
            return true;
        }

        if (bool.TryParse(isRequiredValue, out var parsed))
        {
            return parsed;
        }

        throw new InvalidDataException($"Invalid boolean value '{isRequiredValue}' for attribute 'isRequired'.");
    }
}


