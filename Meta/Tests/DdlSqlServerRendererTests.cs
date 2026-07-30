using Meta.Core.Ddl;

namespace Meta.Core.Tests;

public sealed class DdlSqlServerRendererTests
{
    [Fact]
    public void RenderSchema_IncludesIndexes()
    {
        var database = new DdlDatabase();
        var table = new DdlTable
        {
            Schema = "dbo",
            Name = "Sample",
            PrimaryKey = new DdlPrimaryKeyConstraint
            {
                Name = "PK_Sample",
                IsClustered = true,
            },
        };
        table.PrimaryKey.ColumnNames.Add("Id");
        table.Columns.Add(new DdlColumn
        {
            Name = "Id",
            DataType = "NVARCHAR(128)",
            Collation = MetaSqlStorageContract.IdentityCollation,
            IsNullable = false,
        });
        table.Columns.Add(new DdlColumn { Name = "Name", DataType = "NVARCHAR(MAX)", IsNullable = false });
        table.CheckConstraints.Add(new DdlCheckConstraint
        {
            Name =
                MetaSqlStorageContract.GetIdentityCheckConstraintName(
                    "Sample",
                    "Id"),
            Expression =
                MetaSqlStorageContract.GetIdentityCheckExpression("Id"),
        });
        var index = new DdlIndex
        {
            Name = "IX_Sample_Name",
            IsUnique = false,
            IsClustered = false,
        };
        index.KeyColumns.Add(new DdlIndexColumn { Name = "Name" });
        table.Indexes.Add(index);
        database.Tables.Add(table);

        var sql = DdlSqlServerRenderer.RenderSchema(database);

        Assert.Contains(
            "[Id] NVARCHAR(128) COLLATE Latin1_General_100_CI_AS NOT NULL",
            sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "CONSTRAINT [CK_Sample_Id_MetaIdentity] CHECK (DATALENGTH([Id]) > 0",
            sql,
            StringComparison.Ordinal);
        Assert.Contains("CREATE NONCLUSTERED INDEX [IX_Sample_Name] ON [dbo].[Sample] ([Name] ASC);", sql, StringComparison.Ordinal);
    }
}
