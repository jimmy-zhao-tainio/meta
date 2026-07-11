namespace MetaCli.Core;

internal static class MetaCliOrdering
{
    public static bool TryByPrevious<T>(
        IReadOnlyList<T> items,
        Func<T, T?> previous,
        out IReadOnlyList<T> ordered)
        where T : class
    {
        ordered = Array.Empty<T>();
        if (items.Count == 0)
        {
            return true;
        }

        var heads = items.Where(item => previous(item) is null).ToArray();
        if (heads.Length != 1)
        {
            return false;
        }

        var orderedList = new List<T>();
        var current = heads[0];
        var visited = new HashSet<T>(ReferenceEqualityComparer.Instance);
        while (current is not null && visited.Add(current))
        {
            orderedList.Add(current);
            var next = items.Where(item => ReferenceEquals(previous(item), current)).Take(2).ToArray();
            if (next.Length > 1)
            {
                return false;
            }

            current = next.Length == 0 ? null : next[0];
        }

        ordered = orderedList;
        return true;
    }
}
