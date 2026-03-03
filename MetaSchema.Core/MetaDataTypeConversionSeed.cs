using System.Globalization;
using Meta.Core.Domain;

namespace MetaSchema.Core;

public static class MetaDataTypeConversionSeed
{
    public static Workspace CreateWorkspace(string workspaceRootPath)
    {
        var workspace = MetaSchemaWorkspaceFactory.CreateEmptyWorkspace(
            workspaceRootPath,
            MetaSchemaModels.CreateMetaDataTypeConversionModel());
        new SeedBuilder(workspace).Seed();
        return workspace;
    }

    private sealed class SeedBuilder
    {
        private readonly Workspace workspace;
        private readonly Dictionary<string, int> idCounters = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> typeSystemIds = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> dataTypeIds = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> facetIds = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> settingIds = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> implementationIds = new(StringComparer.OrdinalIgnoreCase);

        public SeedBuilder(Workspace workspace)
        {
            this.workspace = workspace;
        }

        public void Seed()
        {
            SeedTypeSystems();
            SeedFacets();
            SeedDataTypes();
            SeedDataTypeFacets();
            SeedTypeSpecs();
            SeedSettings();
            SeedConversionImplementations();
            SeedMappings();
        }

        private void SeedTypeSystems()
        {
            foreach (var value in MetaDataTypeConversionSeedData.TypeSystems)
            {
                var id = AddRow("TypeSystem", new Dictionary<string, string?> { ["Name"] = value });
                typeSystemIds[value] = id;
            }
        }

        private void SeedFacets()
        {
            foreach (var value in MetaDataTypeConversionSeedData.Facets)
            {
                var id = AddRow("Facet", new Dictionary<string, string?>
                {
                    ["Name"] = value.Name,
                    ["ValueKind"] = value.ValueKind,
                });
                facetIds[value.Name] = id;
            }
        }

        private void SeedDataTypes()
        {
            foreach (var value in MetaDataTypeConversionSeedData.DataTypes)
            {
                var id = AddRow(
                    "DataType",
                    new Dictionary<string, string?>
                    {
                        ["Name"] = value.Name,
                        ["Category"] = value.Category,
                    },
                    new Dictionary<string, string>
                    {
                        ["TypeSystemId"] = RequireId(typeSystemIds, value.TypeSystem, "TypeSystem"),
                    });
                dataTypeIds[BuildDataTypeKey(value.TypeSystem, value.Name)] = id;
            }
        }

        private void SeedDataTypeFacets()
        {
            foreach (var value in MetaDataTypeConversionSeedData.DataTypeFacets)
            {
                AddRow(
                    "DataTypeFacet",
                    new Dictionary<string, string?>
                    {
                        ["IsSupported"] = BoolText(value.IsSupported),
                        ["IsRequired"] = BoolText(value.IsRequired),
                        ["DefaultInt"] = value.DefaultInt.HasValue ? IntText(value.DefaultInt.Value) : null,
                        ["DefaultBool"] = value.DefaultBool.HasValue ? BoolText(value.DefaultBool.Value) : null,
                    },
                    new Dictionary<string, string>
                    {
                        ["DataTypeId"] = RequireDataTypeId(value.TypeSystem, value.DataType),
                        ["FacetId"] = RequireId(facetIds, value.Facet, "Facet"),
                    });
            }
        }

        private void SeedTypeSpecs()
        {
            foreach (var value in MetaDataTypeConversionSeedData.TypeSpecs)
            {
                AddRow(
                    "TypeSpec",
                    new Dictionary<string, string?>
                    {
                        ["Length"] = value.Length.HasValue ? IntText(value.Length.Value) : null,
                        ["Precision"] = value.Precision.HasValue ? IntText(value.Precision.Value) : null,
                        ["Scale"] = value.Scale.HasValue ? IntText(value.Scale.Value) : null,
                        ["TimePrecision"] = value.TimePrecision.HasValue ? IntText(value.TimePrecision.Value) : null,
                        ["IsUnicode"] = value.IsUnicode.HasValue ? BoolText(value.IsUnicode.Value) : null,
                        ["IsFixedLength"] = value.IsFixedLength.HasValue ? BoolText(value.IsFixedLength.Value) : null,
                    },
                    new Dictionary<string, string>
                    {
                        ["DataTypeId"] = RequireDataTypeId(value.TypeSystem, value.DataType),
                    });
            }
        }

        private void SeedSettings()
        {
            foreach (var value in MetaDataTypeConversionSeedData.Settings)
            {
                var id = AddRow("Setting", new Dictionary<string, string?>
                {
                    ["Name"] = value.Name,
                    ["DefaultValue"] = value.DefaultValue,
                });
                settingIds[value.Name] = id;
            }
        }

