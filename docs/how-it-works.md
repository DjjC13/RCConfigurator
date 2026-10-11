# How it works

RCConfigurator has three parts: a probe that reads menus, an editor that turns your changes into rules, and a writer that turns rules into a config for the engine that applies them.

```
 rcprobe.exe ──JSON──▶ RCConfigurator.exe ──shell.nss──▶ Nilesoft Shell ──▶ Explorer
 reads the menu        edits and previews                 applies the rules
```

## Projects

| Project | What it is |
|---|---|
| `RCConfigurator.Core` | The menu model, the rule model, the preview, and the config writer. No Windows-specific code. |
| `RCConfigurator.Probe` | `rcprobe.exe`, a console program that reads one menu and prints it as JSON. |
| `RCConfigurator.App` | The editor, a WPF app. |
| `RCConfigurator.Tests` | Unit tests for Core, and tests that run the real probe. |

## The probe

The probe uses the documented shell interfaces a file manager uses to show Explorer's menu:

1. It turns the target path into a shell item and asks its parent folder for an `IContextMenu` (`IShellFolder::GetUIObjectOf`).
2. It calls `QueryContextMenu` on an empty popup menu, which makes Windows and every registered shell extension add their items.
3. It walks the resulting menu, reading each item's title, state and command name (`GetCommandString`).
4. For each submenu it sends the "about to open" message first, because most submenus are filled in only at that moment.

The probe never calls `InvokeCommand`, so it cannot run a menu command.

For the empty area of a folder or the desktop, the folder alone does not report the items Explorer's own view adds (View, Sort by, Refresh, Paste). The probe therefore hosts a real Explorer view (`IExplorerBrowser`) in a window that is never shown, and asks that view for its background menu.

The probe is a separate process because reading a menu loads third-party shell extensions into the process doing the reading. If one of them crashes or hangs, only the probe is affected; the editor reports the failure and carries on.

The probe reads the classic menu. On Windows 11 that is the menu behind "Show more options", and it is the menu the engine replaces.

## Rules

Each change you make becomes an `ItemRule`:

- **What it targets:** the item's title, and the submenu it sits in if any.
- **Where it applies:** a scope, which is everything, one kind of target, a group of file types, or one file extension, optionally limited to one selected thing or to several.
- **What it does:** hide, rename, move into a named submenu, pin to the top or bottom, or sit just above or below a named neighbour.

An item you add yourself is a `NewItem`: a title, a program, its arguments, a scope and a position.

Titles are compared the way the engine compares them: without accelerator markers, shortcut text or trailing dots, and ignoring case. `EngineName` holds that logic and mirrors the engine's own.

All rules and new items together form a `Profile`, saved as JSON.

### Why moved items name a neighbour

The engine can place an item at a numbered row, but a row number goes stale as soon as another change adds or removes a row above it. It can also place an item relative to another one found by title, and that is what the editor uses. When you press ▲ or ▼, the editor tries sitting above and below each other item in the menu, runs the preview for each, and keeps the choice that lands on the row you asked for.

## Groups of file types

A `FileKind` is a named list of extensions, and a rule's scope can name one. Groups are built in three steps:

