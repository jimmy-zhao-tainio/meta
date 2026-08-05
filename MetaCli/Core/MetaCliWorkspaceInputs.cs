using Meta.Core.Domain;
using Meta.Core.Operations;
using Meta.Core.Serialization;

namespace MetaCli.Core;

public abstract record MetaCliWorkspaceParameter(string Name);

public sealed record MetaCliWorkspaceInput(string Parameter)
    : MetaCliWorkspaceParameter(Parameter);

public sealed record MetaCliWorkspaceTarget(string Parameter)
    : MetaCliWorkspaceParameter(Parameter);

public sealed record MetaCliWorkspaceOutput(
    string Output,
    string XmlParameter,
    string CSharpParameter,
    string SqlParameter,
    string ConnectionEnvironmentParameter)
    : MetaCliWorkspaceParameter(Output);

public static class MetaCliWorkspace
{
    public static Task<IMetaWorkspace> OpenAsync(
        string directory,
        CancellationToken cancellationToken = default) =>
        MetaCliWorkspaceResolver.OpenAsync(directory, cancellationToken);

    public static async Task<MetaCliModel> LoadModelAsync(
        string directory,
        CancellationToken cancellationToken = default)
    {
        await using var workspace = await OpenAsync(directory, cancellationToken)
            .ConfigureAwait(false);
        var state = await WorkspaceComposition.MaterializeAsync(
                workspace,
                cancellationToken)
            .ConfigureAwait(false);
        return TypedWorkspaceModelMapper.FromInMemoryWorkspace(
            state,
            static () => MetaCliModel.CreateEmpty());
    }

    public static string OutputLocation(
        MetaCliInvocation invocation,
        string xmlParameter = "xml",
        string csharpParameter = "csharp",
        string sqlParameter = "sql")
    {
        ArgumentNullException.ThrowIfNull(invocation);
        return invocation.Optional(xmlParameter) ??
            invocation.Optional(csharpParameter) ??
            invocation.Optional(sqlParameter) ??
            throw new InvalidOperationException("A workspace output is required.");
    }

    public static string? OptionalOutputLocation(
        MetaCliInvocation invocation,
        string xmlParameter = "output-xml",
        string csharpParameter = "output-csharp",
        string sqlParameter = "output-sql")
    {
        ArgumentNullException.ThrowIfNull(invocation);
        return Optional(invocation, xmlParameter) ??
            Optional(invocation, csharpParameter) ??
            Optional(invocation, sqlParameter);
    }

    public static void DescribeXml(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        MetaCliWorkspaceDescriptor.WriteXml(directory);
    }

    public static MetaCliWorkspaceInput Open(string parameter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parameter);
        return new MetaCliWorkspaceInput(parameter.Trim());
    }

    public static MetaCliWorkspaceTarget Target(string parameter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parameter);
        return new MetaCliWorkspaceTarget(parameter.Trim());
    }

    public static MetaCliWorkspaceOutput Create(
        string output,
        string xmlParameter,
        string csharpParameter,
        string sqlParameter,
        string connectionEnvironmentParameter = "connection-env")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(output);
        ArgumentException.ThrowIfNullOrWhiteSpace(xmlParameter);
        ArgumentException.ThrowIfNullOrWhiteSpace(csharpParameter);
        ArgumentException.ThrowIfNullOrWhiteSpace(sqlParameter);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionEnvironmentParameter);
        return new MetaCliWorkspaceOutput(
            output.Trim(),
            xmlParameter.Trim(),
            csharpParameter.Trim(),
            sqlParameter.Trim(),
            connectionEnvironmentParameter.Trim());
    }

    private static string? Optional(
        MetaCliInvocation invocation,
        string parameter)
    {
        try
        {
            return invocation.Optional(parameter);
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
    }
}

internal abstract record MetaCliWorkspaceCreation(string Directory);

internal sealed record MetaCliXmlWorkspaceCreation(string WorkspaceDirectory)
    : MetaCliWorkspaceCreation(WorkspaceDirectory);

internal sealed record MetaCliCSharpWorkspaceCreation(string WorkspaceDirectory)
    : MetaCliWorkspaceCreation(WorkspaceDirectory);

