using Meta.Operations.Domain;
using MetaWeave.Core;
using MetaWeaveScript.Execution;
using MetaWeaveScript.Sql;

namespace MetaWeaveScript.Tests;

public sealed class MetaWeaveModelAccessTests
{
    [Fact]
    public void AppendingQueriesKeepsExistingRowsAndAllocatesDistinctIdentities()
    {
        var sql = new MetaWeaveScriptSqlService();
        var model = sql.ImportFromSqlCode("SELECT 'seed' AS Value;");
        var firstStatement = Assert.Single(model.SelectStatementList);
        var firstLiteral = Assert.Single(model.StringLiteralList);

        var secondStatement = sql.ImportIntoModel(model, "SELECT CONCAT('second', '') AS Value;");
        var thirdStatement = sql.ImportIntoModel(model, "SELECT 'third' AS Value;");

        Assert.Same(firstStatement, model.SelectStatementList[0]);
        Assert.Same(firstLiteral, model.StringLiteralList[0]);
        Assert.Equal(3, model.SelectStatementList.Count);
        MetaWeaveModelAccess.VisitCollections(new UniqueIdentities(model));
        Assert.Equal("seed", ExecuteValue(model, firstStatement));
        Assert.Equal("second", ExecuteValue(model, secondStatement));
        Assert.Equal("third", ExecuteValue(model, thirdStatement));
    }

    [Fact]
    public void UpdatingAQueryPrunesItsOldRowsAndKeepsOtherObjectIdentities()
    {
        var authoring = new MetaWeaveAuthoringService();
        var model = authoring.Create("Catalog");
        var weave = Assert.Single(model.WeaveList);
        var direction = authoring.AddDirection(model, "forward", "SourceModel", "TargetModel");
        var retained = authoring.AddRelation(model, "forward", "Retained", "SELECT ' retained Ω ' AS Value;");
        var retainedStatement = retained.SelectStatement;
        var retainedLiteral = Assert.Single(model.StringLiteralList);
        var transformation = authoring.AddTransformation(model, "forward", "Changed", "Target", "SELECT 'old' AS Value;");
        var removedStatement = transformation.SelectStatement;

        var updated = authoring.UpdateTransformation(model, "forward", "Changed", "SELECT CONCAT('new', '') AS Value;");

        Assert.Same(transformation, updated);
        Assert.Same(weave, Assert.Single(model.WeaveList));
        Assert.Same(direction, Assert.Single(model.DirectionList));
        Assert.Same(retained, Assert.Single(model.DirectionRelationList));
        Assert.Same(retainedStatement, retained.SelectStatement);
        Assert.Contains(retainedLiteral, model.StringLiteralList);
        Assert.DoesNotContain(removedStatement, model.SelectStatementList);
        Assert.DoesNotContain(model.StringLiteralList, literal => literal.Literal.Value == "old");
        Assert.Equal(2, model.SelectStatementList.Count);
        Assert.Equal(" retained Ω ", ExecuteValue(model, retainedStatement));
        Assert.Equal("new", ExecuteValue(model, transformation.SelectStatement));
    }

    [Fact]
    public void ImportedQueryUsesAuthoredOrdinalsAndReportsInvalidOrdinals()
    {
        var sql = new MetaWeaveScriptSqlService();
        var model = sql.ImportFromSqlCode("SELECT CONCAT('left', 'right') AS Value;");
        model.FunctionCallParametersItemList[0].Ordinal = "2";
        model.FunctionCallParametersItemList[1].Ordinal = "1";

        Assert.Equal("rightleft", ExecuteValue(model, Assert.Single(model.SelectStatementList)));
        Assert.Contains("CONCAT('right', 'left')", sql.ExportToSqlCode(model));

        model.FunctionCallParametersItemList[0].Ordinal = "-1";
        var result = new MetaWeaveScriptExecutionService().ExecuteQuery(model, EmptySource());

        Assert.False(result.IsSuccess);
        var issue = Assert.Single(result.Issues);
        Assert.Equal("SemanticOrdinalInvalid", issue.Code);
        Assert.Equal(model.FunctionCallParametersItemList[0].Id, issue.SyntaxId);
    }

    [Fact]
    public void ImportedQueryReportsDuplicateSemanticIdentities()
    {
        var model = new MetaWeaveScriptSqlService().ImportFromSqlCode("SELECT 'value' AS Value;");
        var identifier = model.IdentifierList[0];
        model.IdentifierList.Add(new Identifier { Id = identifier.Id, Value = "other" });

        var result = new MetaWeaveScriptExecutionService().ExecuteQuery(model, EmptySource());

        Assert.False(result.IsSuccess);
        var issue = Assert.Single(result.Issues);
        Assert.Equal("SemanticIdentityDuplicate", issue.Code);
        Assert.Equal(identifier.Id, issue.SyntaxId);
    }

    private static string? ExecuteValue(MetaWeaveModel model, SelectStatement statement)
    {
        var result = new MetaWeaveScriptExecutionService().ExecuteQuery(model, statement, EmptySource());
        Assert.True(result.IsSuccess, string.Join("; ", result.Issues.Select(issue => issue.Code + ": " + issue.Message)));
        return Assert.Single(Assert.Single(Assert.IsType<MetaWeaveScriptQueryOutput>(result.Output).Rows).Values).ToInvariantString();
    }

    private static InMemoryWorkspace EmptySource() => new(
        new GenericModel { Name = "SourceModel" },
        new GenericInstance { ModelName = "SourceModel" });

    private sealed class UniqueIdentities(MetaWeaveModel model) : IMetaWeaveCollectionVisitor
    {
        public void Visit<TEntity>(string entityName, Func<MetaWeaveModel, IList<TEntity>> rows,
            Func<TEntity, string> readId, Action<TEntity, string> writeId) where TEntity : class
        {
            var ids = rows(model).Select(readId).ToArray();
            Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
        }
    }
}
