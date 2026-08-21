# Changelog

- Adds an opt-in deferred track preview to the F9 Geo workspace. Node and
  segment edits update the yellow guide geometry immediately without repeatedly
  rebuilding Railroader track meshes, switch geometry, or dual-gauge topology.
  **Apply Track** batches the accumulated neighborhood into one runtime rebuild;
  disabling the option safely applies any pending work first. Existing live
  rebuild behavior remains the default.
- Makes local-axis node movement use the node's full rotation, so forward/back
  movement follows its pitch and grade instead of remaining at one elevation.
- Adds in-game smoothing for an ordered chain of existing track nodes. It reads
  current entry/exit grades, holds both endpoint elevations, solves one cubic
  vertical curve, updates elevation and signed pitch together, previews through
  deferred yellow guides, and blocks disconnected chains, junctions, duplicate
  nodes, and solutions that exceed the editor's grade safety limit.
- Refreshes only scenery overlays touched by an edit instead of rescanning and
  refreshing every scenery marker after each object movement.
- Reduces low-end-system editor contention without lowering active editing
  responsiveness. The desktop whole-map view now idles at 15 FPS (5 FPS while
  minimized) and returns to 60 FPS for input, painting, dragging, generation,
  and status feedback.
- Replaces one grade-label `LateUpdate` and `Camera.main` lookup per track
  segment with one shared 20 Hz billboard pass, and coalesces in-game bridge
  heartbeat writes through a single background file writer.
- Adds native **Fence / Wall** object-line authoring to the Spliney workspace.
  Authors can draw and edit repeated rigid scenery modules with spacing, scale,
  model rotation, side/height offsets, terrain snap, slope alignment, endpoint,
  and safety-cap controls. Legacy RailLoader projects show the unsupported tool
  disabled instead of emitting data they cannot preserve.
- Gives all eight vegetation mask values truthful density names, approximate
  mask strength, and practical examples instead of implying each value is a
  fixed biome. Desktop saves now defensively recalculate tile statistics and
  invalidate overview/detail/scale caches even after paste or generation;
  categorical save/reload and Railroader cache invalidation remain covered so a
  saved paint stroke cannot be silently replaced by stale editor data.

## 0.26.8

- Repairs the portable Signals/CTC authoring contract. New CTC documents now
  include the required `trainOrders` array; territory membership follows
  control-point and block creation, rename, and deletion; and saving either
  sidecar adds Railroad Operations (`AITraffic`) to the package requirements
  and load order. Desktop pre-publish validation now resolves every authored
  signal, interlocking, node, segment, block, route, switch, and territory
  reference across `train-signals.json` and `ctc-system.json`.

- Adds a native **Options** workspace for modular packages. Authors can create
  player-facing on/off, choice, and slider settings, select exact track,
  operations, world, progression, or audio targets, edit/delete rules with
  undo/redo, and validate references before export. The editor writes FUSE
  `settings` plus `featureRules`, marks them reload-required, and keeps the
  workspace visibly disabled in RailLoader mode.

- Adds a native **Water** authoring workspace that distinguishes terrain water
  masks from visible lake planes. It creates pointer-placed rectangles, edits
  polygon points and generation settings, reuses stock lake materials/profiles,
  and replaces base lakes through a documented suppression plus editable copy.
  Legacy controls remain visible but disabled because RailLoader has no honest
  lake-polygon representation. Undo/redo and desktop validation cover the new
  `world.waterSurfaces` data.

- Adds **Validate** and **Export ZIP** to the desktop Mod panel. Native FUSE
  operations validation now covers areas, TrackSpans, loads, industries,
  station agents, physical loaders, unique passenger IDs/codes, and reciprocal
  neighbor links. Dependency/base-game references are warnings for add-ons but
  errors for standalone maps that suppress the base world.
- Completes the passenger route form with next-stop travel time, optional map
  feature gating, and validated intermediate timetable points. Native projects
  write `branchDefinitions`; legacy projects receive the supported `branches`
  subset without changing the native schema.

- Adds explicit **Stock-map add-on / Standalone map** creation to both the
  desktop and F9 native-FUSE workflows. Standalone packages include a native
  map declaration and georeferenced `Map/Map.json`; the desktop generator reads
  that origin and keeps its tile list synchronized after generation, deletion,
  and undo. The stock map's historical coordinate correction is confined to
  the stock origin.

- Corrects FUSE-native scenery serialization to write
  `world.scenery.<id>.assetIdentifier`. Earlier editor output that placed native
  scenery at the document root is migrated without discarding ID collisions.
- Corrects base-game object output: native projects now write schema-safe
  `world.sceneClones` IDs with explicit `targetPath` and `path://scene` sources,
  while legacy projects retain RailLoader `mandelas`. Earlier misplaced native
  mandelas migrate without being silently discarded.
- Adds one-step custom Toolshed service-facility placement: installed authored
  load-point discovery, bunker-C/wood presets, track snapping and offsets, and
  coordinated `world.scenery` + `ToolshedServiceFacilities.json` undo/save.
- Exposes the existing turntable builder and moves loader snap controls onto the
  Facilities workflow where they are usable.
- Writes native roads, rivers, and trestles to `world.splineys` with FUSE
  `type` fields, while keeping Strange Customs `handler` records isolated to
  RailLoader output. Earlier root-level native splineys are migrated safely.
- Writes original-pole moves to native `world.telegraphPoleMovements` instead
  of inserting an Alina `TelegraphPoleMover` handler into native splineys;
  RailLoader output retains its compatible handler record.
- Omits empty native area/segment/loader references and validates loader/station
  prefab URIs before saving, preventing files that look authored but fail the
  FUSE schema.
- Makes native FUSE the recommended new-project format and labels RailLoader
  output as a limited compatibility format. Native data remains the authority;
  unsupported legacy-only representations are not silently invented.
- Adds a native base-game industry removal form that writes the exact runtime ID
  to `operations.removals.industries` without deleting unrelated track/scenery.
- Warns when generated turnout geometry is below a 35 m estimated radius, which
  catches the common case where nodes/segments load but Railroader cannot render
  the switch rails.
- Ignores `TrackMeshGenerated` geometry while selecting base-game Objects, so
  town signs and small props beside track no longer lose the click to rail mesh.
- Clarifies native turntable bridge-track ownership and the split between FUSE
  scenery placement and Toolshed diesel/bunker-C service behavior.

## 0.26.7

- Keeps a failed desktop package creation error visible instead of replacing it
  with a misleading success message. Invalid IDs and occupied folders now fail
  closed all the way through the wizard.
- Interpolates terrain brush samples between mouse events in both the desktop
  editor and F9 editor. Fast sculpting/terraforming strokes no longer leave the
  evenly spaced ridges, gaps, or contour-like bands visible in the reported
  hillside.
- Writes vegetation and water as Railroader's exact categorical mask values
  instead of blending bytes that quantize back to the old value on save. New
  mod-owned terrain overrides are declared and hot-mounted through FUSE when
  possible, so a saved vegetation stroke survives reload instead of reverting.

## 0.26.5

- Adds **Create New Mod** to the in-game F9 graph chooser. A user can enter an
  ID, display name, and author; choose a compatible or native FUSE package; and
  begin editing the newly created graph without installing or running the
  desktop editor.
- Rebuilds the desktop **New Mod** workflow with the same explicit formats,
  parent-folder selection, destination preview, and overwrite protection.
- Makes the recommended package one source of truth for both loaders:
  `Definition.json` + `game-graph.json`, loaded directly by RailLoader and
  imported by FUSE. New manifests require only RailLoader and no longer force
  Strange Customs or Alina's Map Mod.
- Adds genuine native FUSE scaffolding with `Info.json`, `FuseDataFiles`, and an
  editable `map.fuse.json` using schema version 1.0 and native removal lists.
