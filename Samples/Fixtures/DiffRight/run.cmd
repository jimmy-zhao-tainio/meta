cd Workspace
meta instance update Cube 1 --set "Purpose=Right revised"
meta insert Cube 3 --set "CubeName=Operations Cube" --set "Purpose=Operational reporting" --set RefreshMode=Manual
meta instance diff ..\..\DiffLeft\Workspace .
cd ..
