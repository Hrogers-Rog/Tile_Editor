# Hrogers Tile Editor Suite 0.25.0

Hold **Shift+?** in F9 for a live pointer survey showing map/game, Unity
world, graph-local, terrain-tile, and tile-local coordinates plus the nearest
track's signed grade, heading, chainage, gauge, class, and group. F9 no longer
locks the mouse camera automatically: middle mouse toggles between normal
Railroader mouse navigation and an editing-safe camera lock. While locked,
use W/A/S/D to move, the wheel to zoom, and Q/E to rotate.

## Period signaling, CTC, ABS, and train orders

The Operations workspace now contains **Signals** and **Orders** pages for a
1900-1950s operating system. Select timetable/train-order, ABS, or CTC as the
territory mode. Signal hardware begins with Railroader's semaphore family;
the operating data is independent of the visual asset so later searchlight or
position-light families can use the same blocks and routes.

The Signals page contains a schematic territory preview for authoring. Create a control
point by clicking a turnout node, then assign its Normal and Reverse entry
signals and comma-separated protected block IDs. Live dispatching is in the
normal **Company > Operations > Signals & CTC** page supplied by standalone
Signal Runtime. There, a dispatcher can command Normal/Reverse, line either
route, or return the plant to Stop. The runtime checks cars on switches, block
occupancy, conflicting routes, and switch correspondence before clearing a
signal. A switch remains locked until the movement occupies and clears its
route plus the time release.

ABS/CTC blocks are created from a clicked segment and extended one selected
segment at a time. Assign the signal at each end and, for three-aspect ABS,
the next block encountered by a train entering from that end. An occupied
block displays Stop; a clear block followed by an occupied or missing named
next block displays Approach; two clear blocks display Clear. Manual blocks
hold their signals at Stop until a later operator/train-order authority layer
explicitly clears them.

The Orders page creates numbered Form 19, Form 31, track warrant, meet, hold,
and run-extra records with train, crew, block limits, meet location,
instructions, effective/expiry text, priority, and lifecycle settings. Issue
and deliver orders to real Railroader train crews from **Company > Operations
> Train Orders**. Crew members use **My Orders** there or press F8 to read,
repeat/sign, and acknowledge through standalone Signal Runtime. The multiplayer host records the real player and time, then
enforces the authored block authority for manual locomotives and Auto
Engineer/Waypoint operation. Tile Editor is not required on crew clients.
All territory data is stored in `ctc-system.json` beside the edited graph.

## Portable train signals and grade crossings

The dedicated `SIGNALS` workspace places Railroader's own animated semaphore
assemblies rather than decorative scenery. Choose one, two, or three heads;
place at the mouse pointer; move, rotate, or flip it; test an aspect; and save
stable interlocking, protected-node, protected-segment, and direction metadata.
Signals are written immediately to `train-signals.json` beside the selected
map graph. Distribute the standalone `Hrogers.SignalRuntime` with the map. It
loads the base-game signal asset and operates generated diamond interlockings
without requiring Tile Editor during gameplay.

The Diamond Interlocking builder accepts two non-connecting crossing track
segments, finds their actual curve intersection, and creates A1/A2/B1/B2
semaphores at adjustable setback, side-offset, and vertical-offset values. It
stores the two conflicting railroad routes plus approach/release lengths. The
four signals are normal independent records: click any amber mast afterward to
move, rotate, flip, rename, rebind, change its heads, or test its aspect.
Signal setbacks can extend to 5,000 m. Each of the four approaches walks the
connected graph across as many segments as necessary; at a turnout it chooses
the best-aligned continuation while preferring the same group, gauge, class,
and style. The saved signal distinguishes the protected segment chain between
the mast and diamond from the additional approach-locking chain behind it.
If a mast is moved later, select it and use **Recalculate Block From Moved
Mast**; its exact transform is retained while the saved chain is shortened or
extended to the nearest segment on that approach.

Generated diamonds run automatically. Railroader's live car locations on the
four saved approaches request the interlocking, only one route can clear, and
all three conflicting semaphore signals remain at Stop. The active semaphore
returns to Stop after the train enters its protected block, while the route
remains locked through the diamond and releases after the configured clear
delay. The selected signal panel shows the live state and provides manual
request/release controls for testing and dispatching. Manual release is
fail-safe and is refused while the crossing is occupied.

Functional Auto Engineer crossings are written to `grade-crossings.json`
beside the selected map graph. Distribute the small standalone
`Hrogers.CrossingRuntime` mod with the map. It does not depend on this editor or
AI Traffic and applies the crossings to player-owned Waypoint Auto Engineer
equipment as well as unowned AI trains.

