using System.Linq.Expressions;
using System.Reflection;
using Meta.Core.Serialization;

namespace Meta.Core.Operations;

public sealed class TypedMetaOperationPlan<TModel>
    where TModel : class, IMetaWorkspaceModel<TModel>
{
    private readonly IReadOnlyList<Func<TModel, ResolvedTypedMetaOperation>> _operations;

    private TypedMetaOperationPlan(
        IReadOnlyList<Func<TModel, ResolvedTypedMetaOperation>> operations)
    {
        _operations = operations;
    }

    internal IReadOnlyList<Func<TModel, ResolvedTypedMetaOperation>> Operations =>
        _operations;

    public static TypedMetaOperationPlan<TModel> Create(
        Action<TypedMetaOperationPlanBuilder<TModel>> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new TypedMetaOperationPlanBuilder<TModel>();
        configure(builder);
        return new TypedMetaOperationPlan<TModel>(builder.Build());
    }
}

public sealed class TypedMetaOperationPlanBuilder<TModel>
    where TModel : class, IMetaWorkspaceModel<TModel>
{
    private readonly List<Func<TModel, ResolvedTypedMetaOperation>> _operations = [];

    public TypedMetaOperationPlanBuilder<TModel> Insert<TEntity>(TEntity row)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(row);
        var operation =
            TypedWorkspaceXmlSerializer.CaptureInsertOperation<TModel, TEntity>(
                row);
        _operations.Add(model =>
        {
            TypedWorkspaceXmlSerializer.RequireOperationInsert(
                model,
                row,
                operation);
            return new ResolvedTypedMetaOperation(
                operation,
                () => TypedWorkspaceXmlSerializer.AddOperationRow(model, row),
                () => TypedWorkspaceXmlSerializer.RemoveOperationRow(model, row));
        });
        return this;
    }

    public TypedMetaOperationPlanBuilder<TModel> SetProperty<TEntity>(
        TEntity row,
        Expression<Func<TEntity, string?>> propertySelector,
        string value)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(value);
        var property = RequireProperty(propertySelector);
        var address =
            TypedWorkspaceXmlSerializer.CaptureOperationRow<TModel, TEntity>(
                row);
        var propertyName =
            TypedWorkspaceXmlSerializer.RequireOperationScalar<TModel, TEntity>(
                property);
        _operations.Add(model =>
        {
            TypedWorkspaceXmlSerializer.RequireOperationRow(
                model,
                row,
                address);
            MetaOperation operation = new SetPropertyOperation(
                address.EntityName,
                address.Id,
                propertyName,
                value);
            var previousValue = property.GetValue(row);
            return new ResolvedTypedMetaOperation(
                operation,
                () => property.SetValue(row, value),
                () => property.SetValue(row, previousValue));
        });
        return this;
    }

    public TypedMetaOperationPlanBuilder<TModel> ClearProperty<TEntity>(
        TEntity row,
        Expression<Func<TEntity, string?>> propertySelector)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(row);
        var property = RequireProperty(propertySelector);
        var address =
            TypedWorkspaceXmlSerializer.CaptureOperationRow<TModel, TEntity>(
                row);
        var propertyName =
            TypedWorkspaceXmlSerializer.RequireOperationScalar<TModel, TEntity>(
                property);
        _operations.Add(model =>
        {
            TypedWorkspaceXmlSerializer.RequireOperationRow(
                model,
                row,
                address);
            var previousValue = property.GetValue(row);
            return new ResolvedTypedMetaOperation(
                new ClearPropertyOperation(
                    address.EntityName,
                    address.Id,
                    propertyName),
                () => property.SetValue(row, null),
                () => property.SetValue(row, previousValue));
        });
        return this;
    }

    public TypedMetaOperationPlanBuilder<TModel> SetRelationship<TEntity, TTarget>(
        TEntity row,
        Expression<Func<TEntity, TTarget?>> relationshipSelector,
        TTarget target)
        where TEntity : class
        where TTarget : class
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(target);
        var property = RequireProperty(relationshipSelector);
        var address =
            TypedWorkspaceXmlSerializer.CaptureOperationRow<TModel, TEntity>(
                row);
        var targetAddress =
            TypedWorkspaceXmlSerializer.CaptureOperationRow<TModel, TTarget>(
                target);
        var relationshipName =
            TypedWorkspaceXmlSerializer
                .RequireOperationRelationship<TModel, TEntity, TTarget>(
                    property);
        _operations.Add(model =>
        {
            TypedWorkspaceXmlSerializer.RequireOperationRow(
                model,
                row,
                address);
            var previousTarget = property.GetValue(row);
            TypedWorkspaceXmlSerializer.RequireOperationRow(
                model,
                target,
                targetAddress);

            return new ResolvedTypedMetaOperation(
                new SetRelationshipOperation(
                    address.EntityName,
                    address.Id,
                    relationshipName,
                    targetAddress.Id),
                () => property.SetValue(row, target),
                () => property.SetValue(row, previousTarget));
        });
        return this;
    }

    public TypedMetaOperationPlanBuilder<TModel> ClearRelationship<TEntity, TTarget>(
        TEntity row,
        Expression<Func<TEntity, TTarget?>> relationshipSelector)
        where TEntity : class
        where TTarget : class
    {
        ArgumentNullException.ThrowIfNull(row);
        var property = RequireProperty(relationshipSelector);
        var address =
            TypedWorkspaceXmlSerializer.CaptureOperationRow<TModel, TEntity>(
                row);
        var relationshipName =
            TypedWorkspaceXmlSerializer
                .RequireOperationRelationship<TModel, TEntity, TTarget>(
                    property);
        _operations.Add(model =>
        {
            TypedWorkspaceXmlSerializer.RequireOperationRow(
                model,
                row,
                address);
            var previousTarget = property.GetValue(row);
            return new ResolvedTypedMetaOperation(
                new ClearRelationshipOperation(
                    address.EntityName,
                    address.Id,
                    relationshipName),
                () => property.SetValue(row, null),
                () => property.SetValue(row, previousTarget));
        });
        return this;
    }

    public TypedMetaOperationPlanBuilder<TModel> Delete<TEntity>(TEntity row)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(row);
        var address =
            TypedWorkspaceXmlSerializer.CaptureOperationRow<TModel, TEntity>(
                row);
        _operations.Add(model =>
        {
            TypedWorkspaceXmlSerializer.RequireOperationRow(
                model,
                row,
                address);
            var index = TypedWorkspaceXmlSerializer.IndexOfOperationRow(
                model,
                row);
            return new ResolvedTypedMetaOperation(
                new DeleteRecordOperation(address.EntityName, address.Id),
                () => TypedWorkspaceXmlSerializer.RemoveOperationRow(model, row),
                () => TypedWorkspaceXmlSerializer.InsertOperationRow(
                    model,
                    index,
                    row));
        });
        return this;
    }

    internal IReadOnlyList<Func<TModel, ResolvedTypedMetaOperation>> Build()
    {
        return _operations.ToArray();
    }

    private static PropertyInfo RequireProperty(LambdaExpression selector)
    {
        ArgumentNullException.ThrowIfNull(selector);

        Expression body = selector.Body;
        if (body is UnaryExpression
            {
                NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked,
            } conversion)
        {
            body = conversion.Operand;
        }

        if (body is not MemberExpression
            {
                Member: PropertyInfo property,
                Expression: ParameterExpression,
            })
        {
            throw new ArgumentException(
                "Selector must identify one direct entity property.",
                nameof(selector));
        }

        return property;
    }
}

internal sealed record ResolvedTypedMetaOperation(
    MetaOperation Operation,
    Action Apply,
    Action Revert);
