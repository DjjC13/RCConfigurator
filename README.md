# RCConfigurator

A configurator for the Windows right-click menu. Pick what you right-clicked (a text file, a folder, the desktop, several files at once), see the menu Windows builds for it, and edit that menu directly.

**Status: early.** The probe that reads real menus works. The editor and the part that applies your changes are not built yet.

## How it works

1. **Probe** (`rcprobe`): asks Windows for the menu it would show for a target and writes it out as JSON. It only reads; it never shows the menu or runs a command. It runs as its own process because reading a menu loads third-party shell extensions.
2. **Editor** (planned): shows each menu and lets you hide, rename, reorder and regroup items, and add your own.
3. **Engine** (planned): your edits are written as rules for [Nilesoft Shell](https://github.com/moudey/Shell), which applies them inside Explorer.

## Build

Needs Windows 10 or 11 and the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```
dotnet build
dotnet test
```

## Using the probe

```
rcprobe --tree C:\path\to\file.txt          one file
rcprobe --tree a.txt b.txt                  several items selected together
rcprobe --tree --background C:\some\folder  the empty area inside a folder
rcprobe --tree --desktop                    the empty area of the desktop
rcprobe --tree --extended C:\some\folder    include Shift-only items
```

Leave out `--tree` to get JSON. After a build the executable is at `src\RCConfigurator.Probe\bin\Debug\net10.0-windows\rcprobe.exe`.

### Known gaps

- Background menus do not yet include the items Explorer's own view adds, such as View, Sort by, Refresh and Paste.
- The probe reads the classic menu (the one behind "Show more options" on Windows 11), which is the menu the engine replaces.

## Licence

MIT. See [LICENSE](LICENSE).
