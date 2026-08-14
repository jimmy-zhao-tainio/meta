using MetaWeave;

namespace MetaWeaveScript.Tests;

public sealed class MetaWeaveModelTests
{
    [Fact]
    public void GeneratedModelMatchesTheDistilledEntityBoundary()
    {
        var entityLists = typeof(MetaWeaveModel)
            .GetProperties()
            .Where(property =>
                property.Name.EndsWith("List", StringComparison.Ordinal)
                && property.PropertyType.IsGenericType
                && property.PropertyType.GetGenericTypeDefinition() == typeof(List<>))
            .ToArray();

        Assert.Equal(126, entityLists.Length);
        Assert.Equal(
            122,
            entityLists.Count(property => property.Name is not
                (nameof(MetaWeaveModel.WeaveList) or
                 nameof(MetaWeaveModel.DirectionList) or
                 nameof(MetaWeaveModel.DirectionRequirementList) or
                 nameof(MetaWeaveModel.TransformationList))));
        Assert.Contains(entityLists, property => property.Name == nameof(MetaWeaveModel.WeaveList));
        Assert.Contains(entityLists, property => property.Name == nameof(MetaWeaveModel.DirectionList));
        Assert.Contains(entityLists, property => property.Name == nameof(MetaWeaveModel.DirectionRequirementList));
        Assert.Contains(entityLists, property => property.Name == nameof(MetaWeaveModel.TransformationList));
        Assert.Contains(entityLists, property => property.Name == nameof(MetaWeaveModel.IIfCallList));
        Assert.Contains(entityLists, property => property.Name == nameof(MetaWeaveModel.ScalarSubqueryList));
        Assert.DoesNotContain(entityLists, property => property.Name == "XmlNodesTableReferenceList");
        Assert.DoesNotContain(entityLists, property => property.Name == "PivotedTableReferenceList");
        Assert.DoesNotContain(entityLists, property => property.Name == "TransformScriptList");
    }

    [Fact]
    public void ExcludedSyntaxFieldsAreAbsentFromGeneratedTypes()
    {
        Assert.Null(typeof(BinaryQueryExpression).GetProperty("All"));
        Assert.Null(typeof(BinaryQueryExpression).GetProperty("BinaryQueryExpressionType"));
        Assert.Null(typeof(FunctionCall).GetProperty("UniqueRowFilter"));
        Assert.Null(typeof(FunctionCall).GetProperty("WithArrayWrapper"));
        Assert.Null(typeof(GroupByClause).GetProperty("GroupByOption"));
        Assert.Null(typeof(IdentifierOrValueExpression).GetProperty("Value"));
        Assert.Null(typeof(QualifiedJoin).GetProperty("JoinHint"));
        Assert.Null(typeof(StringLiteral).GetProperty("IsNational"));
        Assert.Null(typeof(TableReferenceWithAlias).GetProperty("ForPath"));
    }
}
