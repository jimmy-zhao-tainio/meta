using Meta.Core.Domain;

namespace Meta.Adapters;

internal static class SqlWorkspaceContract
{
    public const string Schema = "dbo";

    public const string CaseInsensitiveCollation =
        "Latin1_General_100_CI_AS_SC";

    public const string PropertySqlType = "NVARCHAR(MAX)";

    public static string IdentitySqlType { get; } =
        $"NVARCHAR({MetaIdentity.MaximumLength}) COLLATE {CaseInsensitiveCollation}";
}
