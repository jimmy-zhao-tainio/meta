using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Meta.Operations.Domain;
using Meta.Operations;
using Meta.Surfaces;
using Meta.Surfaces.Sql;

namespace Meta.Integration;

public sealed class ImportService : IImportService
{
    public Task<InMemoryWorkspace> ImportSqlAsync(string connectionString, string schema, CancellationToken cancellationToken = default)
    {
        return MetaSqlReader.ReadAsync(
            connectionString,
            schema,
            cancellationToken);
    }

    public async Task<InMemoryWorkspace> ImportCsvAsync(
        string csvPath,
        string entityName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(csvPath))
        {
            throw new ArgumentException("CSV file path is required.", nameof(csvPath));
        }

        if (string.IsNullOrWhiteSpace(entityName))
        {
            throw new ArgumentException("Entity name is required.", nameof(entityName));
        }

        var fullCsvPath = Path.GetFullPath(csvPath);
        if (!File.Exists(fullCsvPath))
        {
            throw new FileNotFoundException($"CSV file '{fullCsvPath}' was not found.", fullCsvPath);
        }

        var csvText = await File.ReadAllTextAsync(fullCsvPath, cancellationToken).ConfigureAwait(false);
        var parsedRows = CsvImportSupport.ParseRows(csvText);
        if (parsedRows.Count == 0)
        {
            throw new InvalidOperationException("CSV file must include a header row.");
        }

        var header = parsedRows[0];
        if (header.Count == 0)
        {
            throw new InvalidOperationException("CSV header row is empty.");
        }

        const string idColumn = "Id";
        var idColumnIndex = CsvImportSupport.ResolveIdColumnIndex(header, idColumn);

        var entityIdentifier = CsvImportSupport.NormalizeIdentifier(entityName, fallback: "Entity");
        var modelName = CsvImportSupport.NormalizeIdentifier(entityIdentifier + "Model", fallback: "ImportedModel");
        var columnPlans = CsvImportSupport.BuildColumnPlans(header, idColumnIndex);

        var dataRows = new List<IReadOnlyList<string>>();
        for (var index = 1; index < parsedRows.Count; index++)
        {
            var row = parsedRows[index];
            if (row.Count > header.Count)
            {
                throw new InvalidOperationException(
                    $"CSV row {index + 1} has {row.Count.ToString(CultureInfo.InvariantCulture)} values but header has {header.Count.ToString(CultureInfo.InvariantCulture)} columns.");
            }

            if (CsvImportSupport.IsRowCompletelyEmpty(row))
            {
                continue;
            }

            dataRows.Add(row);
        }

        var workspace = new InMemoryWorkspace(
            new GenericModel
            {
                Name = modelName,
            },
            new GenericInstance
            {
                ModelName = modelName,
            });

        var entity = new GenericEntity
        {
            Name = entityIdentifier,
        };
        workspace.Model.Entities.Add(entity);

        foreach (var plan in columnPlans)
        {
            var values = dataRows.Select(row => CsvImportSupport.GetCellValue(row, plan.ColumnIndex)).ToList();
            var hasEmpty = values.Any(value => string.IsNullOrWhiteSpace(value));

            entity.Properties.Add(new GenericProperty
            {
                Name = plan.PropertyName,
                IsNullable = hasEmpty,
            });
        }

