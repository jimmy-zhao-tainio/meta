#nullable enable
using System;
using System.Collections.Generic;

namespace MetaWeave;
public sealed partial class BinaryQueryExpression
{
    public string Id { get; set; } = null !;
    public QueryExpression QueryExpression { get; set; } = null !;
}

public sealed partial class BinaryQueryExpressionFirstQueryExpressionLink
{
    public string Id { get; set; } = null !;
    public BinaryQueryExpression BinaryQueryExpression { get; set; } = null !;
    public QueryExpression QueryExpression { get; set; } = null !;
}

public sealed partial class BinaryQueryExpressionSecondQueryExpressionLink
{
    public string Id { get; set; } = null !;
    public BinaryQueryExpression BinaryQueryExpression { get; set; } = null !;
    public QueryExpression QueryExpression { get; set; } = null !;
}

public sealed partial class BooleanBinaryExpression
{
    public string Id { get; set; } = null !;
    public string? BinaryExpressionType { get; set; }
    public BooleanExpression BooleanExpression { get; set; } = null !;
}

public sealed partial class BooleanBinaryExpressionFirstExpressionLink
{
    public string Id { get; set; } = null !;
    public BooleanBinaryExpression BooleanBinaryExpression { get; set; } = null !;
    public BooleanExpression BooleanExpression { get; set; } = null !;
}

public sealed partial class BooleanBinaryExpressionSecondExpressionLink
{
    public string Id { get; set; } = null !;
    public BooleanBinaryExpression BooleanBinaryExpression { get; set; } = null !;
    public BooleanExpression BooleanExpression { get; set; } = null !;
}

public sealed partial class BooleanComparisonExpression
{
    public string Id { get; set; } = null !;
    public string? ComparisonType { get; set; }
    public BooleanExpression BooleanExpression { get; set; } = null !;
}

public sealed partial class BooleanComparisonExpressionFirstExpressionLink
{
    public string Id { get; set; } = null !;
    public BooleanComparisonExpression BooleanComparisonExpression { get; set; } = null !;
    public ScalarExpression ScalarExpression { get; set; } = null !;
}

public sealed partial class BooleanComparisonExpressionSecondExpressionLink
{
    public string Id { get; set; } = null !;
    public BooleanComparisonExpression BooleanComparisonExpression { get; set; } = null !;
    public ScalarExpression ScalarExpression { get; set; } = null !;
}

public sealed partial class BooleanExpression
{
    public string Id { get; set; } = null !;
}

public sealed partial class BooleanIsNullExpression
{
    public string Id { get; set; } = null !;
    public string? IsNot { get; set; }
    public BooleanExpression BooleanExpression { get; set; } = null !;
}

public sealed partial class BooleanIsNullExpressionExpressionLink
{
    public string Id { get; set; } = null !;
    public BooleanIsNullExpression BooleanIsNullExpression { get; set; } = null !;
    public ScalarExpression ScalarExpression { get; set; } = null !;
}

public sealed partial class BooleanNotExpression
{
    public string Id { get; set; } = null !;
    public BooleanExpression BooleanExpression { get; set; } = null !;
}

public sealed partial class BooleanNotExpressionExpressionLink
{
    public string Id { get; set; } = null !;
    public BooleanExpression BooleanExpression { get; set; } = null !;
    public BooleanNotExpression BooleanNotExpression { get; set; } = null !;
}

public sealed partial class BooleanParenthesisExpression
{
    public string Id { get; set; } = null !;
    public BooleanExpression BooleanExpression { get; set; } = null !;
}

public sealed partial class BooleanParenthesisExpressionExpressionLink
{
    public string Id { get; set; } = null !;
    public BooleanExpression BooleanExpression { get; set; } = null !;
    public BooleanParenthesisExpression BooleanParenthesisExpression { get; set; } = null !;
}

public sealed partial class CaseExpression
{
    public string Id { get; set; } = null !;
    public PrimaryExpression PrimaryExpression { get; set; } = null !;
}

public sealed partial class CaseExpressionElseExpressionLink
{
    public string Id { get; set; } = null !;
    public CaseExpression CaseExpression { get; set; } = null !;
    public ScalarExpression ScalarExpression { get; set; } = null !;
}

public sealed partial class CoalesceExpression
{
    public string Id { get; set; } = null !;
    public PrimaryExpression PrimaryExpression { get; set; } = null !;
}

public sealed partial class CoalesceExpressionExpressionsItem
{
    public string Id { get; set; } = null !;
    public string? Ordinal { get; set; }
    public CoalesceExpression CoalesceExpression { get; set; } = null !;
    public ScalarExpression ScalarExpression { get; set; } = null !;
}

public sealed partial class ColumnReferenceExpression
{
    public string Id { get; set; } = null !;
    public string? ColumnType { get; set; }
    public PrimaryExpression PrimaryExpression { get; set; } = null !;
}

public sealed partial class ColumnReferenceExpressionMultiPartIdentifierLink
{
    public string Id { get; set; } = null !;
    public ColumnReferenceExpression ColumnReferenceExpression { get; set; } = null !;
    public MultiPartIdentifier MultiPartIdentifier { get; set; } = null !;
}

public sealed partial class CommonTableExpression
{
    public string Id { get; set; } = null !;
}

public sealed partial class CommonTableExpressionExpressionNameLink
{
    public string Id { get; set; } = null !;
    public CommonTableExpression CommonTableExpression { get; set; } = null !;
    public Identifier Identifier { get; set; } = null !;
}

