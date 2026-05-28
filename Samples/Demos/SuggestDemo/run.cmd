cd Workspace
meta model suggest
meta model refactor property-to-relationship --source Order.ProductId --target Product --lookup Id --drop-source-property
meta model refactor property-to-relationship --source Order.SupplierId --target Supplier --lookup Id --drop-source-property
meta model refactor property-to-relationship --source Order.WarehouseId --target Warehouse --lookup Id --drop-source-property
meta model suggest
meta check
cd ..
