namespace MetaTypeConversion.Core;

internal static class MetaTypeConversionSeedData
{
    internal readonly record struct ConversionImplementationSeed(string Id, string Name, string? Description = null);
    internal readonly record struct TypeMappingSeed(string Id, string SourceTypeId, string TargetTypeId, string ConversionImplementationId, string? Notes = null);

    public static readonly ConversionImplementationSeed[] ConversionImplementations =
    [
        new("metatypeconversion:implementation:direct", "Direct", "Direct sanctioned type mapping."),
        new("metatypeconversion:implementation:structural", "Structural", "Structural sanctioned type mapping for non-scalar types.")
    ];

    public static readonly TypeMappingSeed[] TypeMappings =
    [
        new("metatypeconversion:mapping:sqlserver:char", "sqlserver:type:char", "meta:type:AnsiStringFixedLength", "metatypeconversion:implementation:direct"),
        new("metatypeconversion:mapping:sqlserver:varchar", "sqlserver:type:varchar", "meta:type:AnsiString", "metatypeconversion:implementation:direct"),
        new("metatypeconversion:mapping:sqlserver:nchar", "sqlserver:type:nchar", "meta:type:StringFixedLength", "metatypeconversion:implementation:direct"),
        new("metatypeconversion:mapping:sqlserver:nvarchar", "sqlserver:type:nvarchar", "meta:type:String", "metatypeconversion:implementation:direct"),
        new("metatypeconversion:mapping:sqlserver:smallmoney", "sqlserver:type:smallmoney", "meta:type:Decimal", "metatypeconversion:implementation:direct"),
        new("metatypeconversion:mapping:sqlserver:money", "sqlserver:type:money", "meta:type:Decimal", "metatypeconversion:implementation:direct"),
        new("metatypeconversion:mapping:sqlserver:bit", "sqlserver:type:bit", "meta:type:Boolean", "metatypeconversion:implementation:direct"),
        new("metatypeconversion:mapping:sqlserver:tinyint", "sqlserver:type:tinyint", "meta:type:Byte", "metatypeconversion:implementation:direct"),
        new("metatypeconversion:mapping:sqlserver:smallint", "sqlserver:type:smallint", "meta:type:Int16", "metatypeconversion:implementation:direct"),
        new("metatypeconversion:mapping:sqlserver:int", "sqlserver:type:int", "meta:type:Int32", "metatypeconversion:implementation:direct"),
        new("metatypeconversion:mapping:sqlserver:bigint", "sqlserver:type:bigint", "meta:type:Int64", "metatypeconversion:implementation:direct"),
        new("metatypeconversion:mapping:sqlserver:decimal", "sqlserver:type:decimal", "meta:type:Decimal", "metatypeconversion:implementation:direct"),
        new("metatypeconversion:mapping:sqlserver:float", "sqlserver:type:float", "meta:type:Double", "metatypeconversion:implementation:direct"),
        new("metatypeconversion:mapping:sqlserver:time", "sqlserver:type:time", "meta:type:Time", "metatypeconversion:implementation:direct"),
        new("metatypeconversion:mapping:sqlserver:date", "sqlserver:type:date", "meta:type:Date", "metatypeconversion:implementation:direct"),
        new("metatypeconversion:mapping:sqlserver:datetime", "sqlserver:type:datetime", "meta:type:DateTime", "metatypeconversion:implementation:direct"),
        new("metatypeconversion:mapping:sqlserver:datetime2", "sqlserver:type:datetime2", "meta:type:DateTime2", "metatypeconversion:implementation:direct"),
        new("metatypeconversion:mapping:sqlserver:datetimeoffset", "sqlserver:type:datetimeoffset", "meta:type:DateTimeOffset", "metatypeconversion:implementation:direct"),
        new("metatypeconversion:mapping:sqlserver:varbinary", "sqlserver:type:varbinary", "meta:type:Binary", "metatypeconversion:implementation:direct"),
        new("metatypeconversion:mapping:sqlserver:uniqueidentifier", "sqlserver:type:uniqueidentifier", "meta:type:Guid", "metatypeconversion:implementation:direct"),
        new("metatypeconversion:mapping:sqlserver:xml", "sqlserver:type:xml", "meta:type:Xml", "metatypeconversion:implementation:structural"),
        new("metatypeconversion:mapping:sqlserver:sql_variant", "sqlserver:type:sql_variant", "meta:type:Object", "metatypeconversion:implementation:structural"),
        new("metatypeconversion:mapping:sqlserver:geometry", "sqlserver:type:geometry", "meta:type:geometry", "metatypeconversion:implementation:structural"),
        new("metatypeconversion:mapping:sqlserver:geography", "sqlserver:type:geography", "meta:type:geography", "metatypeconversion:implementation:structural"),
        new("metatypeconversion:mapping:sqlserver:hierarchyid", "sqlserver:type:hierarchyid", "meta:type:hierarchyid", "metatypeconversion:implementation:structural")
    ];
}
