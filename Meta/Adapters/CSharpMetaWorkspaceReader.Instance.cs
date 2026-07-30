using Meta.Core.Domain;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Meta.Adapters;

public sealed partial class CSharpMetaWorkspaceReader
{
    private static GenericInstance ReadInstance(
        CSharpMetaSourceMap source,
        CSharpModelMap modelMap)
    {
        if (source.FactorySyntax.Body == null)
        {
            throw new InvalidDataException(
                $"C# Meta factory '{source.FactoryMethod.Name}' must have a method body.");
        }

        var instance = new GenericInstance
        {
            ModelName = modelMap.Model.Name,
        };
        var returnedLists = ResolveReturnedEntityLists(
            source,
            modelMap);
        var recordsByLocal = new Dictionary<ILocalSymbol, CSharpEntityRecords>(
            SymbolEqualityComparer.Default);
        foreach (var entityMap in modelMap.EntitiesByName.Values
                     .OrderBy(
                         item => item.Entity.Name,
                         StringComparer.OrdinalIgnoreCase)
                     .ThenBy(
                         item => item.Entity.Name,
                         StringComparer.Ordinal))
        {
            var local = returnedLists[entityMap.Type];
            var variable = RequireSingleDeclaration<VariableDeclaratorSyntax>(
                local,
                $"C# Meta entity list '{local.Name}'");
            if (!SymbolEqualityComparer.Default.Equals(
                    local.ContainingSymbol,
                    source.FactoryMethod) ||
                !source.FactorySyntax.Body.Span.Contains(variable.Span))
            {
                throw new InvalidDataException(
                    $"C# Meta entity list '{local.Name}' must be declared directly by the BuiltIn factory.");
            }

            if (variable.Initializer == null ||
                source.FactorySemanticModel.GetOperation(
                    variable.Initializer.Value) is not IObjectCreationOperation listCreation)
            {
                throw new InvalidDataException(
                    $"C# Meta entity list '{local.Name}' must be initialized with a List<{entityMap.Entity.Name}> object creation.");
            }

            var records = instance.GetOrCreateEntityRecords(
                entityMap.Entity.Name);
            foreach (var initializer in listCreation.Initializer?.Initializers ?? [])
            {
                if (initializer is not IInvocationOperation
                    {
                        Arguments.Length: 1,
                    } addInvocation ||
                    Unwrap(addInvocation.Arguments[0].Value) is not
                        IObjectCreationOperation recordCreation ||
                    !SymbolEqualityComparer.Default.Equals(
                        recordCreation.Type,
                        entityMap.Type))
                {
                    throw new InvalidDataException(
                        $"C# Meta entity list '{local.Name}' contains an unsupported initializer.");
                }

                records.Add(ReadRecord(entityMap, recordCreation));
            }

            recordsByLocal.Add(
                local,
                new CSharpEntityRecords(entityMap, records));
        }

        ReadPostConstructionAssignments(
            source,
            recordsByLocal);
        return instance;
    }

