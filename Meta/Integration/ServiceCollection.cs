using Meta.Core.Services;

namespace Meta.Integration;

public sealed class ServiceCollection
{
    public IImportService ImportService { get; }
    public IExportService ExportService { get; }
    public IInstanceDiffService InstanceDiffService { get; }
    public IWorkspaceMergeService WorkspaceMergeService { get; }
    public SqlServerDeploymentService SqlServerDeploymentService { get; }

    public ServiceCollection()
    {
        InstanceDiffService = new InstanceDiffService();
        WorkspaceMergeService = new WorkspaceMergeService();
        ImportService = new ImportService();
        ExportService = new ExportService();
        SqlServerDeploymentService = new SqlServerDeploymentService();
    }
}
