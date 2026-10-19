TG3 ModLoader - play several mods for The Guild 3 together
==========================================================
Updates, help and source code: https://github.com/Kleator/TG3ModLoader

WHAT IT DOES
------------
The official Mod Launcher of The Guild 3 can only start ONE gameplay mod at a time: every gameplay
mod contains all of the game's roughly 2000 script files, and the launcher stops as soon as two
mods contain the same file ("error copying mod").

The ModLoader looks at what each of your mods really changes compared to the original game and
combines all these changes into one mod called "MergedMod". If two mods change different things in
the same file, both changes are kept. The official Mod Launcher then starts this one mod.

QUICK START
-----------
1. Subscribe to mods in the Steam Workshop as usual.
2. The TG3ModLoader folder belongs into ...\steamapps\workshop\content\311260\ (next to the
   numbered folders of your mods). Start TG3ModLoader.exe from there.
3. "Your mods": tick the mods you want to play with and drag them into order (or use the arrows
   that appear when you point at a mod). The mod at the TOP wins when two mods change the same
   setting.
4. Click "Combine & Play". The ModLoader combines your mods, prepares the official Mod Launcher and
   opens it - just click "Play" there.
   Maps don't need combining: switch them on in the Mod Launcher as usual.
5. Next time the big button simply says "Play" if nothing has changed.

THE WINDOW
----------
* The box at the top always tells you what to do next:
    "Ready to play" (green)          everything is combined
    "Please combine again" (yellow)  your selection, the order, a conflict choice or a mod update
                                     changed since the last combine - click "Combine & Play"
* "Your mods"  all mods with a short summary each: how many game files it changes and how many
               conflicts it wins. Right-click a mod to open its folder or its Steam Workshop page,
               or to move it to the top or bottom.
* "Conflicts"  settings that more than one of your mods change (see below).
* "Activity"   what the ModLoader did, step by step (handy when you ask for help).
* "Report"     opens a page that sums up your last combine: your mods, the conflicts and notes.
* "More"       test the game, rescan the mods, open folders, remove the ModLoader from the Mod
               Launcher, this guide and the project page.
* Light or dark: the window follows the light/dark mode of Windows automatically.
* Keyboard: Ctrl+1/2/3 switch tabs, Space ticks the selected mod, Alt+Up/Down moves it,
  F1 shows help, F5 rescans the mods.

CONFLICTS - WHEN TWO MODS CHANGE THE SAME SETTING
-------------------------------------------------
Most changes of different mods don't get in each other's way. A conflict only happens when two
mods change the SAME setting to different values - for example one mod sets a price to 100 and
another one to 200. The game can only use one value:
* By default the value of the mod higher in your list is used.
* On the "Conflicts" tab you can pick the value yourself with one click: the value of another mod,
  the value of the original game, or your own value. Then combine again.
* Only that one setting is affected - all other changes of both mods are still used.

GOOD TO KNOW
------------
* Combine again after a mod was updated in the Steam Workshop - the ModLoader tells you when.
* Savegames: the game shows a mod id in the main menu ("Mod: XXXXXXXX") and keeps the savegames of
  each mod id in its own folder (below "Saved Games\Guild3\<SteamID>\"). The same mods in the same
  order always give the same id. If you change your selection, your older savegames are in the
  folder of the previous id.
* Multiplayer: all players must combine the same mods in the same order (same mod id in the main
  menu).
* If a mod contains a mistake of its own (a syntax error), the ModLoader points it out. Such
  mistakes are there without the ModLoader too; the ModLoader does not change them.
* "More > Test the game" starts the game with your combined mods, waits for the main menu, checks
  for script errors and crashes and closes the game again.

WHAT CAN BE COMBINED
--------------------
* Game scripts (.oc, .oo, .inc) - down to single values, list entries (e.g. unlocks), objects,
  actions and functions
* Texts in all languages (.loo) - all new texts are kept
* Screen layouts (.Slate) and other text files - line by line
* Pictures, sounds, models - taken over; only if several mods replace the SAME file does the order
  of your list decide
* New files of a mod (new actions, items, icons ...) are simply added
* Mods with a map AND gameplay changes - the gameplay changes are combined, the maps are copied
  along
* Map-only mods don't need the ModLoader - switch them on in the Mod Launcher; they work together
  with "MergedMod"

TROUBLESHOOTING
---------------
* The Mod Launcher says "Error copying / conflicting mod": a gameplay mod is switched on in the
  launcher next to "MergedMod". Tick it in the ModLoader and combine again (the ModLoader then
  switches it off in the launcher), or switch it off in the launcher yourself.
* The game crashes while loading: use "More > Test the game" and read the notes in the report. A mod
  with a broken script, or file paths with 260 or more characters (very deep folders), make the
  game crash - the ModLoader warns about both.
* A change of a mod is missing in the game: combine again after Workshop updates, and look at the
  "Conflicts" tab - another mod may change the same setting.

FILES
-----
MergedMod\                 the combined mod (switched on in the Mod Launcher)
ModLoader.config.json      your selection, order and conflict choices
ModLoader_Report.html      the report of the last combine
build_manifest.json        details of the last combine
%LOCALAPPDATA%\Guild3ModLauncher\mods.yml.modloader_backup
                           backup of the Mod Launcher settings before the first change

UNINSTALL
---------
Choose "More > Remove from the Mod Launcher" (or run "TG3ModLoader.exe unregister"). This removes
"MergedMod" from the official Mod Launcher and switches your mods on there again (only one gameplay
mod, because the launcher itself cannot load more). Then delete the TG3ModLoader folder.

COMMAND LINE (for advanced users)
---------------------------------
TG3ModLoader.exe build [--mods id1,id2,...] [--all] [--import-launcher] [--no-launcher] [--launch]
                 (--mods: the first id wins conflicts)
TG3ModLoader.exe status | import-launcher | selftest | testrun [--timeout 600] | launch
TG3ModLoader.exe report [--out file.html]                                (write the report again)
TG3ModLoader.exe resolve "<file>::<location>" <modId | =value | clear>   (decide a conflict)
TG3ModLoader.exe unregister                                              (remove from the launcher)
