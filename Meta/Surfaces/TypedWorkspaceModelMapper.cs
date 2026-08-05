using Meta.Core.Domain;
using Meta.Core.Operations;

namespace Meta.Core.Serialization;

public static class TypedWorkspaceModelMapper
{
    public static TModel FromInMemoryWorkspace<TModel>(
        InMemoryWorkspace workspace,
        Func<TModel> createModel)
        where TModel : class
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(createModel);
        return TypedWorkspaceXmlSerializer.FromInMemoryWorkspace(
            workspace,
            createModel);
    }

    public static InMemoryWorkspace ToInMemoryWorkspace<TModel>(TModel model)
        where TModel : class
    {
        ArgumentNullException.ThrowIfNull(model);
        return TypedWorkspaceXmlSerializer.ToInMemoryWorkspace(model);
    }
}
