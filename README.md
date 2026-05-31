# OpenFilesMonitor (v1.2 Recreated)

WPF tool to list SMB open files on Windows file servers and optionally close handles.

## Build
```powershell
dotnet restore
dotnet build
dotnet run --project .\OpenFilesMonitor\OpenFilesMonitor.csproj
```

## Publish (folder)
```powershell
dotnet publish .\OpenFilesMonitor\OpenFilesMonitor.csproj -c Release -r win-x64 -p:SelfContained=true -p:PublishSingleFile=false
```

## Notes
- Servers & credentials are stored in `%LOCALAPPDATA%\OpenFilesMonitor\servers.json`
- Passwords are encrypted with DPAPI (CurrentUser) and are not portable to other machines/users.