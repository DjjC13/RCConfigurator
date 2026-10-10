# RCConfigurator

A configurator for the Windows right-click menu. Pick what you right-clicked (a text file, a folder, the desktop), see the menu Windows builds for it, and change that menu directly.

**Status: early.** The editor reads real menus and writes rules. Applying those rules through the engine has not been tested against a live install yet.

## How it works

1. **Probe** (`rcprobe`): asks Windows for the menu it would show for a target and writes it out as JSON. It only reads; it never shows the menu or runs a command. It runs as its own process because reading a menu loads third-party shell extensions.
2. **Editor** (`RCConfigurator`): shows that menu beside a preview of the result. You can hide an item, rename it, pin it to the top or bottom, or move it into a submenu, and choose whether the change applies to one file type, one kind of target, or everything.
3. **Engine**: your rules are written as a config for [Nilesoft Shell](https://github.com/moudey/Shell), which applies them inside Explorer. RCConfigurator does not include the engine; install it separately.

## Build and run

Needs Windows 10 or 11 and the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```
dotnet build
dotnet test
dotnet run --project src/RCConfigurator.App
```

## Applying your rules

Install Nilesoft Shell (`winget install Nilesoft.Shell`), then choose **Apply to Explorer** in the editor. This:

- writes the config to `%LOCALAPPDATA%\RCConfigurator\shell.nss`, and
- points the engine at it through the per-user registry value `HKCU\Software\Nilesoft\Shell\config`.

Hold Ctrl and right-click once in Explorer to make the engine reload. **Stop using my rules** removes the registry value, which puts the engine back on its own settings.

Your rules are kept in `%APPDATA%\RCConfigurator\profile.json`.

## Using the probe on its own

```
rcprobe --tree C:\path\to\file.txt          one file
rcprobe --tree a.txt b.txt                  several items selected together
rcprobe --tree --background C:\some\folder  the empty area inside a folder
rcprobe --tree --desktop                    the empty area of the desktop
rcprobe --tree --extended C:\some\folder    include Shift-only items
```

Leave out `--tree` to get JSON.

## Known gaps

- Items can be pinned to the top or bottom but not yet put in an arbitrary order.
- You cannot add new items of your own yet.
- The editor does not cover several items selected together, though the probe can read those menus.
- The probe reads the classic menu (the one behind "Show more options" on Windows 11), which is the menu the engine replaces.

## Licence

MIT. See [LICENSE](LICENSE).
