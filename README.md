# Railroader Tile Editor

A Python-based terrain and map editor for **Railroader** mods.

## Runtime And Output Architecture

The desktop Tile Editor and F9 in-game Geo workspace share the editing surface:

- `Hrogers.TileEditorBridge` provides a Tile Editor-owned live Geo panel on top
  of Railroader's `Track.Graph`. F9 shows independent clickable track overlays
  and tools for node/segment operations, smooth grades, pieces, arcs, parallel
  tracks, fitted arcs, turnouts, wyes, and live road/river/bridge splineys.
  Roads, rivers, and AutoTrestle bridges can be placed directly in game. The
  panel is resizable and does not use Alina's Map Editor. It can select and
  remember an installed RailLoader mod/game-graph itself, so the desktop Tile
  Editor does not need to be running.
- Node transforms have their own movable and resizable child window. The main
  Geo surface retains a compact node summary and continue-track controls,
  while naming, move/rotate, exact coordinates, actions, and selective
  copy/paste stay together in the focused Node Editor.
- Its Operations workspace discovers, searches, selects, and edits towns,
  TrackSpans, industries, passenger stops, freight components, engine
  facilities, physical coal/water/fuel loaders, custom loader prefabs,
  station agents, commodities, and turntables. Pointer placement and colored
  world overlays use the same undoable document workflow as track and
  scenery.
- Industry creation includes complete RailLoader/FUSE component controls:
  storage capacity and daily change, car transfer rates and ordering,
  formula input/output maps, team-track import/export profiles, passenger
  population and neighbors, repair/overhaul, interchange variants,
  progression, multiple TrackSpans, and custom typed fields.
- Dedicated Geo `Span` and `Turntable` tools keep these track-geometry tasks
  beside Arc, Turnout, and Wye. Spans may cover a whole segment, a measured
  partial range, or connected start/end segments. Turntables support native
  FUSE pit/bridge geometry, bridge-track gauge, subdivisions, and optional
  roundhouse stalls, plus preserved legacy RailLoader/Alina TurntableBuilder
  output. Standard 30 m and three-foot narrow-gauge presets are included.
- Track nodes support a fast world shortcut: click the first node normally,
  then Shift-click the second node to connect them. The destination remains
  selected for continued Shift-click chain building.
- Track creation and segment properties support `Standard`, three-foot
  `Narrow`, automatic `DualGauge`, explicit `DualGauge_L` /
  `DualGauge_R` shared rails, and `DualGauge_T` shared-rail transitions.
  A selected segment's Railroader track class can also be changed live between
  `Mainline`, `Branch`, and `Industrial`, with undo/redo and schema-safe
  RailLoader/FUSE output.
  New arcs, pieces, parallel tracks, turnouts, wyes, and direct connections
  inherit the active gauge. Existing gauge and companion fields survive
  routine edits, splits, merges, and renames. `DualGauge_T` is authored as
  one short segment between opposite explicit L/R runs; F9 checks its two
  endpoints and prevents accidentally applying it through a whole chain.
  Visible narrow/dual rail geometry is supplied by FUSE Narrow Gauge rather
  than Railroader's standard track builder. F9 now distinguishes saved gauge
  metadata from a loaded live runtime, tells the user when FUSE and FUSE
  Narrow Gauge must be enabled before restarting, and exposes a live gauge
  synchronization action when both are active. Normal 3-foot node and segment
  edits batch their FUSE metadata and rebuild only affected endpoints; they do
  not invoke the expensive whole-map special-work pass. Dual-gauge topology
  edits are deferred and coalesced because their generated ghost/shared rails
  require the complete synchronizer.
- Yellow segment overlays are kept in a protected graph-level editor layer,
  keyed by segment ID, so Railroader track rebuilds and undo/redo no longer
  permanently remove the editable track lines.
- Ctrl-dragging a track node moves it over terrain as one undoable edit.
  Dropping over a second cyan node connects the two while keeping the dragged
  node at its last valid terrain position.
- The in-game Spliney workspace can also build a bridge directly from a
  clicked track segment, sampling Railroader's exact 3D segment curve and
  applying an adjustable below-rail deck offset.
- Road, river, bridge, and trestle control points show movement plus full
  Pitch/Heading/Roll rotation together, with the same arrow controls and
  precision step ranges used for track nodes and scenery.
- Its live Scenery workspace provides a searchable loaded-asset palette,
  mouse-pointer placement and in-world picking, transform controls, terrain snapping,
  duplication, deletion, grouped undo/redo, and JSON saving. Geo and Scenery
  also retain Railroader's native terrain rebuild.
- A dedicated Objects workspace selects base-game buildings and props under
  the mouse and saves move, rotation, scale, enable/disable, and safe clone
  operations as portable RailLoader mandelas. FUSE imports the same entries
  as `world.sceneClones`, so one graph remains compatible with both runtimes.
  Shared town/map/world roots are blocked so an individual station click
  cannot move the entire loaded scene.