- Rejects invalid/reserved mod IDs and non-empty targets instead of producing a
  broken or partially overwritten package.

## 0.26.4

- Distinguishes a short stationary right-click from a held or dragged camera
  gesture in CAMERA FREE mode. A click still deselects everything, while
  right-drag camera navigation preserves the current editor selection.
- Applies the same gesture test to armed placement and terrain tools so using
  the right mouse button to inspect the scene no longer cancels current work.

## 0.26.3

- Leaves a visible draft control marker after the first spline click so the
  starting node is never hidden while choosing the next point.
- Blends the incoming and outgoing directions at every appended control point
  for smoother curves and grade transitions while continuously laying a road,
  river, or bridge spline.

## 0.26.2

- Builds newly placed roads, rivers, and bridges through FUSE's native
  `SplineyAPI` whenever FUSE is loaded. The legacy handler names remain in
  saved RailLoader-compatible JSON, but FUSE no longer falls into its
  non-building Strange Customs compatibility shim at runtime.
- Adds continuous mouse spline laying in F9. Choose Road, River, or Trestle,
  click the first control point, click the second to create the spline, and
  keep clicking to append points; right-click or Esc finishes the tool.
- Calculates each new control point's pitch and heading from the previous
  point so newly drawn spline geometry follows terrain elevation instead of
  starting as a fixed camera-facing strip.

## 0.26.1

- Makes the F9 camera lock an optional navigation aid instead of an editing
  requirement. Track, node, scenery, spliney, pole, terrain, signal, object,
  and operations pointer tools continue updating in both CAMERA FREE and
  CAMERA LOCKED modes.
- Stops switching to CAMERA FREE from canceling active node drags, terrain
  strokes, or pointer-placement previews.
- Temporarily suppresses the normal mouse-camera pan only while a direct
  world-edit gesture is consuming the primary mouse button. Normal FREE-mode
  camera controls resume immediately when the edit gesture ends.

## 0.26.0

- Adds a dedicated desktop **Tile Cleanup** workspace for deleting many
  terrain tiles in one operation. Drag replaces the marked set, Shift-drag
  adds, and Ctrl-drag or right-drag removes tiles from it.
- Adds **Select All** and **Invert / Outside ROW** controls so a generated
  square can be trimmed quickly by marking the right-of-way and inverting the
  selection. Every marked tile is visibly shaded red before deletion.
- Makes batch deletion recoverable: source `.data` files move to a timestamped
  `_TileEditor_Deleted_Tiles` folder with a restore manifest instead of being
  permanently erased. A second confirmation is required and Ctrl+Z restores
  the complete batch, including unsaved terrain pixels.

## 0.25.2

- Adds map-generic Railroad Operations markers for crossings, passenger spots,
  clearance points, switching leads, runarounds, interchange limits, portals,
  mail spots, shop bays, shop stores, physical supply receiving, recovery
  checkpoints, authority limits, ownership boundaries, and trackage rights.
- Binds marker records to native Railroader industry/component, passenger-stop,
  segment, span, node, and track-group identities without moving live train
  orders into the map editor.
- Documents that Form 19/31 and other working paperwork are created and sent
  from Company > Operations; F6 opens a received train-order crew copy.

## 0.25.1

- Fixes yellow segment selection after the camera-navigation update. Track
  nodes, segments, scenery, poles, signals, and other editor overlays remain
  clickable while the camera is FREE; Railroader only starts a mouse pan when
  the drag begins on empty terrain. Camera LOCKED remains available for exact
  placement and dragging.
- Makes the segment hover tooltip show its group and explicitly identify the
  line as editable. Selecting a segment now points directly to the Track form
  for group, gauge, style, class, and control-node editing.
- Adds a Segment ID/name field to that form. Renaming creates the new live
  graph segment, preserves geometry and properties, updates segment references
  stored inside the current graph document, and remains part of graph undo.

## 0.25.0

- Adds a hold-to-view **Shift+? Track Survey** HUD. The pointer readout shows
  stable map/game coordinates, floating Unity world coordinates, graph-local
  and terrain-tile-local coordinates, the real loaded tile ID, and nearby
  track segment details including signed grade, heading, chainage, gauge,
  class, and group.
- Separates editor input protection from camera navigation. Opening F9 leaves
  Railroader's normal mouse camera available; middle mouse toggles an
  editing-safe camera lock. Locked mode retains native W/A/S/D movement and
  speed modifiers, wheel zoom, and Q/E rotation while suppressing mouse pan
  and orbit. The header always shows the current camera state and can also be
  clicked to toggle it.

## 0.24.0

- Adds one-time **Snap to Track** and persistent **Snap + Lock** controls for
  semaphore placement. Pointer placement can snap to the clicked or nearest
  Bezier segment with configurable left/right, lateral, and vertical offsets.
- Saves locked masts using a segment ID, Bezier parameter, and local transform
  offsets. Locked signals follow subsequent curve, grade, and elevation edits
  in F9 and during normal gameplay through Signal Runtime 1.7.0. Existing
  signals can be snapped, locked in place without snapping, or unlocked while
  retaining their current world transform. Generated diamond signals are
  track-locked automatically.
- Makes the existing OBJECTS behavior explicit for Railroader's original
  signs. Selected base-game signs receive a prominent visible/hidden toggle;
  loader-added signs remain in SCENERY and are excluded from this control.

## 0.23.1

- Separates territory authoring from railroad operation. F9 now presents the
  signal/CTC schematic as a selection and configuration preview and keeps
  train orders in authoring mode; live dispatcher and crew controls are in
  Railroader's normal Company > Operations window through Signal Runtime
  1.6.0.

## 0.23.0

- Completes the live train-order workflow: dispatcher issue, delivery to an
  actual Railroader train crew, authenticated crew repeat/acknowledgement,
  fulfillment, cancellation, timestamps, and an audit reason are synchronized
  through Railroader's host-authoritative property state.
- Adds a standalone F8 train-order window supplied by Signal Runtime. Crew
  members can read and acknowledge orders in multiplayer without installing
  or opening Tile Editor; Form 31 uses an explicit sign/repeat action.
- Adds enforceable block authorities and optional order speed limits. A
  delivered but unacknowledged order holds the assigned train. After
  acknowledgement, the host permits only the union of the order's authored
  blocks and holds at the authority limit.
- Integrates authority limits with Auto Engineer's real stopping-distance
  target and guards manually driven locomotives through replicated throttle
  and brake controls. Player-owned Waypoint trains use the same enforcement.
- Adds live train-crew selection and dispatcher controls to Operations >
  Orders, plus quick block assignment and an enforcement toggle when writing
  an order.
- Synchronizes CTC switch/route requests and board indications as well. Remote
  dispatcher clients use authenticated commands, while the host publishes the
  active route, phase, reason, and corresponding semaphore clearance to every
  client.

## 0.22.0

- Adds dedicated **Signals** and **Orders** pages to the Operations workspace
  as the first complete-territory signaling foundation. Territory can be
  modeled as timetable/train-order, ABS, or CTC while retaining a 1900-1950s
  semaphore presentation.
- Adds a live schematic CTC board with Normal/Reverse switch correspondence,
  Main/Diverging route buttons, Stop/cancel, live runtime phase, and compact
  track diagrams for every authored control point.
- Adds in-world authoring for CTC control points from clicked turnout nodes.
  Each control point stores portable board coordinates, power-switch labels,
  entry signal IDs, route block IDs, and switch settings in
  `ctc-system.json` beside the selected map graph.
- Adds ABS/CTC/manual block authoring from clicked track segments. Blocks can
  span multiple segments, own signals at both ends, and name the next block in
  each direction for three-aspect Stop/Approach/Clear logic.
- Adds a period train-order office with numbered Form 19, Form 31, track
  warrant, meet, hold, and run-extra orders; train/crew, authority limits,
  meet point, effective/expiry text, priority, instructions, and lifecycle
  status are saved portably.
