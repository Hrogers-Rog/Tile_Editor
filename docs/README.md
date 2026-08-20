# Tile Editor Documentation

A desktop terrain and map editor for Railroader mods, paired with an in-game F9
workspace that edits the same data live.

## Start Here

| Doc | What it covers |
| --- | --- |
| [Getting Started](GETTING_STARTED.md) | Install, run, and a first editing session |
| [Feature And Workspace Index](FEATURE_INDEX.md) | Find the workspace that owns a remembered task |
| [Keybind Reference](KEYBINDS.md) | Every keyboard and mouse binding |

## Editing

| Doc | What it covers |
| --- | --- |
| [Terrain Editing](TERRAIN_EDITING.md) | Brushes, clamps, selection, tile generation, cleanup, OSM |
| [Track Editing](TRACK_EDITING.md) | Nodes, segments, connecting, gauge, geometry tools |
| [Mod Tools](MOD_TOOLS.md) | Layers, progression, areas, scenery, mandelas, spans, calculators |
| [In-Game Geo Workspace](IN_GAME_GEO.md) | The F9 editor, signals, operations, and two-way sync |
| [Operations And Signals](OPERATIONS_AND_SIGNALS.md) | Industries, spans, loaders, passenger service, semaphores, interlockings |

## Data

| Doc | What it covers |
| --- | --- |
| [Data Formats And Examples](SCHEMA_EXAMPLES.md) | Every JSON format with worked examples |

The runtimes that consume this data — `Hrogers.CrossingRuntime` for grade
crossings, Railroad Operations for signals — **do not require the Tile Editor**.
Players install the small runtime; you ship the JSON inside your map mod.

## Offline Manuals

- [Tile Editor User Manual](pdf/Tile-Editor-User-Manual.pdf) — install, keybinds, terrain, track, mod tools, F9
- [Tile Editor Modding Guide](pdf/Tile-Editor-Modding-Guide.pdf) — data formats, authoring, runtimes

Rebuild with `python scripts/build_pdfs.py` (needs `pip install reportlab`).

## Quick Answers

**How do I pan?** Right mouse drag, or middle mouse. In edit mode LMB paints, so
it cannot pan there.

**`B` does two different things.** Outside edit mode it picks the game/bridge
folder; inside edit mode it cycles brush type. Several keys are contextual — see
[Keybinds](KEYBINDS.md#contextual-keys).

**I deleted tiles by mistake.** They moved to a timestamped
`_TileEditor_Deleted_Tiles` folder, and `Ctrl+Z` restores the whole batch.

**A mode seems stuck in game.** Right-click the world — it cancels every active
mode and clears all selections.

**Narrow gauge doesn't render.** The `gauge` field is metadata. Visible narrow and
dual rail geometry comes from
[FUSE Narrow Gauge](https://github.com/Hrogers-Rog/Narrow_Gauge).

**The Reload button is yellow.** Reloading would discard unsaved changes.

## Related Projects

- [FUSE](https://github.com/F-U-S-E-E/FuseDevelopmentGroup) — the modding layer
- [FUSE Narrow Gauge](https://github.com/Hrogers-Rog/Narrow_Gauge) — narrow and dual-gauge rendering
- [Toolshed](https://github.com/Hrogers-Rog/TheToolShed) — service facilities and operations

## Project

- **Repository:** <https://github.com/Hrogers-Rog/Tile_Editor>
- **Roadmap:** [TRACK_TOOL_ROADMAP.md](../TRACK_TOOL_ROADMAP.md)
