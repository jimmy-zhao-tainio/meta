# MetaMesh

MetaMesh is a foundation model and CLI for mapping sanctioned metadata workspaces.

The current ergonomics slice provides:

- `MetaMesh/Workspace`: sanctioned model workspace.
- `MetaMesh/Tooling`: generated C# tooling from the model.
- `MetaMesh/Core`: workspace scan, check, suggest, mount, link, show, and impact logic.
- `MetaMesh/Cli`: `meta-mesh` command surface.
- `MetaMesh/Tests`: focused model/CLI regression tests.

Build and test from `../meta`:

```cmd
dotnet build MetaMesh.sln --nologo
dotnet test MetaMesh.sln --no-build --nologo
```

Representative CLI use:

```cmd
MetaMesh\Cli\bin\Debug\net8.0\meta-mesh.exe scan ..\meta-bi --new-workspace .\out\Current.MetaMesh --name Current
MetaMesh\Cli\bin\Debug\net8.0\meta-mesh.exe suggest .\out\Current.MetaMesh
MetaMesh\Cli\bin\Debug\net8.0\meta-mesh.exe check --mesh .\out\Current.MetaMesh
```

`MetaHost` is intentionally deferred. The workspace map remains model truth in
`workspace.xml`, `model.xml`, and `instances/*.xml`.