A complete Unity Mod Manager package containing the in-game Geo editor and the
desktop Tile Editor.

- Press `F9` in Railroader to enter or leave Tile Editor mode.
- Node naming, movement, rotation, exact transforms, copy/paste, and node
  actions now live in a separate movable and resizable Node Editor window.
  Geo keeps a compact selected-node card and the fast continue-track
  controls, so track builders no longer share one long panel with every
  transform control. Both windows share the live selection and block world
  clicks underneath them; the Node Editor remembers its position and size.
- The new `OPERATIONS` workspace discovers and searches towns, TrackSpans,
  industries, freight and passenger components, commodities, station agents,
  engine-service loaders, and turntables in both RailLoader/Strange Customs
  layers and native FUSE packages. Colored markers can be clicked in the
  world, and all document edits use the normal Undo/Redo and Save Graph bar.
- Towns and industries can be placed with the mouse, a whole selected track
  segment can become a TrackSpan, and compact profiles add receiving,
  shipping, passenger, repair, team-track, interchange, custom, steam,
  diesel, or combined terminal operations. Water towers, coal conveyors, and
  custom physical service prefabs use pointer placement.
- Industry components expose the production fields used by EFA and other
  full maps: multiple TrackSpans, shared storage, daily storage change,
  maximum storage, car transfer rate, empty/loaded ordering, formula input
  and output rates, team-track import/export profiles, repair/overhaul,
  passenger code/population/branch/neighbors, interchanged output spans, and
  converted loads. Advanced scheduling, cost, fill, book-reason, title, and
  typed custom JSON fields remain collapsed until needed.
- Geo now has dedicated `Span` and `Turntable` tools. Span creates whole,
  measured partial, or connected multi-segment TrackSpans. Turntable has
  standard-gauge and three-foot presets plus editable radius, subdivision,
  bridge gauge, actual-radius pointer preview, and optional roundhouse
  geometry. Native FUSE writes `operations.turntables`; RailLoader preserves
  the legacy Alina TurntableBuilder format and reports that dependency.
- Click a yellow track segment to change its Railroader track class between
  Mainline, Branch, and Industrial. The active class is highlighted, changes
  support undo/redo, and both RailLoader and native FUSE JSON are preserved.
- New track nodes, scenery assets, and telegraph poles can be armed from their
  normal tabs and placed at the exact mouse-pointer target in the world.
  Repeat placement supports quickly extending track, filling scenery, or
  continuing a connected pole line; right-click or Escape cancels.
- In Geo mode, Tile Editor automatically uses the active desktop layer. If the
  desktop is not running, choose an installed map mod from Tile Editor's own
  `Change Mod / Graph` chooser. Its recommended main graph is selected by
  default; every individual mixinto remains available under `More Layers`.
  The chosen layer is remembered and automatically reopened later.
- The chooser also recognizes native FUSE `Info.json` `FuseDataFiles`.
  Editing a `.fuse.json` track fragment preserves FUSE endpoint names,
  operation data, and native removal lists.
- New trestles use the Strange Customs live builder when available and
  otherwise use FUSE's native Spliney API. The saved JSON remains compatible
  with either runtime.
- The target-mod selector remains available from Geo, Scenery, Poles, and
  Terrain after a graph is open. It can refresh newly installed mods and
  prevents switching while track, scenery, Spliney, pole, or terrain changes
  are unsaved.
- All live track nodes and segments appear in the world and can be clicked
  directly. Segment overlays live outside Railroader's replaceable track
  objects, so the yellow lines return after rebuild, undo, and redo. Alina's
  Map Editor is not used.
- Select a segment in F9 Geo mode to view, assign, or clear its graph
  `groupId`. Group changes use the same Undo/Redo and Save Graph workflow as
  other track edits.
- Choose STD, 3-FT, DUAL AUTO, DUAL L, DUAL R, or DUAL T above the Geo tools.
  All new arcs, pieces, parallel tracks, turnouts, wyes, and direct node
  connections inherit that gauge. Click a segment to change only it or apply
  the gauge through its degree-two connected chain. Orange overlays are
  narrow, blue overlays are dual gauge, and magenta marks a shared-rail
  transition. DUAL T is restricted to one short segment between a DUAL L and
  DUAL R run; its endpoint check appears in the selected-segment controls.
  The graph can always store these portable gauge values, but visible
  three-foot/dual rails require both FUSE and FUSE Narrow Gauge to be enabled
  when Railroader starts. F9 reports that runtime state and offers `Sync Gauge
  Visuals` when live synchronization is available. Pure 3-foot edits update
  only affected track endpoints instead of reanalyzing and rebuilding every
  narrow/dual turnout on the map. Dual-gauge topology changes are deferred
  briefly and coalesced into one required ghost/shared-rail synchronization.
