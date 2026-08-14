using System.Globalization;
using MetaWeave;

namespace MetaWeaveScript.Sql.Parsing;

internal sealed partial class MetaWeaveScriptSqlModelBuilder
{
    public BuiltNode CreateNamedTableReference(
        BuiltNode schemaObjectName,
        BuiltNode? alias = null,
        BuiltNode? tableSampleClause = null,
        IReadOnlyList<BuiltNode>? tableHints = null)
    {
        if (tableSampleClause is not null || tableHints is { Count: > 0 })
        {
            throw new InvalidOperationException("MetaWeaveScript table references do not support TABLESAMPLE or table hints.");
        }

        var tableReference = new TableReference { Id = NextId(nameof(TableReference)) };
        model.TableReferenceList.Add(tableReference);
        var aliasBase = new TableReferenceWithAlias
        {
            Id = NextId(nameof(TableReferenceWithAlias)),
            TableReference = tableReference
        };
        model.TableReferenceWithAliasList.Add(aliasBase);
        var named = new NamedTableReference
        {
            Id = NextId(nameof(NamedTableReference)),
            TableReferenceWithAlias = aliasBase
        };
        model.NamedTableReferenceList.Add(named);
        model.NamedTableReferenceSchemaObjectLinkList.Add(new NamedTableReferenceSchemaObjectLink
        {
            Id = NextId(nameof(NamedTableReferenceSchemaObjectLink)),
            NamedTableReference = named,
            SchemaObjectName = schemaObjectName.GetRef<SchemaObjectName>(nameof(SchemaObjectName))
        });
        if (alias is not null)
        {
            model.TableReferenceWithAliasAliasLinkList.Add(new TableReferenceWithAliasAliasLink
            {
                Id = NextId(nameof(TableReferenceWithAliasAliasLink)),
                TableReferenceWithAlias = aliasBase,
                Identifier = alias.GetRef<Identifier>(nameof(Identifier))
            });
        }
        return BuiltNode.Create(
            (nameof(TableReference), tableReference.Id),
            (nameof(TableReferenceWithAlias), aliasBase.Id),
            (nameof(NamedTableReference), named.Id));
    }

    public BuiltNode CreateGlobalFunctionTableReference(BuiltNode functionName, IReadOnlyList<BuiltNode> parameters, BuiltNode? alias = null)
    {
        var tableReference = new TableReference { Id = NextId(nameof(TableReference)) };
        model.TableReferenceList.Add(tableReference);
        var aliasBase = new TableReferenceWithAlias
        {
            Id = NextId(nameof(TableReferenceWithAlias)),
            TableReference = tableReference
        };
        model.TableReferenceWithAliasList.Add(aliasBase);
        if (alias is not null)
        {
            model.TableReferenceWithAliasAliasLinkList.Add(new TableReferenceWithAliasAliasLink
            {
                Id = NextId(nameof(TableReferenceWithAliasAliasLink)),
                TableReferenceWithAlias = aliasBase,
                Identifier = alias.GetRef<Identifier>(nameof(Identifier))
            });
        }
        var functionReference = new GlobalFunctionTableReference
        {
            Id = NextId(nameof(GlobalFunctionTableReference)),
            TableReferenceWithAlias = aliasBase
        };
        model.GlobalFunctionTableReferenceList.Add(functionReference);
        model.GlobalFunctionTableReferenceNameLinkList.Add(new GlobalFunctionTableReferenceNameLink
        {
            Id = NextId(nameof(GlobalFunctionTableReferenceNameLink)),
            GlobalFunctionTableReference = functionReference,
            Identifier = functionName.GetRef<Identifier>(nameof(Identifier))
        });
        for (var ordinal = 0; ordinal < parameters.Count; ordinal++)
        {
            model.GlobalFunctionTableReferenceParametersItemList.Add(new GlobalFunctionTableReferenceParametersItem
            {
                Id = NextId(nameof(GlobalFunctionTableReferenceParametersItem)),
                GlobalFunctionTableReference = functionReference,
                ScalarExpression = parameters[ordinal].GetRef<ScalarExpression>(nameof(ScalarExpression)),
                Ordinal = ordinal.ToString(CultureInfo.InvariantCulture)
            });
        }
        return BuiltNode.Create(
            (nameof(TableReference), tableReference.Id),
            (nameof(TableReferenceWithAlias), aliasBase.Id),
            (nameof(GlobalFunctionTableReference), functionReference.Id));
    }

