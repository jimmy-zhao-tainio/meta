using Meta.Operations.Domain;

namespace Meta.TypedModels;

/// <summary>Compiler-checked projection between one modeled contract and its typed instance graph.</summary>
public interface ITypedWorkspaceBinding<TModel>
    where TModel : class
{
    TModel Read(InMemoryWorkspace state);

    InMemoryWorkspace Write(TModel model, GenericModel contract);
}
