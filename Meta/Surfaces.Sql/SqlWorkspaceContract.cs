using Meta.Operations.Domain;

namespace Meta.Surfaces.Sql;

internal static class SqlWorkspaceContract
{
    public const string Schema = "dbo";

    public const string CaseInsensitiveCollation =
        "Latin1_General_100_CI_AS_SC";

    public const string PropertySqlType = "NVARCHAR(MAX)";

    public const string LogicalModelNameProperty = "Meta.ModelName";

    public static string IdentitySqlType { get; } =
        $"NVARCHAR({MetaIdentity.MaximumLength}) COLLATE {CaseInsensitiveCollation}";
}
