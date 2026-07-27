# Changelog

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
