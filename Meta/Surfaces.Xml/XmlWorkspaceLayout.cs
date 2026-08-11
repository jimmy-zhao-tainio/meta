using Meta.Operations.Domain;

namespace Meta.Surfaces.Xml;

internal sealed class XmlWorkspaceLayout
{
    private readonly Dictionary<RecordAddress, string> _shardByRecord =
        new(RecordAddressComparer.Instance);

    public string? FindShard(string entityName, string recordId)
    {
        return _shardByRecord.TryGetValue(
            new RecordAddress(entityName, recordId),
            out var shardFileName)
            ? shardFileName
            : null;
    }

    public void AssignShard(
        string entityName,
        string recordId,
        string shardFileName)
    {
        var address = RequireAddress(entityName, recordId);
        if (string.IsNullOrWhiteSpace(shardFileName))
        {
            throw new ArgumentException(
                "Shard file name is required.",
                nameof(shardFileName));
        }

        _shardByRecord[address] = shardFileName;
    }

    public void RenameEntity(string oldName, string newName)
    {
        var requiredOldName = MetaName.Require(
            oldName,
            "Existing entity name.");
        var requiredNewName = MetaName.Require(
            newName,
            "New entity name.");
        var oldDefaultShard = requiredOldName + ".xml";
        var newDefaultShard = requiredNewName + ".xml";
        var matches = _shardByRecord
            .Where(item =>
                MetaName.Comparer.Equals(
                    item.Key.EntityName,
                    requiredOldName))
            .ToArray();

        foreach (var match in matches)
        {
            _shardByRecord.Remove(match.Key);
            var shardFileName = string.Equals(
                match.Value,
                oldDefaultShard,
                StringComparison.OrdinalIgnoreCase)
                ? newDefaultShard
                : match.Value;
            _shardByRecord.Add(
                new RecordAddress(
                    requiredNewName,
                    match.Key.RecordId),
                shardFileName);
        }
    }

    public void RenameRecord(
        string entityName,
        string oldId,
        string newId)
    {
        var requiredEntityName = MetaName.Require(
            entityName,
            "Entity name.");
        var requiredOldId = MetaIdentity.Require(
            oldId,
            "Existing record Id.");
        var requiredNewId = MetaIdentity.Require(
            newId,
            "New record Id.");
        var oldAddress = new RecordAddress(
            requiredEntityName,
            requiredOldId);
        if (!_shardByRecord.Remove(oldAddress, out var shardFileName))
        {
            return;
        }

        _shardByRecord.Add(
            new RecordAddress(requiredEntityName, requiredNewId),
            shardFileName);
    }

    public XmlWorkspaceLayout Clone()
    {
        var clone = new XmlWorkspaceLayout();
        foreach (var assignment in _shardByRecord)
        {
            clone._shardByRecord.Add(
                assignment.Key,
                assignment.Value);
        }

        return clone;
    }

    private static RecordAddress RequireAddress(
        string entityName,
        string recordId)
    {
        return new RecordAddress(
            MetaName.Require(entityName, "Entity name."),
            MetaIdentity.Require(recordId, "Record Id."));
    }

    private readonly record struct RecordAddress(
        string EntityName,
        string RecordId);

    private sealed class RecordAddressComparer :
        IEqualityComparer<RecordAddress>
    {
        public static RecordAddressComparer Instance { get; } = new();

        public bool Equals(RecordAddress x, RecordAddress y)
        {
            return MetaName.Comparer.Equals(
                       x.EntityName,
                       y.EntityName) &&
                   MetaIdentity.Comparer.Equals(
                       x.RecordId,
                       y.RecordId);
        }

        public int GetHashCode(RecordAddress obj)
        {
            return HashCode.Combine(
                MetaName.Comparer.GetHashCode(obj.EntityName),
                MetaIdentity.Comparer.GetHashCode(obj.RecordId));
        }
    }
}