public sealed partial class CommonTableExpressionQueryExpressionLink
{
    public string Id { get; set; } = null !;
    public CommonTableExpression CommonTableExpression { get; set; } = null !;
    public QueryExpression QueryExpression { get; set; } = null !;
}

public sealed partial class DataTypeReference
{
    public string Id { get; set; } = null !;
}

public sealed partial class DataTypeReferenceNameLink
{
    public string Id { get; set; } = null !;
    public DataTypeReference DataTypeReference { get; set; } = null !;
    public SchemaObjectName SchemaObjectName { get; set; } = null !;
}

public sealed partial class Direction
{
    public string Id { get; set; } = null !;
    public string TargetModelName { get; set; } = null !;
    public Weave Weave { get; set; } = null !;
}

public sealed partial class DirectionRelation
{
    public string Id { get; set; } = null !;
    public Direction Direction { get; set; } = null !;
    public SelectStatement SelectStatement { get; set; } = null !;
}

public sealed partial class DirectionRequirement
{
    public string Id { get; set; } = null !;
    public string Code { get; set; } = null !;
    public string Message { get; set; } = null !;
    public Direction Direction { get; set; } = null !;
    public SelectStatement SelectStatement { get; set; } = null !;
}

public sealed partial class DirectionSourceWorkspace
{
    public string Id { get; set; } = null !;
    public string ModelName { get; set; } = null !;
    public string Name { get; set; } = null !;
    public Direction Direction { get; set; } = null !;
}

public sealed partial class DirectionStringParameter
{
    public string Id { get; set; } = null !;
    public string Name { get; set; } = null !;
    public Direction Direction { get; set; } = null !;
}

public sealed partial class ExistsPredicate
{
    public string Id { get; set; } = null !;
    public BooleanExpression BooleanExpression { get; set; } = null !;
}

public sealed partial class ExistsPredicateSubqueryLink
{
    public string Id { get; set; } = null !;
    public ExistsPredicate ExistsPredicate { get; set; } = null !;
    public ScalarSubquery ScalarSubquery { get; set; } = null !;
}

public sealed partial class ExpressionGroupingSpecification
{
    public string Id { get; set; } = null !;
    public GroupingSpecification GroupingSpecification { get; set; } = null !;
}

public sealed partial class ExpressionGroupingSpecificationExpressionLink
{
    public string Id { get; set; } = null !;
    public ExpressionGroupingSpecification ExpressionGroupingSpecification { get; set; } = null !;
    public ScalarExpression ScalarExpression { get; set; } = null !;
}

public sealed partial class ExpressionWithSortOrder
{
    public string Id { get; set; } = null !;
    public string? SortOrder { get; set; }
}

public sealed partial class ExpressionWithSortOrderExpressionLink
{
    public string Id { get; set; } = null !;
    public ExpressionWithSortOrder ExpressionWithSortOrder { get; set; } = null !;
    public ScalarExpression ScalarExpression { get; set; } = null !;
}

public sealed partial class FromClause
{
    public string Id { get; set; } = null !;
}

public sealed partial class FromClauseTableReferencesItem
{
    public string Id { get; set; } = null !;
    public string? Ordinal { get; set; }
    public FromClause FromClause { get; set; } = null !;
    public TableReference TableReference { get; set; } = null !;
}

public sealed partial class FunctionCall
{
    public string Id { get; set; } = null !;
    public PrimaryExpression PrimaryExpression { get; set; } = null !;
}

public sealed partial class FunctionCallFunctionNameLink
{
    public string Id { get; set; } = null !;
    public FunctionCall FunctionCall { get; set; } = null !;
    public Identifier Identifier { get; set; } = null !;
}

public sealed partial class FunctionCallOverClauseLink
{
    public string Id { get; set; } = null !;
    public FunctionCall FunctionCall { get; set; } = null !;
    public OverClause OverClause { get; set; } = null !;
}

public sealed partial class FunctionCallParametersItem
{
    public string Id { get; set; } = null !;
    public string? Ordinal { get; set; }
    public FunctionCall FunctionCall { get; set; } = null !;
    public ScalarExpression ScalarExpression { get; set; } = null !;
}

public sealed partial class FunctionCallWithinGroupOrderByClauseLink
{
    public string Id { get; set; } = null !;
    public FunctionCall FunctionCall { get; set; } = null !;
    public OrderByClause OrderByClause { get; set; } = null !;
}

public sealed partial class GlobalFunctionTableReference
{
    public string Id { get; set; } = null !;
    public TableReferenceWithAlias TableReferenceWithAlias { get; set; } = null !;
}

public sealed partial class GlobalFunctionTableReferenceNameLink
{
    public string Id { get; set; } = null !;
    public GlobalFunctionTableReference GlobalFunctionTableReference { get; set; } = null !;
    public Identifier Identifier { get; set; } = null !;
}

public sealed partial class GlobalFunctionTableReferenceParametersItem
{
    public string Id { get; set; } = null !;
    public string? Ordinal { get; set; }
    public GlobalFunctionTableReference GlobalFunctionTableReference { get; set; } = null !;
    public ScalarExpression ScalarExpression { get; set; } = null !;
}

public sealed partial class GroupByClause
{
    public string Id { get; set; } = null !;
}

public sealed partial class GroupByClauseGroupingSpecificationsItem
{
    public string Id { get; set; } = null !;
    public string? Ordinal { get; set; }
    public GroupByClause GroupByClause { get; set; } = null !;
    public GroupingSpecification GroupingSpecification { get; set; } = null !;
}