    private static IReadOnlyDictionary<INamedTypeSymbol, ILocalSymbol>
        ResolveReturnedEntityLists(
            CSharpMetaSourceMap source,
            CSharpModelMap modelMap)
    {
        var returnStatements = source.FactorySyntax.Body!.Statements
            .OfType<ReturnStatementSyntax>()
            .ToArray();
        if (returnStatements.Length != 1 ||
            returnStatements[0].Expression == null ||
            source.FactorySyntax.Body.Statements[^1].Span !=
            returnStatements[0].Span)
        {
            throw new InvalidDataException(
                $"C# Meta factory '{source.FactoryMethod.Name}' must contain one direct return statement.");
        }

        var returnOperation = source.FactorySemanticModel.GetOperation(
            returnStatements[0].Expression!);
        if (returnOperation == null ||
            Unwrap(returnOperation) is not
            IObjectCreationOperation instanceCreation ||
            !SymbolEqualityComparer.Default.Equals(
                instanceCreation.Type,
                source.InstanceType) ||
            instanceCreation.Constructor == null)
        {
            throw new InvalidDataException(
                $"C# Meta factory '{source.FactoryMethod.Name}' must return one constructed '{source.InstanceType.Name}' instance.");
        }

        var parameterCollections = ReadInstanceConstructor(
            source,
            instanceCreation.Constructor,
            modelMap);
        if (instanceCreation.Arguments.Length !=
            parameterCollections.Count)
        {
            throw new InvalidDataException(
                $"C# Meta factory return for '{source.InstanceType.Name}' does not supply every entity collection.");
        }

        var result = new Dictionary<INamedTypeSymbol, ILocalSymbol>(
            SymbolEqualityComparer.Default);
        foreach (var argument in instanceCreation.Arguments)
        {
            if (argument.Parameter == null ||
                !parameterCollections.TryGetValue(
                    argument.Parameter,
                    out var collectionProperty) ||
                TryGetReadOnlyListElementType(
                    collectionProperty.Type) is not { } entityType ||
                !modelMap.EntitiesByType.ContainsKey(entityType))
            {
                throw new InvalidDataException(
                    $"C# Meta factory return contains an unknown collection argument.");
            }

            var value = Unwrap(argument.Value);
            if (value is not IObjectCreationOperation
                {
                    Arguments.Length: 1,
                } readOnlyCreation ||
                TryGetReadOnlyCollectionElementType(
                    readOnlyCreation.Type) is not { } wrappedType ||
                !SymbolEqualityComparer.Default.Equals(
                    wrappedType,
                    entityType) ||
                Unwrap(readOnlyCreation.Arguments[0].Value) is not
                    ILocalReferenceOperation localReference ||
                TryGetListElementType(localReference.Local.Type) is not
                    { } localElementType ||
                !SymbolEqualityComparer.Default.Equals(
                    localElementType,
                    entityType) ||
                !result.TryAdd(entityType, localReference.Local))
            {
                throw new InvalidDataException(
                    $"C# Meta collection '{collectionProperty.Name}' must be returned from one local List<{entityType.Name}> through ReadOnlyCollection<{entityType.Name}>.");
            }
        }

        if (result.Count != modelMap.EntitiesByType.Count)
        {
            throw new InvalidDataException(
                $"C# Meta factory return does not expose every modeled entity list.");
        }

        return result;
    }

    private static IReadOnlyDictionary<IParameterSymbol, IPropertySymbol>
        ReadInstanceConstructor(
            CSharpMetaSourceMap source,
            IMethodSymbol constructor,
            CSharpModelMap modelMap)
    {
        var syntax = RequireSingleDeclaration<ConstructorDeclarationSyntax>(
            constructor,
            $"C# Meta instance constructor '{source.InstanceType.Name}'");
        if (syntax.Body == null ||
            constructor.Parameters.Length !=
            modelMap.EntitiesByType.Count ||
            syntax.Body.Statements.Count !=
            modelMap.EntitiesByType.Count)
        {
            throw new InvalidDataException(
                $"C# Meta instance constructor '{source.InstanceType.Name}' must assign every entity collection exactly once.");
        }

        var semanticModel = source.Compilation.GetSemanticModel(
            syntax.SyntaxTree);
        var result = new Dictionary<IParameterSymbol, IPropertySymbol>(
            SymbolEqualityComparer.Default);
        var assignedProperties = new HashSet<IPropertySymbol>(
            SymbolEqualityComparer.Default);
        foreach (var statement in syntax.Body.Statements)
        {
            if (statement is not ExpressionStatementSyntax expression ||
                semanticModel.GetOperation(expression.Expression) is not
                    ISimpleAssignmentOperation assignment ||
                assignment.Target is not
                    IPropertyReferenceOperation propertyReference ||
                Unwrap(assignment.Value) is not
                    IParameterReferenceOperation parameterReference ||
                !SymbolEqualityComparer.Default.Equals(
                    propertyReference.Property.ContainingType,
                    source.InstanceType) ||
                !modelMap.EntitiesByName.Values.Any(entity =>
                    SymbolEqualityComparer.Default.Equals(
                        entity.CollectionProperty,
                        propertyReference.Property)) ||
                !SymbolEqualityComparer.Default.Equals(
                    parameterReference.Parameter.Type,
                    propertyReference.Property.Type) ||
                !assignedProperties.Add(
                    propertyReference.Property) ||
                !result.TryAdd(
                    parameterReference.Parameter,
                    propertyReference.Property))
            {
                throw new InvalidDataException(
                    $"C# Meta instance constructor '{source.InstanceType.Name}' contains an unsupported statement.");
            }
        }

        if (result.Count != modelMap.EntitiesByType.Count)
        {
            throw new InvalidDataException(
                $"C# Meta instance constructor '{source.InstanceType.Name}' must assign every entity collection exactly once.");
        }

        return result;
    }

