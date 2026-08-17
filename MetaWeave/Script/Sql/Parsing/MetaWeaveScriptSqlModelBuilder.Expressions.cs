using System.Globalization;
using MetaWeave;

namespace MetaWeaveScript.Sql.Parsing;

internal sealed partial class MetaWeaveScriptSqlModelBuilder
{
    public BuiltNode CreateColumnReferenceExpression(BuiltNode multiPartIdentifier, string columnType = "Regular")
    {
        var scalar = new ScalarExpression
        {
            Id = NextId(nameof(ScalarExpression))
        };
        model.ScalarExpressionList.Add(scalar);

        var primary = new PrimaryExpression
        {
            Id = NextId(nameof(PrimaryExpression)),
            ScalarExpression = scalar
        };
        model.PrimaryExpressionList.Add(primary);

        var columnReference = new ColumnReferenceExpression
        {
            Id = NextId(nameof(ColumnReferenceExpression)),
            PrimaryExpression = primary,
            ColumnType = columnType
        };
        model.ColumnReferenceExpressionList.Add(columnReference);
        model.ColumnReferenceExpressionMultiPartIdentifierLinkList.Add(new ColumnReferenceExpressionMultiPartIdentifierLink
        {
            Id = NextId(nameof(ColumnReferenceExpressionMultiPartIdentifierLink)),
            ColumnReferenceExpression = columnReference,
            MultiPartIdentifier = multiPartIdentifier.GetRef<MultiPartIdentifier>(nameof(MultiPartIdentifier))
        });

        return BuiltNode.Create(
            (nameof(ScalarExpression), scalar.Id),
            (nameof(PrimaryExpression), primary.Id),
            (nameof(ColumnReferenceExpression), columnReference.Id));
    }

    public BuiltNode CreateWildcardColumnReferenceExpression()
    {
        var scalar = new ScalarExpression
        {
            Id = NextId(nameof(ScalarExpression))
        };
        model.ScalarExpressionList.Add(scalar);

        var primary = new PrimaryExpression
        {
            Id = NextId(nameof(PrimaryExpression)),
            ScalarExpression = scalar
        };
        model.PrimaryExpressionList.Add(primary);

        var columnReference = new ColumnReferenceExpression
        {
            Id = NextId(nameof(ColumnReferenceExpression)),
            PrimaryExpression = primary,
            ColumnType = "Wildcard"
        };
        model.ColumnReferenceExpressionList.Add(columnReference);

        return BuiltNode.Create(
            (nameof(ScalarExpression), scalar.Id),
            (nameof(PrimaryExpression), primary.Id),
            (nameof(ColumnReferenceExpression), columnReference.Id));
    }

    public BuiltNode CreateParameterReferenceExpression(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var scalar = new ScalarExpression
        {
            Id = NextId(nameof(ScalarExpression))
        };
        model.ScalarExpressionList.Add(scalar);

        var primary = new PrimaryExpression
        {
            Id = NextId(nameof(PrimaryExpression)),
            ScalarExpression = scalar
        };
        model.PrimaryExpressionList.Add(primary);

        var value = new ValueExpression
        {
            Id = NextId(nameof(ValueExpression)),
            PrimaryExpression = primary
        };
        model.ValueExpressionList.Add(value);

        var parameter = new ParameterReferenceExpression
        {
            Id = NextId(nameof(ParameterReferenceExpression)),
            Name = name,
            ValueExpression = value
        };
        model.ParameterReferenceExpressionList.Add(parameter);

        return BuiltNode.Create(
            (nameof(ScalarExpression), scalar.Id),
            (nameof(PrimaryExpression), primary.Id),
            (nameof(ValueExpression), value.Id),
            (nameof(ParameterReferenceExpression), parameter.Id));
    }

    public BuiltNode CreateParenthesisExpression(BuiltNode expression)
    {
        var scalar = new ScalarExpression
        {
            Id = NextId(nameof(ScalarExpression))
        };
        model.ScalarExpressionList.Add(scalar);

        var primary = new PrimaryExpression
        {
            Id = NextId(nameof(PrimaryExpression)),
            ScalarExpression = scalar
        };
        model.PrimaryExpressionList.Add(primary);

        var parenthesisExpression = new ParenthesisExpression
        {
            Id = NextId(nameof(ParenthesisExpression)),
            PrimaryExpression = primary
        };
        model.ParenthesisExpressionList.Add(parenthesisExpression);
        model.ParenthesisExpressionExpressionLinkList.Add(new ParenthesisExpressionExpressionLink
        {
            Id = NextId(nameof(ParenthesisExpressionExpressionLink)),
            ParenthesisExpression = parenthesisExpression,
            ScalarExpression = expression.GetRef<ScalarExpression>(nameof(ScalarExpression))
        });

        return BuiltNode.Create(
            (nameof(ScalarExpression), scalar.Id),
            (nameof(PrimaryExpression), primary.Id),
            (nameof(ParenthesisExpression), parenthesisExpression.Id));
    }

