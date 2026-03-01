@echo off
meta import csv demo-csv\products.csv --entity Product --new-workspace Workspace
meta import csv demo-csv\suppliers.csv --entity Supplier --workspace .\Workspace
meta import csv demo-csv\categories.csv --entity Category --plural Categories --workspace .\Workspace
meta import csv demo-csv\warehouses.csv --entity Warehouse --workspace .\Workspace
meta import csv demo-csv\orders.csv --entity Order --workspace .\Workspace
