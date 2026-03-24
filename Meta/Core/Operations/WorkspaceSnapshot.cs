using System;
using System.Collections.Generic;
using System.Linq;
using Meta.Core.Domain;
using MetaWorkspaceGenerated = Meta.Core.WorkspaceConfig.Generated.MetaWorkspace;
using DomainWorkspace = Meta.Core.Domain.Workspace;

namespace Meta.Core.Operations;

public sealed class WorkspaceSnapshot
{
    public MetaWorkspaceGenerated WorkspaceConfig { get; set; } = new();
    public GenericModel Model { get; set; } = new();
    public GenericInstance Instance { get; set; } = new();
}

public static class WorkspaceSnapshotCloner
{
    public static WorkspaceSnapshot Capture(DomainWorkspace workspace)
    {
        if (workspace == null)
        {
            throw new ArgumentNullException(nameof(workspace));
        }

        return new WorkspaceSnapshot
        {
            WorkspaceConfig = CloneWorkspaceConfig(workspace.WorkspaceConfig),
            Model = CloneModel(workspace.Model),
            Instance = CloneInstance(workspace.Instance),
        };
    }

    public static void Restore(DomainWorkspace workspace, WorkspaceSnapshot snapshot)
    {
        if (workspace == null)
        {
            throw new ArgumentNullException(nameof(workspace));
        }

        if (snapshot == null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        workspace.WorkspaceConfig = CloneWorkspaceConfig(snapshot.WorkspaceConfig);
        workspace.Model = CloneModel(snapshot.Model);
        workspace.Instance = CloneInstance(snapshot.Instance);
        workspace.IsDirty = true;
    }

    public static GenericModel CloneModel(GenericModel source)
    {
        var clone = new GenericModel
        {
            Name = source.Name,
        };

        foreach (var entity in source.Entities)
        {
            var entityClone = new GenericEntity
            {
                Name = entity.Name,
            };

            foreach (var property in entity.Properties)
            {
                entityClone.Properties.Add(new GenericProperty
                {
                    Name = property.Name,
                    DataType = property.DataType,
                    IsNullable = property.IsNullable,
                });
            }

            foreach (var relationship in entity.Relationships)
            {
                entityClone.Relationships.Add(new GenericRelationship
                {
                    Entity = relationship.Entity,
                    Role = relationship.Role,
                });
            }

            clone.Entities.Add(entityClone);
        }

        return clone;
    }

    public static GenericInstance CloneInstance(GenericInstance source)
    {
        var clone = new GenericInstance
        {
            ModelName = source.ModelName,
        };

        foreach (var kvp in source.RecordsByEntity)
        {
            var targetList = clone.GetOrCreateEntityRecords(kvp.Key);
            foreach (var record in kvp.Value)
            {
                var recordClone = new GenericRecord
                {
                    Id = record.Id,
                    SourceShardFileName = record.SourceShardFileName,
                };

                foreach (var value in record.Values)
                {
                    recordClone.Values[value.Key] = value.Value;
                }

                foreach (var relationship in record.RelationshipIds)
                {
                    recordClone.RelationshipIds[relationship.Key] = relationship.Value;
                }

                targetList.Add(recordClone);
            }
        }

        return clone;
    }

    public static MetaWorkspaceGenerated CloneWorkspaceConfig(MetaWorkspaceGenerated source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return MetaWorkspaceGenerated.Normalize(source, "workspace-config");
    }

    public static RowPatch ToRowPatch(GenericRecord record)
    {
        return new RowPatch
        {
            Id = record.Id,
            ReplaceExisting = false,
            Values = record.Values.ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase),
            RelationshipIds = record.RelationshipIds.ToDictionary(item => item.Key, item => item.Value, StringComparer.OrdinalIgnoreCase),
        };
    }
}


