@echo off
meta model suggest --workspace .\Workspace
meta model refactor property-to-relationship --source Order.ProductId --target Product --lookup Id --drop-source-property --workspace .\Workspace
meta model refactor property-to-relationship --source Order.SupplierId --target Supplier --lookup Id --drop-source-property --workspace .\Workspace
meta model refactor property-to-relationship --source Order.WarehouseId --target Warehouse --lookup Id --drop-source-property --workspace .\Workspace
meta model suggest --workspace .\Workspace
meta check --workspace .\Workspace
