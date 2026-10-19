# Changelog

## 1.3.0 - 2026-10-19

- Ready for the Steam Workshop: a copy subscribed in the Workshop keeps its settings and the combined
  mod next to it in `...\workshop\content\311260\TG3ModLoader\`, because Steam replaces the item's
  own folder on every update and deletes it on unsubscribe. A manual installation and a Workshop copy
  share their settings.
- The ModLoader's own Workshop item is never listed as a mod, and combining switches it off in the
  Mod Launcher (it is a program, not a game mod).
- Desktop shortcut: offered once on the first start of a Workshop copy, and available any time under
  **More > Create a desktop shortcut**.
- The readme and the license are shipped as `TG3ModLoader-README.txt` and `TG3ModLoader-LICENSE.txt`,
  so they can never collide with files of other mods in the Mod Launcher.
- Fixed: the Workshop folder next to the program was only found through the Steam library list.
- No change to the merge logic - existing combinations stay valid.

## 1.2.0 - 2026-10-19

- New, modern window made for players:
  - a status box that always says what to do next (*Welcome*, *Please combine again*,
    *Ready to play*, ...),
  - the mods as cards: tick them, drag them into order, see per mod how many game files it changes
    and how many conflicts it wins; right-click for its folder or Steam Workshop page,
  - a **Conflicts** tab where one click picks the value that is used: another mod, the original
    game's value or your own value; changed choices are marked until the next combine,
  - an **Activity** tab with the detailed log,
  - one main button: **Combine & Play**, or **Play** when everything is up to date.
- Light and dark mode: follows the Windows setting automatically, also while the ModLoader is open
  (including the title bar).
- New report: a clear summary of the last combine (mods, conflicts grouped by game area with
  readable names, notes in plain words); the technical file list is folded away. Light and dark
  mode as well.
- Fixed: **Report** opened the report in the layout of the version that made the last combine. The
  report is now written again from the stored data every time (and once after an update).
- The combined mod of another ModLoader copy is no longer offered as a mod, and it is switched off
  in the Mod Launcher when combining (it contains the same files).
- Keyboard and screen reader support for the new controls (Ctrl+1/2/3, Ctrl+Tab, Space,
  Alt+Up/Down, F1, F5).
- New command `report [--out file.html]` writes the report of the last combine again.
- No change to the merge logic - existing combinations stay valid and do not have to be repeated.

## 1.1.2 - 2026-10-19

- Fixed: the Windows taskbar showed a generic program icon instead of the ModLoader icon. The window
  now uses the full multi-size icon and sets it again once it is visible, which the Windows 11
  taskbar needs to pick it up.

## 1.1.1 - 2026-10-19

- Program icon (window, taskbar and `TG3ModLoader.exe`); small sizes show the gear emblem only.
- No change to the merge logic - existing merges stay valid and do not have to be repeated.

## 1.1.0 - 2026-10-19

First public release.

- Merges any number of The Guild 3 mods into one mod (`MergedMod`) that the official Mod Launcher
  loads without "error copying mod" conflicts.
- Structural three-way merge of script files (`.oc`, `.oo`, `.inc`) against the vanilla scripts:
  single values, list entries, objects, actions, `@base/...` includes and functions; additions of
  several mods at the same place are all kept.
- Localization (`.loo`) merged per text key, UI layouts (`.Slate`) and other text files merged line
  by line, binary files by priority; mods with maps and scripts are supported.
- The mod at the top of the list has the highest priority. Real conflicts (the same value changed
  differently) are listed in the GUI and the HTML report and can be decided per location: another
  mod, the vanilla value or an own value.
- Built-in verification: every change of every mod is checked to be present in the result.
- Registers the merged mod in the official Mod Launcher (`mods.yml`) and disables the merged mods
  there; "Remove from Mod Launcher" undoes this.
- Test run: starts the game with the merged mod, waits for the main menu and reports script error
  dialogs, `scripts.log` messages and crashes.
- Deterministic, incremental output: the same mods in the same order give the same mod id, so
  savegames stay assigned.
- Warns about syntax errors inside mods and about file paths the game cannot open (260+ characters).