    private static GenericRecord ReadRecord(
        CSharpEntityMap entityMap,
        IObjectCreationOperation creation)
    {
        var record = new GenericRecord
        {
            Id = entityMap.IdDefault ?? string.Empty,
        };
        foreach (var scalarDefault in entityMap.ScalarDefaults.Where(
                     item => item.Value != null))
        {
            record.Values[scalarDefault.Key] =
                scalarDefault.Value!;
        }

        foreach (var initializer in creation.Initializer?.Initializers ?? [])
        {
            if (initializer is not ISimpleAssignmentOperation assignment ||
                assignment.Target is not IPropertyReferenceOperation target ||
                !SymbolEqualityComparer.Default.Equals(
                    target.Property.ContainingType,
                    entityMap.Type))
            {
                throw new InvalidDataException(
                    $"C# Meta record initializer for '{entityMap.Entity.Name}' contains an unsupported expression.");
            }

            if (SymbolEqualityComparer.Default.Equals(
                    target.Property,
                    entityMap.IdProperty))
            {
                record.Id = RequireConstantString(
                    assignment.Value,
                    $"{entityMap.Entity.Name}.Id");
                continue;
            }

            var scalar = entityMap.ScalarProperties
                .FirstOrDefault(item =>
                    SymbolEqualityComparer.Default.Equals(
                        item.Value,
                        target.Property));
            if (!string.IsNullOrEmpty(scalar.Key))
            {
                if (TryReadConstantString(
                        assignment.Value,
                        out var scalarValue))
                {
                    if (scalarValue == null)
                    {
                        record.Values.Remove(scalar.Key);
                    }
                    else
                    {
                        record.Values[scalar.Key] = scalarValue;
                    }

                    continue;
                }

                throw new InvalidDataException(
                    $"C# Meta scalar '{entityMap.Entity.Name}.{scalar.Key}' must be assigned a compile-time string constant or null.");
            }

            var relationship = entityMap.RelationshipProperties
                .FirstOrDefault(item =>
                    SymbolEqualityComparer.Default.Equals(
                        item.Value,
                        target.Property));
            if (!string.IsNullOrEmpty(relationship.Key) &&
                TryReadConstantString(
                    assignment.Value,
                    out var relationshipValue) &&
                relationshipValue == null)
            {
                continue;
            }

            throw new InvalidDataException(
                $"C# Meta relationship '{entityMap.Entity.Name}.{target.Property.Name}' must be assigned after record construction.");
        }

        return record;
    }

