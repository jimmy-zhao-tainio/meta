using System.Runtime.CompilerServices;
using System.Data;
using Microsoft.Data.SqlClient;
using Meta.Operations.Domain;
using Meta.Operations;

namespace Meta.Surfaces.Sql;

public sealed class SqlWorkspace : IMetaWorkspace
{
    private readonly SqlWorkspaceConnection _connection;

    private SqlWorkspace(SqlWorkspaceConnection connection)
    {
        _connection = connection;
    }

    public static async Task<SqlWorkspace> OpenAsync(
        string connectionString,
        CancellationToken cancellationToken = default) =>
        new(await SqlWorkspaceConnection.OpenAsync(
                connectionString,
                SqlWorkspaceContract.Schema,
                cancellationToken)
            .ConfigureAwait(false));

    public static async Task CreateAsync(
        string connectionString,
        InMemoryWorkspace workspace,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var databaseName = await SqlWorkspaceDatabase.CreateAsync(
                connectionString,
                workspace.Model.Name,
                cancellationToken)
            .ConfigureAwait(false);
        try
        {
            await using var destination = await OpenAsync(
                    connectionString,
                    cancellationToken)
                .ConfigureAwait(false);
            destination._connection.WriteModelName(workspace.Model.Name);
            var modelName = await destination.ReadModelNameAsync(
                cancellationToken)
                .ConfigureAwait(false);
            var operations = WorkspaceSynchronization.PlanCreation(
                workspace,
                modelName);
            await destination._connection.ExecuteVerifiedAsync(
                    operations,
                    workspace,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception creationException)
        {
            try
            {
                await SqlWorkspaceDatabase.DropIfExistsAsync(
                        connectionString,
                        databaseName,
                        CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception cleanupException)
            {
                throw new AggregateException(
                    $"SQL workspace database '{databaseName}' could not be removed after creation failed.",
                    creationException,
                    cleanupException);
            }

            throw;
        }
    }

    public ValueTask<IReadOnlyList<OperationResult>> ExecuteAsync(
        IReadOnlyList<Operation> operations,
        CancellationToken cancellationToken = default) =>
        _connection.ExecuteAsync(operations, cancellationToken);

    public ValueTask DisposeAsync() => _connection.DisposeAsync();

    public ValueTask<string> ReadModelNameAsync(
        CancellationToken cancellationToken = default) =>
        _connection.ReadModelNameAsync(cancellationToken);

    public IAsyncEnumerable<string> ReadEntityNamesAsync(
        CancellationToken cancellationToken = default) =>
        _connection.ReadEntityNamesAsync(cancellationToken);

    public IAsyncEnumerable<PropertyDefinition> ReadPropertiesAsync(
        string entityName,
        CancellationToken cancellationToken = default) =>
        _connection.ReadPropertiesAsync(entityName, cancellationToken);

    public IAsyncEnumerable<RelationshipDefinition> ReadRelationshipsAsync(
        string entityName,
        CancellationToken cancellationToken = default) =>
        _connection.ReadRelationshipsAsync(entityName, cancellationToken);

    public IAsyncEnumerable<RecordData> ReadRecordsAsync(
        string entityName,
        CancellationToken cancellationToken = default) =>
        _connection.ReadRecordsAsync(entityName, cancellationToken);

    public ValueTask<long> CountRecordsAsync(
        string entityName,
        CancellationToken cancellationToken = default) =>
        _connection.CountRecordsAsync(entityName, cancellationToken);

    public ValueTask<RecordQueryResult> QueryRecordsAsync(
        string entityName,
        RecordQuery query,
        CancellationToken cancellationToken = default) =>
        _connection.QueryRecordsAsync(entityName, query, cancellationToken);

    public ValueTask<RecordData?> ReadRecordAsync(
        string entityName,
        string id,
        CancellationToken cancellationToken = default) =>
        _connection.ReadRecordAsync(entityName, id, cancellationToken);
}

public sealed class SqlSchemaSource : IMetaWorkspaceSource, IAsyncDisposable
{
    private readonly SqlWorkspaceConnection _connection;

    private SqlSchemaSource(SqlWorkspaceConnection connection)
    {
        _connection = connection;
    }

    public static async Task<SqlSchemaSource> OpenAsync(
        string connectionString,
        string schema,
        CancellationToken cancellationToken = default) =>
        new(await SqlWorkspaceConnection.OpenAsync(
                connectionString,
                schema,
                cancellationToken)
            .ConfigureAwait(false));

    public ValueTask<string> ReadModelNameAsync(
        CancellationToken cancellationToken = default) =>
        _connection.ReadModelNameAsync(cancellationToken);

    public IAsyncEnumerable<string> ReadEntityNamesAsync(
        CancellationToken cancellationToken = default) =>
        _connection.ReadEntityNamesAsync(cancellationToken);

    public IAsyncEnumerable<PropertyDefinition> ReadPropertiesAsync(
        string entityName,
        CancellationToken cancellationToken = default) =>
        _connection.ReadPropertiesAsync(entityName, cancellationToken);

    public IAsyncEnumerable<RelationshipDefinition> ReadRelationshipsAsync(
        string entityName,
        CancellationToken cancellationToken = default) =>
        _connection.ReadRelationshipsAsync(entityName, cancellationToken);

    public IAsyncEnumerable<RecordData> ReadRecordsAsync(
        string entityName,
        CancellationToken cancellationToken = default) =>
        _connection.ReadRecordsAsync(entityName, cancellationToken);

    public ValueTask<long> CountRecordsAsync(
        string entityName,
        CancellationToken cancellationToken = default) =>
        _connection.CountRecordsAsync(entityName, cancellationToken);

    public ValueTask<RecordQueryResult> QueryRecordsAsync(
        string entityName,
        RecordQuery query,
        CancellationToken cancellationToken = default) =>
        _connection.QueryRecordsAsync(entityName, query, cancellationToken);

    public ValueTask<RecordData?> ReadRecordAsync(
        string entityName,
        string id,
        CancellationToken cancellationToken = default) =>
        _connection.ReadRecordAsync(entityName, id, cancellationToken);

    public ValueTask DisposeAsync() => _connection.DisposeAsync();
}

internal sealed class SqlWorkspaceConnection : IMetaWorkspaceSource, IAsyncDisposable
{
    private readonly SqlConnection _connection;
    private readonly SqlTransaction _transaction;
    private readonly string _schema;
    private GenericModel _model;
    private bool _completed;

    private SqlWorkspaceConnection(
        SqlConnection connection,
        SqlTransaction transaction,
        string schema,
        GenericModel model)
    {
        _connection = connection;
        _transaction = transaction;
        _schema = schema;
        _model = model;
    }

    public static async Task<SqlWorkspaceConnection> OpenAsync(
        string connectionString,
        string schema,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException(
                "Connection string is required.",
                nameof(connectionString));
        }

        var effectiveSchema = string.IsNullOrWhiteSpace(schema)
            ? "dbo"
            : MetaName.Require(schema, "Schema name.");
        var connection = new SqlConnection(connectionString);
        SqlTransaction? transaction = null;
        try
        {
            await connection.OpenAsync(cancellationToken)
                .ConfigureAwait(false);
            MetaName.Require(connection.Database, "Database name.");
            transaction = (SqlTransaction)await connection
                .BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken)
                .ConfigureAwait(false);
            var model = SqlWorkspaceModelReader.Read(
                connection,
                transaction,
                effectiveSchema);
            return new SqlWorkspaceConnection(
                connection,
                transaction,
                effectiveSchema,
                model);
        }
        catch
        {
            if (transaction != null)
            {
                await transaction.DisposeAsync().ConfigureAwait(false);
            }

            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public ValueTask<string> ReadModelNameAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_model.Name);
    }

