# meta

`meta` is a model-first metadata foundation. It defines representation-neutral
workspace operations and carries the same modeled structure through XML, SQL,
and C# surfaces.

The repository also contains
[MetaCli](https://metametabi.com/docs.html#cli-meta-cli),
[MetaDocs](https://metametabi.com/docs.html#cli-meta-docs),
[MetaMesh](https://metametabi.com/docs.html#cli-meta-mesh), and
[MetaWeave](https://metametabi.com/docs.html#cli-meta-weave). Together they
provide modeled command surfaces, documentation, reproducible workspace
operations, and model-to-model correspondence. BI-specific models and tools
live in the companion
[`meta-bi`](https://github.com/jimmy-zhao-tainio/meta-bi) repository and consume
`meta` as packages.

## Documentation

The canonical public reference starts with the
[Meta overview](https://metametabi.com/docs.html#subject-public-meta-overview).
It covers the concepts, models, and command surfaces in both repositories.
Its authored workspaces, generated reference, site, and regeneration mesh live
in the dedicated
[`meta-docs`](https://github.com/jimmy-zhao-tainio/meta-docs) repository. The
MetaDocs model, runtime, and CLI remain here in `meta`.

Use `meta help` for local command help and `meta-docs browse` for the modeled
reference.

## Status

The project is under active pre-release development. The foundation is tested
as a coherent suite, but model and package contracts may still evolve before a
stable public release. Package versions carrying the `-internal` suffix are
development builds, not a statement that the source repository is private.

## Quick start

The current build requires the .NET 8 SDK. Windows is the environment exercised
by continuous integration.

```powershell
dotnet restore Metadata.Framework.sln
dotnet build Metadata.Framework.sln --configuration Release --no-restore --nologo -m:1 -nr:false
dotnet run --project Meta/Cli/Meta.Cli.csproj -- help
```

Build serially because several projects share local generated and package
outputs. See [CONTRIBUTING.md](CONTRIBUTING.md) for tests, documentation
generation, and contribution expectations.

## License

Licensed under the [Apache License 2.0](LICENSE).
