using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Meta.Core.Presentation;

public sealed class ConsolePresenter
{
    public void WriteOk(string summary, params (string Key, string Value)[] details)
    {
        Console.WriteLine($"OK: {Normalize(summary)}");
        WriteDetails(details);
    }

    public void WriteWarning(string message)
    {
        Console.WriteLine($"Warning: {Normalize(message)}");
    }

    public void WriteInfo(string message)
    {
        Console.WriteLine(Normalize(message));
    }

    public void WriteUsage(string usage)
    {
        Console.WriteLine("Usage:");
        foreach (var line in WrapWithPrefix(Normalize(usage), "  ", prefixWidth: 2))
        {
            Console.WriteLine(line);
        }
    }

    public void WriteCommandCatalog(string title, IReadOnlyList<(string Command, string Description)> commands)
    {
        var normalizedTitle = Normalize(title);
        if (!string.IsNullOrWhiteSpace(normalizedTitle))
        {
            Console.WriteLine(normalizedTitle);
            Console.WriteLine();
        }

        if (commands == null || commands.Count == 0)
        {
            Console.WriteLine("  (none)");
            return;
        }

        var ordered = commands
            .Select(item => (Command: Normalize(item.Command), Description: Normalize(item.Description)))
            .ToList();
        var maxCommandWidth = ordered.Max(item => item.Command.Length);
        var contentWidth = GetContentWidth();
        var descriptionWidth = Math.Max(20, contentWidth - (2 + maxCommandWidth + 2));

        foreach (var item in ordered)
        {
            var left = $"  {item.Command.PadRight(maxCommandWidth + 2)}";
            var wrapped = WrapText(item.Description, descriptionWidth);
            if (wrapped.Count == 0)
            {
                Console.WriteLine(left.TrimEnd());
                continue;
            }

            Console.WriteLine(left + wrapped[0]);
            var continuationPrefix = new string(' ', left.Length);
            for (var i = 1; i < wrapped.Count; i++)
            {
                Console.WriteLine(continuationPrefix + wrapped[i]);
            }
        }
    }

    public void WriteOptionCatalog(IReadOnlyList<(string Option, string Description)> options, string title = "Options:")
    {
        Console.WriteLine(Normalize(title));
        Console.WriteLine();
        if (options == null || options.Count == 0)
        {
            Console.WriteLine("  (none)");
            return;
        }

        var ordered = options
            .Select(item => (Option: Normalize(item.Option), Description: Normalize(item.Description)))
            .ToList();
        var maxOptionWidth = ordered.Max(item => item.Option.Length);
        var contentWidth = GetContentWidth();
        var descriptionWidth = Math.Max(20, contentWidth - (2 + maxOptionWidth + 2));

        foreach (var item in ordered)
        {
            var left = $"  {item.Option.PadRight(maxOptionWidth + 2)}";
            var wrapped = WrapText(item.Description, descriptionWidth);
            if (wrapped.Count == 0)
            {
                Console.WriteLine(left.TrimEnd());
                continue;
            }

            Console.WriteLine(left + wrapped[0]);
            var continuationPrefix = new string(' ', left.Length);
            for (var i = 1; i < wrapped.Count; i++)
            {
                Console.WriteLine(continuationPrefix + wrapped[i]);
            }
        }
    }

    public void WriteExamples(IReadOnlyList<string> examples)
    {
        Console.WriteLine("Examples:");
        Console.WriteLine();
        if (examples == null || examples.Count == 0)
        {
            Console.WriteLine("  (none)");
            return;
        }

        foreach (var example in examples
                     .Where(item => !string.IsNullOrWhiteSpace(item)))
        {
            Console.WriteLine($"  {Normalize(example)}");
        }
    }

    public void WriteNext(string next)
    {
        Console.WriteLine($"Next: {Normalize(next)}");
    }

    public void WriteFailure(string message, IEnumerable<string>? details = null)
    {
        var renderedMessage = Normalize(message);
        if (string.IsNullOrWhiteSpace(renderedMessage))
        {
            renderedMessage = "unexpected failure";
        }

        Console.WriteLine($"Error: {renderedMessage}");

        var normalizedDetails = details?
            .Select(Normalize)
            .Where(detail => !string.IsNullOrWhiteSpace(detail))
            .ToList() ?? new List<string>();

        var next = normalizedDetails.FirstOrDefault(line => line.StartsWith("Next:", StringComparison.OrdinalIgnoreCase));
        var nonNextDetails = normalizedDetails
            .Where(line => !line.StartsWith("Next:", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (string.IsNullOrWhiteSpace(next))
        {
            next = "Next: meta help";
        }

        foreach (var detail in nonNextDetails)
        {
            Console.WriteLine(detail);
        }

        Console.WriteLine(next);
    }

    public void WriteKeyValueBlock(string title, IEnumerable<(string Key, string Value)> pairs)
    {
        Console.WriteLine($"{Normalize(title)}:");
        var normalizedPairs = pairs?
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Key))
            .ToList() ?? new List<(string Key, string Value)>();
        if (normalizedPairs.Count == 0)
        {
            Console.WriteLine("  (none)");
            return;
        }

        foreach (var (key, value) in normalizedPairs)
        {
            Console.WriteLine($"  {Normalize(key)}: {Normalize(value)}");
        }
    }

