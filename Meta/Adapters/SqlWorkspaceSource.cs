using System.Runtime.CompilerServices;
using System.Data;
using Microsoft.Data.SqlClient;
using Meta.Core.Domain;
using Meta.Core.Operations;

namespace Meta.Adapters;

public sealed class SqlWorkspaceSource : IMetaWorkspaceSource, IAsyncDisposable
{
    private readonly SqlConnection _connection;
    private readonly SqlTransaction _transaction;
    private readonly string _schema;
    private readonly GenericModel _model;

    private SqlWorkspaceSource(
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

    public static async Task<SqlWorkspaceSource> OpenAsync(
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
            return new SqlWorkspaceSource(
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
}
