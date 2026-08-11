namespace Meta.Operations.Domain;

public sealed class InMemoryWorkspace
{
    public InMemoryWorkspace(GenericModel model, GenericInstance instance)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
        Instance = instance ?? throw new ArgumentNullException(nameof(instance));
    }

    public GenericModel Model { get; }
    public GenericInstance Instance { get; }

    public InMemoryWorkspace Clone() =>
        new(Model.Clone(), Instance.Clone());
}
