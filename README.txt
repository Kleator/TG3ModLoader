TG3 ModLoader - use several mods for The Guild 3 at the same time
=================================================================
Source code, updates and issues: https://github.com/Kleator/TG3ModLoader

WHY?
----
The official Mod Launcher copies all enabled mods into one folder, one after another, and stops at
the first duplicate file name ("copy error / conflict with mod"). Since every script mod ships all
of the roughly 2000 script files of the game, you can only ever use ONE script mod that way.

The TG3 ModLoader compares every mod with the original scripts of the game (vanilla), works out
what each mod actually changes, and combines the changes of all selected mods into ONE mod
("MergedMod"). If two mods change different values in the same file (for example both edit
GameplayDefault.oc), both changes are kept completely.

SUPPORTED MOD CONTENT
---------------------
* Scripts (.oc, .oo, .inc)       - structural three-way merge down to single values, list entries
                                   (e.g. unlocks), objects, actions, @base/... includes and functions
* Localization (.loo)            - merged per text key (all new texts are kept)
* UI layouts (.Slate) and other  - line-based three-way merge
  text files
* Graphics, sounds, models etc.  - taken over; only if several mods replace the SAME file does
                                   the priority decide
* New files (new actions, items, icons ...) are simply added
* Mods with a map AND scripts    - the scripts are merged, the maps are copied along
* Map-only mods                  - do not need the ModLoader; enable them in the Mod Launcher as
                                   usual - they work together with "MergedMod"

HOW TO USE
----------
1. Subscribe to mods in the Steam Workshop as usual.
2. Start TG3ModLoader.exe (located in ...\steamapps\workshop\content\311260\TG3ModLoader).
3. Check the mods you want. Set the priority with "Move up" / "Move down": the mod at the TOP of
   the list has the highest priority. The priority ONLY matters when two mods change the same value
   differently.
4. Click "Merge & start Mod Launcher".
   The ModLoader registers "MergedMod" in the official Mod Launcher, enables it and disables the
   individually merged mods there (otherwise the launcher would report a conflict again).
5. Enable map mods in the Mod Launcher if you like and click "Play".

Optional: "Test run" starts the game with the merged mod, waits for the main menu, checks for
script errors and crashes, and closes the game again.

IMPORTANT
---------
* Merge again after every Workshop update of a mod (or when you add mods). The ModLoader shows at
  the bottom of the window when this is necessary.
* Real conflicts: when several mods change the SAME value differently (e.g. one mod sets a limit to
  100, another one to 200), only this one value is affected - all other changes of both mods are
  kept. By default the mod further up in the list wins. All conflicts are listed on the
  "Conflicts" tab and in the report (ModLoader_Report.html) with the vanilla value and the value of
  every mod. For every location you can choose a different mod, keep the vanilla value or enter
  your own value (double-click the conflict), then merge again.
* The report also contains an automatic verification: for every mod it checks that each of its
  changes is present in the result (or was deliberately replaced because of a conflict).
* The game shows a mod id in the main menu ("Mod: XXXXXXXX") and stores savegames per mod id
  (folders below "Saved Games\Guild3\<SteamID>\"). The id is calculated from the content of the
  scripts: the same mods in the same order always produce exactly the same files and therefore the
  same id. A different selection results in a different id - older savegames are then in the folder
  of the previous id. For multiplayer, all players must merge the same mods in the same order (same
  mod id in the main menu).
* If a mod itself contains a syntax error, the ModLoader reports it as a note. Such errors come from
  the mod (they exist without the ModLoader too); the ModLoader does not change them.

TROUBLESHOOTING
---------------
* The Mod Launcher reports "Error copying / conflicting mod": a script mod is enabled in the
  launcher in addition to "MergedMod". Select it in the ModLoader and merge again (the ModLoader then
  disables it in the launcher), or disable it in the launcher.
* The game crashes while loading: use "Test run" and read the notes of the last merge. A mod with a
  broken script, or file paths with 260 or more characters (very deep folders), make the game crash -
  the ModLoader warns about both.
* Changes of a mod are missing in the game: merge again after Workshop updates; check the
  "Conflicts" tab - another mod may change the same value.

COMMAND LINE
------------
TG3ModLoader.exe build [--mods id1,id2,...] [--all] [--import-launcher] [--no-launcher] [--launch]
                 (--mods: first id = highest priority)
TG3ModLoader.exe status | import-launcher | selftest | testrun [--timeout 600] | launch
TG3ModLoader.exe resolve "<file>::<location>" <modId | =value | clear>   (decide a conflict)
TG3ModLoader.exe unregister                                              (remove from the launcher)

FILES
-----
MergedMod\                 the merged mod (registered in the Mod Launcher)
ModLoader.config.json      selection, order, manual conflict decisions
ModLoader_Report.html      report of the last merge
build_manifest.json        state of the last merge
%LOCALAPPDATA%\Guild3ModLauncher\mods.yml.modloader_backup
                           backup of the launcher configuration before the first change

UNINSTALL
---------
Click "Remove from Mod Launcher" in the ModLoader (or run "TG3ModLoader.exe unregister"). This
removes "MergedMod" from the official Mod Launcher and re-enables the merged mods there (only one
script mod, because the launcher itself cannot load more). Then delete the TG3ModLoader folder.
