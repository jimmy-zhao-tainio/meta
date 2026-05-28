using System.Text;
using Meta.Core.Domain;

namespace Meta.Core.Presentation.Cli;

public sealed record ModelDrivenAddOptionSpec(
    string OptionName,
    string PropertyName,
    bool Required,
    string ValueLabel = "<value>");

public sealed record ModelDrivenAddRelationshipOptionSpec(
    string OptionName,
    string ColumnName,
    string TargetEntityName,
    bool Required,
    string ValueLabel = "<id>");

public sealed record ModelDrivenAddCommandSpec(
    string CommandName,
    string EntityName,
    IReadOnlyList<ModelDrivenAddOptionSpec> PropertyOptions,
    IReadOnlyList<ModelDrivenAddRelationshipOptionSpec> RelationshipOptions)
{
    public string Description => $"Add a {EntityName} row.";
}

public static class ModelDrivenAddCommandCatalog
{
    public static IReadOnlyDictionary<string, ModelDrivenAddCommandSpec> Build(GenericModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var specs = new List<ModelDrivenAddCommandSpec>();
        foreach (var entity in model.Entities.OrderBy(item => item.Name, StringComparer.Ordinal))
        {
            var propertyOptions = entity.Properties
                .OrderBy(item => item.Name, StringComparer.Ordinal)
                .Select(property => new ModelDrivenAddOptionSpec(
                    "--" + ToKebabCase(property.Name),
                    property.Name,
                    !property.IsNullable && !string.Equals(property.Name, "Ordinal", StringComparison.OrdinalIgnoreCase)))
                .ToArray();

            var relationshipOptions = entity.Relationships
                .OrderBy(item => item.GetColumnName(), StringComparer.Ordinal)
                .Select(relationship => new ModelDrivenAddRelationshipOptionSpec(
                    "--" + ToKebabCase(relationship.GetNavigationName()),
                    relationship.GetColumnName(),
                    relationship.Entity,
                    !relationship.IsNullable))
                .ToArray();

            var optionNames = propertyOptions.Select(item => item.OptionName)
                .Concat(relationshipOptions.Select(item => item.OptionName))
                .GroupBy(item => item, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToArray();
            if (optionNames.Length > 0)
            {
                throw new InvalidOperationException(
                    $"Entity '{entity.Name}' has colliding generated CLI option(s): {string.Join(", ", optionNames)}.");
            }

            specs.Add(new ModelDrivenAddCommandSpec(
                "add-" + ToKebabCase(entity.Name),
                entity.Name,
                propertyOptions,
                relationshipOptions));
        }

        return specs.ToDictionary(spec => spec.CommandName, StringComparer.OrdinalIgnoreCase);
    }

    public static string ToKebabCase(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var builder = new StringBuilder(value.Length + 8);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (char.IsUpper(c))
            {
                if (i > 0 && builder.Length > 0 && builder[^1] != '-')
                {
                    builder.Append('-');
                }

                builder.Append(char.ToLowerInvariant(c));
                continue;
            }

            if (c == '_' || c == ' ')
            {
                if (builder.Length > 0 && builder[^1] != '-')
                {
                    builder.Append('-');
                }

                continue;
            }

            builder.Append(char.ToLowerInvariant(c));
        }

        return builder.ToString();
    }
}
