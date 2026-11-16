#nullable enable
using System;
using System.Collections.Generic;

namespace TestTyped;
public sealed partial class Alpha
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Note { get; set; }
    public Beta? Parent { get; set; }
}

public sealed partial class Beta
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
}

public sealed partial class TestTypedModel
{
    public static TestTypedModel CreateEmpty() => new();
    public List<Alpha> AlphaList { get; set; } = new();
    public List<Beta> BetaList { get; set; } = new();
}

public static partial class TestTypedInstance
{
    private static readonly TestTypedModel _builtIn = CreateBuiltIn();
    public static TestTypedModel BuiltIn => _builtIn;

    public static TestTypedModel CreateBuiltIn()
    {
        var model = TestTypedModel.CreateEmpty();
        return model;
    }
}