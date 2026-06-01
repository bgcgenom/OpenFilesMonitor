# OpenFilesMonitor

OpenFilesMonitor is a Windows WPF utility for system administrators who need to view SMB open files on Windows file servers and optionally close open file handles.

## Why use it?

Windows file servers often accumulate locked files from disconnected users, crashed applications, stale sessions, or long-running processes. OpenFilesMonitor gives admins a simple GUI for checking open SMB files across configured servers without jumping between multiple MMC consoles or command-line tools.

## Features

- View open SMB files on Windows file servers
- Track which user/session has a file open
- Close selected open file handles when needed
- Store server profiles locally
- Protect stored passwords using Windows DPAPI CurrentUser encryption
- Built with C# / WPF / .NET 8 for Windows

## Download

Download the latest release from the Releases page.

## Requirements

- Windows 10/11 or Windows Server
- .NET 8 Desktop Runtime, unless using a self-contained build
- Appropriate permissions on the target file server

## Quick start

1. Download the latest release.
2. Extract the ZIP.
3. Run `OpenFilesMonitor.exe`.
4. Add one or more Windows file servers.
5. Refresh the open files list.
6. Optionally close selected handles.

## Security notes

Server definitions are stored in:

`%LOCALAPPDATA%\OpenFilesMonitor\servers.json`

Passwords are encrypted using Windows DPAPI CurrentUser scope and are not portable between Windows users or machines.

## Build from source

...