    private static void ReadPostConstructionAssignments(
        CSharpMetaSourceMap source,
        IReadOnlyDictionary<ILocalSymbol, CSharpEntityRecords> recordsByLocal)
    {
        var dictionaries = new Dictionary<ILocalSymbol, INamedTypeSymbol>(
            SymbolEqualityComparer.Default);
        var populatedDictionaries = new HashSet<ILocalSymbol>(
            SymbolEqualityComparer.Default);
        foreach (var statement in source.FactorySyntax.Body!.Statements)
        {
            switch (statement)
            {
                case LocalDeclarationStatementSyntax localDeclaration:
                    ReadFactoryLocalDeclaration(
                        source,
                        localDeclaration,
                        recordsByLocal,
                        dictionaries);
                    break;
                case ForEachStatementSyntax forEach:
                    var populatedDictionary = RequireDictionaryIndexLoop(
                        source,
                        forEach,
                        recordsByLocal,
                        dictionaries);
                    if (!populatedDictionaries.Add(populatedDictionary))
                    {
                        throw new InvalidDataException(
                            $"C# Meta identity index '{populatedDictionary.Name}' is populated more than once.");
                    }

                    break;
                case ExpressionStatementSyntax expression:
                    ReadRecordAssignment(
                        source,
                        expression,
                        recordsByLocal,
                        dictionaries,
                        populatedDictionaries);
                    break;
                case ReturnStatementSyntax:
                    break;
                default:
                    throw new InvalidDataException(
                        $"C# Meta factory '{source.FactoryMethod.Name}' contains unsupported statement '{statement.Kind()}'.");
            }
        }
    }

    private static void ReadFactoryLocalDeclaration(
        CSharpMetaSourceMap source,
        LocalDeclarationStatementSyntax declaration,
        IReadOnlyDictionary<ILocalSymbol, CSharpEntityRecords> recordsByLocal,
        IDictionary<ILocalSymbol, INamedTypeSymbol> dictionaries)
    {
        if (declaration.Declaration.Variables.Count != 1)
        {
            throw new InvalidDataException(
                $"C# Meta factory '{source.FactoryMethod.Name}' local declarations must contain one variable.");
        }

        var variable = declaration.Declaration.Variables[0];
        var local = source.FactorySemanticModel.GetDeclaredSymbol(
            variable) as ILocalSymbol;
        if (local == null)
        {
            throw new InvalidDataException(
                $"C# Meta factory '{source.FactoryMethod.Name}' contains an unresolved local declaration.");
        }

        if (recordsByLocal.ContainsKey(local))
        {
            return;
        }

        if (TryGetDictionaryValueEntityType(local.Type) is not
                { } entityType ||
            !recordsByLocal.Values.Any(records =>
                SymbolEqualityComparer.Default.Equals(
                    records.EntityMap.Type,
                    entityType)) ||
            variable.Initializer == null ||
            source.FactorySemanticModel.GetOperation(
                variable.Initializer.Value) is not
                IObjectCreationOperation
                {
                    Arguments.Length: 1,
                } creation ||
            Unwrap(creation.Arguments[0].Value) is not
                IPropertyReferenceOperation comparer ||
            !comparer.Property.IsStatic ||
            !string.Equals(
                comparer.Property.Name,
                "OrdinalIgnoreCase",
                StringComparison.Ordinal) ||
            !string.Equals(
                comparer.Property.ContainingType.Name,
                nameof(StringComparer),
                StringComparison.Ordinal) ||
            !string.Equals(
                comparer.Property.ContainingNamespace?.ToDisplayString(),
                "System",
                StringComparison.Ordinal) ||
            !dictionaries.TryAdd(local, entityType))
        {
            throw new InvalidDataException(
                $"C# Meta factory local '{local.Name}' is not a modeled entity list or its ordinal-ignore-case identity index.");
        }
    }

