using System.Collections;
using System.Reflection;
using Meta.Integration;
using MetaWeave;
using MetaWeaveScript.Sql;
using MetaWeaveScript.Sql.Parsing;

namespace MetaWeaveScript.Tests;

public sealed class MetaWeaveScriptSqlTests
{
    public static TheoryData<string> RetainedQueries => new()
    {
        "SELECT 1 AS Id;",
        "-- target identity\nSELECT /* spelling witness */ 0001 AS Id, 'it''s' AS Name, NULL AS RoleId;",
        "SELECT DISTINCT s.Id AS Id FROM Source AS s;",
        "SELECT s.Id AS Id, s.Name AS Name FROM Source AS s;",
        "WITH cte AS (SELECT s.Id AS Id, s.Name AS Name FROM Source AS s) SELECT c.Id AS Id, UPPER(c.Name) AS Name FROM cte AS c WHERE c.Name IS NOT NULL;",
        "WITH first_cte AS (SELECT s.Id AS Id FROM Source AS s), second_cte AS (SELECT f.Id AS Id FROM first_cte AS f) SELECT s.Id AS Id FROM second_cte AS s;",
        "WITH hierarchy AS (SELECT n.Id AS Id, n.ParentId AS ParentId, n.Id AS Path FROM (VALUES ('root', NULL), ('child', 'root')) AS n(Id, ParentId) WHERE n.ParentId IS NULL UNION ALL SELECT n.Id AS Id, n.ParentId AS ParentId, CONCAT(h.Path, '/', n.Id) AS Path FROM (VALUES ('root', NULL), ('child', 'root')) AS n(Id, ParentId) INNER JOIN hierarchy AS h ON n.ParentId = h.Id) SELECT h.Id AS Id, h.Path AS Path FROM hierarchy AS h;",
        "SELECT s.Id AS Id FROM Source AS s UNION ALL SELECT t.Id AS Id FROM Other AS t;",
        "SELECT s.Id AS Id FROM Source AS s UNION ALL (SELECT t.Id AS Id FROM Other AS t);",
        "SELECT l.Id AS Id FROM LeftSource AS l INNER JOIN RightSource AS r ON l.RoleId = r.Id;",
        "SELECT l.Id AS Id, r.Name AS Name FROM LeftSource AS l LEFT JOIN RightSource AS r ON l.RoleId = r.Id;",
        "SELECT l.Id AS Id FROM LeftSource AS l CROSS JOIN RightSource AS r;",
        "SELECT d.Id AS Id FROM (SELECT s.Id AS Id FROM Source AS s) AS d;",
        "SELECT v.Id AS Id, v.Name AS Name FROM (VALUES (1, 'one'), (2, 'two')) AS v(Id, Name);",
        "SELECT s.Id AS Id, p.value AS Part FROM Source AS s CROSS APPLY STRING_SPLIT(s.Tags, ',') AS p;",
        "SELECT s.Id AS Id, p.value AS Part FROM Source AS s OUTER APPLY STRING_SPLIT(s.Tags, ',') AS p;",
        "SELECT s.Kind AS Id, COUNT(*) AS ItemCount, STRING_AGG(s.Name, ',') WITHIN GROUP (ORDER BY s.Name ASC) AS Names FROM Source AS s GROUP BY s.Kind;",
        "SELECT MIN(s.Name) AS FirstName, MAX(s.Name) AS LastName FROM Source AS s;",
        "SELECT c.Id AS Id, ROW_NUMBER() OVER (PARTITION BY c.Kind ORDER BY c.Phase ASC, COALESCE(TRY_CONVERT(int, c.Ordinal), 2147483647), c.Id) AS Ordinal FROM Source AS c;",
        "SELECT CONCAT(LOWER('A'), UPPER('b')) AS C, TRIM(' x ') AS T, LTRIM(' x') AS LT, RTRIM('x ') AS RT, LEN('abc ') AS N, REPLACE('abc', 'b', 'x') AS R, SUBSTRING('abc', 1, 2) AS S, LEFT('abc', 1) AS L, RIGHT('abc', 1) AS RR;",
        "SELECT IIF(1 = 1, 'yes', 'no') AS Answer;",
        "SELECT s.Id AS Id FROM Source AS s WHERE NOT ((s.A = 'a' OR s.B <> 'b') AND s.C < 'c') OR (s.D <= 'd' AND s.E > 'e' AND s.F >= 'f' AND s.G IN ('g', 'h') AND s.H IS NULL);",
        "SELECT s.Id AS Id, CASE WHEN s.Name LIKE 'A%' THEN CONCAT(UPPER(s.Name), '!') ELSE COALESCE(NULLIF(s.Name, ''), 'unknown') END AS Name FROM Source AS s;",
        "SELECT s.Id AS Id, (SELECT r.Name AS Name FROM Related AS r WHERE r.Id = s.RoleId) AS RelatedName FROM Source AS s WHERE EXISTS (SELECT r.Id AS Id FROM Related AS r WHERE r.Id = s.RoleId);",
        "SELECT w.Id AS Id, CONCAT(w.Name, '.', i.PhysicalName, '.', @databaseName) AS Name FROM warehouse.Source AS w INNER JOIN implementation.Mapping AS i ON w.Id = i.SourceId;"
    };

