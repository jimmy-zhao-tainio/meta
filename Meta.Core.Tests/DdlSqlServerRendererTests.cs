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
        table.Columns.Add(new DdlColumn { Name = "Id", DataType = "NVARCHAR(128)", IsNullable = false });
        table.Columns.Add(new DdlColumn { Name = "Name", DataType = "NVARCHAR(MAX)", IsNullable = false });
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

        Assert.Contains("CREATE NONCLUSTERED INDEX [IX_Sample_Name] ON [dbo].[Sample] ([Name] ASC);", sql, StringComparison.Ordinal);
    }
}