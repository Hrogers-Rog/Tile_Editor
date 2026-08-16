# Keybind Reference

Every keyboard and mouse binding in the desktop Tile Editor. Press **`?`** or
**F1** in the editor to see the same table in-app (help page 9).

Bindings are **contextual** — several keys do different things depending on
whether edit mode is on. Those are marked.

## Cheat Sheet

The dozen you will actually use every session:

| Key | Action |
| --- | --- |
| `E` | Toggle edit mode (terrain painting) |
| `F` | Fit all tiles in view |
| `Ctrl+S` | Save |
| `Ctrl+Z` | Undo |
| `Ctrl+click` | Create node at cursor |
| `RMB drag` | Pan the map |
| `Scroll` | Zoom |
| `[` `]` | Brush smaller / larger |
| `-` `=` | Brush weaker / stronger |
| `T` | Toggle track overlay |
| `Esc` | Cancel anything / close panel |
| `?` / `F1` | Help |

## Display Modes

| Key | Action |
| --- | --- |
| `H` | Heightmap mode — elevation contours |
| `V` | Vegetation mode — veg/biome layer |
| `W` | Water mode — water coverage |
| `S` | Toggle hillshade |
| `D` | Toggle diff overlay (modified tiles) |
| `O` | Toggle OSM overlay |
| `T` | Toggle track display |
| `N` | Toggle node dots |
| `I` | Toggle tile info tooltip |

## View And Navigation

| Key | Action |
| --- | --- |
| `F` | Fit view to tiles |
| `R` | Redraw / refresh |
| `Ctrl+R` | Reload graph and mod data from disk |
| `Scroll` | Zoom in / out |
| `RMB drag` | Pan the map |
| `MMB` | Pan (middle mouse) |
| `Mousewheel over panel` | Scroll lists |
| `←` `→` `Tab` | Navigate help pages |

Note: help page 2 lists `LMB drag` for panning while the keybind summary lists
`RMB drag`. Right mouse is the reliable pan outside edit mode; in edit mode LMB
paints, so use RMB or MMB there.

## Interface Scale

| Key | Action |
| --- | --- |
| `Ctrl+-` | Shrink UI scale |
| `Ctrl+=` | Grow UI scale |
| `Ctrl+0` | Reset UI scale to 100% |

Scale persists between launches. The sidebar `A-` / `100%` / `A+` controls under
Workspace do the same thing.

## Files

| Key | Action |
| --- | --- |
| `Ctrl+S` | Save modified tiles / mod project |
| `Ctrl+Z` | Undo — terrain strokes or mod edits (up to 50 mod steps) |
| `L` | Load track graph JSON |
| `B` | Pick game / bridge folder *(outside edit mode)* |
| `Drag-drop folder` | Drop a tile folder on the window to load it |
| `Esc` / `Q` | Quit, cancel, or close |

## Terrain Editing

Edit mode only — press `E` first. LMB paints, RMB erases.

| Key | Action |
| --- | --- |
| `E` | Toggle edit mode |
| `B` | Cycle brush type *(edit mode)* |
| `[` / `]` | Brush size down / up |
| `Ctrl+Scroll` | Brush size |
| `-` / `=` | Brush strength down / up |
| `LMB` | Paint / raise |
| `RMB` | Erase / lower |
| `MMB` | Eyedropper — sample height or veg at cursor |
| `0`–`7` | Vegetation preset *(veg mode)* |
| `,` | Set height floor clamp to cursor height |
| `.` | Set height ceiling clamp to cursor height |
| `Ctrl+,` | Clear floor clamp |
| `Ctrl+.` | Clear ceiling clamp |
| `Ctrl+X` | Export heightmap as PNG |

### Brush Types

Cycle with `B` in edit mode.

| Brush | Effect |
| --- | --- |
| Raise | Raise (LMB) or lower (RMB) |
| Flatten | Level to the height you first clicked |
| Paint | Stamp an exact target height |
| Smooth | Average pixels with neighbours |
| Noise | Add fBm procedural noise |
| Erode | Simulate hydraulic erosion |

## Terrain Selection

Press `M` in edit mode.

| Key | Action |
| --- | --- |
| `M` | Toggle selection mode |
| `Click+drag` | Rect / lasso select |
| `Click` (wand) | Flood-fill select similar values |
| `Ctrl+C` | Copy selection |
| `Ctrl+V` | Paste selection — click to place |
| `Ctrl+X` | Cut selection / export PNG |
| `Delete` | Clear selected region |

## Track Editing

| Input | Action |
| --- | --- |
| `LMB` | Select node or segment |
| `Click segment` | Selects anywhere along the line, not just the midpoint |
| `Click empty space` | Deselect |
| `Click selected node` | Second click starts drag |
| `Drag release` | Commit move — samples terrain height |
| `Ctrl+click` map | Create node at cursor |
| `Ctrl+click` node | Finish a connection to that node |
| `Delete` | Delete selected node or segment |
| `Esc` | Cancel drag / connect / place |
| `Ctrl+drag` | Rubber-band select nodes |
| `Shift+drag` | Add to group selection |

### Connection Feedback Colours

Drag a node toward a target and watch the highlight — it tells you what will
happen on release:

| Highlight | Result |
| --- | --- |
| Snap ring on a node | Connect the two with a segment |
| **Cyan** on a segment | Insert the node into that segment (splits it in two) |
| **Yellow** on a segment (`Shift+drag`) | Insert the node **and** add a turnout diverge leg |

Inserting into a segment deletes the original and creates two; the new node
inherits a bezier heading. The yellow turnout variant uses the current
Geo → Turnout settings for angle and length.

## Tile Generation Panel

| Input | Action |
| --- | --- |
| `Click tile cell` | Queue a tile for generation |
| `Drag cells` | Box-select a region to queue |
| `RMB tile cell` | Remove from queue |
| `Scroll` | Zoom the generate grid |
| `MMB drag` | Pan the generate grid |
| `Run` | Generate all queued tiles |

## Contextual Keys

Three keys change meaning with edit mode, which is the most common source of
confusion:

| Key | Outside edit mode | In edit mode |
| --- | --- | --- |
| `B` | Pick game / bridge folder | Cycle brush type |
| `LMB` | Select / pan | Paint |
| `RMB` | Pan | Erase |

`M`, `[`, `]`, `-`, `=`, `,`, `.`, and `0`–`7` only do anything in edit mode.

## Status Bar

The bottom bar reports the current action and errors. `↩ N` shows how many mod
undo steps are available.

## Related

- [Terrain Editing](TERRAIN_EDITING.md)
- [Track Editing](TRACK_EDITING.md)
- [Getting Started](GETTING_STARTED.md)
