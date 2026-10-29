# Portaler

**Every portal connects to every portal.** Step into any portal and the map opens with a list of all portals on the server. Pick a name or click a pin and you're there. There's no pairing and no tag matching, and by default no item restrictions.

Built for servers that play together: **favorites can be personal or shared with everyone**, the newest portals show up for all players, and new portals name themselves after their biome (`BF-007`, `MO-012`). Find any portal in seconds with **search, sorting and foldable biome groups**, rename portals right from the list, and get home with the **Back** button.

Everything is synced through the server and kept with the world save, so it survives restarts and mod updates.

![Portaler next to the map](https://raw.githubusercontent.com/nfp007/portaler/main/docs/overview.jpg)

Source code, issues and ideas: **[github.com/nfp007/portaler](https://github.com/nfp007/portaler)**

## Features

**1. Travel from any portal to any portal**
- Walk into a portal: the big map opens with the portal list on the left.
- Click a name in the list, or a portal pin on the map, to travel.
- Vanilla name pairing is switched off, so every portal is always active.
- The portal you stand in stays in the list, marked in red with "you are here", and cannot be selected.

**2. Personal & shared favorites**
- Right click a name: **mine** (silver star, only for this character), then **shared** (bigger glowing gold star, visible to the whole server), then off.
- Favorites are listed on top, shared ones first.
- Anyone can add or remove a shared favorite. Everybody has the same rights.

**3. Search**
- Click the search box and type: only portals whose name contains the text are shown (any case), matching letters in gold.
- While typing, the game ignores your keys (no walking, the map stays open). Esc or x clears the search.

**4. Sorting & foldable groups**
- **Biome** (default): grouped by biome, Deep North first. Click again for Meadows first.
- **Name**: one list A to Z, or Z to A.
- **Distance**: nearest first (or farthest first), with the distance shown next to each name.
- Click a group title (a biome, Favorites, Last built) to fold or unfold it.
- Sort choice and folded groups are remembered (folded groups per character).

**5. "Back to" and "Last built"**
- **Back to**: the portal you last travelled from, at the top of the list. One click takes you back. Remembered per character.
- **Last built**: the 3 newest portals built by anyone on the server.

**6. Automatic names & rename from the list**
- New portals are named after their biome plus a number, for example `BF-007` or `MO-012`.
- Numbers keep counting up per biome and are never reused.
- Portals you name yourself, and existing portals, keep their names.
- Hover a name in the list and press **E** to rename that portal from anywhere in the world (up to 25 characters).

**7. Map pins**
- Every portal gets a fiery ring pin with its name on the map.
- Pins from other mods (for example automatic pin mods) that sit on a portal are hidden, so each portal shows just one icon.

**8. Server synced**
- New, renamed and destroyed portals show up for everybody within seconds.
- Favorites, "Last built" and name counters are stored on the server, next to the world save, and survive restarts and mod updates.

**9. Optional vanilla restrictions**
- By default every item can travel, metals included.
- Prefer the game's own rules? Switch on `VanillaRestrictions` on the server (see Configuration).

<details>
<summary><b>More screenshots</b> (click to show)</summary>

**Search**: type part of a name, matches in gold

![Search](https://raw.githubusercontent.com/nfp007/portaler/main/docs/search.jpg)

**Sorted by distance**, with "Back to" and favorites on top

![Sorted by distance](https://raw.githubusercontent.com/nfp007/portaler/main/docs/sort-distance.jpg)

**Sorted by biome**, with folded groups

![Sorted by biome](https://raw.githubusercontent.com/nfp007/portaler/main/docs/sort-biome.jpg)

</details>

<details>
<summary><b>Controls</b> (click to show)</summary>

| Action | How |
|---|---|
| Open the portal list | Walk into any portal |
| Travel | Left click a name in the list, or a portal pin on the map |
| Favorite: mine / shared / off | Right click a name (or click its star) |
| Rename a portal | Hover its name and press **E**, then Enter to save or Esc to cancel |
| Search | Click the search box, type part of a name; Esc or x clears it |
| Go back | Click "Back to: ..." at the top of the list |
| Fold / unfold a group | Click the group title (biome, Favorites, Last built) |
| Change sorting | Click **Biome**, **Name** or **Distance** at the top of the list |
| Reverse sorting | Click the active sort button again |
| Scroll the list | Mouse wheel over the list |
| Close | **Close** button, Esc, or walk out of the portal |

</details>

<details>
<summary><b>Configuration</b> (click to show)</summary>

Config file: `BepInEx/config/nfp007.Portaler.cfg` (created on first start). Only the **server's** (or the host's) value counts; it is sent to all players.

| Setting | Default | What it does |
|---|---|---|
| `[Server] VanillaRestrictions` | `false` | `false` = no restrictions, everything travels through every portal. `true` = the game's own rules: no metals/ores through the wooden portal, and the world modifiers "no portals" and "no portals during boss fights" apply. |

Everything else is set from inside the game:

| What | Where it is stored |
|---|---|
| Sort choice (per PC), folded groups and "Back to" portal (per character) | `Portaler.prefs` in Valheim's data folder (`AppData\LocalLow\IronGate\Valheim`) |
| Favorites, "Last built", name counters (per world) | `<world>.portaler.txt` next to the world save on the server |

- Keep `<world>.portaler.txt` together with your world backups.
- Deleting it resets favorites and "Last built". Numbering then continues from the highest existing portal number.

</details>

<details>
<summary><b>Installation</b> (click to show)</summary>

Portaler must be installed on the **server and on every player**, with the same version everywhere.

**Players**
- Install with a mod manager (Gale, r2modman, Thunderstore Mod Manager), or
- manually: copy `Portaler.dll` to `BepInEx/plugins/Portaler/`.

**Dedicated server**
- Copy `Portaler.dll` to `BepInEx/plugins/Portaler/` on the server and restart it.

**Notes**
- Do not run it together with other portal-list mods (for example PotalMap).
- Single player and self-hosted worlds work too: your game is then the server.

</details>

<details>
<summary><b>Dependencies</b> (click to show)</summary>

- [BepInExPack for Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/) 5.4.2351 or newer

</details>

<details>
<summary><b>Source code & feedback</b> (click to show)</summary>

- Source code: [github.com/nfp007/portaler](https://github.com/nfp007/portaler)
- Bugs and ideas: [open an issue](https://github.com/nfp007/portaler/issues)
- Building from source: see [BUILDING.md](https://github.com/nfp007/portaler/blob/main/BUILDING.md)
- License: MIT

</details>