- Click a first cyan node normally, then hold Shift and click a second cyan
  node to connect them with the active-gauge track segment. The second node remains
  selected, allowing continued Shift-clicks to build a connected chain.
- Hold Ctrl and drag a cyan node across terrain to reposition it as one
  undoable edit. Release over another cyan node when it turns green to connect
  the dragged node to that target; the dragged node stays at its last valid
  terrain position so the new segment does not collapse to zero length.
- Right-click anywhere in the world to clear track, Spliney, scenery, pole,
  base-object, and operations selections. The same gesture cancels pointer
  placement, node dragging, bridge picking, node/pole connections, Fit Arc
  chains, measured TrackSpan starts, terrain strokes, and delete confirmation.
- F9 reserves the mouse for editor selection and placement. Railroader's
  left-drag pan, right-drag orbit, first-person mouse look, and wheel zoom are
  disabled while the panel is open; W/A/S/D and the normal fast-movement
  modifiers remain available for camera travel.
- Node movement has primary WORLD and LOCAL modes. WORLD follows map axes;
  LOCAL X/Z follows the node's track heading. Movement uses targeted,
  debounced track rebuilding so rapid nudges and Ctrl-drag stay responsive.
- A persistent `NEW NODE IDS` bar accepts a prefix and readable name. Every
  node created by Node, Pieces, Arc, Grade, Parallel, Turnout, or Wye uses the
  pattern with an automatically unique numeric suffix, such as
  `BRY_YardLead_001`.
- Track, Spliney, scenery, and pole markers share one lightweight overlay
  material. Track markers and pick colliders outside the useful camera range
  sleep and wake automatically, long segments use fewer pick objects, and
  scenery searches are cached between Unity GUI passes.
- The scenery asset picker pages through the entire loaded asset catalog
  instead of showing only the first matches. It also discovers RailLoader
  `SCAssetPacks` that registered after Railroader built its initial identifier
  list, while filtering out definitions the live manager cannot resolve.
  Search, Previous, and Next work across the combined catalog.
- The `OBJECTS` tab edits base-game buildings and props as RailLoader
  mandelas/FUSE scene clones. Click the object itself, refine the chosen
  hierarchy level when needed, then move or rotate it in world/local axes,
  scale it, disable/re-enable it, or clone it beside the source or at the
  mouse pointer. Town, map, and world-scene containers are hard selection
  boundaries, so clicking Bryson Station cannot move the whole scene.
  Unsafe saved-state objects cannot be cloned. Thin signs and small props use
  a modest screen-space picking halo only when normal collider and renderer
  ray selection misses.
- Object overrides use parent-local transforms, share the normal in-game
  Undo/Redo and Save Graph workflow, and remain readable by both Strange
  Customs and FUSE.
- Selected nodes open one unified transform workspace with movement and
  full-axis rotation step selectors and arrow pads visible together. The
  movement pad separates elevation from plan movement, labels WORLD/LOCAL
  axes, and uses bold arrow glyphs and hover help. Pitch, heading, and roll
  have paired curved-arrow controls. `More...` reveals exact X/Y/Z fields,
  all precision increments, local axes, and connection tools.
- The node `Copy / Paste...` drawer has matching copy-only and paste-only
  actions for elevation, grade/pitch, heading, bank, complete rotation,
  elevation combinations, and the switch-stand flag. The clipboard records
  only the chosen fields, disables incompatible paste combinations, and never
  changes X/Z position or graph connections. Every paste is undoable and uses
  the lightweight connected-track refresh.
- `Place Free Node` is visible in the primary node actions even while another
  node is selected. It places an independent node at the mouse, selects it,
  and makes it the immediate starting point for connected placement and every
  Geo construction tool.
- `Add +10 m` sits beside Split, Level, and Flip. It creates and selects a
  connected node ten meters ahead using the current node's heading, grade,
  and bank, so repeated clicks continue a straight constant-grade run.
- The two-row Geo toolbar provides Spliney, Pieces, Arc, Parallel, Fit Arc,
  Node, Grade, Turnout, Wye, Span, and Turntable workspaces.
- Wye can build a complete operational three-turnout wye in one undoable edit:
  select one existing approach endpoint or a normal two-segment through-track
  node, choose Compact, Standard, or Broad, customize the through length,
  triangle depth, tail stub, exit lead, side, and grade, then click Build.
  Through-track mode splits and reuses the forward track while preserving its
  alignment, grade, style, and class. The legacy simple three-way frog remains
  available in a collapsed section.
