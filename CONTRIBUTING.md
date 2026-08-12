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
dotnet build Metadata.Framework.sln --configuration Release --no-restore --nologo -m:1 -nr:false
```

Run the focused test project for the changed area and the relevant solution or
suite. The continuous-integration workflow in [`.github/workflows/ci.yml`](.github/workflows/ci.yml)
is the authoritative full verification list.

## Generated artifacts and documentation

- Change the authoring model or generator, then regenerate affected outputs.
- Do not hand-edit generated workspace instance XML or the generated MetaDocs
  site.
- The public documentation source and regeneration mesh live under
  [`MetaDocs/Docs`](MetaDocs/Docs).
- Include generated changes in the same pull request so reviewers can verify
  the result against its source.

## Pull requests

Keep changes focused, explain any model-contract impact, list generated files,
and report the exact builds and tests run. Run `git diff --check` before
submission and avoid committing local feeds, build outputs, credentials, or
machine-specific paths.
