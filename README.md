# Railroader Tile Editor

A Python desktop terrain and map editor for **Railroader** mods, paired with an
in-game **F9** workspace that edits the same data live.

Build terrain from real-world elevation, lay track on it, place scenery and
operations, author signals and grade crossings, and hot-reload the result into a
running game.

## Documentation

Full documentation is in **[docs/](docs/README.md)**.

- [Getting Started](docs/GETTING_STARTED.md) — install, run, first session
- [Keybind Reference](docs/KEYBINDS.md) — every binding
- [Terrain Editing](docs/TERRAIN_EDITING.md) · [Track Editing](docs/TRACK_EDITING.md) · [Mod Tools](docs/MOD_TOOLS.md)
- [In-Game Geo Workspace](docs/IN_GAME_GEO.md) — the F9 editor
- [Data Formats And Examples](docs/SCHEMA_EXAMPLES.md) — every JSON format, with worked examples

An older printable guide ships as `HunterR_Map_Editor_Guide.pdf`.

## What It Does

- **Terrain** — load and generate tiles from real elevation data, sculpt with six
  brush types, paint vegetation and water, and trim generated blocks down to the
  right-of-way
- **Track** — visual node and segment editing on real terrain, with arcs, grades,
  parallel track, turnouts, wyes, spans, and turntables
- **Splineys** — roads, rivers, trestle bridges, and native repeated-object
  lines for fences, retaining walls, guardrails, and similar modules
- **Operations** — towns, industries, components, progression, stations, loaders
- **Scenery and objects** — place assets, and move or clone base-game props as
  portable mandelas
- **Signals and crossings** — semaphores with diamond interlockings, and
  functional grade crossings
- **Live bridge** — two-way sync with a running game through
  `Hrogers.TileEditorBridge`

## Runtime Components

The editor is an authoring tool. What players install are small runtimes that read
the portable JSON it writes:

| Component | Role |
| --- | --- |
| `Hrogers.TileEditorBridge` | In-game F9 authoring workspace and live bridge |
| `Hrogers.CrossingRuntime` | Loads `grade-crossings.json` and registers native crossing markers. Independent of the Tile Editor. |
| `Hrogers.SignalRuntime` | Part of Railroad Operations; loads `train-signals.json` using Railroader's own animated semaphore asset |

Because crossing markers are registered in the shared track graph, they work for
player-owned locomotives in Waypoint Auto Engineer mode as well as AI equipment.

## Compatibility

Native FUSE is the canonical format for new work. It supports complete standalone
maps and every editor capability. RailLoader `game-graph.json` remains an
optional compatibility output for older data mods; controls that it cannot
represent are disabled instead of weakening or rewriting the native schema.
The selectors read `Info.json`'s `FuseDataFiles`, and `.fuse.json` fragments keep
their native structure.

RailLoader `Definition.json` (manifest version 8) remains available for portable
content; FUSE-only operations can live in native fragments when the legacy schema
cannot express them.

Both the desktop **New Mod** wizard and the in-game F9 chooser distinguish a
stock-map add-on from a standalone map. A standalone project creates the native
map declaration plus `Map/Map.json`; terrain generation then uses that map's own
latitude, longitude, and tile size rather than the stock North Carolina origin.

Narrow and dual-gauge rail geometry is rendered by
[FUSE Narrow Gauge](https://github.com/Hrogers-Rog/Narrow_Gauge), not by
Railroader's standard track builder.

## Requirements

- 64-bit Python 3.10 or newer
- `pygame-ce`, `numpy`, `Pillow`, `requests`, `scipy`

```bash
pip install -r requirements.txt
```

## Running

Windows:

```bat
run_editor.bat
```

The launcher finds Python through the `py` launcher, PATH, registry entries, and
common python.org, Conda, and Scoop folders — Python does not need to be on PATH.
Set `TILE_EDITOR_PYTHON` to an exact `python.exe` for a portable install.

Any platform:

```bash
python -m edit_tiles
```

## Project Layout

| Path | Contents |
| --- | --- |
| `edit_tiles/` | Main editor package |
| `mod_project/` | Mod, layer, and project data model plus geometry helpers |
| `railroader_bridge.py` | Live bridge integration |
| `TileEditorBridge/` | In-game F9 workspace and bridge panel |
| `CrossingRuntime/` | Standalone grade-crossing runtime |
| `docs/` | Documentation |
| `run_editor.bat` | Windows launcher |
| `TRACK_TOOL_ROADMAP.md` | Roadmap and planning notes |

## Notes

- Generated Python caches, crash logs, release zips, build output, and terrain
  tile outputs are gitignored.
- This is a **source repo** — packaged release archives are uploaded as GitHub
  releases rather than tracked here.
- OSM and Mapbox data come from third-party services under their own terms.
