# Portaler – working rules

Valheim BepInEx mod by **nfp007**: every portal connects to every portal (list + map, favorites, categories, search, sorting).
The same DLL runs on the dedicated server and on every client.

## Project layout
- `src/` – C# source (net48, BepInEx 5, HarmonyX). Plugin id `nfp007.Portaler`.
- `art/` – images embedded in the DLL (`cat_*.png`, `pin_*.png`) plus `icon.png` (mod icon, gold "P" in a fiery ring).
  `tools/make_art.py` regenerates `cat_*` and `pin_*` from `art/pin64.png` (run it after changing icons).
- `package/Portaler/` – Thunderstore layout: `manifest.json`, `README.md`, `CHANGELOG.md`, `icon.png`, `plugins/Portaler.dll`.
- `docs/` – screenshots used by the README (linked via `raw.githubusercontent.com/nfp007/portaler/main/docs/...`).
- `lib/` – game + BepInEx DLLs for building. **Never commit** (copyrighted; in `.gitignore`).

## Build
- `dotnet build -c Release` → `bin/Release/Portaler.dll` (offline: `nuget.config` clears sources, references point to `lib/`).
- After building, check the version with the assembly metadata before packaging.

## Versioning and release (every change)
1. Bump the version in **three** places: `src/Plugin.cs` (`Version`), `Portaler.csproj` (`<Version>`), `package/Portaler/manifest.json` (`version_number`).
2. Add a `CHANGELOG.md` entry at the top (copy to `package/Portaler/CHANGELOG.md`). Keep `README.md` and `package/Portaler/README.md` identical.
3. **One zip** per version: `Portaler-<ver>.zip` = contents of `package/Portaler/` (not the folder), manifest **without** `author`
   (Thunderstore takes the author from the team). The same zip is used for Gale "Import local mod", Thunderstore and Hexium.
4. Thunderstore/Hexium never accept the same version twice; versions not uploaded yet may be rewritten (ask first).
5. Copy changed files to the user's PC folder `G:\Valheim\Portaler`: source files, the zip into `package\`, the DLL into `build\`.
   The `package\Portaler\` template folder is **not** copied to the PC (it only lives in the repo). `lib\` on the PC holds the build references.
6. Commit as `nfp007 <nfp007@users.noreply.github.com>` and push to `github.com/nfp007/portaler` (`main`). Force push only when asked.
7. Tell the user whether the **server** DLL must be updated too (anything touching `PortalServer`, RPCs or the list format).

## Design rules
- UI matches Valheim: dark wood boards with light, uneven frames (`Art.Wood`), orange headers, gold accents.
  Generated IMGUI textures must go through `Art.Px` (Valheim renders in linear colour space; otherwise colours look washed out).
- **Mock-up first**: for new visual features draw a preview image (Python/PIL in the scratchpad) and get approval before implementing.
- Equal rights: no admin-only features. Anyone may rename, set categories or change shared favorites.
- Every feature in the README states what is **🌐 shared** (server, everybody) and what is **👤 personal** (per character / per PC).
- Shared data lives on the server in `<world>.portaler.txt` next to the world save. Portals are identified by **position** (ZDOIDs change on every world load).
- Personal settings: `Portaler.prefs` in Valheim's data folder (`Application.persistentDataPath`); configurable options in `BepInEx/config/nfp007.Portaler.cfg`.
- Any portal name change made by the server must call `ZDOMan.instance.SetDirtyPortals()`, or the world save drops it.
- Keys shown in the UI come from the config (`RenameKey`, `CategoryKey`), never hard-coded text.
- While typing or a Portaler window is open, game keys are blocked through `Minimap.InTextInput` (`PortalUI.BlocksKeys`).

## Working with the user
- The user tests in-game and sends screenshots; nothing is "done" until confirmed there. Say clearly what was not tested.
- Prefer short options/variants to choose from; ask before large or irreversible changes.
- Keep messages short: what changed, which files to install where, what to check.
