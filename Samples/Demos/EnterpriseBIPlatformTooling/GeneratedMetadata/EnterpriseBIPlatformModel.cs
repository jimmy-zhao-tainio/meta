using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Meta.Core.Domain;

namespace EnterpriseBIPlatform
{
    public sealed partial class EnterpriseBIPlatformModel
    {
        internal EnterpriseBIPlatformModel(
            IReadOnlyList<Cube> cubeList,
            IReadOnlyList<Dimension> dimensionList,
            IReadOnlyList<Fact> factList,
            IReadOnlyList<Measure> measureList,
            IReadOnlyList<System> systemList,
            IReadOnlyList<SystemCube> systemCubeList,
            IReadOnlyList<SystemDimension> systemDimensionList,
            IReadOnlyList<SystemFact> systemFactList,
            IReadOnlyList<SystemType> systemTypeList
        )
        {
            CubeList = cubeList;
            DimensionList = dimensionList;
            FactList = factList;
            MeasureList = measureList;
            SystemList = systemList;
            SystemCubeList = systemCubeList;
            SystemDimensionList = systemDimensionList;
            SystemFactList = systemFactList;
            SystemTypeList = systemTypeList;
        }

        public IReadOnlyList<Cube> CubeList { get; }
        public IReadOnlyList<Dimension> DimensionList { get; }
        public IReadOnlyList<Fact> FactList { get; }
        public IReadOnlyList<Measure> MeasureList { get; }
        public IReadOnlyList<System> SystemList { get; }
        public IReadOnlyList<SystemCube> SystemCubeList { get; }
        public IReadOnlyList<SystemDimension> SystemDimensionList { get; }
        public IReadOnlyList<SystemFact> SystemFactList { get; }
        public IReadOnlyList<SystemType> SystemTypeList { get; }
    }

