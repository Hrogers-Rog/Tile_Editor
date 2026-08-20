# Mod Tools

The second nav row. These panels edit everything in a map mod that isn't terrain
or raw track geometry.

Every panel writes to the owning mod JSON layer and triggers a bridge reload.
`Ctrl+Z` undoes panel edits too, up to 50 steps.

## Mod Panel

| Control | What it does |
| --- | --- |
| Open Mod Folder | Load a RailLoader `Definition.json` or native FUSE `Info.json` package |
| Open Base Game | Load a standalone `graph-data.json` |
| New Mod | Create and immediately open a complete compatible or native FUSE package |
| Save All | Save every dirty layer file |
| Validate | Run a copyable pre-publish report for manifests, graph, native operations, and references |
| Export ZIP | Validate and create a clean distribution archive; errors block export, warnings do not |
| Layer dot | Toggle layer visibility |
| Layer row | Set as the active layer |
| ● indicator | Unsaved changes in that layer |

Columns show per-layer counts: Nodes (plus deleted), Segs, Spl (splineys), Areas.

Closing the panel with ✕ or `Esc` leaves the mod loaded.

### Validate And Export

**Validate** checks the saved package rather than a hidden editor snapshot. If
the project is dirty, the editor asks whether to save first. The report includes
manifest/file problems, track topology, TrackSpan endpoints, native FUSE
operations relationships, and native water-surface geometry:

- industry-to-area, component-to-TrackSpan, and component-to-load references;
- required TrackSpans/loads for known component types;
- unique passenger stop IDs and timetable codes;
- missing and one-way passenger-neighbor relationships;
- station-agent-to-passenger-stop and physical-loader-to-industry references.
- water boundary point count/coordinates, self-crossing polygons, material/source
  fields, and safe UV/tessellation ranges.
- native player-option settings, comparison operators, target kinds, duplicate
  targets, and whether every target is authored in the same file.

An add-on may intentionally reference the base game or a required package, so a
reference not defined inside that package is a warning. A standalone map sets
`suppressBaseWorld` and must be self-contained; the same missing reference is an
error. This distinction prevents a legitimate patch/add-on from being labeled
broken while still preventing an incomplete new map from being published.

**Export ZIP** runs the same validation. Errors stop the export and open the
copyable report. Warnings remain visible but allow a clean ZIP after the author
confirms that its external dependencies are intentional. Development files,
backups, caches, build output, and oversized files are omitted.

### Creating A Mod From Scratch

Choose **New Mod**, enter a stable ID such as `YourName.NewMap`, then select a
package format and the parent folder. The editor creates its own named child
folder and refuses to overwrite a folder containing files.

- **Native FUSE package (recommended)** creates `Info.json` and
  `map.fuse.json`. This is the authoritative editor format and supports native
  endpoints, removals, station agents, turntables, and every future FUSE-only
  feature.
- **Legacy RailLoader package (limited)** creates `Definition.json` and
  `game-graph.json`. RailLoader loads it directly and FUSE can import it, but the
  editor disables native-only operations that cannot be represented honestly in
  legacy JSON. It does not require Strange Customs or Alina's Map Mod for the
  subset it supports.

Do not hand-maintain the same objects in both files. A future legacy export is a
lossy compatibility projection from the native document, never a second source
of truth. If a project must still ship to RailLoader users today, author only the
legacy-supported subset or release a separately tested legacy build.

## Options Workspace (in-game)

Open `F9` → **Options** in a native FUSE project to make one mod configurable
instead of publishing a base package plus several optional add-ons.

1. Choose **ON / OFF**, **CHOICE**, or **SLIDER**.
2. Enter stable rule and setting IDs plus the label players will see.
3. Set the default and the value/comparison that includes the feature.
4. Cycle through target kinds and add the exact authored nodes, segments, spans,
   scenery, industries, components, loaders, water surfaces, progression, or
   audio objects controlled by the option.
5. Save the player option, then save the mod.

The editor writes top-level `settings` and `featureRules`, forces
`reloadRequired: true`, validates references, and includes the edit in ordinary
undo/redo. FUSE keeps the complete source definition and filters only its
runtime copy, so switching the setting off does not erase authored data.

Feature rules intentionally cannot target base-game or dependency objects. This
keeps a package option from silently deleting another package's content. Use
normal FUSE removals or a separately ordered patch for that job.

The RailLoader schema has no matching object-level option feature. The same
workspace remains visible but greyed out for a legacy project.

## Map Legend

What you see with a mod loaded:

| Appearance | Meaning |
| --- | --- |
| Yellow lines | `game-graph.json` track segments |
| Grey lines | Base game track |
| Blue polylines | Rivers (FlowyThingBuilder) |
| Tan polylines | Roads (FlowyThingBuilder) |
| Grey/cream | AutoTrestle bridge spans |
| Orange circles | Turntables |
| Green diamonds | Stations |
| Orange diamonds | Loaders / industry |
| Coloured squares | Scenery buildings — zoom in for the model name |
| Large circles | Area/town centres with name labels |
| **Orange circle** | Live locomotive (from the bridge) |
| **Teal square** | Live railcar (from the bridge) |

The last two only appear with a live game connected.

## Progression Editor (Prog)

Left column lists sections in topological order, prerequisites first. Right column
lists map features and what each unlocks.

| Control | What it does |
| --- | --- |
| + Section | Add a purchasable section |
| + Feature | Add a map feature |
| Del Section / Feature | Remove the selected item |
| Save | Write back to `progressions.json` |

