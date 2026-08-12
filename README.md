# meta

`meta` is a model-first metadata foundation. It defines representation-neutral
workspace operations and carries the same modeled structure through XML, SQL,
and C# surfaces.

The repository also contains the modeled command runtime and the MetaDocs,
MetaMesh, and MetaWeave foundation products. BI-specific models and tools live
in the companion [`meta-bi`](https://github.com/jimmy-zhao-tainio/meta-bi)
repository and consume `meta` as packages.

## Documentation

The canonical public reference is [metametabi.com/docs.html](https://metametabi.com/docs.html).
It covers the concepts, models, and command surfaces in both repositories.
Local generated outputs and their authored workflow are under
[`MetaDocs/Docs`](MetaDocs/Docs); they are kept here so documentation remains
reviewable and reproducible without duplicating the full reference in this
README.

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
