# DIPlaybox

A Windows desktop archive browser for Disney Infinity modding and asset research.

DIPlaybox provides a simple interface for browsing and extracting ordinary ZIP files and supported Disney Infinity encrypted `PK-FFFF` archives. It is a standalone C# application built with Windows Forms and .NET 8; Unity and QuickBMS are not required.

**Status: early prototype.** Archive support is limited, and compatibility with every game version or platform has not been established. This source package has not been independently build- or gameplay-tested as part of its GitHub preparation.

## Features

- Browse archive filenames, original sizes, packed sizes, and compression methods.
- Filter entries by filename.
- Extract selected entries or an entire archive.
- Test supported DI entries using decompression, size, and CRC checks.
- Create ordinary ZIP files and add or delete files in them.
- Open an archive through the file picker, drag and drop, or a command-line path.

**Disney Infinity archives are read-only.** ZIP creation and editing apply to ordinary ZIP files, not encrypted DI containers. DIPlaybox does not currently repack DI archives or convert models, textures, or animations.

## Requirements

- Windows.
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) to build from source.

No external NuGet packages are declared in the project.

## Build and run

Clone or download the repository, open a terminal in the directory containing `DIPlaybox.csproj`, and run:

```powershell
dotnet build -c Release
dotnet run -c Release
```

To open an archive at startup:

```powershell
dotnet run -c Release -- "C:\path\to\archive.zip"
```

To publish a self-contained 64-bit Windows application:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

The output is `publish\DIPlaybox.exe`. A self-contained build includes the runtime; users do not need the .NET SDK to run that published application.

## Using DIPlaybox

1. Select **Open** or drag an archive onto the window.
2. Browse the entries or enter a filename filter.
3. Select entries and choose **Extract Selected**, or use **Extract All**.
4. Use **Test** to check extraction of supported entries.

For ordinary ZIP editing, use **New ZIP**, **Add Files**, and **Delete Selected**. Work on copies when modifying archives.

## Current limitations

- DI decompression supports methods **0 (stored)** and **8 (deflate)** only.
- Alternate-key `psx_` archives and special full-payload `.dct` encryption handling are not implemented.
- Plaintext proprietary DI containers and other platform/archive variants are not guaranteed to work.
- Password-protected ordinary ZIPs are not supported.
- Extraction limits are 512 MiB per entry and 4 GiB total.
- ZIP creation currently adds selected top-level files; recursive folder import is not implemented.
- The DI reader's encryption/header handling still needs validation across representative archives. A failed read does not necessarily mean the source archive is corrupt.

## Project layout

```text
DIPlaybox.csproj          Project and build settings
Program.cs               Application entry point
MainForm.cs              Archive browser and ZIP editing interface
Core/ArchiveService.cs   Format detection, DI reading, extraction, and CRC checks
```

The repository contains tool source only. Game archives, extracted assets, IDE caches, and build output are excluded.

## Reporting issues and contributing

Useful contributions include archive-format fixes, additional compression support, extraction validation, and interface improvements.

When reporting an issue, include the tool revision, game/platform/version, archive type, operation attempted, and complete error message. Small synthetic reproductions or diagnostic details are preferable to uploading game assets. Do not include account data or private files.

## Credits and licensing

Disney Infinity and its game assets belong to their respective rights holders. DIPlaybox is an unofficial project and is not affiliated with or endorsed by Disney or Avalanche Software.

The initial DI reader was adapted from `DI3Archives.cs`, as recorded in the original project README. Upstream authorship and licensing should be documented before assigning a repository-wide license. No license grant is implied by this README.
