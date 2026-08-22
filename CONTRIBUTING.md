# Contributing

Thank you for contributing to `meta`. The project is model-first: a change to a
workspace model is an architectural change, while generated XML, C#, SQL, and
documentation are derived surfaces.

## Before changing a model

Open an issue or discussion before making a broad product-model change. Keep
bug fixes and implementation corrections separate from proposals that alter a
sanctioned contract.

## Build and test

The current toolchain is .NET 8 on Windows. Restore once, then build serially
because projects share local generated and package outputs:

```powershell
dotnet restore Metadata.Framework.sln
dotnet build Metadata.Framework.sln --configuration Release --no-restore --nologo -m:1 -nr:false -p:UpdateMetaPublishDir=false -p:UpdateMetaDocsPublishDir=false -p:UpdateMetaWeavePublishDir=false
```

Run the focused test project for the changed area. Run the full correctness
profile after a successful build with:

```powershell
.\eng\test.ps1 -Profile Correctness -Configuration Release -NoBuild
```

The runner keeps builds serial, then runs already-built test projects with
bounded concurrency and reports each project's duration. Omit `-NoBuild` to
restore, build, and test in one command. The `Performance100K` and
`Performance1M` profiles are separate scale witnesses rather than part of the
everyday correctness path. The continuous-integration workflow in
[`.github/workflows/ci.yml`](.github/workflows/ci.yml) uses the same runner.

## Generated artifacts and documentation

- Change the authoring model or generator, then regenerate affected outputs.
- Do not hand-edit generated workspace instance XML or the generated MetaDocs
  site.
- The public documentation source and regeneration mesh live in the dedicated
  [`meta-docs`](https://github.com/jimmy-zhao-tainio/meta-docs) repository.
- Include generated changes in the same pull request so reviewers can verify
  the result against its source.

## Pull requests

Keep changes focused, explain any model-contract impact, list generated files,
and report the exact builds and tests run. Run `git diff --check` before
submission and avoid committing local feeds, build outputs, credentials, or
machine-specific paths.
