using System.Collections.ObjectModel;

namespace Meta.Operations;

public abstract record RecordCondition
{
    private protected RecordCondition(string FieldName, string Value)
    {
        this.FieldName = MetaName.Require(FieldName, "Record field name.");
        this.Value = Value ?? throw new ArgumentNullException(nameof(Value));
    }

    public string FieldName { get; }
    public string Value { get; }

    public sealed record Equal : RecordCondition
    {
        public Equal(string FieldName, string Value)
            : base(FieldName, Value)
        {
        }
    }

    public sealed record Contains : RecordCondition
    {
        public Contains(string FieldName, string Value)
            : base(FieldName, Value)
        {
        }
    }
}

public sealed class RecordQuery
{
    public RecordQuery(
        int MaximumRecords,
        params RecordCondition[] Conditions)
    {
        if (MaximumRecords <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumRecords),
                "Maximum records must be greater than zero.");
        }

        ArgumentNullException.ThrowIfNull(Conditions);
        if (Conditions.Any(condition => condition == null))
        {
            throw new ArgumentException(
                "Record conditions cannot contain null.",
                nameof(Conditions));
        }

        this.MaximumRecords = MaximumRecords;
        this.Conditions = new ReadOnlyCollection<RecordCondition>(
            Conditions.ToArray());
    }

    public int MaximumRecords { get; }
    public IReadOnlyList<RecordCondition> Conditions { get; }
}

public sealed class RecordQueryResult
{
    public RecordQueryResult(
        long TotalCount,
        IReadOnlyCollection<RecordData> Records)
    {
        if (TotalCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(TotalCount));
        }

        ArgumentNullException.ThrowIfNull(Records);
        if (Records.Count > TotalCount)
        {
            throw new ArgumentException(
                "Returned records cannot exceed the total count.",
                nameof(Records));
        }

        this.TotalCount = TotalCount;
        this.Records = new ReadOnlyCollection<RecordData>(
            Records.ToArray());
    }

    public long TotalCount { get; }
    public IReadOnlyList<RecordData> Records { get; }
}
