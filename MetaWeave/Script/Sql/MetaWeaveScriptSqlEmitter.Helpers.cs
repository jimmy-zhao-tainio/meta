using System.Globalization;
using MetaWeave;

namespace MetaWeaveScript.Sql;

internal sealed partial class MetaWeaveScriptSqlEmitter
{
    private static T GetById<T>(IEnumerable<T> rows, Func<T, string?> selectId, string id, string label) where T : class
    {
        var match = rows.FirstOrDefault(row => string.Equals(selectId(row) ?? string.Empty, id, StringComparison.Ordinal));
        return match ?? throw new InvalidOperationException($"Required MetaWeaveScript lookup '{label}' with id '{id}' was not found.");
    }

    private static T GetByBaseId<T>(IEnumerable<T> rows, Func<T, string?> selectBaseId, string baseId, string label) where T : class
    {
        var match = FindByBaseId(rows, selectBaseId, baseId);
        return match ?? throw new InvalidOperationException($"Required MetaWeaveScript base lookup '{label}' with base id '{baseId}' was not found.");
    }

    private static T? FindByBaseId<T>(IEnumerable<T> rows, Func<T, string?> selectBaseId, string baseId) where T : class =>
        rows.FirstOrDefault(row => string.Equals(selectBaseId(row) ?? string.Empty, baseId, StringComparison.Ordinal));

    private static TLink GetOwnerLink<TLink>(IEnumerable<TLink> links, Func<TLink, string?> selectOwnerId, string ownerId, string label) where TLink : class
    {
        var match = FindOwnerLink(links, selectOwnerId, ownerId);
        return match ?? throw new InvalidOperationException($"Required MetaWeaveScript link '{label}' with owner id '{ownerId}' was not found.");
    }

    private static TLink? FindOwnerLink<TLink>(IEnumerable<TLink> links, Func<TLink, string?> selectOwnerId, string ownerId) where TLink : class =>
        links.FirstOrDefault(row => string.Equals(selectOwnerId(row) ?? string.Empty, ownerId, StringComparison.Ordinal));

    private static IEnumerable<TItem> GetOrderedItems<TItem>(IEnumerable<TItem> items,
        Func<TItem, string?> selectOwnerId, Func<TItem, string?> selectOrdinal, string ownerId) where TItem : class =>
        items.Where(row => string.Equals(selectOwnerId(row) ?? string.Empty, ownerId, StringComparison.Ordinal))
            .OrderBy(row => ParseOrdinal(selectOrdinal(row)));

    private static int ParseOrdinal(string? ordinal) =>
        int.TryParse(ordinal, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;

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
