using System.Collections.Concurrent;
using System.Globalization;
using MetaWeave;

namespace MetaWeaveScript.Sql;

internal sealed partial class MetaWeaveScriptSqlEmitter
{
    private static readonly ConcurrentDictionary<Type, string?> OwnerPropertyByType = new();
    private static readonly ConcurrentDictionary<Type, string?> BasePropertyByType = new();

    private static T GetById<T>(IEnumerable<T> rows, string id, string label) where T : class
    {
        var match = rows.FirstOrDefault(row => string.Equals(GetString(row, "Id"), id, StringComparison.Ordinal));
        return match ?? throw new InvalidOperationException($"Required MetaWeaveScript lookup '{label}' with id '{id}' was not found.");
    }

    private static T GetByBaseId<T>(IEnumerable<T> rows, string baseId, string label) where T : class
    {
        var match = FindByBaseId(rows, baseId);
        return match ?? throw new InvalidOperationException($"Required MetaWeaveScript base lookup '{label}' with base id '{baseId}' was not found.");
    }

    private static T? FindByBaseId<T>(IEnumerable<T> rows, string baseId) where T : class
    {
        var propertyName = ResolveBaseProperty(typeof(T));
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return null;
        }

        return rows.FirstOrDefault(row => string.Equals(GetRelatedId(row, propertyName), baseId, StringComparison.Ordinal));
    }

    private static TLink GetOwnerLink<TLink>(IEnumerable<TLink> links, string ownerId, string label) where TLink : class
    {
        var match = FindOwnerLink(links, ownerId);
        return match ?? throw new InvalidOperationException($"Required MetaWeaveScript link '{label}' with owner id '{ownerId}' was not found.");
    }

    private static TLink? FindOwnerLink<TLink>(IEnumerable<TLink> links, string ownerId) where TLink : class
    {
        var propertyName = ResolveOwnerProperty(typeof(TLink));
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return null;
        }

        return links.FirstOrDefault(row => string.Equals(GetRelatedId(row, propertyName), ownerId, StringComparison.Ordinal));
    }

    private static IEnumerable<TItem> GetOrderedItems<TItem>(IEnumerable<TItem> items, string ownerId) where TItem : class
    {
        var propertyName = ResolveOwnerProperty(typeof(TItem));
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return [];
        }

        return items.Where(row => string.Equals(GetRelatedId(row, propertyName), ownerId, StringComparison.Ordinal))
            .OrderBy(row => ParseOrdinal(GetString(row, "Ordinal")));
    }

    private static string GetString(object target, string propertyName) =>
        (string?)target.GetType().GetProperty(propertyName)?.GetValue(target) ?? string.Empty;

    private static string GetRelatedId(object target, string propertyName)
    {
        var related = target.GetType().GetProperty(propertyName)?.GetValue(target);
        return related is null ? string.Empty : GetString(related, "Id");
    }

    private static int ParseOrdinal(string? ordinal) =>
        int.TryParse(ordinal, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;

    private static string? ResolveOwnerProperty(Type type)
    {
        return OwnerPropertyByType.GetOrAdd(type, static key =>
        {
            var referenceProperties = key.GetProperties()
                .Select(property => property.Name)
                .Where(name => IsReferenceProperty(key, name))
                .ToArray();

            if (referenceProperties.Length == 0)
            {
                return null;
            }

            if (referenceProperties.Length == 1)
            {
                return referenceProperties[0];
            }

            foreach (var candidate in referenceProperties.OrderByDescending(static value => value.Length))
            {
                if (key.Name.StartsWith(candidate, StringComparison.Ordinal))
                {
                    return candidate;
                }
            }

            return referenceProperties[0];
        });
    }

    private static string? ResolveBaseProperty(Type type)
    {
        return BasePropertyByType.GetOrAdd(type, static key =>
        {
            return key.GetProperties()
                .Select(property => property.Name)
                .FirstOrDefault(name => IsReferenceProperty(key, name));
        });
    }

    private static bool IsReferenceProperty(Type ownerType, string propertyName)
    {
        var property = ownerType.GetProperty(propertyName);
        return property is not null &&
               property.PropertyType != typeof(string) &&
               property.PropertyType.GetProperty("Id") is not null;
    }

    private static bool IsTrue(string? value) =>
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    private static string RenderIdentifier(Identifier identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier.Value))
        {
            return "[]";
        }

        if (string.Equals(identifier.QuoteType, "SquareBracket", StringComparison.Ordinal))
        {
            return "[" + identifier.Value.Replace("]", "]]", StringComparison.Ordinal) + "]";
        }

        if (string.Equals(identifier.QuoteType, "Backtick", StringComparison.Ordinal))
        {
            return "[" + identifier.Value.Replace("]", "]]", StringComparison.Ordinal) + "]";
        }

        if (string.Equals(identifier.QuoteType, "MergeActionPseudoColumn", StringComparison.Ordinal))
        {
            return string.Equals(identifier.Value, "$action", StringComparison.OrdinalIgnoreCase)
                ? "$action"
                : throw new InvalidOperationException($"Unsupported MetaWeaveScript MERGE OUTPUT pseudo-column '{identifier.Value}'.");
        }

        if (!string.IsNullOrWhiteSpace(identifier.QuoteType) &&
            !string.Equals(identifier.QuoteType, "NotQuoted", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unsupported MetaWeaveScript Identifier.QuoteType '{identifier.QuoteType}'.");
        }

        if (IsPlainIdentifier(identifier.Value))
        {
            return identifier.Value;
        }

        return "[" + identifier.Value.Replace("]", "]]", StringComparison.Ordinal) + "]";
    }

    private static bool IsPlainIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (!(char.IsLetter(value[0]) || value[0] == '_' || value[0] == '@' || value[0] == '#'))
        {
            return false;
        }

        for (var i = 1; i < value.Length; i++)
        {
            var ch = value[i];
            if (!(char.IsLetterOrDigit(ch) || ch == '_' || ch == '@' || ch == '#' || ch == '$'))
            {
                return false;
            }
        }

        return true;
    }
}
