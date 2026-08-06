# Samples

Samples in this repo are command-driven.

Rules:
- run the `.cmd` files from the sample directory they live in
- no PowerShell
- no variables inside sample scripts
- `setup.cmd` creates the sample workspace state
- `run.cmd` executes the example flow
- `cleanup.cmd` removes generated workspace state

Structure:
- `Contracts`: source inputs used by import-based samples
- `MainWorkspace`: bootstrap a workspace from the sample contracts
- `Demos/SuggestDemo`: CSV import -> suggest -> refactor
- `Fixtures/DiffLeft` and `Fixtures/DiffRight`: equal-model diff/merge pair
