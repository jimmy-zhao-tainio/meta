using System.Security.Cryptography;
using System.Text;

namespace Meta.Core.Ddl;

public static class MetaSqlStorageContract
{
    public const string IdentityCollation = "Latin1_General_100_CI_AS";
    public const string IdentityCharacterCollation =
        "Latin1_General_100_BIN2";
    public const int IdentityMaxLength = 128;

    public static string GetIdentityCheckConstraintName(
        string entityName,
        string columnName)
    {
        return NormalizeIdentifier(
            $"CK_{entityName}_{columnName}_MetaIdentity");
    }

    public static string GetIdentityCheckExpression(string columnName)
    {
        var column = QuoteIdentifier(columnName);
        return
            $"DATALENGTH({column}) > 0 AND " +
            $"LEFT({column}, 1) <> N' ' AND " +
            $"RIGHT({column}, 1) <> N' ' AND " +
            $"{column} COLLATE {IdentityCharacterCollation} " +
            "NOT LIKE N'%[^ -~]%'";
    }

    public static string GetIdentityCheckCatalogDefinition(
        string columnName)
    {
        var column = QuoteIdentifier(columnName);
        return
            $"(datalength({column})>(0) AND " +
            $"left({column},(1))<>N' ' AND " +
            $"right({column},(1))<>N' ' AND " +
            $"NOT ({column}) collate {IdentityCharacterCollation} " +
            "like N'%[^ -~]%')";
    }

    public static string RequireRepresentableIdentity(
        string value,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"'{parameterName}' is required.");
        }

        if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"'{parameterName}' cannot contain leading or trailing whitespace.");
        }

        if (value.Length > IdentityMaxLength)
        {
            throw new InvalidOperationException(
                $"'{parameterName}' exceeds the SQL Server Meta Id length of {IdentityMaxLength}.");
        }

        if (value.Any(character => character is < ' ' or > '~'))
        {
            throw new InvalidOperationException(
                $"'{parameterName}' contains characters outside the SQL Server Meta identity repertoire (printable ASCII).");
        }

        return value;
    }

    private static string QuoteIdentifier(string identifier)
    {
        return $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";
    }

    private static string NormalizeIdentifier(string identifier)
    {
        if (identifier.Length <= IdentityMaxLength)
        {
            return identifier;
        }

        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(identifier));
        var hash = Convert.ToHexString(hashBytes)[..16];
        var prefixLength = IdentityMaxLength - 1 - hash.Length;
        return identifier[..prefixLength] + "_" + hash;
    }
}
