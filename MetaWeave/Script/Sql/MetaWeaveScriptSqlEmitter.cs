using System.Text;
using MetaWeave;

namespace MetaWeaveScript.Sql;

internal sealed partial class MetaWeaveScriptSqlEmitter
{
    private readonly MetaWeaveModel model;

    public MetaWeaveScriptSqlEmitter(MetaWeaveModel model)
    {
        this.model = model;
    }

    public string Render(TSqlStatement root)
    {
        var statementWithCtes = FindByBaseId(model.StatementWithCtesList, root.Id);
        if (statementWithCtes is not null)
        {
            var selectStatement = FindByBaseId(model.SelectStatementList, statementWithCtes.Id);
            if (selectStatement is not null)
            {
                return RenderStatementWithCtes(
                    statementWithCtes,
                    RenderSelectStatementBody(selectStatement));
            }

            throw new InvalidOperationException($"Unsupported MetaWeaveScript statement-with-CTEs id '{statementWithCtes.Id}'.");
        }

        throw new InvalidOperationException($"Unsupported MetaWeaveScript TSqlStatement id '{root.Id}'.");
    }

    public string Render(SelectStatement root)
    {
        var statementBase = GetById(
            model.StatementWithCtesList,
            root.StatementWithCtes.Id,
            "SelectStatement.Base");
        return RenderStatementWithCtes(statementBase, RenderSelectStatementBody(root));
    }

    private string RenderStatementWithCtes(
        StatementWithCtes statementBase,
        string body)
    {
        var builder = new StringBuilder();

        var withCtesLink = FindOwnerLink(model.StatementWithCtesWithCtesLinkList, statementBase.Id);
        if (withCtesLink is not null)
        {
            builder.Append(RenderWithClause(withCtesLink.WithCtes));
            builder.AppendLine();
        }

        builder.Append(body);
        return builder.ToString();
    }

    private string RenderSelectStatementBody(SelectStatement root)
    {
        var queryExpressionLink = GetOwnerLink(model.SelectStatementQueryExpressionLinkList, root.Id, "SelectStatement.QueryExpression");
        return RenderQueryExpression(queryExpressionLink.QueryExpression);
    }
}