- Arc, Turnout, and Wye each provide a collapsible named-profile library.
  Profiles save immediately to `Mods/TrackBridge/tile_editor_track_profiles.json`
  and survive mod upgrades. Arc profiles retain radius, angle, control-node
  count, grade, and side; Turnout profiles retain lead, divergence, grade, and
  side; Wye profiles retain every dimension, grade, and tail side.
- Arc now creates an explicit user-selected number of new control nodes from
  1 through 64 instead of using a hidden automatic count.
- Pieces chains straight, curved, or turnout pieces from the current endpoint.
  Parallel offsets the selected segment, and Fit Arc reshapes an ordered chain
  of connected nodes into one circular alignment.
- Spliney can place new roads, rivers, bridges, and trestles directly at the
  in-game camera target. Existing and new objects provide clickable live
  control points with movement, full rotation, point insertion/deletion,
  exact transforms, whole-object deletion, undo/redo, and atomic JSON save.
  Loaded base-game roads and rivers are included even when they are not listed
  in the selected mod graph; the first adjustment creates a same-name override
  and refreshes the affected terrain and mask tiles.
  Roads and rivers expose width and loaded Strange Customs profiles; bridges
  preserve their AutoTrestle curve and expose Block/Bent styles independently
  at both ends.
- Selected Spliney points show movement and rotation together in the main
  workspace, matching track nodes and scenery. Pitch X, Heading Y, and Roll Z
  have arrow controls, 0.01 through 180 degree steps, Level X/Z, Reset
  Rotation, and Flip Y 180; exact fields remain under `More...`.
- `Bridge Directly from Track` temporarily exposes the yellow track overlays:
  click one segment, set the vertical below-rail offset, then build. The
  bridge uses the TrackSegment's two endpoint positions and rotations, which
  reproduce the same high-accuracy 3D Bezier span—including grade, pitch,
  crests, and sags—without redundant intermediate controls. The default deck
  offset is 0.30 m below the rail. After each build, the new trestle and
  consumed track selection are cleared and yellow track picking remains armed
  for the next bridge. Existing trestles also show `Build Another Bridge`.
- Spliney discovery and overlay attachment are cached. Large maps with many
  AutoTrestles do not rescan and rebuild every panel heartbeat; unresolved
  loader objects use a bounded five-second retry.
- Drag the bottom-right resize handle to resize the F9 panel. Its size is
  remembered between sessions.
- While F9 is open, Railroader's normal panel, teleport, and gameplay
  shortcuts are locked so typing in Tile Editor fields does not open unrelated
  windows. Keyboard camera movement and Tile Editor mouse selection remain
  available. Hold the middle mouse button to temporarily restore Railroader's
  normal mouse pan, orbit, and zoom controls. Tile Editor ignores world clicks
  during the hold and resumes editing as soon as middle mouse is released.
- The dedicated Terrain tab paints directly under the mouse with a visible
  brush footprint. `Sculpt Terrain` and `Surface Paint` are separate
  workspaces so height editing cannot accidentally change vegetation or
  water masks.
- Terrain also has a local OpenStreetMap guide. It streams either a 5 x 5 or
  8 x 8 game-tile window around the camera, drapes 32-section map meshes over
  the live terrain, unloads meshes and textures outside that window, and
  keeps downloaded images in the installed mod's `Cache/OSM` folder.
  `Overview`, `Detail`, `Sharp`, and `Ultra` match the desktop editor's useful
  z15-z18 range and report the approximate ground resolution. Sharp and Ultra
  concentrate their highest resolution around the camera while retaining
  lighter coverage farther out. Trilinear mipmaps and anisotropic filtering
  keep angled views readable. Opacity remains adjustable in game. `Clear
  Lines` makes the pale OSM background nearly transparent while retaining
  stronger roads, rivers, buildings, labels, and boundaries; `Full Map`
  restores the original raster. Alignment uses the same `Map.json`
  latitude/longitude calibration as the desktop editor. The Terrain controls
  show OSM cache size and tile count; `Clear Cache` uses a confirmation click
  before deleting those downloaded tiles.
- Practical sculpt presets cover building pads, track/road beds, walkways,
  ditches, and embankments. Building Pad holds one sampled elevation for the
  whole stroke; Path/Road removes cross-slope while following the route;
  Grade Plane holds a chosen grade and heading; Ditch and Berm converge to a
  controlled depth or height.
- Raise, Lower, Smooth, Set Height, and Noise remain available. Every height
  tool has a maximum cut/fill limit, stable target convergence, configurable
  shape, falloff, radius, strength, spacing, sparse terrain Undo/Redo, and
  atomic native-tile saving.
