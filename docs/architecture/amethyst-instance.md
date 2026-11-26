---
updated: 2026-11-26
covers: [src/housecarl-core/AmethystInstance.cs, src/housecarl-core/DiskPath.cs, src/housecarl-core/GamePrefix.cs, src/housecarl-core/Mo2SkipRules.cs, src/housecarl-mcp/OutputLocations.cs, src/housecarl-mcp/Program.cs, src/housecarl-mcp/WriteSentences.cs, scripts/build-plugin.sh]
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

## Wording, install and packaging

- Messages name the manager the instance is (`Mo2InstancePaths.Source`, `Mo2Roots.OverwriteOutput`, or `WriteSentences.IsAmethystOutput` for a result's folder). The MO2 string is the other arm of each branch and stays as it is. Text with no instance in reach (the server `instructions`, the not-configured prompt) states both.
- The server `instructions` sit at Claude Code's 2,048-character cut (`ServerInstructionsTests`). They carry no tool recap, since `tools/list` does; adding text means cutting text.
- `mo2_instance_dir` and `amethyst_instance_dir` are both optional userConfig. The Linux `.mcp.json` passes them as `HouseCarl__Mo2InstanceDir` and `HouseCarl__AmethystInstanceDir`; `SetupHouseCarl` takes saved user config, then explicit paths, then `Mo2InstanceDir`, then `AmethystInstanceDir`.
- **Instance and profile.** The Amethyst instance is the game's staging root (the folder holding `profiles/`). Like MO2's `selected_profile`, the profile is not configured: `deploy_state.json` `last_active_profile` names it, and `LoadOrderService.RederiveIfIniChanged` stats that file (`Mo2Instance.IniPath`) before each tool call and rebuilds the load order when the active profile moves. A profile folder (or a root with `profile=`) pins that profile and nothing is followed.
- `scripts/build-plugin.sh` assembles the same `dist/` tree as `build-plugin.ps1` with a linux-x64 framework-dependent server run as `dotnet housecarl-mcp.dll`. It skips the Windows setup exe, START-HERE note, zip and notices regeneration.
