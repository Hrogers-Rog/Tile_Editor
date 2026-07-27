# Track Tool Roadmap

## Goal
Make manual track laying in the 2D editor feel like railroad drafting instead of raw node fighting.

The editor should help with three things:
- Control `X/Z` alignment precisely.
- Make `Y` / grade visible and editable without leaving top-down workflow.
- Keep the user in control while adding smart constraints, measurements, and cleanup tools.

## Design Rules
- Keep the current top-toolbar workflow. Do not bring back the old sidebar model.
- Separate plan view work (`X/Z`) from profile work (`Y`).
- Prefer guided manual tools over full automation.
- Reuse existing selection and panel patterns where possible.
- Build shared path/stationing helpers first so later tools reuse the same math.

## Phase 0: AMM Output Stability And Core Editing

This phase comes before adding more drafting tools. A workflow is not complete
until the edit is visible, undoable, saved in the correct AMM graph format,
validated, and optionally hot-reloaded.

### Completed Foundation

- New nodes become visible immediately after placement.
- New map mods default to RailLoader `Definition.json` manifests.
- Auto Save OFF defers both disk writes and TrackBridge reload commands.
- Live bridge geometry refreshes when existing nodes move or rotate.
- Core add, connect, move, delete, property, split, merge, and undo actions
  share one merge, cache, save, and hot-reload path.
- Scenery placement writes the correct Y rotation and uniform scale, shows a
  live placement ghost, highlights the selected object, and supports undo.
- Grade chains support parabolic entry and exit transitions around a held
  grade, with a purple Profile preview and synchronized node elevation/pitch.
- Geometry panels scroll when the Profile dock reduces their available height.
- Regression tests cover RailLoader output, core track edits, scenery
  transforms, vertical-curve math/pitch, undo, and bridge behavior.
- The compact `Hrogers.TileEditorBridge` UMM panel now reports desktop editor,
  project/layer, Geo, selection, track, scenery, and save status. It remotely
  prepares Geo/Scenery tools, focuses the editor, undoes, and saves/reloads
  through the existing TrackBridge folder.
- Regular turnouts and wyes use exact circular-chord placement, preserve
  approach grade/pitch, validate minimum radii, and avoid four-route wyes.
- Shift-inserting a turnout splits the selected segment and adds the diverging
  leg as one save/undo transaction.
- The desktop app remains the complete editing UI. RailLoader remains the
  manifest for Strange Customs/AMM map data.

### Next Work

1. Add end-to-end coverage for split, merge, turnout, insert, and geometry
   preview commits.
2. Run graph validation before hot reload and show actionable errors in the UI.
3. Add recovery tests for `.bak` files and deferred/manual saves.
4. Add scenery move/duplicate, snap, axis-specific scale, and model-library
   tools.
5. Add automatic node insertion at vertical-curve boundaries when the selected
   chain is too sparse to represent the requested profile exactly.
6. Replace silent exception handling in editing paths with visible diagnostics.
7. Break the largest `TileEditor` responsibilities into focused services only
   after behavior is protected by tests.
8. Add command acknowledgements and protocol-version diagnostics to the compact
   bridge panel after live in-game testing.

## Gauge And Turntable Architecture

### Gauge Editing — Implemented

- Keep one canonical editor value per segment: `Standard`, `Narrow`,
  `DualGauge`, `DualGauge_L`, `DualGauge_R`, or `DualGauge_T`.
- Write the value as optional `gauge` metadata in portable RailLoader graphs.
  RailLoader ignores it; FUSE retains it for `FUSE.NarrowGauge`.
- Preserve unknown future gauge strings and all unrelated segment fields
  during routine edits.
- Let every track builder inherit the active gauge, while split/merge/rename
  operations inherit the source segment's gauge.
- Show gauge-specific overlay colors and allow applying a gauge to one segment
  or across a degree-two through chain.
- Read and write native FUSE `FuseDataFiles` without changing native endpoint
  fields or removal semantics.