- Extends the standalone Signal Runtime with safe host-authoritative switch
  commands, CTC route conflict checks, switch locking and correspondence,
  block occupancy, automatic route release, and two-direction ABS aspects.
  Normal gameplay still does not require Tile Editor.

## 0.21.0

- Makes generated four-signal diamonds functional through the standalone
  Signal Runtime. Live Railroader car locations request routes on the saved
  approach chains; only one of A1/A2/B1/B2 can clear, conflicting semaphores
  remain at Stop, the route locks as the train enters, and it releases only
  after the diamond clears.
- Adds compact live interlock controls to each generated approach signal:
  runtime state, automatic/manual mode, request-this-approach, and fail-safe
  release. A release request is refused while either crossing route is
  occupied.
- Adds a one-click block recalculation after independently moving a signal
  mast. The editor keeps the exact hand-set transform, finds the nearest
  segment on that saved approach, and rewrites its protected chain, approach
  binding, direction, and interlocking approach node as one undoable edit.
- Saves automatic operation and configurable release/cancel timing with every
  newly generated diamond. Existing `train-signals.json` diamonds default to
  automatic operation without requiring a rebuild.

## 0.20.1

- Makes the fixed footer context-aware on the Signals workspace. Its Undo and
  Redo buttons now operate on `train-signals.json`, so a generated four-signal
  diamond can be removed or restored as one edit without hunting for separate
  controls inside the scrolling signal form.
- Shows the signal undo/redo history counts in the footer and labels the third
  control `Signals Auto-Saved` to make clear that signal edits are written
  independently from `game-graph.json`.

## 0.20.0

- Makes diamond signal placement and block metadata traverse connected track
  chains instead of stopping at the two selected crossing segments. A 500-800
  m signal setback can now cross any number of normal segment boundaries.
- Saves `protectedSegmentIds` for the complete block from each semaphore to
  the diamond and `approachSegmentIds` for the longer approach-locking chain.
  Each Railroad A/B route also saves its combined `segmentIds` collection.
- Follows the sole continuation automatically. At a turnout or other
  multi-choice node, scores heading alignment first while preferring matching
  graph group, gauge, track class, and style; the build result reports every
  ambiguous continuation it resolved.
- Raises the supported signal setback to 5,000 m and changes the diamond
  builder default to 600 m for realistic interlocking spacing.
- Extends Signal Runtime's public signal and route records with the full
  protected, approach, and interlocking route segment chains.

## 0.19.0

- Adds a four-signal railroad Diamond Interlocking builder to the Signals
  workspace. Mark the two non-connecting crossing segments as Railroad A and
  Railroad B, verify the detected crossing point/angle, then generate A1, A2,
  B1, and B2 semaphore approaches in one undoable operation.
- Samples the real Bezier curves to find the crossing instead of assuming a
  midpoint. A vertical-separation check rejects overpasses, while signal
  setback, side offset, vertical offset, head count, approach-locking length,
  and release-block length remain configurable.
- Keeps every generated signal as an independent record and amber world
  object. Each can be moved, raised/lowered, rotated on all axes, flipped,
  renamed, enabled/disabled, rebound, or aspect-tested after generation.
  Renames and deletes update the diamond route's signal references.
- Exposes parsed diamond route metadata through Signal Runtime
  `Main.Interlockings`, including crossing point/angle, Railroad A/B segment
  IDs, four signal IDs, approach node IDs, and locking lengths.

## 0.18.0

- Adds a dedicated in-game `SIGNALS` workspace for actual base-game animated
  semaphore assemblies rather than decorative signal scenery. Place one-,
  two-, or three-head signals at the mouse pointer, select their amber world
  masts, move in WORLD/LOCAL axes, rotate/flip them, edit exact transforms,
  enable/disable, delete, and test signal aspects.
- Writes portable `train-signals.json` beside the selected graph with stable
  signal IDs plus interlocking ID, protected node, protected segment, travel
  direction, head count, transform, and starting aspect fields.
- Adds the separately installable Hrogers Train Signal Runtime. It clones the
  base game's CTC semaphore model while removing the original CTC behavior and
  pickable so duplicate vanilla IDs cannot be created. The blade animation,
  materials, and culling remain available to an external interlocking mod.
- Exposes runtime signal discovery and aspect controls through
  `Main.Signals`, `TryGetSignal`, and `TrySetAspect`. Normal gameplay requires
  only the map and Signal Runtime, not Tile Editor.

## 0.17.0

- Moves authored grade crossings out of AI Traffic's private configuration
  and into a portable `grade-crossings.json` stored with the edited map mod.
- Adds the separately installable Hrogers Grade Crossing Runtime. It discovers
  portable crossing files without loading the Tile Editor or AI Traffic and
  registers native `TrackMarkerType.Crossing` markers in Railroader's shared
  graph.
- Registers each crossing on every connected approach segment by default.
  Railroader's native crossing signaler therefore works from either direction
  for unowned AI trains and player-owned locomotives using Waypoint Auto
  Engineer mode.
- Keeps placed signal and crossing-signal models as ordinary persisted scenery
  entries. Players need the model's asset pack, but not the Tile Editor.

## 0.16.9

- Adds click-position insertion on a selected track segment. The click is
  projected onto the exact Bezier instead of forcing a midpoint split.
- Makes Add +10 m explicitly preserve the selected node's grade and pitch,
  adds World/Local movement for spline points, and reduces a node transform
  to one coalesced TrackObjectManager rebuild request.
- Prevents track, spline, scenery, pole, and operations markers beneath the
  F9 or Node Editor windows from receiving clicks.
- Adds one-click turnout-stand flipping and persistent on/off control for the
  physical bumper generated at a dead-end node. Bumper state is stored beside
  the graph in `tile-editor-track-overrides.json`, keeping the RailLoader/FUSE
  graph schema untouched.
- Adds visual-only Signals, Crossings, and Signs scenery filters. Base-game
  signs remain disableable through the Objects workspace.
- Adds functional Auto Engineer grade-crossing marker toggles for selected
  track nodes when AI Traffic is installed, followed by a live configuration
  reload.
- Limits graph, spline, pole, terrain, crossing, override, and full deployment
  backups to the newest three copies.

## 0.16.8

- Adds a deliberate F9 camera-unlock modifier without restoring accidental
  mouse flight. Hold the middle mouse button to temporarily return normal
  Railroader mouse pan, orbit, and zoom controls; Tile Editor ignores world
  clicks during that hold. Release middle mouse to resume editing.

## 0.16.7

- Adds a guarded `Clear Cache` button to the desktop OSM toolbar and the F9
  Terrain OSM controls. Both show the current downloaded tile count and disk
  usage, require a confirmation click, clear decoded map textures, and prevent
  downloads already in flight from repopulating the cleared cache.
- Excludes `Cache/OSM` from future live-mod deployment backups so packaging a
  new editor version does not duplicate downloaded map tiles.

## 0.16.6

- Makes right-click in the world the universal editor cancel gesture. It
  deselects track nodes/segments, Spliney points, scenery, poles, base-game
  objects, and operations entries while cancelling node drags, pointer
  placement, bridge picking, connections, Fit Arc chains, TrackSpan starts,
  pole wiring, terrain strokes, and pending delete confirmations.
- Reserves mouse input for editing while F9 is open. Railroader's raw
  left-drag pan, right-drag orbit, mouse-look, and wheel zoom are suppressed;
  strategy-camera movement remains available through W/A/S/D with the normal
  fast-movement modifiers.
- Keeps Bridge Directly from Track armed after each successful build. The
  consumed segment and new trestle point are cleared, yellow overlays return,
  and the editor immediately waits for the next track segment.