    public BuiltNode CreateQueryDerivedTable(BuiltNode queryExpression, BuiltNode alias, IReadOnlyList<BuiltNode>? columns = null)
    {
        var tableReference = new TableReference
        {
            Id = NextId(nameof(TableReference))
        };
        model.TableReferenceList.Add(tableReference);

        var aliasBase = new TableReferenceWithAlias
        {
            Id = NextId(nameof(TableReferenceWithAlias)),
            TableReference = tableReference
        };
        model.TableReferenceWithAliasList.Add(aliasBase);
        model.TableReferenceWithAliasAliasLinkList.Add(new TableReferenceWithAliasAliasLink
        {
            Id = NextId(nameof(TableReferenceWithAliasAliasLink)),
            TableReferenceWithAlias = aliasBase,
            Identifier = alias.GetRef<Identifier>(nameof(Identifier))
        });

        var aliasAndColumns = new TableReferenceWithAliasAndColumns
        {
            Id = NextId(nameof(TableReferenceWithAliasAndColumns)),
            TableReferenceWithAlias = aliasBase
        };
        model.TableReferenceWithAliasAndColumnsList.Add(aliasAndColumns);

        if (columns is not null)
        {
            for (var ordinal = 0; ordinal < columns.Count; ordinal++)
            {
                model.TableReferenceWithAliasAndColumnsColumnsItemList.Add(new TableReferenceWithAliasAndColumnsColumnsItem
                {
                    Id = NextId(nameof(TableReferenceWithAliasAndColumnsColumnsItem)),
                    TableReferenceWithAliasAndColumns = aliasAndColumns,
                    Identifier = columns[ordinal].GetRef<Identifier>(nameof(Identifier)),
                    Ordinal = ordinal.ToString(CultureInfo.InvariantCulture)
                });
            }
        }

        var queryDerivedTable = new QueryDerivedTable
        {
            Id = NextId(nameof(QueryDerivedTable)),
            TableReferenceWithAliasAndColumns = aliasAndColumns
        };
        model.QueryDerivedTableList.Add(queryDerivedTable);
        model.QueryDerivedTableQueryExpressionLinkList.Add(new QueryDerivedTableQueryExpressionLink
        {
            Id = NextId(nameof(QueryDerivedTableQueryExpressionLink)),
            QueryDerivedTable = queryDerivedTable,
            QueryExpression = queryExpression.GetRef<QueryExpression>(nameof(QueryExpression))
        });
        return BuiltNode.Create(
            (nameof(TableReference), tableReference.Id),
            (nameof(TableReferenceWithAlias), aliasBase.Id),
            (nameof(TableReferenceWithAliasAndColumns), aliasAndColumns.Id),
            (nameof(QueryDerivedTable), queryDerivedTable.Id));
    }

    public BuiltNode CreateInlineDerivedTable(IReadOnlyList<BuiltNode> rowValues, BuiltNode alias, IReadOnlyList<BuiltNode>? columns = null)
    {
        var tableReference = new TableReference
        {
            Id = NextId(nameof(TableReference))
        };
        model.TableReferenceList.Add(tableReference);

        var aliasBase = new TableReferenceWithAlias
        {
            Id = NextId(nameof(TableReferenceWithAlias)),
            TableReference = tableReference
        };
        model.TableReferenceWithAliasList.Add(aliasBase);
        model.TableReferenceWithAliasAliasLinkList.Add(new TableReferenceWithAliasAliasLink
        {
            Id = NextId(nameof(TableReferenceWithAliasAliasLink)),
            TableReferenceWithAlias = aliasBase,
            Identifier = alias.GetRef<Identifier>(nameof(Identifier))
        });

        var aliasAndColumns = new TableReferenceWithAliasAndColumns
        {
            Id = NextId(nameof(TableReferenceWithAliasAndColumns)),
            TableReferenceWithAlias = aliasBase
        };
        model.TableReferenceWithAliasAndColumnsList.Add(aliasAndColumns);

        if (columns is not null)
        {
            for (var ordinal = 0; ordinal < columns.Count; ordinal++)
            {
                model.TableReferenceWithAliasAndColumnsColumnsItemList.Add(new TableReferenceWithAliasAndColumnsColumnsItem
                {
                    Id = NextId(nameof(TableReferenceWithAliasAndColumnsColumnsItem)),
                    TableReferenceWithAliasAndColumns = aliasAndColumns,
                    Identifier = columns[ordinal].GetRef<Identifier>(nameof(Identifier)),
                    Ordinal = ordinal.ToString(CultureInfo.InvariantCulture)
                });
            }
        }

        var inlineDerivedTable = new InlineDerivedTable
        {
            Id = NextId(nameof(InlineDerivedTable)),
            TableReferenceWithAliasAndColumns = aliasAndColumns
        };
        model.InlineDerivedTableList.Add(inlineDerivedTable);
        for (var ordinal = 0; ordinal < rowValues.Count; ordinal++)
        {
            model.InlineDerivedTableRowValuesItemList.Add(new InlineDerivedTableRowValuesItem
            {
                Id = NextId(nameof(InlineDerivedTableRowValuesItem)),
                InlineDerivedTable = inlineDerivedTable,
                RowValue = rowValues[ordinal].GetRef<RowValue>(nameof(RowValue)),
                Ordinal = ordinal.ToString(CultureInfo.InvariantCulture)
            });
        }
        return BuiltNode.Create(
            (nameof(TableReference), tableReference.Id),
            (nameof(TableReferenceWithAlias), aliasBase.Id),
            (nameof(TableReferenceWithAliasAndColumns), aliasAndColumns.Id),
            (nameof(InlineDerivedTable), inlineDerivedTable.Id));
    }

