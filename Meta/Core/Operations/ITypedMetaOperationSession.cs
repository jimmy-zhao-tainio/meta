using Meta.Core.Serialization;

namespace Meta.Core.Operations;

public interface ITypedMetaOperationSession<TModel>
    where TModel : class, IMetaWorkspaceModel<TModel>
{
    TModel Model { get; }

    TypedMetaOperationResult Apply(
        Action<TypedMetaOperationPlanBuilder<TModel>> configure);

    TypedMetaOperationResult Apply(TypedMetaOperationPlan<TModel> plan);
}
