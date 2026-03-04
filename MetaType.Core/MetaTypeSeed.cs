using Meta.Core.Domain;

namespace MetaType.Core;

internal static class MetaTypeSeed
{
    public static void Populate(GenericInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);

        foreach (var typeSystemName in MetaTypeSeedData.TypeSystems)
        {
            instance.GetOrCreateEntityRecords("TypeSystem").Add(new GenericRecord
            {
                Id = BuildTypeSystemId(typeSystemName),
                Values = { ["Name"] = typeSystemName }
            });
        }

        foreach (var type in MetaTypeSeedData.Types)
        {
            var record = new GenericRecord
            {
                Id = BuildTypeId(type.TypeSystem, type.Name),
                RelationshipIds =
                {
                    ["TypeSystemId"] = BuildTypeSystemId(type.TypeSystem)
                }
            };
            record.Values["Name"] = type.Name;
            if (!string.IsNullOrWhiteSpace(type.Category))
            {
                record.Values["Category"] = type.Category;
            }

            if (string.Equals(type.TypeSystem, "Meta", StringComparison.Ordinal))
            {
                record.Values["IsCanonical"] = "true";
            }

            instance.GetOrCreateEntityRecords("Type").Add(record);
        }

        foreach (var spec in MetaTypeSeedData.TypeSpecs)
        {
            var record = new GenericRecord
            {
                Id = BuildTypeSpecId(spec),
                RelationshipIds =
                {
                    ["TypeId"] = BuildTypeId(spec.TypeSystem, spec.Type)
                }
            };

            var name = BuildTypeSpecName(spec);
            if (!string.IsNullOrWhiteSpace(name))
            {
                record.Values["Name"] = name;
            }

            if (spec.Length.HasValue)
            {
                record.Values["Length"] = spec.Length.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            if (spec.Precision.HasValue)
            {
                record.Values["Precision"] = spec.Precision.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            if (spec.Scale.HasValue)
            {
                record.Values["Scale"] = spec.Scale.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            if (spec.TimePrecision.HasValue)
            {
                record.Values["TimePrecision"] = spec.TimePrecision.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            if (spec.IsUnicode.HasValue)
            {
                record.Values["IsUnicode"] = spec.IsUnicode.Value ? "true" : "false";
            }

            if (spec.IsFixedLength.HasValue)
            {
                record.Values["IsFixedLength"] = spec.IsFixedLength.Value ? "true" : "false";
            }

            instance.GetOrCreateEntityRecords("TypeSpec").Add(record);
        }
    }

    internal static string BuildTypeSystemId(string typeSystemName)
    {
        return NormalizeKey(typeSystemName) + ":type-system";
    }

    internal static string BuildTypeId(string typeSystemName, string typeName)
    {
        return NormalizeKey(typeSystemName) + ":type:" + typeName;
    }

    private static string BuildTypeSpecId(MetaTypeSeedData.TypeSpecSeed spec)
    {
        var suffix = BuildTypeSpecKey(spec);
        return BuildTypeId(spec.TypeSystem, spec.Type) + ":spec:" + suffix;
    }

    private static string BuildTypeSpecName(MetaTypeSeedData.TypeSpecSeed spec)
    {
        var facets = new List<string>();
        if (spec.Length.HasValue)
        {
            facets.Add("Length=" + spec.Length.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (spec.Precision.HasValue)
        {
            facets.Add("Precision=" + spec.Precision.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (spec.Scale.HasValue)
        {
            facets.Add("Scale=" + spec.Scale.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (spec.TimePrecision.HasValue)
        {
            facets.Add("TimePrecision=" + spec.TimePrecision.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (spec.IsUnicode.HasValue)
        {
            facets.Add("IsUnicode=" + (spec.IsUnicode.Value ? "true" : "false"));
        }

        if (spec.IsFixedLength.HasValue)
        {
            facets.Add("IsFixedLength=" + (spec.IsFixedLength.Value ? "true" : "false"));
        }

        return facets.Count == 0 ? spec.Type : spec.Type + " (" + string.Join(", ", facets) + ")";
    }

    private static string BuildTypeSpecKey(MetaTypeSeedData.TypeSpecSeed spec)
    {
        var parts = new List<string>();
        if (spec.Length.HasValue)
        {
            parts.Add("length=" + spec.Length.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (spec.Precision.HasValue)
        {
            parts.Add("precision=" + spec.Precision.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (spec.Scale.HasValue)
        {
            parts.Add("scale=" + spec.Scale.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (spec.TimePrecision.HasValue)
        {
            parts.Add("timeprecision=" + spec.TimePrecision.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (spec.IsUnicode.HasValue)
        {
            parts.Add("unicode=" + (spec.IsUnicode.Value ? "true" : "false"));
        }

        if (spec.IsFixedLength.HasValue)
        {
            parts.Add("fixed=" + (spec.IsFixedLength.Value ? "true" : "false"));
        }

        return parts.Count == 0 ? "default" : string.Join(";", parts);
    }

    private static string NormalizeKey(string value)
    {
        return value.Trim().ToLowerInvariant();
    }
}
