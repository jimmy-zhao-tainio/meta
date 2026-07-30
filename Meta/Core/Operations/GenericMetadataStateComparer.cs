using Meta.Core.Domain;

namespace Meta.Core.Operations;

internal static class GenericMetadataStateComparer
{
    public static string? FindDifference(
        GenericMetadataState left,
        GenericMetadataState right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        if (!string.Equals(
                left.Model.ComputeContractSignature(),
                right.Model.ComputeContractSignature(),
                StringComparison.Ordinal))
        {
            return "Model contracts differ.";
        }

        if (!string.Equals(
                left.Instance.ModelName,
                right.Instance.ModelName,
                StringComparison.Ordinal))
        {
            return
                $"Instance model names differ: '{left.Instance.ModelName}' and '{right.Instance.ModelName}'.";
        }

        var entityNames = left.Model.Entities
            .Select(item => item.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unknownEntity = left.Instance.RecordsByEntity.Keys
            .Concat(right.Instance.RecordsByEntity.Keys)
            .FirstOrDefault(name => !entityNames.Contains(name));
        if (unknownEntity != null)
        {
            return $"Instance state contains unknown entity '{unknownEntity}'.";
        }

        foreach (var entityName in entityNames)
        {
            var leftRecords = GetRecords(left, entityName);
            var rightRecords = GetRecords(right, entityName);
            var difference = FindRecordDifference(
                entityName,
                leftRecords,
                rightRecords);
            if (difference != null)
            {
                return difference;
            }
        }

        return null;
    }

    private static IReadOnlyCollection<GenericRecord> GetRecords(
        GenericMetadataState state,
        string entityName)
    {
        return state.Instance.RecordsByEntity.TryGetValue(
            entityName,
            out var records)
            ? records
            : [];
    }

    private static string? FindRecordDifference(
        string entityName,
        IReadOnlyCollection<GenericRecord> left,
        IReadOnlyCollection<GenericRecord> right)
    {
        if (left.Count != right.Count)
        {
            return
                $"Entity '{entityName}' record counts differ: {left.Count} and {right.Count}.";
        }

        var rightById = right.ToDictionary(
            item => item.Id,
            StringComparer.OrdinalIgnoreCase);
        foreach (var leftRecord in left)
        {
            if (!rightById.TryGetValue(leftRecord.Id, out var rightRecord))
            {
                return
                    $"Entity '{entityName}' record '{leftRecord.Id}' is missing.";
            }

            if (!string.Equals(
                    leftRecord.Id,
                    rightRecord.Id,
                    StringComparison.Ordinal))
            {
                return
                    $"Entity '{entityName}' record Id spellings differ: '{leftRecord.Id}' and '{rightRecord.Id}'.";
            }

            if (!DictionariesAreEqual(
                    leftRecord.Values,
                    rightRecord.Values))
            {
                return
                    $"Entity '{entityName}' record '{leftRecord.Id}' properties differ.";
            }

            if (!DictionariesAreEqual(
                    leftRecord.RelationshipIds,
                    rightRecord.RelationshipIds))
            {
                return
                    $"Entity '{entityName}' record '{leftRecord.Id}' relationships differ.";
            }
        }

        return null;
    }

    private static bool DictionariesAreEqual(
        IReadOnlyDictionary<string, string> left,
        IReadOnlyDictionary<string, string> right)
    {
        return left.Count == right.Count &&
               left.All(item =>
                   right.TryGetValue(item.Key, out var rightValue) &&
                   string.Equals(
                       item.Value,
                       rightValue,
                       StringComparison.Ordinal));
    }
}
