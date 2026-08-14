using MetaWeave;

namespace MetaWeaveScript.Sql;

internal sealed partial class MetaWeaveScriptSqlEmitter
{
    private string RenderFromClause(FromClause fromClause)
    {
        var tableReferences = GetOrderedItems(model.FromClauseTableReferencesItemList, fromClause.Id)
            .Select(row => RenderTableReference(row.TableReference))
            .ToArray();
        return string.Join(", ", tableReferences);
    }

    private string RenderTableReference(TableReference tableReference)
    {
        var aliasBase = FindByBaseId(model.TableReferenceWithAliasList, tableReference.Id);
        var aliasAndColumnsBase = aliasBase is null ? null : FindByBaseId(model.TableReferenceWithAliasAndColumnsList, aliasBase.Id);
        var namedTableReference = FindByBaseId(model.NamedTableReferenceList, aliasBase?.Id ?? tableReference.Id);
        if (namedTableReference is not null)
        {
            var schemaObject = GetOwnerLink(model.NamedTableReferenceSchemaObjectLinkList, namedTableReference.Id, "NamedTableReference.SchemaObject").SchemaObjectName;
            var rendered = RenderSchemaObjectName(schemaObject);

            var aliasLink = aliasBase is null ? null : FindOwnerLink(model.TableReferenceWithAliasAliasLinkList, aliasBase.Id);
            if (aliasLink is not null)
            {
                rendered += " AS " + RenderIdentifier(aliasLink.Identifier);
            }

            return rendered;
        }

        var globalFunctionTableReference = aliasBase is null ? null : FindByBaseId(model.GlobalFunctionTableReferenceList, aliasBase.Id);
        if (globalFunctionTableReference is not null)
        {
            var functionName = RenderIdentifier(GetOwnerLink(
                model.GlobalFunctionTableReferenceNameLinkList,
                globalFunctionTableReference.Id,
                "GlobalFunctionTableReference.Name").Identifier);
            var parameters = GetOrderedItems(model.GlobalFunctionTableReferenceParametersItemList, globalFunctionTableReference.Id)
                .Select(row => RenderScalarExpression(row.ScalarExpression))
                .ToArray();

            var rendered = $"{functionName}({string.Join(", ", parameters)})";
            var aliasOwner = aliasBase
                ?? throw new InvalidOperationException($"GlobalFunctionTableReference '{globalFunctionTableReference.Id}' did not resolve to TableReferenceWithAlias.");
            var aliasLink = FindOwnerLink(model.TableReferenceWithAliasAliasLinkList, aliasOwner.Id);
            if (aliasLink is not null)
            {
                rendered += " AS " + RenderIdentifier(aliasLink.Identifier);
            }

            return rendered;
        }

        var queryDerivedTable = aliasAndColumnsBase is null ? null : FindByBaseId(model.QueryDerivedTableList, aliasAndColumnsBase.Id);
        if (queryDerivedTable is not null)
        {
            var queryExpression = RenderQueryExpression(GetOwnerLink(
                model.QueryDerivedTableQueryExpressionLinkList,
                queryDerivedTable.Id,
                "QueryDerivedTable.QueryExpression").QueryExpression);
            return $"({Environment.NewLine}{queryExpression}{Environment.NewLine}){RenderAliasAndColumns(aliasAndColumnsBase!)}";
        }

        var inlineDerivedTable = aliasAndColumnsBase is null ? null : FindByBaseId(model.InlineDerivedTableList, aliasAndColumnsBase.Id);
        if (inlineDerivedTable is not null)
        {
            var rowValues = GetOrderedItems(model.InlineDerivedTableRowValuesItemList, inlineDerivedTable.Id)
                .Select(row => RenderRowValue(row.RowValue))
                .ToArray();
            return $"({Environment.NewLine}VALUES{Environment.NewLine}    {string.Join("," + Environment.NewLine + "    ", rowValues)}{Environment.NewLine}){RenderAliasAndColumns(aliasAndColumnsBase!)}";
        }

        var joinTableReference = FindByBaseId(model.JoinTableReferenceList, tableReference.Id)
            ?? throw new InvalidOperationException($"Unsupported MetaWeaveScript TableReference id '{tableReference.Id}'.");

        var first = RenderTableReference(GetOwnerLink(model.JoinTableReferenceFirstTableReferenceLinkList, joinTableReference.Id, "JoinTableReference.FirstTableReference").TableReference);
        var second = RenderTableReference(GetOwnerLink(model.JoinTableReferenceSecondTableReferenceLinkList, joinTableReference.Id, "JoinTableReference.SecondTableReference").TableReference);

        var qualifiedJoin = FindByBaseId(model.QualifiedJoinList, joinTableReference.Id);
        if (qualifiedJoin is not null)
        {
            var joinText = qualifiedJoin.QualifiedJoinType switch
            {
                "Inner" => "INNER JOIN",
                "LeftOuter" => "LEFT OUTER JOIN",
                _ => throw new InvalidOperationException($"Unsupported MetaWeaveScript QualifiedJoinType '{qualifiedJoin.QualifiedJoinType}'.")
            };
            var predicate = RenderBooleanExpression(GetOwnerLink(model.QualifiedJoinSearchConditionLinkList, qualifiedJoin.Id, "QualifiedJoin.SearchCondition").BooleanExpression);
            return $"{first}{Environment.NewLine}{joinText} {second}{Environment.NewLine}    ON {predicate}";
        }

        var unqualifiedJoin = FindByBaseId(model.UnqualifiedJoinList, joinTableReference.Id);
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