        var records = workspace.Instance.GetOrCreateEntityRecords(entity.Name);
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var rowIndex = 0; rowIndex < dataRows.Count; rowIndex++)
        {
            var dataRow = dataRows[rowIndex];
            var recordId = CsvImportSupport.GetCellValue(dataRow, idColumnIndex);
            if (string.IsNullOrWhiteSpace(recordId))
            {
                throw new InvalidOperationException(
                    $"CSV row {rowIndex + 2} is missing required Id value from column '{header[idColumnIndex]}'.");
            }

            if (!MetaIdentity.TryValidate(recordId, out var identityError))
            {
                throw new InvalidOperationException(
                    $"CSV row {rowIndex + 2} has invalid Id value in column '{header[idColumnIndex]}'. {identityError}");
            }

            if (!ids.Add(recordId))
            {
                throw new InvalidOperationException(
                    $"CSV contains duplicate Id '{recordId}' in column '{header[idColumnIndex]}'.");
            }

            var record = new GenericRecord
            {
                Id = recordId,
            };

            foreach (var plan in columnPlans)
            {
                var cellValue = CsvImportSupport.GetCellValue(dataRow, plan.ColumnIndex);
                if (string.IsNullOrWhiteSpace(cellValue))
                {
                    continue;
                }

                record.Values[plan.PropertyName] = cellValue;
            }

            records.Add(record);
        }

        return workspace;
    }

    public CsvImportPlan PlanCsvImport(
        InMemoryWorkspace targetWorkspace,
        InMemoryWorkspace importedWorkspace)
    {
        ArgumentNullException.ThrowIfNull(targetWorkspace);
        ArgumentNullException.ThrowIfNull(importedWorkspace);

        var importedEntity = importedWorkspace.Model.Entities.Single();
        var importedRows = importedWorkspace.Instance.RecordsByEntity[importedEntity.Name];
        var existingEntity = targetWorkspace.Model.FindEntity(importedEntity.Name);
        var operations = existingEntity == null
            ? PlanNewEntityImport(importedEntity, importedRows)
            : PlanExistingEntityImport(
                targetWorkspace,
                existingEntity,
                importedEntity,
                importedRows);

        return new CsvImportPlan(
            importedEntity.Name,
            importedRows.Count,
            operations);
    }

    private static IReadOnlyList<Operation> PlanNewEntityImport(
        GenericEntity importedEntity,
        IReadOnlyList<GenericRecord> importedRows)
    {
        var operations = new List<Operation>
        {
            new Operation.AddEntity(importedEntity.Name),
        };

        operations.AddRange(importedEntity.Properties.Select(property =>
            new Operation.AddProperty(
                importedEntity.Name,
                property.Name,
                IsRequired: !property.IsNullable)));
        operations.AddRange(importedRows
            .OrderBy(record => record.Id, MetaIdentity.Comparer)
            .Select(record => new Operation.InsertRecord(
                importedEntity.Name,
                record.Id,
                record.Values)));
        return operations;
    }

    private static IReadOnlyList<Operation> PlanExistingEntityImport(
        InMemoryWorkspace targetWorkspace,
        GenericEntity existingEntity,
        GenericEntity importedEntity,
        IReadOnlyList<GenericRecord> importedRows)
    {
        var existingPropertiesByName = existingEntity.Properties
            .ToDictionary(item => item.Name, MetaName.Comparer);
        var existingRelationshipsByName = existingEntity.Relationships
            .ToDictionary(item => item.GetColumnName(), MetaName.Comparer);
        foreach (var importedProperty in importedEntity.Properties)
        {
            if (existingPropertiesByName.ContainsKey(importedProperty.Name) ||
                existingRelationshipsByName.ContainsKey(importedProperty.Name))
            {
                continue;
            }

            throw new InvalidOperationException(
                $"CSV column '{importedProperty.Name}' does not match existing property or relationship on entity '{existingEntity.Name}'.");
        }

        var existingRows = targetWorkspace.Instance.GetOrCreateEntityRecords(existingEntity.Name);
        var rowsById = existingRows.ToDictionary(item => item.Id, MetaIdentity.Comparer);
        ValidateCsvImportPreflight(
            existingEntity,
            importedEntity,
            importedRows,
            rowsById);

        var importedIds = importedRows
            .Select(record => record.Id)
            .ToHashSet(MetaIdentity.Comparer);
        var operations = new List<Operation>();
        var deferredRelationships = new List<Operation>();

        foreach (var importedRow in importedRows.OrderBy(record => record.Id, MetaIdentity.Comparer))
        {
            if (!rowsById.ContainsKey(importedRow.Id))
            {
                var values = importedEntity.Properties
                    .Where(property => existingPropertiesByName.ContainsKey(property.Name))
                    .Where(property => importedRow.Values.ContainsKey(property.Name))
                    .ToDictionary(
                        property => existingPropertiesByName[property.Name].Name,
                        property => importedRow.Values[property.Name],
                        MetaName.Comparer);
                var requiredRelationships = importedEntity.Properties
                    .Where(property => existingRelationshipsByName.TryGetValue(
                        property.Name,
                        out var relationship) &&
                        !relationship.IsNullable)
                    .ToDictionary(
                        property => existingRelationshipsByName[property.Name].GetColumnName(),
                        property => importedRow.Values[property.Name],
                        MetaName.Comparer);

                operations.Add(new Operation.InsertRecord(
                    existingEntity.Name,
                    importedRow.Id,
                    values,
                    requiredRelationships));
            }
            else
            {
                foreach (var importedProperty in importedEntity.Properties)
                {
                    var name = importedProperty.Name;
                    var hasValue = importedRow.Values.TryGetValue(name, out var value);
                    if (existingPropertiesByName.TryGetValue(name, out var property))
                    {
                        operations.Add(hasValue && !string.IsNullOrWhiteSpace(value)
                            ? new Operation.SetProperty(
                                existingEntity.Name,
                                importedRow.Id,
                                property.Name,
                                value)
                            : new Operation.ClearProperty(
                                existingEntity.Name,
                                importedRow.Id,
                                property.Name));
                        continue;
                    }

                    var relationship = existingRelationshipsByName[name];
                    if (!hasValue || string.IsNullOrWhiteSpace(value))
                    {
                        operations.Add(new Operation.ClearRelationship(
                            existingEntity.Name,
                            importedRow.Id,
                            relationship.GetColumnName()));
                        continue;
                    }

                    AddRelationshipOperation(
                        operations,
                        deferredRelationships,
                        targetWorkspace,
                        existingEntity,
                        importedRow.Id,
                        relationship,
                        value,
                        importedIds);
                }
            }

            foreach (var importedProperty in importedEntity.Properties)
            {
                if (!existingRelationshipsByName.TryGetValue(
                        importedProperty.Name,
                        out var relationship) ||
                    !relationship.IsNullable ||
                    !importedRow.Values.TryGetValue(importedProperty.Name, out var value) ||
                    string.IsNullOrWhiteSpace(value) ||
                    rowsById.ContainsKey(importedRow.Id))
                {
                    continue;
                }

                AddRelationshipOperation(
                    operations,
                    deferredRelationships,
                    targetWorkspace,
                    existingEntity,
                    importedRow.Id,
                    relationship,
                    value,
                    importedIds);
            }
        }

        operations.AddRange(deferredRelationships);
        return operations;
    }

    private static void AddRelationshipOperation(
        ICollection<Operation> operations,
        ICollection<Operation> deferredRelationships,
        InMemoryWorkspace targetWorkspace,
        GenericEntity sourceEntity,
        string sourceId,
        GenericRelationship relationship,
        string targetId,
        IReadOnlySet<string> importedIds)
    {
        var operation = new Operation.SetRelationship(
            sourceEntity.Name,
            sourceId,
            relationship.GetColumnName(),
            targetId);
        var targetWillBeImported = MetaName.Comparer.Equals(
                                       relationship.Entity,
                                       sourceEntity.Name) &&
                                   importedIds.Contains(targetId) &&
                                   !targetWorkspace.Instance.GetOrCreateEntityRecords(relationship.Entity)
                                       .Any(record => MetaIdentity.Comparer.Equals(record.Id, targetId));
        (targetWillBeImported ? deferredRelationships : operations).Add(operation);
    }

    private static void ValidateCsvImportPreflight(
        GenericEntity existingEntity,
        GenericEntity importedEntity,
        IReadOnlyList<GenericRecord> importedRows,
        IReadOnlyDictionary<string, GenericRecord> rowsById)
    {
        var existingPropertiesByName = existingEntity.Properties
            .ToDictionary(item => item.Name, MetaName.Comparer);
        var existingRelationshipsByName = existingEntity.Relationships
            .ToDictionary(item => item.GetColumnName(), MetaName.Comparer);
        var importedColumnNames = importedEntity.Properties
            .Select(item => item.Name)
            .ToHashSet(MetaName.Comparer);

        foreach (var importedRow in importedRows.OrderBy(record => record.Id, MetaIdentity.Comparer))
        {
            var isExistingRow = rowsById.ContainsKey(importedRow.Id);

            foreach (var importedProperty in importedEntity.Properties)
            {
                var name = importedProperty.Name;
                var hasValue = importedRow.Values.TryGetValue(name, out var value);
                var isBlank = !hasValue || string.IsNullOrWhiteSpace(value);

                if (existingRelationshipsByName.TryGetValue(name, out var existingRelationship))
                {
                    if (!existingRelationship.IsNullable && isBlank)
                    {
                        throw new InvalidOperationException(
                            $"CSV row '{importedRow.Id}' leaves required relationship '{name}' blank on entity '{existingEntity.Name}'.");
                    }

                    continue;
                }

                if (existingPropertiesByName.TryGetValue(name, out var existingProperty) &&
                    !existingProperty.IsNullable &&
                    isBlank)
                {
                    throw new InvalidOperationException(
                        $"CSV row '{importedRow.Id}' leaves required property '{name}' blank on entity '{existingEntity.Name}'.");
                }
            }

            if (isExistingRow)
            {
                continue;
            }

            foreach (var requiredProperty in existingEntity.Properties
                         .Where(item => !item.IsNullable)
                         .OrderBy(item => item.Name, MetaName.Comparer))
            {
                if (!importedColumnNames.Contains(requiredProperty.Name))
                {
                    throw new InvalidOperationException(
                        $"CSV row '{importedRow.Id}' cannot create new '{existingEntity.Name}' because required property '{requiredProperty.Name}' is missing from the import columns.");
                }

                if (!importedRow.Values.TryGetValue(requiredProperty.Name, out var propertyValue) ||
                    string.IsNullOrWhiteSpace(propertyValue))
                {
                    throw new InvalidOperationException(
                        $"CSV row '{importedRow.Id}' leaves required property '{requiredProperty.Name}' blank on entity '{existingEntity.Name}'.");
                }
            }

            foreach (var requiredRelationshipName in existingEntity.Relationships
                         .Where(item => !item.IsNullable)
                         .Select(item => item.GetColumnName())
                         .OrderBy(name => name, MetaName.Comparer))
            {
                if (!importedColumnNames.Contains(requiredRelationshipName))
                {
                    throw new InvalidOperationException(
                        $"CSV row '{importedRow.Id}' cannot create new '{existingEntity.Name}' because required relationship '{requiredRelationshipName}' is missing from the import columns.");
                }

                if (!importedRow.Values.TryGetValue(requiredRelationshipName, out var relationshipValue) ||
                    string.IsNullOrWhiteSpace(relationshipValue))
                {
                    throw new InvalidOperationException(
                        $"CSV row '{importedRow.Id}' leaves required relationship '{requiredRelationshipName}' blank on entity '{existingEntity.Name}'.");
                }
            }
        }
    }

}





