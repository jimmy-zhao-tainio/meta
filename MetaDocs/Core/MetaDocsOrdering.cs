using System.Runtime.CompilerServices;

namespace MetaDocs.Core;

internal static class MetaDocsOrdering
{
    public static IReadOnlyList<T> ByPrevious<T>(
        IEnumerable<T> rows,
        Func<T, T?> previous,
        Func<T, string?> fallbackKey)
        where T : class
    {
        var items = rows
            .Where(static row => row is not null)
            .Distinct(ReferenceComparer<T>.Instance)
            .ToArray();
        if (items.Length <= 1)
        {
            return items;
        }

        var itemSet = items.ToHashSet(ReferenceComparer<T>.Instance);
        var childrenByPrevious = items
            .Where(row => previous(row) is not null && itemSet.Contains(previous(row)!))
            .GroupBy(row => previous(row)!, ReferenceComparer<T>.Instance)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(fallbackKey, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                ReferenceComparer<T>.Instance);

        var result = new List<T>(items.Length);
        var seen = new HashSet<T>(ReferenceComparer<T>.Instance);
        var roots = items
            .Where(row => previous(row) is null || !itemSet.Contains(previous(row)!))
            .OrderBy(fallbackKey, StringComparer.OrdinalIgnoreCase);

        foreach (var root in roots)
        {
            Append(root);
        }

        foreach (var remaining in items
                     .Where(row => !seen.Contains(row))
                     .OrderBy(fallbackKey, StringComparer.OrdinalIgnoreCase))
        {
            Append(remaining);
        }

        return result;

        void Append(T row)
        {
            if (!seen.Add(row))
            {
                return;
            }

            result.Add(row);
            if (!childrenByPrevious.TryGetValue(row, out var children))
            {
                return;
            }

            foreach (var child in children)
            {
                Append(child);
            }
        }
    }

    private sealed class ReferenceComparer<T> : IEqualityComparer<T>
        where T : class
    {
        public static readonly ReferenceComparer<T> Instance = new();

        public bool Equals(T? x, T? y) => ReferenceEquals(x, y);

        public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
