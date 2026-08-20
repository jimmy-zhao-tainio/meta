# MetaDocs

MetaDocs is documentation carried as a Meta workspace. It combines authored
technical prose with reference data imported from MetaCli command workspaces
and Meta model workspaces. Subjects, narratives, descriptions, navigation,
views, templates, and source provenance remain modeled and reviewable before
anything is rendered.

A documentation suite is assembled from multiple MetaDocs workspaces. Imports
refresh command and model structure while retaining authored descriptions;
suite merging combines those sources into one navigable reference; validation
checks its lifecycle and links; and rendering produces a static site.

The public `meta` and `meta-bi` reference is built this way. Its authored
material and imported workspaces live under [`Docs`](Docs), the combined
workspace is [`Docs/SuiteWorkspace`](Docs/SuiteWorkspace), and the complete
rebuild is declared by the
[`Documentation.MetaMesh`](Docs/Documentation.MetaMesh) workspace.

## Work with modeled documentation

The `meta-docs` CLI can browse and search a documentation workspace, import CLI
and model reference data, author pages and descriptions, merge a suite,
validate it, and render the static site.

```text
meta-docs contents --workspace MetaDocs/Docs/SuiteWorkspace
meta-docs render-site --workspace MetaDocs/Docs/SuiteWorkspace --out <site-directory>
```

The published result is
[metametabi.com/docs.html](https://metametabi.com/docs.html). The rendered site
is generated output; the MetaDocs workspaces are the maintained documentation
source.