    internal static class EnterpriseBIPlatformModelFactory
    {
        internal static EnterpriseBIPlatformModel CreateFromWorkspace(Workspace workspace)
        {
            if (workspace == null)
            {
                throw new global::System.ArgumentNullException(nameof(workspace));
            }

            var cubeList = new List<Cube>();
            if (workspace.Instance.RecordsByEntity.TryGetValue("Cube", out var cubeListRecords))
            {
                foreach (var record in cubeListRecords.OrderBy(item => item.Id, global::System.StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Id, global::System.StringComparer.Ordinal))
                {
                    cubeList.Add(new Cube
                    {
                        Id = record.Id ?? string.Empty,
                        CubeName = record.Values.TryGetValue("CubeName", out var cubeNameValue) ? cubeNameValue ?? string.Empty : string.Empty,
                        Purpose = record.Values.TryGetValue("Purpose", out var purposeValue) ? purposeValue ?? string.Empty : string.Empty,
                        RefreshMode = record.Values.TryGetValue("RefreshMode", out var refreshModeValue) ? refreshModeValue ?? string.Empty : string.Empty,
                    });
                }
            }

            var dimensionList = new List<Dimension>();
            if (workspace.Instance.RecordsByEntity.TryGetValue("Dimension", out var dimensionListRecords))
            {
                foreach (var record in dimensionListRecords.OrderBy(item => item.Id, global::System.StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Id, global::System.StringComparer.Ordinal))
                {
                    dimensionList.Add(new Dimension
                    {
                        Id = record.Id ?? string.Empty,
                        DimensionName = record.Values.TryGetValue("DimensionName", out var dimensionNameValue) ? dimensionNameValue ?? string.Empty : string.Empty,
                        HierarchyCount = record.Values.TryGetValue("HierarchyCount", out var hierarchyCountValue) ? hierarchyCountValue ?? string.Empty : string.Empty,
                        IsConformed = record.Values.TryGetValue("IsConformed", out var isConformedValue) ? isConformedValue ?? string.Empty : string.Empty,
                    });
                }
            }

            var factList = new List<Fact>();
            if (workspace.Instance.RecordsByEntity.TryGetValue("Fact", out var factListRecords))
            {
                foreach (var record in factListRecords.OrderBy(item => item.Id, global::System.StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Id, global::System.StringComparer.Ordinal))
                {
                    factList.Add(new Fact
                    {
                        Id = record.Id ?? string.Empty,
                        BusinessArea = record.Values.TryGetValue("BusinessArea", out var businessAreaValue) ? businessAreaValue ?? string.Empty : string.Empty,
                        FactName = record.Values.TryGetValue("FactName", out var factNameValue) ? factNameValue ?? string.Empty : string.Empty,
                        Grain = record.Values.TryGetValue("Grain", out var grainValue) ? grainValue ?? string.Empty : string.Empty,
                        MeasureCount = record.Values.TryGetValue("MeasureCount", out var measureCountValue) ? measureCountValue ?? string.Empty : string.Empty,
                    });
                }
            }

            var measureList = new List<Measure>();
            if (workspace.Instance.RecordsByEntity.TryGetValue("Measure", out var measureListRecords))
            {
                foreach (var record in measureListRecords.OrderBy(item => item.Id, global::System.StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Id, global::System.StringComparer.Ordinal))
                {
                    measureList.Add(new Measure
                    {
                        Id = record.Id ?? string.Empty,
                        MDX = record.Values.TryGetValue("MDX", out var mDXValue) ? mDXValue ?? string.Empty : string.Empty,
                        MeasureName = record.Values.TryGetValue("MeasureName", out var measureNameValue) ? measureNameValue ?? string.Empty : string.Empty,
                        CubeId = record.RelationshipIds.TryGetValue("CubeId", out var cubeRelationshipId) ? cubeRelationshipId ?? string.Empty : string.Empty,
                    });
                }
            }

            var systemList = new List<System>();
            if (workspace.Instance.RecordsByEntity.TryGetValue("System", out var systemListRecords))
            {
                foreach (var record in systemListRecords.OrderBy(item => item.Id, global::System.StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Id, global::System.StringComparer.Ordinal))
                {
                    systemList.Add(new System
                    {
                        Id = record.Id ?? string.Empty,
                        DeploymentDate = record.Values.TryGetValue("DeploymentDate", out var deploymentDateValue) ? deploymentDateValue ?? string.Empty : string.Empty,
                        SystemName = record.Values.TryGetValue("SystemName", out var systemNameValue) ? systemNameValue ?? string.Empty : string.Empty,
                        Version = record.Values.TryGetValue("Version", out var versionValue) ? versionValue ?? string.Empty : string.Empty,
                        SystemTypeId = record.RelationshipIds.TryGetValue("SystemTypeId", out var systemTypeRelationshipId) ? systemTypeRelationshipId ?? string.Empty : string.Empty,
                    });
                }
            }

            var systemCubeList = new List<SystemCube>();
            if (workspace.Instance.RecordsByEntity.TryGetValue("SystemCube", out var systemCubeListRecords))
            {
                foreach (var record in systemCubeListRecords.OrderBy(item => item.Id, global::System.StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Id, global::System.StringComparer.Ordinal))
                {
                    systemCubeList.Add(new SystemCube
                    {
                        Id = record.Id ?? string.Empty,
                        ProcessingMode = record.Values.TryGetValue("ProcessingMode", out var processingModeValue) ? processingModeValue ?? string.Empty : string.Empty,
                        CubeId = record.RelationshipIds.TryGetValue("CubeId", out var cubeRelationshipId) ? cubeRelationshipId ?? string.Empty : string.Empty,
                        SystemId = record.RelationshipIds.TryGetValue("SystemId", out var systemRelationshipId) ? systemRelationshipId ?? string.Empty : string.Empty,
                    });
                }
            }

            var systemDimensionList = new List<SystemDimension>();
            if (workspace.Instance.RecordsByEntity.TryGetValue("SystemDimension", out var systemDimensionListRecords))
            {
                foreach (var record in systemDimensionListRecords.OrderBy(item => item.Id, global::System.StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Id, global::System.StringComparer.Ordinal))
                {
                    systemDimensionList.Add(new SystemDimension
                    {
                        Id = record.Id ?? string.Empty,
                        ConformanceLevel = record.Values.TryGetValue("ConformanceLevel", out var conformanceLevelValue) ? conformanceLevelValue ?? string.Empty : string.Empty,
                        DimensionId = record.RelationshipIds.TryGetValue("DimensionId", out var dimensionRelationshipId) ? dimensionRelationshipId ?? string.Empty : string.Empty,
                        SystemId = record.RelationshipIds.TryGetValue("SystemId", out var systemRelationshipId) ? systemRelationshipId ?? string.Empty : string.Empty,
                    });
                }
            }

            var systemFactList = new List<SystemFact>();
            if (workspace.Instance.RecordsByEntity.TryGetValue("SystemFact", out var systemFactListRecords))
            {
                foreach (var record in systemFactListRecords.OrderBy(item => item.Id, global::System.StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Id, global::System.StringComparer.Ordinal))
                {
                    systemFactList.Add(new SystemFact
                    {
                        Id = record.Id ?? string.Empty,
                        LoadPattern = record.Values.TryGetValue("LoadPattern", out var loadPatternValue) ? loadPatternValue ?? string.Empty : string.Empty,
                        FactId = record.RelationshipIds.TryGetValue("FactId", out var factRelationshipId) ? factRelationshipId ?? string.Empty : string.Empty,
                        SystemId = record.RelationshipIds.TryGetValue("SystemId", out var systemRelationshipId) ? systemRelationshipId ?? string.Empty : string.Empty,
                    });
                }
            }

            var systemTypeList = new List<SystemType>();
            if (workspace.Instance.RecordsByEntity.TryGetValue("SystemType", out var systemTypeListRecords))
            {
                foreach (var record in systemTypeListRecords.OrderBy(item => item.Id, global::System.StringComparer.OrdinalIgnoreCase).ThenBy(item => item.Id, global::System.StringComparer.Ordinal))
                {
                    systemTypeList.Add(new SystemType
                    {
                        Id = record.Id ?? string.Empty,
                        Description = record.Values.TryGetValue("Description", out var descriptionValue) ? descriptionValue ?? string.Empty : string.Empty,
                        TypeName = record.Values.TryGetValue("TypeName", out var typeNameValue) ? typeNameValue ?? string.Empty : string.Empty,
                    });
                }
            }

            var cubeListById = new Dictionary<string, Cube>(global::System.StringComparer.Ordinal);
            foreach (var row in cubeList)
            {
                cubeListById[row.Id] = row;
            }

            var dimensionListById = new Dictionary<string, Dimension>(global::System.StringComparer.Ordinal);
            foreach (var row in dimensionList)
            {
                dimensionListById[row.Id] = row;
            }

            var factListById = new Dictionary<string, Fact>(global::System.StringComparer.Ordinal);
            foreach (var row in factList)
            {
                factListById[row.Id] = row;
            }

            var measureListById = new Dictionary<string, Measure>(global::System.StringComparer.Ordinal);
            foreach (var row in measureList)
            {
                measureListById[row.Id] = row;
            }

            var systemListById = new Dictionary<string, System>(global::System.StringComparer.Ordinal);
            foreach (var row in systemList)
            {
                systemListById[row.Id] = row;
            }

            var systemCubeListById = new Dictionary<string, SystemCube>(global::System.StringComparer.Ordinal);
            foreach (var row in systemCubeList)
            {
                systemCubeListById[row.Id] = row;
            }

            var systemDimensionListById = new Dictionary<string, SystemDimension>(global::System.StringComparer.Ordinal);
            foreach (var row in systemDimensionList)
            {
                systemDimensionListById[row.Id] = row;
            }

            var systemFactListById = new Dictionary<string, SystemFact>(global::System.StringComparer.Ordinal);
            foreach (var row in systemFactList)
            {
                systemFactListById[row.Id] = row;
            }

            var systemTypeListById = new Dictionary<string, SystemType>(global::System.StringComparer.Ordinal);
            foreach (var row in systemTypeList)
            {
                systemTypeListById[row.Id] = row;
            }

            foreach (var row in measureList)
            {
                row.Cube = RequireTarget(
                    cubeListById,
                    row.CubeId,
                    "Measure",
                    row.Id,
                    "CubeId");
            }

            foreach (var row in systemList)
            {
                row.SystemType = RequireTarget(
                    systemTypeListById,
                    row.SystemTypeId,
                    "System",
                    row.Id,
                    "SystemTypeId");
            }

            foreach (var row in systemCubeList)
            {
                row.Cube = RequireTarget(
                    cubeListById,
                    row.CubeId,
                    "SystemCube",
                    row.Id,
                    "CubeId");
            }

            foreach (var row in systemCubeList)
            {
                row.System = RequireTarget(
                    systemListById,
                    row.SystemId,
                    "SystemCube",
                    row.Id,
                    "SystemId");
            }

            foreach (var row in systemDimensionList)
            {
                row.Dimension = RequireTarget(
                    dimensionListById,
                    row.DimensionId,
                    "SystemDimension",
                    row.Id,
                    "DimensionId");
            }

            foreach (var row in systemDimensionList)
            {
                row.System = RequireTarget(
                    systemListById,
                    row.SystemId,
                    "SystemDimension",
                    row.Id,
                    "SystemId");
            }

            foreach (var row in systemFactList)
            {
                row.Fact = RequireTarget(
                    factListById,
                    row.FactId,
                    "SystemFact",
                    row.Id,
                    "FactId");
            }

            foreach (var row in systemFactList)
            {
                row.System = RequireTarget(
                    systemListById,
                    row.SystemId,
                    "SystemFact",
                    row.Id,
                    "SystemId");
            }

            return new EnterpriseBIPlatformModel(
                new ReadOnlyCollection<Cube>(cubeList),
                new ReadOnlyCollection<Dimension>(dimensionList),
                new ReadOnlyCollection<Fact>(factList),
                new ReadOnlyCollection<Measure>(measureList),
                new ReadOnlyCollection<System>(systemList),
                new ReadOnlyCollection<SystemCube>(systemCubeList),
                new ReadOnlyCollection<SystemDimension>(systemDimensionList),
                new ReadOnlyCollection<SystemFact>(systemFactList),
                new ReadOnlyCollection<SystemType>(systemTypeList)
            );
        }

        private static T RequireTarget<T>(
            Dictionary<string, T> rowsById,
            string targetId,
            string sourceEntityName,
            string sourceId,
            string relationshipName)
            where T : class
        {
            if (string.IsNullOrEmpty(targetId))
            {
                throw new global::System.InvalidOperationException(
                    $"Relationship '{sourceEntityName}.{relationshipName}' on row '{sourceEntityName}:{sourceId}' is empty."
            }

            if (!rowsById.TryGetValue(targetId, out var target))
            {
                throw new global::System.InvalidOperationException(
                    $"Relationship '{sourceEntityName}.{relationshipName}' on row '{sourceEntityName}:{sourceId}' points to missing Id '{targetId}'."
            }

            return target;
        }
    }
}
