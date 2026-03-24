using System;
using System.Collections.Generic;
using System.Linq;
using Meta.Core.Domain;

namespace Meta.Core.Services;

public sealed class GraphStatsReport
{
    public int NodeCount { get; set; }
    public int EdgeCount { get; set; }
    public int UniqueEdgeCount { get; set; }
    public int DuplicateEdgeCount { get; set; }
    public int MissingTargetEdgeCount { get; set; }
    public int WeaklyConnectedComponents { get; set; }
    public int RootCount { get; set; }
    public int SinkCount { get; set; }
    public int IsolatedCount { get; set; }
    public bool HasCycles { get; set; }
    public int CycleCount { get; set; }
    public int? DagMaxDepth { get; set; }
    public double AverageInDegree { get; set; }
    public double AverageOutDegree { get; set; }
    public List<GraphHub> TopOutDegree { get; } = new();
    public List<GraphHub> TopInDegree { get; } = new();
    public List<string> CycleSamples { get; } = new();
}

public sealed class GraphHub
{
    public string Entity { get; set; } = string.Empty;
    public int Degree { get; set; }
}

public static class GraphStatsService
{
    public static GraphStatsReport Compute(GenericModel model, int topN = 10, int cycleSampleLimit = 10)
    {
        if (model == null)
        {
            throw new ArgumentNullException(nameof(model));
        }

        if (topN <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(topN), "topN must be > 0.");
        }

