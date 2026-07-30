namespace Meta.Core.Operations;

public sealed class MetaOperationPlan
{
    public static MetaOperationPlan Empty { get; } = new(Array.Empty<MetaOperation>());

    public MetaOperationPlan(IEnumerable<MetaOperation> operations)
    {
        ArgumentNullException.ThrowIfNull(operations);

        var copy = operations.ToArray();
        if (copy.Any(operation => operation == null))
        {
            throw new ArgumentException(
                "An operation plan cannot contain null operations.",
                nameof(operations));
        }

        Operations = Array.AsReadOnly(copy);
    }

    public IReadOnlyList<MetaOperation> Operations { get; }

    public static MetaOperationPlan Create(params MetaOperation[] operations)
    {
        return new MetaOperationPlan(operations);
    }
}