    private static ILocalSymbol RequireDictionaryIndexLoop(
        CSharpMetaSourceMap source,
        ForEachStatementSyntax syntax,
        IReadOnlyDictionary<ILocalSymbol, CSharpEntityRecords> recordsByLocal,
        IReadOnlyDictionary<ILocalSymbol, INamedTypeSymbol> dictionaries)
    {
        var iterator = source.FactorySemanticModel.GetDeclaredSymbol(
            syntax) as ILocalSymbol;
        var collectionOperation = source.FactorySemanticModel.GetOperation(
            syntax.Expression);
        if (iterator == null ||
            collectionOperation == null ||
            Unwrap(collectionOperation) is not
                ILocalReferenceOperation collectionReference ||
            !recordsByLocal.TryGetValue(
                collectionReference.Local,
                out var entityRecords) ||
            syntax.Statement is not BlockSyntax
            {
                Statements.Count: 1,
            } block ||
            block.Statements[0] is not
                ExpressionStatementSyntax expression ||
            source.FactorySemanticModel.GetOperation(
                expression.Expression) is not
                ISimpleAssignmentOperation assignment ||
            assignment.Target is not
                IPropertyReferenceOperation
                {
                    Property.IsIndexer: true,
                    Instance: ILocalReferenceOperation dictionaryReference,
                    Arguments.Length: 1,
                } dictionaryTarget ||
            !dictionaries.TryGetValue(
                dictionaryReference.Local,
                out var dictionaryEntityType) ||
            !SymbolEqualityComparer.Default.Equals(
                dictionaryEntityType,
                entityRecords.EntityMap.Type) ||
            Unwrap(assignment.Value) is not
                ILocalReferenceOperation valueReference ||
            !SymbolEqualityComparer.Default.Equals(
                valueReference.Local,
                iterator) ||
            Unwrap(dictionaryTarget.Arguments[0].Value) is not
                IPropertyReferenceOperation
                {
                    Instance: ILocalReferenceOperation idSource,
                } idReference ||
            !SymbolEqualityComparer.Default.Equals(
                idSource.Local,
                iterator) ||
            !SymbolEqualityComparer.Default.Equals(
                idReference.Property,
                entityRecords.EntityMap.IdProperty))
        {
            throw new InvalidDataException(
                $"C# Meta factory '{source.FactoryMethod.Name}' contains an unsupported foreach statement.");
        }

        return dictionaryReference.Local;
    }

    private static void ReadRecordAssignment(
        CSharpMetaSourceMap source,
        ExpressionStatementSyntax syntax,
        IReadOnlyDictionary<ILocalSymbol, CSharpEntityRecords> recordsByLocal,
        IReadOnlyDictionary<ILocalSymbol, INamedTypeSymbol> dictionaries,
        IReadOnlySet<ILocalSymbol> populatedDictionaries)
    {
        if (source.FactorySemanticModel.GetOperation(
                syntax.Expression) is not
                ISimpleAssignmentOperation assignment ||
            assignment.Target is not IPropertyReferenceOperation target ||
            !TryResolveIndexedRecord(
                target.Instance,
                recordsByLocal,
                out var sourceRecord))
        {
            throw new InvalidDataException(
                $"C# Meta factory '{source.FactoryMethod.Name}' contains an unsupported expression statement.");
        }

        if (SymbolEqualityComparer.Default.Equals(
                target.Property,
                sourceRecord.EntityMap.IdProperty))
        {
            throw new InvalidDataException(
                $"C# Meta identity '{sourceRecord.EntityMap.Entity.Name}.Id' must be assigned in the object initializer.");
        }

        var scalar = sourceRecord.EntityMap.ScalarProperties
            .FirstOrDefault(item =>
                SymbolEqualityComparer.Default.Equals(
                    item.Value,
                    target.Property));
        if (!string.IsNullOrEmpty(scalar.Key))
        {
            throw new InvalidDataException(
                $"C# Meta scalar '{sourceRecord.EntityMap.Entity.Name}.{scalar.Key}' must be assigned in the object initializer.");
        }

        var relationship = sourceRecord.EntityMap.RelationshipProperties
            .FirstOrDefault(item =>
                SymbolEqualityComparer.Default.Equals(
                    item.Value,
                    target.Property));
        if (string.IsNullOrEmpty(relationship.Key))
        {
            throw new InvalidDataException(
                $"C# Meta factory assignment targets unknown member '{sourceRecord.EntityMap.Entity.Name}.{target.Property.Name}'.");
        }

        if (TryReadConstantString(
                assignment.Value,
                out var relationshipValue) &&
            relationshipValue == null)
        {
            sourceRecord.Record.RelationshipIds.Remove(
                relationship.Key);
            return;
        }

        if (TryReadRequiredTargetId(
                source,
                assignment.Value,
                dictionaries,
                populatedDictionaries,
                target.Property.Type,
                out var targetId))
        {
            var targetRecords = recordsByLocal.Values.Single(records =>
                SymbolEqualityComparer.Default.Equals(
                    records.EntityMap.Type,
                    target.Property.Type));
            var resolvedTarget = targetRecords.Records.SingleOrDefault(record =>
                string.Equals(
                    record.Id,
                    targetId,
                    StringComparison.OrdinalIgnoreCase));
            if (resolvedTarget == null)
            {
                throw new InvalidDataException(
                    $"C# Meta relationship assignment '{sourceRecord.EntityMap.Entity.Name}.{target.Property.Name}' points to missing Id '{targetId}'.");
            }

            sourceRecord.Record.RelationshipIds[relationship.Key] =
                resolvedTarget.Id;
            return;
        }

        if (TryResolveIndexedRecord(
                assignment.Value,
                recordsByLocal,
                out var targetRecord) &&
            SymbolEqualityComparer.Default.Equals(
                targetRecord.EntityMap.Type,
                target.Property.Type))
        {
            sourceRecord.Record.RelationshipIds[relationship.Key] =
                targetRecord.Record.Id;
            return;
        }

        throw new InvalidDataException(
            $"C# Meta relationship assignment '{sourceRecord.EntityMap.Entity.Name}.{target.Property.Name}' is not statically resolvable.");
    }

