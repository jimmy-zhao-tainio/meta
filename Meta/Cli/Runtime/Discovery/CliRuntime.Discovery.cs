internal sealed partial class CliRuntime
{
    static int LevenshteinDistance(string left, string right)
    {
        if (string.IsNullOrEmpty(left))
        {
            return string.IsNullOrEmpty(right) ? 0 : right.Length;
        }
    
        if (string.IsNullOrEmpty(right))
        {
            return left.Length;
        }
    
        var costs = new int[right.Length + 1];
        for (var j = 0; j <= right.Length; j++)
        {
            costs[j] = j;
        }
    
        for (var i = 1; i <= left.Length; i++)
        {
            var previousDiagonal = costs[0];
            costs[0] = i;
            for (var j = 1; j <= right.Length; j++)
            {
                var temp = costs[j];
                var substitutionCost = char.ToLowerInvariant(left[i - 1]) == char.ToLowerInvariant(right[j - 1]) ? 0 : 1;
                costs[j] = Math.Min(
                    Math.Min(costs[j] + 1, costs[j - 1] + 1),
                    previousDiagonal + substitutionCost);
                previousDiagonal = temp;
            }
        }
    
        return costs[right.Length];
    }

    IReadOnlyList<string> SuggestValues(string input, IEnumerable<string> candidates, int maxCount = 3)
    {
        var normalizedInput = input?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedInput))
        {
            return Array.Empty<string>();
        }
    
        var uniqueCandidates = candidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(candidate => candidate, StringComparer.OrdinalIgnoreCase)
            .ToList();
    
        var scored = new List<(string Candidate, int Rank, int Distance, double Similarity)>();
        foreach (var candidate in uniqueCandidates)
        {
            if (string.Equals(candidate, normalizedInput, StringComparison.Ordinal))
            {
                scored.Add((candidate, 0, 0, 1.0));
                continue;
            }
    
            if (string.Equals(candidate, normalizedInput, StringComparison.OrdinalIgnoreCase))
            {
                scored.Add((candidate, 1, 0, 1.0));
                continue;
            }
    
            if (candidate.StartsWith(normalizedInput, StringComparison.OrdinalIgnoreCase))
            {
                var distance = Math.Abs(candidate.Length - normalizedInput.Length);
                var similarity = 1.0 - (distance / (double)Math.Max(candidate.Length, normalizedInput.Length));
                scored.Add((candidate, 2, distance, similarity));
                continue;
            }
    
            var distanceEdit = LevenshteinDistance(normalizedInput, candidate);
            var maxLength = Math.Max(normalizedInput.Length, candidate.Length);
            var similarityEdit = maxLength == 0 ? 1.0 : 1.0 - (distanceEdit / (double)maxLength);
            var accepted = normalizedInput.Length switch
            {
                <= 4 => distanceEdit <= 2,
                <= 8 => distanceEdit <= 2,
                _ => similarityEdit >= 0.8,
            };
    
            if (accepted)
            {
                scored.Add((candidate, 3, distanceEdit, similarityEdit));
            }
        }
    
        return scored
            .OrderBy(item => item.Rank)
            .ThenBy(item => item.Distance)
            .ThenByDescending(item => item.Similarity)
            .ThenBy(item => item.Candidate, StringComparer.OrdinalIgnoreCase)
            .Take(maxCount)
            .Select(item => item.Candidate)
            .ToList();
    }

    string? TryGetEntityFromCurrentCommandArgs()
    {
        if (args.Length == 0)
        {
            return null;
        }
    
        var command = args[0].Trim().ToLowerInvariant();
        return command switch
        {
            "view" when args.Length >= 3 && string.Equals(args[1], "entity", StringComparison.OrdinalIgnoreCase) => args[2],
            "view" when args.Length >= 4 && string.Equals(args[1], "instance", StringComparison.OrdinalIgnoreCase) => args[2],
            "query" when args.Length >= 2 => args[1],
            "insert" when args.Length >= 2 => args[1],
            "bulk-insert" when args.Length >= 2 => args[1],
            "delete" when args.Length >= 2 => args[1],
            "instance" when args.Length >= 3 && string.Equals(args[1], "update", StringComparison.OrdinalIgnoreCase) => args[2],
            "instance" when args.Length >= 4 && string.Equals(args[1], "relationship", StringComparison.OrdinalIgnoreCase) => args[3],
            _ => null,
        };
    }
}
