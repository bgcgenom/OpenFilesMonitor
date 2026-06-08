---
title: OpenFilesMonitor
description: Windows WPF GUI to list SMB open files on Windows file servers (Get-SmbOpenFile / MSFT_SmbOpenFile) and close selected handles (Close-SmbOpenFile).
---

# OpenFilesMonitor

**OpenFilesMonitor** is a lightweight Windows **WPF** admin tool that connects to one or more **Windows file servers** and shows **currently open SMB files** with full path and user context. It also allows you to **close selected open file handles** (optionally “force”) so you can release stuck locks on shared files.

If you’ve ever needed a simple **“Get-SmbOpenFile GUI”** or **“Close-SmbOpenFile GUI”** for helpdesk/sysadmin work, this is that.

## What it does

- **Lists SMB open files** across multiple Windows file servers
- Shows:
  - Server
  - Client user name
  - Client computer name
  - Full path / share path (when available)
  - FileId
- **Filters** results quickly (user, client, filename, path, share path)
- **Exports** results to CSV
- **Closes selected open files** using server-side SMB handle close (best effort)

## How it works

Under the hood, it queries open SMB handles via the Windows SMB provider (`MSFT_SmbOpenFile`) over **CIM/WinRM (WSMan)** with **DCOM fallback**.

For closing handles, it uses the supported SMB cmdlet approach (**Close-SmbOpenFile**) via an external `powershell.exe` invocation (avoids embedded runspace issues).

## Requirements

### On the admin workstation (where you run the app)
- Windows 10/11 or Windows Server with desktop UI
- Network access to the file servers
- Credentials that have rights to enumerate and close open SMB handles

### On the file servers
- SMB server role (normal file server)
- Remote management access:
  - WinRM/CIM (WSMan), or DCOM (depending on environment)
- Permissions:
  - Typically requires **local admin** or equivalent rights to close handles

> Note: Closing an SMB handle drops the server-side file lock. Client apps (like Excel) may keep the window open, but saving usually errors or forces a “Save As”.

## Download

Go to the **Releases** page for the latest build:

- **Download:** see the repository’s **Releases** section (recommended)

If you download the framework-dependent build, you’ll need the **.NET 8 Desktop Runtime** installed on Windows.

## Build from source

```powershell
dotnet restore
dotnet build
dotnet run --project .\OpenFilesMonitor\OpenFilesMonitor.csproj