    public BuiltNode CreateFunctionCall(BuiltNode functionName, IReadOnlyList<BuiltNode> parameters, string? uniqueRowFilter = null)
    {
        if (!string.IsNullOrEmpty(uniqueRowFilter))
        {
            throw new InvalidOperationException("MetaWeaveScript function calls do not support DISTINCT.");
        }

        var scalar = new ScalarExpression
        {
            Id = NextId(nameof(ScalarExpression))
        };
        model.ScalarExpressionList.Add(scalar);

        var primary = new PrimaryExpression
        {
            Id = NextId(nameof(PrimaryExpression)),
            ScalarExpression = scalar
        };
        model.PrimaryExpressionList.Add(primary);

        var functionCall = new FunctionCall
        {
            Id = NextId(nameof(FunctionCall)),
            PrimaryExpression = primary
        };
        model.FunctionCallList.Add(functionCall);
        model.FunctionCallFunctionNameLinkList.Add(new FunctionCallFunctionNameLink
        {
            Id = NextId(nameof(FunctionCallFunctionNameLink)),
            FunctionCall = functionCall,
            Identifier = functionName.GetRef<Identifier>(nameof(Identifier))
        });

        for (var ordinal = 0; ordinal < parameters.Count; ordinal++)
        {
            model.FunctionCallParametersItemList.Add(new FunctionCallParametersItem
            {
                Id = NextId(nameof(FunctionCallParametersItem)),
                FunctionCall = functionCall,
                ScalarExpression = parameters[ordinal].GetRef<ScalarExpression>(nameof(ScalarExpression)),
                Ordinal = ordinal.ToString(CultureInfo.InvariantCulture)
            });
        }

        return BuiltNode.Create(
            (nameof(ScalarExpression), scalar.Id),
            (nameof(PrimaryExpression), primary.Id),
            (nameof(FunctionCall), functionCall.Id));
    }

    public BuiltNode CreateScalarSubquery(BuiltNode queryExpression)
    {
        var scalar = new ScalarExpression
        {
            Id = NextId(nameof(ScalarExpression))
        };
        model.ScalarExpressionList.Add(scalar);

        var primary = new PrimaryExpression
        {
            Id = NextId(nameof(PrimaryExpression)),
            ScalarExpression = scalar
        };
        model.PrimaryExpressionList.Add(primary);

        var scalarSubquery = new ScalarSubquery
        {
            Id = NextId(nameof(ScalarSubquery)),
            PrimaryExpression = primary
        };
        model.ScalarSubqueryList.Add(scalarSubquery);
        model.ScalarSubqueryQueryExpressionLinkList.Add(new ScalarSubqueryQueryExpressionLink
        {
            Id = NextId(nameof(ScalarSubqueryQueryExpressionLink)),
            ScalarSubquery = scalarSubquery,
            QueryExpression = queryExpression.GetRef<QueryExpression>(nameof(QueryExpression))
        });

        return BuiltNode.Create(
            (nameof(ScalarExpression), scalar.Id),
            (nameof(PrimaryExpression), primary.Id),
            (nameof(ScalarSubquery), scalarSubquery.Id));
    }

    public BuiltNode CreateSearchedCaseExpression(
        IReadOnlyList<(BuiltNode WhenExpression, BuiltNode ThenExpression)> whenClauses,
        BuiltNode? elseExpression)
    {
        var scalar = new ScalarExpression
        {
            Id = NextId(nameof(ScalarExpression))
        };
        model.ScalarExpressionList.Add(scalar);

        var primary = new PrimaryExpression
        {
            Id = NextId(nameof(PrimaryExpression)),
            ScalarExpression = scalar
        };
        model.PrimaryExpressionList.Add(primary);

        var caseExpression = new CaseExpression
        {
            Id = NextId(nameof(CaseExpression)),
            PrimaryExpression = primary
        };
        model.CaseExpressionList.Add(caseExpression);

        var searchedCaseExpression = new SearchedCaseExpression
        {
            Id = NextId(nameof(SearchedCaseExpression)),
            CaseExpression = caseExpression
        };
        model.SearchedCaseExpressionList.Add(searchedCaseExpression);

        for (var ordinal = 0; ordinal < whenClauses.Count; ordinal++)
        {
            var whenClause = new WhenClause
            {
                Id = NextId(nameof(WhenClause))
            };
            model.WhenClauseList.Add(whenClause);

            var searchedWhenClause = new SearchedWhenClause
            {
                Id = NextId(nameof(SearchedWhenClause)),
                WhenClause = whenClause
            };
            model.SearchedWhenClauseList.Add(searchedWhenClause);

            model.SearchedCaseExpressionWhenClausesItemList.Add(new SearchedCaseExpressionWhenClausesItem
            {
                Id = NextId(nameof(SearchedCaseExpressionWhenClausesItem)),
                SearchedCaseExpression = searchedCaseExpression,
                SearchedWhenClause = searchedWhenClause,
                Ordinal = ordinal.ToString(CultureInfo.InvariantCulture)
            });
            model.SearchedWhenClauseWhenExpressionLinkList.Add(new SearchedWhenClauseWhenExpressionLink
            {
                Id = NextId(nameof(SearchedWhenClauseWhenExpressionLink)),
                SearchedWhenClause = searchedWhenClause,
                BooleanExpression = whenClauses[ordinal].WhenExpression.GetRef<BooleanExpression>(nameof(BooleanExpression))
            });
            model.WhenClauseThenExpressionLinkList.Add(new WhenClauseThenExpressionLink
            {
                Id = NextId(nameof(WhenClauseThenExpressionLink)),
                WhenClause = whenClause,
                ScalarExpression = whenClauses[ordinal].ThenExpression.GetRef<ScalarExpression>(nameof(ScalarExpression))
            });
        }

        if (elseExpression is not null)
        {
            model.CaseExpressionElseExpressionLinkList.Add(new CaseExpressionElseExpressionLink
            {
                Id = NextId(nameof(CaseExpressionElseExpressionLink)),
                CaseExpression = caseExpression,
                ScalarExpression = elseExpression.GetRef<ScalarExpression>(nameof(ScalarExpression))
            });
        }

        return BuiltNode.Create(
            (nameof(ScalarExpression), scalar.Id),
            (nameof(PrimaryExpression), primary.Id),
            (nameof(CaseExpression), caseExpression.Id),
            (nameof(SearchedCaseExpression), searchedCaseExpression.Id));
    }
}