- Adds an explicit `Build Another Bridge` action when an existing trestle
  control point is selected, plus a persistent control hint in the panel
  footer.

## 0.16.5

- Merges loaded RailLoader `SCAssetPacks` into the searchable scenery palette,
  including late-registered assets omitted from Railroader's initial catalog.
  Only identifiers the live scenery manager can actually resolve are shown.
- Builds an AutoTrestle from the selected TrackSegment's two endpoint controls
  and rotations. This reproduces the same single Bezier span without creating
  redundant control nodes every eight meters.
- Discovers loaded base-game roads and rivers as editable Spliney sources even
  when they are absent from the selected mod graph. Their real control points
  can be moved and rotated; the first edit creates a same-name graph override.
- Rebuilds each edited base or mod road/river through `RiverPath.Rebuild`, so
  its height, roadbed, water, dirt, object, and vegetation-mask modifiers
  invalidate the affected terrain tiles.
- Adds a small screen-space selection halo after normal collider and renderer
  ray picking misses, making thin Mandela signs and small props easier to
  select without allowing large scene roots.

## 0.16.4

- Moves node naming, movement, rotation, exact transform editing, clipboard
  controls, and node actions out of the main Geo tool surface into a dedicated
  Node Editor window.
- Keeps a compact selected-node card and fast `Place Next` / `Add +10 m`
  controls in Geo so track can be extended without reopening the full
  transform workspace.
- Makes the Node Editor independently movable, resizable, scrollable, and
  persistent across F9 sessions. Pointer picking and Ctrl-drag treat both
  editor windows as protected UI.
- Allows the Node Editor to remain open while live node selection changes, and
  provides free-node mouse placement when no node is selected.

## 0.16.3

- Expands industry component creation using the supplied EFA production
  `industries.json` (17 areas, 67 loads, and 71 active components) as the
  compatibility reference instead of relying on starter defaults.
- Adds editable daily storage change, maximum storage, car transfer rate,
  empty/loaded ordering, shared-storage, load, car-filter, and multi-TrackSpan
  fields for rail loaders and unloaders.
- Adds formulaic per-day input/output maps and multi-entry team-track
  import/export profiles with ideal-car counts.
- Adds passenger stop ID, timetable code, population, branch, and neighbor
  controls; repair overhaul; interchanged loader/unloader fields; progression;
  and multiple interchange/repair spans.
- Adds a collapsed advanced component drawer for cost, service hours, fill
  fraction, book reasons, title, and arbitrary typed JSON. Native FUSE stores
  custom values under `fields`; legacy output safely merges non-conflicting
  handler properties at component level.
- Validates numeric ranges and structured formula/team/custom input before an
  atomic document edit, preserving the prior document on any error.

## 0.16.2

- Moves the Turntable builder out of the Operations tool row and into a
  dedicated Geo tool beside Arc, Turnout, Wye, and the new Span tool.
- Shows the configured turntable pit radius directly under the mouse before
  placement instead of using a generic small pointer marker.
- Adds whole-segment, measured partial, and connected multi-segment TrackSpan
  creation. Multi-segment mode marks a start segment/distance, then builds to
  a selected connected end segment/distance.
- Writes each TrackSpan endpoint using the nearest RailLoader Start/End or
  native FUSE A/B reference and validates range and graph connectivity before
  changing the document.

## 0.16.1

- Makes multi-entry operations and engine-facility profile creation fully
  atomic: if any validation fails, the complete pre-click document and
  operations selection are restored instead of leaving a partial facility.

## 0.16.0

- Adds a dedicated `OPERATIONS` workspace with cached search, selection, and
  colored in-world overlays for towns, TrackSpans, industries, passenger
  stops, freight components, commodities, engine-service objects, station
  agents, and turntables.
- Adds mouse-pointer placement for towns, industries, physical coal/water/fuel
  loaders, custom service prefabs, FUSE station agents, and turntable centers.
- Creates whole-segment TrackSpans from clicked track, plus receiving,
  shipping, passenger, repair, team-track, interchange, and custom industry
  components using RailLoader or native FUSE field names as appropriate.
- Adds one-click steam, diesel, and combined engine-terminal operating
  profiles while keeping physical service objects independently placeable.
- Adds native FUSE turntable output with radius, subdivisions, bridge-track
  gauge, optional roundhouse stalls, and standard/narrow-gauge presets.
  Legacy layers retain Alina TurntableBuilder entries and clearly expose the
  runtime dependency.
- Makes all operations document changes atomic, undoable, dirty-tracked, and
  compatible with the existing Save Graph and desktop ownership locks.

## 0.15.10

- Reduces F9 editor draw calls and material allocations by sharing one
  vertex-colored material across track, Spliney, scenery, and pole overlays.
- Automatically sleeps distant track lines, node markers, direction arrows,
  and pick colliders, then wakes them as the camera moves. The active radius
  expands when using a high aerial camera.
- Removes a quadratic scene search while creating segment overlays, reduces
  long-segment collider density, caps direction chevrons, and slows
  nonessential live-scene reconciliation.
- Caches scenery asset search pages between Unity IMGUI layout/repaint passes
  instead of filtering and allocating the complete catalog repeatedly.
- Adds a remembered node ID prefix and readable name pattern shared by Node,
  Pieces, Arc, Grade, Parallel, Turnout, and Wye. Unique numeric suffixes are
  assigned automatically.

## 0.15.9

- Puts accessible WORLD/LOCAL movement toggles directly beside the primary
  movement controls for both scenery assets and telegraph poles. LOCAL plan
  movement follows the selected object's heading while elevation stays
  vertical.
- Gives poles the same full Pitch X, Heading Y, and Roll Z rotation controls
  used by track and scenery, including quick steps and exact rotation fields.
- Saves complete transforms on Tile Editor-created poles and adds portable
  rotation overrides for original map poles while retaining compatible
  cumulative `TelegraphPoleMover` position offsets.
- Makes pole rotation participate in pole undo, redo, dirty state, save, live
  rebuilding, and desktop synchronization.

## 0.15.8

- Adds Track Class controls to the F9 selected-segment editor for Mainline,
  Branch, and Industrial, the three classes supported by Railroader.
- Highlights the segment's active class and explains Railroader's default
  35/25/15 mph behavior when a segment's explicit speed limit is zero.
- Makes class changes undoable, writes the correct RailLoader or native FUSE
  spelling, republishes FUSE segment metadata, and rebuilds only the affected
  track endpoints.

## 0.15.7

- Prevents straight 3-foot track editing from launching FUSE Narrow Gauge's
  whole-map ghost-rail, turnout-analysis, and track-mesh rebuild after every
  new node or segment.
- Batches all changed FUSE segment definitions once per editor action,
  consumes FUSE's redundant pending full-rebuild request, refreshes the
  narrow-gauge metadata cache directly, and rebuilds only affected endpoints.
- Uses the targeted path for free nodes, `Add +10 m`, mouse placement,
  Shift-click connections, Pieces, Arc, Grade, Turnout, Wye, Parallel,
  injected nodes, segment style/group changes, and gauge changes.
- Keeps expensive full Narrow Gauge synchronization deferred and coalesced
  for dual-gauge edits touching shared/ghost rail topology. Pure 3-foot
  turnouts stay on the targeted path; `Sync Gauge Visuals` remains available
  when an explicit whole-graph refresh is wanted.
- Corrects `Add +10 m` to calculate forward movement in graph-local
  coordinates while preserving the selected node's heading, grade, and bank.

## 0.15.6

- Adds an `Add +10 m` button directly beside Split, Level, and Flip in the
  selected-node controls.
- Each click creates a connected node ten meters forward using the selected
  node's heading, grade, and bank, selects that new node, and lets repeated
  clicks extend a constant-heading/constant-grade run without using the
  pointer.
