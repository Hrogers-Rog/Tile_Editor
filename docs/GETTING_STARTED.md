# Getting Started

The Tile Editor is a desktop application for building Railroader maps: terrain
tiles, track graphs, roads and rivers, scenery, operations, signals, and
crossings. It pairs with an in-game F9 workspace that edits the same data live.

## Requirements

- 64-bit **Python 3.10 or newer**
- `pygame-ce`, `numpy`, `Pillow`, `requests`, `scipy`

```bash
pip install -r requirements.txt
```

## Running

Windows:

```bat
run_editor.bat
```

The launcher finds Python through the `py` launcher, PATH, registry entries, and
common python.org, Conda, and Scoop locations — **Python does not need to be on
PATH**. If you use a portable or unusual install, set `TILE_EDITOR_PYTHON` to the
exact `python.exe` path.

Any platform:

```bash
python -m edit_tiles
```

## The Interface

A two-row navigation bar sits at the top: **row 1 is terrain tools, row 2 is mod
tools.** The minimap is top-right, the status bar is at the bottom, and selection
properties appear top-left when something is selected.

Press **`?`** or **F1** at any time for nine pages of in-app help. Full bindings
are in [KEYBINDS.md](KEYBINDS.md).

## First Session: Editing Track

1. Open an existing map with **Mod → Open Mod Folder**, or choose **New Mod** to
   create one from scratch. Existing packages may use RailLoader
   `Definition.json` or native FUSE `Info.json`.
2. The map shows every track layer, coloured by source file.
3. **Click a node** — its properties appear top-left.
4. **Drag a selected node** to move it. Terrain height is sampled automatically on
   release.
5. **Ctrl+click** empty space (or Geo → Add Node) to place a new node.
6. Select a node, click **Connect →**, then Ctrl+click a second node to draw a
   segment.
7. Changes auto-save to `game-graph.json` and hot-reload into a running game in
   about half a second.

## Start A Stock-Map Add-on Or A New Map

Choose **Mod → New Mod** in the desktop editor, or **Create New Mod** in the F9
graph chooser.

1. Choose **Native FUSE** for new work. RailLoader Legacy is available for old
   data packages, but native-only controls are disabled in that mode.
2. Choose **Stock-map add-on** when your package changes the existing
   Railroader world.
3. Choose **Standalone map** when you are building a separate world from the
   ground up. Enter the real latitude and longitude of the southwest map origin.
4. The standalone wizard creates `Info.json`, `map.fuse.json`, and
   `Map/Map.json`. The native declaration suppresses the stock world only when
   this map is launched.
5. In the desktop editor, the new `Map` tile folder opens with the package and
   tile generation uses its georeference automatically.
6. When creating through F9, return to the main menu and launch the registered
   map through FUSE before authoring its empty world. F9 cannot replace the live
   stock-map session underneath the player.

Generated, deleted, and undo-restored terrain tiles update the `tiles` list in
`Map.json` automatically. The stock map's historical westward terrain correction
is applied only at the stock origin and never leaks into a new map.

## First Session: Editing Terrain

1. Load tiles — **Load Tiles**, or drag a tile folder onto the window.
2. Press **`F`** to fit them in view.
3. Press **`E`** for edit mode. The cursor becomes a brush.
4. **LMB paints, RMB erases.** `[` and `]` size the brush; `-` and `=` change
   strength.
5. Press **`B`** to cycle brush type — Raise, Flatten, Paint, Smooth, Noise,
   Erode.
6. **Ctrl+S** saves modified tiles. **Ctrl+Z** undoes.

Press **`D`** for diff mode at any point to see which tiles you have changed.

## Display Modes

| Key | Mode |
| --- | --- |
| `H` | Heightmap — elevation contours, dark green low to yellow/white high |
| `V` | Vegetation — dominant biome per pixel (presets 0–7) |
| `W` | Water — white is water, black is land |
| `D` | Diff — tiles modified since the last save |

Vegetation values are density levels, not eight fixed biomes or anonymous paint
numbers. The examples describe common uses; the game's density rules still
exclude plants around track, water, objects, steep slopes, and cut-tree masks.

| ID | Density | Approx. mask | Typical use |
| --- | --- | --- | --- |
| 0 | Full | 100% | Dense forest and maximum plant placement |
| 1 | Very Dense | 86% | Woodland or other very dense cover |
| 2 | Dense | 71% | Trees and brush with small openings |
| 3 | Medium | 57% | Balanced mixed vegetation and open ground |
| 4 | Light | 43% | Grass, shrubs, and scattered trees |
| 5 | Sparse | 29% | Mostly open grass or ground |
| 6 | Minimal | 14% | Pasture, cropland, and lightly planted yards |
| 7 | Clear | 0% | Built-up, bare, snow, or open-water ground |

Saving is atomic and keeps a recoverable tile backup. A successful desktop save
recalculates tile statistics and invalidates every overview/detail/scale render
cache, even when pixels were changed by paste or generation rather than the
brush. The in-game editor invalidates Railroader's terrain cache after a
successful save (or rebuilds it when a new override source was mounted), so
vegetation and water reload from the saved tile instead of reverting to a stale
texture.

`S` toggles hillshade, which adds directional lighting and is the fastest way to
read terrain shape.

## Live Connection To The Game

The `●LIVE`/`OFF` indicator in the nav bar shows the TrackBridge connection.

With `Hrogers.TileEditorBridge` installed in Railroader, edits made in the desktop
editor hot-reload into the running game, and the in-game **F9** workspace edits the
same data back. Track, scenery, splineys, telegraph poles, and terrain tiles
synchronise in both directions.

Dirty-edit ownership locks prevent both sides writing at once. If a reload arrives
while you have unsaved work, it is preserved as a timestamped conflict copy rather
than being overwritten.

See [In-Game Geo Workspace](IN_GAME_GEO.md).

## Interface Scale

`Ctrl+-` and `Ctrl+=` shrink and grow the whole UI; `Ctrl+0` resets to 100%. The
setting persists between launches. The sidebar `A-` / `100%` / `A+` buttons under
Workspace do the same.

## The Reload Button

Reopens the current track graph and mod JSON from disk. Its colour tells you the
state:

| Colour | Meaning |
| --- | --- |
| Grey | Nothing loaded that can be reloaded |
| Blue | Reload available |
| **Yellow** | Reload available **and unsaved changes would be discarded** |

## Undo

`Ctrl+Z` covers terrain strokes and mod edits, up to 50 mod steps. The status bar
shows `↩ N` with the number available.

Batch tile deletion is also undoable — deleted tiles move to a timestamped
`_TileEditor_Deleted_Tiles` recovery folder, and `Ctrl+Z` restores the whole batch.

## Next

- [Keybind Reference](KEYBINDS.md) — every binding
- [Terrain Editing](TERRAIN_EDITING.md)
- [Track Editing](TRACK_EDITING.md)
- [Mod Tools](MOD_TOOLS.md)
- [Data Formats](SCHEMA_EXAMPLES.md)