    public BuiltNode CreateRowValue(IReadOnlyList<BuiltNode> columnValues)
    {
        var rowValue = new RowValue { Id = NextId(nameof(RowValue)) };
        model.RowValueList.Add(rowValue);
        for (var ordinal = 0; ordinal < columnValues.Count; ordinal++)
        {
            model.RowValueColumnValuesItemList.Add(new RowValueColumnValuesItem
            {
                Id = NextId(nameof(RowValueColumnValuesItem)),
                RowValue = rowValue,
                ScalarExpression = columnValues[ordinal].GetRef<ScalarExpression>(nameof(ScalarExpression)),
                Ordinal = ordinal.ToString(CultureInfo.InvariantCulture)
            });
        }
        return BuiltNode.Create((nameof(RowValue), rowValue.Id));
    }

    public BuiltNode CreateQualifiedJoin(BuiltNode firstTableReference, BuiltNode secondTableReference, string joinType, BuiltNode searchCondition)
    {
        var tableReference = new TableReference
        {
            Id = NextId(nameof(TableReference))
        };
        model.TableReferenceList.Add(tableReference);

        var joinBase = new JoinTableReference
        {
            Id = NextId(nameof(JoinTableReference)),
            TableReference = tableReference
        };
        model.JoinTableReferenceList.Add(joinBase);

        var qualified = new QualifiedJoin
        {
            Id = NextId(nameof(QualifiedJoin)),
            JoinTableReference = joinBase,
            QualifiedJoinType = joinType
        };
        model.QualifiedJoinList.Add(qualified);
        model.JoinTableReferenceFirstTableReferenceLinkList.Add(new JoinTableReferenceFirstTableReferenceLink
        {
            Id = NextId(nameof(JoinTableReferenceFirstTableReferenceLink)),
            JoinTableReference = joinBase,
            TableReference = firstTableReference.GetRef<TableReference>(nameof(TableReference))
        });
        model.JoinTableReferenceSecondTableReferenceLinkList.Add(new JoinTableReferenceSecondTableReferenceLink
        {
            Id = NextId(nameof(JoinTableReferenceSecondTableReferenceLink)),
            JoinTableReference = joinBase,
            TableReference = secondTableReference.GetRef<TableReference>(nameof(TableReference))
        });
        model.QualifiedJoinSearchConditionLinkList.Add(new QualifiedJoinSearchConditionLink
        {
            Id = NextId(nameof(QualifiedJoinSearchConditionLink)),
            QualifiedJoin = qualified,
            BooleanExpression = searchCondition.GetRef<BooleanExpression>(nameof(BooleanExpression))
        });
        return BuiltNode.Create(
            (nameof(TableReference), tableReference.Id),
            (nameof(JoinTableReference), joinBase.Id),
            (nameof(QualifiedJoin), qualified.Id));
    }

    public BuiltNode CreateUnqualifiedJoin(BuiltNode firstTableReference, BuiltNode secondTableReference, string joinType)
    {
        var tableReference = new TableReference
        {
            Id = NextId(nameof(TableReference))
        };
        model.TableReferenceList.Add(tableReference);

        var joinBase = new JoinTableReference
        {
            Id = NextId(nameof(JoinTableReference)),
            TableReference = tableReference
        };
        model.JoinTableReferenceList.Add(joinBase);

        var unqualified = new UnqualifiedJoin
        {
            Id = NextId(nameof(UnqualifiedJoin)),
            JoinTableReference = joinBase,
            UnqualifiedJoinType = joinType
        };
        model.UnqualifiedJoinList.Add(unqualified);
        model.JoinTableReferenceFirstTableReferenceLinkList.Add(new JoinTableReferenceFirstTableReferenceLink
        {
            Id = NextId(nameof(JoinTableReferenceFirstTableReferenceLink)),
            JoinTableReference = joinBase,
            TableReference = firstTableReference.GetRef<TableReference>(nameof(TableReference))
        });
        model.JoinTableReferenceSecondTableReferenceLinkList.Add(new JoinTableReferenceSecondTableReferenceLink
        {
            Id = NextId(nameof(JoinTableReferenceSecondTableReferenceLink)),
            JoinTableReference = joinBase,
            TableReference = secondTableReference.GetRef<TableReference>(nameof(TableReference))
        });
        return BuiltNode.Create(
            (nameof(TableReference), tableReference.Id),
            (nameof(JoinTableReference), joinBase.Id),
            (nameof(UnqualifiedJoin), unqualified.Id));
    }

    public BuiltNode CreateFromClause(IReadOnlyList<BuiltNode> tableReferences)
    {
        var row = new FromClause { Id = NextId(nameof(FromClause)) };
        model.FromClauseList.Add(row);
        for (var ordinal = 0; ordinal < tableReferences.Count; ordinal++)
        {
            model.FromClauseTableReferencesItemList.Add(new FromClauseTableReferencesItem
            {
                Id = NextId(nameof(FromClauseTableReferencesItem)),
                FromClause = row,
                TableReference = tableReferences[ordinal].GetRef<TableReference>(nameof(TableReference)),
                Ordinal = ordinal.ToString(CultureInfo.InvariantCulture)
            });
        }
        return BuiltNode.Create((nameof(FromClause), row.Id));
    }

}