- `Surface Paint` owns vegetation IDs 0 through 7, a vegetation eyedropper,
  Water Paint, and Water Clear.
- Terrain saves use Railroader's native tile format. Existing mod tiles receive
  timestamped backups before editing; base-game tiles are written as map-mod
  overrides instead of changing the game's original files.
- The in-game Scenery tab provides a searchable palette of every loaded
  scenery asset, exact mouse-pointer placement, clickable cyan world markers,
  world/local movement and full-axis pitch/heading/roll controls visible
  together, terrain snap, uniform or per-axis scale, exact transforms, model
  replacement, duplication, and deletion.
- The dedicated Poles tab displays nearby numbered poles as amber markers.
  Select a pole, arm connected placement, and click exact world positions to
  lay a continuous telegraph line; or place a standalone pole and connect wires
  manually. WORLD/LOCAL movement and full Pitch/Heading/Roll rotation are
  visible together for both original and Tile Editor-created poles. Custom
  poles save their complete transform directly; original poles retain
  cumulative `TelegraphPoleMover` position compatibility and use a portable
  Tile Editor override for rotation.
- New pole nodes and wire edges persist beside the owning map mod in
  `tile-editor-telegraph-poles.json`. The UMM bridge automatically restores
  every installed sidecar at map startup in one batched native-manager
  rebuild. Coordinates remain in stable game space.
- Track and scenery edits share one ordered in-game Undo/Redo history and the
  same atomic `Save Graph` operation.
- Desktop and in-game edits now synchronize in both directions for graph
  track/scenery data, roads/rivers/bridges, telegraph-pole sidecars, and
  terrain tiles. Saving one side reloads the corresponding open data on the
  other side. Unsaved ownership locks prevent simultaneous editing, and a
  timestamped conflict copy is written before an incoming reload could
  replace unsaved work.
- Scenery persistence always uses stable game coordinates. Floating-origin
  world coordinates are converted only while reading or applying a live Unity
  object, and selection automatically reattaches by ID after the loader
  replaces an object during Save.
- Overlay visibility and selection colors are event-driven. The panel does not
  rescan or recolor the complete map every frame, and dynamic loader discovery
  is throttled and skipped when the live object set is unchanged.
- Undo, Redo, and track rebuild schedule bounded overlay-repair passes after
  Unity finishes replacing graph objects. Missing, stale, or empty segment
  lines are reattached without restoring a continuous full-map scan.
- In-game changes use Tile Editor's own grouped Undo/Redo stack, atomic JSON
  save, timestamped pre-save backup, and direct Railroader track rebuild.
- Run `Launch Tile Editor.bat` from the installed mod folder to start the
  desktop editor. It accepts any compatible 64-bit Python 3.10 or newer and
  searches the Windows `py` launcher, PATH, Python registry entries,
  python.org user/system folders, Conda, Scoop, and portable overrides.
  Python does not need to be on PATH.
- Run `Launch Tile Editor.bat --diagnose-python` to show the exact interpreter
  the package found. Unusual installs can set `TILE_EDITOR_PYTHON` to the full
  `python.exe` path. `Repair Tile Editor Environment.bat` now creates or
  rebuilds a missing/broken isolated environment itself.
- The Desktop and Scenery tabs retain the `Mods/TrackBridge` connection for the
  full desktop asset browser and project tools.
- Desktop AutoTrestle selections include `Fit Trestle to Track`, which rebuilds
  an existing trestle from the matching rail segment's exact 3D Bezier.

The live Geo workspace works directly with Railroader's `Track.Graph`.
Strange Customs/AMM remains the output format and loader for modded graph JSON;
Alina's Map Editor is not required.

## Installed layout

```text
Railroader/
└─ Mods/
   └─ Hrogers.TileEditorBridge/
      ├─ Hrogers.TileEditorBridge.dll
      ├─ Info.json
      ├─ VERSION.txt
      ├─ PackageManifest.json
      ├─ Launch Tile Editor.bat
      ├─ Repair Tile Editor Environment.bat
      └─ TileEditor/
         ├─ edit_tiles/
         ├─ mod_project/
         ├─ railroader_bridge.py
         └─ requirements.txt
```

Build with the Railroader Unity managed assemblies and Unity Mod Manager:

```powershell
dotnet build -c Release `
  -p:UnityManagedDir="C:\path\to\Railroader_Data\Managed" `
  -p:GameDir="C:\path\to\Railroader"
```

Install the complete `Hrogers.TileEditorBridge` folder in:

`Railroader\Mods\Hrogers.TileEditorBridge`
