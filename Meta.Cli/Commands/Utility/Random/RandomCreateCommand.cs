using MetaWorkspaceConfig = Meta.Core.WorkspaceConfig.Generated.MetaWorkspace;

internal sealed partial class CliRuntime
{
    async Task<int> RandomCreateAsync(string[] commandArgs)
    {
        var workspacePath = !string.IsNullOrWhiteSpace(globalWorkspacePath)
            ? globalWorkspacePath
            : Path.Combine("Samples", "Random100");
        var entities = 100;
        var seed = 20260213;
        var propertiesMin = 2;
        var propertiesMax = 8;
        var rowsMin = 1;
        var rowsMax = 12;
        var maxRelationships = 4;
        var modelName = "RandomModel100";
        var dbConnection = "Server=localhost;Trusted_Connection=True;TrustServerCertificate=True;";
        var dbName = "MetadataRandom100";
        var applyDb = true;
    
        for (var i = 2; i < commandArgs.Length; i++)
        {
            var arg = commandArgs[i];
            switch (arg.ToLowerInvariant())
            {
                case "--workspace":
                    if (i + 1 >= commandArgs.Length)
                    {
                        return PrintArgumentError("Error: --workspace requires a path.");
                    }
    
                    workspacePath = commandArgs[++i];
                    break;
                case "--entities":
                    if (i + 1 >= commandArgs.Length || !int.TryParse(commandArgs[++i], out entities) || entities <= 0)
                    {
                        return PrintArgumentError("Error: --entities requires a positive integer.");
                    }
    
                    break;
                case "--seed":
                    if (i + 1 >= commandArgs.Length || !int.TryParse(commandArgs[++i], out seed))
                    {
                        return PrintArgumentError("Error: --seed requires an integer.");
                    }
    
                    break;
                case "--properties-min":
                    if (i + 1 >= commandArgs.Length || !int.TryParse(commandArgs[++i], out propertiesMin) || propertiesMin < 1)
                    {
                        return PrintArgumentError("Error: --properties-min requires an integer >= 1.");
                    }
    
                    break;
                case "--properties-max":
                    if (i + 1 >= commandArgs.Length || !int.TryParse(commandArgs[++i], out propertiesMax) || propertiesMax < 1)
                    {
                        return PrintArgumentError("Error: --properties-max requires an integer >= 1.");
                    }
    
                    break;
                case "--rows-min":
                    if (i + 1 >= commandArgs.Length || !int.TryParse(commandArgs[++i], out rowsMin) || rowsMin < 1)
                    {
                        return PrintArgumentError("Error: --rows-min requires an integer >= 1.");
                    }
    
                    break;
                case "--rows-max":
                    if (i + 1 >= commandArgs.Length || !int.TryParse(commandArgs[++i], out rowsMax) || rowsMax < 1)
                    {
                        return PrintArgumentError("Error: --rows-max requires an integer >= 1.");
                    }
    
                    break;
                case "--max-relationships":
                    if (i + 1 >= commandArgs.Length || !int.TryParse(commandArgs[++i], out maxRelationships) || maxRelationships < 0)
                    {
                        return PrintArgumentError("Error: --max-relationships requires an integer >= 0.");
                    }
    
                    break;
                case "--model-name":
                    if (i + 1 >= commandArgs.Length)
                    {
                        return PrintArgumentError("Error: --model-name requires a value.");
                    }
    
                    modelName = commandArgs[++i];
                    break;
                case "--database-connection":
                    if (i + 1 >= commandArgs.Length)
                    {
                        return PrintArgumentError("Error: --database-connection requires a value.");
                    }
    
                    dbConnection = commandArgs[++i];
                    break;
                case "--database-name":
                    if (i + 1 >= commandArgs.Length)
                    {
                        return PrintArgumentError("Error: --database-name requires a value.");
                    }
    
                    dbName = commandArgs[++i];
                    break;
                case "--no-database":
                    applyDb = false;
                    break;
                default:
                    return PrintArgumentError($"Error: unknown random create option '{arg}'.");
            }
        }
    
        if (propertiesMin > propertiesMax)
        {
            return PrintArgumentError("Error: --properties-min cannot be greater than --properties-max.");
        }
    
        if (rowsMin > rowsMax)
        {
            return PrintArgumentError("Error: --rows-min cannot be greater than --rows-max.");
        }
    
        if (maxRelationships < 0)
        {
            return PrintArgumentError("Error: --max-relationships cannot be negative.");
        }
    
        try
        {
            var randomWorkspace = BuildRandomWorkspace(
                modelName: modelName,
                entityCount: entities,
                seed: seed,
                minAdditionalProperties: propertiesMin,
                maxAdditionalProperties: propertiesMax,
                maxRelationshipsPerEntity: maxRelationships,
                minRowsPerEntity: rowsMin,
                maxRowsPerEntity: rowsMax);
    
            var diagnostics = services.ValidationService.Validate(randomWorkspace.Workspace);
            randomWorkspace.Workspace.Diagnostics = diagnostics;
            if (diagnostics.HasErrors || (globalStrict && diagnostics.WarningCount > 0))
            {
                PrintHumanFailure("Cannot create randomized workspace", BuildHumanValidationBlockers("random.create", Array.Empty<WorkspaceOp>(), diagnostics));
                return 2;
            }
    
            var workspaceRoot = Path.GetFullPath(workspacePath);
            randomWorkspace.Workspace.WorkspaceRootPath = workspaceRoot;
            randomWorkspace.Workspace.MetadataRootPath = Path.Combine(workspaceRoot, "metadata");
            await services.WorkspaceService.SaveAsync(randomWorkspace.Workspace).ConfigureAwait(false);
    
            var generatedRoot = Path.Combine(workspaceRoot, "generated");
            var sqlRoot = Path.Combine(generatedRoot, "sql");
            var csharpRoot = Path.Combine(generatedRoot, "csharp");
            var ssdtRoot = Path.Combine(generatedRoot, "ssdt");
            var sqlManifest = GenerationService.GenerateSql(randomWorkspace.Workspace, sqlRoot);
            var csharpManifest = GenerationService.GenerateCSharp(randomWorkspace.Workspace, csharpRoot);
            var ssdtManifest = GenerationService.GenerateSsdt(randomWorkspace.Workspace, ssdtRoot);
    
            if (applyDb)
            {
                var schemaPath = Path.Combine(sqlRoot, "schema.sql");
                var dataPath = Path.Combine(sqlRoot, "data.sql");
                await RecreateDatabaseFromScriptsAsync(dbConnection, dbName, schemaPath, dataPath).ConfigureAwait(false);
            }

            presenter.WriteOk(
                "random workspace created",
                ("Workspace", workspaceRoot),
                ("Model", randomWorkspace.Workspace.Model.Name),
                ("Entities", randomWorkspace.Workspace.Model.Entities.Count.ToString(CultureInfo.InvariantCulture)),
                ("Relationships", randomWorkspace.TotalRelationships.ToString(CultureInfo.InvariantCulture)),
                ("Rows", randomWorkspace.TotalRows.ToString(CultureInfo.InvariantCulture)),
                ("MaxDepth", randomWorkspace.MaxDepth.ToString(CultureInfo.InvariantCulture)),
                ("Seed", seed.ToString(CultureInfo.InvariantCulture)));
            presenter.WriteKeyValueBlock(
                "Generated",
                new[]
                {
                    ("sql", sqlRoot),
                    ("csharp", csharpRoot),
                    ("ssdt", ssdtRoot),
                });
            if (applyDb)
            {
                presenter.WriteInfo($"Database: recreated ({dbName})");
            }
            else
            {
                presenter.WriteInfo("Database: skipped (--no-database)");
            }
    
            return 0;
        }
        catch (SqlException exception)
        {
            return PrintGenerationError("E_RANDOM_DB_APPLY",
                "random create database apply failed. " + exception.Message);
        }
        catch (Exception exception)
        {
            return PrintGenerationError("E_RANDOM_CREATE",
                "random create failed. " + exception.Message);
        }
    }
    
