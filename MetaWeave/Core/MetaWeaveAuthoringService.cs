using System.Collections;
using Meta.Integration;
using Meta.Operations.Domain;
using MetaWeaveScript.Sql;

namespace MetaWeave.Core;

public sealed record MetaWeaveSourceWorkspaceDefinition(
    string Name,
    string ModelName);

public sealed class MetaWeaveAuthoringService
{
    public MetaWeaveModel Create(
        string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var model = MetaWeaveModel.CreateEmpty();
        model.WeaveList.Add(new Weave
        {
            Id = name.Trim(),
        });
        return model;
    }

    public Direction AddDirection(
        MetaWeaveModel model,
        string name,
        string sourceModelName,
        string targetModelName) =>
        AddDirection(
            model,
            name,
            [new MetaWeaveSourceWorkspaceDefinition("source", sourceModelName)],
            targetModelName);

    public Direction AddDirection(
        MetaWeaveModel model,
        string name,
        IReadOnlyList<MetaWeaveSourceWorkspaceDefinition> sourceWorkspaces,
        string targetModelName)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(sourceWorkspaces);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetModelName);

        var weave = RequireWeave(model);
        var normalizedName = name.Trim();
        var normalizedTarget = MetaName.Require(
            targetModelName.Trim(),
            "Direction target model name.");
        if (model.DirectionList.Any(direction =>
                MetaName.Comparer.Equals(direction.Id, normalizedName)))
        {
            throw new InvalidOperationException(
                $"Direction '{normalizedName}' already exists.");
        }

        if (sourceWorkspaces.Count == 0)
        {
            throw new InvalidOperationException(
                $"Direction '{normalizedName}' requires at least one source workspace.");
        }

        var normalizedSources = new List<MetaWeaveSourceWorkspaceDefinition>(sourceWorkspaces.Count);
        var sourceNames = new HashSet<string>(MetaName.Comparer);
        foreach (var sourceWorkspace in sourceWorkspaces)
        {
            if (sourceWorkspace is null)
            {
                throw new InvalidOperationException(
                    $"Direction '{normalizedName}' contains a missing source workspace declaration.");
            }

            var sourceName = MetaName.Require(
                sourceWorkspace.Name.Trim(),
                "Direction source workspace name.");
            var sourceModelName = MetaName.Require(
                sourceWorkspace.ModelName.Trim(),
                "Direction source model name.");
            if (!sourceNames.Add(sourceName))
            {
                throw new InvalidOperationException(
                    $"Source workspace '{sourceName}' is declared more than once in direction '{normalizedName}'.");
            }

            normalizedSources.Add(new MetaWeaveSourceWorkspaceDefinition(
                sourceName,
                sourceModelName));
        }

        var direction = new Direction
        {
            Id = normalizedName,
            TargetModelName = normalizedTarget,
            Weave = weave,
        };
        model.DirectionList.Add(direction);
        foreach (var sourceWorkspace in normalizedSources)
        {
            AddSourceWorkspace(
                model,
                direction,
                sourceWorkspace.Name,
                sourceWorkspace.ModelName);
        }

        return direction;
    }

    public DirectionSourceWorkspace AddSourceWorkspace(
        MetaWeaveModel model,
        string directionName,
        string name,
        string modelName) =>
        AddSourceWorkspace(model, RequireDirection(model, directionName), name, modelName);

    public DirectionStringParameter AddStringParameter(
        MetaWeaveModel model,
        string directionName,
        string name)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(directionName);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var direction = RequireDirection(model, directionName);
        var normalizedName = MetaName.Require(name.Trim(), "Direction string parameter name.");
        if (model.DirectionStringParameterList.Any(parameter =>
                SameDirection(parameter.Direction, direction) &&
                MetaName.Comparer.Equals(parameter.Name, normalizedName)))
        {
            throw new InvalidOperationException(
                $"String parameter '{normalizedName}' already exists in direction '{direction.Id}'.");
        }

        var parameter = new DirectionStringParameter
        {
            Id = CreateOwnedId(direction.Id, normalizedName),
            Name = normalizedName,
            Direction = direction,
        };
        model.DirectionStringParameterList.Add(parameter);
        return parameter;
    }

    private static DirectionSourceWorkspace AddSourceWorkspace(
        MetaWeaveModel model,
        Direction direction,
        string name,
        string modelName)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(direction);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelName);

        var normalizedName = MetaName.Require(name.Trim(), "Direction source workspace name.");
        var normalizedModelName = MetaName.Require(modelName.Trim(), "Direction source model name.");
        if (model.DirectionSourceWorkspaceList.Any(source =>
                SameDirection(source.Direction, direction) &&
                MetaName.Comparer.Equals(source.Name, normalizedName)))
        {
            throw new InvalidOperationException(
                $"Source workspace '{normalizedName}' already exists in direction '{direction.Id}'.");
        }

        var sourceWorkspace = new DirectionSourceWorkspace
        {
            Id = CreateOwnedId(direction.Id, normalizedName),
            Name = normalizedName,
            ModelName = normalizedModelName,
            Direction = direction,
        };
        model.DirectionSourceWorkspaceList.Add(sourceWorkspace);
        return sourceWorkspace;
    }

    public Transformation AddTransformation(
        MetaWeaveModel model,
        string directionName,
        string name,
        string targetEntityName,
        string script)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(directionName);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetEntityName);
        ArgumentException.ThrowIfNullOrWhiteSpace(script);

        var direction = RequireDirection(model, directionName);
        var normalizedName = name.Trim();
        var normalizedTargetEntityName = targetEntityName.Trim();
        var transformationId = CreateTransformationId(direction.Id, normalizedName);
        var directionTransformations = model.TransformationList.Where(
            transformation => SameDirection(transformation.Direction, direction));
        if (directionTransformations.Any(transformation =>
                MetaName.Comparer.Equals(transformation.Id, transformationId)))
        {
            throw new InvalidOperationException(
                $"Transformation '{normalizedName}' already exists in direction '{direction.Id}'.");
        }

        if (directionTransformations.Any(transformation =>
                MetaName.Comparer.Equals(
                    transformation.TargetEntityName,
                    normalizedTargetEntityName)))
        {
            throw new InvalidOperationException(
                $"Target entity '{normalizedTargetEntityName}' already has a transformation in direction '{direction.Id}'.");
        }

        var sqlService = new MetaWeaveScriptSqlService();
        _ = sqlService.ImportFromSqlCode(script);
        var selectStatement = sqlService.ImportIntoModel(model, script);
        var transformation = new Transformation
        {
            Id = transformationId,
            TargetEntityName = normalizedTargetEntityName,
            Direction = direction,
            SelectStatement = selectStatement,
        };
        model.TransformationList.Add(transformation);
        return transformation;
    }

    public DirectionRelation AddRelation(
        MetaWeaveModel model,
        string directionName,
        string name,
        string script)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(directionName);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(script);

        var direction = RequireDirection(model, directionName);
        var normalizedName = MetaName.Require(name.Trim(), "Direction relation name.");
        var relationId = CreateOwnedId(direction.Id, normalizedName);
        if (model.DirectionRelationList.Any(relation =>
                SameDirection(relation.Direction, direction) &&
                MetaName.Comparer.Equals(relation.Id, relationId)))
        {
            throw new InvalidOperationException(
                $"Relation '{normalizedName}' already exists in direction '{direction.Id}'.");
        }

        var sqlService = new MetaWeaveScriptSqlService();
        _ = sqlService.ImportFromSqlCode(script);
        var relation = new DirectionRelation
        {
            Id = relationId,
            Direction = direction,
            SelectStatement = sqlService.ImportIntoModel(model, script),
        };
        model.DirectionRelationList.Add(relation);
        return relation;
    }

    public DirectionRelation UpdateRelation(
        MetaWeaveModel model,
        string directionName,
        string name,
        string script)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(directionName);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(script);

        var direction = RequireDirection(model, directionName);
        var normalizedName = name.Trim();
        var matches = model.DirectionRelationList.Where(relation =>
            SameDirection(relation.Direction, direction) &&
            MetaName.Comparer.Equals(GetRelationName(relation), normalizedName)).ToArray();
        var relation = matches.Length switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException(
                $"Relation '{normalizedName}' was not found in direction '{direction.Id}'."),
            _ => throw new InvalidOperationException(
                $"Relation '{normalizedName}' is ambiguous in direction '{direction.Id}'.")
        };

        var sqlService = new MetaWeaveScriptSqlService();
        _ = sqlService.ImportFromSqlCode(script);
        relation.SelectStatement = sqlService.ImportIntoModel(model, script);
        RemoveUnreachableRows(model);
        return relation;
    }

    public Transformation UpdateTransformation(
        MetaWeaveModel model,
        string directionName,
        string name,
        string script)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(directionName);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(script);

        var direction = RequireDirection(model, directionName);
        var normalizedName = name.Trim();
        var matches = model.TransformationList.Where(transformation =>
            SameDirection(transformation.Direction, direction) &&
            MetaName.Comparer.Equals(
                GetTransformationName(transformation),
                normalizedName)).ToArray();
        var transformation = matches.Length switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException(
                $"Transformation '{normalizedName}' was not found in direction '{direction.Id}'."),
            _ => throw new InvalidOperationException(
                $"Transformation '{normalizedName}' is ambiguous in direction '{direction.Id}'.")
        };

        var sqlService = new MetaWeaveScriptSqlService();
        _ = sqlService.ImportFromSqlCode(script);
        transformation.SelectStatement = sqlService.ImportIntoModel(model, script);
        RemoveUnreachableRows(model);
        return transformation;
    }

    public DirectionRequirement AddRequirement(
        MetaWeaveModel model,
        string directionName,
        string name,
        string code,
        string message,
        string script)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(directionName);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(script);

        var direction = RequireDirection(model, directionName);
        var normalizedName = name.Trim();
        var requirementId = CreateOwnedId(direction.Id, normalizedName);
        if (model.DirectionRequirementList.Any(requirement =>
                SameDirection(requirement.Direction, direction) &&
                MetaName.Comparer.Equals(requirement.Id, requirementId)))
        {
            throw new InvalidOperationException(
                $"Requirement '{normalizedName}' already exists in direction '{direction.Id}'.");
        }

        var sqlService = new MetaWeaveScriptSqlService();
        _ = sqlService.ImportFromSqlCode(script);
        var requirement = new DirectionRequirement
        {
            Id = requirementId,
            Code = code.Trim(),
            Message = message.Trim(),
            Direction = direction,
            SelectStatement = sqlService.ImportIntoModel(model, script),
        };
        model.DirectionRequirementList.Add(requirement);
        return requirement;
    }

    public DirectionRequirement UpdateRequirement(
        MetaWeaveModel model,
        string directionName,
        string name,
        string script)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(directionName);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(script);

        var direction = RequireDirection(model, directionName);
        var normalizedName = name.Trim();
        var matches = model.DirectionRequirementList.Where(requirement =>
            SameDirection(requirement.Direction, direction) &&
            MetaName.Comparer.Equals(
                GetRequirementName(requirement),
                normalizedName)).ToArray();
        var requirement = matches.Length switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException(
                $"Requirement '{normalizedName}' was not found in direction '{direction.Id}'."),
            _ => throw new InvalidOperationException(
                $"Requirement '{normalizedName}' is ambiguous in direction '{direction.Id}'.")
        };

        var sqlService = new MetaWeaveScriptSqlService();
        _ = sqlService.ImportFromSqlCode(script);
        requirement.SelectStatement = sqlService.ImportIntoModel(model, script);
        RemoveUnreachableRows(model);
        return requirement;
    }

    public static string GetTransformationName(Transformation transformation)
    {
        ArgumentNullException.ThrowIfNull(transformation);
        var prefix = transformation.Direction is null
            ? null
            : transformation.Direction.Id + "/";
        return !string.IsNullOrEmpty(prefix) &&
               transformation.Id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? transformation.Id[prefix.Length..]
            : transformation.Id;
    }

    public static string GetRequirementName(DirectionRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        var prefix = requirement.Direction is null
            ? null
            : requirement.Direction.Id + "/";
        return !string.IsNullOrEmpty(prefix) &&
               requirement.Id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? requirement.Id[prefix.Length..]
            : requirement.Id;
    }

    public static string GetRelationName(DirectionRelation relation)
    {
        ArgumentNullException.ThrowIfNull(relation);
        var prefix = relation.Direction is null
            ? null
            : relation.Direction.Id + "/";
        return !string.IsNullOrEmpty(prefix) &&
               relation.Id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? relation.Id[prefix.Length..]
            : relation.Id;
    }

    private static Weave RequireWeave(MetaWeaveModel model) =>
        model.WeaveList.Count switch
        {
            1 => model.WeaveList[0],
            _ => throw new InvalidOperationException(
                $"A MetaWeave workspace requires exactly one Weave but contained {model.WeaveList.Count}.")
        };

    private static Direction RequireDirection(
        MetaWeaveModel model,
        string directionName)
    {
        var matches = model.DirectionList.Where(direction =>
            MetaName.Comparer.Equals(direction.Id, directionName.Trim())).ToArray();
        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException(
                $"Direction '{directionName.Trim()}' was not found."),
            _ => throw new InvalidOperationException(
                $"Direction '{directionName.Trim()}' is ambiguous.")
        };
    }

    private static string CreateTransformationId(
        string directionName,
        string transformationName) =>
        CreateOwnedId(directionName, transformationName);

    private static string CreateOwnedId(
        string directionName,
        string memberName) =>
        $"{directionName}/{memberName}";

    private static bool SameDirection(Direction? left, Direction right) =>
        left is not null &&
        (ReferenceEquals(left, right) || MetaName.Comparer.Equals(left.Id, right.Id));

    private static void RemoveUnreachableRows(MetaWeaveModel model)
    {
        var workspace = TypedWorkspaceModelMapper.ToInMemoryWorkspace(model);
        var records = new Dictionary<string, GenericRecord>(StringComparer.OrdinalIgnoreCase);
        var neighbors = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var entityRecords in workspace.Instance.RecordsByEntity)
        {
            foreach (var record in entityRecords.Value)
            {
                var key = RowKey(entityRecords.Key, record.Id);
                records.Add(key, record);
                neighbors.Add(key, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            }
        }

        foreach (var entityRecords in workspace.Instance.RecordsByEntity)
        {
            var entity = workspace.Model.FindEntity(entityRecords.Key)
                ?? throw new InvalidOperationException(
                    $"Entity '{entityRecords.Key}' was not found while pruning a MetaWeave query graph.");
            foreach (var record in entityRecords.Value)
            {
                var key = RowKey(entity.Name, record.Id);
                foreach (var relationshipValue in record.RelationshipIds)
                {
                    var relationship = entity.FindRelationshipByColumnName(relationshipValue.Key)
                        ?? throw new InvalidOperationException(
                            $"Relationship column '{relationshipValue.Key}' was not found on entity '{entity.Name}'.");
                    var targetKey = RowKey(relationship.Entity, relationshipValue.Value);
                    if (!neighbors.ContainsKey(targetKey))
                    {
                        continue;
                    }

                    neighbors[key].Add(targetKey);
                    neighbors[targetKey].Add(key);
                }
            }
        }

        var reachable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<string>(
            workspace.Instance.RecordsByEntity
                .GetValueOrDefault(nameof(Weave), [])
                .Select(record => RowKey(nameof(Weave), record.Id)));
        while (pending.TryDequeue(out var key))
        {
            if (!reachable.Add(key))
            {
                continue;
            }

            foreach (var neighbor in neighbors[key])
            {
                pending.Enqueue(neighbor);
            }
        }

        foreach (var entity in workspace.Model.Entities)
        {
            var property = typeof(MetaWeaveModel).GetProperty(entity.GetListName())
                ?? throw new InvalidOperationException(
                    $"MetaWeaveModel does not expose list '{entity.GetListName()}'.");
            var list = property.GetValue(model) as IList
                ?? throw new InvalidOperationException(
                    $"MetaWeaveModel member '{property.Name}' is not a mutable list.");
            for (var index = list.Count - 1; index >= 0; index--)
            {
                var item = list[index]
                    ?? throw new InvalidOperationException(
                        $"MetaWeaveModel member '{property.Name}' contains a null row.");
                var id = item.GetType().GetProperty(nameof(Weave.Id))?.GetValue(item) as string
                    ?? throw new InvalidOperationException(
                        $"MetaWeave row '{item.GetType().Name}' does not expose an identity.");
                if (!reachable.Contains(RowKey(entity.Name, id)))
                {
                    list.RemoveAt(index);
                }
            }
        }
    }

    private static string RowKey(string entityName, string id) =>
        entityName + "\0" + id;
}
