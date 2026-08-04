namespace MetaMesh.Core;

public sealed record MetaMeshWorkspaceContext(
    string WorkspaceLocation,
    string? WorkspaceDirectory,
    string CurrentDirectory);
