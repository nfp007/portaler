# Performance notes

Working notes for optimising Portaler: how to measure, what was measured, what is still open.

## How to measure

- **Developer build** (log always on): `dotnet build -c Release -p:Dev=true`. It defines `PERF`, logs
  "developer build, performance log on" at start and is packaged as `Portaler-<ver>-dev.zip`
  (manifest description starts with "DEVELOPER BUILD"). Never upload a dev zip.
- **Any normal build:** set `[Debug] PerfLog = true` in `BepInEx/config/nfp007.Portaler.cfg`.
- Code: `src/Perf.cs`. Sections are measured with `Perf.Begin()` / `Perf.End(Perf.S.X, t)`
  (time via `Stopwatch`, memory via `Profiler.GetMonoUsedSizeLong`).
- Output in `BepInEx/LogOutput.log` of the Gale profile (the user plays profile `00`), lines start with `[Perf]`:
  - every 10 s: game frame avg/max, `list open %`, Portaler avg/max ms per frame, allocated KB per frame,
    OnGUI calls per frame, then per section `avg/max` ms per frame. `*` = nested inside `cliTick` or `gui`
    (not added to the total again).
  - after every trip: `moved after`, `area ready after`, `arrived after`, worst frame, quick arrival on/off.
  - once after spawn: `Prewarm after spawn: N ms`.

| Section | What |
|---|---|
| `srvTick` | `PortalServer.Tick` (scan every 2 s) |
| `cliTick` | `PortalClient.Tick` (map preview, rename, pins) |
| `pinScan*` | scan of other mods' pins sitting on portals (every 0.5 s) |
| `pinRefresh*` | rebuilding Portaler's own pins after a new list |
| `hide` | hiding other mods' pins (every frame + `UpdatePins` postfix) |
| `scale` | pin sizes / on-top canvases (every frame) |
| `gui` | `PortalUI.Draw` (all OnGUI calls) |
| `line*` | fiery preview line |
| `click*`, `mapClose*`, `tpStart*` | destination click: Portaler's own work, closing the map, the game's `Player.TeleportTo` (other mods' patches on it included) |

## Measurements (Nov 2026, user's PC, local world "fam", 86 portals, ~145 map pins)

| Build | First map opening | List open: Portaler per frame | Garbage per frame | Trips (arrived after) |
|---|---|---|---|---|
| 1.0.12 dev, round 1 | 2,166 ms freeze (11.5 MB) | 0.3-0.9 ms | 7-30 KB | 8.8 / 10.2 / 8.9 / 10.5 s |
| 1.0.12 dev, round 2 | 624 ms (10 MB) | 0.8-1.9 ms (more groups open) | 20-91 KB | 8.6 / 10.6 s |
| 1.0.13 dev (= release 1.0.13) | 184 ms (8.5 MB); prewarm at spawn 111 ms | 1.7-1.9 ms | 56-78 KB | 5.7 / 4.3 / 4.3 / 9.4 s (quick arrival) |

Game frames were 10-34 ms, so Portaler with the list open is roughly 5-8 % of a frame; closed it is ~0.004 ms.

Vanilla teleport (from `Player.UpdateTeleport` IL): moves the player when `m_teleportTimer > 2`, ends when
`(timer > 8 || !distant) && ZNetScene.IsAreaReady && ZoneSystem.FindFloor`, fallback at 15 s
(distant: put on `GetSolidHeight + 0.5` at the target). `QuickArrival.cs` only nudges the timer for Portaler trips:
to 2 when the loading screen (`Hud.m_loadingScreen.alpha`) is dark, to 8 when the area is ready.

## Findings

- **Fixed in 1.0.13:** first-open freeze (fonts/styles + big panel texture generated synchronously; now prewarmed at
  spawn, wood textures built on a thread pool via `Art.WoodPixels`), texture cache thrash (`RowTex` was cleared at
  64 entries: 160-280 ms / 80-130 MB spikes; now LRU with 300 entries), list sorted/grouped several times per frame
  (now `PortalUI.GetModel`, rebuilt on change or every 0.5 s, only at a Layout event).
- **Trip freeze 140-320 ms is not Portaler:** it is in `tpStart` = `Player.TeleportTo`, where the user's own mod
  **SmartSave 1.2.0** saves on teleport (`SmartSave:Teleport total=301 ms ... replaceFile=171.8 ... alloc=124 MB`)
  and again on arrival (166 ms). Portaler's own click: 2-5 ms; closing the map (vanilla): 9-11 ms.
- Load time after the move (4-8 s) is the game building the area (frames up to ~1 s), not Portaler.

## Open ideas (not done yet)

1. **Draw only visible rows.** The list still lays out and draws all 87 rows every frame, also those scrolled
   out of view. Skip drawing (keep `GUILayoutUtility.GetRect`) for rows outside `_scroll.y .. _scroll.y + view height`
   (view height = last frame's scroll view rect). Expected: list cost down to the ~10-15 visible rows.
2. **Remaining 184 ms on the first opening** after prewarm: find what is still built then (probably the panel
   texture upload `Art.ToTexture`, or rows/pickers built synchronously because `w*h <= SyncPixels`).
3. Garbage per frame (56-78 KB) mostly comes from IMGUI itself (`GUILayoutEntry` per `GetRect`); idea 1 does not
   remove it. A fixed-height manual layout (`GUI.*` with own rects instead of `GUILayout`) inside the scroll view
   would, but is a bigger rewrite.
4. `pinScan` compares every map pin with every portal every 0.5 s (cheap now: ~150 pins). With thousands of pins
   (Cartur's Map Pins on a big world) a spatial grid by map cell would keep it flat.
5. SmartSave (separate mod): the teleport save's `replaceFile` on cloud storage is the slow part.
