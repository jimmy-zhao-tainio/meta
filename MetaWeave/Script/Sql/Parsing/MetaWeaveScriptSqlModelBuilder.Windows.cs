using System.Globalization;
using MetaWeave;

namespace MetaWeaveScript.Sql.Parsing;

internal sealed partial class MetaWeaveScriptSqlModelBuilder
{
    public BuiltNode CreateOverClause(
        IReadOnlyList<BuiltNode>? partitions,
        BuiltNode orderByClause)
    {
        var overClause = new OverClause
        {
            Id = NextId(nameof(OverClause))
        };
        model.OverClauseList.Add(overClause);

        if (partitions is not null)
        {
            for (var ordinal = 0; ordinal < partitions.Count; ordinal++)
            {
                model.OverClausePartitionsItemList.Add(new OverClausePartitionsItem
                {
                    Id = NextId(nameof(OverClausePartitionsItem)),
                    OverClause = overClause,
                    ScalarExpression = partitions[ordinal].GetRef<ScalarExpression>(nameof(ScalarExpression)),
                    Ordinal = ordinal.ToString(CultureInfo.InvariantCulture)
                });
            }
        }

        model.OverClauseOrderByClauseLinkList.Add(new OverClauseOrderByClauseLink
        {
            Id = NextId(nameof(OverClauseOrderByClauseLink)),
            OverClause = overClause,
            OrderByClause = orderByClause.GetRef<OrderByClause>(nameof(OrderByClause))
        });

        return BuiltNode.Create((nameof(OverClause), overClause.Id));
    }

    public BuiltNode AttachOverClause(BuiltNode functionCall, BuiltNode overClause)
    {
        model.FunctionCallOverClauseLinkList.Add(new FunctionCallOverClauseLink
        {
            Id = NextId(nameof(FunctionCallOverClauseLink)),
            FunctionCall = functionCall.GetRef<FunctionCall>(nameof(FunctionCall)),
            OverClause = overClause.GetRef<OverClause>(nameof(OverClause))
        });

        return functionCall;
    }
}
