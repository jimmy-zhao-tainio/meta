using System.Data;
using Microsoft.Data.SqlClient;
using Meta.Operations.Domain;

namespace Meta.Surfaces.Sql;

internal static class SqlWorkspaceModelMetadata
{
    public static string Read(
        SqlConnection connection,
        SqlTransaction? transaction)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT TOP (1) CONVERT(nvarchar(450), value)
            FROM sys.extended_properties
            WHERE class = 0
              AND major_id = 0
              AND name = @name;
            """;
        command.Parameters.Add(PropertyNameParameter());
        var value = command.ExecuteScalar();
        return value is null || value == DBNull.Value
            ? MetaName.Require(connection.Database, "Database name.")
            : MetaName.Require(
                Convert.ToString(value),
                "SQL workspace logical model name.");
    }

    public static void Write(
        SqlConnection connection,
        SqlTransaction transaction,
        string modelName)
    {
        var name = MetaName.Require(modelName, "Model name.");
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            IF EXISTS
            (
                SELECT 1
                FROM sys.extended_properties
                WHERE class = 0
                  AND major_id = 0
                  AND name = @name
            )
            BEGIN
                EXEC sys.sp_updateextendedproperty
                    @name = @name,
                    @value = @value;
            END
            ELSE
            BEGIN
                EXEC sys.sp_addextendedproperty
                    @name = @name,
                    @value = @value;
            END;
            """;
        command.Parameters.Add(PropertyNameParameter());
        command.Parameters.Add(new SqlParameter(
            "@value",
            SqlDbType.NVarChar,
            MetaName.MaximumLength)
        {
            Value = name,
        });
        command.ExecuteNonQuery();
    }

    private static SqlParameter PropertyNameParameter() =>
        new("@name", SqlDbType.NVarChar, 128)
        {
            Value = SqlWorkspaceContract.LogicalModelNameProperty,
        };
}