### Turntables — Next Operational Track Tool

Turntables are graph operations, not ordinary scenery. The planned workspace
will:

1. Place the center at the mouse pointer and preview the pit, bridge axis, and
   every generated pit connection.
2. Expose radius, yaw, subdivisions, bridge length, vertical offset, and
   `Standard` or three-foot bridge gauge in the primary panel.
3. Put roundhouse stalls, start/stall angle, track length, prefab choices,
   controller type, and custom visual identifiers under Advanced.
4. Generate deterministic pit-node IDs so ordinary track can snap to and
   reconnect with the turntable after later edits.
5. Offer two explicit save backends:
   - Portable legacy turntable data for RailLoader/Strange Customs, which FUSE
     can convert.
   - Native `operations.turntables` for FUSE custom visuals and controllers,
     including the RLW narrow-gauge pit and bridge assets.
6. Never silently write both backends for the same table until duplicate
   conversion behavior has been verified. The UI will show the chosen runtime
   compatibility before Build.

FUSE currently exposes one numeric `bridgeTrackGauge`, so the first
operational release should support standard- or narrow-gauge bridges. A true
three-rail dual-gauge bridge needs a verified custom visual/controller and
Narrow Gauge companion contract; it should not be faked as a normal
single-gauge bridge.

## Editor Surface Plan
- `Measure` becomes a top-toolbar tool button that opens a compact measurement/stationing panel.
- `Geo` grows into the main plan-view drafting panel for straights, arcs, fits, and alignment cleanup.
- `Profile` becomes a docked bottom panel, not a modal overlay.
- On-map overlays provide live HUD feedback, station labels, guide lines, and validation warnings.

## Phase 1: Measurement And Straight-Line Foundation

### What Ships
1. Quick Measure between 2 selected nodes.
2. Along-track distance between connected nodes.
3. Stationing / milepost origin on a selected node.
4. Construction line / baseline tool.
5. Bearing lock and distance lock for manual placement and dragging.
6. Live cursor HUD for heading, distance, offset, delta Y, and grade.

### Why First
This phase removes the biggest day-to-day friction immediately:
- You can measure what you already built.
- You can draft true tangents instead of eyeballing them.
- Later profile and curve tools can reuse the same stationing and graph helpers.

### UI Plan
- Add a row-2 `Measure` button to the top toolbar.
- Reuse the current calculator/panel pattern for the first measurement panel.
- Add buttons in the panel for `Set Start`, `Set End`, `Swap`, `Clear`, `Set MP Origin`, and `Clear Origin`.
- Show result fields for:
  - Direct X/Z distance
  - True 3D distance
  - Along-track distance
  - Delta Y
  - Average grade
  - Start station
  - End station
- Add an on-map construction line overlay with lateral offset readout.
- Add a small live HUD near the cursor during node drag/place operations.

### Data / Model Work
- Add graph-context helpers that work for:
  - Mod project merged graph
  - Loaded track graph
  - Bridge/live graph
- Add reusable path-distance and station-cache helpers.
- Track construction-line state:
  - start point
  - end point
  - heading
  - optional lock status
- Add bearing-lock and distance-lock state for node placement / drag.

### Acceptance Criteria
- User can measure any 2 selected connected or unconnected nodes.
- Along-track distance uses real connected path length, not straight-line fallback.
- User can set `MP 0.00` or `Sta 0+00` from a selected node.
- User can place or drag a node while locked to a baseline / bearing.
- Editor displays live offset from the current construction line.

## Phase 2: OSM Guides And Curve Drafting

### What Ships
1. Trace Guide Path over OSM right-of-way.
2. Deviation overlay from drafted alignment to guide path.
3. Radius-first arc placement tool.
4. 3-point arc fit.
5. Fit selected nodes to a true arc.
6. Minimum-radius warning.
7. Curve clean-up / continuity pass for rough manual layouts.

