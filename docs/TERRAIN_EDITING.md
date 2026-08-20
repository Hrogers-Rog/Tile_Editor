# Terrain Editing

Sculpting heightmaps, painting vegetation and water, and generating tiles.

Press **`E`** to toggle edit mode. The cursor becomes a brush and a toolbar
appears below the nav bar. **LMB paints, RMB erases.**

## Brushes

Cycle with **`B`** in edit mode.

| Brush | Effect |
| --- | --- |
| **Raise** | Raise (LMB) or lower (RMB) |
| **Flatten** | Level the area to the height you first clicked |
| **Paint** | Stamp an exact target height |
| **Smooth** | Average pixels with their neighbours |
| **Noise** | Add fBm procedural noise |
| **Erode** | Simulate hydraulic erosion |

Flatten samples on first click, so click on the elevation you want *before*
dragging across the area you want levelled.

## Brush Controls

| Key | Action |
| --- | --- |
| `[` / `]` | Size down / up |
| `Ctrl+Scroll` | Size |
| `-` / `=` | Strength down / up |
| `MMB` | Eyedropper — sample height or vegetation at the cursor |

## Height Clamps

Clamps bound what a brush can do, which is how you grade against a fixed
elevation without overshooting.

| Key | Action |
| --- | --- |
| `,` | Set floor clamp to cursor height |
| `.` | Set ceiling clamp to cursor height |
| `Ctrl+,` | Clear floor clamp |
| `Ctrl+.` | Clear ceiling clamp |

Set a floor at rail height and Smooth aggressively around a right-of-way — the
clamp stops the brush pulling terrain below the grade.

## Vegetation And Water

Press `V` for vegetation mode, `W` for water.

In vegetation mode, keys **`0`–`7`** select the preset to paint. Water mode paints
the water mask — white is water, black is land.

The preset is a vegetation-density level, not a fixed biome:

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

Those are placement-mask strengths. The game's vegetation graph can still
suppress individual plants around track, water, objects, steep slopes, and
cut-tree masks. Generated land-cover data maps source classes onto these levels;
it does not store an ecological class in the tile.

The water mask does not draw a visible lake plane. For that, open **F9 → Geo →
Water** in a native FUSE project. The Water workspace can place a rectangular
surface with the world pointer, edit every polygon boundary point, reuse the
material/profile from a loaded stock lake, and replace a stock lake with an
editable FUSE-owned copy. Collider, terrain snapping, UV scale, tessellation,
and vertical offset are explicit fields.

RailLoader has no equivalent lake-polygon schema, so these controls are visibly
disabled in legacy mode instead of writing data that would be lost on export.

## Selection

Press **`M`** in edit mode.

| Tool | Use |
| --- | --- |
| Rect / Lasso | Click and drag to select a region |
| Wand | Click to flood-fill select similar values |

| Key | Action |
| --- | --- |
| `Ctrl+C` | Copy |
| `Ctrl+V` | Paste — then click to place |
| `Ctrl+X` | Cut / export PNG |
| `Delete` | Clear the selected region |

Copy and paste move terrain between locations, which is the quickest way to
repeat a landform.

## Tile Generation

Open the **Generate** panel to build terrain from real-world data.

| Action | Result |
| --- | --- |
| Click a tile cell | Queue it |
| Drag across cells | Box-select a region to queue |
| RMB a cell | Remove from the queue |
| Scroll | Zoom the grid |
| MMB drag | Pan the grid |
| **Run** | Generate everything queued |

Queue the whole area first, then Run once — generation is the slow part.

Tile bounds come from the nearest `Map.json`: `origin.latitude`,
`origin.longitude`, and `tileDimension`. Standalone native projects create this
file during the New Mod wizard, so elevation, vegetation, and OSM alignment are
not tied to the stock map. Existing legacy tile folders without a manifest use
the stock-map defaults for compatibility.

After generation, the editor atomically synchronizes `Map.json.tiles` with the
tile files actually present on disk. Tile cleanup and undo do the same, so a map
package cannot silently advertise deleted terrain or omit restored terrain.

## Tile Cleanup

A dedicated workspace for trimming large generated blocks down to what a railroad
actually needs.

| Action | Result |
| --- | --- |
| Drag | Mark tiles |
| `Shift`+drag | Add to the marked set |
| `Ctrl`/right-drag | Keep tiles |
| Invert / Outside ROW | Mark everything outside the right-of-way |

The usual workflow is to mark the ROW, then **Invert / Outside ROW**, then delete.

Batch deletion asks for confirmation and moves files into a timestamped
`_TileEditor_Deleted_Tiles` recovery folder. **`Ctrl+Z` restores the whole batch** —
nothing is destroyed immediately.

## OSM Guide Overlay

Press **`O`** for the OpenStreetMap overlay, which is the practical way to trace
real alignments.

The in-game F9 equivalent streams an aligned guide over just the 5×5 or 8×8
game-tile neighbourhood around the camera, following terrain and unloading behind
you. It shares the desktop editor's `Map.json` georeference and caches images
under the installed mod rather than a machine-specific path.

| Setting | Options |
| --- | --- |
| Mode | **Clear Lines** (default — drops the pale map sheet, keeps features) or **Full Map** |
| Resolution | Overview → Ultra, matching z15–z18 |

Adaptive detail keeps the camera area sharp without rendering the whole coverage
window at maximum resolution. Both the desktop OSM toolbar and the F9 Terrain
controls show cache usage and offer a confirmation-protected **Clear Cache**.

OSM and Mapbox data come from third-party services under their own terms — check
those before redistributing anything derived from them.

## In-Game Terrain Workspace

The F9 Terrain workspace separates practical sculpting from vegetation and water
mask painting. The separate **Geo → Water** workspace creates the visible water
surface. Terrain's building-pad, track/road, walkway, grade-plane, ditch, and
embankment tools use bounded cut/fill with target convergence, specifically to
avoid the spikes a naive flatten produces.

## Saving

`Ctrl+S` saves all modified tiles atomically, keeps up to three recoverable
per-tile backups, and clears every derived render cache only after the new file
has replaced the old one. `Ctrl+X` exports the heightmap as a PNG.
Press `D` for diff mode to see exactly which tiles are unsaved — they carry a
yellow border.

## Related

- [Keybinds](KEYBINDS.md)
- [Getting Started](GETTING_STARTED.md)
- [Track Editing](TRACK_EDITING.md)
