using System.Text.RegularExpressions;

namespace Meta.Operations.Domain;

public static class MetaName
{
    public const int MaximumLength = 128;

    private static readonly Regex Pattern =
        new("^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.Compiled);

    public static StringComparer Comparer { get; } =
        StringComparer.OrdinalIgnoreCase;

    public static bool IsValid(string? value)
    {
        return TryValidate(value, out _);
    }

    public static bool TryValidate(string? value, out string error)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            error = "Name is required.";
            return false;
        }

        if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            error = "Name cannot contain leading or trailing whitespace.";
            return false;
        }

        if (value.Length > MaximumLength)
        {
            error = $"Name cannot exceed {MaximumLength} characters.";
            return false;
        }

        if (!Pattern.IsMatch(value))
        {
            error = "Name must begin with a letter or underscore and contain only letters, digits, and underscores.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    public static string Require(string? value, string description)
    {
        if (!TryValidate(value, out var error))
        {
            throw new InvalidOperationException($"{description} {error}");
        }

        return value!;
    }
}