### Why Second
Once straights are under control, the next pain point is curves:
- OSM is useful as a guide but poor as direct geometry.
- Users need to place smooth curves without solving radius manually every time.

### UI Plan
- Keep this inside `Geo`, but add an `Alignment` sub-tab or mode set.
- Add plan-view tools:
  - `Guide`
  - `Arc`
  - `Fit Arc`
  - `Clean Curve`
- Show live curve HUD with:
  - radius
  - angle
  - arc length
  - chord length
- Render radius warnings directly on the offending segment(s).

### Data / Model Work
- Introduce guide-path data separate from final track nodes.
- Add arc-fit helpers and best-fit math for selected node chains.
- Store draft alignment metadata without forcing immediate node bake.
- Add deviation sampling between current geometry and guide path.

### Acceptance Criteria
- User can rough in a guide path over OSM without committing final track.
- User can lay a constant-radius arc by radius and angle.
- User can select a rough hand-drawn curve and convert it to a true arc.
- Editor warns when radius drops below the chosen threshold.
- Deviation to guide path is visible and understandable.

## Phase 3: Bottom Profile And Vertical Design

### What Ships
1. Docked bottom profile panel for selected chain.
2. Terrain profile line.
3. Track profile line.
4. Node markers editable in profile view.
5. Grade labels between profile points.
6. Benchmark pins / target profile references.
7. Cut/fill shading.
8. Grade-break warnings and vertical smoothness checks.
9. Parabolic entry/exit grade transitions with held-grade control.
10. Grade-aware node pitch (`rotX`) updates.

### Why Third
This phase solves the hidden `Y` problem directly.
The user keeps using the top-down map for `X/Z`, but finally gets a clean, interactive railroad-profile view for `Y`.

### UI Plan
- Add a row-2 `Profile` button to toggle a docked bottom panel.
- Keep the map visible above the profile at all times.
- Sync hover and selection between profile and map.
- Display:
  - distance/station axis
  - elevation axis
  - terrain line
  - track line
  - node handles
  - grade labels
  - benchmark markers
- Support dragging profile nodes vertically while preserving station position.

### Data / Model Work
- Reuse Phase 1 stationing helpers as the profile X-axis.
- Sample terrain under the selected chain by station.
- Build profile render data from selected nodes / chain order.
- Add profile edit actions that update node Y only.
- Add cut/fill and vertical warning calculations.

### Acceptance Criteria
- User can select a chain and immediately see its vertical profile.
- Hovering the profile highlights the same location on the map.
- Dragging a point in the profile changes Y without changing X/Z.
- Terrain vs track relationship is obvious.
- Grade breaks and cut/fill trouble spots are visible.

## Follow-On Phase: Alignment Pieces And Snap Assembly
This comes after the three foundation phases above.

### What Ships Later
- Straight piece
- Arc piece
- Easement piece
- Turnout templates
- Snap-by-endpoint-pose assembly
- Flex-track solver
- Bake alignment pieces to game nodes / segments

### Why Later
The piece-based "model train set" workflow is powerful, but it works best after:
- stationing exists
- straight drafting exists
- curve math exists
- profile editing exists

Otherwise the editor gets a new abstraction layer before the geometry foundation is stable.

## Suggested Build Order Inside The Current Codebase
1. Add graph/stationing helpers in `edit_tiles/app.py`.
2. Add `Measure` entry point in the top toolbar and event routing.
3. Extend the existing calc/panel pattern for measurement UI.
4. Add construction line + bearing/distance lock overlays in `edit_tiles/renderer.py` and drag handling in `edit_tiles/events.py`.
5. Add guide-path and curve math in `Geo`.
6. Add bottom profile dock and profile-edit interactions.
7. Add alignment-piece system only after Phases 1-3 are stable.

## Success Condition
When these phases are done, the editor should let you:
- draft straights intentionally
- measure mileage reliably
- fit smooth curves on purpose
- edit grade visually
- use OSM as a guide without tracing garbage geometry
- stop fighting raw nodes for every small correction
