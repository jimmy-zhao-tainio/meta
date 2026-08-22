---
name: author-meta-docs
description: Author and regenerate the combined Meta and Meta-BI documentation through MetaDocs workspaces and the canonical MetaMesh operation.
---

# Author MetaDocs

Use this skill for public documentation spanning `meta` and `meta-bi`. The
combined documentation pipeline lives in the `meta` repository even when the
subject belongs to a `meta-bi` product.

## Know the locations

- Authored public prose workspace:
  `MetaDocs/Docs/Workspaces/metametabi-authored`
- Imported CLI and model reference workspaces:
  `MetaDocs/Docs/Workspaces/<subject>`
- Combined generated suite:
  `MetaDocs/Docs/SuiteWorkspace`
- Canonical regeneration mesh:
  `MetaDocs/Docs/Documentation.MetaMesh`
- Rendered site output:
  `MetaDocs/Docs/Site/docs.html`
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

From the `meta` repository root:

```powershell
meta-docs validate --workspace MetaDocs/Docs/Workspaces/metametabi-authored
meta-mesh validate --operation regenerate-public-docs --workspace MetaDocs/Docs/Documentation.MetaMesh
meta-mesh run --operation regenerate-public-docs --workspace MetaDocs/Docs/Documentation.MetaMesh
```

The mesh currently contains one 53-step operation over 95 declared workspaces.
Use `meta-mesh steps` to inspect it rather than reproducing those imports by
hand. Ensure current CLI model workspaces and both repository checkouts are
available before regeneration.

## Review output

Browse the generated suite with `meta-docs`, open the rendered anchor when
visual confirmation matters, and search for stale titles, paths, or removed
subjects. Review the final Git diff: authored changes, refreshed reference
workspaces, suite output, and `docs.html` must all be explainable by the mesh.
Run `git diff --check` in both repositories. Do not regenerate or publish the
site unless the task requests generated output.
