using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Meta.Operations.Domain;
using Meta.Operations;
using Meta.Surfaces.Xml;

namespace Meta.Integration;

public sealed class ExportService : IExportService
{
    private static readonly UTF8Encoding Utf8NoBom =
        new(encoderShouldEmitUTF8Identifier: false);
    public async Task ExportXmlAsync(InMemoryWorkspace workspace, string outputDirectory, CancellationToken cancellationToken = default)
    {
        if (workspace == null)
        {
            throw new ArgumentNullException(nameof(workspace));
        }

        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new ArgumentException("Output directory is required.", nameof(outputDirectory));
        }

        await XmlWorkspaceWriter.WriteNewAsync(workspace, outputDirectory, cancellationToken).ConfigureAwait(false);
    }

    public async Task ExportCsvAsync(
        IMetaWorkspaceSource source,
        string entityName,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ArgumentNullException.ThrowIfNull(source);

        if (string.IsNullOrWhiteSpace(entityName))
        {
            throw new ArgumentException("Entity name is required.", nameof(entityName));
        }

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("CSV output path is required.", nameof(outputPath));
        }

        var resolvedEntityName = await ResolveEntityNameAsync(
                source,
                entityName,
                cancellationToken)
            .ConfigureAwait(false);
        var relationshipColumns = new List<string>();
        await foreach (var relationship in source.ReadRelationshipsAsync(
                           resolvedEntityName,
                           cancellationToken))
        {
            relationshipColumns.Add(relationship.GetColumnName());
        }

        var propertyColumns = new List<string>();
        await foreach (var property in source.ReadPropertiesAsync(
                           resolvedEntityName,
                           cancellationToken))
        {
            propertyColumns.Add(property.Name);
        }

        var fullOutputPath = Path.GetFullPath(outputPath);
        var outputDirectory = Path.GetDirectoryName(fullOutputPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        await using var writer = new StreamWriter(
            fullOutputPath,
            append: false,
            Utf8NoBom);
        await WriteCsvRowAsync(
                writer,
                new[] { "Id" }
                    .Concat(relationshipColumns)
                    .Concat(propertyColumns),
                cancellationToken)
            .ConfigureAwait(false);

        await foreach (var row in source.ReadRecordsAsync(
                           resolvedEntityName,
                           cancellationToken))
        {
            var values = new List<string>(1 + relationshipColumns.Count + propertyColumns.Count)
            {
                row.Id
            };
            values.AddRange(relationshipColumns.Select(name =>
                row.RelationshipIds.TryGetValue(name, out var relationshipId) ? relationshipId : string.Empty));
            values.AddRange(propertyColumns.Select(name =>
                row.Values.TryGetValue(name, out var value) ? value : string.Empty));
            await WriteCsvRowAsync(
                    writer,
                    values,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task<string> ResolveEntityNameAsync(
        IMetaWorkspaceSource source,
        string entityName,
        CancellationToken cancellationToken)
    {
        await foreach (var candidate in source.ReadEntityNamesAsync(
                           cancellationToken))
        {
            if (MetaName.Comparer.Equals(candidate, entityName))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            $"Entity '{entityName}' does not exist.");
    }

    private static Task WriteCsvRowAsync(
        TextWriter writer,
        IEnumerable<string> values,
        CancellationToken cancellationToken)
    {
        var line = string.Join(",", values.Select(EscapeCsv));
        return writer.WriteLineAsync(line.AsMemory(), cancellationToken);
    }

    private static string EscapeCsv(string? value)
    {
        var text = value ?? string.Empty;
        if (!text.Contains(',') && !text.Contains('"') && !text.Contains('\r') && !text.Contains('\n'))
        {
            return text;
        }

        return "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}

