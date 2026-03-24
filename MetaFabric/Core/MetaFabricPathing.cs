using Meta.Core.Domain;

namespace MetaFabric.Core;

internal sealed record FabricScopePathStepDefinition(
    string ReferenceName,
    int Ordinal);

internal sealed record FabricResolvedPath(
    string EntityName,
    string RowId);

internal static class MetaFabricPathing
{
    public static IReadOnlyList<string> ParsePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("Scope path must not be empty.");
        }

        var steps = path
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(step => !string.IsNullOrWhiteSpace(step))
            .ToArray();
        if (steps.Length == 0)
        {
            throw new InvalidOperationException($"Scope path '{path}' did not contain any usable steps.");
        }

        return steps;
    }

    public static string SerializePath(IEnumerable<string> steps)
    {
        return string.Join('.', steps);
    }

    public static string SerializePath(IEnumerable<FabricScopePathStepDefinition> steps)
    {
        return SerializePath(steps.OrderBy(step => step.Ordinal).Select(step => step.ReferenceName));
    }

    public static void ValidatePath(GenericModel model, string startEntityName, IReadOnlyList<FabricScopePathStepDefinition> steps, string expectedTerminalEntityName, string context)
    {
        var currentEntity = model.FindEntity(startEntityName)
            ?? throw new InvalidOperationException($"{context}: entity '{startEntityName}' was not found in model '{model.Name}'.");

        foreach (var step in steps.OrderBy(item => item.Ordinal))
        {
            if (string.Equals(step.ReferenceName, "Id", StringComparison.Ordinal))
            {
                continue;
            }

            var relationship = currentEntity.FindRelationshipByColumnName(step.ReferenceName);
            if (relationship == null)
            {
                throw new InvalidOperationException($"{context}: relationship usage '{step.ReferenceName}' was not found on entity '{currentEntity.Name}' in model '{model.Name}'.");
            }

            currentEntity = model.FindEntity(relationship.Entity)
                ?? throw new InvalidOperationException($"{context}: target entity '{relationship.Entity}' was not found in model '{model.Name}'.");
        }

        if (!string.Equals(currentEntity.Name, expectedTerminalEntityName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{context}: path terminates at entity '{currentEntity.Name}', expected '{expectedTerminalEntityName}'.");
        }
    }

    public static bool TryResolvePath(
        Workspace workspace,
        string startEntityName,
        GenericRecord startRow,
        IReadOnlyList<FabricScopePathStepDefinition> steps,
        out FabricResolvedPath resolvedPath,
        out string error)
    {
        var currentEntityName = startEntityName;
        var currentRow = startRow;
        foreach (var step in steps.OrderBy(item => item.Ordinal))
        {
            if (string.Equals(step.ReferenceName, "Id", StringComparison.Ordinal))
            {
                continue;
            }

            var currentEntity = workspace.Model.FindEntity(currentEntityName);
            if (currentEntity == null)
            {
                error = $"Entity '{currentEntityName}' was not found in model '{workspace.Model.Name}'.";
                resolvedPath = new FabricResolvedPath(string.Empty, string.Empty);
                return false;
            }

            var relationship = currentEntity.FindRelationshipByColumnName(step.ReferenceName);
            if (relationship == null)
            {
                error = $"Relationship usage '{step.ReferenceName}' was not found on entity '{currentEntity.Name}'.";
                resolvedPath = new FabricResolvedPath(string.Empty, string.Empty);
                return false;
            }

            if (!currentRow.RelationshipIds.TryGetValue(step.ReferenceName, out var nextId) || string.IsNullOrWhiteSpace(nextId))
            {
                error = $"Row '{currentEntity.Name}:{currentRow.Id}' is missing relationship usage '{step.ReferenceName}'.";
                resolvedPath = new FabricResolvedPath(string.Empty, string.Empty);
                return false;
            }

            var nextEntityName = relationship.Entity;
            var nextRow = workspace.Instance.GetOrCreateEntityRecords(nextEntityName)
                .SingleOrDefault(record => string.Equals(record.Id, nextId, StringComparison.Ordinal));
            if (nextRow == null)
            {
                error = $"Row '{currentEntity.Name}:{currentRow.Id}' references missing '{nextEntityName}:{nextId}' through '{step.ReferenceName}'.";
                resolvedPath = new FabricResolvedPath(string.Empty, string.Empty);
                return false;
            }

            currentEntityName = nextEntityName;
            currentRow = nextRow;
        }

        error = string.Empty;
        resolvedPath = new FabricResolvedPath(currentEntityName, currentRow.Id);
        return true;
    }

    public static IReadOnlyList<string> EnumerateCandidatePaths(GenericModel model, GenericEntity startEntity, string expectedTerminalEntityName, int maxDepth = 3)
    {
        var results = new HashSet<string>(StringComparer.Ordinal);
        if (string.Equals(startEntity.Name, expectedTerminalEntityName, StringComparison.Ordinal))
        {
            results.Add("Id");
        }

        EnumerateRecursive(model, startEntity, expectedTerminalEntityName, maxDepth, new List<string>(), new HashSet<string>(StringComparer.Ordinal) { startEntity.Name }, results);

        return results
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item, StringComparer.Ordinal)
            .ToList();
    }

    private static void EnumerateRecursive(
        GenericModel model,
        GenericEntity currentEntity,
        string expectedTerminalEntityName,
        int remainingDepth,
        List<string> currentSteps,
        HashSet<string> visitedEntities,
        HashSet<string> results)
    {
        if (remainingDepth <= 0)
        {
            return;
        }

        foreach (var relationship in currentEntity.Relationships.OrderBy(item => item.GetColumnName(), StringComparer.Ordinal))
        {
            var nextEntityName = relationship.Entity;
            currentSteps.Add(relationship.GetColumnName());
            if (string.Equals(nextEntityName, expectedTerminalEntityName, StringComparison.Ordinal))
            {
                results.Add(SerializePath(currentSteps));
            }

            if (!visitedEntities.Contains(nextEntityName))
            {
                var nextEntity = model.FindEntity(nextEntityName);
                if (nextEntity != null)
                {
                    visitedEntities.Add(nextEntityName);
                    EnumerateRecursive(model, nextEntity, expectedTerminalEntityName, remainingDepth - 1, currentSteps, visitedEntities, results);
                    visitedEntities.Remove(nextEntityName);
                }
            }

            currentSteps.RemoveAt(currentSteps.Count - 1);
        }
    }
}