- Reorganizes node movement into a compact, labeled direction pad with
  separate elevation controls, bold directional glyphs, WORLD/LOCAL context,
  and hover descriptions.
- Groups pitch, heading, and roll into three accessible rotation cards with
  paired counter-clockwise/clockwise controls and higher-contrast button
  states.

## 0.15.5

- Keeps yellow segment lines and their click geometry in a protected,
  graph-level overlay layer instead of parenting them under track segments
  that Railroader replaces during Rebuild Track, undo, redo, and full graph
  rebuilds.
- Indexes overlays by segment ID, removes stale entries with deleted track,
  and restores hidden overlays reliably when F9 Geo mode returns.
- Reports whether FUSE Narrow Gauge is actually loaded. A saved 3-foot or
  dual-gauge value now clearly says when the companion runtime and a
  Railroader restart are still required to change the visible rails.
- Adds `Sync Gauge Visuals` when FUSE Narrow Gauge is active, republishing the
  live segment definitions and requesting its native graph/track rebuild.

## 0.15.4

- Makes node-property copy selective as well as paste selective. F9 can now
  copy only Elevation, Grade, Heading, Bank, Rotation X/Y/Z, Elevation +
  Grade, Elevation + Rotation, Switch Flag, or All Settings.
- Records exactly which fields the clipboard contains and disables any paste
  action requiring properties that were not copied.
- Adds a prominent `Place Free Node` action beside connected placement in the
  primary selected-node workspace. The mouse-placed node is independent,
  becomes selected immediately, and can be used as the start of a new track
  alignment.

## 0.15.3

- Adds a compact reusable F9 node-property clipboard. `Copy Node Settings`
  captures elevation, pitch/grade, heading, bank, full rotation, and the
  switch-stand flag from the selected track node.
- Adds targeted paste actions for Elevation, Grade, Heading, Bank, Rotation
  X/Y/Z, Elevation + Grade, Elevation + Rotation, Switch Flag, and All
  Settings.
- Explicitly preserves the target node's X/Z position, ID, and connections
  during property paste so matching elevations or rotations cannot collapse
  track nodes together.
- Routes each paste through the existing lightweight connected-track update,
  JSON write, overlay repair, Undo, Redo, and desktop synchronization paths.

## 0.15.2

- Mirrors the desktop editor's practical OSM zoom range in F9 with explicit
  `Overview z15`, `Detail z16`, `Sharp z17`, and `Ultra z18` presets.
- Makes z17 the default for new installs and reports its approximate
  meters-per-pixel resolution directly in the Terrain panel.
- Streams detail adaptively: Sharp covers the nearby 3 x 3 working area;
  Ultra is used beneath the camera, Sharp surrounds it, and Detail fills the
  rest of the selected 5 x 5 or 8 x 8 coverage window.
- Loads the camera tile first and drops obsolete queued downloads when the
  camera changes terrain tiles, improving high-resolution response without
  increasing the two-request network limit.
- Builds OSM textures with mipmaps, trilinear filtering, eight-level
  anisotropic filtering, and a small mip bias so map details remain clearer
  in low, angled camera views.

## 0.15.1

- Adds a default `Clear Lines` OSM display that makes the pale map background
  almost transparent while preserving higher-contrast roads, rivers,
  buildings, labels, and boundaries over the live terrain.
- Keeps `Full Map` as an immediate in-game toggle for the original complete
  OSM raster.
- Applies opacity through both built-in and URP shader color properties and
  explicit alpha blending so `Fade -` / `Fade +` reliably change the overlay.
- Retains readable downloaded textures in memory only while processing them;
  the disk cache remains the original reusable PNG data.
- Makes live packaging preserve the cache DLL belonging to a currently
  running Railroader process while still removing stale cache copies. The new
  assembly becomes active on the next game restart.

## 0.15.0

- Adds a streamed in-game OpenStreetMap terrain guide with selectable 5 x 5
  and 8 x 8 game-tile windows, zoom, opacity, disk caching, and required map
  attribution.
- Drapes OSM imagery over 32-section terrain-following meshes, tracks
  Railroader's floating origin, refreshes while sculpting, and unloads mesh
  and texture memory outside the moving camera window.
- Reads the active map's `Map.json` origin and tile calibration so F9 and the
  desktop editor use the same OSM alignment. Downloads are limited to two at
  once to avoid panel stutter and tile-server bursts.
- Renames the vague dual-gauge `FLIP` choice to `DUAL T`, explains that it is
  one fixed shared-rail transition segment, blocks through-chain application,
  and validates that the selected transition has one dual neighbor at each
  end with opposite explicit L/R sides.
- Prevents Mandela selection from promoting a clicked building or prop into
  a town, map, or world-scene aggregate. Geometric growth and renderer-count
  guards stop `One Level Up` at the individual-asset boundary.
- Routes broad scene colliders to the renderer fallback so clicking Bryson
  Station selects the individual visual asset instead of moving the shared
  world hierarchy.

## 0.14.0

- Adds first-class standard, three-foot narrow, automatic dual gauge, explicit
  left/right shared rail, and shared-rail flip metadata to F9 and desktop
  track editing.
- Makes every new track builder inherit the active gauge while split, merge,
  rename, reverse, and property edits preserve the source segment gauge.
- Adds single-segment and degree-two through-chain gauge application with
  gauge-specific live and desktop overlay colors.
- Preserves unknown companion metadata and custom segment fields instead of
  replacing the complete JSON object during normal edits.
- Publishes live gauge changes through FUSE `TrackAPI` and requests one
  Narrow Gauge synchronization pass when `FUSE.NarrowGauge` is loaded.
- Discovers native FUSE track fragments from `Info.json` `FuseDataFiles` and
  keeps FUSE `startNodeId` / `endNodeId` and removal-list semantics intact.
- Documents the turntable backend plan: portable converted legacy turntables
  plus native FUSE operations for RLW narrow-gauge visuals and controllers.

## 0.13.1

- Adds `Rebuild Track` directly beside `Rebuild Terrain` at the top of the F9
  Geo workspace.
- Runs Railroader's complete track rebuild and schedules Tile Editor's bounded
  overlay-repair passes so node and segment lines return after rebuilding.

## 0.13.0

- Adds a dedicated F9 `OBJECTS` workspace for base-game scene objects
  (RailLoader mandelas / FUSE scene clones).
- Selects buildings and props directly under the mouse, including a
  renderer-bounds fallback for objects without useful physics colliders.
- Automatically promotes a clicked mesh toward a useful object root while
  retaining `Select Parent` and `Closer to Clicked Part` controls for unusual
  hierarchies.
- Gives selected base objects the same primary world/local movement,
  pitch/heading/roll rotation, scale, and exact parent-relative transform
  controls used elsewhere in Tile Editor.
- Clones safe base-game objects beside the source or at an exact mouse-pointer
  location. Sources containing saved `KeyValueObject` state are blocked, and
  loader scenery is routed to the normal Scenery workspace.
- Supports enable/disable, clone deletion, saved-override browsing and search,
  unified Undo/Redo, atomic graph save, and desktop synchronization.
- Saves one legacy `mandelas` definition that Strange Customs applies
  directly and FUSE converts to `world.sceneClones`, keeping the selected mod
  portable between both runtimes.
- Stores all scene-object transforms in parent-local coordinates to prevent
  floating-origin or world/local teleport errors on reload.

## 0.12.9

- Adds prominent WORLD and LOCAL movement buttons beside the primary F9 node
  movement controls.
- Keeps WORLD movement aligned to map X/Y/Z and makes LOCAL X/Z follow the
  selected track node's heading while Raise/Lower remains vertical.
- Replaces full graph JSON snapshots for simple node transforms with compact
  node/connected-segment undo records.
- Uses Railroader's targeted, debounced node rebuild queue instead of
  rebuilding the entire railroad after every movement click.