        private void SeedConversionImplementations()
        {
            foreach (var value in MetaDataTypeConversionSeedData.ConversionImplementations)
            {
                var id = AddRow("ConversionImplementation", new Dictionary<string, string?>
                {
                    ["Key"] = value.Key,
                    ["Kind"] = value.Kind,
                    ["CSharpEntryPoint"] = value.CSharpEntryPoint,
                });
                implementationIds[value.Key] = id;
            }
        }

        private void SeedMappings()
        {
            foreach (var value in MetaDataTypeConversionSeedData.TypeMappings)
            {
                AddMapping(value);
            }
        }

        private void AddMapping(MetaDataTypeConversionSeedData.MappingSeed value)
        {
            var mappingId = AddRow(
                "TypeMapping",
                new Dictionary<string, string?>
                {
                    ["Name"] = value.Name,
                    ["Priority"] = IntText(value.Priority),
                    ["Lossiness"] = value.Lossiness,
                    ["IsImplicit"] = BoolText(value.IsImplicit),
                    ["Notes"] = value.Notes,
                },
                    new Dictionary<string, string>
                    {
                    ["SourceTypeSystemId"] = RequireId(typeSystemIds, value.SourceTypeSystem, "TypeSystem"),
                    ["TargetTypeSystemId"] = RequireId(typeSystemIds, value.TargetTypeSystem, "TypeSystem"),
                    ["SourceDataTypeId"] = RequireDataTypeId(value.SourceTypeSystem, value.SourceDataType),
                    ["TargetDataTypeId"] = RequireDataTypeId(value.TargetTypeSystem, value.TargetDataType),
                    ["ConversionImplementationId"] = RequireId(implementationIds, value.ConversionImplementation, "ConversionImplementation"),
                    ["SettingId"] = RequireId(settingIds, value.Setting, "Setting"),
                });

            foreach (var condition in value.Conditions ?? Array.Empty<MetaDataTypeConversionSeedData.MappingConditionSeed>())
            {
                AddRow(
                    "TypeMappingCondition",
                    new Dictionary<string, string?>
                    {
                        ["Operator"] = condition.Operator,
                        ["ValueInt"] = condition.ValueInt.HasValue ? IntText(condition.ValueInt.Value) : null,
                    },
                    new Dictionary<string, string>
                    {
                        ["TypeMappingId"] = mappingId,
                        ["FacetId"] = RequireId(facetIds, condition.Facet, "Facet"),
                    });
            }

            foreach (var transform in value.Transforms ?? Array.Empty<MetaDataTypeConversionSeedData.MappingTransformSeed>())
            {
                AddRow(
                    "TypeMappingFacetTransform",
                    new Dictionary<string, string?>
                    {
                        ["Mode"] = transform.Mode,
                        ["SetInt"] = transform.SetInt.HasValue ? IntText(transform.SetInt.Value) : null,
                        ["SetBool"] = transform.SetBool.HasValue ? BoolText(transform.SetBool.Value) : null,
                    },
                    new Dictionary<string, string>
                    {
                        ["TypeMappingId"] = mappingId,
                        ["FacetId"] = RequireId(facetIds, transform.Facet, "Facet"),
                    });
            }
        }

        private string AddRow(
            string entityName,
            IReadOnlyDictionary<string, string?> values,
            IReadOnlyDictionary<string, string>? relationships = null)
        {
            var id = NextId(entityName);
            var row = new GenericRecord
            {
                Id = id,
            };

            foreach (var value in values)
            {
                if (value.Value != null)
                {
                    row.Values[value.Key] = value.Value;
                }
            }

            foreach (var relationship in relationships ?? new Dictionary<string, string>())
            {
                row.RelationshipIds[relationship.Key] = relationship.Value;
            }

            workspace.Instance.GetOrCreateEntityRecords(entityName).Add(row);
            return id;
        }

        private string NextId(string entityName)
        {
            if (!idCounters.TryGetValue(entityName, out var current))
            {
                current = 0;
            }

            current++;
            idCounters[entityName] = current;
            return current.ToString(CultureInfo.InvariantCulture);
        }

        private string RequireDataTypeId(string typeSystemName, string dataTypeName)
        {
            var key = BuildDataTypeKey(typeSystemName, dataTypeName);
            return RequireId(dataTypeIds, key, "DataType");
        }

        private static string BuildDataTypeKey(string typeSystemName, string dataTypeName)
        {
            return typeSystemName + "|" + dataTypeName;
        }

        private static string RequireId(
            IReadOnlyDictionary<string, string> idLookup,
            string name,
            string entityName)
        {
            if (idLookup.TryGetValue(name, out var id))
            {
                return id;
            }

            throw new InvalidOperationException($"{entityName} row '{name}' is not seeded.");
        }

        private static string BoolText(bool value)
        {
            return value ? "true" : "false";
        }

        private static string IntText(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }
    }
}

