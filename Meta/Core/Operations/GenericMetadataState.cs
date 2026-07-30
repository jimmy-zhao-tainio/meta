using Meta.Core.Domain;

namespace Meta.Core.Operations;

public sealed class GenericMetadataState
{
    public GenericMetadataState(
        GenericModel model,
        GenericInstance instance)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
        Instance = instance ?? throw new ArgumentNullException(nameof(instance));
    }

    public GenericModel Model { get; }
    public GenericInstance Instance { get; }

    public GenericMetadataState Clone()
    {
        return new GenericMetadataState(
            Model.Clone(),
            WorkspaceSnapshotCloner.CloneInstance(Instance));
    }

    public static GenericMetadataState Capture(Workspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        return new GenericMetadataState(
            workspace.Model.Clone(),
            WorkspaceSnapshotCloner.CloneInstance(workspace.Instance));
    }
}
