# User guide

## The window

The editor has three columns.

1. **When I right-click** is where you choose what was clicked: a file, a folder, a drive, empty space in a folder, or empty space on the desktop. For a file you also choose the file type, because Windows builds a different menu for each one.
2. **Windows shows this menu** is the menu Windows builds for that target right now, read live from your system. Your changes are marked on it: hidden items are struck through, and renamed or moved items carry a note.
3. **You will get this menu** is a preview of the result once your rules are applied.

Tick **Include Shift-only items** to also see entries that normally appear only while you hold Shift. **Read the menu again** re-reads the menu, which is useful after you install or remove a program.

## Changing an item

Select an item in the middle column. The panel underneath lets you:

- **Hide** it.
- **Rename** it.
- **Move it into a submenu.** Type a name to create a submenu, or pick one you have already made. Several items can share a submenu.
- **Change its position** to the top or bottom of the menu.

**Undo changes** removes every change you made to the selected item.

Items inside a submenu, such as the entries under 7-Zip, can be changed the same way. Expand the submenu and select the item.

## Choosing where a change applies

Each change has a scope, set with **This change applies to**:

| Scope | Example |
|---|---|
| One file type | Only `.txt` files |
| One kind of target | All files, or only folders, or only the desktop |
| Everything | Everything you right-click |

New changes start with the scope you used last. When rules overlap, the narrower one wins: a rule for `.txt` files overrides a rule for everything.

Rules match items by their title. If two programs add items with the same title, a rule affects both.

## Applying your changes

Changes are saved as you make them, but Explorer does not use them until you apply.

1. Install [Nilesoft Shell](https://github.com/moudey/Shell) if you have not already: `winget install Nilesoft.Shell`. Installing it replaces the Windows 11 menu with its own straight away.
2. Choose **Apply to Explorer**.
3. Hold Ctrl and right-click once in Explorer. This makes the engine reload its config.

The top right of the window shows whether the engine is installed and whether it is using your rules.

## Going back

- **Stop using my rules** returns Nilesoft Shell to its own settings. Hold Ctrl and right-click once to reload.
- To return to the standard Windows menu, uninstall the engine: `winget uninstall Nilesoft.Shell`.
- Holding Ctrl and the Windows key while you right-click turns the engine off and shows the classic Windows menu. Holding Ctrl while you right-click turns it back on.

## Where things are kept

| What | Where |
|---|---|
| Your rules | `%APPDATA%\RCConfigurator\profile.json` |
| The config written for the engine | `%LOCALAPPDATA%\RCConfigurator\shell.nss` |
| Sample files the probe right-clicks | `%LOCALAPPDATA%\RCConfigurator\samples` |
| The setting that points the engine at the config | `HKCU\Software\Nilesoft\Shell`, value `config` |

Deleting the first three resets RCConfigurator. Nothing is written outside your own user profile.

## Troubleshooting

**The menu in Explorer has not changed.** Hold Ctrl and right-click once to reload. If it still has not changed, look for errors in `shell.log` in the engine's folder, usually `C:\Program Files\Nilesoft Shell`.

**"Could not read the menu."** A shell extension may have stalled or crashed while the menu was being read. Choose **Read the menu again**. If one target always fails, a program that adds items to that menu is the likely cause.

**An item is missing from the middle column.** The editor shows the classic menu, the one behind "Show more options" on Windows 11. Items that exist only in the Windows 11 menu do not appear, and the engine replaces that menu anyway.

**A rule does not match on other files.** Some titles include the name of what you clicked, such as `Add to "report.zip"`. The editor matches those by the part before the name. If a title is built some other way, the rule may only match the sample file.
