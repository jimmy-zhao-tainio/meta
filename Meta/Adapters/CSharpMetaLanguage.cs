using Meta.Core.Operations;
using Microsoft.CodeAnalysis.CSharp;

namespace Meta.Adapters;

internal static class CSharpMetaLanguage
{
    public static void RequireRepresentable(
        GenericMetadataState state)
    {
        RequireIdentifier(state.Model.Name, "model");
        var entityNames = state.Model.Entities
            .Select(entity => entity.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var rootTypeName = ResolveRootTypeName(
            state.Model.Name,
            entityNames);
        var rootMemberNames = state.Model.Entities
            .Select(entity => entity.GetListName())
            .Append("BuiltIn")
            .ToHashSet(StringComparer.Ordinal);
        if (rootMemberNames.Contains(rootTypeName))
        {
            throw new NotSupportedException(
                $"C# workspace root type '{rootTypeName}' conflicts with a generated root member.");
        }

        foreach (var generatedTypeName in new[]
                 {
                     rootTypeName,
                     rootTypeName + "Instance",
                     rootTypeName + "InstanceFactory",
                 })
        {
            if (entityNames.Contains(generatedTypeName))
            {
                throw new NotSupportedException(
                    $"C# workspace generated type '{generatedTypeName}' conflicts with an entity name.");
            }
        }

        foreach (var entity in state.Model.Entities)
        {
            RequireIdentifier(entity.Name, "entity");
            RequireIdentifier(entity.GetListName(), "entity collection");
            var memberNames = new HashSet<string>(
                StringComparer.Ordinal)
            {
                "Id",
            };
            foreach (var property in entity.Properties)
            {
                RequireIdentifier(
                    property.Name,
                    $"property '{entity.Name}'");
                RequireMemberName(
                    memberNames,
                    entity.Name,
                    property.Name);
            }

            foreach (var relationship in entity.Relationships)
            {
                var navigationName = relationship.GetNavigationName();
                RequireIdentifier(
                    navigationName,
                    $"relationship '{entity.Name}'");
                RequireMemberName(
                    memberNames,
                    entity.Name,
                    navigationName);
            }
        }
    }

    private static void RequireMemberName(
        ISet<string> memberNames,
        string entityName,
        string memberName)
    {
        if (string.Equals(
                entityName,
                memberName,
                StringComparison.Ordinal) ||
            !memberNames.Add(memberName))
        {
            throw new NotSupportedException(
                $"C# workspace member '{entityName}.{memberName}' conflicts with another C# declaration.");
        }
    }

    private static string ResolveRootTypeName(
        string modelName,
        IReadOnlySet<string> entityNames)
    {
        if (!entityNames.Contains(modelName))
        {
            return modelName;
        }

        var candidate = modelName + "Model";
        var suffix = 2;
        while (entityNames.Contains(candidate))
        {
            candidate = modelName + "Model" + suffix;
            suffix++;
        }

        return candidate;
    }

    private static void RequireIdentifier(
        string value,
        string subject)
    {
        if (!SyntaxFacts.IsValidIdentifier(value) ||
            SyntaxFacts.GetKeywordKind(value) != SyntaxKind.None ||
            SyntaxFacts.GetContextualKeywordKind(value) != SyntaxKind.None)
        {
            throw new NotSupportedException(
                $"C# workspace cannot represent {subject} name '{value}' in its current source contract.");
        }
    }
}
