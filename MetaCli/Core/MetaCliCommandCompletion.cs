namespace MetaCli.Core;

public sealed class MetaCliCommandCompletion
{
    private readonly List<Action> actions = new();

    public void OnSucceeded(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        actions.Add(action);
    }

    internal void Complete()
    {
        foreach (var action in actions)
        {
            action();
        }
    }
}