    internal void WriteModelName(string modelName)
    {
        EnsureActive();
        SqlWorkspaceModelMetadata.Write(
            _connection,
            _transaction,
            modelName);
    }

    public async IAsyncEnumerable<string> ReadEntityNamesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var entity in _model.Entities)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return entity.Name;
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    public async IAsyncEnumerable<PropertyDefinition> ReadPropertiesAsync(
        string entityName,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var entity = await ReadEntityAsync(entityName, cancellationToken)
            .ConfigureAwait(false);
        foreach (var property in entity.Properties)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new PropertyDefinition(
                property.Name,
                IsRequired: !property.IsNullable);
        }
    }

    public async IAsyncEnumerable<RelationshipDefinition> ReadRelationshipsAsync(
        string entityName,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var entity = await ReadEntityAsync(entityName, cancellationToken)
            .ConfigureAwait(false);
        foreach (var relationship in entity.Relationships)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new RelationshipDefinition(
                relationship.Entity,
                string.IsNullOrEmpty(relationship.Role)
                    ? null
                    : relationship.Role,
                IsRequired: !relationship.IsNullable);
        }
    }

    public async IAsyncEnumerable<RecordData> ReadRecordsAsync(
        string entityName,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var entity = await ReadEntityAsync(entityName, cancellationToken)
            .ConfigureAwait(false);
        await foreach (var record in SqlServerImportReader
                           .StreamRowsAsync(
                               _connection,
                               _transaction,
                               _schema,
                               entity,
                               cancellationToken)
                           .ConfigureAwait(false))
        {
            yield return record;
        }
    }

    public async ValueTask<RecordData?> ReadRecordAsync(
        string entityName,
        string id,
        CancellationToken cancellationToken = default)
    {
        var entity = await ReadEntityAsync(entityName, cancellationToken)
            .ConfigureAwait(false);
        return await SqlServerImportReader.ReadRowAsync(
                _connection,
                _transaction,
                _schema,
                entity,
                MetaIdentity.Require(id, "Record Id."),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask<long> CountRecordsAsync(
        string entityName,
        CancellationToken cancellationToken = default)
    {
        var entity = await ReadEntityAsync(entityName, cancellationToken)
            .ConfigureAwait(false);
        return await SqlServerImportReader.CountRowsAsync(
                _connection,
                _transaction,
                _schema,
                entity,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask<RecordQueryResult> QueryRecordsAsync(
        string entityName,
        RecordQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var entity = await ReadEntityAsync(entityName, cancellationToken)
            .ConfigureAwait(false);
        return await SqlServerImportReader.QueryRowsAsync(
                _connection,
                _transaction,
                _schema,
                entity,
                query,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public ValueTask<IReadOnlyList<OperationResult>> ExecuteAsync(
        IReadOnlyList<Operation> operations,
        CancellationToken cancellationToken = default)
    {
        ValidateExecution(operations, cancellationToken);

        if (operations.Count == 0)
        {
            return ValueTask.FromResult<IReadOnlyList<OperationResult>>([]);
        }

        try
        {
            var results = ApplyOperations(operations);
            _transaction.Commit();
            _completed = true;
            return ValueTask.FromResult<IReadOnlyList<OperationResult>>(results);
        }
        catch (Exception executionFailure)
        {
            FailExecution(executionFailure);
            throw;
        }
    }

    internal async ValueTask<IReadOnlyList<OperationResult>> ExecuteVerifiedAsync(
        IReadOnlyList<Operation> operations,
        InMemoryWorkspace expected,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ValidateExecution(operations, cancellationToken);
        try
        {
            var results = ApplyOperations(operations);
            var actual = await WorkspaceComposition.MaterializeAsync(
                    this,
                    cancellationToken)
                .ConfigureAwait(false);
            var difference = InMemoryWorkspaceComparer.FindDifference(
                expected,
                actual);
            if (difference != null)
            {
                throw new InvalidOperationException(
                    $"Created SQL workspace changed metadata semantics. {difference}");
            }

            _transaction.Commit();
            _completed = true;
            return results;
        }
        catch (Exception executionFailure)
        {
            FailExecution(executionFailure);
            throw;
        }
    }

    private IReadOnlyList<OperationResult> ApplyOperations(
        IReadOnlyList<Operation> operations)
    {
        var target = new SqlOperationTarget(
            _connection,
            _transaction,
            _schema);
        var results = new List<OperationResult>(operations.Count);
        for (var index = 0; index < operations.Count; index++)
        {
            var operation = operations[index];
            try
            {
                results.Add(operation.ApplyTo(target));
                if (operation is Operation.ModelOperation or
                    Operation.RefactorOperation)
                {
                    _model = SqlWorkspaceModelReader.Read(
                        _connection,
                        _transaction,
                        _schema);
                }
            }
            catch (MetaOperationException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new MetaOperationException(
                    index,
                    operation,
                    exception);
            }
        }

        return results;
    }

    public async ValueTask DisposeAsync()
    {
        await _transaction.DisposeAsync().ConfigureAwait(false);
        await _connection.DisposeAsync().ConfigureAwait(false);
    }

    private ValueTask<GenericEntity> ReadEntityAsync(
        string entityName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var name = MetaName.Require(entityName, "Entity name.");
        var entity = _model.FindEntity(name) ??
                     throw new InvalidOperationException(
                         $"Entity '{name}' does not exist.");
        return ValueTask.FromResult(entity);
    }

    private void EnsureActive()
    {
        if (_completed)
        {
            throw new InvalidOperationException(
                "SQL workspace execution has completed.");
        }
    }

    private void FailExecution(Exception executionFailure)
    {
        _completed = true;
        try
        {
            _transaction.Rollback();
        }
        catch (Exception rollbackFailure)
        {
            throw new AggregateException(
                "SQL workspace execution and rollback both failed.",
                executionFailure,
                rollbackFailure);
        }
    }

    private void ValidateExecution(
        IReadOnlyList<Operation> operations,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operations);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureActive();
        if (operations.Any(operation => operation == null))
        {
            throw new ArgumentException(
                "Operations cannot contain null.",
                nameof(operations));
        }
    }
}
