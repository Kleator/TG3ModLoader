# Changelog

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