    [Theory]
    [MemberData(nameof(RetainedQueries))]
    public void RetainedQueriesParseEmitAndReparse(string sql)
    {
        var parser = new MetaWeaveScriptSqlParser();
        var service = new MetaWeaveScriptSqlService();
        var first = parser.ParseSqlCode(sql);
        var firstEmission = service.ExportToSqlCode(first);
        var second = parser.ParseSqlCode(firstEmission);
        var secondEmission = service.ExportToSqlCode(second);

        Assert.Equal(firstEmission, secondEmission);
        Assert.Equal(CreateStructuralFingerprint(first), CreateStructuralFingerprint(second));
    }

    [Theory]
    [InlineData("SELECT * FROM Source;")]
    [InlineData("SELECT -1 AS Id;")]
    [InlineData("SELECT 1.5 AS Id;")]
    [InlineData("SELECT CAST(s.Id AS int) AS Id FROM Source AS s;")]
    [InlineData("SELECT s.Id AS Id FROM catalog.dbo.Source AS s;")]
    [InlineData("SELECT s.Id AS Id FROM Source AS s ORDER BY s.Id;")]
    [InlineData("SELECT s.Id AS Id FROM Source AS s RIGHT JOIN Other AS o ON s.Id = o.Id;")]
    [InlineData("SELECT s.Id AS Id FROM Source AS s WHERE CONTAINS(s.Name, 'x');")]
    [InlineData("SELECT Id = s.Id FROM Source AS s;")]
    [InlineData("SELECT s.Id AS 'Id' FROM Source AS s;")]
    [InlineData("SELECT s.Id AS Id FROM Source AS s WHERE s.Id != 'x';")]
    [InlineData("SELECT STRING_AGG(s.Name, ',') AS Names FROM Source AS s;")]
    [InlineData("SELECT ROW_NUMBER() AS Ordinal FROM Source AS s;")]
    [InlineData("SELECT ROW_NUMBER() OVER (PARTITION BY s.Kind) AS Ordinal FROM Source AS s;")]
    [InlineData("SELECT TRY_CONVERT(bigint, s.Id) AS Id FROM Source AS s;")]
    [InlineData("SELECT TRY_CONVERT(int, s.Id, 1) AS Id FROM Source AS s;")]
    [InlineData("SELECT SHA256_HEX('abc') AS Hash;")]
    [InlineData("SELECT p.value AS Value FROM STRING_SPLIT('a,b', ',') AS p(value);")]
    public void ExcludedSyntaxFailsAsUnsupported(string sql)
    {
        var exception = Assert.Throws<MetaWeaveScriptSqlParserException>(() => new MetaWeaveScriptSqlParser().ParseSqlCode(sql));
        Assert.Equal(MetaWeaveScriptSqlParserFailureKind.UnsupportedSyntax, exception.FailureKind);
    }

    [Fact]
    public void ParsedModelSurvivesWorkspaceSerialization()
    {
        var service = new MetaWeaveScriptSqlService();
        var model = service.ImportFromSqlCode("SELECT s.Id AS Id, s.Name AS Name FROM Source AS s;");
        var workspacePath = Path.Combine(Path.GetTempPath(), "meta", "metaweavescript-tests", Guid.NewGuid().ToString("N"));

        TypedWorkspaceModelMapper.Create(model, workspacePath, "xml");
        var loaded = TypedWorkspaceModelMapper.Load<MetaWeaveModel>(workspacePath);

        Assert.Equal(service.ExportToSqlCode(model), service.ExportToSqlCode(loaded));
        Assert.Equal(CreateStructuralFingerprint(model), CreateStructuralFingerprint(loaded));
    }

    private static string CreateStructuralFingerprint(MetaWeaveModel model)
    {
        var entries = new List<string>();
        foreach (var listProperty in typeof(MetaWeaveModel).GetProperties().Where(property => typeof(IEnumerable).IsAssignableFrom(property.PropertyType)).OrderBy(property => property.Name, StringComparer.Ordinal))
        {
            if (listProperty.GetValue(model) is not IEnumerable rows)
            {
                continue;
            }
            foreach (var row in rows.Cast<object>().OrderBy(GetId, StringComparer.Ordinal))
            {
                var values = row.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public)
                    .OrderBy(property => property.Name, StringComparer.Ordinal)
                    .Select(property => $"{property.Name}={RenderValue(property.GetValue(row))}");
                entries.Add($"{listProperty.Name}:{string.Join("|", values)}");
            }
        }
        return string.Join("\n", entries);
    }

    private static string GetId(object row) => row.GetType().GetProperty("Id")?.GetValue(row)?.ToString() ?? string.Empty;

    private static string RenderValue(object? value) => value switch
    {
        null => "<null>",
        string text => text,
        _ => GetId(value)
    };
}
