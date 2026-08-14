using System.Collections;
using Meta.Integration;
using Meta.Operations.Domain;
using MetaWeaveScript.Sql;

namespace MetaWeave.Core;

public sealed class MetaWeaveAuthoringService
{
    public MetaWeaveModel Create(
        string name,
        string leftModelName,
        string rightModelName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(leftModelName);
        ArgumentException.ThrowIfNullOrWhiteSpace(rightModelName);

        var model = MetaWeaveModel.CreateEmpty();
        model.WeaveList.Add(new Weave
        {
            Id = name.Trim(),
            LeftModelName = leftModelName.Trim(),
            RightModelName = rightModelName.Trim(),
        });
        return model;
    }

    public Direction AddDirection(
        MetaWeaveModel model,
        string name,
        string sourceModelName,
        string targetModelName)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceModelName);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetModelName);

        var weave = RequireWeave(model);
        var normalizedName = name.Trim();
        var normalizedSource = sourceModelName.Trim();
        var normalizedTarget = targetModelName.Trim();
        if (model.DirectionList.Any(direction =>
                MetaName.Comparer.Equals(direction.Id, normalizedName)))
        {
            throw new InvalidOperationException(
                $"Direction '{normalizedName}' already exists.");
        }

        var isForward =
            MetaName.Comparer.Equals(normalizedSource, weave.LeftModelName) &&
            MetaName.Comparer.Equals(normalizedTarget, weave.RightModelName);
        var isReverse =
            MetaName.Comparer.Equals(normalizedSource, weave.RightModelName) &&
            MetaName.Comparer.Equals(normalizedTarget, weave.LeftModelName);
        if (!isForward && !isReverse)
        {
            throw new InvalidOperationException(
                $"Direction '{normalizedName}' must map between weave models '{weave.LeftModelName}' and '{weave.RightModelName}'.");
        }

        if (model.DirectionList.Any(direction =>
                MetaName.Comparer.Equals(direction.SourceModelName, normalizedSource) &&
                MetaName.Comparer.Equals(direction.TargetModelName, normalizedTarget)))
        {
            throw new InvalidOperationException(
                $"A direction from '{normalizedSource}' to '{normalizedTarget}' already exists.");
        }

        var direction = new Direction
        {
            Id = normalizedName,
            SourceModelName = normalizedSource,
            TargetModelName = normalizedTarget,
            Weave = weave,
        };
        model.DirectionList.Add(direction);
        return direction;
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