    private static bool TryResolveIndexedRecord(
        IOperation? operation,
        IReadOnlyDictionary<ILocalSymbol, CSharpEntityRecords> recordsByLocal,
        out CSharpRecordReference record)
    {
        operation = operation == null ? null : Unwrap(operation);
        ILocalSymbol? local = null;
        IOperation? indexOperation = null;
        if (operation is IPropertyReferenceOperation
            {
                Property.IsIndexer: true,
                Instance: ILocalReferenceOperation localReference,
                Arguments.Length: 1,
            } indexer)
        {
            local = localReference.Local;
            indexOperation = indexer.Arguments[0].Value;
        }
        else if (operation is IArrayElementReferenceOperation
                 {
                     ArrayReference: ILocalReferenceOperation arrayReference,
                     Indices.Length: 1,
                 } array)
        {
            local = arrayReference.Local;
            indexOperation = array.Indices[0];
        }

        if (local == null ||
            indexOperation?.ConstantValue is not
            {
                HasValue: true,
                Value: int index,
            } ||
            !recordsByLocal.TryGetValue(local, out var entityRecords) ||
            index < 0 ||
            index >= entityRecords.Records.Count)
        {
            record = default;
            return false;
        }

        record = new CSharpRecordReference(
            entityRecords.EntityMap,
            entityRecords.Records[index]);
        return true;
    }