public sealed partial class GroupingSpecification
{
    public string Id { get; set; } = null !;
}

public sealed partial class Identifier
{
    public string Id { get; set; } = null !;
    public string? QuoteType { get; set; }
    public string? Value { get; set; }
}

public sealed partial class IdentifierOrValueExpression
{
    public string Id { get; set; } = null !;
}

public sealed partial class IdentifierOrValueExpressionIdentifierLink
{
    public string Id { get; set; } = null !;
    public Identifier Identifier { get; set; } = null !;
    public IdentifierOrValueExpression IdentifierOrValueExpression { get; set; } = null !;
}

public sealed partial class IIfCall
{
    public string Id { get; set; } = null !;
    public PrimaryExpression PrimaryExpression { get; set; } = null !;
}

public sealed partial class IIfCallElseExpressionLink
{
    public string Id { get; set; } = null !;
    public IIfCall IIfCall { get; set; } = null !;
    public ScalarExpression ScalarExpression { get; set; } = null !;
}

public sealed partial class IIfCallPredicateLink
{
    public string Id { get; set; } = null !;
    public BooleanExpression BooleanExpression { get; set; } = null !;
    public IIfCall IIfCall { get; set; } = null !;
}

public sealed partial class IIfCallThenExpressionLink
{
    public string Id { get; set; } = null !;
    public IIfCall IIfCall { get; set; } = null !;
    public ScalarExpression ScalarExpression { get; set; } = null !;
}

public sealed partial class InlineDerivedTable
{
    public string Id { get; set; } = null !;
    public TableReferenceWithAliasAndColumns TableReferenceWithAliasAndColumns { get; set; } = null !;
}

public sealed partial class InlineDerivedTableRowValuesItem
{
    public string Id { get; set; } = null !;
    public string? Ordinal { get; set; }
    public InlineDerivedTable InlineDerivedTable { get; set; } = null !;
    public RowValue RowValue { get; set; } = null !;
}

public sealed partial class InPredicate
{
    public string Id { get; set; } = null !;
    public BooleanExpression BooleanExpression { get; set; } = null !;
}

public sealed partial class InPredicateExpressionLink
{
    public string Id { get; set; } = null !;
    public InPredicate InPredicate { get; set; } = null !;
    public ScalarExpression ScalarExpression { get; set; } = null !;
}

public sealed partial class InPredicateValuesItem
{
    public string Id { get; set; } = null !;
    public string? Ordinal { get; set; }
    public InPredicate InPredicate { get; set; } = null !;
    public ScalarExpression ScalarExpression { get; set; } = null !;
}

public sealed partial class IntegerLiteral
{
    public string Id { get; set; } = null !;
    public Literal Literal { get; set; } = null !;
}

public sealed partial class JoinTableReference
{
    public string Id { get; set; } = null !;
    public TableReference TableReference { get; set; } = null !;
}

public sealed partial class JoinTableReferenceFirstTableReferenceLink
{
    public string Id { get; set; } = null !;
    public JoinTableReference JoinTableReference { get; set; } = null !;
    public TableReference TableReference { get; set; } = null !;
}

public sealed partial class JoinTableReferenceSecondTableReferenceLink
{
    public string Id { get; set; } = null !;
    public JoinTableReference JoinTableReference { get; set; } = null !;
    public TableReference TableReference { get; set; } = null !;
}

public sealed partial class LikePredicate
{
    public string Id { get; set; } = null !;
    public BooleanExpression BooleanExpression { get; set; } = null !;
}

public sealed partial class LikePredicateFirstExpressionLink
{
    public string Id { get; set; } = null !;
    public LikePredicate LikePredicate { get; set; } = null !;
    public ScalarExpression ScalarExpression { get; set; } = null !;
}

public sealed partial class LikePredicateSecondExpressionLink
{
    public string Id { get; set; } = null !;
    public LikePredicate LikePredicate { get; set; } = null !;
    public ScalarExpression ScalarExpression { get; set; } = null !;
}

public sealed partial class Literal
{
    public string Id { get; set; } = null !;
    public string? Value { get; set; }
    public ValueExpression ValueExpression { get; set; } = null !;
}

public sealed partial class MultiPartIdentifier
{
    public string Id { get; set; } = null !;
}

public sealed partial class MultiPartIdentifierIdentifiersItem
{
    public string Id { get; set; } = null !;
    public string? Ordinal { get; set; }
    public Identifier Identifier { get; set; } = null !;
    public MultiPartIdentifier MultiPartIdentifier { get; set; } = null !;
}

public sealed partial class NamedTableReference
{
    public string Id { get; set; } = null !;
    public TableReferenceWithAlias TableReferenceWithAlias { get; set; } = null !;
}

public sealed partial class NamedTableReferenceSchemaObjectLink
{
    public string Id { get; set; } = null !;
    public NamedTableReference NamedTableReference { get; set; } = null !;
    public SchemaObjectName SchemaObjectName { get; set; } = null !;
}

public sealed partial class NullIfExpression
{
    public string Id { get; set; } = null !;
    public PrimaryExpression PrimaryExpression { get; set; } = null !;
}

public sealed partial class NullIfExpressionFirstExpressionLink
{
    public string Id { get; set; } = null !;
    public NullIfExpression NullIfExpression { get; set; } = null !;
    public ScalarExpression ScalarExpression { get; set; } = null !;
}

