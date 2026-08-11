using Meta.Operations.Domain;
using Meta.Operations;
using Meta.Integration;

namespace MetaCli.Core;

public abstract record MetaCliWorkspaceParameter(string Name);

public sealed record MetaCliWorkspaceInput(string Parameter)
    : MetaCliWorkspaceParameter(Parameter);

public sealed record MetaCliOptionalWorkspaceInput(string Parameter)
    : MetaCliWorkspaceParameter(Parameter);

public sealed record MetaCliWorkspaceTarget(string Parameter)
    : MetaCliWorkspaceParameter(Parameter);

public sealed record MetaCliWorkspaceOutput(
    string Output,
    string XmlParameter,
    string CSharpParameter,
    string SqlParameter,
    string ConnectionEnvironmentParameter,
    string? LocationParameter = null)
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

    public static MetaCliOptionalWorkspaceInput OpenOptional(string parameter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(parameter);
        return new MetaCliOptionalWorkspaceInput(parameter.Trim());
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

    public static MetaCliWorkspaceOutput CreateAt(
        string output,
        string locationParameter,
        string xmlParameter,
        string csharpParameter,
        string sqlParameter,
        string connectionEnvironmentParameter = "connection-env")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(output);
        ArgumentException.ThrowIfNullOrWhiteSpace(locationParameter);
        ArgumentException.ThrowIfNullOrWhiteSpace(xmlParameter);
        ArgumentException.ThrowIfNullOrWhiteSpace(csharpParameter);
        ArgumentException.ThrowIfNullOrWhiteSpace(sqlParameter);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionEnvironmentParameter);
        return new MetaCliWorkspaceOutput(
            output.Trim(),
            xmlParameter.Trim(),
            csharpParameter.Trim(),
            sqlParameter.Trim(),
            connectionEnvironmentParameter.Trim(),
            locationParameter.Trim());
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
        where TModel : class
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
        where TModel : class, new()
    {
        var state = await WorkspaceComposition.MaterializeAsync(
                Required(parameter),
                cancellationToken)
            .ConfigureAwait(false);
        return TypedWorkspaceModelMapper.FromInMemoryWorkspace(
            state,
            static () => new TModel());
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
        if (output.LocationParameter is not null)
        {
            var location = Optional(invocation, output.LocationParameter);
            if (string.IsNullOrWhiteSpace(location))
            {
                throw new InvalidOperationException(
                    $"--{output.LocationParameter} is required.");
            }

            var selectedSurfaces = new[]
            {
                IsPresent(invocation, output.XmlParameter),
                IsPresent(invocation, output.CSharpParameter),
                IsPresent(invocation, output.SqlParameter),
            }.Count(static value => value);
            if (selectedSurfaces != 1)
            {
                throw new InvalidOperationException(
                    $"Specify exactly one of --{output.XmlParameter}, --{output.CSharpParameter}, or --{output.SqlParameter}.");
            }

            if (IsPresent(invocation, output.XmlParameter))
            {
                return new MetaCliXmlWorkspaceCreation(Path.GetFullPath(location));
            }

            if (IsPresent(invocation, output.CSharpParameter))
            {
                return new MetaCliCSharpWorkspaceCreation(Path.GetFullPath(location));
            }

            var sqlConnectionEnvironment = Optional(invocation, output.ConnectionEnvironmentParameter);
            if (string.IsNullOrWhiteSpace(sqlConnectionEnvironment))
            {
                throw new InvalidOperationException(
                    $"--{output.ConnectionEnvironmentParameter} is required with --{output.SqlParameter}.");
            }

            return new MetaCliSqlWorkspaceCreation(
                Path.GetFullPath(location),
                sqlConnectionEnvironment.Trim());
        }

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

    private static bool IsPresent(MetaCliInvocation invocation, string parameter)
    {
        try
        {
            return invocation.IsPresent(parameter);
        }
        catch (KeyNotFoundException)
        {
            return false;
        }
    }
}