- A dedicated Poles workspace provides amber world markers, real pole-node
  creation at the mouse pointer, continuous-line placement, manual wire
  connections, WORLD/LOCAL movement, full Pitch/Heading/Roll rotation, and
  deletion. New poles and edges persist in the owning map mod's
  `tile-editor-telegraph-poles.json`; original poles still save compatible
  cumulative Alina TelegraphPoleMover offsets plus portable rotation
  overrides.
- A dedicated Terrain workspace separates practical terrain sculpting from
  vegetation/water surface painting. Stable building-pad, track/road,
  walkway, grade-plane, ditch, and embankment tools use bounded cut/fill and
  target convergence to avoid flatten spikes.
- Terrain can stream an aligned OpenStreetMap guide over only the 5 x 5 or
  8 x 8 game-tile neighborhood around the camera. The guide follows terrain,
  unloads behind the camera, shares the desktop editor's `Map.json`
  georeference, and caches images under the installed mod rather than a
  machine-specific source path. Its default `Clear Lines` mode removes the
  pale map sheet while retaining useful map features; `Full Map` remains
  available when the complete raster is preferred. Matching z15-z18
  resolution presets range from `Overview` through `Ultra`; adaptive detail
  keeps the camera area sharp without filling the entire coverage window with
  maximum-resolution tiles.
- Track, scenery, road/river/bridge splineys, telegraph poles, and terrain
  tiles synchronize in both directions between the F9 workspace and the
  desktop editor. Dirty-edit ownership locks prevent simultaneous writes;
  incoming reloads preserve unsaved work as timestamped conflict copies.
- F9 track nodes have a field-aware property clipboard. Copy only elevation,
  grade, heading, bank, full rotation, an elevation/rotation combination, the
  turnout switch flag, or all settings; only compatible paste actions become
  available on the target. X/Z position and connections remain unchanged.
- A primary `Place Free Node` mouse action creates and selects an independent
  starting node anywhere on the terrain, ready for connected placement,
  Pieces, Arc, Grade, Turnout, or Wye.
- The selected-node action row also has `Add +10 m`; repeated clicks create
  connected nodes ahead using the current heading, grade, and bank and select
  each new endpoint for uninterrupted track extension.
- The node transform workspace uses a high-contrast direction pad: elevation
  is separated from WORLD/LOCAL plan movement, pitch/heading/roll are grouped
  into paired curved-arrow controls, and hover text explains every axis.
- New track nodes can use a remembered prefix and readable name pattern.
  Every builder shares the pattern and adds a collision-safe number
  automatically.
- In-game overlays now share lightweight materials, cache repeated asset
  searches, reduce long-segment pick markers, and automatically sleep distant
  track visuals and colliders until the camera approaches them.
- Its Desktop and Scenery tabs still expose the editor status and remotely
  prepare tools, focus the desktop editor, undo, and save/reload through the
  existing TrackBridge connection.
- RailLoader `game-graph.json` remains the portable baseline. FUSE can import
  it, including Narrow Gauge metadata, while RailLoader safely ignores the
  extra `gauge` field.
- Native FUSE packages are also editable. The desktop and F9 selectors read
  `Info.json` `FuseDataFiles`; `.fuse.json` track fragments retain
  `startNodeId` / `endNodeId`, native removal lists, and unrelated FUSE
  operations instead of being rewritten as legacy JSON.
- RailLoader `Definition.json` (manifest version 8) remains available for
  portable content, while FUSE-only operations can live in native fragments
  when the legacy schema cannot express them.
- The existing `Mods/TrackBridge` files continue to carry live game graph and
  reload traffic. The compact UMM panel adds a small status/request channel
  beside them without changing their format.

This project combines:

- terrain tile loading and generation
- track graph editing
- vertical profile design with constant grades and smooth grade transitions
- spliney editing for roads, rivers, and trestles
- geometry drafting tools for arcs, grade work, turnout work, and piece-based assembly
- bridge/live graph integration with an in-game Geo editing workspace

## Project Layout

- `edit_tiles/`: main editor package
- `mod_project/`: mod/layer/project data model and geometry helpers
- `railroader_bridge.py`: live bridge integration
- `TileEditorBridge/`: Unity Mod Manager in-game Geo workspace and bridge panel
- `run_editor.bat`: Windows launcher
- `TRACK_TOOL_ROADMAP.md`: tool roadmap and planning notes
- `HunterR_Map_Editor_Guide.pdf`: usage guide

## Requirements

- 64-bit Python 3.10 or newer
- `pygame-ce`
- `numpy`
- `Pillow`
- `requests`
- `scipy`

Install them with:

```bash
pip install -r requirements.txt
```

## Running

Windows:

```bat
run_editor.bat
```

The Windows launcher finds Python through the official `py` launcher, PATH,
registry entries, and common python.org, Conda, and Scoop folders. Python does
not have to be added to PATH. Set `TILE_EDITOR_PYTHON` to an exact
`python.exe` path when using a portable or unusual installation.

Or directly:

```bash
python -m edit_tiles
```

## Notes

- Generated Python caches, crash logs, release zips, and terrain tile outputs are ignored in Git.
- The repository is set up as a **source repo**; packaged release archives should be uploaded separately as GitHub releases if needed.
