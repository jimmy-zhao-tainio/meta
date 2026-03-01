meta import csv demo-csv\products.csv --entity Product --new-workspace Workspace
cd Workspace
meta import csv ..\demo-csv\suppliers.csv --entity Supplier
meta import csv ..\demo-csv\categories.csv --entity Category --plural Categories
meta import csv ..\demo-csv\warehouses.csv --entity Warehouse
meta import csv ..\demo-csv\orders.csv --entity Order
cd ..
