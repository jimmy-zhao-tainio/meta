namespace MetaSchema.Core;

internal static class MetaDataTypeConversionSeedData
{
    internal readonly record struct FacetSeed(string Name, string ValueKind);
    internal readonly record struct DataTypeSeed(string TypeSystem, string Name, string? Category);
    internal readonly record struct DataTypeFacetSeed(
        string TypeSystem,
        string DataType,
        string Facet,
        bool IsSupported,
        bool IsRequired,
        int? DefaultInt = null,
        bool? DefaultBool = null);
    internal readonly record struct TypeSpecSeed(
        string TypeSystem,
        string DataType,
        int? Length = null,
        int? Precision = null,
        int? Scale = null,
        int? TimePrecision = null,
        bool? IsUnicode = null,
        bool? IsFixedLength = null);
    internal readonly record struct SettingSeed(string Name, string DefaultValue);
    internal readonly record struct ConversionImplementationSeed(string Key, string Kind, string CSharpEntryPoint);
    internal readonly record struct MappingConditionSeed(string Facet, string Operator, int? ValueInt = null);
    internal readonly record struct MappingTransformSeed(string Facet, string Mode, int? SetInt = null, bool? SetBool = null);
    internal readonly record struct MappingSeed(
        string Name,
        string SourceTypeSystem,
        string SourceDataType,
        string TargetTypeSystem,
        string TargetDataType,
        string ConversionImplementation,
        string Setting,
        int Priority,
        string Lossiness,
        bool IsImplicit,
        string? Notes = null,
        IReadOnlyList<MappingConditionSeed>? Conditions = null,
        IReadOnlyList<MappingTransformSeed>? Transforms = null);

    public static readonly string[] TypeSystems =
    [
        "Meta",
        "SqlServer",
        "Synapse",
        "Snowflake",
        "SSIS",
        "CSharp",
    ];

    public static readonly FacetSeed[] Facets =
    [
        new("Length", "Int"),
        new("Precision", "Int"),
        new("Scale", "Int"),
        new("TimePrecision", "Int"),
        new("IsUnicode", "Bool"),
        new("IsFixedLength", "Bool"),
    ];

