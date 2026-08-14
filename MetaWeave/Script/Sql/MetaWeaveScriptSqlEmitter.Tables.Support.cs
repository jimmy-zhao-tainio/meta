using MetaWeave;

namespace MetaWeaveScript.Sql;

internal sealed partial class MetaWeaveScriptSqlEmitter
{
    private string RenderSelectElement(SelectElement selectElement)
    {
        var selectScalarExpression = FindByBaseId(model.SelectScalarExpressionList, selectElement.Id)
            ?? throw new InvalidOperationException($"Unsupported MetaWeaveScript SelectElement id '{selectElement.Id}'.");

        var expression = RenderScalarExpression(GetOwnerLink(
            model.SelectScalarExpressionExpressionLinkList,
            selectScalarExpression.Id,
            "SelectScalarExpression.Expression").ScalarExpression);

        var columnNameLink = FindOwnerLink(model.SelectScalarExpressionColumnNameLinkList, selectScalarExpression.Id);
        if (columnNameLink is not null)
        {
            return $"{expression} AS {RenderIdentifierOrValueExpression(columnNameLink.IdentifierOrValueExpression)}";
        }

        return expression;
    }

    private string RenderRowValue(RowValue rowValue)
    {
        var values = GetOrderedItems(model.RowValueColumnValuesItemList, rowValue.Id)
            .Select(row => RenderScalarExpression(row.ScalarExpression))
            .ToArray();
        return "(" + string.Join(", ", values) + ")";
    }

    private string RenderSchemaObjectName(SchemaObjectName schemaObjectName)
    {
        var multiPartIdentifier = GetById(model.MultiPartIdentifierList, schemaObjectName.MultiPartIdentifier.Id, "SchemaObjectName.Base");
        return RenderMultiPartIdentifier(multiPartIdentifier);
    }

    private string RenderIdentifierOrValueExpression(IdentifierOrValueExpression value)
    {
        var identifierLink = FindOwnerLink(model.IdentifierOrValueExpressionIdentifierLinkList, value.Id);
        if (identifierLink is not null)
        {
            return RenderIdentifier(identifierLink.Identifier);
        }

        throw new InvalidOperationException($"IdentifierOrValueExpression '{value.Id}' was empty.");
    }

    private string RenderMultiPartIdentifier(MultiPartIdentifier multiPartIdentifier)
    {
        var parts = GetOrderedItems(model.MultiPartIdentifierIdentifiersItemList, multiPartIdentifier.Id)
            .Select(row => RenderIdentifier(row.Identifier))
            .ToArray();
        return string.Join(".", parts);
    }

    private string RenderAliasAndColumns(TableReferenceWithAliasAndColumns aliasAndColumns)
    {
        return RenderAliasAndColumns(aliasAndColumns, requireAlias: true);
    }

    private string RenderAliasAndColumns(TableReferenceWithAliasAndColumns aliasAndColumns, bool requireAlias)
    {
        var aliasBase = GetById(model.TableReferenceWithAliasList, aliasAndColumns.TableReferenceWithAlias.Id, "TableReferenceWithAliasAndColumns.Base");
        var aliasLink = FindOwnerLink(model.TableReferenceWithAliasAliasLinkList, aliasBase.Id);
        if (aliasLink is null)
        {
            if (requireAlias)
            {
                throw new InvalidOperationException(
                    $"TableReferenceWithAliasAndColumns '{aliasAndColumns.Id}' was missing an alias.");
            }

            var orphanColumns = GetOrderedItems(model.TableReferenceWithAliasAndColumnsColumnsItemList, aliasAndColumns.Id);
            if (orphanColumns.Any())
            {
                throw new InvalidOperationException(
                    $"TableReferenceWithAliasAndColumns '{aliasAndColumns.Id}' contained column aliases without a table alias.");
            }

            return string.Empty;
        }

        var columns = GetOrderedItems(model.TableReferenceWithAliasAndColumnsColumnsItemList, aliasAndColumns.Id)
            .Select(row => RenderIdentifier(row.Identifier))
            .ToArray();

        return columns.Length == 0
            ? " AS " + RenderIdentifier(aliasLink.Identifier)
            : " AS " + RenderIdentifier(aliasLink.Identifier) + "(" + string.Join(", ", columns) + ")";
    }
}
