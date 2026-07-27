# Operations Editor Plan

## Goal

Add a straightforward `OPERATIONS` workspace for towns, passenger stations,
industries, engine facilities, commodity loads, and physical service loaders.
The common workflow should be visual and profile-driven. Raw RailLoader,
Strange Customs, FUSE, and community component fields should remain available
under `Advanced`.

## Formats found

### RailLoader / Strange Customs game-graph mixins

The installed EFA Track Pack stores:

- `areas`: towns/operating areas, including name, position, radius, display
  color, order, and nested industries.
- `areas.<area>.industries`: industry name, local position, contract behavior,
  and a dictionary of components.
- `components`: track-bound operating behavior. The installed pack uses
  passenger stops, loaders, unloaders, formulaic production, repair tracks,
  team tracks, interchanges, interchanged loaders/unloaders, and progression
  components.
- `loads`: commodity definitions such as coal, diesel fuel, repair parts,
  lumber, and passengers.
- `tracks.spans`: named portions of one or more track segments referenced by
  each operating component through `trackSpans`.

Null area, industry, or component entries are meaningful legacy deletion
patches and must be preserved.

### Native FUSE packages

FUSE separates the same concepts:

- `tracks.areas`
- `tracks.spans`
- `operations.loads`
- `operations.industries`
- `operations.industries.<id>.components`
- `operations.loaders`
- `operations.stations`

FUSE provides canonical component names:

- `loader`
- `unloader`
- `formulaic`
- `repairTrack`
- `teamTrack`
- `interchange`
- `interchangedLoader`
- `interchangedUnloader`
- `teleportLoading`
- `progression`
- `passengerStop`

A fully qualified external type plus a free-form `fields` dictionary supports
community components. FUSE also normalizes legacy names such as
`Model.Ops.IndustryLoader`, `AlinasMapMod.PaxStationComponent`, and known
Confusing Supplements handlers.

## Important distinction

An industry `loader` or `unloader` component controls railroad car operations
on a TrackSpan. A physical water tower, coal chute, fuel stand, or other
interactive service object is a separate placed loader with position,
rotation, prefab, and owning industry ID. The editor must present these as:

- `Rail delivery / shipping`
- `Engine service object`

This avoids calling both unrelated objects simply "loader."

## Proposed F9 layout

Add one top-level `OPERATIONS` tab with compact subtabs for towns, spans,
industries, passenger service, and facilities. Turntable construction belongs
in Geo with the other track-geometry builders; Operations still discovers and
associates saved turntables.

### Towns

- Click the world to place the town center.
- Name, ID prefix, radius, map color, and display order.
- `Use nearby spans` and an optional advanced group ID.
- Existing towns appear as soft circular overlays with a center marker.

### Track Spans

- Click a track segment and choose `Use whole segment`.
- For precision, enter measured start/end distances along one segment.
- Multi-segment spans mark a selected start segment/distance, then select the
  connected end segment and enter its distance.
- Preview the covered track in a distinct color before saving.
- Name the span once; industries select it from a searchable list afterward.

### Industries

- Select or create a town.
- Click the world to place the industry label/anchor.
- Set name, ID, contract usage, and display order.
- Add components from clear profiles:
  - Receives freight
  - Ships freight
  - Team track
  - Production / formula
  - Interchange
  - Passenger stop
  - Repair track
  - Progression delivery
  - Custom component

### Passenger

- Station name, timetable code, population, branch, and passenger TrackSpan.
- Searchable neighbor-stop picker with `Connect both ways`.
- Optional branch timing and intermediate-stop drawer.
- Optional station-agent prefab placement using the same mouse move/rotate
  controls as scenery.
- Default `passengers` load and `*` car filter are filled automatically.

### Facilities

Profiles create a complete but editable facility:

- Steam service: coal delivery/storage, water service object, optional sand,
  ash/cinder handling, and repair track.
- Diesel service: diesel delivery/storage, fuel stand, repair parts, and
  repair/overhaul track.