internal sealed record MetaCliSqlWorkspaceCreation(
    string WorkspaceDirectory,
    string ConnectionEnvironmentVariable)
    : MetaCliWorkspaceCreation(WorkspaceDirectory);

public sealed class MetaCliWorkspaces : IAsyncDisposable
{
    private readonly MetaCliInvocation invocation;
    private readonly IReadOnlyDictionary<string, IMetaWorkspace> workspaces;
    private readonly IReadOnlyDictionary<string, MetaCliWorkspaceOutput> outputs;

    internal MetaCliWorkspaces(
        MetaCliInvocation invocation,
        IReadOnlyDictionary<string, IMetaWorkspace> workspaces,
        IReadOnlyDictionary<string, MetaCliWorkspaceOutput> outputs)
    {
        this.invocation = invocation;
        this.workspaces = workspaces;
        this.outputs = outputs;
    }

    public Task CreateAsync(
        string output,
        InMemoryWorkspace workspace,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(output);
        ArgumentNullException.ThrowIfNull(workspace);
        if (!outputs.TryGetValue(output.Trim(), out var binding))
        {
            throw new KeyNotFoundException(
                $"Workspace output '{output}' was not bound for this command.");
        }

        return MetaCliWorkspaceResolver.CreateAsync(
            ResolveCreation(invocation, binding),
            workspace,
            cancellationToken);
    }

    public Task CreateAsync<TModel>(
        string output,
        TModel model,
        CancellationToken cancellationToken = default)
        where TModel : class, IMetaWorkspaceModel<TModel>
    {
        ArgumentNullException.ThrowIfNull(model);
        return CreateAsync(
            output,
            TypedWorkspaceModelMapper.ToInMemoryWorkspace(model),
            cancellationToken);
    }

    public IMetaWorkspace Required(string parameter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parameter);
        return workspaces.TryGetValue(parameter.Trim(), out var workspace)
            ? workspace
            : throw new KeyNotFoundException(
                $"Workspace input '{parameter}' was not bound for this command.");
    }

    public IMetaWorkspace? Optional(string parameter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parameter);
        return workspaces.GetValueOrDefault(parameter.Trim());
    }

    public async Task<TModel> RequiredAsync<TModel>(
        string parameter,
        CancellationToken cancellationToken = default)
        where TModel : class, IMetaWorkspaceModel<TModel>
    {
        var state = await WorkspaceComposition.MaterializeAsync(
                Required(parameter),
                cancellationToken)
            .ConfigureAwait(false);
        return TypedWorkspaceModelMapper.FromInMemoryWorkspace(
            state,
            static () => TModel.CreateEmpty());
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var workspace in workspaces.Values.Reverse())
        {
            await workspace.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static MetaCliWorkspaceCreation ResolveCreation(
        MetaCliInvocation invocation,
        MetaCliWorkspaceOutput output)
    {
        var xml = Optional(invocation, output.XmlParameter);
        var csharp = Optional(invocation, output.CSharpParameter);
        var sql = Optional(invocation, output.SqlParameter);
        var selected = new[] { xml, csharp, sql }.Count(static value => !string.IsNullOrWhiteSpace(value));
        if (selected != 1)
        {
            throw new InvalidOperationException(
                $"Specify exactly one of --{output.XmlParameter}, --{output.CSharpParameter}, or --{output.SqlParameter}.");
        }

        if (!string.IsNullOrWhiteSpace(xml))
        {
            return new MetaCliXmlWorkspaceCreation(Path.GetFullPath(xml));
        }

        if (!string.IsNullOrWhiteSpace(csharp))
        {
            return new MetaCliCSharpWorkspaceCreation(Path.GetFullPath(csharp));
        }

        var connectionEnvironment = Optional(invocation, output.ConnectionEnvironmentParameter);
        if (string.IsNullOrWhiteSpace(connectionEnvironment))
        {
            throw new InvalidOperationException(
                $"--{output.ConnectionEnvironmentParameter} is required with --{output.SqlParameter}.");
        }

        return new MetaCliSqlWorkspaceCreation(
            Path.GetFullPath(sql!),
            connectionEnvironment.Trim());
    }

    private static string? Optional(MetaCliInvocation invocation, string parameter)
    {
        try
        {
            return invocation.Optional(parameter);
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
    }
}