    public static readonly DataTypeSeed[] DataTypes =
    [
        new("Meta", "String", "Text"),
        new("Meta", "AnsiString", "Text"),
        new("Meta", "StringFixedLength", "Text"),
        new("Meta", "AnsiStringFixedLength", "Text"),
        new("Meta", "Boolean", "Logical"),
        new("Meta", "Byte", "Numeric"),
        new("Meta", "SByte", "Numeric"),
        new("Meta", "Int16", "Numeric"),
        new("Meta", "UInt16", "Numeric"),
        new("Meta", "Int32", "Numeric"),
        new("Meta", "UInt32", "Numeric"),
        new("Meta", "Int64", "Numeric"),
        new("Meta", "UInt64", "Numeric"),
        new("Meta", "Decimal", "Numeric"),
        new("Meta", "VarNumeric", "Numeric"),
        new("Meta", "Single", "Numeric"),
        new("Meta", "Double", "Numeric"),
        new("Meta", "Time", "Temporal"),
        new("Meta", "Date", "Temporal"),
        new("Meta", "DateTime", "Temporal"),
        new("Meta", "DateTime2", "Temporal"),
        new("Meta", "DateTimeOffset", "Temporal"),
        new("Meta", "Binary", "Binary"),
        new("Meta", "Guid", "Identifier"),
        new("Meta", "Xml", "Structured"),
        new("Meta", "Object", "Structured"),
        new("Meta", "geometry", "Spatial"),
        new("Meta", "geography", "Spatial"),
        new("Meta", "hierarchyid", "Spatial"),

        new("SqlServer", "char", "Text"),
        new("SqlServer", "varchar", "Text"),
        new("SqlServer", "nchar", "Text"),
        new("SqlServer", "nvarchar", "Text"),
        new("SqlServer", "smallmoney", "Numeric"),
        new("SqlServer", "money", "Numeric"),
        new("SqlServer", "bit", "Logical"),
        new("SqlServer", "tinyint", "Numeric"),
        new("SqlServer", "smallint", "Numeric"),
        new("SqlServer", "int", "Numeric"),
        new("SqlServer", "bigint", "Numeric"),
        new("SqlServer", "decimal", "Numeric"),
        new("SqlServer", "float", "Numeric"),
        new("SqlServer", "time", "Temporal"),
        new("SqlServer", "date", "Temporal"),
        new("SqlServer", "datetime", "Temporal"),
        new("SqlServer", "datetime2", "Temporal"),
        new("SqlServer", "datetimeoffset", "Temporal"),
        new("SqlServer", "geometry", "Spatial"),
        new("SqlServer", "geography", "Spatial"),
        new("SqlServer", "hierarchyid", "Spatial"),
        new("SqlServer", "varbinary", "Binary"),
        new("SqlServer", "uniqueidentifier", "Identifier"),
        new("SqlServer", "sql_variant", "Structured"),
        new("SqlServer", "xml", "Structured"),

        new("Synapse", "varchar", "Text"),
        new("Synapse", "varbinary", "Binary"),
        new("Synapse", "decimal", "Numeric"),
        new("Synapse", "float", "Numeric"),
        new("Synapse", "date", "Temporal"),
        new("Synapse", "datetime2", "Temporal"),
        new("Synapse", "datetimeoffset", "Temporal"),
        new("Synapse", "bit", "Logical"),
        new("Synapse", "bigint", "Numeric"),
        new("Synapse", "int", "Numeric"),

        new("Snowflake", "varchar", "Text"),
        new("Snowflake", "binary", "Binary"),
        new("Snowflake", "number", "Numeric"),
        new("Snowflake", "float", "Numeric"),
        new("Snowflake", "date", "Temporal"),
        new("Snowflake", "timestamp_ntz", "Temporal"),
        new("Snowflake", "timestamp_tz", "Temporal"),
        new("Snowflake", "boolean", "Logical"),
        new("Snowflake", "variant", "Structured"),

        new("SSIS", "DT_I8", "Numeric"),
        new("SSIS", "DT_BYTES", "Binary"),
        new("SSIS", "DT_BOOL", "Logical"),
        new("SSIS", "DT_STR", "Text"),
        new("SSIS", "DT_DBDATE", "Temporal"),
        new("SSIS", "DT_DBTIMESTAMP", "Temporal"),
        new("SSIS", "DT_DBTIMESTAMP2", "Temporal"),
        new("SSIS", "DT_DBTIMESTAMPOFFSET", "Temporal"),
        new("SSIS", "DT_NUMERIC", "Numeric"),
        new("SSIS", "DT_R8", "Numeric"),
        new("SSIS", "DT_IMAGE", "Binary"),
        new("SSIS", "DT_I4", "Numeric"),
        new("SSIS", "DT_CY", "Numeric"),
        new("SSIS", "DT_WSTR", "Text"),
        new("SSIS", "DT_NTEXT", "Structured"),
        new("SSIS", "DT_R4", "Numeric"),
        new("SSIS", "DT_TEXT", "Text"),
        new("SSIS", "DT_DBTIME2", "Temporal"),
        new("SSIS", "DT_UI1", "Numeric"),
        new("SSIS", "DT_GUID", "Identifier"),
        new("SSIS", "DT_I2", "Numeric"),

        new("CSharp", "string", "Text"),
        new("CSharp", "bool", "Logical"),
        new("CSharp", "int", "Numeric"),
        new("CSharp", "long", "Numeric"),
        new("CSharp", "decimal", "Numeric"),
        new("CSharp", "double", "Numeric"),
        new("CSharp", "DateOnly", "Temporal"),
        new("CSharp", "DateTime", "Temporal"),
        new("CSharp", "DateTimeOffset", "Temporal"),
        new("CSharp", "Guid", "Identifier"),
        new("CSharp", "byte[]", "Binary"),
        new("CSharp", "object", "Structured"),
    ];

    public static readonly DataTypeFacetSeed[] DataTypeFacets =
    [
        new("Meta", "String", "Length", true, false, 255),
        new("Meta", "String", "IsUnicode", true, true, null, true),
        new("Meta", "String", "IsFixedLength", true, true, null, false),
        new("Meta", "AnsiString", "Length", true, false, 255),
        new("Meta", "AnsiString", "IsUnicode", true, true, null, false),
        new("Meta", "AnsiString", "IsFixedLength", true, true, null, false),
        new("Meta", "StringFixedLength", "Length", true, true, 1),
        new("Meta", "StringFixedLength", "IsUnicode", true, true, null, true),
        new("Meta", "StringFixedLength", "IsFixedLength", true, true, null, true),
        new("Meta", "AnsiStringFixedLength", "Length", true, true, 1),
        new("Meta", "AnsiStringFixedLength", "IsUnicode", true, true, null, false),
        new("Meta", "AnsiStringFixedLength", "IsFixedLength", true, true, null, true),
        new("Meta", "Decimal", "Precision", true, false, 18),
        new("Meta", "Decimal", "Scale", true, false, 0),
        new("Meta", "VarNumeric", "Precision", true, false, 18),
        new("Meta", "VarNumeric", "Scale", true, false, 0),
        new("Meta", "Single", "Precision", true, false, 24),
        new("Meta", "Double", "Precision", true, false, 53),
        new("Meta", "Time", "TimePrecision", true, false, 7),
        new("Meta", "DateTime2", "TimePrecision", true, false, 7),
        new("Meta", "DateTimeOffset", "TimePrecision", true, false, 7),
        new("Meta", "Binary", "Length", true, false, 8000),
    ];