    public void WriteTable(IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows)
    {
        if (headers == null || headers.Count == 0)
        {
            return;
        }

        var normalizedHeaders = headers.Select(Normalize).ToList();
        var normalizedRows = rows?
            .Select(row =>
            {
                var normalized = row?.Select(Normalize).ToList() ?? new List<string>();
                while (normalized.Count < normalizedHeaders.Count)
                {
                    normalized.Add(string.Empty);
                }

                if (normalized.Count > normalizedHeaders.Count)
                {
                    normalized = normalized.Take(normalizedHeaders.Count).ToList();
                }

                return (IReadOnlyList<string>)normalized;
            })
            .ToList() ?? new List<IReadOnlyList<string>>();

        var widths = new int[normalizedHeaders.Count];
        for (var i = 0; i < normalizedHeaders.Count; i++)
        {
            widths[i] = normalizedHeaders[i].Length;
        }

        foreach (var row in normalizedRows)
        {
            for (var i = 0; i < widths.Length; i++)
            {
                var value = i < row.Count ? row[i] : string.Empty;
                if (value.Length > widths[i])
                {
                    widths[i] = value.Length;
                }
            }
        }

        Console.WriteLine("  " + FormatRow(normalizedHeaders, widths));
        if (normalizedRows.Count == 0)
        {
            Console.WriteLine("  (none)");
            return;
        }

        foreach (var row in normalizedRows)
        {
            Console.WriteLine("  " + FormatRow(row, widths));
        }
    }

    public void WriteError(
        string code,
        string message,
        IEnumerable<(string Key, string Value)>? wherePairs = null,
        IEnumerable<string>? hints = null)
    {
        _ = Normalize(code);
        var details = new List<string>();

        if (hints != null)
        {
            details.AddRange(hints);
        }

        WriteFailure(message, details);
    }

    private static void WriteDetails(IEnumerable<(string Key, string Value)> details)
    {
        var normalized = details?
            .Where(detail => !string.IsNullOrWhiteSpace(detail.Key))
            .ToList() ?? new List<(string Key, string Value)>();
        foreach (var (key, value) in normalized)
        {
            Console.WriteLine($"{Normalize(key)}: {Normalize(value)}");
        }
    }

    private static string FormatRow(IReadOnlyList<string> row, IReadOnlyList<int> widths)
    {
        var cells = new string[widths.Count];
        for (var i = 0; i < widths.Count; i++)
        {
            var value = i < row.Count ? row[i] : string.Empty;
            cells[i] = value.PadRight(widths[i]);
        }

        return string.Join("  ", cells);
    }

    private static List<string> WrapText(string text, int width)
    {
        var normalized = Normalize(text);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return new List<string>();
        }

        if (width < 20)
        {
            return new List<string> { normalized };
        }

        var lines = new List<string>();
        var paragraphLines = normalized
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
            .Select(item => item.Trim())
            .ToList();

        foreach (var paragraph in paragraphLines)
        {
            if (paragraph.Length == 0)
            {
                lines.Add(string.Empty);
                continue;
            }

            var words = paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0)
            {
                lines.Add(string.Empty);
                continue;
            }

            var builder = new StringBuilder();
            foreach (var word in words)
            {
                if (builder.Length == 0)
                {
                    builder.Append(word);
                    continue;
                }

                if (builder.Length + 1 + word.Length <= width)
                {
                    builder.Append(' ');
                    builder.Append(word);
                    continue;
                }

                lines.Add(builder.ToString());
                builder.Clear();
                builder.Append(word);
            }

            if (builder.Length > 0)
            {
                lines.Add(builder.ToString());
            }
        }

        return lines;
    }

    private static IReadOnlyList<string> WrapWithPrefix(string value, string prefix, int prefixWidth)
    {
        var width = Math.Max(20, GetContentWidth() - prefixWidth);
        var wrapped = WrapText(value, width);
        if (wrapped.Count == 0)
        {
            return new[] { prefix.TrimEnd() };
        }

        return wrapped.Select(line => prefix + line).ToArray();
    }

    private static int GetContentWidth()
    {
        const int fallbackWidth = 100;
        try
        {
            var width = Console.IsOutputRedirected ? fallbackWidth : Console.WindowWidth;
            if (width <= 0)
            {
                return fallbackWidth;
            }

            return Math.Min(width, fallbackWidth);
        }
        catch
        {
            return fallbackWidth;
        }
    }

    private static string Normalize(string? value)
    {
        if (value == null)
        {
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.TrimEnd();
    }
}


