---
name: author-meta-docs
description: Author and regenerate the combined Meta and Meta-BI documentation through MetaDocs workspaces and the canonical MetaMesh operation.
---

# Author MetaDocs

Use this skill for public documentation spanning `meta` and `meta-bi`. The
MetaDocs model, runtime, and CLI live in `meta`; the combined documentation
pipeline and its generated artifacts live in the sibling `meta-docs`
repository. Keep checkouts named `meta`, `meta-bi`, and `meta-docs` under the
same parent directory because the canonical mesh uses that coordinate system.

## Know the locations

- Authored public prose workspace:
  `meta-docs/Workspaces/metametabi-authored`
- Imported CLI and model reference workspaces:
  `meta-docs/Workspaces/<subject>`
- Combined generated suite:
  `meta-docs/SuiteWorkspace`
- Canonical regeneration mesh:
  `meta-docs/Documentation.MetaMesh`
- Rendered site output:
  `meta-docs/Site/docs.html`
- Published site: `https://metametabi.com/docs.html`

The regeneration operation imports the 22 modeled CLI surfaces and documented
models from both repositories, validates authored and opt-in instance sources,
merges the suite, validates it, and renders the site.

## Inspect and author

Use `meta-docs browse`, `contents`, and `search` against the authored workspace
or combined suite. Use `author-page` for a guide/overview page and
`update-description` for an existing subject, CLI, command, option, or model.
For substantial bodies, prefer the command's standard-input option where
available.

Do not hand-edit:

- imported CLI/model documentation workspaces;
- the combined suite;
- `docs.html`;
- generated instance XML as a substitute for `meta-docs` authoring.

The intentionally concise repository READMEs point into MetaDocs; do not copy
the full public narrative back into them.

## Validate and regenerate

From the `meta-docs` repository root:

```powershell
meta-docs validate --workspace Workspaces/metametabi-authored
meta-mesh validate --operation regenerate-public-docs --workspace Documentation.MetaMesh
meta-mesh run --operation regenerate-public-docs --workspace Documentation.MetaMesh
```

The mesh currently contains one 53-step operation over 95 declared workspaces.
Use `meta-mesh steps` to inspect it rather than reproducing those imports by
hand. Ensure current CLI model workspaces and all three sibling checkouts are
available before regeneration. The modeled operation is the end-to-end
documentation gate; MetaDocs tests remain focused on the model, runtime, and
CLI behavior.

## Review output

Browse the generated suite with `meta-docs`, open the rendered anchor when
visual confirmation matters, and search for stale titles, paths, or removed
subjects. Review the final Git diff: authored changes, refreshed reference
workspaces, suite output, and `docs.html` must all be explainable by the mesh.
Run `git diff --check` in both repositories. Do not regenerate or publish the
site unless the task requests generated output.
