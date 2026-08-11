using System.Collections.ObjectModel;

namespace Meta.Operations;

public abstract partial record Operation
{
    public abstract record ModelOperation : Operation;
    public abstract record InstanceOperation : Operation;
    public abstract record RefactorOperation : Operation;

    public abstract OperationResult ApplyTo(IOperationTarget target);

    private protected static string RequireName(
        string? value,
        string description) =>
        MetaName.Require(value, description);

    private protected static string RequireIdentity(
        string? value,
        string description) =>
        MetaIdentity.Require(value, description);

    private protected static string RequireText(
        string? value,
        string description) =>
        value ?? throw new ArgumentNullException(description);

    private protected static IReadOnlyDictionary<string, string> CopyValues(
        IReadOnlyDictionary<string, string>? source,
        bool identities)
    {
        var copy = new Dictionary<string, string>(MetaName.Comparer);
        if (source != null)
        {
            foreach (var item in source)
            {
                var name = RequireName(item.Key, "Member name.");
                var value = identities
                    ? RequireIdentity(item.Value, "Related record Id.")
                    : RequireText(item.Value, "Property value.");
                copy.Add(name, value);
            }
        }

        return new ReadOnlyDictionary<string, string>(copy);
    }
}
