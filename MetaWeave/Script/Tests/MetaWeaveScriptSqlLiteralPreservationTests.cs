using MetaWeaveScript.Sql;

namespace MetaWeaveScript.Tests;

public sealed class MetaWeaveScriptSqlLiteralPreservationTests
{
    [Theory]
    [InlineData("\r\n")]
    [InlineData("\r")]
    [InlineData("\n")]
    public void ImportAndExportPreserveExactLiteralTextAndFollowingErrorLocation(string lineBreak)
    {
        var value = " Åsa's 漢字 🙂" + lineBreak + "quoted 'value' ";
        var code = "SELECT '" + value.Replace("'", "''", StringComparison.Ordinal) + "' AS Value;";
        var service = new MetaWeaveScriptSqlService();

        var imported = service.ImportFromSqlCode(code);
        Assert.Equal(value, Assert.Single(imported.StringLiteralList).Literal.Value);
        var exported = service.ExportToSqlCode(imported);
        var restored = service.ImportFromSqlCode(exported);
        Assert.Equal(value, Assert.Single(restored.StringLiteralList).Literal.Value);

        var invalidCode = code + " ?";
        var error = Assert.Throws<MetaWeaveScriptSqlImportException>(
            () => service.ImportFromSqlCode(invalidCode));
        Assert.Equal(2, error.Line);
        Assert.Equal("quoted ''value'' ' AS Value; ".Length + 1, error.Column);
        Assert.Equal(invalidCode.Length - 1, error.Offset);
    }
}