Section fields: id, display name, prerequisites, cost, feature to enable.
Feature fields: id, display name, areas unlocked, track groups.

Topological ordering means a section always appears after everything it depends
on — if one is in an unexpected place, its prerequisites are the reason.

## Area / Town Editor (Areas)

Three columns: areas (sorted by order, with a source-layer colour dot),
industries in the selected town, and components in the selected industry.

| Control | What it does |
| --- | --- |
| Click a row | Select and load the next column |
| Go to Area | Pan the map there and close the panel |
| + Area | Create at the current map centre |
| + Industry | Create in the selected town |
| + Component | Create in the selected industry |
| Edit Area / Industry / Comp | Edit that JSON object directly |
| Del Area | **Mark** for deletion (not saved yet) |
| Del Industry / Comp | Remove immediately from its parent |
| Save | Write all dirty town JSON files |

Note the asymmetry: deleting an area only marks it, while deleting an industry or
component removes it from the parent right away.

Industry creation exposes the full RailLoader/FUSE component set — storage
capacity and daily change, car transfer rates and ordering, formula input/output
maps, team-track import/export profiles, passenger population and neighbours,
repair/overhaul, interchange variants, progression, multiple TrackSpans, and
custom typed fields.

## Scenery Placement (Scenery)

| Control | What it does |
| --- | --- |
| Model ID field | Type an identifier or click a quick-pick |
| RotY nudge | ±90 / 45 before placing |
| Place on Map | Placement mode — click to drop |
| `Esc` | Exit placement mode |
| Go To | Pan to the selected object |
| Del Object | Remove from the layer |

Y position is auto-sampled from terrain at the click point.

The in-game Scenery workspace adds a searchable loaded-asset palette that includes
runtime-resolvable RailLoader `SCAssetPacks` registered after the initial catalog,
plus in-world picking, transform controls, terrain snapping, and duplication.

## Mandela / Prefab Instances (Mandela)

Places instances of base-game prefabs.

| Control | What it does |
| --- | --- |
| Target | Destination scene path key |
| Prefab | Base-game prefab path to instantiate |
| Base Pick | Search `dumped-mandelas.txt` and fill Prefab |
| Reload Base | Reload the dumped prefab catalog |
| Load Sel / Save Sel | Pull draft values from an entry, or write them back |
| Duplicate | Copy a placement to a new target path |
| Place on Map | Drop the draft at terrain height |
| Enabled | Cycle default / enabled / disabled |

Legacy projects store these entries as root-level RailLoader `mandelas`; FUSE
imports those for compatibility. Native projects write the stronger
`world.sceneClones` contract directly, including a schema-safe definition ID,
the actual Unity path in `targetPath`, and `path://scene/...` for clone sources.

## Objects Workspace (in-game)

Selects base-game buildings and props under the mouse and saves move, rotate,
scale, enable/disable, and safe-clone operations. The active project format
decides whether they save as native FUSE scene clones or legacy RailLoader
mandelas.

Shared town, map, and world roots are blocked, so clicking one station cannot move
the entire scene. Thin signs and small props get a screen-space picking halo when
the ray misses. Generated track meshes are ignored by the Objects picker, so a
town sign beside or behind the rail can be selected without the track stealing
the click.

The F9 OBJECTS page also identifies Railroader's original scene signs and gives
them a dedicated visible/hidden toggle, without treating loader-added scenery
signs as base-game objects.

## Track Spans (Spans)

Lists all spans across layers with their layer colour. Click a span to edit its
upper and lower segment, distance, and end. **+ New Span** and **Del** manage
entries.

Spans may cover a whole segment, a measured partial range, or connected
start/end segments.

## Group Move (Group)

`Ctrl+drag` rubber-bands a node selection; `Shift`+rubber-band adds to it. Apply
dX / dY / dZ / Rot to translate and rotate everything selected at once.

## Calculators (Calc)

| Calculator | Input → output |
| --- | --- |
| Crossover | Separation + angle → leg lengths |
| Curved Turnout | Radius + gauge + angle → diverge geometry |
| Grade / Slope | Run + rise → percent, ratio, angle |
| Measure | Run, rise, grade, heading between picks |

## Spliney Panel

| Control | What it does |
| --- | --- |
| Type | Road or River |
| Target | An existing writable spliney layer |
| New JSON… | Create and register a new road or river file |
| Width / Seed | Defaults for new placements |
| Use Sel Rot | Use the selected track node's heading |
| Place on Map | First click creates a spliney with two starter points |
| Go To / Delete | Navigate to or remove the selected spliney |

The details card shows style, width, point count, and length.

Loaded base-game roads and rivers also expose their real control nodes. The first
change creates a same-name override in the selected graph, and every adjustment
refreshes the associated height, roadbed, dirt, object, water, and
vegetation-mask terrain modifiers.

## Telegraph Poles (in-game)

Amber world markers, pole creation at the pointer, continuous-line placement,
manual wire connections, WORLD/LOCAL movement, full pitch/heading/roll rotation,
and deletion.

New poles and edges persist in the map mod's
`tile-editor-telegraph-poles.json`. Original poles still save compatible
cumulative offsets plus portable rotation overrides. Native FUSE projects write
base-pole offsets to `world.telegraphPoleMovements`; RailLoader projects write
the equivalent Alina `TelegraphPoleMover` handler.

## Related

- [Data Formats](SCHEMA_EXAMPLES.md) — what these panels write
- [In-Game Geo Workspace](IN_GAME_GEO.md)
- [Track Editing](TRACK_EDITING.md)
