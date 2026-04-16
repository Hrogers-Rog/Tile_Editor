# Railroader Tile Editor

A Python-based terrain and map editor for **Railroader** mods.

This project combines:
- terrain tile loading and generation
- track graph editing
- spliney editing for roads, rivers, and trestles
- geometry drafting tools for arcs, grade work, turnout work, and piece-based assembly
- bridge/live graph integration through `railroader_bridge.py`

## Project Layout

- `edit_tiles/`: main editor package
- `mod_project/`: mod/layer/project data model and geometry helpers
- `railroader_bridge.py`: live bridge integration
- `run_editor.bat`: Windows launcher
- `TRACK_TOOL_ROADMAP.md`: tool roadmap and planning notes
- `HunterR_Map_Editor_Guide.pdf`: usage guide

## Requirements

- Python 3.14+
- `pygame-ce`
- `numpy`
- `Pillow`
- `requests`
- `scipy`

Install them with:

```bash
pip install -r requirements.txt
```

## Running

Windows:

```bat
run_editor.bat
```

Or directly:

```bash
python -m edit_tiles
```

## Notes

- Generated Python caches, crash logs, release zips, and terrain tile outputs are ignored in Git.
- The repository is set up as a **source repo**; packaged release archives should be uploaded separately as GitHub releases if needed.
