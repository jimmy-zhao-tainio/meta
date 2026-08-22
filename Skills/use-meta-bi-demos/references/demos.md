# Meta-BI demo routing

Use demos as executable product witnesses. Inspect each README and MetaMesh
operation before running it; prerequisites and destructive effects differ.

## Complete system

- `Demos/AdventureWorksBiStackDemo`: 27 workspaces, 29 operations, and 614
  ordered steps spanning source discovery, Raw and Business Data Vault,
  dimensional warehouse, TransformScript and Binding, Data Quality, Pipeline,
  Orchestration, Analytics, Tabular, deployment, execution, and verification.
  It requires AdventureWorks2022 and local SQL Server for database operations;
  Tabular operations are optional and require Analysis Services.

## Modeling and conversion

- `RawDataVaultCliIntegration`: author, convert, deploy, and verify Raw Data
  Vault.
- `RawDataVaultFromMetaSchemaCliIntegration`: extract a schema, convert it to
  Raw Data Vault, convert to MetaSql, deploy, and verify.
- `BusinessDataVaultCliIntegration`: author, convert, deploy, and verify
  Business Data Vault.
- `MetaDataWarehouseCliIntegration`: author a dimensional model, convert it
  through an implementation workspace, deploy, and verify.
- `MetaAnalyticsCliIntegration`: author the portable analytics model through
  its owning CLI.

## Transforms, quality, and execution

- `MetaTransformScriptCliIntegration`: import, emit, re-import, project to
  MetaSql, and compare a small transform set.
- `MetaTransformScriptReferenceCorpusCliIntegration`: broader supported SQL
  form and round-trip witness.
- `MetaTransformScriptTpcDsCliIntegration`: q01-q99-derived parser, binding,
  emission, re-import, and semantic projection pressure corpus.
- `MetaTransformBindingCliIntegration`: import scripts and bind them against
  sanctioned schema workspaces.
- `MetaDataQualityCliIntegration`: end-to-end SQL Server Data Quality workflow.
- `MetaDataQualityPhase2ABCliIntegration`: offline corpus inference and
  promotion witness.
- `MetaDataQualityRealDbCliIntegration`: full corpus inference and execution
  against SQL Server.
- `MetaPipelineSqlServerCliIntegration`: executable transforms, batching,
  operational evidence, and SQL Server runtime.
- `MetaOrchestrationCliIntegration`: binding effects, pipeline workspaces,
  inferred dependencies, run planning, and execution.

## Analytical runtimes

- `MetaTabularDeployCliIntegration`: Analytics-to-Tabular conversion and live
  Tabular deployment.
- `MetaMultiDimensionalDeployCliIntegration`: Analytics-to-Multidimensional
  conversion and live deployment.
- `MetaMultiDimensionalHierarchyQueryCliIntegration`: hierarchy conversion,
  deployment, processing, and MDX query witness.
