using System.Globalization;
using MetaWeave;

namespace MetaWeaveScript.Sql.Parsing;

internal sealed partial class MetaWeaveScriptSqlModelBuilder
{
    public BuiltNode CreateCommonTableExpression(
        BuiltNode expressionName,
        BuiltNode queryExpression)
    {
        var commonTableExpression = new CommonTableExpression
        {
            Id = NextId(nameof(CommonTableExpression))
        };
        model.CommonTableExpressionList.Add(commonTableExpression);
        model.CommonTableExpressionExpressionNameLinkList.Add(new CommonTableExpressionExpressionNameLink
        {
            Id = NextId(nameof(CommonTableExpressionExpressionNameLink)),
            CommonTableExpression = commonTableExpression,
            Identifier = expressionName.GetRef<Identifier>(nameof(Identifier))
        });
        model.CommonTableExpressionQueryExpressionLinkList.Add(new CommonTableExpressionQueryExpressionLink
        {
            Id = NextId(nameof(CommonTableExpressionQueryExpressionLink)),
            CommonTableExpression = commonTableExpression,
            QueryExpression = queryExpression.GetRef<QueryExpression>(nameof(QueryExpression))
        });

        return BuiltNode.Create((nameof(CommonTableExpression), commonTableExpression.Id));
    }

    public BuiltNode CreateSelectStatement(
        BuiltNode queryExpression,
        IReadOnlyList<BuiltNode>? commonTableExpressions = null)
    {
        var statementBase = CreateStatementWithCtes(commonTableExpressions);
        var statementWithCtesId = statementBase.GetId(nameof(StatementWithCtes));

        var selectStatement = new SelectStatement
        {
            Id = NextId(nameof(SelectStatement)),
            StatementWithCtes = (StatementWithCtes)ResolveBuiltNodeReference(
                nameof(StatementWithCtes),
                statementWithCtesId)
        };
        model.SelectStatementList.Add(selectStatement);
        model.SelectStatementQueryExpressionLinkList.Add(new SelectStatementQueryExpressionLink
        {
            Id = NextId(nameof(SelectStatementQueryExpressionLink)),
            SelectStatement = selectStatement,
            QueryExpression = queryExpression.GetRef<QueryExpression>(nameof(QueryExpression))
        });

        return BuiltNode.Create(
            (nameof(TSqlStatement), statementBase.GetId(nameof(TSqlStatement))),
            (nameof(StatementWithCtes), statementWithCtesId),
            (nameof(SelectStatement), selectStatement.Id));
    }

    public BuiltNode CreateBinaryQueryExpression(
        BuiltNode firstQueryExpression,
        BuiltNode secondQueryExpression,
        string binaryQueryExpressionType,
        bool all)
    {
        if (!string.Equals(binaryQueryExpressionType, "Union", StringComparison.Ordinal) || !all)
        {
            throw new InvalidOperationException("MetaWeaveScript binary queries are UNION ALL.");
        }

        var queryExpression = new QueryExpression
        {
            Id = NextId(nameof(QueryExpression))
        };
        model.QueryExpressionList.Add(queryExpression);

        var binaryQueryExpression = new BinaryQueryExpression
        {
            Id = NextId(nameof(BinaryQueryExpression)),
            QueryExpression = queryExpression
        };
        model.BinaryQueryExpressionList.Add(binaryQueryExpression);
        model.BinaryQueryExpressionFirstQueryExpressionLinkList.Add(new BinaryQueryExpressionFirstQueryExpressionLink
        {
            Id = NextId(nameof(BinaryQueryExpressionFirstQueryExpressionLink)),
            BinaryQueryExpression = binaryQueryExpression,
            QueryExpression = firstQueryExpression.GetRef<QueryExpression>(nameof(QueryExpression))
        });
        model.BinaryQueryExpressionSecondQueryExpressionLinkList.Add(new BinaryQueryExpressionSecondQueryExpressionLink
        {
            Id = NextId(nameof(BinaryQueryExpressionSecondQueryExpressionLink)),
            BinaryQueryExpression = binaryQueryExpression,
            QueryExpression = secondQueryExpression.GetRef<QueryExpression>(nameof(QueryExpression))
        });

        return BuiltNode.Create(
            (nameof(QueryExpression), queryExpression.Id),
            (nameof(BinaryQueryExpression), binaryQueryExpression.Id));
    }

    public BuiltNode CreateQueryParenthesisExpression(BuiltNode queryExpression)
    {
        var parent = new QueryExpression
        {
            Id = NextId(nameof(QueryExpression))
        };
        model.QueryExpressionList.Add(parent);

        var queryParenthesisExpression = new QueryParenthesisExpression
        {
            Id = NextId(nameof(QueryParenthesisExpression)),
            QueryExpression = parent
        };
        model.QueryParenthesisExpressionList.Add(queryParenthesisExpression);
        model.QueryParenthesisExpressionQueryExpressionLinkList.Add(new QueryParenthesisExpressionQueryExpressionLink
        {
            Id = NextId(nameof(QueryParenthesisExpressionQueryExpressionLink)),
            QueryParenthesisExpression = queryParenthesisExpression,
            QueryExpression = queryExpression.GetRef<QueryExpression>(nameof(QueryExpression))
        });

        return BuiltNode.Create(
            (nameof(QueryExpression), parent.Id),
            (nameof(QueryParenthesisExpression), queryParenthesisExpression.Id));
    }

    private BuiltNode CreateStatementWithCtes(
        IReadOnlyList<BuiltNode>? commonTableExpressions)
    {
        var sqlStatement = new TSqlStatement
        {
            Id = NextId(nameof(TSqlStatement))
        };
        model.TSqlStatementList.Add(sqlStatement);

        var statementWithCtes = new StatementWithCtes
        {
            Id = NextId(nameof(StatementWithCtes)),
            TSqlStatement = sqlStatement
        };
        model.StatementWithCtesList.Add(statementWithCtes);

        if (commonTableExpressions is not null && commonTableExpressions.Count > 0)
        {
            var withCtes = new WithCtes
            {
                Id = NextId(nameof(WithCtes))
            };
            model.WithCtesList.Add(withCtes);
            model.StatementWithCtesWithCtesLinkList.Add(new StatementWithCtesWithCtesLink
            {
                Id = NextId(nameof(StatementWithCtesWithCtesLink)),
                StatementWithCtes = statementWithCtes,
                WithCtes = withCtes
            });

            for (var ordinal = 0; ordinal < commonTableExpressions.Count; ordinal++)
            {
                model.WithCtesCommonTableExpressionsItemList.Add(new WithCtesCommonTableExpressionsItem
                {
                    Id = NextId(nameof(WithCtesCommonTableExpressionsItem)),
                    WithCtes = withCtes,
                    CommonTableExpression = commonTableExpressions[ordinal]
                        .GetRef<CommonTableExpression>(nameof(CommonTableExpression)),
                    Ordinal = ordinal.ToString(CultureInfo.InvariantCulture)
                });
            }
        }

        return BuiltNode.Create(
            (nameof(TSqlStatement), sqlStatement.Id),
            (nameof(StatementWithCtes), statementWithCtes.Id));
    }
}
