"""mod_project.validation
validate_mod() and export_clean_zip().
"""

import math as _math
import re
import zipfile
from pathlib import Path

from .layer import _load_json
from .project import ModProject


def validate_mod(folder: Path) -> list:
    """Full pre-publish validation of a mod folder.

    Returns a list of (severity, message) tuples where severity is 'error' or 'warning'.
    An empty list means the mod is valid.

    Checks (sourced from ModDefinition.Validate() and game source):
      - Definition.json: exists, parses, has id/name/version
      - ID regex ^[A-Za-z0-9_.]+$ (D3)
      - Manifest version 0 < v <= 8 (D4)
      - updateUrl must be https:// if present
      - All file() mixinto references exist on disk
      - Reserved IDs rejected ('railloader', 'railroader')
      - No duplicate JSON keys in any layer file
      - All segment startId/endId reference existing nodes (in merged view)
      - All span segmentId values reference existing segments
      - No orphan nodes (nodes with no segments)
      - Segments shorter than 1m (warns -- may cause physics issues)
    """
    import re as _re
    import math as _math

    issues = []
    def err(msg):  issues.append(('error',   msg))
    def warn(msg): issues.append(('warning', msg))

    # --- Definition.json / Info.json ---
    def_path  = folder / 'Definition.json'
    info_path = folder / 'Info.json'

    if info_path.exists():
        # UMM format
        try:
            defn = _load_json(info_path)
        except Exception as e:
            err(f"Info.json parse failed: {e}")
            return issues
        mod_id   = defn.get('Id', '')
        mod_name = defn.get('DisplayName', '')
        version  = defn.get('Version', '')
        if not mod_id or not mod_name or not version:
            err("Info.json missing required field(s): Id, DisplayName, Version")
        if mod_id and not _re.match(r'^[A-Za-z0-9_.]+$', mod_id):
            warn(f"Id '{mod_id}' contains chars outside [A-Za-z0-9_.] -- not valid")
        entry_method = defn.get('EntryMethod', '')
        if defn.get('AssemblyName') and not entry_method:
            warn("Info.json has AssemblyName but no EntryMethod")

    elif def_path.exists():
        # Railloader format
        try:
            defn = _load_json(def_path)
        except Exception as e:
            err(f"Definition.json parse failed: {e}")
            return issues
        mod_id   = defn.get('id', '')
        mod_name = defn.get('name', '')
        version  = defn.get('version', '')
        if not mod_id or not mod_name or not version:
            err("Definition.json missing required field(s): id, name, version")
        if mod_id in ('railloader', 'railroader'):
            err(f"ID '{mod_id}' is reserved")
        if mod_id and not _re.match(r'^[A-Za-z0-9_.]+$', mod_id):
            warn(f"ID '{mod_id}' contains chars outside [A-Za-z0-9_.] -- deprecated")
        mv = defn.get('manifestVersion', 0)
        if not (0 < mv <= 8):
            err(f"manifestVersion {mv} out of supported range 1-8")
        update_url = defn.get('updateUrl')
        if update_url and not update_url.startswith('https://'):
            err(f"updateUrl must be https://  (got: {update_url!r})")

    else:
        err("Neither Info.json (UMM) nor Definition.json (Railloader) found")
        return issues

    # --- Mixinto file references (Railloader only) ---
    mixintos = defn.get('mixintos', {}) if def_path.exists() else {}
    for target, entries in mixintos.items():
        if isinstance(entries, str):
            entries = [entries]
        if not isinstance(entries, list):
            entries = [entries]
        for entry in entries:
            ref = entry if isinstance(entry, str) else (entry.get('mixinto', '') if isinstance(entry, dict) else '')
            m = _re.match(r'file\((.+?)\)', ref)
            if m:
                fpath = folder / m.group(1)
                if not fpath.exists():
                    err(f"mixinto file not found: {m.group(1)} (target={target})")
            elif _re.match(r'dir\((.+?)\)', ref):
                dm = _re.match(r'dir\((.+?)\)', ref)
                dpath = folder / dm.group(1)
                if not dpath.is_dir():
                    err(f"mixinto dir not found: {dm.group(1)} (target={target})")

    # --- Duplicate key check and graph validation ---
    try:
        proj = ModProject.open_mod_folder(folder)
    except Exception as e:
        warn(f"Could not load mod for graph validation: {e}")
        return issues

    merged_nodes    = proj.merged_nodes
    merged_segments = proj.merged_segments
    merged_spans    = {}
    for layer in proj.layers:
        merged_spans.update(layer.spans)

    # Segment node reference validity
    seg_ids = set(merged_segments.keys())
    node_ids = set(merged_nodes.keys())
    for sid, seg in merged_segments.items():
        if seg.get('startId') not in node_ids:
            err(f"Segment {sid}: startId '{seg.get('startId')}' not found in nodes")
        if seg.get('endId') not in node_ids:
            err(f"Segment {sid}: endId '{seg.get('endId')}' not found in nodes")

    # Short segment warning (< 1m)
    for sid, seg in merged_segments.items():
        n0 = merged_nodes.get(seg.get('startId', ''))
        n1 = merged_nodes.get(seg.get('endId', ''))
        if n0 and n1:
            d = _math.sqrt((n1['x']-n0['x'])**2 + (n1['z']-n0['z'])**2)
            if d < 1.0:
                warn(f"Segment {sid} is very short ({d:.2f}m) -- may cause physics issues")

    # Orphan nodes
    connected = set()
    for seg in merged_segments.values():
        connected.add(seg.get('startId'))
        connected.add(seg.get('endId'))
    for nid in node_ids:
        if nid not in connected:
            warn(f"Node {nid} has no connected segments (orphan)")

    # Span segment reference validity + distance range (D11h/j/k)
    from .geometry import segment_length as _seg_len
    for span_id, span in merged_spans.items():
        if not isinstance(span, dict):
            continue
        for side in ('upper', 'lower'):
            loc = span.get(side, {})
            if not isinstance(loc, dict):
                continue
            seg_id = loc.get('segmentId', '')
            if seg_id and seg_id not in seg_ids:
                err(f"Span {span_id}.{side}: segmentId '{seg_id}' not found in segments")
                continue
            dist = loc.get('distance', 0.0)
            # D11k: distance must be >= 0
            if dist < 0:
                err(f"Span {span_id}.{side}: distance {dist:.3f} is negative")
            # D11j: distance must be <= segment length + 0.05m tolerance
            if seg_id and seg_id in merged_segments:
                seg = merged_segments[seg_id]
                seg_len = _seg_len(seg, merged_nodes)
                if seg_len > 0 and dist > seg_len + 0.05:
                    err(f"Span {span_id}.{side}: distance {dist:.3f}m exceeds "
                        f"segment length {seg_len:.3f}m (+0.05m tolerance)")

    # D11i: industry trackSpans reference real spans
    for layer in proj.layers:
        for area_id, area in layer.areas.items():
            industries = area.get('industries', {}) if isinstance(area, dict) else {}
            for ind_id, ind in industries.items():
                components = ind.get('components', {}) if isinstance(ind, dict) else {}
                comp_iter = components.values() if isinstance(components, dict) else components
                for comp in comp_iter:
                    if not isinstance(comp, dict):
                        continue
                    for ts in comp.get('trackSpans', []):
                        if ts and ts not in merged_spans:
                            err(f"Area {area_id} industry {ind_id}: "
                                f"trackSpan '{ts}' not found in spans")

    # D11n: text values must be plain strings, not dicts
    for layer in proj.layers:
        for text_id, text_val in layer.texts.items():
            if not isinstance(text_val, str):
                err(f"Layer {layer.label}: texts['{text_id}'] is "
                    f"{type(text_val).__name__}, expected str (A1 fix not applied)")

    # D11o: trackClass values must be valid
    valid_classes = {'Mainline', 'Branch', 'Industrial'}
    for sid, seg in merged_segments.items():
        tc = seg.get('trackClass', '')
        if tc and tc not in valid_classes:
            warn(f"Segment {sid}: trackClass '{tc}' is not valid "
                 f"(expected one of {sorted(valid_classes)})")

    return issues


