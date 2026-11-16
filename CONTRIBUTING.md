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
dotnet restore Meta.sln
dotnet build Meta.sln --configuration Release --no-restore --nologo -m:1 -nr:false -p:UpdateMetaPublishDir=false -p:UpdateMetaDocsPublishDir=false -p:UpdateMetaWeavePublishDir=false -p:UpdateInstallMetaPublishDir=false
```

`Meta.sln` includes the foundation libraries, modeled tools, Installer and tests.
The command above builds all required test artifacts without refreshing the local
published tool directories. A separate Installer build is not required.

Run the focused test project for the changed area. The unit profile contains
only deterministic in-process tests, disables xUnit parallelism, and requires
every unit assembly's summed TRX execution time to remain below one second:

```powershell
.\eng\test.ps1 -Profile Unit -Configuration Release
```

Run `-Profile Integration` when a change affects mutable process environment,
workspace surfaces, CLIs, or cross-component behavior. The runner reports
summed per-test execution and process time separately. It never restores or
builds; missing outputs fail as missing prerequisites. The `Performance100K`
and `Performance1M` profiles remain separate scale witnesses.
Run these profiles locally; GitHub hosts the source repository, not the build
or test execution.

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
