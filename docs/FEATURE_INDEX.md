# Feature And Workspace Index

The Tile Editor is an authoring suite. It is not required for FUSE or for
players using a finished map. Use this page when you remember the job but not
which workspace owns it.

| Job | Workspace |
| --- | --- |
| Paint height, vegetation, water, smooth, noise, or erosion | Desktop Terrain |
| Create/edit visible lake planes or replace a stock lake | `F9` → Geo → Water (native FUSE) |
| Import/generate terrain tiles or OSM overlays | Desktop Terrain / Generate |
| Create, connect, move, split, or delete track | Desktop Track or `F9` → Geo |
| Build arc, turnout, wye, parallel track, span, bridge, or turntable geometry | `F9` → Geo |
| Inspect node↔segment relationships and jump between them | `F9` → Geo node/segment detail |
| Set standard, narrow, or dual gauge metadata | Desktop Track or `F9` → Geo |
| Place asset-pack buildings/props | Scenery |
| Move/disable base-game objects and town signs | `F9` → Objects |
| Edit roads, rivers, trestles, and bridge spline control points | Spliney |
| Draw fences, retaining walls, guardrails, or pipes from repeated rigid modules | `F9` → Geo → Spliney → Fence / Wall (native FUSE) |
| Create towns, TrackSpans, industries, or freight components | `F9` → Operations |
| Place station agents or physical service objects | `F9` → Operations |
| Create passenger stops and neighbour links | `F9` → Operations |
| Give players on/off, choice, or slider-controlled mod sections | `F9` → Options (native FUSE) |
| Place semaphores or build a diamond interlocking | `F9` → Operations → Signals |
| Edit progression sections and unlock features | Prog |
| Edit areas/industries/components as JSON | Areas |
| Move several nodes together | Group |
| Measure grade/heading or calculate crossover/turnout dimensions | Calc |
| Inspect base and modded runtime graphs | FUSE `/fuse.dumpgraph` and `/fuse.dumpruntimegraph` |

## Runtime Boundaries

- The desktop app and F9 bridge are authoring tools.
- Finished FUSE/RailLoader graph and scenery JSON is loaded by FUSE; players do
  not install the Tile Editor.
- `train-signals.json` is loaded in normal play by Railroad Operations' signal
  runtime.
- `grade-crossings.json` is loaded by `Hrogers.CrossingRuntime`.
- Working custom fuel/water facilities are provided by Toolshed. The Editor can
  place/author their data, but it does not become their gameplay runtime.
- Visible lake planes use FUSE native `world.waterSurfaces`; terrain water-mask
  painting is a separate terrain-layer operation.
- Repeated-object lines use FUSE native `world.splineys` with
  `type: "objectLine"`; legacy RailLoader mode cannot preserve them and shows
  that tool disabled.
- Player-selectable modular sections use native FUSE `settings` and
  `featureRules`; RailLoader mode shows Options disabled because the legacy
  schema cannot preserve this behavior.
- Narrow/dual-gauge rendering is provided by FUSE Narrow Gauge.

Keeping these separate prevents an Editor failure from blocking a player's save,
company window, or FUSE startup.

## Essential References

- [Keybind Reference](KEYBINDS.md)
- [In-Game Geo Workspace](IN_GAME_GEO.md)
- [Operations And Signals](OPERATIONS_AND_SIGNALS.md)
- [Data Formats And Examples](SCHEMA_EXAMPLES.md)
