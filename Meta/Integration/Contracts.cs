using Meta.Operations;
using Meta.Operations.Domain;

namespace Meta.Integration;

public interface IImportService
{
    Task<InMemoryWorkspace> ImportSqlAsync(
        string connectionString,
        string schema,
        CancellationToken cancellationToken = default);

    Task<InMemoryWorkspace> ImportCsvAsync(
        string csvPath,
        string entityName,
        CancellationToken cancellationToken = default);

    CsvImportPlan PlanCsvImport(
        InMemoryWorkspace targetWorkspace,
        InMemoryWorkspace importedWorkspace);
}

public readonly record struct CsvImportPlan(
    string EntityName,
    int RowCount,
    IReadOnlyList<Operation> Operations);

public interface IExportService
{
    Task ExportXmlAsync(
        InMemoryWorkspace workspace,
        string outputDirectory,
        CancellationToken cancellationToken = default);

    Task ExportCsvAsync(
        IMetaWorkspaceSource source,
        string entityName,
        string outputPath,
        CancellationToken cancellationToken = default);
}