public sealed partial class NullIfExpressionSecondExpressionLink
{
    public string Id { get; set; } = null !;
    public NullIfExpression NullIfExpression { get; set; } = null !;
    public ScalarExpression ScalarExpression { get; set; } = null !;
}

public sealed partial class NullLiteral
{
    public string Id { get; set; } = null !;
    public Literal Literal { get; set; } = null !;
}

public sealed partial class OrderByClause
{
    public string Id { get; set; } = null !;
}

public sealed partial class OrderByClauseOrderByElementsItem
{
    public string Id { get; set; } = null !;
    public string? Ordinal { get; set; }
    public ExpressionWithSortOrder ExpressionWithSortOrder { get; set; } = null !;
    public OrderByClause OrderByClause { get; set; } = null !;
}

public sealed partial class OverClause
{
    public string Id { get; set; } = null !;
}

public sealed partial class OverClauseOrderByClauseLink
{
    public string Id { get; set; } = null !;
    public OrderByClause OrderByClause { get; set; } = null !;
    public OverClause OverClause { get; set; } = null !;
}

public sealed partial class OverClausePartitionsItem
{
    public string Id { get; set; } = null !;
    public string? Ordinal { get; set; }
    public OverClause OverClause { get; set; } = null !;
    public ScalarExpression ScalarExpression { get; set; } = null !;
}

public sealed partial class ParameterizedDataTypeReference
{
    public string Id { get; set; } = null !;
    public DataTypeReference DataTypeReference { get; set; } = null !;
}

public sealed partial class ParameterReferenceExpression
{
    public string Id { get; set; } = null !;
    public string Name { get; set; } = null !;
    public ValueExpression ValueExpression { get; set; } = null !;
}

public sealed partial class ParenthesisExpression
{
    public string Id { get; set; } = null !;
    public PrimaryExpression PrimaryExpression { get; set; } = null !;
}

public sealed partial class ParenthesisExpressionExpressionLink
{
    public string Id { get; set; } = null !;
    public ParenthesisExpression ParenthesisExpression { get; set; } = null !;
    public ScalarExpression ScalarExpression { get; set; } = null !;
}

public sealed partial class PrimaryExpression
{
    public string Id { get; set; } = null !;
    public ScalarExpression ScalarExpression { get; set; } = null !;
}

public sealed partial class QualifiedJoin
{
    public string Id { get; set; } = null !;
    public string? QualifiedJoinType { get; set; }
    public JoinTableReference JoinTableReference { get; set; } = null !;
}

public sealed partial class QualifiedJoinSearchConditionLink
{
    public string Id { get; set; } = null !;
    public BooleanExpression BooleanExpression { get; set; } = null !;
    public QualifiedJoin QualifiedJoin { get; set; } = null !;
}

public sealed partial class QueryDerivedTable
{
    public string Id { get; set; } = null !;
    public TableReferenceWithAliasAndColumns TableReferenceWithAliasAndColumns { get; set; } = null !;
}

public sealed partial class QueryDerivedTableQueryExpressionLink
{
    public string Id { get; set; } = null !;
    public QueryDerivedTable QueryDerivedTable { get; set; } = null !;
    public QueryExpression QueryExpression { get; set; } = null !;
}

public sealed partial class QueryExpression
{
    public string Id { get; set; } = null !;
}

public sealed partial class QueryParenthesisExpression
{
    public string Id { get; set; } = null !;
    public QueryExpression QueryExpression { get; set; } = null !;
}

public sealed partial class QueryParenthesisExpressionQueryExpressionLink
{
    public string Id { get; set; } = null !;
    public QueryExpression QueryExpression { get; set; } = null !;
    public QueryParenthesisExpression QueryParenthesisExpression { get; set; } = null !;
}

public sealed partial class QuerySpecification
{
    public string Id { get; set; } = null !;
    public string? UniqueRowFilter { get; set; }
    public QueryExpression QueryExpression { get; set; } = null !;
}

public sealed partial class QuerySpecificationFromClauseLink
{
    public string Id { get; set; } = null !;
    public FromClause FromClause { get; set; } = null !;
    public QuerySpecification QuerySpecification { get; set; } = null !;
}

public sealed partial class QuerySpecificationGroupByClauseLink
{
    public string Id { get; set; } = null !;
    public GroupByClause GroupByClause { get; set; } = null !;
    public QuerySpecification QuerySpecification { get; set; } = null !;
}

public sealed partial class QuerySpecificationSelectElementsItem
{
    public string Id { get; set; } = null !;
    public string? Ordinal { get; set; }
    public QuerySpecification QuerySpecification { get; set; } = null !;
    public SelectElement SelectElement { get; set; } = null !;
}

public sealed partial class QuerySpecificationWhereClauseLink
{
    public string Id { get; set; } = null !;
    public QuerySpecification QuerySpecification { get; set; } = null !;
    public WhereClause WhereClause { get; set; } = null !;
}

public sealed partial class RowValue
{
    public string Id { get; set; } = null !;
}

public sealed partial class RowValueColumnValuesItem
{
    public string Id { get; set; } = null !;
    public string? Ordinal { get; set; }
    public RowValue RowValue { get; set; } = null !;
    public ScalarExpression ScalarExpression { get; set; } = null !;
}

public sealed partial class ScalarExpression
{
    public string Id { get; set; } = null !;
}

public sealed partial class ScalarSubquery
{
    public string Id { get; set; } = null !;
    public PrimaryExpression PrimaryExpression { get; set; } = null !;
}

