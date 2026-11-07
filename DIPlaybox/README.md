# DI Playbox — Windows archive manager (prototype v0.1)

WinForms .NET 8 source project. Supports ordinary ZIPs and the Disney Infinity encrypted `PK FF FF` ZIP-like container, based on the user's `DI3Archives.cs`.

## Build on Windows

1. Install the **.NET 8 SDK** (Windows).
2. Extract this project folder.
3. Open PowerShell inside the `DIPlaybox` folder.
4. Run:

```powershell
dotnet build -c Release
dotnet run -c Release -- "C:\path\to\archive.zip"
```

To publish a standalone Windows executable (win-x64):

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

The resulting application is `publish\DIPlaybox.exe`. The SDK is required to build; the self-contained published app does not require users to install the runtime.

## Features

- Open normal ZIP archives and Disney Infinity encrypted ZIP-like archives.
- Browse filename, size, packed size, compression.
- Filter archive entries by filename.
- Extract selected entries or all entries.
- Test entry extraction and CRC checks for DI entries.
- Create ZIPs, add files, delete files from **standard ZIPs**.
- Drag and drop a ZIP onto the window; accepts a file path as the first command-line argument.

## Limitations / cautions

- The Lua viewer update builds successfully and passed 24 decoder/highlighting/archive assertions. Full interactive UI coverage and the optional external unluac action still need manual testing.
- DI archive reads were checked against real data.zip and gamedb/core/core.zip samples; this does not certify all archive variants.
- DI archives are read-only; no repacking, adding, or deleting.
- DI entry compression methods 0 (stored) and 8 (deflate) only.
- The original QuickBMS script may have special cases for `.dct`, `psx_` archives, and other compression methods. These are **not implemented**.
- ZIP encryption/password-protected ZIPs are not supported by .NET ZipArchive.
- Individual entry safety limit: 512 MiB; total extraction safety limit: 4 GiB.
- Only adds top-level files to standard ZIPs in this first version; no folder import yet.
- DI local headers use the original implementation's reset-to-zero CTR behavior; some archive variants may require corrections.
- Before setting this as the system default `.zip` app, test it with ordinary and DI archives. Windows "Open with" -> "Choose another app" is the easiest safe first test. Keep your previous ZIP program available.

## Design

- `Core/ArchiveService.cs`: format detection, DI AES-CTR index, per-entry extraction, standard ZIP reading, safe extraction.
- `MainForm.cs`: desktop archive UI and normal ZIP editing.
- `Program.cs`: Windows application entry point.

This is a standalone app; no Unity Editor or QuickBMS dependency.

## Lua viewer update

See [LUA_VIEWER.md](LUA_VIEWER.md) for native Lua 5.1 table decoding, syntax highlighting, archive-member viewing and the optional external unluac action.

## BENT / MTB viewers

Read-only collapsed trees for supported BENT records and MTB sections/material/sampler bindings. Open loose files or double-click archive members. Includes search, offsets, hex and explicit unsupported-format diagnostics. See [BENT_MTB_VIEWERS.md](BENT_MTB_VIEWERS.md).

## Texture and model viewers

Double-click supported TBODY/DDS/TSTREAM/PNG textures or model OCT entries. Includes texture previews and PNG-copy export, textured bind-pose geometry, source normals, skeleton overlay, orbit/zoom, and targeted external texture lookup. See [TEXTURE_MODEL_VIEWERS.md](TEXTURE_MODEL_VIEWERS.md) for supported formats and limits. Build the main project normally; its WPF preview library is referenced automatically.