    public static readonly TypeSpecSeed[] TypeSpecs =
    [
        new("Meta", "String", Length: 255, IsUnicode: true, IsFixedLength: false),
        new("Meta", "AnsiString", Length: 255, IsUnicode: false, IsFixedLength: false),
        new("Meta", "StringFixedLength", Length: 64, IsUnicode: true, IsFixedLength: true),
        new("Meta", "AnsiStringFixedLength", Length: 64, IsUnicode: false, IsFixedLength: true),
        new("Meta", "Decimal", Precision: 18, Scale: 0),
        new("Meta", "VarNumeric", Precision: 38, Scale: 10),
        new("Meta", "Single", Precision: 24),
        new("Meta", "Double", Precision: 53),
        new("Meta", "Time", TimePrecision: 7),
        new("Meta", "DateTime2", TimePrecision: 7),
        new("Meta", "DateTimeOffset", TimePrecision: 7),
        new("Meta", "Binary", Length: 8000),
        new("SqlServer", "varchar", Length: 255, IsUnicode: false, IsFixedLength: false),
        new("SqlServer", "nvarchar", Length: 255, IsUnicode: true, IsFixedLength: false),
        new("SqlServer", "decimal", Precision: 18, Scale: 0),
        new("SqlServer", "time", TimePrecision: 7),
        new("SqlServer", "datetime2", TimePrecision: 7),
        new("SqlServer", "varbinary", Length: 8000),
        new("Synapse", "varchar", Length: 8000),
        new("Snowflake", "timestamp_tz", TimePrecision: 9),
    ];

    public static readonly SettingSeed[] Settings =
    [
        new("AlwaysOn", "Y"),
        new("ConvertGuidToString", "N"),
    ];

    public static readonly ConversionImplementationSeed[] ConversionImplementations =
    [
        new("Sql.Identity", "Sql", "MetaSchema.Runtime.Sql.Identity"),
        new("Sql.Cast", "Sql", "MetaSchema.Runtime.Sql.Cast"),
        new("Sql.Convert", "Sql", "MetaSchema.Runtime.Sql.Convert"),
        new("Ssis.DataConversion", "Ssis", "MetaSchema.Runtime.Ssis.DataConversion"),
        new("DotNet.Parse", "DotNet", "MetaSchema.Runtime.DotNet.Parse"),
    ];