- Removes per-frame destruction/recreation of segment arrow renderers and
  pick colliders during Ctrl-drag. The curve preview updates at 30 Hz and pick
  geometry refreshes once movement settles.
- Replaces the scenery menu's hidden first-12-assets cap with a paginated
  catalog showing 16 assets per page, result totals, and Previous/Next
  navigation. Search spans the complete loaded scenery library.
- Locks normal Railroader panel, teleport, and gameplay shortcuts while F9 is
  open, while preserving camera movement and Tile Editor world picking.
- Restores only the input actions Tile Editor disabled when F9 closes. Direct
  terrain/object clicks also suppress normal in-game object activation.

## 0.12.8

- Adds the selected segment's `groupId` to the F9 Geo track workspace.
- Provides direct Apply and Clear controls while showing the active group in
  the selection header.
- Makes group changes undoable/redoable and writes them through the normal
  graph JSON save and live-track rebuild path.

## 0.12.7

- Replaces the wall of individual game-graph JSON files with a compact
  mod-first chooser.
- Automatically selects each mod's likely main graph while retaining every
  mixinto layer behind `More Layers` for advanced editing.
- Builds new bridges and trestles through Strange Customs when its live
  builder is available, or through FUSE's native Spliney API when it is not.
- Keeps the saved legacy trestle definition portable between both runtimes
  and unregisters FUSE-built trestles correctly during Undo and Delete.

## 0.12.6

- Makes Rebuild Terrain explicitly requeue visible tiles around the camera
  after Railroader clears and reloads its terrain store.
- Keeps the button in a `Rebuilding Terrain...` state until the camera tile is
  ready, and reports success, an unavailable map manager, or a timed-out tile
  load instead of claiming immediate success.
- Prevents rebuilds from discarding unsaved desktop or in-game terrain edits.
- Derives the packaged desktop editor's Railroader root from its installed
  `Mods` location and passes that path through `TILE_EDITOR_GAME_DIR`.
- Removes developer PDB/source paths from the release DLL and fails packaging
  if the staged release contains the build machine's source or user path.
- Deletes stale Unity Mod Manager DLL cache files during a live deployment so
  an older bridge cannot remain active after an update.

## 0.12.5

- Replaces the PATH-only desktop launcher check with shared Windows Python
  discovery.
- Finds compatible 64-bit Python 3.10+ through `TILE_EDITOR_PYTHON`, the
  official `py` launcher, PATH, per-user and machine registry entries,
  python.org install folders, Conda, Scoop, and common portable locations.
- Adds `Launch Tile Editor.bat --diagnose-python` to report the exact
  interpreter selected without starting the editor.
- Detects a broken or moved packaged `.venv` and safely rebuilds that
  disposable environment with a newly discovered interpreter.
- Makes `Repair Tile Editor Environment.bat` capable of creating a missing
  environment instead of requiring a successful first launch.
- Accepts supported Python installations that are not added to PATH and gives
  an exact `TILE_EDITOR_PYTHON` recovery option when discovery fails.
- Adds Ctrl-drag world movement for track nodes. Live node and connected
  segment overlays follow the cursor without rebuilding the complete map
  every frame.
- Commits the full drag as one grouped Undo/Redo edit and writes the final
  stable game position through the normal graph-save/synchronization path.
- Highlights a node under the drop cursor in green and connects it to the
  dragged node on release. The dragged node remains at its last terrain
  position to avoid a zero-length segment.

## 0.12.3

- Moves full Spliney-point rotation into the primary workspace instead of
  hiding pitch and roll under advanced controls.
- Gives road, river, bridge, and trestle points the same six Pitch X,
  Heading Y, and Roll Z arrow controls used by nodes and scenery.
- Expands Spliney movement and rotation presets to the complete node ranges:
  0.01 to 1000 m and 0.01 to 180 degrees.
- Adds Level X/Z, Reset Rotation, and Flip Y 180 actions while keeping exact
  position, rotation, and width fields under `More...`.
- Continues saving every point rotation in stable game coordinates and
  rebuilding the live Spliney after each adjustment.

## 0.12.2

- Adds direct click-then-Shift-click track connection: click the first cyan
  node normally, then Shift-click the second node to create the segment.
- Leaves the second node selected after connecting so additional Shift-clicks
  can rapidly build a node-to-node chain.
- Routes shortcut connections through the existing grouped edit, overlay
  repair, Undo/Redo, graph save, and desktop synchronization path.
- Rejects self-connections and duplicate segments with an in-panel status
  message instead of throwing out of the world-click handler.

## 0.12.1

- Fixes a desktop-editor crash when the navigation bar checked for unsaved
  terrain at the same moment the terrain loader thread added a tile.
- Makes the F9 editor fully usable without starting the desktop Tile Editor.
  An installed RailLoader mod/game-graph can be selected directly in game.
- Keeps `Change Mod / Graph` available after a layer is open across Geo,
  Scenery, Poles, and Terrain instead of showing the chooser only at startup.
- Remembers the selected in-game graph and automatically reopens it during
  later sessions whenever the desktop editor is offline.
- Ignores stale desktop heartbeat files for automatic graph selection.
- Prevents graph switching while track/scenery, Spliney, telegraph-pole, or
  terrain changes remain unsaved.

## 0.12.0

- Splits the in-game Terrain workspace into `Sculpt Terrain` and
  `Surface Paint`, keeping vegetation and water masks independent from height
  editing.
- Adds task-focused Building Pad, Path/Road, Grade Plane, Ditch, and Berm
  brushes plus presets for buildings, track/roads, walkways, ditches, and
  embankments.
- Reworks Flatten, Set Height, Noise, and related height brushes to converge
  toward stable targets instead of accumulating overshoot. A per-stroke
  maximum cut/fill clamp prevents extreme spikes.
- Makes Building Pad hold the first sampled elevation for the complete stroke,
  Path/Road follow the route while removing cross-slope, and Grade Plane use
  an anchored heading and grade.
- Makes desktop and in-game saves synchronize both ways for graph
  track/scenery data, road/river/bridge Splineys, telegraph-pole sidecars, and
  terrain tiles.
- Publishes dirty ownership for both editors and blocks conflicting graph or
  terrain mutations while the other editor has unsaved work.
- Preserves timestamped game or desktop conflict copies before an incoming
  reload replaces unsaved graph, Spliney, or terrain data.
- Uses atomic desktop terrain-tile replacement with one timestamped backup,
  then reloads only the corresponding live terrain data.

## 0.11.1

- Prevents a raw terrain rebuild while painted terrain changes are unsaved,
  avoiding accidental loss of the current terrain stroke history.

## 0.11.0

- Replaces camera-only creation for track nodes, scenery assets, and telegraph
  poles with an exact mouse-pointer placement workflow and a visible world
  target. Repeat mode supports continuous track, scenery, and pole laying.
- Connected node placement creates the new node and its track segment as one
  undoable operation; connected pole placement keeps advancing the selected
  end of the line.
- Adds a dedicated resizable `TERRAIN` tab with live Raise, Lower, Flatten,
  Smooth, Set Height, and Noise brushes.
- Adds direct painting for Railroader's vegetation IDs 0 through 7 and the
  water mask, with Circle/Square footprints plus Hard, Linear, Smooth, and
  Gaussian falloff.
- Adds radius and strength presets, adjustable brush spacing, height sampling,
  noise scale/amplitude, an exact world-space footprint ring, and keyboard or
  mouse-wheel brush-size shortcuts.
- Adds terrain-specific grouped Undo/Redo. Undo records store only touched
  height and mask samples so normal brush strokes do not copy whole terrain
  tiles or reintroduce the prior panel stutter.
- Saves edited terrain in Railroader's native packed PNG `.data` format,
  makes one timestamped backup per source tile, writes mod-owned tiles in
  place, and creates a mod override instead of overwriting base-game data.