- Combined engine terminal.
- Coal/water/sand/fuel service object only.
- Custom physical loader prefab.

Each generated component remains individually editable. The user chooses the
TrackSpan for rail deliveries and places physical service objects with the
mouse.

### Turntables

- Expose the builder as a dedicated Geo tool beside Arc, Turnout, Wye, and
  Span rather than as an Operations subtab.
- Place the turntable center with the mouse and use the camera heading as the
  bridge's starting heading.
- Set pit radius, pit subdivisions, bridge-track gauge, and optional custom
  pit/bridge visuals.
- Optionally generate roundhouse stalls with a first-stall angle, spacing,
  and track length.
- Provide standard-gauge 30 m and three-foot narrow-gauge 21.4 m presets.
- FUSE output uses native `operations.turntables`, including generated pit
  nodes, bridge track, and roundhouse tracks.
- RailLoader output preserves the legacy
  `AlinasMapMod.Turntable.TurntableBuilder` entry in the buildings/splineys
  layer and clearly reports that runtime dependency.
- Validate radius, subdivisions, gauge, stall count, and stall length before
  modifying the document.

## Component editor

Show only the fields used by the chosen profile:

- Name and component type
- One or more TrackSpans
- Load/commodity
- Car type filter
- Storage capacity and daily change
- Car transfer rate
- Empty/loaded ordering behavior
- Formula input/output amounts per day
- Team-track import/export profiles and ideal car count
- Passenger code, population, branch, and neighbors
- Repair/overhaul and interchange-specific fields

`Advanced` exposes:

- Shared storage
- Time windows and cost
- Formula inputs/outputs
- Team-track profiles
- Passenger branch definitions
- Interchange conversion/output spans
- Fully qualified custom handler type
- Arbitrary typed custom fields, with FUSE `fields` output or safe legacy
  top-level handler fields

## Commodity library

The load picker should combine base-game loads, all loaded mod loads, and
definitions in the selected layer. It should support:

- Search and favorites
- Duplicate an existing load as a starting point
- Name, units, density, importability, prices, and car filters
- Validation when an industry references a missing load

Coal, water, sand, diesel fuel, repair parts, passengers, and common freight
loads should be one-click presets without preventing custom identifiers.

## Saving and compatibility

Use one internal operations model and format-specific writers:

- When editing a legacy game-graph mixin, preserve its `areas`, nested
  `industries`, `components`, `loads`, null deletion patches, local positions,
  and legacy component type names.
- When editing a native FUSE package, write canonical `tracks` and
  `operations` entries.
- FUSE can continue consuming legacy RailLoader/Strange Customs files through
  its converter, so an existing legacy mod does not need to be rewritten.
- If the owning mod has no operations layer, offer to create
  `tile-editor-operations.json` and add it to the mod's `Definition.json`
  mixinto list.

Every save must be atomic, backed up, undoable, and included in desktop/F9
dirty ownership and synchronization.

## Validation before build/save

- Every component TrackSpan exists.
- Every industry area exists.
- Every referenced load exists.
- Passenger stop IDs and timetable codes are unique.
- Passenger neighbors exist; one-way links receive a warning.
- Physical loader and station-agent industry/stop IDs exist.
- Built-in component types receive type-specific required-field validation.
- External custom types and fields are preserved exactly, with a warning when
  their providing mod is not loaded.
- Position conversion follows the same stable game/world coordinate rules as
  scenery to prevent teleporting to `0,0,0`.

## Implementation order

1. Discovery, selection, search, and colored overlays for areas, spans,
   industries, passenger stops, physical loaders, and turntables.
2. Span creation from selected track, town/industry pointer placement, basic
   operating components, physical service objects, and turntable placement.
3. Passenger station and complete engine-facility wizards.
4. Formulaic industries, interchanges, progression components, custom
   handlers, reusable profiles, and advanced turntable visual controls.
5. Safe live rebuild/reload adapters for RailLoader and FUSE, with save-only
   fallback when a runtime cannot refresh an operations object safely.
