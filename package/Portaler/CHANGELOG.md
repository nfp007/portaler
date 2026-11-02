# Changelog

## 1.0.10
- Updated mod description and README introduction to cover all current features.

## 1.0.9
- "Back to" can be dragged too, and so can the whole portal list (new title "All portals"): any block can go above or below all portals.
- While dragging, the lifted title says where it will land ("drop: before Favorites", "drop: at the end").

## 1.0.8
- "You are here" is now greyed out instead of red; "Back to" is golden and the newest one glows.
- "Back to" keeps your last 3 departure portals: **show 1 2 3** in its title chooses how many are listed.
- "Last built": **show 1-5** in its title (default 3).
- New group **Players' last trips**: the last portal each other player travelled to, shared through the server. **Players: n of m** opens a window to choose whom you follow (personal).
- Favorites, Players' last trips and Last built can be dragged by the handle left of their title into any order (per character).
- The server DLL must be updated too.

## 1.0.7
- Map preview reworked: close portals (up to 2 km) are framed together with extra room around the target; far targets are shown directly instead of a fast flight across the map.
- Calm glide (1 s, soft start and stop) to the preview and back.
- The preview stays 10 s after the mouse leaves the list.
- New settings `HoverMaxFitMeters`, `HoverMoveSeconds`; `HoverReturnDelay` replaced by `HoverStaySeconds`.

## 1.0.6
- Colours match Valheim's own panels: warm medium-brown wood instead of almost black. (A colour correction made everything far too dark; it is removed.)

## 1.0.5
- New screenshots on the mod page: the map with the fiery line and big category badges, and the category filter.

## 1.0.4
- While choosing a destination, portal pins, their names and the fiery line are drawn above all other pins and labels (they were covered by other mods' pins and names).
- Portal pins always at full brightness.

## 1.0.3
- Category badge on the map pin 20% bigger.
- Fixed: the previewed pin got small again right away. It now stays big as long as the fiery line is shown.
- Pin sizes are applied every frame, so hovering a pin on the map reacts smoothly.

## 1.0.2
- Category icon back in the lower right corner of the map pin, as a big round badge.
- Steady pin sizes: no more pulsing. The previewed portal's pin is bigger and stays big; a pin under the mouse grows a little.
- New settings: `PinSizePreview`, `PinSizeHover`.

## 1.0.1
- Category icon now sits big inside the portal ring on the map (was too small to see).
- Map preview: a soft fiery line flows from your portal to the previewed one.
- Map preview stays 5 seconds (was 0.25) before gliding back, and does not glide back at all if you move or zoom the map yourself.
- New settings: `HoverReturnDelay`, `HoverLine`.
- README: every feature says what is shared with everybody and what is personal.

## 1.0.0
- First stable release.
- Hover a name: the map glides to that portal and its pin glows (delay, zoom and return configurable).
- Categories: press T over a name to choose Base, Mine, Farm, Trader, Boss, Danger, Dungeon or Explore. Icon in the list and as a badge on the map pin, shared with everybody.
- Category filter: one click on an icon shows only that category.
- Favorites: drag them into your own order (per character); new favorites go to the end.
- Configurable keys: `RenameKey` (E) and `CategoryKey` (T).

## 0.5.3
- Mod page: three smaller screenshots (search, distance sorting, biome sorting) under the main one.

## 0.5.2
- Fixed: the "Back to" row did not show the portal name.
- Mod page links to the GitHub repository; new screenshots.

## 0.5.1
- New mod description and README: clearer intro, features in order of importance.

## 0.5.0
- Search box: click and type to filter the list by name; matches shown in gold. Esc or x clears it.
- "Back to" row: one click returns you to the portal you last travelled from (per character).

## 0.4.1
- Click a group title (biome, Favorites, Last built) to fold or unfold it. Saved per character, only on your PC.

## 0.4.0
- New server setting `VanillaRestrictions` (default off): use the game's own portal rules (item restrictions, "no portals" world modifiers).
- Fixed: the mouse wheel over the list also zoomed the map for a moment.
- Fixed: dragging the list's scrollbar also dragged the map underneath.

## 0.3.2
- Clearer help text in the list: right click a name to favorite it.
- New README with feature list, controls, configuration and installation.

## 0.3.1
- Fixed: portal names changed from the list were lost after restarting the world.

## 0.3.0
- Shared favorites: right click once = mine (silver star), twice = shared with everybody (bigger glowing gold star), third time = off.
- Shared favorites are listed first.

## 0.2.3
- Fixed: typing in the rename box did not work.

## 0.2.2
- Rename portals from the list: hover a name and press E.

## 0.2.1
- No more double icons: other mods' pins on a portal are hidden.

## 0.2.0
- Favorites, "Last built" and sorting are kept across restarts and mod updates.
- Server data is stored next to the world save.
- Released by nfp007.

## 0.1.3
- Fixed washed-out colours in game.
- Sorting by Biome, Name or Distance, each reversible. Distance shown in Distance mode.

## 0.1.2
- Wooden Valheim-style panel and name boards.
- New mod icon and fiery ring map pin with see-through centre.
- The portal you stand in stays in the list, marked "you are here".

## 0.1.1
- Bigger text, solid panel wide enough for 25-character names.
- Favorites (right click) and "Last built" group.
- Scrolling the list no longer zooms the map.

## 0.1.0
- First version: portal list when entering a portal, click a name or map pin to travel, automatic biome names, server sync.
