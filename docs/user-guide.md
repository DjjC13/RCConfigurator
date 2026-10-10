# User guide

## Starting up

RCConfigurator needs three things before it opens the editor. It checks them every time it starts. If any is missing it shows a set-up window instead, and **Continue** stays greyed out until all three are ticked.

1. **Nilesoft Shell is installed.** This is the program that applies your changes inside Explorer.
2. **Windows shows the classic right-click menu.** Windows 11 normally shows a shorter menu with "Show more options" at the bottom. Your changes appear in the classic menu, so Windows needs to show that one straight away. It is a setting for your account only. Windows 10 already works this way.
3. **Nilesoft Shell is running in Explorer.** It has to be registered with Windows, and Explorer has to have loaded it.

**Set these up for me** does whatever is missing, in order:

- installs Nilesoft Shell with the Windows package manager (`winget install Nilesoft.Shell`), in its own window;
- switches Windows to the classic menu;
- registers Nilesoft Shell with Windows;
- restarts Explorer if that is needed for any of it to take effect.

Windows asks for administrator permission for the install and for the registration, because those change the whole PC. Nothing else needs it, and you never need to start RCConfigurator itself as administrator.

If the package manager is not available, the download page opens instead; install it from there and come back. **Quit** closes RCConfigurator. The window checks again whenever you return to it.

### Does anything need to start with Windows?

No. Once Nilesoft Shell is registered, Explorer loads it by itself every time it starts, so your menus are in place as soon as you sign in. RCConfigurator does not need to be running.

### Updates to Nilesoft Shell

When the editor opens, RCConfigurator asks the Windows package manager whether a newer Nilesoft Shell exists. If one does, it runs the update in its own window (Windows asks for permission), then makes sure the engine is still registered and running.

## The window

The editor has three columns.

1. **When I right-click** is where you choose what was clicked: a file, several files, a folder, several folders, a drive, empty space in a folder, or empty space on the desktop. For files you also choose the file type, because Windows builds a different menu for each one. Below it, **My own items** lists the items you have added.
2. **Windows shows this menu** is the menu Windows builds for that target right now, read live from your system. Your changes are marked on it: hidden items are struck through, and renamed or moved items carry a note.
3. **You will get this menu** is a preview of the result once your rules are applied.

Tick **Include Shift-only items** to also see entries that normally appear only while you hold Shift. **Read the menu again** re-reads the menu, which is useful after you install or remove a program.

## Changing an item

Select an item in the middle column. The panel underneath lets you:

- **Hide** it.
- **Rename** it.
- **Move it into a submenu.** Type a name to create a submenu, or pick one you have already made. Several items can share a submenu.
- **Change its position.** Pin it to the top or bottom of the menu, or use the ▲ and ▼ buttons to move it one row at a time. Moving past a separator line puts the item in the next group.

**Undo changes** removes every change you made to the selected item.

A moved item is remembered as sitting just above or below a neighbouring item, not as a row number. It therefore stays beside that neighbour when you hide or add other items. If the neighbour does not exist in some other menu the rule applies to, the item goes to the bottom of that menu.

Items inside a submenu, such as the entries under 7-Zip, can be changed the same way. Expand the submenu and select the item.

## Choosing where a change applies

Each change has a scope, set with **This change applies to**:

| Scope | Example |
|---|---|
| One file type | Only `.txt` files |
| One kind of target | All files, or only folders, or only the desktop |
| Only several at once | Only when several files, or several folders, are selected together |
| Everything | Everything you right-click |

To make a change that applies only when several things are selected, choose **Several files** or **Several folders** on the left first. The narrower scopes are offered there.

New changes start with the scope you used last. When rules overlap, the narrower one wins: a rule for `.txt` files overrides a rule for everything.

Rules match items by their title. If two programs add items with the same title, a rule affects both.

## Adding your own items

Under **My own items**, choose **Add…** and fill in:

- **Name shown in the menu.**
- **Program to run.** Type a path or use **Browse…**. A program on your PATH, such as `notepad.exe`, works without a full path.
- **What to pass to it.** `%1` stands for the path of whatever you right-clicked. Keep the quotes around it so paths with spaces work.
- **Show it when I right-click.** Any file, files of one type, a folder, a drive, empty space in a folder, empty space on the desktop, or anything.
- **Position** and, optionally, a **submenu** to put it in.

When several things are selected, the program is run once for each of them.

Select an item in the list and choose **Edit…** or **Remove** to change or delete it. Double-clicking an item also edits it.

## Applying your changes

Changes are saved as you make them, but Explorer does not use them until you choose **Apply to Explorer**.

Applying restarts Explorer, because the engine only reads its rules when Explorer starts. The taskbar disappears for a second or two and comes back, and any open folder windows close. Your other programs are not affected.

The top right of the window shows whether the engine is installed and whether it is using your rules.

## Going back

- **Stop using my rules** returns Nilesoft Shell to its own settings and restarts Explorer.
- To remove the engine, uninstall it: `winget uninstall Nilesoft.Shell`.
- To return to the Windows 11 menu, delete the setting the set-up window added, then sign out and back in:

  ```
  reg delete "HKCU\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}" /f
  ```
- Holding Ctrl and the Windows key while you right-click turns the engine off and shows the classic Windows menu. Holding Ctrl while you right-click turns it back on.

## Where things are kept

| What | Where |
|---|---|
| Your rules | `%APPDATA%\RCConfigurator\profile.json` |
| The config written for the engine | `%LOCALAPPDATA%\RCConfigurator\shell.nss` |
| Sample files the probe right-clicks | `%LOCALAPPDATA%\RCConfigurator\samples` |
| The setting that points the engine at the config | `HKCU\Software\Nilesoft\Shell`, value `config` |
| The classic-menu setting | `HKCU\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32` |

Deleting the first three resets RCConfigurator. RCConfigurator itself writes nothing outside your own user profile; installing Nilesoft Shell is a normal program install.

## Troubleshooting

**The menu in Explorer has not changed.** Choose **Apply to Explorer** again and wait for the taskbar to come back. If it still has not changed, close RCConfigurator and start it again so it re-checks that the engine is registered and running.

**Explorer did not come back after applying.** Press Ctrl+Shift+Esc to open Task Manager, choose **Run new task**, type `explorer` and press Enter.

**I was running RCConfigurator or Nilesoft Shell as administrator to make it work.** That is no longer needed. Earlier betas left you to restart Explorer yourself, and Nilesoft Shell's own tool for that insists on administrator rights.

**"Could not read the menu."** A shell extension may have stalled or crashed while the menu was being read. Choose **Read the menu again**. If one target always fails, a program that adds items to that menu is the likely cause.

**An item is missing from the middle column.** The editor shows the classic menu, the one behind "Show more options" on Windows 11. Items that exist only in the Windows 11 menu do not appear, and the engine replaces that menu anyway.

**A rule does not match on other files.** Some titles include the name of what you clicked, such as `Add to "report.zip"`. The editor matches those by the part before the name. If a title is built some other way, the rule may only match the sample file.
