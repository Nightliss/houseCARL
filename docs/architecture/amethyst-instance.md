---
updated: 2026-11-26
covers: [src/housecarl-core/AmethystInstance.cs, src/housecarl-core/DiskPath.cs, src/housecarl-core/GamePrefix.cs, src/housecarl-core/Mo2SkipRules.cs, src/housecarl-mcp/OutputLocations.cs]
---
# The Amethyst instance and Linux paths

## What it is

An Amethyst Mod Manager profile folder is a second source for the roots `Mo2Instance` derives; everything after that
(`Mo2LoadOrder`, `AssetResolver`, the services) is the MO2 code path. `Mo2Instance` hands any folder holding
`profile_state.json` beside `modlist.txt` to `AmethystInstance`.

## Contracts

- **Profile roots.** A profile with `profile_specific_mods` keeps `mods`, `overwrite` and `Root_Folder` inside itself, any other beside `profiles`. `profile_settings.game_path` beats the game config's. The default profile never overrides.
- **Order.** `modlist.txt` top is highest priority, `*` marks an always-on mod; overwrite beats every mod, game Data is the floor.
- **Hidden files.** Amethyst honours no `.mohidden` suffix. It hides per mod what `excluded_mod_files` switches off and `root_mod_files` routes to the game root, and `meta.ini` and its other bookkeeping names at any depth.
- **Profile INIs.** The profile's `ini files` folder is the game's INI folder only when `profile_ini_files` is set; otherwise the prefix's My Games.
- **Writes.** New plugins and riders go to the profile's overwrite folder, never a new mod folder: Amethyst's refresh and plugin sync scan overwrite as a source of their own (`Utils/filegraph/service.py` `refresh_changed`, `Utils/plugins/sync.py` `sync_overwrite_plugins`), while a folder written into staging is unknown to its catalog and modlist, and a profile's `mods` can be a link into a Wabbajack store. `into=` has no folder to name there and is refused.
- **Not modelled.** `mod_strip_prefixes`, `rootFolder=true` mods, the per-game deploy blacklist and handler routing rules are not read; a file those move still resolves where it sits in the mod.

## Linux paths

- A Data path is `\`-separated and any case. Join it onto disk only through `LooseListings.OnDisk` / `DiskPath.Join`, which match each segment case-insensitively off Windows; take its parts with `DiskPath.FileName` / `DirectoryName`, never `Path.*`.
- Mutagen spells asset paths with the OS separator; `DiskPath.Logical` restores `\`.
- Where the game's Documents live is `GamePrefix`: the Proton prefix from `HOUSECARL_PROTON_PREFIX`, the instance's own, or the Steam library beside the game; `HOUSECARL_MY_GAMES` overrides the folder.
- `source=` on `place`, and the file-or-Data-path arguments of `nif` and `decompile`, name a disk file when drive or UNC rooted, or when a leading `/` starts at a folder that exists at the filesystem root (`DiskPath.NamesDiskPath`); anything else, `/meshes/x.nif` included, is a Data path. Windows keeps `Path.IsPathFullyQualified`.
- Two entries that differ only in case in one folder read as one.