public sealed partial class ScalarSubqueryQueryExpressionLink
{
    public string Id { get; set; } = null !;
    public QueryExpression QueryExpression { get; set; } = null !;
    public ScalarSubquery ScalarSubquery { get; set; } = null !;
}

public sealed partial class SchemaObjectName
{
    public string Id { get; set; } = null !;
    public MultiPartIdentifier MultiPartIdentifier { get; set; } = null !;
}

public sealed partial class SchemaObjectNameBaseIdentifierLink
{
    public string Id { get; set; } = null !;
    public Identifier Identifier { get; set; } = null !;
    public SchemaObjectName SchemaObjectName { get; set; } = null !;
}

public sealed partial class SearchedCaseExpression
{
    public string Id { get; set; } = null !;
    public CaseExpression CaseExpression { get; set; } = null !;
}

public sealed partial class SearchedCaseExpressionWhenClausesItem
{
    public string Id { get; set; } = null !;
    public string? Ordinal { get; set; }
    public SearchedCaseExpression SearchedCaseExpression { get; set; } = null !;
    public SearchedWhenClause SearchedWhenClause { get; set; } = null !;
}

public sealed partial class SearchedWhenClause
{
    public string Id { get; set; } = null !;
    public WhenClause WhenClause { get; set; } = null !;
}

public sealed partial class SearchedWhenClauseWhenExpressionLink
{
    public string Id { get; set; } = null !;
    public BooleanExpression BooleanExpression { get; set; } = null !;
    public SearchedWhenClause SearchedWhenClause { get; set; } = null !;
}

public sealed partial class SelectElement
{
    public string Id { get; set; } = null !;
}

public sealed partial class SelectScalarExpression
{
    public string Id { get; set; } = null !;
    public SelectElement SelectElement { get; set; } = null !;
}

public sealed partial class SelectScalarExpressionColumnNameLink
{
    public string Id { get; set; } = null !;
    public IdentifierOrValueExpression IdentifierOrValueExpression { get; set; } = null !;
    public SelectScalarExpression SelectScalarExpression { get; set; } = null !;
}

public sealed partial class SelectScalarExpressionExpressionLink
{
    public string Id { get; set; } = null !;
    public ScalarExpression ScalarExpression { get; set; } = null !;
    public SelectScalarExpression SelectScalarExpression { get; set; } = null !;
}

public sealed partial class SelectStatement
{
    public string Id { get; set; } = null !;
    public StatementWithCtes StatementWithCtes { get; set; } = null !;
}

public sealed partial class SelectStatementQueryExpressionLink
{
    public string Id { get; set; } = null !;
    public QueryExpression QueryExpression { get; set; } = null !;
    public SelectStatement SelectStatement { get; set; } = null !;
}

public sealed partial class SqlDataTypeReference
{
    public string Id { get; set; } = null !;
    public string? SqlDataTypeOption { get; set; }
    public ParameterizedDataTypeReference ParameterizedDataTypeReference { get; set; } = null !;
}

public sealed partial class StatementWithCtes
{
    public string Id { get; set; } = null !;
    public TSqlStatement TSqlStatement { get; set; } = null !;
}

public sealed partial class StatementWithCtesWithCtesLink
{
    public string Id { get; set; } = null !;
    public StatementWithCtes StatementWithCtes { get; set; } = null !;
    public WithCtes WithCtes { get; set; } = null !;
}

public sealed partial class StringLiteral
{
    public string Id { get; set; } = null !;
    public Literal Literal { get; set; } = null !;
}

public sealed partial class TableReference
{
    public string Id { get; set; } = null !;
}

public sealed partial class TableReferenceWithAlias
{
    public string Id { get; set; } = null !;
    public TableReference TableReference { get; set; } = null !;
}

public sealed partial class TableReferenceWithAliasAliasLink
{
    public string Id { get; set; } = null !;
    public Identifier Identifier { get; set; } = null !;
    public TableReferenceWithAlias TableReferenceWithAlias { get; set; } = null !;
}

public sealed partial class TableReferenceWithAliasAndColumns
{
    public string Id { get; set; } = null !;
    public TableReferenceWithAlias TableReferenceWithAlias { get; set; } = null !;
}

public sealed partial class TableReferenceWithAliasAndColumnsColumnsItem
{
    public string Id { get; set; } = null !;
    public string? Ordinal { get; set; }
    public Identifier Identifier { get; set; } = null !;
    public TableReferenceWithAliasAndColumns TableReferenceWithAliasAndColumns { get; set; } = null !;
}

public sealed partial class Transformation
{
    public string Id { get; set; } = null !;
    public string TargetEntityName { get; set; } = null !;
    public Direction Direction { get; set; } = null !;
    public SelectStatement SelectStatement { get; set; } = null !;
}

public sealed partial class TryConvertCall
{
    public string Id { get; set; } = null !;
    public PrimaryExpression PrimaryExpression { get; set; } = null !;
}

public sealed partial class TryConvertCallDataTypeLink
{
    public string Id { get; set; } = null !;
    public DataTypeReference DataTypeReference { get; set; } = null !;
    public TryConvertCall TryConvertCall { get; set; } = null !;
}

public sealed partial class TryConvertCallParameterLink
{
    public string Id { get; set; } = null !;
    public ScalarExpression ScalarExpression { get; set; } = null !;
    public TryConvertCall TryConvertCall { get; set; } = null !;
}

public sealed partial class TSqlStatement
{
    public string Id { get; set; } = null !;
}

