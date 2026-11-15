#nullable enable
using System;
using System.Collections.Generic;

namespace MetaTypedModelsSqlOrchestrationProof;
public sealed partial class Item
{
    public string Id { get; set; } = null!;
    public string? Note { get; set; }
}

public sealed partial class MetaTypedModelsSqlOrchestrationProofModel
{
    public static MetaTypedModelsSqlOrchestrationProofModel CreateEmpty() => new();
    public List<Item> ItemList { get; set; } = new();
}

public static partial class MetaTypedModelsSqlOrchestrationProofInstance
{
    private static readonly MetaTypedModelsSqlOrchestrationProofModel _builtIn = CreateBuiltIn();
    public static MetaTypedModelsSqlOrchestrationProofModel BuiltIn => _builtIn;

    public static MetaTypedModelsSqlOrchestrationProofModel CreateBuiltIn()
    {
        var model = MetaTypedModelsSqlOrchestrationProofModel.CreateEmpty();
        return model;
    }
}