@echo off
meta instance update Cube 1 --set "Purpose=Right revised" --workspace .\Workspace
meta insert Cube 3 --set "CubeName=Operations Cube" --set "Purpose=Operational reporting" --set RefreshMode=Manual --workspace .\Workspace
meta instance diff .\..\DiffLeft\Workspace .\Workspace
