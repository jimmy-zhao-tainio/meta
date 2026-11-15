using MetaWeave;

namespace MetaWeaveScript.Sql;

internal sealed partial class MetaWeaveScriptSqlEmitter
{
    private string RenderFromClause(FromClause fromClause)
    {
        var tableReferences = GetOrderedItems(model.FromClauseTableReferencesItemList, static row => row.FromClause?.Id, static row => row.Ordinal, fromClause.Id)
            .Select(row => RenderTableReference(row.TableReference))
            .ToArray();
        return string.Join(", ", tableReferences);
    }

    private string RenderTableReference(TableReference tableReference)
    {
        var aliasBase = FindByBaseId(model.TableReferenceWithAliasList, static row => row.TableReference?.Id, tableReference.Id);
        var aliasAndColumnsBase = aliasBase is null ? null : FindByBaseId(model.TableReferenceWithAliasAndColumnsList, static row => row.TableReferenceWithAlias?.Id, aliasBase.Id);
        var namedTableReference = FindByBaseId(model.NamedTableReferenceList, static row => row.TableReferenceWithAlias?.Id, aliasBase?.Id ?? tableReference.Id);
        if (namedTableReference is not null)
        {
            var schemaObject = GetOwnerLink(model.NamedTableReferenceSchemaObjectLinkList, static row => row.NamedTableReference?.Id, namedTableReference.Id, "NamedTableReference.SchemaObject").SchemaObjectName;
            var rendered = RenderSchemaObjectName(schemaObject);

            var aliasLink = aliasBase is null ? null : FindOwnerLink(model.TableReferenceWithAliasAliasLinkList, static row => row.TableReferenceWithAlias?.Id, aliasBase.Id);
            if (aliasLink is not null)
            {
                rendered += " AS " + RenderIdentifier(aliasLink.Identifier);
            }

            return rendered;
        }

        var globalFunctionTableReference = aliasBase is null ? null : FindByBaseId(model.GlobalFunctionTableReferenceList, static row => row.TableReferenceWithAlias?.Id, aliasBase.Id);
        if (globalFunctionTableReference is not null)
        {
            var functionName = RenderIdentifier(GetOwnerLink(
                model.GlobalFunctionTableReferenceNameLinkList, static row => row.GlobalFunctionTableReference?.Id,
                globalFunctionTableReference.Id,
                "GlobalFunctionTableReference.Name").Identifier);
            var parameters = GetOrderedItems(model.GlobalFunctionTableReferenceParametersItemList, static row => row.GlobalFunctionTableReference?.Id, static row => row.Ordinal, globalFunctionTableReference.Id)
                .Select(row => RenderScalarExpression(row.ScalarExpression))
                .ToArray();

            var rendered = $"{functionName}({string.Join(", ", parameters)})";
            var aliasOwner = aliasBase
                ?? throw new InvalidOperationException($"GlobalFunctionTableReference '{globalFunctionTableReference.Id}' did not resolve to TableReferenceWithAlias.");
            var aliasLink = FindOwnerLink(model.TableReferenceWithAliasAliasLinkList, static row => row.TableReferenceWithAlias?.Id, aliasOwner.Id);
            if (aliasLink is not null)
            {
                rendered += " AS " + RenderIdentifier(aliasLink.Identifier);
            }

            return rendered;
        }

        var queryDerivedTable = aliasAndColumnsBase is null ? null : FindByBaseId(model.QueryDerivedTableList, static row => row.TableReferenceWithAliasAndColumns?.Id, aliasAndColumnsBase.Id);
        if (queryDerivedTable is not null)
        {
            var queryExpression = RenderQueryExpression(GetOwnerLink(
                model.QueryDerivedTableQueryExpressionLinkList, static row => row.QueryDerivedTable?.Id,
                queryDerivedTable.Id,
                "QueryDerivedTable.QueryExpression").QueryExpression);
            return $"({Environment.NewLine}{queryExpression}{Environment.NewLine}){RenderAliasAndColumns(aliasAndColumnsBase!)}";
        }

        var inlineDerivedTable = aliasAndColumnsBase is null ? null : FindByBaseId(model.InlineDerivedTableList, static row => row.TableReferenceWithAliasAndColumns?.Id, aliasAndColumnsBase.Id);
        if (inlineDerivedTable is not null)
        {
            var rowValues = GetOrderedItems(model.InlineDerivedTableRowValuesItemList, static row => row.InlineDerivedTable?.Id, static row => row.Ordinal, inlineDerivedTable.Id)
                .Select(row => RenderRowValue(row.RowValue))
                .ToArray();
            return $"({Environment.NewLine}VALUES{Environment.NewLine}    {string.Join("," + Environment.NewLine + "    ", rowValues)}{Environment.NewLine}){RenderAliasAndColumns(aliasAndColumnsBase!)}";
        }

        var joinTableReference = FindByBaseId(model.JoinTableReferenceList, static row => row.TableReference?.Id, tableReference.Id)
            ?? throw new InvalidOperationException($"Unsupported MetaWeaveScript TableReference id '{tableReference.Id}'.");

        var first = RenderTableReference(GetOwnerLink(model.JoinTableReferenceFirstTableReferenceLinkList, static row => row.JoinTableReference?.Id, joinTableReference.Id, "JoinTableReference.FirstTableReference").TableReference);
        var second = RenderTableReference(GetOwnerLink(model.JoinTableReferenceSecondTableReferenceLinkList, static row => row.JoinTableReference?.Id, joinTableReference.Id, "JoinTableReference.SecondTableReference").TableReference);

        var qualifiedJoin = FindByBaseId(model.QualifiedJoinList, static row => row.JoinTableReference?.Id, joinTableReference.Id);
        if (qualifiedJoin is not null)
        {
            var joinText = qualifiedJoin.QualifiedJoinType switch
            {
                "Inner" => "INNER JOIN",
                "LeftOuter" => "LEFT OUTER JOIN",
                _ => throw new InvalidOperationException($"Unsupported MetaWeaveScript QualifiedJoinType '{qualifiedJoin.QualifiedJoinType}'.")
            };
            var predicate = RenderBooleanExpression(GetOwnerLink(model.QualifiedJoinSearchConditionLinkList, static row => row.QualifiedJoin?.Id, qualifiedJoin.Id, "QualifiedJoin.SearchCondition").BooleanExpression);
            return $"{first}{Environment.NewLine}{joinText} {second}{Environment.NewLine}    ON {predicate}";
        }

        var unqualifiedJoin = FindByBaseId(model.UnqualifiedJoinList, static row => row.JoinTableReference?.Id, joinTableReference.Id);
        if (unqualifiedJoin is not null)
        {
            var joinText = unqualifiedJoin.UnqualifiedJoinType switch
            {
                "CrossJoin" => "CROSS JOIN",
                "CrossApply" => "CROSS APPLY",
                "OuterApply" => "OUTER APPLY",
                _ => throw new InvalidOperationException($"Unsupported MetaWeaveScript UnqualifiedJoinType '{unqualifiedJoin.UnqualifiedJoinType}'.")
            };
            return $"{first}{Environment.NewLine}{joinText} {second}";
        }

        throw new InvalidOperationException($"Unsupported MetaWeaveScript JoinTableReference id '{joinTableReference.Id}'.");
    }
}
