using System.Text;
using Meta.Core.Operations;

namespace Meta.Adapters;

internal static class CSharpMetaStateSignature
{
    public static string Build(GenericMetadataState state)
    {
        var builder = new StringBuilder();
        Append(builder, state.Model.ComputeContractSignature());
        Append(builder, state.Instance.ModelName);

        foreach (var entity in state.Model.Entities
                     .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(item => item.Name, StringComparer.Ordinal))
        {
            Append(builder, entity.Name);
            var records = state.Instance.RecordsByEntity.TryGetValue(
                entity.Name,
                out var entityRecords)
                ? entityRecords
                : [];
            foreach (var record in records
                         .OrderBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(item => item.Id, StringComparer.Ordinal))
            {
                Append(builder, record.Id);
                foreach (var value in record.Values
                             .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                             .ThenBy(item => item.Key, StringComparer.Ordinal))
                {
                    Append(builder, "property", value.Key, value.Value);
                }

                foreach (var relationship in record.RelationshipIds
                             .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                             .ThenBy(item => item.Key, StringComparer.Ordinal))
                {
                    Append(
                        builder,
                        "relationship",
                        relationship.Key,
                        relationship.Value);
                }
            }
        }

        return builder.ToString();
    }

    private static void Append(
        StringBuilder builder,
        params string?[] values)
    {
        foreach (var value in values)
        {
            if (value == null)
            {
                builder.Append("-1:");
                continue;
            }

            builder.Append(value.Length);
            builder.Append(':');
            builder.Append(value);
        }

        builder.AppendLine();
    }
}
