#nullable enable
using System;
using System.Collections.Generic;

namespace MetaMesh;
public sealed partial class CSharpWorkspace
{
    public string Id { get; set; } = null !;
    public string Path { get; set; } = null !;
    public Workspace Workspace { get; set; } = null !;
}

public sealed partial class Mesh
{
    public string Id { get; set; } = null !;
    public string? Description { get; set; }
    public string Name { get; set; } = null !;
    public string? RootPath { get; set; }
}

public sealed partial class Operation
{
    public string Id { get; set; } = null !;
    public string? Description { get; set; }
    public string Name { get; set; } = null !;
    public Mesh Mesh { get; set; } = null !;
}

public sealed partial class OperationStep
{
    public string Id { get; set; } = null !;
    public string? Arguments { get; set; }
    public string? Description { get; set; }
    public string Executable { get; set; } = null !;
    public string? ExpectedExitCode { get; set; }
    public string Name { get; set; } = null !;
    public string? WorkingDirectory { get; set; }
    public Operation Operation { get; set; } = null !;
    public OperationStep? PreviousStep { get; set; }
}

public sealed partial class SqlWorkspace
{
    public string Id { get; set; } = null !;
    public string ConnectionEnvironmentVariable { get; set; } = null !;
    public Workspace Workspace { get; set; } = null !;
}

public sealed partial class Workspace
{
    public string Id { get; set; } = null !;
    public string? Description { get; set; }
    public string? ModelName { get; set; }
    public string Name { get; set; } = null !;
    public Mesh Mesh { get; set; } = null !;
}

public sealed partial class XmlWorkspace
{
    public string Id { get; set; } = null !;
    public string Path { get; set; } = null !;
    public Workspace Workspace { get; set; } = null !;
}

public sealed partial class MetaMeshModel
{
    public static MetaMeshModel CreateEmpty() => new();
    public List<CSharpWorkspace> CSharpWorkspaceList { get; set; } = new();
    public List<Mesh> MeshList { get; set; } = new();
    public List<Operation> OperationList { get; set; } = new();
    public List<OperationStep> OperationStepList { get; set; } = new();
    public List<SqlWorkspace> SqlWorkspaceList { get; set; } = new();
    public List<Workspace> WorkspaceList { get; set; } = new();
    public List<XmlWorkspace> XmlWorkspaceList { get; set; } = new();
}

public static partial class MetaMeshInstance
{
    private static readonly MetaMeshModel _builtIn = CreateBuiltIn();
    public static MetaMeshModel BuiltIn => _builtIn;

    public static MetaMeshModel CreateBuiltIn()
    {
        var model = MetaMeshModel.CreateEmpty();
        return model;
    }
}