    async Task RecreateDatabaseFromScriptsAsync(
        string connectionString,
        string databaseName,
        string schemaScriptPath,
        string dataScriptPath)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Database connection string is required.");
        }
    
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            throw new InvalidOperationException("Database name is required.");
        }
    
        var schemaScript = await File.ReadAllTextAsync(schemaScriptPath).ConfigureAwait(false);
        var dataScript = await File.ReadAllTextAsync(dataScriptPath).ConfigureAwait(false);
        var escapedDatabaseLiteral = databaseName.Replace("'", "''", StringComparison.Ordinal);
        var escapedDatabaseIdentifier = databaseName.Replace("]", "]]", StringComparison.Ordinal);
    
        var masterBuilder = new SqlConnectionStringBuilder(connectionString)
        {
            InitialCatalog = "master",
        };
    
        await using (var masterConnection = new SqlConnection(masterBuilder.ConnectionString))
        {
            await masterConnection.OpenAsync().ConfigureAwait(false);
            var recreateSql =
                $"IF DB_ID(N'{escapedDatabaseLiteral}') IS NOT NULL BEGIN ALTER DATABASE [{escapedDatabaseIdentifier}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{escapedDatabaseIdentifier}]; END; CREATE DATABASE [{escapedDatabaseIdentifier}];";
            await using var recreateCommand = new SqlCommand(recreateSql, masterConnection)
            {
                CommandTimeout = 300,
            };
            await recreateCommand.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
    
        var databaseBuilder = new SqlConnectionStringBuilder(connectionString)
        {
            InitialCatalog = databaseName,
        };
    
        await using var databaseConnection = new SqlConnection(databaseBuilder.ConnectionString);
        await databaseConnection.OpenAsync().ConfigureAwait(false);
    
        foreach (var batch in SplitSqlBatches(schemaScript))
        {
            await using var command = new SqlCommand(batch, databaseConnection)
            {
                CommandTimeout = 300,
            };
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
    
        foreach (var batch in SplitSqlBatches(dataScript))
        {
            await using var command = new SqlCommand(batch, databaseConnection)
            {
                CommandTimeout = 300,
            };
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
    }
    
    IReadOnlyList<string> SplitSqlBatches(string script)
    {
        var batches = new List<string>();
        var builder = new StringBuilder();
        using var reader = new StringReader(script ?? string.Empty);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (string.Equals(line.Trim(), "GO", StringComparison.OrdinalIgnoreCase))
            {
                var batch = builder.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(batch))
                {
                    batches.Add(batch);
                }
    
                builder.Clear();
                continue;
            }
    
            builder.AppendLine(line);
        }
    
        var finalBatch = builder.ToString().Trim();
        if (!string.IsNullOrWhiteSpace(finalBatch))
        {
            batches.Add(finalBatch);
        }
    
        return batches;
    }
    
    RandomWorkspaceResult BuildRandomWorkspace(
        string modelName,
        int entityCount,
        int seed,
        int minAdditionalProperties,
        int maxAdditionalProperties,
        int maxRelationshipsPerEntity,
        int minRowsPerEntity,
        int maxRowsPerEntity)
    {
        var random = new Random(seed);
        var resolvedModelName = string.IsNullOrWhiteSpace(modelName)
            ? $"RandomModel_{entityCount}_{seed}"
            : modelName.Trim();
        var workspace = new Workspace
        {
            Model = new GenericModel
            {
                Name = resolvedModelName,
            },
            Instance = new GenericInstance
            {
                ModelName = resolvedModelName,
            },
        };
    
        var depthBucketCount = Math.Min(entityCount, random.Next(8, 20));
        var entityDepths = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var entities = new List<GenericEntity>(entityCount);
        for (var index = 0; index < entityCount; index++)
        {
            var depth = index < depthBucketCount ? index : random.Next(0, depthBucketCount);
            var entity = new GenericEntity
            {
                Name = $"Entity{index:D4}",
            };

            var propertyCount = random.Next(minAdditionalProperties, maxAdditionalProperties + 1);
            for (var propertyIndex = 1; propertyIndex <= propertyCount; propertyIndex++)
            {
                entity.Properties.Add(new GenericProperty
                {
                    Name = $"P{propertyIndex:D2}",
                    DataType = "string",
                    IsNullable = random.NextDouble() >= 0.35d,
                });
            }
    
            workspace.Model.Entities.Add(entity);
            entities.Add(entity);
            entityDepths[entity.Name] = depth;
        }
    
        var maxDepth = entityDepths.Values.Max();
        foreach (var entity in entities)
        {
            var depth = entityDepths[entity.Name];
            if (depth <= 0 || maxRelationshipsPerEntity == 0)
            {
                continue;
            }
    
            var candidates = entities.Where(candidate => entityDepths[candidate.Name] < depth).ToList();
            if (candidates.Count == 0)
            {
                continue;
            }
    
            var relationshipCount = random.Next(1, Math.Min(maxRelationshipsPerEntity, candidates.Count) + 1);
            var usedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var relationIndex = 0; relationIndex < relationshipCount; relationIndex++)
            {
                var target = candidates[random.Next(candidates.Count)];
                if (!usedTargets.Add(target.Name))
                {
                    continue;
                }
    
                entity.Relationships.Add(new GenericRelationship
                {
                    Entity = target.Name,
                });
            }
        }
    
        var totalRows = 0;
        var orderedEntities = entities
            .OrderBy(entity => entityDepths[entity.Name])
            .ThenBy(entity => entity.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var entity in orderedEntities)
        {
            var rowCount = random.Next(minRowsPerEntity, maxRowsPerEntity + 1);
            totalRows += rowCount;
            var records = workspace.Instance.GetOrCreateEntityRecords(entity.Name);
            records.Clear();
    
            for (var rowIndex = 1; rowIndex <= rowCount; rowIndex++)
            {
                var id = rowIndex.ToString(CultureInfo.InvariantCulture);
                var record = new GenericRecord
                {
                    Id = id,
                };
    
                foreach (var property in entity.Properties.Where(property =>
                             !string.Equals(property.Name, "Id", StringComparison.OrdinalIgnoreCase)))
                {
                    if (property.IsNullable && random.NextDouble() < 0.25d)
                    {
                        continue;
                    }
    
                    record.Values[property.Name] = $"{property.Name}_{rowIndex:D4}_{random.Next(1000, 9999)}";
                }
    
                foreach (var relationship in entity.Relationships)
                {
                    var targetRows = workspace.Instance.GetOrCreateEntityRecords(relationship.Entity);
                    var target = targetRows[random.Next(targetRows.Count)];
                    record.RelationshipIds[relationship.GetColumnName()] = target.Id;
                }
    
                records.Add(record);
            }
        }
    
        return new RandomWorkspaceResult(
            workspace,
            maxDepth,
            workspace.Model.Entities.Sum(entity => entity.Relationships.Count),
            totalRows);
    }
    
    void PrintWorkspaceSummary(Workspace workspace)
    {
        var entityCount = workspace.Model.Entities.Count;
        var rowCount = workspace.Instance.RecordsByEntity.Values.Sum(records => records.Count);
        var dataSizes = CalculateWorkspaceDataSizes(workspace);
        presenter.WriteInfo("Status: ok");
        presenter.WriteKeyValueBlock(
            "Workspace",
            new[]
            {
                ("Path", workspace.WorkspaceRootPath),
                ("Metadata", workspace.MetadataRootPath),
            });
        presenter.WriteKeyValueBlock(
            "Model",
            new[]
            {
                ("Name", workspace.Model.Name),
                ("Entities", entityCount.ToString(CultureInfo.InvariantCulture)),
                ("Rows", rowCount.ToString(CultureInfo.InvariantCulture)),
            });
        presenter.WriteKeyValueBlock(
            "Data",
            new[]
            {
                ("Model", FormatByteSizeWithBytes(dataSizes.ModelBytes)),
                ("Instance", FormatByteSizeWithBytes(dataSizes.InstanceBytes)),
            });
        presenter.WriteKeyValueBlock(
            "Contract",
            new[]
            {
                ("Version", MetaWorkspaceConfig.GetContractVersion(workspace.WorkspaceConfig)),
            });
    }
    
    (long ModelBytes, long InstanceBytes) CalculateWorkspaceDataSizes(Workspace workspace)
    {
        var modelPath = ResolveFirstExistingPath(new[]
        {
            ResolveWorkspaceConfigPathFromWorkspaceRoot(
                workspace,
                MetaWorkspaceConfig.GetModelFile(workspace.WorkspaceConfig),
                "metadata/model.xml"),
            Path.Combine(workspace.MetadataRootPath, "model.xml"),
            Path.Combine(workspace.WorkspaceRootPath, "model.xml"),
        });
    
        var modelBytes = GetFileSize(modelPath);
    
        var instanceBytes = 0L;
        var shardDirectory = ResolveWorkspaceConfigPathFromWorkspaceRoot(
            workspace,
            MetaWorkspaceConfig.GetInstanceDir(workspace.WorkspaceConfig),
            "metadata/instance");
        if (Directory.Exists(shardDirectory))
        {
            var shardFiles = Directory.GetFiles(shardDirectory, "*.xml");
            if (shardFiles.Length > 0)
            {
                instanceBytes = shardFiles.Sum(GetFileSize);
            }
        }
    
        if (instanceBytes == 0)
        {
            instanceBytes = GetDirectorySize(shardDirectory);
        }
    
        return (modelBytes, instanceBytes);
    }
    
    string ResolveWorkspaceConfigPathFromWorkspaceRoot(Workspace workspace, string? configuredPath, string fallbackRelativePath)
    {
        var value = string.IsNullOrWhiteSpace(configuredPath) ? fallbackRelativePath : configuredPath.Trim();
        var normalized = value.Replace('/', Path.DirectorySeparatorChar);
        return Path.IsPathRooted(normalized)
            ? Path.GetFullPath(normalized)
            : Path.GetFullPath(Path.Combine(workspace.WorkspaceRootPath, normalized));
    }
    
    string ResolveFirstExistingPath(IEnumerable<string> candidates)
    {
        foreach (var candidate in candidates.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
    
        return string.Empty;
    }
    
    long GetFileSize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return 0L;
        }
    
        return new FileInfo(path).Length;
    }

    long GetDirectorySize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return 0L;
        }

        return Directory.GetFiles(path, "*", SearchOption.AllDirectories)
            .Select(file => new FileInfo(file).Length)
            .Sum();
    }
    
    string FormatByteSizeWithBytes(long bytes)
    {
        var human = FormatByteSize(bytes);
        return string.Equals(human, $"{bytes} B", StringComparison.Ordinal)
            ? human
            : $"{human} ({bytes} B)";
    }
    
    string FormatByteSize(long bytes)
    {
        const double Kb = 1024d;
        const double Mb = Kb * 1024d;
        const double Gb = Mb * 1024d;
    
        if (bytes < Kb)
        {
            return $"{bytes} B";
        }
    
        if (bytes < Mb)
        {
            return (bytes / Kb).ToString("0.##", CultureInfo.InvariantCulture) + " KB";
        }
    
        if (bytes < Gb)
        {
            return (bytes / Mb).ToString("0.##", CultureInfo.InvariantCulture) + " MB";
        }
    
        return (bytes / Gb).ToString("0.##", CultureInfo.InvariantCulture) + " GB";
    }
    
    void PrintContractCompatibilityWarning(Meta.Core.WorkspaceConfig.Generated.MetaWorkspace workspaceConfig)
    {
        var contractVersion = MetaWorkspaceConfig.GetContractVersion(workspaceConfig);
        if (!MetaWorkspaceConfig.TryParseContractVersion(contractVersion, out var major, out var minor))
        {
            return;
        }
    
        if (major == SupportedContractMajorVersion && minor > SupportedContractMinorVersion)
        {
            presenter.WriteWarning(
                $"workspace contractVersion '{contractVersion}' is newer than tool baseline '{SupportedContractMajorVersion}.{SupportedContractMinorVersion}'.");
        }
    }
    
    bool WorkspaceLooksInitialized(string workspaceRoot, string metadataRoot)
    {
        return File.Exists(Path.Combine(workspaceRoot, "workspace.xml")) ||
               File.Exists(Path.Combine(metadataRoot, "model.xml")) ||
               Directory.Exists(Path.Combine(metadataRoot, "instance"));
    }
    
    (string WorkspaceRootPath, string MetadataRootPath) ResolveWorkspaceFilesystemContext(string workspacePath)
    {
        var absolutePath = Path.GetFullPath(workspacePath);
        return HasWorkspaceOverrideInInvocation()
            ? ResolveWorkspaceFilesystemContextWithoutSearch(absolutePath)
            : DiscoverWorkspaceFilesystemContext(absolutePath);
    }
    
    (string WorkspaceRootPath, string MetadataRootPath) DiscoverWorkspaceFilesystemContext(string startPath)
    {
        var current = Directory.Exists(startPath)
            ? Path.GetFullPath(startPath)
            : Path.GetFullPath(Path.GetDirectoryName(startPath) ?? startPath);
    
        while (!string.IsNullOrWhiteSpace(current))
        {
            var metadataRoot = Path.Combine(current, "metadata");
            var workspaceXml = Path.Combine(current, "workspace.xml");
            if (File.Exists(workspaceXml) || IsWorkspaceMetadataCandidate(metadataRoot))
            {
                return (current, metadataRoot);
            }
    
            var parent = Directory.GetParent(current);
            if (parent == null)
            {
                break;
            }
    
            current = parent.FullName;
        }
    
        throw new FileNotFoundException($"Could not find workspace metadata starting from '{startPath}'.");
    }
    
    (string WorkspaceRootPath, string MetadataRootPath) ResolveWorkspaceFilesystemContextWithoutSearch(string workspacePath)
    {
        if (string.Equals(Path.GetFileName(workspacePath), "metadata", StringComparison.OrdinalIgnoreCase))
        {
            var workspaceRoot = Directory.GetParent(workspacePath)?.FullName ?? workspacePath;
            if (IsWorkspaceMetadataCandidate(workspacePath) || Directory.Exists(workspacePath))
            {
                return (workspaceRoot, workspacePath);
            }
        }
    
        var metadataRoot = Path.Combine(workspacePath, "metadata");
        if (IsWorkspaceMetadataCandidate(metadataRoot) || Directory.Exists(metadataRoot))
        {
            return (workspacePath, metadataRoot);
        }
    
        throw new FileNotFoundException($"Could not find workspace metadata under '{workspacePath}'.");
    }
    
    bool IsWorkspaceMetadataCandidate(string metadataRootPath)
    {
        var workspaceRootPath = Directory.GetParent(metadataRootPath)?.FullName ?? metadataRootPath;
        return File.Exists(Path.Combine(workspaceRootPath, "workspace.xml")) ||
               File.Exists(Path.Combine(metadataRootPath, "model.xml")) ||
               Directory.Exists(Path.Combine(metadataRootPath, "instance"));
    }
    
    void PrintSelectedRecord(string entityName, GenericRecord record)
    {
        presenter.WriteInfo($"Instance: {BuildEntityInstanceAddress(entityName, record.Id)}");
        var rows = new List<IReadOnlyList<string>>();
        foreach (var value in record.Values
                     .OrderBy(item => string.Equals(item.Key, "Id", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                     .ThenBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            rows.Add(new[] { value.Key, value.Value });
        }
    
        foreach (var relationship in record.RelationshipIds
                     .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            rows.Add(new[] { relationship.Key, relationship.Value });
        }
    
        presenter.WriteTable(new[] { "Field", "Value" }, rows);
    }
    
    void PrintQueryResult(Workspace workspace, string entityName, string whereExpression, IReadOnlyList<GenericRecord> rows, int top)
    {
        presenter.WriteInfo($"Query: {entityName}");
        presenter.WriteInfo($"Filter: {whereExpression}");
        presenter.WriteInfo($"Matches: {rows.Count.ToString(CultureInfo.InvariantCulture)}");
    
        var limit = top <= 0 ? 200 : top;
        var previewColumns = ResolveQueryPreviewColumns(workspace, entityName);
        var previewRows = new List<IReadOnlyList<string>>();
        foreach (var row in rows.Take(limit))
        {
            var cells = new List<string>();
            foreach (var column in previewColumns)
            {
                if (string.Equals(column, "Id", StringComparison.OrdinalIgnoreCase))
                {
                    cells.Add(row.Id);
                    continue;
                }
    
                cells.Add(row.Values.TryGetValue(column, out var value) ? value : string.Empty);
            }
    
            previewRows.Add(cells);
        }
    
        presenter.WriteTable(previewColumns, previewRows);
    
        if (rows.Count > limit)
        {
            presenter.WriteInfo($"InstancesTruncated: {(rows.Count - limit).ToString(CultureInfo.InvariantCulture)}");
        }
    }
    
    string BuildFilterSummary(IReadOnlyList<(string Mode, string Field, string Value)> filters)
    {
        if (filters == null || filters.Count == 0)
        {
            return "(none)";
        }
    
        return string.Join(
            " AND ",
            filters.Select(filter =>
            {
                if (string.Equals(filter.Mode, "contains", StringComparison.OrdinalIgnoreCase))
                {
                    return $"{filter.Field} contains {QuoteInstanceId(filter.Value)}";
                }
    
                return $"{filter.Field} = {QuoteInstanceId(filter.Value)}";
            }));
    }
    
    IReadOnlyList<GenericRecord> QueryRows(Workspace workspace, string entityName, IReadOnlyList<(string Mode, string Field, string Value)> filters)
    {
        if (workspace == null)
        {
            throw new ArgumentNullException(nameof(workspace));
        }
    
        var entity = RequireEntity(workspace, entityName);
        IEnumerable<GenericRecord> rows = workspace.Instance.GetOrCreateEntityRecords(entityName);
        if (filters is { Count: > 0 })
        {
            foreach (var filter in filters)
            {
                var resolvedField = ResolveQueryField(entity, filter.Field);
                rows = rows.Where(row => QueryFilterMatches(row, resolvedField, filter));
            }
        }
    
        return rows
            .OrderBy(row => row.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
    
    string ResolveQueryField(GenericEntity entity, string fieldName)
    {
        if (string.Equals(fieldName, "Id", StringComparison.OrdinalIgnoreCase))
        {
            return "Id";
        }
    
        var property = entity.Properties
            .FirstOrDefault(item => string.Equals(item.Name, fieldName, StringComparison.OrdinalIgnoreCase));
        if (property != null)
        {
            return property.Name;
        }
    
        var relationship = entity.Relationships
            .FirstOrDefault(item =>
                string.Equals(item.GetRoleOrDefault(), fieldName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.GetColumnName(), fieldName, StringComparison.OrdinalIgnoreCase));
        if (relationship != null)
        {
            return relationship.GetColumnName();
        }
    
        throw new InvalidOperationException($"Field '{fieldName}' does not exist on entity '{entity.Name}'.");
    }
    
    bool QueryFilterMatches(GenericRecord row, string resolvedField, (string Mode, string Field, string Value) filter)
    {
        var fieldValue = GetQueryFieldValue(row, resolvedField);
        if (string.Equals(filter.Mode, "contains", StringComparison.OrdinalIgnoreCase))
        {
            return fieldValue.IndexOf(filter.Value ?? string.Empty, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    
        return string.Equals(fieldValue, filter.Value ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }
    
    string GetQueryFieldValue(GenericRecord row, string fieldName)
    {
        if (string.Equals(fieldName, "Id", StringComparison.OrdinalIgnoreCase))
        {
            return row.Id ?? string.Empty;
        }
    
        if (row.Values.TryGetValue(fieldName, out var value))
        {
            return value ?? string.Empty;
        }
    
        if (row.RelationshipIds.TryGetValue(fieldName, out var relationshipValue))
        {
            return relationshipValue ?? string.Empty;
        }
    
        return string.Empty;
    }
    
    void PrintGraphStats(Workspace workspace, GraphStatsReport stats, int topN)
    {
        presenter.WriteInfo($"Graph: {workspace.Model.Name}");
        presenter.WriteInfo($"Nodes: {stats.NodeCount.ToString(CultureInfo.InvariantCulture)}");
        presenter.WriteInfo(
            $"Edges: declared={stats.EdgeCount.ToString(CultureInfo.InvariantCulture)} unique={stats.UniqueEdgeCount.ToString(CultureInfo.InvariantCulture)} dup={stats.DuplicateEdgeCount.ToString(CultureInfo.InvariantCulture)} missingTarget={stats.MissingTargetEdgeCount.ToString(CultureInfo.InvariantCulture)}");
        presenter.WriteInfo(
            $"Components: {stats.WeaklyConnectedComponents.ToString(CultureInfo.InvariantCulture)}  Roots: {stats.RootCount.ToString(CultureInfo.InvariantCulture)}  Sinks: {stats.SinkCount.ToString(CultureInfo.InvariantCulture)}  Isolated: {stats.IsolatedCount.ToString(CultureInfo.InvariantCulture)}");
        presenter.WriteInfo(
            $"Cycles: {(stats.HasCycles ? "yes" : "no")}  MaxDepth: {(stats.DagMaxDepth.HasValue ? stats.DagMaxDepth.Value.ToString(CultureInfo.InvariantCulture) : "n/a")}");
        presenter.WriteInfo(
            $"AvgDegree: in={stats.AverageInDegree.ToString("F3", CultureInfo.InvariantCulture)} out={stats.AverageOutDegree.ToString("F3", CultureInfo.InvariantCulture)}");
    
        presenter.WriteInfo($"Top out-degree ({topN.ToString(CultureInfo.InvariantCulture)}):");
        presenter.WriteTable(
            new[] { "Entity", "OutDegree" },
            stats.TopOutDegree
                .Select(hub => (IReadOnlyList<string>)new[]
                {
                    hub.Entity,
                    hub.Degree.ToString(CultureInfo.InvariantCulture),
                })
                .ToList());
    
        presenter.WriteInfo($"Top in-degree ({topN.ToString(CultureInfo.InvariantCulture)}):");
        presenter.WriteTable(
            new[] { "Entity", "InDegree" },
            stats.TopInDegree
                .Select(hub => (IReadOnlyList<string>)new[]
                {
                    hub.Entity,
                    hub.Degree.ToString(CultureInfo.InvariantCulture),
                })
                .ToList());
    
        if (stats.CycleSamples.Count > 0)
        {
            presenter.WriteInfo($"Cycle samples ({stats.CycleSamples.Count.ToString(CultureInfo.InvariantCulture)}):");
            foreach (var sample in stats.CycleSamples)
            {
                presenter.WriteInfo($"  {sample}");
            }
        }
    }
    
    IReadOnlyList<string> ResolveQueryPreviewColumns(Workspace workspace, string entityName)
    {
        var entity = workspace.Model.FindEntity(entityName);
        if (entity == null)
        {
            return new[] { "Id" };
        }
    
        var columns = new List<string> { "Id" };
        var additional = entity.Properties
            .Where(property => !string.Equals(property.Name, "Id", StringComparison.OrdinalIgnoreCase))
            .OrderBy(property => property.IsNullable ? 1 : 0)
            .ThenBy(property => property.Name, StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .Select(property => property.Name)
            .ToList();
        columns.AddRange(additional);
        return columns;
    }
    
    IReadOnlyList<(string Key, string Value)> BuildRowPreviewDetails(GenericEntity entity, RowPatch rowPatch)
    {
        var details = new List<(string Key, string Value)>();
        var previewProperty = entity.Properties
            .Where(property => !string.Equals(property.Name, "Id", StringComparison.OrdinalIgnoreCase))
            .OrderBy(property => property.IsNullable ? 1 : 0)
            .ThenBy(property => property.Name, StringComparer.OrdinalIgnoreCase)
            .Select(property => property.Name)
            .FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(previewProperty) &&
            rowPatch.Values.TryGetValue(previewProperty, out var previewValue) &&
            !string.IsNullOrWhiteSpace(previewValue))
        {
            details.Add((previewProperty, previewValue));
        }
    
        return details;
    }
    
    IReadOnlyList<(string Key, string Value)> BuildUpsertSuccessDetails(
        Workspace workspace,
        string entityName,
        IReadOnlyList<string> rowIds)
    {
        var existingIds = workspace.Instance.GetOrCreateEntityRecords(entityName)
            .Select(record => record.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var inserted = rowIds.Count(id => !existingIds.Contains(id));
        var updated = rowIds.Count - inserted;
        return new[]
        {
            ("Inserted", inserted.ToString(CultureInfo.InvariantCulture)),
            ("Updated", updated.ToString(CultureInfo.InvariantCulture)),
            ("Total", rowIds.Count.ToString(CultureInfo.InvariantCulture)),
        };
    }
    
    GenericEntity RequireEntity(Workspace workspace, string entityName)
    {
        var entity = workspace.Model.FindEntity(entityName);
        if (entity == null)
        {
            throw new InvalidOperationException($"Entity '{entityName}' does not exist.");
        }
    
        return entity;
    }
    
    GenericRecord? TryFindRowById(Workspace workspace, string entityName, string id)
    {
        if (workspace == null)
        {
            throw new ArgumentNullException(nameof(workspace));
        }
    
        if (string.IsNullOrWhiteSpace(entityName))
        {
            throw new InvalidOperationException("Entity name is required.");
        }
    
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }
    
        var rows = workspace.Instance.GetOrCreateEntityRecords(entityName);
        return rows.FirstOrDefault(row => string.Equals(row.Id, id, StringComparison.OrdinalIgnoreCase));
    }
    
    GenericRecord ResolveRowById(Workspace workspace, string entityName, string id)
    {
        var row = TryFindRowById(workspace, entityName, id);
        if (row == null)
        {
            throw new InvalidOperationException($"Instance with Id '{id}' does not exist in entity '{entityName}'.");
        }
    
        return row;
    }
    
    RowPatch BuildRowPatchForUpdate(
        GenericEntity entity,
        string id,
        IReadOnlyDictionary<string, string> setValues)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new InvalidOperationException($"Cannot update '{entity.Name}' instance with empty Id.");
        }
    
        var propertyNames = entity.Properties.Select(property => property.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var relationshipByAlias = BuildRelationshipAliasMap(entity);
    
        var patch = new RowPatch
        {
            Id = id,
        };
    
        foreach (var pair in setValues)
        {
            if (string.Equals(pair.Key, "Id", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("instance update does not allow updating Id.");
            }
    
            if (propertyNames.Contains(pair.Key))
            {
                patch.Values[pair.Key] = pair.Value;
                continue;
            }
    
            if (relationshipByAlias.TryGetValue(pair.Key, out var relationshipUsageName))
            {
                patch.RelationshipIds[relationshipUsageName] = NormalizeRelationshipInputValue(pair.Value, relationshipUsageName);
                continue;
            }
    
            throw new InvalidOperationException(
                $"Field '{pair.Key}' is not a property or relationship on entity '{entity.Name}'.");
        }
    
        return patch;
    }
    
    RowPatch BuildRowPatchForCreate(
        Workspace workspace,
        GenericEntity entity,
        IReadOnlyDictionary<string, string> setValues,
        string? explicitId)
    {
        var id = !string.IsNullOrWhiteSpace(explicitId)
            ? explicitId.Trim()
            : GenerateNextId(workspace, entity.Name);
    
        if (workspace.Instance.GetOrCreateEntityRecords(entity.Name)
            .Any(row => string.Equals(row.Id, id, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Cannot create '{entity.Name}' with Id '{id}' because it already exists.");
        }
    
        var propertyNames = entity.Properties.Select(property => property.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var relationshipByAlias = BuildRelationshipAliasMap(entity);
    
        var patch = new RowPatch
        {
            Id = id,
            Values =
            {
                ["Id"] = id,
            },
        };
    
        foreach (var pair in setValues)
        {
            if (string.Equals(pair.Key, "Id", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
    
            if (propertyNames.Contains(pair.Key))
            {
                patch.Values[pair.Key] = pair.Value;
                continue;
            }
    
            if (relationshipByAlias.TryGetValue(pair.Key, out var relationshipUsageName))
            {
                patch.RelationshipIds[relationshipUsageName] = NormalizeRelationshipInputValue(pair.Value, relationshipUsageName);
                continue;
            }
    
            throw new InvalidOperationException($"Field '{pair.Key}' is not a property or relationship on entity '{entity.Name}'.");
        }
    
        EnsureCreatePatchIncludesRequiredRelationships(entity, patch, operationName: "insert", rowNumber: null);
        return patch;
    }
    
    bool ContainsIdSetAssignment(IReadOnlyDictionary<string, string> setValues)
    {
        if (setValues == null || setValues.Count == 0)
        {
            return false;
        }
    
        return setValues.Keys.Any(key => string.Equals(key, "Id", StringComparison.OrdinalIgnoreCase));
    }
    
    string ResolveRelationshipName(GenericEntity entity, string candidateToEntityName)
    {
        return ResolveRelationshipDefinition(entity, candidateToEntityName, out _)
            ?.GetColumnName() ?? string.Empty;
    }

    GenericRelationship? ResolveRelationshipDefinition(
        GenericEntity entity,
        string candidateToEntityName,
        out bool isAmbiguous)
    {
        isAmbiguous = false;
        var selector = candidateToEntityName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(selector))
        {
            return null;
        }

        var byRoleOrColumn = entity.Relationships
            .Where(item =>
                string.Equals(item.GetRoleOrDefault(), selector, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.GetColumnName(), selector, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (byRoleOrColumn.Count == 1)
        {
            return byRoleOrColumn[0];
        }

        if (byRoleOrColumn.Count > 1)
        {
            isAmbiguous = true;
            return null;
        }

        var byTarget = entity.Relationships
            .Where(item => string.Equals(item.Entity, selector, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (byTarget.Count == 1)
        {
            return byTarget[0];
        }

        if (byTarget.Count > 1)
        {
            isAmbiguous = true;
        }

        return null;
    }
    
    string TryGetDisplayValue(GenericEntity entity, GenericRecord row)
    {
        var previewProperty = entity.Properties
            .Where(property => !string.Equals(property.Name, "Id", StringComparison.OrdinalIgnoreCase))
            .OrderBy(property => property.IsNullable ? 1 : 0)
            .ThenBy(property => property.Name, StringComparer.OrdinalIgnoreCase)
            .Select(property => property.Name)
            .FirstOrDefault();
        if (string.IsNullOrWhiteSpace(previewProperty))
        {
            return string.Empty;
        }
    
        return row.Values.TryGetValue(previewProperty, out var value) ? value : string.Empty;
    }
    
    int CountRelationshipUsages(GenericRecord row, string relationshipUsageName)
    {
        return row.RelationshipIds.Count(item =>
            string.Equals(item.Key, relationshipUsageName, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(item.Value));
    }
    
    RowPatch BuildRelationshipUsageRewritePatch(
        GenericRecord sourceRow,
        string relationshipUsageName,
        string? targetId)
    {
        var patch = new RowPatch
        {
            Id = sourceRow.Id,
            ReplaceExisting = true,
        };
        foreach (var value in sourceRow.Values)
        {
            patch.Values[value.Key] = value.Value;
        }
    
        foreach (var relationship in sourceRow.RelationshipIds
                     .Where(item => !string.Equals(item.Key, relationshipUsageName, StringComparison.OrdinalIgnoreCase)))
        {
            patch.RelationshipIds[relationship.Key] = relationship.Value;
        }
    
        if (!string.IsNullOrWhiteSpace(targetId))
        {
            patch.RelationshipIds[relationshipUsageName] = targetId;
        }
    
        return patch;
    }
    
    WorkspaceOp BuildUpsertOperationFromRows(
        Workspace workspace,
        GenericEntity entity,
        IReadOnlyList<Dictionary<string, string>> rows,
        IReadOnlyList<string> keyFields,
        bool autoEnsure,
        bool autoId = false)
    {
        var operation = new WorkspaceOp
        {
            Type = WorkspaceOpTypes.BulkUpsertRows,
            EntityName = entity.Name,
        };
    
        var propertyNames = entity.Properties
            .Select(property => property.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var relationshipByAlias = BuildRelationshipAliasMap(entity);
    
        foreach (var keyField in keyFields)
        {
            if (!string.Equals(keyField, "Id", StringComparison.OrdinalIgnoreCase) &&
                !propertyNames.Contains(keyField) &&
                !relationshipByAlias.ContainsKey(keyField))
            {
                throw new InvalidOperationException($"bulk-insert --key field '{keyField}' is not valid for entity '{entity.Name}'.");
            }
        }
    
        var reservedIds = workspace.Instance.GetOrCreateEntityRecords(entity.Name)
            .Select(record => record.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (autoId)
        {
            var nonNumericId = reservedIds.FirstOrDefault(id => !long.TryParse(id, out _));
            if (!string.IsNullOrWhiteSpace(nonNumericId))
            {
                throw new InvalidOperationException(
                    $"Cannot auto-generate Id for entity '{entity.Name}' because existing Id '{nonNumericId}' is not numeric. Use explicit Id values in input.");
            }
        }
        var createdByKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var existingOrPlannedIds = workspace.Instance.GetOrCreateEntityRecords(entity.Name)
            .Select(record => record.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            row.TryGetValue("Id", out var providedId);
            var id = providedId?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(id))
            {
                if (autoId)
                {
                    id = GenerateNextIdFromReserved(reservedIds);
                    reservedIds.Add(id);
                }
                else if (keyFields.Count > 0)
                {
                    id = ResolveIdByKeys(workspace, entity, keyFields, row, autoEnsure, createdByKey, reservedIds);
                }
                else
                {
                    throw new InvalidOperationException("bulk-insert row is missing Id and no --key fields were provided.");
                }
            }
            else
            {
                reservedIds.Add(id);
            }

            var createsNewRow = !existingOrPlannedIds.Contains(id);
            existingOrPlannedIds.Add(id);
    
            var patch = new RowPatch
            {
                Id = id,
                Values =
                {
                    ["Id"] = id,
                },
            };
    
            foreach (var pair in row)
            {
                if (string.Equals(pair.Key, "Id", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
    
                if (propertyNames.Contains(pair.Key))
                {
                    patch.Values[pair.Key] = pair.Value;
                    continue;
                }
    
                if (relationshipByAlias.TryGetValue(pair.Key, out var relationshipUsageName))
                {
                    patch.RelationshipIds[relationshipUsageName] = NormalizeRelationshipInputValue(pair.Value, relationshipUsageName);
                    continue;
                }
    
                throw new InvalidOperationException($"Column '{pair.Key}' is not a property or relationship on entity '{entity.Name}'.");
            }

            if (createsNewRow)
            {
                EnsureCreatePatchIncludesRequiredRelationships(entity, patch, operationName: "bulk-insert", rowNumber: rowIndex + 1);
            }
    
            operation.RowPatches.Add(patch);
        }
    
        return operation;
    }
    
    string ResolveIdByKeys(
        Workspace workspace,
        GenericEntity entity,
        IReadOnlyList<string> keyFields,
        IReadOnlyDictionary<string, string> row,
        bool autoEnsure,
        IDictionary<string, string> createdByKey,
        ISet<string> reservedIds)
    {
        var keyValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in keyFields)
        {
            if (!row.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"bulk-insert --key field '{key}' is missing or empty in input row.");
            }

            var resolvedKey = ResolveQueryField(entity, key);
            keyValues[resolvedKey] = value.Trim();
        }

        var signature = string.Join(
            "\u001f",
            keyValues
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => $"{pair.Key.ToLowerInvariant()}={pair.Value.ToLowerInvariant()}"));
        if (createdByKey.TryGetValue(signature, out var existingCreatedId))
        {
            return existingCreatedId;
        }

        var candidates = workspace.Instance.GetOrCreateEntityRecords(entity.Name)
            .Where(record => keyValues.All(pair =>
                string.Equals(GetRecordFieldValue(record, pair.Key), pair.Value, StringComparison.OrdinalIgnoreCase)))
            .Select(record => record.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    
        if (candidates.Count == 1)
        {
            return candidates[0];
        }
    
        if (candidates.Count > 1)
        {
            throw new InvalidOperationException(
                $"bulk-insert --key matched multiple rows in '{entity.Name}' for key '{signature}'.");
        }
    
        if (!autoEnsure)
        {
            throw new InvalidOperationException(
                $"bulk-insert --key found no matching row in '{entity.Name}' for key '{signature}'.");
        }
    
        var createdId = GenerateNextIdFromReserved(reservedIds);
        reservedIds.Add(createdId);
        createdByKey[signature] = createdId;
        return createdId;
    }
    
    string GetRecordFieldValue(GenericRecord record, string field)
    {
        if (string.Equals(field, "Id", StringComparison.OrdinalIgnoreCase))
        {
            return record.Id ?? string.Empty;
        }
    
        if (record.Values.TryGetValue(field, out var value))
        {
            return value ?? string.Empty;
        }
    
        if (record.RelationshipIds.TryGetValue(field, out var relationshipValue))
        {
            return relationshipValue ?? string.Empty;
        }
    
        return string.Empty;
    }
    
    string GenerateNextIdFromReserved(ISet<string> reservedIds)
    {
        var numericIds = reservedIds
            .Select(value => long.TryParse(value, out var parsed) ? parsed : (long?)null)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToList();
    
        if (numericIds.Count > 0)
        {
            var next = numericIds.Max() + 1;
            while (reservedIds.Contains(next.ToString()))
            {
                next++;
            }
    
            return next.ToString();
        }
    
        var candidate = 1L;
        while (reservedIds.Contains(candidate.ToString()))
        {
            candidate++;
        }
    
        return candidate.ToString();
    }
    
    IReadOnlyList<Dictionary<string, string>> ParseBulkInputRows(string input, string format)
    {
        var effectiveFormat = string.IsNullOrWhiteSpace(format)
            ? DetectBulkFormat(input)
            : format.Trim().ToLowerInvariant();
    
        return effectiveFormat switch
        {
            "tsv" => ParseDelimitedRows(input, '\t'),
            "csv" => ParseDelimitedRows(input, ','),
            _ => throw new InvalidOperationException($"Unsupported input format '{effectiveFormat}'."),
        };
    }
    
    string DetectBulkFormat(string input)
    {
        var firstLine = (input ?? string.Empty)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? string.Empty;
        return firstLine.Contains('\t') ? "tsv" : "csv";
    }
    
    IReadOnlyList<Dictionary<string, string>> ParseDelimitedRows(string input, char delimiter)
    {
        var lines = (input ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .ToList();
        if (lines.Count == 0)
        {
            return Array.Empty<Dictionary<string, string>>();
        }
    
        var header = lines[0].Split(delimiter).Select(item => item.Trim()).ToArray();
        if (header.Length == 0 || header.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException("Input header is empty or invalid.");
        }
    
        var rows = new List<Dictionary<string, string>>();
        for (var i = 1; i < lines.Count; i++)
        {
            var parts = lines[i].Split(delimiter);
            if (parts.Length != header.Length)
            {
                throw new InvalidOperationException(
                    $"Input row {i + 1} column count ({parts.Length}) does not match header ({header.Length}).");
            }
    
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var c = 0; c < header.Length; c++)
            {
                row[header[c]] = parts[c].Trim();
            }
    
            rows.Add(row);
        }
    
        return rows;
    }
    
    string NormalizeRelationshipInputValue(string value, string relationshipName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Relationship '{relationshipName}' requires a target Id value.");
        }
    
        var trimmed = value.Trim();
        return trimmed;
    }

    void EnsureCreatePatchIncludesRequiredRelationships(
        GenericEntity entity,
        RowPatch patch,
        string operationName,
        int? rowNumber)
    {
        foreach (var relationship in entity.Relationships
                     .Select(item => item.GetColumnName())
                     .Where(name => !string.IsNullOrWhiteSpace(name))
                     .OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
        {
            if (!patch.RelationshipIds.TryGetValue(relationship, out var relationshipId) ||
                string.IsNullOrWhiteSpace(relationshipId))
            {
                if (string.Equals(operationName, "bulk-insert", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"bulk-insert row {rowNumber.GetValueOrDefault()} is missing required relationship '{relationship}'. Set column '{relationship}' to a target Id.");
                }

                throw new InvalidOperationException(
                    $"insert is missing required relationship '{relationship}'. Set it with --set {relationship}=<Id>.");
            }
        }
    }

    Dictionary<string, string> BuildRelationshipAliasMap(GenericEntity entity)
    {
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var relationship in entity.Relationships)
        {
            var relationshipName = relationship.GetColumnName();
            if (string.IsNullOrWhiteSpace(relationshipName))
            {
                continue;
            }

            aliases[relationshipName] = relationshipName;
            aliases[relationship.GetRoleOrDefault()] = relationshipName;
        }

        return aliases;
    }
    
    string GenerateNextId(Workspace workspace, string entityName)
    {
        var records = workspace.Instance.GetOrCreateEntityRecords(entityName);
        var ids = records
            .Select(row => row.Id)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    
        var numericIds = ids
            .Select(value => long.TryParse(value, out var parsed) ? parsed : (long?)null)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToList();
    
        if (numericIds.Count > 0)
        {
            var next = numericIds.Max() + 1;
            while (ids.Contains(next.ToString()))
            {
                next++;
            }
    
            return next.ToString();
        }
    
        var candidate = 1L;
        while (ids.Contains(candidate.ToString()))
        {
            candidate++;
        }
    
        return candidate.ToString();
    }
}