- Rebuilds only saved loaded tiles unless a new mod override requires a full
  terrain rescan.

## 0.10.0

- Adds a dedicated `POLES` tab so telegraph-line work no longer shares the
  Scenery workspace.
- Creates real live `SimpleGraph` pole nodes at the camera ground target and
  real graph edges for their wires.
- Supports a fast continue-the-line workflow: select an amber pole, aim the
  camera, and add a connected pole; the new pole becomes the next selection.
- Adds nearest-pole connection distance, standalone creation, manual
  start-to-destination wire connections, and removal of Tile Editor wires.
- Adds movement, exact position, persistent heading rotation, and
  confirmation-protected deletion for Tile Editor-created poles while keeping
  the existing cumulative `TelegraphPoleMover` path for original map poles.
- Saves new nodes and wires beside the owning map mod in
  `tile-editor-telegraph-poles.json`. The UMM bridge discovers and restores
  these files automatically on later game launches.
- Applies saved custom poles from every installed mod in one batched graph
  update, pausing the native pole manager and rebuilding it once to avoid
  creation-time stutter.
- Keeps stable game coordinates in the sidecar and converts only at the live
  Unity boundary, preventing floating-origin teleports.

## 0.9.2

- Adds live telegraph-pole selection and movement to the in-game Scenery
  workspace.
- Displays nearby numbered poles with amber clickable markers; the selected
  pole turns magenta.
- Adds the same six-direction movement pad and common movement steps used by
  track, scenery, and Spliney points, plus exact stable game-coordinate entry.
- Reads and updates the owning `AlinasMapMod.TelegraphPoleMover` entry instead
  of treating poles as ordinary scenery assets.
- Preserves the mover schema by accumulating edits into matching
  `polesToMove` and `poleMovement` rows, including poles that already have
  custom offsets.
- Converts only at the live Unity boundary with `WorldToGame`/`GameToWorld`,
  preventing floating-origin coordinates from being persisted.
- Rebuilds the TelegraphPoleManager after movement so connected wires and
  culling positions follow the moved pole.
- Adds a reset-to-original-offset command, dedicated pole undo/redo, atomic
  save, and timestamped backup of the owning telegraph-pole JSON layer.

## 0.9.1

- Adds `Bridge Directly from Track` to the in-game Spliney workspace.
- Temporarily exposes the normal yellow track overlays so a track segment can
  be clicked directly; the selected segment turns green and reports its live
  length.
- Samples Railroader's native high-accuracy 3D segment curve by distance,
  preserving horizontal curvature, grade, pitch, crests, sags, and endpoints.
- Places every generated bridge control point an adjustable distance below the
  rail, defaulting to 0.30 m.
- Adds adjustable control-point spacing from 1 through 50 m, defaulting to
  8 m, with a bounded maximum of 257 samples.
- Keeps bridge name and independent Block/Bent start/end styles configurable
  before the one-click live build.
- Treats track-derived bridge creation as one normal Spliney undo/redo/save
  operation and automatically leaves track-picking mode after a successful
  build.

## 0.9.0

- Expands the in-game Spliney workspace from road/river point editing into a
  complete Road, River, and AutoTrestle bridge creator and editor.
- Places new two-point splineys at the camera target with editable name,
  initial length, road/river width, loaded Strange Customs profile, and bridge
  end styles.
- Discovers `StrangeCustoms.AutoTrestleBuilder` objects alongside
  `FlowyThingBuilder` roads and rivers and adds green clickable bridge control
  points.
- Adds live bridge point move/rotate, insertion, deletion, exact transforms,
  independent Block/Bent start and end styles, and AutoTrestle regeneration.
- Adds undo/redo and confirmation-protected whole-spline deletion for created
  and existing roads, rivers, bridges, and trestles.
- Preserves each builder's native JSON schema: road/river width remains
  conditional, bridge points do not gain invalid width fields, and existing
  head/tail property casing is retained.
- Reuses the active graph document when splineys share the selected track
  layer, preventing a stale Spliney copy from overwriting later track edits.
- Caches live Spliney attachment and visibility state, with a bounded
  five-second retry for late loader objects, to avoid panel stutter on maps
  containing hundreds of AutoTrestles.

## 0.8.2

- Fixes segment overlay lines disappearing after Undo, Redo, graph edits, or
  Rebuild Track.
- Detects overlays bound to a replaced graph object and overlays whose line
  renderer is missing or empty.
- Schedules three bounded post-edit repair passes so overlay reattachment runs
  after Unity's deferred object destruction and loader reconciliation finish.
- Limits normal repair passes to affected node and segment IDs; only an
  explicit full Track Rebuild reconciles all overlays.
- Keeps repair independent of the one-second scenery/spliney refresh throttle
  and retains the event-driven, no-per-overlay-Update performance model.
- Adds regression coverage for replacement-safe segment overlay repair.

## 0.8.1

- Replaces the track node Move/Rotate mode switch with one unified transform
  workspace.
- Keeps movement steps and the six-direction movement pad visible directly
  above rotation steps and the full pitch/heading/roll pad.
- Keeps `More...` focused on exact transforms, complete precision grids, local
  axes, reset, and connection tools.
- Promotes scenery/building pitch and roll out of `More...` and places them
  beside heading in a full six-button rotation pad directly below movement.
- Adds a compact scenery rotation step row with common increments while
  retaining every precision increment under `More...`.
- Adds regression coverage ensuring track and scenery movement and rotation
  remain available together.

## 0.8.0

- Adds named persistent profile libraries to Arc, Turnout, and Wye.
- Saves profile libraries immediately and atomically in
  `Mods/TrackBridge/tile_editor_track_profiles.json`, outside the versioned mod
  folder so profiles survive upgrades and reinstalls.
- Adds compact collapsed profile controls for selecting, loading, saving,
  updating, and confirmation-protected deletion.
- Stores radius, angle, explicit node count, grade, and direction in Arc
  profiles.
- Stores lead length, divergence angle, grade, and direction in Turnout
  profiles.
- Stores all complete-wye dimensions, mainline grade, and tail side in Wye
  profiles.
- Adds an explicit Arc control-node count from 1 through 64 to both Arc and
  Pieces/Arc, replacing the hidden automatic node count.
- Adds regression coverage for persistent tool profiles and explicit arc-node
  generation.

## 0.7.1

- Allows the complete Wye builder to start from a normal two-segment node in
  an existing through track, not only a dead-end approach node.
- Detects the forward segment from the selected node heading, verifies that
  Through + Exit lengths fit, and reports the measured available length when
  they do not.
- Splits and reuses the existing forward segment so the selected node and the
  new opposite node become proper degree-three turnouts instead of creating a
  four-way junction.
- Preserves the reused track's curve, vertical profile, style, class, group,
  priority, and speed limit while reconnecting its remaining route.
- Shows Endpoint Mode or Through-Track Mode directly in the Wye panel and
  disables complete build only for incompatible junction selections.
- Adds regression coverage for through-track reuse and clearance validation.

## 0.7.0

- Rebuilds the Wye workspace around a one-click complete operational wye.
- Generates exactly three degree-three turnout junctions, both curved triangle
  legs, a through continuation, and a tail track ending at a stub.
- Uses Railroader's node tangent system at all three frogs so the connected
  routes enter and leave smoothly.
- Automatically points the generated wye away from the selected existing
  approach track.
- Adds Compact, Standard, and Broad starting shapes plus editable through
  length, triangle depth, tail stub length, exit length, tail side, and
  mainline grade controls for fully custom wyes.
- Carries the triangle over the requested mainline elevation plane and makes
  the tail level for smooth vertical tangency at the third frog.
- Groups the full build into one Undo operation and selects the through exit
  so track laying can continue immediately.
- Retains the former two-leg wye frog generator in a collapsed Simple Frog
  Builder section.
