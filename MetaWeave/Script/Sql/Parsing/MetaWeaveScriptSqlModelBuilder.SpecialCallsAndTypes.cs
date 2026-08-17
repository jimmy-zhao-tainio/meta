using MetaWeave;

namespace MetaWeaveScript.Sql.Parsing;

internal sealed partial class MetaWeaveScriptSqlModelBuilder
{
    public BuiltNode CreateIntDataTypeReference()
    {
        var typeIdentifier = CreateIdentifier("int", "NotQuoted");
        var schemaObjectName = CreateSchemaObjectName([typeIdentifier]);

        var dataTypeReference = new DataTypeReference
        {
            Id = NextId(nameof(DataTypeReference))
        };
        model.DataTypeReferenceList.Add(dataTypeReference);
        model.DataTypeReferenceNameLinkList.Add(new DataTypeReferenceNameLink
        {
            Id = NextId(nameof(DataTypeReferenceNameLink)),
            DataTypeReference = dataTypeReference,
            SchemaObjectName = schemaObjectName.GetRef<SchemaObjectName>(nameof(SchemaObjectName))
        });

        var parameterizedDataTypeReference = new ParameterizedDataTypeReference
        {
            Id = NextId(nameof(ParameterizedDataTypeReference)),
            DataTypeReference = dataTypeReference
        };
        model.ParameterizedDataTypeReferenceList.Add(parameterizedDataTypeReference);

        var sqlDataTypeReference = new SqlDataTypeReference
        {
            Id = NextId(nameof(SqlDataTypeReference)),
            ParameterizedDataTypeReference = parameterizedDataTypeReference,
            SqlDataTypeOption = "Int"
        };
        model.SqlDataTypeReferenceList.Add(sqlDataTypeReference);

        return BuiltNode.Create(
            (nameof(DataTypeReference), dataTypeReference.Id),
            (nameof(ParameterizedDataTypeReference), parameterizedDataTypeReference.Id),
            (nameof(SqlDataTypeReference), sqlDataTypeReference.Id));
    }

    public BuiltNode CreateTryConvertCall(BuiltNode dataTypeReference, BuiltNode parameter)
    {
        var scalar = new ScalarExpression
        {
            Id = NextId(nameof(ScalarExpression))
        };
        model.ScalarExpressionList.Add(scalar);

        var primary = new PrimaryExpression
        {
            Id = NextId(nameof(PrimaryExpression)),
            ScalarExpression = scalar
        };
        model.PrimaryExpressionList.Add(primary);

        var tryConvertCall = new TryConvertCall
        {
            Id = NextId(nameof(TryConvertCall)),
            PrimaryExpression = primary
        };
        model.TryConvertCallList.Add(tryConvertCall);
        model.TryConvertCallDataTypeLinkList.Add(new TryConvertCallDataTypeLink
        {
            Id = NextId(nameof(TryConvertCallDataTypeLink)),
            TryConvertCall = tryConvertCall,
            DataTypeReference = dataTypeReference.GetRef<DataTypeReference>(nameof(DataTypeReference))
        });
        model.TryConvertCallParameterLinkList.Add(new TryConvertCallParameterLink
        {
            Id = NextId(nameof(TryConvertCallParameterLink)),
            TryConvertCall = tryConvertCall,
            ScalarExpression = parameter.GetRef<ScalarExpression>(nameof(ScalarExpression))
        });

        return BuiltNode.Create(
            (nameof(ScalarExpression), scalar.Id),
            (nameof(PrimaryExpression), primary.Id),
            (nameof(TryConvertCall), tryConvertCall.Id));
    }
}
