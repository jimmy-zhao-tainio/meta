using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Meta.Core.Domain;
using Meta.Core.Services;

namespace Meta.Adapters;

public sealed class ExportService : IExportService
{
    private readonly IWorkspaceService _workspaceService;

    public ExportService(IWorkspaceService workspaceService)
    {
        _workspaceService = workspaceService ?? throw new ArgumentNullException(nameof(workspaceService));
    }

    public async Task ExportXmlAsync(Workspace workspace, string outputDirectory, CancellationToken cancellationToken = default)
    {
        if (workspace == null)
        {
            throw new ArgumentNullException(nameof(workspace));
        }

        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new ArgumentException("Output directory is required.", nameof(outputDirectory));
        }

        var clone = new Workspace
        {
            WorkspaceRootPath = outputDirectory,
            MetadataRootPath = string.Empty,
            WorkspaceConfig = workspace.WorkspaceConfig,
            Model = workspace.Model,
            Instance = workspace.Instance,
            Diagnostics = workspace.Diagnostics,
            IsDirty = workspace.IsDirty,
        };
        await _workspaceService.SaveAsync(clone, cancellationToken).ConfigureAwait(false);
    }

    public async Task ExportCsvAsync(
        Workspace workspace,
        string entityName,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (workspace == null)
        {
            throw new ArgumentNullException(nameof(workspace));
        }

        if (string.IsNullOrWhiteSpace(entityName))
        {
            throw new ArgumentException("Entity name is required.", nameof(entityName));
        }

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("CSV output path is required.", nameof(outputPath));
        }

        var entity = workspace.Model.FindEntity(entityName)
            ?? throw new InvalidOperationException($"Entity '{entityName}' does not exist.");

        var relationshipColumns = entity.Relationships
            .Select(relationship => relationship.GetColumnName())
            .ToList();
        var propertyColumns = entity.Properties
            .Select(property => property.Name)
            .ToList();
        var rows = workspace.Instance.GetOrCreateEntityRecords(entity.Name)
            .OrderBy(row => row.Id, StringComparer.Ordinal)
            .ToList();

        var fullOutputPath = Path.GetFullPath(outputPath);
        var outputDirectory = Path.GetDirectoryName(fullOutputPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        var builder = new StringBuilder();
        WriteCsvRow(builder, new[] { "Id" }.Concat(relationshipColumns).Concat(propertyColumns));

        foreach (var row in rows)
        {
            var values = new List<string>(1 + relationshipColumns.Count + propertyColumns.Count)
            {
                row.Id
            };
            values.AddRange(relationshipColumns.Select(name =>
                row.RelationshipIds.TryGetValue(name, out var relationshipId) ? relationshipId : string.Empty));
            values.AddRange(propertyColumns.Select(name =>
                row.Values.TryGetValue(name, out var value) ? value : string.Empty));
            WriteCsvRow(builder, values);
        }

        await File.WriteAllTextAsync(fullOutputPath, builder.ToString(), Encoding.UTF8, cancellationToken).ConfigureAwait(false);
    }

    public Task ExportSqlAsync(
        Workspace workspace,
        string schemaOutputPath,
        string dataOutputPath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (workspace == null)
        {
            throw new ArgumentNullException(nameof(workspace));
        }

        if (string.IsNullOrWhiteSpace(schemaOutputPath))
        {
            throw new ArgumentException("Schema output path is required.", nameof(schemaOutputPath));
        }

        if (string.IsNullOrWhiteSpace(dataOutputPath))
        {
            throw new ArgumentException("Data output path is required.", nameof(dataOutputPath));
        }

        var tempRoot = Path.Combine(Path.GetTempPath(), "metadata-studio-export", Guid.NewGuid().ToString("N"));
        try
        {
            GenerationService.GenerateSql(workspace, tempRoot);
            var generatedSchemaPath = Path.Combine(tempRoot, "schema.sql");
            var generatedDataPath = Path.Combine(tempRoot, "data.sql");

            var schemaFullPath = Path.GetFullPath(schemaOutputPath);
            var dataFullPath = Path.GetFullPath(dataOutputPath);

            var schemaDirectory = Path.GetDirectoryName(schemaFullPath);
            if (!string.IsNullOrWhiteSpace(schemaDirectory))
            {
                Directory.CreateDirectory(schemaDirectory);
            }

            var dataDirectory = Path.GetDirectoryName(dataFullPath);
            if (!string.IsNullOrWhiteSpace(dataDirectory))
            {
                Directory.CreateDirectory(dataDirectory);
            }

            File.Copy(generatedSchemaPath, schemaFullPath, overwrite: true);
            File.Copy(generatedDataPath, dataFullPath, overwrite: true);
            return Task.CompletedTask;
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    public Task ExportCSharpAsync(Workspace workspace, string outputPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (workspace == null)
        {
            throw new ArgumentNullException(nameof(workspace));
        }

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException("C# output path is required.", nameof(outputPath));
        }

        var outputRoot = ResolveCSharpOutputDirectory(outputPath);
        GenerationService.GenerateCSharp(workspace, outputRoot);
        return Task.CompletedTask;
    }

    private static string ResolveCSharpOutputDirectory(string outputPath)
    {
        if (outputPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            var outputFilePath = Path.GetFullPath(outputPath);
            var directory = Path.GetDirectoryName(outputFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                return directory;
            }

            return Directory.GetCurrentDirectory();
        }

        return Path.GetFullPath(outputPath);
    }

    private static void WriteCsvRow(StringBuilder builder, IEnumerable<string> values)
    {
        builder.AppendJoin(",", values.Select(EscapeCsv));
        builder.AppendLine();
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

