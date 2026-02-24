# MCCompanion

**Cross-platform Midnight Commander companion** — a single `.exe` / binary  
that brings Windows-quality file manager features to MC on both **Windows** and **Linux**.  
All interactive dialogs use **Terminal.Gui v1** (MIT), a pure-TUI toolkit that  
runs inside the terminal — no X11 or GUI framework needed.

---

## Features

| Key | Command | Windows | Linux |
|-----|---------|---------|-------|
| `D` | `clip-dir`   | Copy directory to clipboard | Same (xclip/xsel/wl-copy) |
| `P` | `clip-path`  | Copy full path to clipboard | Same |
| `N` | `clip-name`  | Copy filename to clipboard  | Same |
| `Q` | `clip-path` (tagged) | Multiple paths | Same |
| `L` | `clip-name` (tagged) | Multiple names | Same |
| `W` | `context`    | **Native Windows context menu** (IContextMenu3) | Open in Nautilus/Dolphin/Nemo |
| `O` | `open-with`  | Windows Open With dialog    | TUI input → any app + xdg-open |
| `I` | `properties` | Windows Properties dialog   | TUI properties panel with stat info |
| `A` | `attributes` | H/R/S/A checkboxes (TUI)   | Unix chmod permissions (TUI) |
| `T` | `touch`      | Set timestamps (TUI)        | Same |
| `H` | `checksum`   | MD5/SHA-1/SHA-256 (TUI)     | Same |
| `S` | `dir-size`   | Recursive folder size (TUI) | Same |
| `R` | `rename`     | Batch rename with preview (TUI) | Same |
| `E` | `terminal`   | `wt` → `pwsh` → `cmd`      | `gnome-terminal` → `konsole` → `xterm` |
| `C` | `compare`    | WinMerge → VSCode → fc.exe  | Meld → kdiff3 → VSCode → vimdiff |

---

## Requirements

| Platform | Runtime | Clipboard |
|----------|---------|-----------|
| Windows  | .NET 8 Desktop Runtime (or self-contained publish) | Built-in Win32 |
| Linux    | .NET 8 Runtime | `xclip` **or** `xsel` **or** `wl-clipboard` |

Install clipboard tool on Ubuntu/Debian:
```bash
sudo apt install xclip
```

---

## Build

### Option A — Visual Studio 2022/2026 (Windows)
1. Open `MCCompanion.sln`
2. Select **Release | Any CPU**
3. **Ctrl+Shift+B**

### Option B — VS Code (Windows or Linux)
1. Open the root folder in VS Code
2. Install recommended extensions when prompted (C# Dev Kit)
3. **Ctrl+Shift+B** → `build` task

### Option C — Command line (any platform)
```bash
# Debug build
dotnet build MCCompanion/MCCompanion.csproj

# Release build
dotnet build MCCompanion/MCCompanion.csproj -c Release

# Self-contained single file for Linux (no runtime needed on target)
dotnet publish MCCompanion/MCCompanion.csproj \
  -c Release -r linux-x64 --self-contained true \
  -p:PublishSingleFile=true \
  -o ./publish/linux-x64

# Self-contained single file for Windows
dotnet publish MCCompanion/MCCompanion.csproj \
  -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true \
  -o ./publish/win-x64
```

---

## Installation

### Windows
Copy `MCCompanion.exe` to `C:\TOOLS\mc\` (or any directory in PATH).

### Linux
```bash
cp publish/linux-x64/MCCompanion ~/bin/MCCompanion
chmod +x ~/bin/MCCompanion
```
Make sure `~/bin` is in your `$PATH` (it is by default on Ubuntu).

---

## MC Menu Setup

### Windows
Open in MC: **F9 → Command → Edit Menu File**
Path: `C:\Users\<you>\AppData\Roaming\Midnight Commander\menu`

Paste the contents of `mc_menu_template.txt`, then replace `MCCompanion` with  
the full path: `C:\\TOOLS\\mc\\MCCompanion.exe`

### Linux
```bash
mkdir -p ~/.config/mc
cp mc_menu_template.txt ~/.config/mc/menu
```
The template already uses just `MCCompanion` — works if it's in `$PATH`.  
If not, replace with `~/bin/MCCompanion` or the full path.

---

## Architecture

```
MCCompanion/
├── MCCompanion.sln
├── mc_menu_template.txt          ← paste into MC's user menu
├── README.md
├── .vscode/
│   ├── tasks.json                ← build + publish tasks
│   ├── launch.json               ← debug launch configs
│   └── extensions.json           ← recommended extensions
└── MCCompanion/
    ├── MCCompanion.csproj        ← net8.0 (not net8.0-windows)
    ├── Program.cs                ← verb dispatcher
    ├── Commands/
    │   ├── Core.cs               ← ICommand, PathHelper, ClipboardHelper, ProcessHelper
    │   ├── ClipboardCommand.cs   ← clip-dir / clip-path / clip-name
    │   ├── ShellCommands.cs      ← context / properties / open-with (dispatches per OS)
    │   ├── ChecksumCommand.cs    ← TUI checksum dialog (pure .NET crypto)
    │   ├── BatchRenameCommand.cs ← TUI rename dialog with live preview
    │   ├── FileInfoCommands.cs   ← attributes / dir-size / touch
    │   └── UtilityCommands.cs    ← terminal / compare
    ├── Platform/
    │   └── Windows/              ← compiled ONLY on Windows
    │       ├── ShellContextMenu.cs   (IContextMenu2/3 P/Invoke)
    │       └── ShellDialogs.cs       (ShellExecuteEx, SHOpenWithDialog)
    └── UI/
        ├── Tui.cs                ← Terminal.Gui lifecycle wrapper
        └── LinuxDialogs.cs       ← Properties panel + Open-With for Linux
```

**Platform isolation strategy:**
- `Platform/Windows/*.cs` files are excluded from compilation on Linux via a  
  `Condition="!$([MSBuild]::IsOSPlatform('Windows'))"` in the `.csproj`.
- Runtime guards (`if (OperatingSystem.IsWindows())`) prevent Linux code paths  
  from calling Windows-only code even in the same file.
- `[SupportedOSPlatform("windows")]` attributes let the Roslyn analyser warn  
  if a Windows-only method is called without a guard.

---

## Troubleshooting

| Problem | Likely cause | Fix |
|---------|-------------|-----|
| `xclip not found` | Not installed | `sudo apt install xclip` |
| Context menu missing 7-Zip entries | 32-bit build | Publish as `win-x64` |
| Terminal doesn't open on Linux | No supported emulator found | Install `gnome-terminal` or `xterm` |
| `stat` not found (permissions) | Minimal container | Install `coreutils` |
| TUI garbled in Windows Terminal | Legacy console mode | Enable VT mode in Windows Terminal |
| `%d` has no trailing `/` in mcwin32 | mcwin32 quirk | Menu template uses `%d/%f` — the `/` is intentional |

---

## Adding new commands

1. Create a class implementing `ICommand` in `Commands/`
2. Add a `case "your-verb"` in `Program.cs`
3. Add a menu entry in `mc_menu_template.txt`
4. Rebuild — no registration, no config, no installer