        if (cycleSampleLimit < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cycleSampleLimit), "cycleSampleLimit must be >= 0.");
        }

        var nodeNames = model.Entities
            .Where(entity => !string.IsNullOrWhiteSpace(entity.Name))
            .Select(entity => entity.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var nodeSet = new HashSet<string>(nodeNames, StringComparer.OrdinalIgnoreCase);

        var declaredEdges = new List<(string Source, string Target)>();
        foreach (var entity in model.Entities)
        {
            if (string.IsNullOrWhiteSpace(entity.Name))
            {
                continue;
            }

            foreach (var relationship in entity.Relationships)
            {
                if (string.IsNullOrWhiteSpace(relationship.Entity))
                {
                    continue;
                }

                declaredEdges.Add((entity.Name, relationship.Entity));
            }
        }

        var uniqueDeclaredEdgeSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var edge in declaredEdges)
        {
            uniqueDeclaredEdgeSet.Add(EdgeKey(edge.Source, edge.Target));
        }

        var adjacency = nodeNames.ToDictionary(
            name => name,
            _ => new SortedSet<string>(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
        var inDegree = nodeNames.ToDictionary(name => name, _ => 0, StringComparer.OrdinalIgnoreCase);
        var outDegree = nodeNames.ToDictionary(name => name, _ => 0, StringComparer.OrdinalIgnoreCase);

        var missingTargetEdgeCount = 0;
        foreach (var edge in declaredEdges)
        {
            if (!nodeSet.Contains(edge.Source) || !nodeSet.Contains(edge.Target))
            {
                missingTargetEdgeCount++;
                continue;
            }

            if (adjacency[edge.Source].Add(edge.Target))
            {
                outDegree[edge.Source]++;
                inDegree[edge.Target]++;
            }
        }

        var uniqueExistingEdgeCount = adjacency.Values.Sum(targets => targets.Count);
        var weakComponents = ComputeWeaklyConnectedComponents(nodeNames, adjacency);
        var rootCount = nodeNames.Count(name => inDegree[name] == 0);
        var sinkCount = nodeNames.Count(name => outDegree[name] == 0);
        var isolatedCount = nodeNames.Count(name => inDegree[name] == 0 && outDegree[name] == 0);

        var stronglyConnectedComponents = TarjanScc(nodeNames, adjacency);
        var cyclicComponents = stronglyConnectedComponents
            .Where(component => component.Count > 1 || (component.Count == 1 && adjacency[component[0]].Contains(component[0])))
            .ToList();
        var hasCycles = cyclicComponents.Count > 0;

        var report = new GraphStatsReport
        {
            NodeCount = nodeNames.Count,
            EdgeCount = declaredEdges.Count,
            UniqueEdgeCount = uniqueDeclaredEdgeSet.Count,
            DuplicateEdgeCount = declaredEdges.Count - uniqueDeclaredEdgeSet.Count,
            MissingTargetEdgeCount = missingTargetEdgeCount,
            WeaklyConnectedComponents = weakComponents,
            RootCount = rootCount,
            SinkCount = sinkCount,
            IsolatedCount = isolatedCount,
            HasCycles = hasCycles,
            CycleCount = cyclicComponents.Count,
            DagMaxDepth = hasCycles ? null : ComputeDagMaxDepth(nodeNames, adjacency, inDegree),
            AverageInDegree = nodeNames.Count == 0 ? 0d : uniqueExistingEdgeCount / (double)nodeNames.Count,
            AverageOutDegree = nodeNames.Count == 0 ? 0d : uniqueExistingEdgeCount / (double)nodeNames.Count,
        };

        foreach (var item in outDegree
                     .OrderByDescending(pair => pair.Value)
                     .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                     .Take(topN))
        {
            report.TopOutDegree.Add(new GraphHub
            {
                Entity = item.Key,
                Degree = item.Value,
            });
        }

        foreach (var item in inDegree
                     .OrderByDescending(pair => pair.Value)
                     .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                     .Take(topN))
        {
            report.TopInDegree.Add(new GraphHub
            {
                Entity = item.Key,
                Degree = item.Value,
            });
        }

        if (cycleSampleLimit > 0)
        {
            foreach (var component in cyclicComponents.Take(cycleSampleLimit))
            {
                report.CycleSamples.Add(BuildCycleSample(component, adjacency));
            }
        }

        return report;
    }

    private static int ComputeWeaklyConnectedComponents(
        IReadOnlyCollection<string> nodeNames,
        IReadOnlyDictionary<string, SortedSet<string>> adjacency)
    {
        var undirected = nodeNames.ToDictionary(
            name => name,
            _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);

        foreach (var pair in adjacency)
        {
            foreach (var target in pair.Value)
            {
                undirected[pair.Key].Add(target);
                undirected[target].Add(pair.Key);
            }
        }

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var componentCount = 0;
        foreach (var node in nodeNames.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
        {
            if (!visited.Add(node))
            {
                continue;
            }

            componentCount++;
            var queue = new Queue<string>();
            queue.Enqueue(node);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var neighbor in undirected[current].OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
                {
                    if (visited.Add(neighbor))
                    {
                        queue.Enqueue(neighbor);
                    }
                }
            }
        }

        return componentCount;
    }

    private static int ComputeDagMaxDepth(
        IReadOnlyCollection<string> nodeNames,
        IReadOnlyDictionary<string, SortedSet<string>> adjacency,
        IReadOnlyDictionary<string, int> inDegree)
    {
        var inDegreeWork = inDegree.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        var depth = nodeNames.ToDictionary(name => name, _ => 0, StringComparer.OrdinalIgnoreCase);

        var queue = new Queue<string>(inDegreeWork
            .Where(pair => pair.Value == 0)
            .Select(pair => pair.Key)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase));
        var visitedCount = 0;

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            visitedCount++;
            foreach (var neighbor in adjacency[current].OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
            {
                depth[neighbor] = Math.Max(depth[neighbor], depth[current] + 1);
                inDegreeWork[neighbor]--;
                if (inDegreeWork[neighbor] == 0)
                {
                    queue.Enqueue(neighbor);
                }
            }
        }

        if (visitedCount != nodeNames.Count)
        {
            return -1;
        }

        return depth.Values.DefaultIfEmpty(0).Max();
    }

    private static List<List<string>> TarjanScc(
        IReadOnlyCollection<string> nodeNames,
        IReadOnlyDictionary<string, SortedSet<string>> adjacency)
    {
        var index = 0;
        var stack = new Stack<string>();
        var onStack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var indexes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var lowLinks = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var sccs = new List<List<string>>();

        foreach (var node in nodeNames.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
        {
            if (!indexes.ContainsKey(node))
            {
                StrongConnect(node);
            }
        }

        return sccs;

        void StrongConnect(string node)
        {
            indexes[node] = index;
            lowLinks[node] = index;
            index++;
            stack.Push(node);
            onStack.Add(node);

            foreach (var neighbor in adjacency[node].OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
            {
                if (!indexes.ContainsKey(neighbor))
                {
                    StrongConnect(neighbor);
                    lowLinks[node] = Math.Min(lowLinks[node], lowLinks[neighbor]);
                }
                else if (onStack.Contains(neighbor))
                {
                    lowLinks[node] = Math.Min(lowLinks[node], indexes[neighbor]);
                }
            }

            if (lowLinks[node] != indexes[node])
            {
                return;
            }

            var component = new List<string>();
            while (stack.Count > 0)
            {
                var popped = stack.Pop();
                onStack.Remove(popped);
                component.Add(popped);
                if (string.Equals(popped, node, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
            }

            component.Sort(StringComparer.OrdinalIgnoreCase);
            sccs.Add(component);
        }
    }

    private static string BuildCycleSample(
        IReadOnlyCollection<string> component,
        IReadOnlyDictionary<string, SortedSet<string>> adjacency)
    {
        if (component.Count == 1)
        {
            var node = component.First();
            return $"{node}->{node}";
        }

        var allowed = new HashSet<string>(component, StringComparer.OrdinalIgnoreCase);
        var start = component.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).First();
        var path = new List<string> { start };
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { start };

        if (TryBuildCyclePath(start, start, adjacency, allowed, visited, path, out var cyclePath))
        {
            return string.Join("->", cyclePath);
        }

        var ordered = component.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
        ordered.Add(ordered[0]);
        return string.Join("->", ordered);
    }

    private static bool TryBuildCyclePath(
        string current,
        string start,
        IReadOnlyDictionary<string, SortedSet<string>> adjacency,
        IReadOnlySet<string> allowed,
        ISet<string> visited,
        IList<string> path,
        out IReadOnlyList<string> cyclePath)
    {
        foreach (var neighbor in adjacency[current].OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
        {
            if (!allowed.Contains(neighbor))
            {
                continue;
            }

            if (string.Equals(neighbor, start, StringComparison.OrdinalIgnoreCase) && path.Count > 1)
            {
                var result = path.ToList();
                result.Add(start);
                cyclePath = result;
                return true;
            }

            if (!visited.Add(neighbor))
            {
                continue;
            }

            path.Add(neighbor);
            if (TryBuildCyclePath(neighbor, start, adjacency, allowed, visited, path, out cyclePath))
            {
                return true;
            }

            path.RemoveAt(path.Count - 1);
            visited.Remove(neighbor);
        }

        cyclePath = Array.Empty<string>();
        return false;
    }

    private static string EdgeKey(string source, string target)
    {
        return source + "->" + target;
    }
}

