namespace Meta.Operations.Domain;

public static class MetaIdentity
{
    public const int MaximumLength = 450;
    private const char FirstAllowedCharacter = ' ';
    private const char LastAllowedCharacter = '~';

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
            error = "Identity is required.";
            return false;
        }

        if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            error = "Identity cannot contain leading or trailing whitespace.";
            return false;
        }

        if (value.Length > MaximumLength)
        {
            error = $"Identity cannot exceed {MaximumLength} characters.";
            return false;
        }

        foreach (var character in value)
        {
            if (character < FirstAllowedCharacter ||
                character > LastAllowedCharacter)
            {
                error = "Identity can contain only printable ASCII characters.";
                return false;
            }
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