1. Windows' own label for each registered extension (`PerceivedType` in the registry) gives Images, Videos, Audio, Text files, Documents and Archives.
2. Extensions Windows does not label are looked up in two public lists, downloaded whole and cached for 30 days: [mime-db](https://github.com/jshttp/mime-db) for the media type and [GitHub Linguist's languages.yml](https://github.com/github-linguist/linguist) for whether the extension holds code. `FileTypeCatalogue` applies them in a fixed order: a programming or markup language makes it Code; otherwise a media type of image, video, audio, text, font or model puts it in the matching group; otherwise any other Linguist entry makes it a text file. Media types under `application/` are ignored, because that family covers everything from fonts to installers and neither list says more.
3. The user's own groups are added.

No model or guesswork is involved: a type is in a group because Windows or one of the two lists says so, or because the user put it there.

## Combined menus

Types in a group rarely have identical menus, so a group is shown as one menu holding every item found on any of its types. `MenuMerge` builds it. It starts from the menu the most types share, so the usual layout shapes the result, then fits each other menu in: an item new to the result goes just after the last item that menu shares with it, or just below the next shared one if it has none before it. Each item keeps the list of types it was seen on.

Reading that many menus one process at a time would be slow, so the probe has an `--each` mode that reads a separate menu for each path in a single run, about ten times faster per file. The editor runs four of those at once, forty files each.

## The preview

`MenuPreview` applies a profile to a menu the probe read and follows the engine's ordering: items pinned to the top, then the rest in their original order with added items and submenus placed at the separator nearest the middle, then items pinned to the bottom. Items placed next to a neighbour are inserted last, one at a time and in their original order. It is a model of the engine, not the engine, so the real menu can differ in small ways.

## The engine config

Changing Explorer's menu at runtime means running code inside Explorer. RCConfigurator leaves that to [Nilesoft Shell](https://github.com/moudey/Shell) and only writes its config. `NssWriter` turns each rule into one `modify` entry:

| Rule | Config |
|---|---|
| Hide "Ask Copilot" everywhere | `modify(type='*' find="\"ask copilot\"" vis=vis.remove)` |
| Hide "Print" for `.txt` files | `modify(type='file' where=sel.file.ext==".txt" find="\"print\"" vis=vis.remove)` |
| Rename and pin to top | `modify(type='*' find="\"open with code\"" title="VS Code" pos="top")` |
| Move into a submenu | `menu(mode="multiple" title="Extras") { }` and `modify(type='*' find="\"share\"" menu="Extras")` |
| An item inside a submenu | `modify(type='dir' in="7-zip/crc sha" find="\"md5\"" vis=vis.remove)` |
| Move "Delete" to just below "Copy" | `modify(type='*' find="\"delete\"" pos=indexof("\"copy\"", 1))` |
| Only when several files are selected | `modify(type='file' where=sel.count>1 find="\"print\"" vis=vis.remove)` |
| Every type in a group | `modify(type='file' where=(sel.file.ext==".jpg" or sel.file.ext==".png") find="\"print\"" vis=vis.remove)` |
| A new item | `item(type='file' mode="multiple" title="Open in my editor" cmd="C:\\Tools\\editor.exe" args="\"" + sel.path + "\"" invoke="multiple")` |

The arguments of a new item are written as quoted pieces joined to `sel.path`, so nothing you type is evaluated as an engine expression.

Rules with a wider scope are written first, so narrower rules override them.

The config is written to `%LOCALAPPDATA%\RCConfigurator\shell.nss`. The engine reads its config path from the per-user registry value `HKCU\Software\Nilesoft\Shell\config` and falls back to the `shell.nss` in its own folder when that value is absent. **Apply to Explorer** sets the value and **Stop using my rules** removes it, so nothing in the engine's own folder is changed and no admin rights are needed.

The engine code is kept behind the `Engine` class and `NssWriter`, so another engine could be supported by replacing those two.

## Start-up requirements

Before the editor opens, the app checks three things:

- **The engine is installed.** It looks for `shell.exe` in the engine's usual folders.
- **The classic menu is on** (Windows 11 only). This is an empty default value under `HKCU\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32`, which stops Windows loading its newer menu for that user.
- **The engine is running.** Its context-menu class must be registered (`HKCR\CLSID\{BAE3934B-8A6A-4BFB-81BD-3FC599A1BAF1}`) and its DLL must be loaded in the Explorer process. Registration is what makes Explorer load it at every start, so nothing has to be added to Windows start-up.

If a check fails, a set-up window offers to fix everything and the editor does not open until all three pass.

## What needs administrator rights

RCConfigurator runs as an ordinary user. Only two steps change the whole PC, and for those it starts a separate elevated process so that Windows asks for permission:

- installing or updating the engine, which the Windows package manager does;
- registering the engine, by running its own `shell.exe -register -restart -silent`.

Everything else is per-user: the rules, the config file, the registry value that points the engine at it, and the classic-menu setting.

## Reloading the engine

The engine reads its config when Explorer starts, and otherwise only when the user makes a reload gesture. So applying rules means restarting Explorer. The app posts the taskbar's own "exit Explorer" message, waits for Explorer to leave, and starts it again. Asking it to exit normally matters: if Explorer is simply killed, Windows restarts it at the same moment the app does, and the second copy opens a stray folder window. The Restart Manager was tried first and rejected because it waited out a 30-second timeout on Explorer.

## Development aids

The editor accepts these command-line options, which exist for development:

| Option | Effect |
|---|---|
| `--profile file.json` | Keep rules in this file instead of the usual one. |
| `--screenshot file.png` | Save a picture of the first window that opens, then exit. |
| `--skip-checks` | Open the editor without the start-up requirements. |
| `--show-checks` | Show the set-up window even when everything is already in place. |
| `--show Images` | Open the editor on the file-type entry whose name starts that way. |
| `--offline` | Do not download the public file-type lists. Not only for development: use it to keep the app off the network. |
| `--demo` | With `--screenshot`: make a few sample changes first and write the resulting config beside the picture. |