    private static bool TryReadRequiredTargetId(
        CSharpMetaSourceMap source,
        IOperation operation,
        IReadOnlyDictionary<ILocalSymbol, INamedTypeSymbol> dictionaries,
        IReadOnlySet<ILocalSymbol> populatedDictionaries,
        ITypeSymbol targetEntityType,
        out string targetId)
    {
        operation = Unwrap(operation);
        if (operation is not IInvocationOperation invocation ||
            !string.Equals(
                invocation.TargetMethod.Name,
                "RequireTarget",
                StringComparison.Ordinal) ||
            !invocation.TargetMethod.IsStatic ||
            !SymbolEqualityComparer.Default.Equals(
                invocation.TargetMethod.ContainingType,
                source.FactoryMethod.ContainingType))
        {
            targetId = string.Empty;
            return false;
        }

        RequireTargetHelper(
            source,
            invocation.TargetMethod);
        var indexArgument = invocation.Arguments.SingleOrDefault(item =>
            string.Equals(
                item.Parameter?.Name,
                "rowsById",
                StringComparison.Ordinal));
        var argument = invocation.Arguments.SingleOrDefault(item =>
            string.Equals(
                item.Parameter?.Name,
                "targetId",
                StringComparison.Ordinal));
        if (indexArgument == null ||
            Unwrap(indexArgument.Value) is not
                ILocalReferenceOperation indexReference ||
            !dictionaries.TryGetValue(
                indexReference.Local,
                out var indexedEntityType) ||
            !SymbolEqualityComparer.Default.Equals(
                indexedEntityType,
                targetEntityType) ||
            argument == null ||
            !TryReadConstantString(argument.Value, out var value) ||
            value == null)
        {
            targetId = string.Empty;
            return false;
        }

        if (!populatedDictionaries.Contains(indexReference.Local))
        {
            throw new InvalidDataException(
                $"C# Meta relationship resolver uses identity index '{indexReference.Local.Name}' before it is populated.");
        }

        targetId = value;
        return true;
    }

    private static void RequireTargetHelper(
        CSharpMetaSourceMap source,
        IMethodSymbol constructedMethod)
    {
        var method = constructedMethod.OriginalDefinition;
        var syntax = RequireSingleDeclaration<MethodDeclarationSyntax>(
            method,
            $"C# Meta relationship resolver '{method.ContainingType.Name}.{method.Name}'");
        if (syntax.Body is not
            {
                Statements.Count: 3,
            } body ||
            body.Statements[0] is not IfStatementSyntax emptyCheck ||
            !ContainsOnlyThrow(emptyCheck.Statement) ||
            body.Statements[1] is not IfStatementSyntax lookupCheck ||
            !ContainsOnlyThrow(lookupCheck.Statement) ||
            body.Statements[2] is not ReturnStatementSyntax
            {
                Expression: not null,
            } returnStatement)
        {
            throw new InvalidDataException(
                $"C# Meta relationship resolver '{method.ContainingType.Name}.{method.Name}' has an unsupported body.");
        }

        var semanticModel = source.Compilation.GetSemanticModel(
            syntax.SyntaxTree);
        if (semanticModel.GetOperation(emptyCheck.Condition) is not
                IInvocationOperation emptyInvocation ||
            !emptyInvocation.TargetMethod.IsStatic ||
            !string.Equals(
                emptyInvocation.TargetMethod.Name,
                nameof(string.IsNullOrEmpty),
                StringComparison.Ordinal) ||
            emptyInvocation.TargetMethod.ContainingType.SpecialType !=
                SpecialType.System_String ||
            emptyInvocation.Arguments.Length != 1 ||
            Unwrap(emptyInvocation.Arguments[0].Value) is not
                IParameterReferenceOperation targetIdReference ||
            !string.Equals(
                targetIdReference.Parameter.Name,
                "targetId",
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"C# Meta relationship resolver '{method.ContainingType.Name}.{method.Name}' must reject an empty target Id.");
        }

        var lookupOperation = semanticModel.GetOperation(
            lookupCheck.Condition);
        if (lookupOperation is not IUnaryOperation
            {
                OperatorKind: UnaryOperatorKind.Not,
                Operand: IInvocationOperation lookupInvocation,
            } ||
            !string.Equals(
                lookupInvocation.TargetMethod.Name,
                "TryGetValue",
                StringComparison.Ordinal) ||
            lookupInvocation.Instance is not
                IParameterReferenceOperation rowsReference ||
            !string.Equals(
                rowsReference.Parameter.Name,
                "rowsById",
                StringComparison.Ordinal) ||
            lookupInvocation.Arguments.Length != 2 ||
            Unwrap(lookupInvocation.Arguments[0].Value) is not
                IParameterReferenceOperation lookupIdReference ||
            !SymbolEqualityComparer.Default.Equals(
                lookupIdReference.Parameter,
                targetIdReference.Parameter))
        {
            throw new InvalidDataException(
                $"C# Meta relationship resolver '{method.ContainingType.Name}.{method.Name}' must resolve the target Id through its identity index.");
        }

        var targetDesignation = lookupCheck.Condition
            .DescendantNodes()
            .OfType<SingleVariableDesignationSyntax>()
            .SingleOrDefault();
        var targetLocal = targetDesignation == null
            ? null
            : semanticModel.GetDeclaredSymbol(targetDesignation);
        var returnOperation = semanticModel.GetOperation(
            returnStatement.Expression!);
        if (targetLocal == null ||
            returnOperation == null ||
            Unwrap(returnOperation) is not
                ILocalReferenceOperation returnReference ||
            !SymbolEqualityComparer.Default.Equals(
                returnReference.Local,
                targetLocal))
        {
            throw new InvalidDataException(
                $"C# Meta relationship resolver '{method.ContainingType.Name}.{method.Name}' must return the resolved target.");
        }
    }