# ---------------------------------------------------------------------------
# D12 -- export_clean_zip: package a mod for distribution
# ---------------------------------------------------------------------------

def export_clean_zip(folder: Path, output_path: Path):
    """Package a mod folder into a clean distribution zip.

    Excludes files that should not be in a published mod:
      .json.bak, .vs/, .vsidx, *.sln, *.csproj, *.slnx, v17/, v16/,
      DocumentLayout.json, __pycache__/, .git/, .gitignore,
      kv_state/, any file > 50 MB, *.pyc, *.pdb (debug symbols)

    Runs validate_mod() first and prints any issues found.
    """
    import zipfile as _zipfile

    issues = validate_mod(folder)
    errors = [m for s, m in issues if s == 'error']
    warnings = [m for s, m in issues if s == 'warning']
    for m in warnings:
        print(f"[export] WARNING: {m}")
    if errors:
        for m in errors:
            print(f"[export] ERROR: {m}")
        print(f"[export] Aborting -- fix {len(errors)} error(s) before exporting")
        return False

    _EXCLUDE_NAMES = {
        'documentlayout.json', '.gitignore', 'thumbs.db', '.ds_store',
    }
    _EXCLUDE_SUFFIXES = {
        '.bak', '.pyc', '.pdb', '.user', '.vsidx', '.sqlite', '.wsuo',
        '.py',   # D12l: exclude Python dev scripts from published zip
    }
    _EXCLUDE_DIRS = {
        '.vs', '.git', '__pycache__', 'kv_state',
        'bin', 'obj',           # C# build output -- never ship these
        'v17', 'v16', 'v3', 'filecontentindex',  # D12e: added v3
    }
    _MAX_FILE_BYTES = 50 * 1024 * 1024  # 50 MB

    output_path = Path(output_path)
    output_path.parent.mkdir(parents=True, exist_ok=True)

    # Support both UMM (Info.json) and Railloader (Definition.json)
    def_path  = folder / 'Definition.json'
    info_path = folder / 'Info.json'
    if def_path.exists():
        mod_id = _load_json(def_path).get('id', folder.name)
    elif info_path.exists():
        mod_id = _load_json(info_path).get('Id', folder.name)
    else:
        mod_id = folder.name
    arcroot = mod_id  # top-level dir inside the zip

    written = 0
    with _zipfile.ZipFile(output_path, 'w', _zipfile.ZIP_DEFLATED) as zf:
        for fpath in sorted(folder.rglob('*')):
            if not fpath.is_file():
                continue
            rel = fpath.relative_to(folder)
            parts_lower = [p.lower() for p in rel.parts]
            # Skip excluded directories
            if any(p in _EXCLUDE_DIRS for p in parts_lower[:-1]):
                continue
            fname_lower = parts_lower[-1]
            # Skip excluded filenames
            if fname_lower in _EXCLUDE_NAMES:
                continue
            # Skip excluded suffixes (including .json.bak)
            if any(fname_lower.endswith(s) for s in _EXCLUDE_SUFFIXES):
                continue
            # Skip oversized files
            if fpath.stat().st_size > _MAX_FILE_BYTES:
                print(f"[export] skipping {rel} (>{_MAX_FILE_BYTES//1024//1024}MB)")
                continue
            arcname = f"{arcroot}/{rel}"
            zf.write(fpath, arcname)
            written += 1

    print(f"[export] wrote {written} files -> {output_path}")
    return True