public sealed partial class UnqualifiedJoin
{
    public string Id { get; set; } = null !;
    public string? UnqualifiedJoinType { get; set; }
    public JoinTableReference JoinTableReference { get; set; } = null !;
}

public sealed partial class ValueExpression
{
    public string Id { get; set; } = null !;
    public PrimaryExpression PrimaryExpression { get; set; } = null !;
}

public sealed partial class Weave
{
    public string Id { get; set; } = null !;
}

public sealed partial class WhenClause
{
    public string Id { get; set; } = null !;
}

public sealed partial class WhenClauseThenExpressionLink
{
    public string Id { get; set; } = null !;
    public ScalarExpression ScalarExpression { get; set; } = null !;
    public WhenClause WhenClause { get; set; } = null !;
}

public sealed partial class WhereClause
{
    public string Id { get; set; } = null !;
}

public sealed partial class WhereClauseSearchConditionLink
{
    public string Id { get; set; } = null !;
    public BooleanExpression BooleanExpression { get; set; } = null !;
    public WhereClause WhereClause { get; set; } = null !;
}

public sealed partial class WithCtes
{
    public string Id { get; set; } = null !;
}

public sealed partial class WithCtesCommonTableExpressionsItem
{
    public string Id { get; set; } = null !;
    public string? Ordinal { get; set; }
    public CommonTableExpression CommonTableExpression { get; set; } = null !;
    public WithCtes WithCtes { get; set; } = null !;
}