    public static readonly MappingSeed[] TypeMappings =
    [
        new(
            "Meta.AnsiStringFixedLength->SqlServer.char",
            "Meta", "AnsiStringFixedLength", "SqlServer", "char",
            "Sql.Cast", "AlwaysOn", 100, "Exact", true,
            Transforms:
            [
                new("Length", "Copy"),
                new("IsUnicode", "SetConst", SetBool: false),
                new("IsFixedLength", "SetConst", SetBool: true),
            ]),
        new(
            "Meta.AnsiString->SqlServer.varchar",
            "Meta", "AnsiString", "SqlServer", "varchar",
            "Sql.Cast", "AlwaysOn", 100, "Exact", true,
            Transforms:
            [
                new("Length", "Copy"),
                new("IsUnicode", "SetConst", SetBool: false),
                new("IsFixedLength", "SetConst", SetBool: false),
            ]),
        new(
            "Meta.StringFixedLength->SqlServer.nchar",
            "Meta", "StringFixedLength", "SqlServer", "nchar",
            "Sql.Cast", "AlwaysOn", 100, "Exact", true,
            Transforms:
            [
                new("Length", "Copy"),
                new("IsUnicode", "SetConst", SetBool: true),
                new("IsFixedLength", "SetConst", SetBool: true),
            ]),
        new(
            "Meta.String->SqlServer.nvarchar",
            "Meta", "String", "SqlServer", "nvarchar",
            "Sql.Cast", "AlwaysOn", 100, "Exact", true,
            Transforms:
            [
                new("Length", "Copy"),
                new("IsUnicode", "SetConst", SetBool: true),
                new("IsFixedLength", "SetConst", SetBool: false),
            ]),
        new("Meta.Boolean->SqlServer.bit", "Meta", "Boolean", "SqlServer", "bit", "Sql.Identity", "AlwaysOn", 100, "Exact", true),
        new("Meta.Byte->SqlServer.tinyint", "Meta", "Byte", "SqlServer", "tinyint", "Sql.Identity", "AlwaysOn", 100, "Exact", true),
        new("Meta.Int16->SqlServer.smallint", "Meta", "Int16", "SqlServer", "smallint", "Sql.Identity", "AlwaysOn", 100, "Exact", true),
        new("Meta.UInt16->SqlServer.smallint", "Meta", "UInt16", "SqlServer", "smallint", "Sql.Convert", "AlwaysOn", 100, "Lossy", false),
        new("Meta.Int32->SqlServer.int", "Meta", "Int32", "SqlServer", "int", "Sql.Identity", "AlwaysOn", 100, "Exact", true),
        new("Meta.UInt32->SqlServer.int", "Meta", "UInt32", "SqlServer", "int", "Sql.Convert", "AlwaysOn", 100, "Lossy", false),
        new("Meta.Int64->SqlServer.bigint", "Meta", "Int64", "SqlServer", "bigint", "Sql.Identity", "AlwaysOn", 100, "Exact", true),
        new("Meta.UInt64->SqlServer.bigint", "Meta", "UInt64", "SqlServer", "bigint", "Sql.Convert", "AlwaysOn", 100, "Lossy", false),
        new(
            "Meta.Decimal->SqlServer.decimal",
            "Meta", "Decimal", "SqlServer", "decimal",
            "Sql.Cast", "AlwaysOn", 100, "Exact", true,
            Transforms:
            [
                new("Precision", "Copy"),
                new("Scale", "Copy"),
            ]),
        new(
            "Meta.VarNumeric->SqlServer.decimal",
            "Meta", "VarNumeric", "SqlServer", "decimal",
            "Sql.Cast", "AlwaysOn", 100, "Exact", true,
            Transforms:
            [
                new("Precision", "Copy"),
                new("Scale", "Copy"),
            ]),
        new(
            "Meta.Single->SqlServer.float",
            "Meta", "Single", "SqlServer", "float",
            "Sql.Cast", "AlwaysOn", 100, "Widening", true,
            Transforms: [new("Precision", "Copy")]),
        new(
            "Meta.Double->SqlServer.float",
            "Meta", "Double", "SqlServer", "float",
            "Sql.Cast", "AlwaysOn", 100, "Exact", true,
            Transforms: [new("Precision", "Copy")]),
        new(
            "Meta.Time->SqlServer.time",
            "Meta", "Time", "SqlServer", "time",
            "Sql.Cast", "AlwaysOn", 100, "Exact", true,
            Transforms: [new("TimePrecision", "Copy")]),
        new("Meta.Date->SqlServer.date", "Meta", "Date", "SqlServer", "date", "Sql.Identity", "AlwaysOn", 100, "Exact", true),
        new(
            "Meta.DateTime2->SqlServer.datetime2",
            "Meta", "DateTime2", "SqlServer", "datetime2",
            "Sql.Cast", "AlwaysOn", 100, "Exact", true,
            Transforms: [new("TimePrecision", "Copy")]),
        new(
            "Meta.DateTimeOffset->SqlServer.datetimeoffset",
            "Meta", "DateTimeOffset", "SqlServer", "datetimeoffset",
            "Sql.Cast", "AlwaysOn", 100, "Exact", true,
            Transforms: [new("TimePrecision", "Copy")]),
        new(
            "Meta.Binary->SqlServer.varbinary",
            "Meta", "Binary", "SqlServer", "varbinary",
            "Sql.Cast", "AlwaysOn", 100, "Exact", true,
            Transforms: [new("Length", "Copy")]),
        new("Meta.Guid->SqlServer.uniqueidentifier", "Meta", "Guid", "SqlServer", "uniqueidentifier", "Sql.Identity", "AlwaysOn", 100, "Exact", true),
        new("Meta.Object->SqlServer.sql_variant", "Meta", "Object", "SqlServer", "sql_variant", "Sql.Convert", "AlwaysOn", 100, "Lossy", false),
        new("Meta.Xml->SqlServer.xml", "Meta", "Xml", "SqlServer", "xml", "Sql.Identity", "AlwaysOn", 100, "Exact", true),
        new("Meta.geometry->SqlServer.geometry", "Meta", "geometry", "SqlServer", "geometry", "Sql.Identity", "AlwaysOn", 100, "Exact", true),
        new("Meta.geography->SqlServer.geography", "Meta", "geography", "SqlServer", "geography", "Sql.Identity", "AlwaysOn", 100, "Exact", true),
        new("Meta.hierarchyid->SqlServer.hierarchyid", "Meta", "hierarchyid", "SqlServer", "hierarchyid", "Sql.Identity", "AlwaysOn", 100, "Exact", true),
        new("Meta.DateTime->SqlServer.datetime", "Meta", "DateTime", "SqlServer", "datetime", "Sql.Cast", "AlwaysOn", 100, "Lossy", false),

        new(
            "Meta.AnsiString->SqlServer.varchar(MAX)",
            "Meta", "AnsiString", "SqlServer", "varchar",
            "Sql.Cast", "AlwaysOn", 300, "Widening", true,
            Conditions: [new("Length", ">=", 8001)],
            Transforms: [new("Length", "SetConst", SetInt: -1)]),
        new(
            "Meta.String->SqlServer.nvarchar(MAX)",
            "Meta", "String", "SqlServer", "nvarchar",
            "Sql.Cast", "AlwaysOn", 300, "Widening", true,
            Conditions: [new("Length", ">=", 4001)],
            Transforms: [new("Length", "SetConst", SetInt: -1)]),
        new(
            "Meta.Binary->SqlServer.varbinary(MAX)",
            "Meta", "Binary", "SqlServer", "varbinary",
            "Sql.Cast", "AlwaysOn", 300, "Widening", true,
            Conditions: [new("Length", ">=", 8001)],
            Transforms: [new("Length", "SetConst", SetInt: -1)]),
        new(
            "Meta.Decimal->SqlServer.decimal(DefaultPrecision)",
            "Meta", "Decimal", "SqlServer", "decimal",
            "Sql.Cast", "AlwaysOn", 250, "Exact", true,
            Conditions: [new("Precision", "Missing")],
            Transforms: [new("Precision", "SetConst", SetInt: 18)]),
        new(
            "Meta.Decimal->SqlServer.decimal(DefaultScale)",
            "Meta", "Decimal", "SqlServer", "decimal",
            "Sql.Cast", "AlwaysOn", 250, "Exact", true,
            Conditions: [new("Scale", "Missing")],
            Transforms: [new("Scale", "SetConst", SetInt: 0)]),
        new(
            "Meta.Time->SqlServer.time(DefaultPrecision)",
            "Meta", "Time", "SqlServer", "time",
            "Sql.Cast", "AlwaysOn", 250, "Exact", true,
            Conditions: [new("TimePrecision", "Missing")],
            Transforms: [new("TimePrecision", "SetConst", SetInt: 7)]),
        new(
            "Meta.DateTime2->SqlServer.datetime2(DefaultPrecision)",
            "Meta", "DateTime2", "SqlServer", "datetime2",
            "Sql.Cast", "AlwaysOn", 250, "Exact", true,
            Conditions: [new("TimePrecision", "Missing")],
            Transforms: [new("TimePrecision", "SetConst", SetInt: 7)]),
        new(
            "Meta.DateTimeOffset->SqlServer.datetimeoffset(DefaultPrecision)",
            "Meta", "DateTimeOffset", "SqlServer", "datetimeoffset",
            "Sql.Cast", "AlwaysOn", 250, "Exact", true,
            Conditions: [new("TimePrecision", "Missing")],
            Transforms: [new("TimePrecision", "SetConst", SetInt: 7)]),

        new(
            "Meta.String->Synapse.varchar",
            "Meta", "String", "Synapse", "varchar",
            "Sql.Cast", "AlwaysOn", 100, "Exact", true,
            Transforms:
            [
                new("Length", "Copy"),
                new("Length", "ClampMax", SetInt: 8000),
            ]),
        new(
            "Meta.Binary->Synapse.varbinary",
            "Meta", "Binary", "Synapse", "varbinary",
            "Sql.Cast", "AlwaysOn", 100, "Exact", true,
            Transforms:
            [
                new("Length", "Copy"),
                new("Length", "ClampMax", SetInt: 8000),
            ]),
        new(
            "Meta.Decimal->Synapse.decimal",
            "Meta", "Decimal", "Synapse", "decimal",
            "Sql.Cast", "AlwaysOn", 100, "Exact", true,
            Transforms:
            [
                new("Precision", "Copy"),
                new("Scale", "Copy"),
            ]),
        new("Meta.Date->Synapse.date", "Meta", "Date", "Synapse", "date", "Sql.Identity", "AlwaysOn", 100, "Exact", true),
        new(
            "Meta.DateTime2->Synapse.datetime2",
            "Meta", "DateTime2", "Synapse", "datetime2",
            "Sql.Cast", "AlwaysOn", 100, "Exact", true,
            Transforms: [new("TimePrecision", "Copy")]),
        new(
            "Meta.DateTimeOffset->Synapse.datetimeoffset",
            "Meta", "DateTimeOffset", "Synapse", "datetimeoffset",
            "Sql.Cast", "AlwaysOn", 100, "Exact", true,
            Transforms: [new("TimePrecision", "Copy")]),
        new("Meta.Boolean->Synapse.bit", "Meta", "Boolean", "Synapse", "bit", "Sql.Identity", "AlwaysOn", 100, "Exact", true),
        new("Meta.Int64->Synapse.bigint", "Meta", "Int64", "Synapse", "bigint", "Sql.Identity", "AlwaysOn", 100, "Exact", true),
        new("Meta.Int32->Synapse.int", "Meta", "Int32", "Synapse", "int", "Sql.Identity", "AlwaysOn", 100, "Exact", true),
        new(
            "Meta.Object->Synapse.varbinary",
            "Meta", "Object", "Synapse", "varbinary",
            "Sql.Convert", "AlwaysOn", 120, "Lossy", false,
            Transforms: [new("Length", "SetConst", SetInt: -1)]),
        new(
            "Meta.Guid->Synapse.varchar",
            "Meta", "Guid", "Synapse", "varchar",
            "Sql.Convert", "ConvertGuidToString", 130, "Widening", false,
            Transforms:
            [
                new("Length", "SetConst", SetInt: 36),
                new("IsUnicode", "SetConst", SetBool: false),
            ]),
        new(
            "Meta.geometry->Synapse.varbinary",
            "Meta", "geometry", "Synapse", "varbinary",
            "Sql.Convert", "AlwaysOn", 150, "Lossy", false,
            Transforms: [new("Length", "SetConst", SetInt: -1)]),
        new(
            "Meta.geography->Synapse.varbinary",
            "Meta", "geography", "Synapse", "varbinary",
            "Sql.Convert", "AlwaysOn", 150, "Lossy", false,
            Transforms: [new("Length", "SetConst", SetInt: -1)]),
        new(
            "Meta.hierarchyid->Synapse.varbinary",
            "Meta", "hierarchyid", "Synapse", "varbinary",
            "Sql.Convert", "AlwaysOn", 150, "Lossy", false,
            Transforms: [new("Length", "SetConst", SetInt: -1)]),

        new(
            "Meta.String->Snowflake.varchar",
            "Meta", "String", "Snowflake", "varchar",
            "Sql.Cast", "AlwaysOn", 100, "Exact", true,
            Transforms: [new("Length", "Copy")]),
        new(
            "Meta.Binary->Snowflake.binary",
            "Meta", "Binary", "Snowflake", "binary",
            "Sql.Cast", "AlwaysOn", 100, "Exact", true,
            Transforms: [new("Length", "Copy")]),
        new(
            "Meta.Decimal->Snowflake.number",
            "Meta", "Decimal", "Snowflake", "number",
            "Sql.Cast", "AlwaysOn", 100, "Exact", true,
            Transforms:
            [
                new("Precision", "Copy"),
                new("Scale", "Copy"),
            ]),
        new("Meta.Date->Snowflake.date", "Meta", "Date", "Snowflake", "date", "Sql.Identity", "AlwaysOn", 100, "Exact", true),
        new(
            "Meta.DateTime2->Snowflake.timestamp_ntz",
            "Meta", "DateTime2", "Snowflake", "timestamp_ntz",
            "Sql.Cast", "AlwaysOn", 100, "Exact", true,
            Transforms: [new("TimePrecision", "DefaultIfMissing", SetInt: 9)]),
        new(
            "Meta.DateTimeOffset->Snowflake.timestamp_tz",
            "Meta", "DateTimeOffset", "Snowflake", "timestamp_tz",
            "Sql.Cast", "AlwaysOn", 100, "Exact", true,
            Transforms: [new("TimePrecision", "DefaultIfMissing", SetInt: 9)]),
        new("Meta.Boolean->Snowflake.boolean", "Meta", "Boolean", "Snowflake", "boolean", "Sql.Identity", "AlwaysOn", 100, "Exact", true),
        new("Meta.Double->Snowflake.float", "Meta", "Double", "Snowflake", "float", "Sql.Identity", "AlwaysOn", 100, "Exact", true),
        new("Meta.Object->Snowflake.variant", "Meta", "Object", "Snowflake", "variant", "Sql.Convert", "AlwaysOn", 100, "Widening", true),
        new(
            "Meta.Guid->Snowflake.varchar",
            "Meta", "Guid", "Snowflake", "varchar",
            "Sql.Convert", "ConvertGuidToString", 130, "Widening", false,
            Transforms: [new("Length", "SetConst", SetInt: 36)]),
        new("Meta.geometry->Snowflake.binary", "Meta", "geometry", "Snowflake", "binary", "Sql.Convert", "AlwaysOn", 150, "Lossy", false),
        new("Meta.geography->Snowflake.binary", "Meta", "geography", "Snowflake", "binary", "Sql.Convert", "AlwaysOn", 150, "Lossy", false),
        new("Meta.hierarchyid->Snowflake.binary", "Meta", "hierarchyid", "Snowflake", "binary", "Sql.Convert", "AlwaysOn", 150, "Lossy", false),

        new("SqlServer.bigint->SSIS.DT_I8", "SqlServer", "bigint", "SSIS", "DT_I8", "Ssis.DataConversion", "AlwaysOn", 100, "Exact", true),
        new(
            "SqlServer.nvarchar->SSIS.DT_WSTR",
            "SqlServer", "nvarchar", "SSIS", "DT_WSTR",
            "Ssis.DataConversion", "AlwaysOn", 100, "Exact", true,
            Transforms: [new("Length", "Copy")]),
        new("SqlServer.uniqueidentifier->SSIS.DT_GUID", "SqlServer", "uniqueidentifier", "SSIS", "DT_GUID", "Ssis.DataConversion", "AlwaysOn", 100, "Exact", true),
        new(
            "SqlServer.time->SSIS.DT_DBTIME2",
            "SqlServer", "time", "SSIS", "DT_DBTIME2",
            "Ssis.DataConversion", "AlwaysOn", 100, "Exact", true,
            Transforms: [new("TimePrecision", "Copy")]),
        new(
            "SqlServer.decimal->SSIS.DT_NUMERIC",
            "SqlServer", "decimal", "SSIS", "DT_NUMERIC",
            "Ssis.DataConversion", "AlwaysOn", 100, "Exact", true,
            Transforms:
            [
                new("Precision", "Copy"),
                new("Scale", "Copy"),
            ]),
        new("SqlServer.xml->SSIS.DT_NTEXT", "SqlServer", "xml", "SSIS", "DT_NTEXT", "Ssis.DataConversion", "AlwaysOn", 100, "Lossy", false),
        new("SqlServer.int->SSIS.DT_I4", "SqlServer", "int", "SSIS", "DT_I4", "Ssis.DataConversion", "AlwaysOn", 100, "Exact", true),
        new("SqlServer.tinyint->SSIS.DT_UI1", "SqlServer", "tinyint", "SSIS", "DT_UI1", "Ssis.DataConversion", "AlwaysOn", 100, "Exact", true),
        new("SqlServer.smallint->SSIS.DT_I2", "SqlServer", "smallint", "SSIS", "DT_I2", "Ssis.DataConversion", "AlwaysOn", 100, "Exact", true),
        new("SqlServer.bit->SSIS.DT_BOOL", "SqlServer", "bit", "SSIS", "DT_BOOL", "Ssis.DataConversion", "AlwaysOn", 100, "Exact", true),
        new(
            "SqlServer.datetime2->SSIS.DT_DBTIMESTAMP2",
            "SqlServer", "datetime2", "SSIS", "DT_DBTIMESTAMP2",
            "Ssis.DataConversion", "AlwaysOn", 100, "Exact", true,
            Transforms: [new("TimePrecision", "Copy")]),
        new(
            "SqlServer.datetimeoffset->SSIS.DT_DBTIMESTAMPOFFSET",
            "SqlServer", "datetimeoffset", "SSIS", "DT_DBTIMESTAMPOFFSET",
            "Ssis.DataConversion", "AlwaysOn", 100, "Exact", true,
            Transforms: [new("TimePrecision", "Copy")]),
        new("SqlServer.bigint->Meta.Int64", "SqlServer", "bigint", "Meta", "Int64", "Sql.Identity", "AlwaysOn", 100, "Exact", true),
        new(
            "SqlServer.nvarchar->Meta.String",
            "SqlServer", "nvarchar", "Meta", "String",
            "Sql.Identity", "AlwaysOn", 100, "Exact", true,
            Transforms:
            [
                new("Length", "Copy"),
                new("IsUnicode", "SetConst", SetBool: true),
                new("IsFixedLength", "SetConst", SetBool: false),
            ]),
        new("SqlServer.uniqueidentifier->Meta.Guid", "SqlServer", "uniqueidentifier", "Meta", "Guid", "Sql.Identity", "AlwaysOn", 100, "Exact", true),
        new(
            "SqlServer.time->Meta.Time",
            "SqlServer", "time", "Meta", "Time",
            "Sql.Identity", "AlwaysOn", 100, "Exact", true,
            Transforms: [new("TimePrecision", "Copy")]),
        new(
            "SqlServer.decimal->Meta.Decimal",
            "SqlServer", "decimal", "Meta", "Decimal",
            "Sql.Identity", "AlwaysOn", 100, "Exact", true,
            Transforms:
            [
                new("Precision", "Copy"),
                new("Scale", "Copy"),
            ]),
        new("SqlServer.xml->Meta.Xml", "SqlServer", "xml", "Meta", "Xml", "Sql.Identity", "AlwaysOn", 100, "Exact", true),
        new("SqlServer.int->Meta.Int32", "SqlServer", "int", "Meta", "Int32", "Sql.Identity", "AlwaysOn", 100, "Exact", true),
        new("SqlServer.bit->Meta.Boolean", "SqlServer", "bit", "Meta", "Boolean", "Sql.Identity", "AlwaysOn", 100, "Exact", true),
        new(
            "SqlServer.varbinary->Meta.Binary",
            "SqlServer", "varbinary", "Meta", "Binary",
            "Sql.Identity", "AlwaysOn", 100, "Exact", true,
            Transforms: [new("Length", "Copy")]),
        new(
            "SqlServer.datetime2->Meta.DateTime2",
            "SqlServer", "datetime2", "Meta", "DateTime2",
            "Sql.Identity", "AlwaysOn", 100, "Exact", true,
            Transforms: [new("TimePrecision", "Copy")]),
        new(
            "SqlServer.datetimeoffset->Meta.DateTimeOffset",
            "SqlServer", "datetimeoffset", "Meta", "DateTimeOffset",
            "Sql.Identity", "AlwaysOn", 100, "Exact", true,
            Transforms: [new("TimePrecision", "Copy")]),
        new(
            "SqlServer.float->Meta.Double",
            "SqlServer", "float", "Meta", "Double",
            "Sql.Identity", "AlwaysOn", 100, "Exact", true,
            Transforms: [new("Precision", "Copy")]),
        new(
            "SqlServer.char->Meta.AnsiStringFixedLength",
            "SqlServer", "char", "Meta", "AnsiStringFixedLength",
            "Sql.Identity", "AlwaysOn", 100, "Exact", true,
            Transforms:
            [
                new("Length", "Copy"),
                new("IsUnicode", "SetConst", SetBool: false),
                new("IsFixedLength", "SetConst", SetBool: true),
            ]),
        new(
            "SqlServer.varchar->Meta.AnsiString",
            "SqlServer", "varchar", "Meta", "AnsiString",
            "Sql.Identity", "AlwaysOn", 100, "Exact", true,
            Transforms:
            [
                new("Length", "Copy"),
                new("IsUnicode", "SetConst", SetBool: false),
                new("IsFixedLength", "SetConst", SetBool: false),
            ]),
        new(
            "SqlServer.nchar->Meta.StringFixedLength",
            "SqlServer", "nchar", "Meta", "StringFixedLength",
            "Sql.Identity", "AlwaysOn", 100, "Exact", true,
            Transforms:
            [
                new("Length", "Copy"),
                new("IsUnicode", "SetConst", SetBool: true),
                new("IsFixedLength", "SetConst", SetBool: true),
            ]),
        new("SqlServer.sql_variant->Meta.Object", "SqlServer", "sql_variant", "Meta", "Object", "Sql.Identity", "AlwaysOn", 100, "Widening", true),
        new("SqlServer.geometry->Meta.geometry", "SqlServer", "geometry", "Meta", "geometry", "Sql.Identity", "AlwaysOn", 100, "Exact", true),
        new("SqlServer.geography->Meta.geography", "SqlServer", "geography", "Meta", "geography", "Sql.Identity", "AlwaysOn", 100, "Exact", true),
        new("SqlServer.hierarchyid->Meta.hierarchyid", "SqlServer", "hierarchyid", "Meta", "hierarchyid", "Sql.Identity", "AlwaysOn", 100, "Exact", true),
        new("SqlServer.date->Meta.Date", "SqlServer", "date", "Meta", "Date", "Sql.Identity", "AlwaysOn", 100, "Exact", true),
    ];
}
