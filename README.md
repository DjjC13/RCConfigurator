<p align="center">
  <img src="assets/logo.svg" width="120" alt="RCConfigurator logo">
</p>

<h1 align="center">RCConfigurator</h1>

<p align="center">
  See the right-click menu Windows builds for anything you click, and change it.
</p>

![The editor showing a text file's menu beside a preview of the edited result](docs/images/editor.png)

The Windows right-click menu is different for every kind of thing you click: a text file, a folder, a drive, the desktop. RCConfigurator shows you the real menu for each one and lets you hide, rename, reorder and regroup its items and add your own, with a live preview of the result.

> **Status: early.** The editor reads real menus and writes rules. Applying those rules inside Explorer relies on [Nilesoft Shell](https://github.com/moudey/Shell), and that last step has not been tested against a live install yet. Expect rough edges.

## What it does

- **Reads the real menu.** It asks Windows for the menu it would show, including submenus and Shift-only items, so you see every entry your installed programs add.
- **Edits by context.** A change can apply to one file type, to one kind of target (all files, folders, drives, folder backgrounds, the desktop), only when several things are selected, or to everything you right-click.
- **Hides, renames, reorders and regroups.** Move any item up or down, pin it to the top or bottom, or collect items into submenus of your own.
- **Adds your own items.** Give an item a name and a program to run, and choose where it appears.
- **Previews the result.** The menu you will get is shown beside the menu Windows builds.
- **Applies without admin rights.** Your rules are written to a config file in your own profile and Nilesoft Shell is pointed at it.

## Getting started

Download the zip for your PC from the [latest release](https://github.com/DjjC13/RCConfigurator/releases), unzip it anywhere, and run `RCConfigurator.exe`. Nothing else needs installing to use the editor.

- `win-x64` is for most PCs.
- `win-arm64` is for Windows on ARM, such as Snapdragon laptops. Use it there rather than the x64 build, so the editor sees the same menu items Explorer does.

The builds are not code-signed, so Windows SmartScreen may warn the first time you run one.

To have your changes take effect in Explorer, install the engine:

```
winget install Nilesoft.Shell
```

Then choose **Apply to Explorer** in the editor, and hold Ctrl while you right-click once in Explorer so the engine reloads.

## Documentation

- [User guide](docs/user-guide.md): using the editor, applying and undoing changes, troubleshooting.
- [How it works](docs/how-it-works.md): the probe, the rule model, and how rules become an engine config.

## Not done yet

- A live test of the generated config against Nilesoft Shell. Everything up to that point is tested.
- Mixed selections, such as files and folders selected together.
- Icons of your choice, and submenus inside submenus.

## Building from source

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```
git clone https://github.com/DjjC13/RCConfigurator.git
cd RCConfigurator
dotnet run --project src/RCConfigurator.App
dotnet test
```

Some tests run the probe against the real Windows shell, so they need a normal desktop session.

## Credits

RCConfigurator does not change Explorer itself. That work is done by [Nilesoft Shell](https://github.com/moudey/Shell), an MIT-licensed project by Nilesoft. RCConfigurator is not affiliated with it and does not include it.

## Licence

MIT. See [LICENSE](LICENSE).