- Adds a regression check for the complete-wye topology and panel controls.

## 0.6.2

- Removes per-frame `Update` work from every track node, segment, spliney
  point, and scenery marker.
- Updates marker colors only when selection or visibility actually changes.
- Caches the active Geo/Scenery/Spliney workspace so IMGUI repaint events no
  longer trigger repeated whole-scene overlay searches.
- Removes the full track-overlay traversal from the recurring bridge heartbeat.
- Throttles dynamic scenery/spliney loader reconciliation to once per second
  and skips scenery marker traversal when the live instance signature is
  unchanged.
- Limits segment pick sections to a maximum of 24 with 30 m spanning
  colliders, substantially reducing renderer and physics-component counts
  while preserving continuous segment picking.
- Makes track edits create or rebuild only affected overlays instead of
  walking every node and segment.
- Avoids rebuilding Railroader's track object manager during scenery-only
  Undo/Redo.
- Adds a regression check preventing per-overlay frame update methods from
  returning.

## 0.6.1

- Prevents post-save scenery movement from mixing Railroader's shifted Unity
  world coordinates with persistent game coordinates.
- Centralizes scenery capture through `WorldTransformer.WorldToGame` and live
  application through `WorldTransformer.GameToWorld`.
- Adds a live round-trip check that detects and corrects a coordinate-frame
  mismatch before the object can be moved or saved at the wrong location.
- Retains scenery selection by stable ID and automatically reattaches it to
  the replacement instance materialized by Strange Customs after Save.
- Adds a regression check protecting the game-coordinate persistence
  invariant.

## 0.6.0

- Adds `Rebuild Terrain` to both Geo and Scenery using Railroader's native
  `MapManager.RebuildAll` workflow.
- Replaces the Scenery placeholder with a Tile Editor-owned live workspace
  that does not depend on Alina's Map Editor.
- Adds cyan/magenta clickable world markers for loaded scenery objects.
- Adds a searchable palette sourced from every scenery definition currently
  loaded by Railroader's asset manager.
- Adds live placement at the camera target, world/local movement arrows,
  terrain snapping, heading/pitch/roll controls, full precision step grids,
  uniform scaling, exact per-axis transforms, model replacement, duplication,
  camera focus, and confirmed deletion.
- Writes scenery using the existing Strange Customs/RailLoader `scenery` JSON
  schema while preserving unknown fields on entries already in the output
  layer.
- Integrates scenery operations into the same ordered Undo/Redo stack, atomic
  graph save, and timestamped backup used by live track editing.
- Reconciles immediate Tile Editor placement previews with the later
  Strange Customs file-watcher materialization so saved objects do not appear
  twice.
- Hides track, spliney, and scenery markers outside their relevant workspace
  to keep the world view readable.

## 0.5.0

- Adds a draggable bottom-right resize handle to the F9 editor and remembers
  the chosen panel dimensions between sessions.
- Restores the complete movement and rotation increment ranges, including
  movement through 1000 m and rotation through 180 degrees.
- Adds a compact two-row Geo tool family matching the desktop workflow:
  Spliney, Pieces, Arc, Parallel, Fit Arc, Node, Grade, Turnout, and Wye.
- Adds live sequential straight, curved, and turnout pieces that continue from
  the newly created endpoint.
- Adds grouped, undoable parallel-track generation from a selected segment,
  with left, right, or both-side placement.
- Adds grouped, undoable circular fitting for an ordered connected node chain,
  including fitted-radius and RMS error feedback.
- Adds an independent live road/river Spliney editor with clickable control
  points, movement arrows, width and rotation controls, insert/delete, exact
  transforms, live mesh rebuilding, undo/redo, atomic save, and timestamped
  source backups.
- Finds spliney sources from the owning RailLoader mod even when the selected
  graph JSON is stored in a nested folder.

## 0.4.3

- Replaces the six rectangular movement rows with a compact two-row
  directional pad.
- Adds large Forward, Back, Left, and Right arrow symbols while keeping the
  X/Z axis and direction visible on every button.
- Places Lower and Raise beside the forward control for quick elevation work.
- Replaces rotation rows with paired counter-clockwise and clockwise arrow
  pads for Pitch X, Heading Y, and Roll Z.
- Adds a dedicated high-contrast direction-button style sized for quick
  in-game use.

## 0.4.2

- Reorganizes the selected-node interface around progressive disclosure.
- Keeps only Move/Rotate modes, common step sizes, large directional buttons,
  and the core Add/Split/Level/Flip/Show/Delete actions in the default view.
- Moves exact X/Y/Z fields, complete step grids, local-axis movement,
  reset-rotation, free-node placement, and connection setup under `More...`.
- Shows connection completion automatically after the user selects its
  destination node, even though advanced controls collapse.
- Gives selected segments a separate compact Inject/Show/Style/Delete view
  instead of mixing disabled segment controls into the node editor.
- Returns the scroll view to the top and closes advanced controls whenever
  the world selection changes.

## 0.4.1

- Replaces the translucent in-game window body with a nearly opaque dark
  background, brighter labels, and a high-contrast border.
- Adds exact selected-node position and rotation fields for X, Y, and Z.
- Adds undoable stepped movement in world or local node axes, including
  raise/lower and forward/back controls.
- Adds independent pitch, heading, and roll controls with increments from
  0.01 to 180 degrees, plus level, reset-rotation, and flip actions.
- Notifies Railroader and rebuilds every connected segment after a node
  transform so track geometry and overlays update immediately.
- Generates trestles from dense samples of the track's exact 3D Bezier,
  including vertical curvature, tangent pitch, and roll.
- Adds `Fit Trestle to Track` to repair existing AutoTrestle splineys by
  matching their endpoints to the nearest rail segment.

## 0.4.0

- Removes the in-game dependency on Alina's Map Editor and its graph chooser.
- Adds a Tile Editor-owned graph session that automatically opens the active
  desktop layer or lets the user choose a RailLoader game-graph mixinto.
- Adds independent cyan/magenta node and yellow/green segment overlays with
  world picking and tooltips.
- Mutates Railroader's live Track.Graph directly and writes compatible
  Strange Customs/AMM patch entries to the selected JSON layer.
- Adds Tile Editor-owned grouped Undo/Redo, atomic Save, a timestamped
  pre-save backup, and live track rebuild.
- Adds native in-game Track, Grade, Arc, Turnout, and Wye tool pages.

## 0.3.0

- Replaces the compact F9 remote with a full Tile Editor in-game Geo workspace.
- Shows Alina Map Editor's clickable node and segment overlays while F9 mode is
  open and hides them when the mode closes.
- Adds live node/segment selection, add, connect, inject, split, level, flip,
  delete, style, camera focus, rebuild, Undo/Redo, and graph Save controls.
- Adds smooth vertical grade transitions that ease from the selected node's
  current grade and can accurately maintain an existing grade.
- Adds circular arc and turnout-branch builders with direction and target-grade
  controls. Generated geometry is grouped into one native undo operation.
- Connects to Map Editor late so RailLoader and Unity Mod Manager load order
  cannot prevent the rest of the Tile Editor bridge from starting.

## 0.2.1

- Adds a built-in UMM game heartbeat when no legacy TrackBridge is installed.
- Consumes bridge ping/reload commands and retriggers the Strange Customs graph
  file watcher.
- Writes command acknowledgements for live-link diagnostics.

## 0.2.0

- Packages the complete desktop Tile Editor inside the UMM mod folder.
- Adds versioned launch and environment-repair BAT files.
- Adds the compact F9 in-game bridge panel.
- Adds bidirectional desktop status and action requests through TrackBridge.
- Improves grade transitions, graded turnouts, wyes, and turnout insertion.
- Improves scenery placement preview, rotation, scaling, saving, and undo.

## 0.1.0

- Initial compact Tile Editor Bridge panel.