    private static bool ContainsOnlyThrow(StatementSyntax statement)
    {
        return statement switch
        {
            ThrowStatementSyntax => true,
            BlockSyntax
            {
                Statements.Count: 1,
            } block when block.Statements[0] is ThrowStatementSyntax => true,
            _ => false,
        };
    }

    private static INamedTypeSymbol? TryGetListElementType(
        ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol
            {
                Name: "List",
                Arity: 1,
            } named ||
            !string.Equals(
                named.ContainingNamespace?.ToDisplayString(),
                "System.Collections.Generic",
                StringComparison.Ordinal))
        {
            return null;
        }

        return named.TypeArguments[0] as INamedTypeSymbol;
    }

    private static INamedTypeSymbol? TryGetDictionaryValueEntityType(
        ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol
            {
                Name: "Dictionary",
                Arity: 2,
            } named ||
            !string.Equals(
                named.ContainingNamespace?.ToDisplayString(),
                "System.Collections.Generic",
                StringComparison.Ordinal) ||
            named.TypeArguments[0].SpecialType !=
            SpecialType.System_String)
        {
            return null;
        }

        return named.TypeArguments[1] as INamedTypeSymbol;
    }

    private static INamedTypeSymbol? TryGetReadOnlyCollectionElementType(
        ITypeSymbol? type)
    {
        if (type is not INamedTypeSymbol
            {
                Name: "ReadOnlyCollection",
                Arity: 1,
            } named ||
            !string.Equals(
                named.ContainingNamespace?.ToDisplayString(),
                "System.Collections.ObjectModel",
                StringComparison.Ordinal))
        {
            return null;
        }

        return named.TypeArguments[0] as INamedTypeSymbol;
    }

    private static string RequireConstantString(
        IOperation operation,
        string memberName)
    {
        if (!TryReadConstantString(operation, out var value) ||
            value == null)
        {
            throw new InvalidDataException(
                $"C# Meta member '{memberName}' must be assigned a non-null compile-time string constant.");
        }

        return value;
    }

    private static bool TryReadConstantString(
        IOperation operation,
        out string? value)
    {
        operation = Unwrap(operation);
        if (!operation.ConstantValue.HasValue ||
            operation.ConstantValue.Value is not (string or null))
        {
            value = null;
            return false;
        }

        value = operation.ConstantValue.Value as string;
        return true;
    }

    private static IOperation Unwrap(IOperation operation)
    {
        while (true)
        {
            switch (operation)
            {
                case IConversionOperation conversion:
                    operation = conversion.Operand;
                    continue;
                case IParenthesizedOperation parenthesized:
                    operation = parenthesized.Operand;
                    continue;
                default:
                    return operation;
            }
        }
    }

    private sealed record CSharpEntityRecords(
        CSharpEntityMap EntityMap,
        List<GenericRecord> Records);

    private readonly record struct CSharpRecordReference(
        CSharpEntityMap EntityMap,
        GenericRecord Record);
}