public sealed partial class MetaWeaveModel
{
    public static MetaWeaveModel CreateEmpty() => new();
    public List<BinaryQueryExpression> BinaryQueryExpressionList { get; set; } = new();
    public List<BinaryQueryExpressionFirstQueryExpressionLink> BinaryQueryExpressionFirstQueryExpressionLinkList { get; set; } = new();
    public List<BinaryQueryExpressionSecondQueryExpressionLink> BinaryQueryExpressionSecondQueryExpressionLinkList { get; set; } = new();
    public List<BooleanBinaryExpression> BooleanBinaryExpressionList { get; set; } = new();
    public List<BooleanBinaryExpressionFirstExpressionLink> BooleanBinaryExpressionFirstExpressionLinkList { get; set; } = new();
    public List<BooleanBinaryExpressionSecondExpressionLink> BooleanBinaryExpressionSecondExpressionLinkList { get; set; } = new();
    public List<BooleanComparisonExpression> BooleanComparisonExpressionList { get; set; } = new();
    public List<BooleanComparisonExpressionFirstExpressionLink> BooleanComparisonExpressionFirstExpressionLinkList { get; set; } = new();
    public List<BooleanComparisonExpressionSecondExpressionLink> BooleanComparisonExpressionSecondExpressionLinkList { get; set; } = new();
    public List<BooleanExpression> BooleanExpressionList { get; set; } = new();
    public List<BooleanIsNullExpression> BooleanIsNullExpressionList { get; set; } = new();
    public List<BooleanIsNullExpressionExpressionLink> BooleanIsNullExpressionExpressionLinkList { get; set; } = new();
    public List<BooleanNotExpression> BooleanNotExpressionList { get; set; } = new();
    public List<BooleanNotExpressionExpressionLink> BooleanNotExpressionExpressionLinkList { get; set; } = new();
    public List<BooleanParenthesisExpression> BooleanParenthesisExpressionList { get; set; } = new();
    public List<BooleanParenthesisExpressionExpressionLink> BooleanParenthesisExpressionExpressionLinkList { get; set; } = new();
    public List<CaseExpression> CaseExpressionList { get; set; } = new();
    public List<CaseExpressionElseExpressionLink> CaseExpressionElseExpressionLinkList { get; set; } = new();
    public List<CoalesceExpression> CoalesceExpressionList { get; set; } = new();
    public List<CoalesceExpressionExpressionsItem> CoalesceExpressionExpressionsItemList { get; set; } = new();
    public List<ColumnReferenceExpression> ColumnReferenceExpressionList { get; set; } = new();
    public List<ColumnReferenceExpressionMultiPartIdentifierLink> ColumnReferenceExpressionMultiPartIdentifierLinkList { get; set; } = new();
    public List<CommonTableExpression> CommonTableExpressionList { get; set; } = new();
    public List<CommonTableExpressionExpressionNameLink> CommonTableExpressionExpressionNameLinkList { get; set; } = new();
    public List<CommonTableExpressionQueryExpressionLink> CommonTableExpressionQueryExpressionLinkList { get; set; } = new();
    public List<DataTypeReference> DataTypeReferenceList { get; set; } = new();
    public List<DataTypeReferenceNameLink> DataTypeReferenceNameLinkList { get; set; } = new();
    public List<Direction> DirectionList { get; set; } = new();
    public List<DirectionRelation> DirectionRelationList { get; set; } = new();
    public List<DirectionRequirement> DirectionRequirementList { get; set; } = new();
    public List<DirectionSourceWorkspace> DirectionSourceWorkspaceList { get; set; } = new();
    public List<DirectionStringParameter> DirectionStringParameterList { get; set; } = new();
    public List<ExistsPredicate> ExistsPredicateList { get; set; } = new();
    public List<ExistsPredicateSubqueryLink> ExistsPredicateSubqueryLinkList { get; set; } = new();
    public List<ExpressionGroupingSpecification> ExpressionGroupingSpecificationList { get; set; } = new();
    public List<ExpressionGroupingSpecificationExpressionLink> ExpressionGroupingSpecificationExpressionLinkList { get; set; } = new();
    public List<ExpressionWithSortOrder> ExpressionWithSortOrderList { get; set; } = new();
    public List<ExpressionWithSortOrderExpressionLink> ExpressionWithSortOrderExpressionLinkList { get; set; } = new();
    public List<FromClause> FromClauseList { get; set; } = new();
    public List<FromClauseTableReferencesItem> FromClauseTableReferencesItemList { get; set; } = new();
    public List<FunctionCall> FunctionCallList { get; set; } = new();
    public List<FunctionCallFunctionNameLink> FunctionCallFunctionNameLinkList { get; set; } = new();
    public List<FunctionCallOverClauseLink> FunctionCallOverClauseLinkList { get; set; } = new();
    public List<FunctionCallParametersItem> FunctionCallParametersItemList { get; set; } = new();
    public List<FunctionCallWithinGroupOrderByClauseLink> FunctionCallWithinGroupOrderByClauseLinkList { get; set; } = new();
    public List<GlobalFunctionTableReference> GlobalFunctionTableReferenceList { get; set; } = new();
    public List<GlobalFunctionTableReferenceNameLink> GlobalFunctionTableReferenceNameLinkList { get; set; } = new();
    public List<GlobalFunctionTableReferenceParametersItem> GlobalFunctionTableReferenceParametersItemList { get; set; } = new();
    public List<GroupByClause> GroupByClauseList { get; set; } = new();
    public List<GroupByClauseGroupingSpecificationsItem> GroupByClauseGroupingSpecificationsItemList { get; set; } = new();
    public List<GroupingSpecification> GroupingSpecificationList { get; set; } = new();
    public List<Identifier> IdentifierList { get; set; } = new();
    public List<IdentifierOrValueExpression> IdentifierOrValueExpressionList { get; set; } = new();
    public List<IdentifierOrValueExpressionIdentifierLink> IdentifierOrValueExpressionIdentifierLinkList { get; set; } = new();
    public List<IIfCall> IIfCallList { get; set; } = new();
    public List<IIfCallElseExpressionLink> IIfCallElseExpressionLinkList { get; set; } = new();
    public List<IIfCallPredicateLink> IIfCallPredicateLinkList { get; set; } = new();
    public List<IIfCallThenExpressionLink> IIfCallThenExpressionLinkList { get; set; } = new();
    public List<InlineDerivedTable> InlineDerivedTableList { get; set; } = new();
    public List<InlineDerivedTableRowValuesItem> InlineDerivedTableRowValuesItemList { get; set; } = new();
    public List<InPredicate> InPredicateList { get; set; } = new();
    public List<InPredicateExpressionLink> InPredicateExpressionLinkList { get; set; } = new();
    public List<InPredicateValuesItem> InPredicateValuesItemList { get; set; } = new();
    public List<IntegerLiteral> IntegerLiteralList { get; set; } = new();
    public List<JoinTableReference> JoinTableReferenceList { get; set; } = new();
    public List<JoinTableReferenceFirstTableReferenceLink> JoinTableReferenceFirstTableReferenceLinkList { get; set; } = new();
    public List<JoinTableReferenceSecondTableReferenceLink> JoinTableReferenceSecondTableReferenceLinkList { get; set; } = new();
    public List<LikePredicate> LikePredicateList { get; set; } = new();
    public List<LikePredicateFirstExpressionLink> LikePredicateFirstExpressionLinkList { get; set; } = new();
    public List<LikePredicateSecondExpressionLink> LikePredicateSecondExpressionLinkList { get; set; } = new();
    public List<Literal> LiteralList { get; set; } = new();
    public List<MultiPartIdentifier> MultiPartIdentifierList { get; set; } = new();
    public List<MultiPartIdentifierIdentifiersItem> MultiPartIdentifierIdentifiersItemList { get; set; } = new();
    public List<NamedTableReference> NamedTableReferenceList { get; set; } = new();
    public List<NamedTableReferenceSchemaObjectLink> NamedTableReferenceSchemaObjectLinkList { get; set; } = new();
    public List<NullIfExpression> NullIfExpressionList { get; set; } = new();
    public List<NullIfExpressionFirstExpressionLink> NullIfExpressionFirstExpressionLinkList { get; set; } = new();
    public List<NullIfExpressionSecondExpressionLink> NullIfExpressionSecondExpressionLinkList { get; set; } = new();
    public List<NullLiteral> NullLiteralList { get; set; } = new();
    public List<OrderByClause> OrderByClauseList { get; set; } = new();
    public List<OrderByClauseOrderByElementsItem> OrderByClauseOrderByElementsItemList { get; set; } = new();
    public List<OverClause> OverClauseList { get; set; } = new();
    public List<OverClauseOrderByClauseLink> OverClauseOrderByClauseLinkList { get; set; } = new();
    public List<OverClausePartitionsItem> OverClausePartitionsItemList { get; set; } = new();
    public List<ParameterizedDataTypeReference> ParameterizedDataTypeReferenceList { get; set; } = new();
    public List<ParameterReferenceExpression> ParameterReferenceExpressionList { get; set; } = new();
    public List<ParenthesisExpression> ParenthesisExpressionList { get; set; } = new();
    public List<ParenthesisExpressionExpressionLink> ParenthesisExpressionExpressionLinkList { get; set; } = new();
    public List<PrimaryExpression> PrimaryExpressionList { get; set; } = new();
    public List<QualifiedJoin> QualifiedJoinList { get; set; } = new();
    public List<QualifiedJoinSearchConditionLink> QualifiedJoinSearchConditionLinkList { get; set; } = new();
    public List<QueryDerivedTable> QueryDerivedTableList { get; set; } = new();
    public List<QueryDerivedTableQueryExpressionLink> QueryDerivedTableQueryExpressionLinkList { get; set; } = new();
    public List<QueryExpression> QueryExpressionList { get; set; } = new();
    public List<QueryParenthesisExpression> QueryParenthesisExpressionList { get; set; } = new();
    public List<QueryParenthesisExpressionQueryExpressionLink> QueryParenthesisExpressionQueryExpressionLinkList { get; set; } = new();
    public List<QuerySpecification> QuerySpecificationList { get; set; } = new();
    public List<QuerySpecificationFromClauseLink> QuerySpecificationFromClauseLinkList { get; set; } = new();
    public List<QuerySpecificationGroupByClauseLink> QuerySpecificationGroupByClauseLinkList { get; set; } = new();
    public List<QuerySpecificationSelectElementsItem> QuerySpecificationSelectElementsItemList { get; set; } = new();
    public List<QuerySpecificationWhereClauseLink> QuerySpecificationWhereClauseLinkList { get; set; } = new();
    public List<RowValue> RowValueList { get; set; } = new();
    public List<RowValueColumnValuesItem> RowValueColumnValuesItemList { get; set; } = new();
    public List<ScalarExpression> ScalarExpressionList { get; set; } = new();
    public List<ScalarSubquery> ScalarSubqueryList { get; set; } = new();
    public List<ScalarSubqueryQueryExpressionLink> ScalarSubqueryQueryExpressionLinkList { get; set; } = new();
    public List<SchemaObjectName> SchemaObjectNameList { get; set; } = new();
    public List<SchemaObjectNameBaseIdentifierLink> SchemaObjectNameBaseIdentifierLinkList { get; set; } = new();
    public List<SearchedCaseExpression> SearchedCaseExpressionList { get; set; } = new();
    public List<SearchedCaseExpressionWhenClausesItem> SearchedCaseExpressionWhenClausesItemList { get; set; } = new();
    public List<SearchedWhenClause> SearchedWhenClauseList { get; set; } = new();
    public List<SearchedWhenClauseWhenExpressionLink> SearchedWhenClauseWhenExpressionLinkList { get; set; } = new();
    public List<SelectElement> SelectElementList { get; set; } = new();
    public List<SelectScalarExpression> SelectScalarExpressionList { get; set; } = new();
    public List<SelectScalarExpressionColumnNameLink> SelectScalarExpressionColumnNameLinkList { get; set; } = new();
    public List<SelectScalarExpressionExpressionLink> SelectScalarExpressionExpressionLinkList { get; set; } = new();
    public List<SelectStatement> SelectStatementList { get; set; } = new();
    public List<SelectStatementQueryExpressionLink> SelectStatementQueryExpressionLinkList { get; set; } = new();
    public List<SqlDataTypeReference> SqlDataTypeReferenceList { get; set; } = new();
    public List<StatementWithCtes> StatementWithCtesList { get; set; } = new();
    public List<StatementWithCtesWithCtesLink> StatementWithCtesWithCtesLinkList { get; set; } = new();
    public List<StringLiteral> StringLiteralList { get; set; } = new();
    public List<TableReference> TableReferenceList { get; set; } = new();
    public List<TableReferenceWithAlias> TableReferenceWithAliasList { get; set; } = new();
    public List<TableReferenceWithAliasAliasLink> TableReferenceWithAliasAliasLinkList { get; set; } = new();
    public List<TableReferenceWithAliasAndColumns> TableReferenceWithAliasAndColumnsList { get; set; } = new();
    public List<TableReferenceWithAliasAndColumnsColumnsItem> TableReferenceWithAliasAndColumnsColumnsItemList { get; set; } = new();
    public List<Transformation> TransformationList { get; set; } = new();
    public List<TryConvertCall> TryConvertCallList { get; set; } = new();
    public List<TryConvertCallDataTypeLink> TryConvertCallDataTypeLinkList { get; set; } = new();
    public List<TryConvertCallParameterLink> TryConvertCallParameterLinkList { get; set; } = new();
    public List<TSqlStatement> TSqlStatementList { get; set; } = new();
    public List<UnqualifiedJoin> UnqualifiedJoinList { get; set; } = new();
    public List<ValueExpression> ValueExpressionList { get; set; } = new();
    public List<Weave> WeaveList { get; set; } = new();
    public List<WhenClause> WhenClauseList { get; set; } = new();
    public List<WhenClauseThenExpressionLink> WhenClauseThenExpressionLinkList { get; set; } = new();
    public List<WhereClause> WhereClauseList { get; set; } = new();
    public List<WhereClauseSearchConditionLink> WhereClauseSearchConditionLinkList { get; set; } = new();
    public List<WithCtes> WithCtesList { get; set; } = new();
    public List<WithCtesCommonTableExpressionsItem> WithCtesCommonTableExpressionsItemList { get; set; } = new();
}

public static partial class MetaWeaveInstance
{
    private static readonly MetaWeaveModel _builtIn = CreateBuiltIn();
    public static MetaWeaveModel BuiltIn => _builtIn;

    public static MetaWeaveModel CreateBuiltIn()
    {
        var model = MetaWeaveModel.CreateEmpty();
        return model;
    }
}