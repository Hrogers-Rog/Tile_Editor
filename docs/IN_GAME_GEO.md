# In-Game Geo Workspace (F9)

`Hrogers.TileEditorBridge` puts a live editing workspace inside Railroader, on top
of the game's own `Track.Graph`. Press **F9**.

It does not use Alina's Map Editor, and it can select and remember an installed
RailLoader mod or game-graph by itself — **the desktop Tile Editor does not need to
be running.**

## Mouse Handling

While F9 is open the mouse is reserved for editing. Railroader's pan, orbit, look,
and wheel zoom are locked so the camera stays put during placement.

- `W` `A` `S` `D` and the normal fast-movement modifiers still move the camera.
- **Hold middle mouse** to temporarily restore all normal camera controls. Editing
  clicks are ignored until you release it.
- **Right-click the world** to clear every selection and cancel any active mode —
  placement, connection, bridge-picking, Fit Arc, TrackSpan, pole-wiring, terrain,
  and delete.

That right-click is the universal escape hatch; reach for it when a mode seems
stuck.

## Workspaces

| Workspace | Covers |
| --- | --- |
| **Geo** | Nodes, segments, grades, pieces, arcs, parallel track, fitted arcs, turnouts, wyes, splineys |
| **Terrain** | Sculpting, plus vegetation and water painting, and the OSM guide |
| **Scenery** | Searchable asset palette, placement, transforms, terrain snapping |
| **Objects** | Base-game buildings and props → portable mandelas |
| **Poles** | Telegraph pole nodes and wire edges |
| **Signals** | Semaphore placement and interlockings |
| **Operations** | Towns, spans, industries, stations, loaders, turntables |
| **Desktop** | Editor status; remotely focus, undo, save, and reload |

The panel is resizable.

## Track Editing

A fast shortcut for connected track: **click the first node normally, then
Shift-click the second** to connect them. The destination stays selected, so you
can keep Shift-clicking to build a chain.

| Action | Result |
| --- | --- |
| Place Free Node | Create and select an independent starting node anywhere on terrain |
| Add +10 m | Create a connected node ahead using current heading, grade, and bank |
| `Ctrl`+drag a node | Move it over terrain as one undoable edit |
| Drop over a cyan node | Connect the two, keeping the dragged node at its last valid position |

Repeated **Add +10 m** clicks extend track continuously, selecting each new
endpoint — the quickest way to run a tangent.

Node transforms live in their own movable, resizable child window. The main Geo
surface keeps a compact summary and continue-track controls, while naming,
move/rotate, exact coordinates, actions, and selective copy/paste stay together in
the focused Node Editor.

Its direction pad separates elevation from WORLD/LOCAL plan movement and groups
pitch, heading, and roll into paired curved-arrow controls, with hover text on
every axis.

### Property Clipboard

Copy a single field rather than the whole node: elevation, grade, heading, bank,
full rotation, an elevation+rotation combination, the turnout switch flag, or
everything. Only compatible paste actions light up on the target. X/Z position and
connections are never changed.

### Naming

New nodes can use a remembered prefix and readable name pattern. Every builder
shares the pattern and adds a collision-safe number.

## Gauge

Track creation and segment properties support `Standard`, three-foot `Narrow`,
automatic `DualGauge`, explicit `DualGauge_L` / `DualGauge_R`, and `DualGauge_T`
transitions. Track class can be changed live between Mainline, Branch, and
Industrial with undo/redo and schema-safe output.

F9 distinguishes saved gauge metadata from a loaded live runtime, tells you when
FUSE and FUSE Narrow Gauge must be enabled before restarting, and offers a live
gauge synchronisation action when both are active.

Performance detail worth knowing: ordinary 3-foot node and segment edits batch
their FUSE metadata and rebuild only affected endpoints. **Dual-gauge topology
edits are deferred and coalesced**, because their generated ghost and shared rails
need the complete synchroniser — so a dual-gauge change costs more than a narrow
one.

`DualGauge_T` is authored as one short segment between opposite explicit L/R runs;
F9 checks its two endpoints and prevents applying it through a whole chain.

## Signals

Places Railroader's base-game animated semaphore assemblies with one, two, or
three heads: pointer placement, amber world selection, WORLD/LOCAL movement, full
rotation and flip controls, exact transforms, and test aspects.

Masts support a one-time rail snap or a **persistent track lock**. A locked mast
keeps its side, height, and facing offsets and follows later Bezier curve, grade,
and elevation edits.

The **Diamond Interlocking** builder detects the real intersection of two
non-connecting Bezier segments, rejects a grade-separated overpass, and places
four independently adjustable A1/A2/B1/B2 semaphores. Setback, lateral and
vertical offset, head count, approach locking, and release length are all
configurable. Long 500–800 m setbacks follow connected track across segment
boundaries automatically.

Output is portable `train-signals.json`, loaded during ordinary gameplay by
Railroad Operations — players never install the Tile Editor. See
[Data Formats](SCHEMA_EXAMPLES.md#train-signals).

Live **Signals & CTC**, **Train Orders**, and **My Orders** desks join the normal
Company → Operations window. F9 remains the map-authoring editor.

## Splineys And Bridges

The in-game Spliney workspace can build a bridge directly from a clicked track
segment. Its two endpoint controls inherit the track node positions and rotations,
reproducing Railroader's exact 3D Bezier span with an adjustable below-rail deck
offset and no redundant intermediate nodes. Yellow track picking stays armed for
the next bridge afterwards.

Road, river, bridge, and trestle control points show movement plus full
pitch/heading/roll together, using the same arrow controls and precision steps as
track nodes and scenery.

## Operations

Discovers, searches, selects, and edits towns, TrackSpans, industries, passenger
stops, freight components, engine facilities, physical coal/water/fuel loaders,
custom loader prefabs, station agents, commodities, and turntables. Pointer
placement and coloured world overlays use the same undoable document workflow as
track and scenery.

Dedicated Geo **Span** and **Turntable** tools sit beside Arc, Turnout, and Wye.
Turntables support native FUSE pit and bridge geometry, bridge-track gauge,
subdivisions, and optional roundhouse stalls, plus preserved legacy
RailLoader/Alina `TurntableBuilder` output. Standard 30 m and three-foot
narrow-gauge presets are included.

## Sync With The Desktop Editor

Track, scenery, road/river/bridge splineys, telegraph poles, and terrain tiles
synchronise in **both directions**.

Dirty-edit ownership locks prevent simultaneous writes. An incoming reload
preserves unsaved work as a timestamped conflict copy rather than discarding it.

`Mods/TrackBridge` files carry the live graph and reload traffic; the compact UMM
panel adds a small status and request channel beside them without changing that
format.

## Rendering Notes

Yellow segment overlays live in a protected graph-level editor layer keyed by
segment id, so Railroader track rebuilds and undo/redo no longer permanently
remove editable track lines.

Overlays share lightweight materials, cache repeated asset searches, reduce
long-segment pick markers, and sleep distant track visuals and colliders until
the camera approaches.

## Related

- [Data Formats](SCHEMA_EXAMPLES.md)
- [Mod Tools](MOD_TOOLS.md)
- [Getting Started](GETTING_STARTED.md)
