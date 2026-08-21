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
        if str(mod_id).lower() in ('railloader', 'railroader', 'fuse'):
            err(f"ID '{mod_id}' is reserved")
        if mod_id and not _re.match(r'^[A-Za-z0-9_.]+$', mod_id):
            warn(f"Id '{mod_id}' contains chars outside [A-Za-z0-9_.] -- not valid")
        entry_method = defn.get('EntryMethod', '')
        if defn.get('AssemblyName') and not entry_method:
            warn("Info.json has AssemblyName but no EntryMethod")
        fuse_data_files = defn.get('FuseDataFiles', [])
        if isinstance(fuse_data_files, str):
            fuse_data_files = [fuse_data_files]
        for relative in fuse_data_files:
            if not isinstance(relative, str) or not relative.strip():
                err("Info.json FuseDataFiles contains an invalid empty entry")
                continue
            if not (folder / relative).is_file():
                err(f"FUSE data file not found: {relative}")

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
        if str(mod_id).lower() in ('railloader', 'railroader', 'fuse'):
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
        if getattr(layer, 'is_fuse_native', False):
            # Native industries are projected into layer.areas for the shared
            # editor UI. Validate their canonical operations representation
            # below so dependency-provided references are classified correctly.
            continue
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

    _validate_native_operations(
        proj,
        merged_spans,
        err,
        warn,
    )
    _validate_native_world(proj, err, warn)
    _validate_native_feature_rules(proj, err, warn)
    _validate_signaling_sidecars(
        folder,
        defn,
        node_ids,
        seg_ids,
        err,
        warn,
    )

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


def _validate_signaling_sidecars(folder, manifest, node_ids, segment_ids,
                                 err, warn):
    """Validate the editor's portable signal and CTC documents together."""
    signal_path = folder / 'train-signals.json'
    ctc_path = folder / 'ctc-system.json'
    if not signal_path.exists() and not ctc_path.exists():
        return

    signal_document = _load_signaling_document(signal_path, err)
    ctc_document = _load_signaling_document(ctc_path, err)
    if signal_document is None and ctc_document is None:
        return

    if not _manifest_has_signal_runtime(manifest):
        err("Signals/CTC: package manifest must require Railroad Operations "
            "('AITraffic') so train-signals.json and ctc-system.json load "
            "during normal gameplay")

    known_signal_ids = set()
    if signal_document is not None:
        signals = signal_document.get('signals')
        if not isinstance(signals, list):
            err("train-signals.json: signals must be an array")
            signals = []
        interlockings = signal_document.get('interlockings')
        if not isinstance(interlockings, list):
            err("train-signals.json: interlockings must be an array")
            interlockings = []
        for index, signal in enumerate(signals):
            prefix = f"train-signals.json signals[{index}]"
            if not isinstance(signal, dict):
                err(f"{prefix}: definition must be an object")
                continue
            signal_id = _signaling_id(signal.get('id'))
            if not signal_id:
                err(f"{prefix}: id is required")
            elif signal_id.lower() in known_signal_ids:
                err(f"{prefix}: duplicate signal id '{signal_id}'")
            else:
                known_signal_ids.add(signal_id.lower())
            head_count = signal.get('headCount', 1)
            if not isinstance(head_count, int) or isinstance(head_count, bool) \
                    or not 1 <= head_count <= 3:
                err(f"{prefix}: headCount must be an integer from 1 through 3")
            if signal.get('initialAspect', 'stop') not in {
                    'stop', 'approach', 'clear', 'diverging-approach',
                    'diverging-clear', 'restricting'}:
                err(f"{prefix}: initialAspect is not supported")
            if signal.get('direction', 'forward') not in {'forward', 'reverse'}:
                err(f"{prefix}: direction must be 'forward' or 'reverse'")
            for field in ('protectedSegmentId',):
                _validate_optional_reference(
                    signal.get(field), segment_ids, prefix, field, err)
            for field in ('protectedSegmentIds', 'approachSegmentIds'):
                values = signal.get(field, [])
                if not isinstance(values, list):
                    err(f"{prefix}: {field} must be an array")
                    continue
                for item_index, value in enumerate(values):
                    _validate_optional_reference(
                        value, segment_ids, prefix,
                        f"{field}[{item_index}]", err)
            _validate_optional_reference(
                signal.get('protectedNodeId'), node_ids,
                prefix, 'protectedNodeId', err)
            attachment = signal.get('trackAttachment')
            if attachment is not None:
                if not isinstance(attachment, dict):
                    err(f"{prefix}: trackAttachment must be an object")
                else:
                    _validate_optional_reference(
                        attachment.get('segmentId'), segment_ids,
                        prefix, 'trackAttachment.segmentId', err,
                        required=bool(attachment.get('locked')))
                    parameter = attachment.get('parameter', 0)
                    if not isinstance(parameter, (int, float)) \
                            or isinstance(parameter, bool) \
                            or not _math.isfinite(parameter) \
                            or not 0 <= parameter <= 1:
                        err(f"{prefix}: trackAttachment.parameter must be "
                            "a finite number from 0 through 1")
        _validate_signal_interlockings(
            interlockings,
            known_signal_ids,
            node_ids,
            segment_ids,
            err,
        )

    if ctc_document is not None:
        _validate_ctc_document(
            ctc_document,
            known_signal_ids,
            node_ids,
            segment_ids,
            err,
            warn,
        )


def _load_signaling_document(path, err):
    if not path.exists():
        return None
    try:
        document = _load_json(path)
    except Exception as exc:
        err(f"{path.name} parse failed: {exc}")
        return None
    if not isinstance(document, dict):
        err(f"{path.name}: root must be an object")
        return None
    if document.get('formatVersion') != 1:
        err(f"{path.name}: formatVersion must be 1")
    return document


def _manifest_has_signal_runtime(manifest):
    values = []
    for key in ('Requirements', 'requires', 'FuseRequires'):
        value = manifest.get(key, []) if isinstance(manifest, dict) else []
        values.extend(value if isinstance(value, list) else [value])
    for value in values:
        if isinstance(value, str):
            candidate = value
        elif isinstance(value, dict):
            candidate = value.get('Id') or value.get('id') or ''
        else:
            candidate = ''
        if str(candidate).strip().lower() in {
                'aitraffic', 'hrogers.signalruntime'}:
            return True
    return False


def _signaling_id(value):
    return value.strip() if isinstance(value, str) else ''


def _validate_optional_reference(value, known_ids, prefix, field, err,
                                 required=False):
    value = _signaling_id(value)
    if not value:
        if required:
            err(f"{prefix}: {field} is required")
        return
    normalized_ids = {str(identifier).lower() for identifier in known_ids}
    if value.lower() not in normalized_ids:
        err(f"{prefix}: {field} references missing id '{value}'")


def _validate_signal_interlockings(interlockings, signal_ids, node_ids,
                                  segment_ids, err):
    known = set()
    for index, interlocking in enumerate(interlockings):
        prefix = f"train-signals.json interlockings[{index}]"
        if not isinstance(interlocking, dict):
            err(f"{prefix}: definition must be an object")
            continue
        interlocking_id = _signaling_id(interlocking.get('id'))
        if not interlocking_id:
            err(f"{prefix}: id is required")
        elif interlocking_id.lower() in known:
            err(f"{prefix}: duplicate interlocking id '{interlocking_id}'")
        else:
            known.add(interlocking_id.lower())
        routes = interlocking.get('routes')
        if not isinstance(routes, list) or len(routes) < 2:
            err(f"{prefix}: routes must contain both conflicting movements")
            continue
        for route_index, route in enumerate(routes):
            route_prefix = f"{prefix} routes[{route_index}]"
            if not isinstance(route, dict):
                err(f"{route_prefix}: definition must be an object")
                continue
            for field, ids in (
                    ('segmentId', segment_ids),):
                _validate_optional_reference(
                    route.get(field), ids, route_prefix, field, err,
                    required=True)
            for field, ids in (
                    ('segmentIds', segment_ids),
                    ('signalIds', signal_ids),
                    ('approachNodeIds', node_ids)):
                values = route.get(field, [])
                if not isinstance(values, list) or not values:
                    err(f"{route_prefix}: {field} must be a non-empty array")
                    continue
                for value_index, value in enumerate(values):
                    _validate_optional_reference(
                        value, ids, route_prefix,
                        f"{field}[{value_index}]", err, required=True)


def _validate_ctc_document(document, signal_ids, node_ids, segment_ids,
                           err, warn):
    arrays = {}
    for name in ('territories', 'controlPoints', 'blocks', 'trainOrders'):
        value = document.get(name)
        if not isinstance(value, list):
            err(f"ctc-system.json: {name} must be an array")
            value = []
        arrays[name] = value

    block_ids = _unique_document_ids(
        arrays['blocks'], 'ctc-system.json blocks', err)
    control_point_ids = _unique_document_ids(
        arrays['controlPoints'], 'ctc-system.json controlPoints', err)
    _unique_document_ids(
        arrays['territories'], 'ctc-system.json territories', err)
    _unique_document_ids(
        arrays['trainOrders'], 'ctc-system.json trainOrders', err)

    for index, block in enumerate(arrays['blocks']):
        if not isinstance(block, dict):
            continue
        prefix = f"ctc-system.json blocks[{index}]"
        if block.get('mode') not in {'abs', 'ctc', 'manual'}:
            err(f"{prefix}: mode must be abs, ctc, or manual")
        values = block.get('segmentIds')
        if not isinstance(values, list) or not values:
            err(f"{prefix}: segmentIds must be a non-empty array")
        else:
            for value_index, value in enumerate(values):
                _validate_optional_reference(
                    value, segment_ids, prefix,
                    f"segmentIds[{value_index}]", err, required=True)
        signals = block.get('signals', {})
        if not isinstance(signals, dict):
            err(f"{prefix}: signals must be an object")
        else:
            for end in ('a', 'b'):
                _validate_optional_reference(
                    signals.get(end), signal_ids, prefix,
                    f"signals.{end}", err)
        next_blocks = block.get('nextBlocks', {})
        if not isinstance(next_blocks, dict):
            err(f"{prefix}: nextBlocks must be an object")
        else:
            for direction in ('fromA', 'fromB'):
                _validate_optional_reference(
                    next_blocks.get(direction), block_ids, prefix,
                    f"nextBlocks.{direction}", err)

    for index, control_point in enumerate(arrays['controlPoints']):
        if not isinstance(control_point, dict):
            continue
        prefix = f"ctc-system.json controlPoints[{index}]"
        switches = control_point.get('switches')
        if not isinstance(switches, list) or not switches:
            err(f"{prefix}: switches must be a non-empty array")
            switches = []
        for switch_index, switch in enumerate(switches):
            if not isinstance(switch, dict):
                err(f"{prefix} switches[{switch_index}]: must be an object")
                continue
            _validate_optional_reference(
                switch.get('nodeId'), node_ids, prefix,
                f"switches[{switch_index}].nodeId", err, required=True)
        routes = control_point.get('routes')
        if not isinstance(routes, list) or not routes:
            err(f"{prefix}: routes must be a non-empty array")
            routes = []
        for route_index, route in enumerate(routes):
            route_prefix = f"{prefix} routes[{route_index}]"
            if not isinstance(route, dict):
                err(f"{route_prefix}: must be an object")
                continue
            _validate_optional_reference(
                route.get('entrySignalId'), signal_ids, route_prefix,
                'entrySignalId', err, required=True)
            values = route.get('blockIds')
            if not isinstance(values, list) or not values:
                err(f"{route_prefix}: blockIds must be a non-empty array")
            else:
                for value_index, value in enumerate(values):
                    _validate_optional_reference(
                        value, block_ids, route_prefix,
                        f"blockIds[{value_index}]", err, required=True)
            settings = route.get('switchSettings')
            if not isinstance(settings, list) or not settings:
                err(f"{route_prefix}: switchSettings must be a non-empty array")
            else:
                for setting_index, setting in enumerate(settings):
                    if not isinstance(setting, dict):
                        err(f"{route_prefix} switchSettings[{setting_index}]: "
                            "must be an object")
                        continue
                    _validate_optional_reference(
                        setting.get('nodeId'), node_ids, route_prefix,
                        f"switchSettings[{setting_index}].nodeId",
                        err, required=True)

    assigned_control_points = set()
    assigned_blocks = set()
    for index, territory in enumerate(arrays['territories']):
        if not isinstance(territory, dict):
            continue
        prefix = f"ctc-system.json territories[{index}]"
        if territory.get('mode') not in {'train-orders', 'abs', 'ctc'}:
            err(f"{prefix}: mode must be train-orders, abs, or ctc")
        for field, ids, assigned in (
                ('controlPointIds', control_point_ids,
                 assigned_control_points),
                ('blockIds', block_ids, assigned_blocks)):
            values = territory.get(field, [])
            if not isinstance(values, list):
                err(f"{prefix}: {field} must be an array")
                continue
            for value_index, value in enumerate(values):
                _validate_optional_reference(
                    value, ids, prefix,
                    f"{field}[{value_index}]", err, required=True)
                if _signaling_id(value):
                    assigned.add(_signaling_id(value).lower())
    for identifier in sorted(control_point_ids):
        if identifier.lower() not in assigned_control_points:
            warn(f"ctc-system.json: control point '{identifier}' is not assigned "
                 "to a territory")
    for identifier in sorted(block_ids):
        if identifier.lower() not in assigned_blocks:
            warn(f"ctc-system.json: block '{identifier}' is not assigned to a territory")


def _unique_document_ids(items, label, err):
    identifiers = set()
    originals = set()
    for index, item in enumerate(items):
        prefix = f"{label}[{index}]"
        if not isinstance(item, dict):
            err(f"{prefix}: definition must be an object")
            continue
        identifier = _signaling_id(item.get('id'))
        if not identifier:
            err(f"{prefix}: id is required")
            continue
        normalized = identifier.lower()
        if normalized in identifiers:
            err(f"{prefix}: duplicate id '{identifier}'")
            continue
        identifiers.add(normalized)
        originals.add(identifier)
    return originals


def _validate_native_world(proj, err, warn):
    """Validate native-only world objects that legacy JSON cannot express."""
    for layer in proj.layers:
        if not getattr(layer, 'is_fuse_native', False):
            continue
        world = layer._raw.get('world') or {}
        if not isinstance(world, dict):
            err(f"Native layer {layer.label}: world must be an object")
            continue
        surfaces = world.get('waterSurfaces') or {}
        if not isinstance(surfaces, dict):
            err(f"Native layer {layer.label}: world.waterSurfaces must be an object")
            continue
        removals = world.get('removals') or {}
        removed_ids = set()
        if isinstance(removals, dict):
            values = removals.get('waterSurfaces') or []
            if isinstance(values, list):
                removed_ids = {str(value) for value in values if value is not None}

        for surface_id, surface in surfaces.items():
            prefix = f"Native water surface {surface_id}"
            if not isinstance(surface, dict):
                err(f"{prefix}: definition must be an object")
                continue
            if str(surface_id) in removed_ids:
                err(f"{prefix}: cannot be defined and removed in the same file")
            points = surface.get('points') or []
            if not isinstance(points, list) or len(points) < 3:
                err(f"{prefix}: points must contain at least three boundary positions")
                continue
            parsed = []
            for index, point in enumerate(points):
                if not isinstance(point, dict):
                    err(f"{prefix}: points[{index}] must be an x/y/z object")
                    continue
                coords = []
                valid = True
                for axis in ('x', 'y', 'z'):
                    value = point.get(axis)
                    if not isinstance(value, (int, float)) or not _math.isfinite(value):
                        err(f"{prefix}: points[{index}].{axis} must be a finite number")
                        valid = False
                    coords.append(float(value) if isinstance(value, (int, float)) else 0.0)
                if valid:
                    parsed.append(tuple(coords))
            for field, default, upper in (
                ('uvScale', 1.0, None),
                ('triangleDensity', 0.2, 1.0),
                ('maximumTriangleArea', 50.0, None),
            ):
                value = surface.get(field, default)
                if not isinstance(value, (int, float)) or not _math.isfinite(value) or value <= 0:
                    err(f"{prefix}: {field} must be a finite number greater than zero")
                elif upper is not None and value > upper:
                    err(f"{prefix}: {field} must be at most {upper:g}")
            for field in ('sourceLakePath', 'materialName'):
                if field in surface and (not isinstance(surface[field], str)
                                         or not surface[field].strip()):
                    err(f"{prefix}: {field} must be a non-empty string when present")
            if len(parsed) == len(points) and _water_polygon_self_intersects(parsed):
                err(f"{prefix}: boundary crosses itself in the X/Z plane")
            if bool(surface.get('lockHeight', True)) and parsed:
                first_y = parsed[0][1]
                if any(abs(point[1] - first_y) > 0.01 for point in parsed[1:]):
                    warn(f"{prefix}: lockHeight is on, so all point elevations will use the first point's Y value")


def _water_polygon_self_intersects(points):
    """Return True when non-adjacent polygon edges cross in X/Z."""
    def orient(a, b, c):
        return (b[0] - a[0]) * (c[2] - a[2]) - (b[2] - a[2]) * (c[0] - a[0])

    def crosses(a, b, c, d):
        ab_c = orient(a, b, c)
        ab_d = orient(a, b, d)
        cd_a = orient(c, d, a)
        cd_b = orient(c, d, b)
        return ((ab_c > 1e-6 and ab_d < -1e-6)
                or (ab_c < -1e-6 and ab_d > 1e-6)) and (
                    (cd_a > 1e-6 and cd_b < -1e-6)
                    or (cd_a < -1e-6 and cd_b > 1e-6)
                )

    count = len(points)
    for first in range(count):
        a = points[first]
        b = points[(first + 1) % count]
        for second in range(first + 1, count):
            if second == first or second == (first + 1) % count:
                continue
            if first == 0 and second == count - 1:
                continue
            c = points[second]
            d = points[(second + 1) % count]
            if crosses(a, b, c, d):
                return True
    return False


def _validate_native_feature_rules(proj, err, warn):
    """Validate native player options before a package is exported."""
    operators = {
        'equals', 'notEquals', 'greaterThan', 'greaterThanOrEqual',
        'lessThan', 'lessThanOrEqual',
    }
    numeric_operators = operators - {'equals', 'notEquals'}
    for layer in proj.layers:
        if not getattr(layer, 'is_fuse_native', False):
            continue
        raw = layer._raw
        settings = raw.get('settings') or {}
        rules = raw.get('featureRules') or {}
        if not isinstance(settings, dict):
            err(f"Native layer {layer.label}: settings must be an object")
            settings = {}
        if not isinstance(rules, dict):
            err(f"Native layer {layer.label}: featureRules must be an object")
            continue

        tracks = raw.get('tracks') if isinstance(raw.get('tracks'), dict) else {}
        operations = raw.get('operations') if isinstance(raw.get('operations'), dict) else {}
        world = raw.get('world') if isinstance(raw.get('world'), dict) else {}
        progression = raw.get('progression') if isinstance(raw.get('progression'), dict) else {}
        audio = raw.get('audio') if isinstance(raw.get('audio'), dict) else {}
        known = {
            'trackNodes': _native_object_ids(tracks.get('nodes')),
            'trackSegments': _native_object_ids(tracks.get('segments')),
            'trackSpans': _native_object_ids(tracks.get('spans')),
            'trackAreas': _native_object_ids(tracks.get('areas')),
            'loads': _native_object_ids(operations.get('loads')),
            'industries': _native_object_ids(operations.get('industries')),
            'industryComponents': _native_component_ids(operations.get('industries')),
            'loaders': _native_object_ids(operations.get('loaders')),
            'turntables': _native_object_ids(operations.get('turntables')),
            'stations': _native_object_ids(operations.get('stations')),
            'scenery': _native_object_ids(world.get('scenery')),
            'splineys': _native_object_ids(world.get('splineys')),
            'waterSurfaces': _native_object_ids(world.get('waterSurfaces')),
            'telegraphPoles': _native_object_ids(world.get('telegraphPoles')),
            'mapLabels': _native_object_ids(world.get('mapLabels')),
            'mapMasks': _native_object_ids(world.get('mapMasks')),
            'mapTiles': _native_object_ids(world.get('mapTiles')),
            'sceneClones': _native_object_ids(world.get('sceneClones')),
            'progressions': _native_object_ids(progression.get('progressions')),
            'mapFeatures': _native_object_ids(progression.get('mapFeatures')),
            'whistles': _native_object_ids(audio.get('whistles')),
            'horns': _native_object_ids(audio.get('horns')),
            'bells': _native_object_ids(audio.get('bells')),
        }
        for rule_id, rule in rules.items():
            prefix = f"Native feature rule {rule_id}"
            if not isinstance(rule_id, str) or not re.match(r'^[A-Za-z0-9][A-Za-z0-9._:-]*$', rule_id):
                err(f"{prefix}: rule ID is not a valid FUSE ID")
            if not isinstance(rule, dict):
                err(f"{prefix}: definition must be an object")
                continue
            setting_id = rule.get('setting')
            setting = settings.get(setting_id) if isinstance(setting_id, str) else None
            if not isinstance(setting, dict):
                err(f"{prefix}: setting '{setting_id}' is not declared in this layer")
            elif not bool(setting.get('reloadRequired')):
                warn(f"{prefix}: setting '{setting_id}' should set reloadRequired=true")
            operator = rule.get('operator', 'equals')
            if operator not in operators:
                err(f"{prefix}: unsupported operator '{operator}'")
            setting_type = str(setting.get('type', 'text')).lower() if isinstance(setting, dict) else ''
            if operator in numeric_operators and setting_type not in {
                    'number', 'float', 'double', 'int', 'integer'}:
                err(f"{prefix}: numeric operator requires a number setting")
            if 'value' not in rule or rule.get('value') is None:
                err(f"{prefix}: comparison value is required")
            targets = rule.get('targets')
            if not isinstance(targets, dict):
                err(f"{prefix}: targets must be an object")
                continue
            target_count = 0
            for kind, values in targets.items():
                if kind not in known:
                    err(f"{prefix}: unsupported target kind '{kind}'")
                    continue
                if not isinstance(values, list):
                    err(f"{prefix}: targets.{kind} must be an array")
                    continue
                seen = set()
                for target_id in values:
                    target_count += 1
                    if not isinstance(target_id, str) or not target_id.strip():
                        err(f"{prefix}: targets.{kind} contains a blank/non-string ID")
                    elif target_id not in known[kind]:
                        err(f"{prefix}: targets.{kind} object '{target_id}' is not authored in this layer")
                    elif target_id.lower() in seen:
                        warn(f"{prefix}: targets.{kind} repeats '{target_id}'")
                    else:
                        seen.add(target_id.lower())
            if target_count == 0:
                err(f"{prefix}: add at least one authored object target")


def _native_object_ids(value):
    return set(value.keys()) if isinstance(value, dict) else set()


def _native_component_ids(industries):
    result = set()
    if not isinstance(industries, dict):
        return result
    for industry_id, industry in industries.items():
        components = industry.get('components') if isinstance(industry, dict) else None
        if not isinstance(components, dict):
            continue
        result.update(f"{industry_id}/{component_id}" for component_id in components)
    return result


def _validate_native_operations(proj, merged_spans, err, warn):
    """Validate native FUSE operations without mislabeling dependency refs.

    A normal add-on may intentionally point at a base-game or required-mod
    area/span/load/industry. Those unresolved local references are warnings.
    A standalone map suppresses the base world and must provide every reference,
    so the same conditions are errors.
    """
    native_layers = [
        layer for layer in proj.layers
        if getattr(layer, 'is_fuse_native', False)
    ]
    if not native_layers:
        return

    complete_map = any(
        bool((layer._raw.get('map') or {}).get('suppressBaseWorld'))
        for layer in native_layers
        if isinstance(layer._raw.get('map'), dict)
    )

    def unresolved(message):
        (err if complete_map else warn)(message + (
            " (standalone map must define it locally)"
            if complete_map
            else " (may be supplied by the base game or a required package)"
        ))

    area_ids = set()
    span_ids = {
        str(key) for key, value in merged_spans.items()
        if value is not None
    }
    load_ids = set()
    industry_entries = []
    station_entries = []
    loader_entries = []

    for layer in native_layers:
        raw = layer._raw
        tracks = raw.get('tracks') or {}
        operations = raw.get('operations') or {}
        if not isinstance(tracks, dict):
            tracks = {}
        if not isinstance(operations, dict):
            operations = {}

        areas = tracks.get('areas') or {}
        if isinstance(areas, dict):
            area_ids.update(
                str(key) for key, value in areas.items()
                if value is not None
            )
        loads = operations.get('loads') or {}
        if isinstance(loads, dict):
            load_ids.update(
                str(key) for key, value in loads.items()
                if value is not None
            )
        industries = operations.get('industries') or {}
        if isinstance(industries, dict):
            industry_entries.extend(
                (str(key), value)
                for key, value in industries.items()
                if isinstance(value, dict)
            )
        stations = operations.get('stations') or {}
        if isinstance(stations, dict):
            station_entries.extend(
                (str(key), value)
                for key, value in stations.items()
                if isinstance(value, dict)
            )
        loaders = operations.get('loaders') or {}
        if isinstance(loaders, dict):
            loader_entries.extend(
                (str(key), value)
                for key, value in loaders.items()
                if isinstance(value, dict)
            )

    industry_ids = {item[0] for item in industry_entries}
    passenger_entries = []
    track_bound_types = {
        'loader', 'unloader', 'formulaic', 'repairtrack', 'teamtrack',
        'interchange', 'interchangedloader', 'interchangedunloader',
        'teleportloading', 'progression', 'passengerstop',
    }
    load_types = {
        'loader', 'unloader', 'interchangedloader',
        'interchangedunloader', 'teleportloading',
    }

    for industry_id, industry in industry_entries:
        area_id = str(industry.get('areaId') or '').strip()
        if not area_id:
            err(f"Native industry {industry_id}: areaId is required")
        elif area_id not in area_ids:
            unresolved(
                f"Native industry {industry_id}: areaId '{area_id}' "
                "is not defined in this package"
            )

        components = industry.get('components') or {}
        if not isinstance(components, dict):
            err(f"Native industry {industry_id}: components must be an object")
            continue
        for component_id, component in components.items():
            if not isinstance(component, dict):
                err(
                    f"Native industry {industry_id} component {component_id}: "
                    "definition must be an object"
                )
                continue
            component_type = str(component.get('type') or '').strip()
            type_key = component_type.lower()
            spans = component.get('trackSpanIds') or []
            if isinstance(spans, str):
                spans = [spans]
            if not isinstance(spans, list):
                err(
                    f"Native industry {industry_id} component {component_id}: "
                    "trackSpanIds must be an array"
                )
                spans = []
            if type_key in track_bound_types and not any(
                    str(value or '').strip() for value in spans):
                err(
                    f"Native industry {industry_id} component {component_id}: "
                    f"{component_type or 'component'} needs a TrackSpan"
                )
            for span_id in spans:
                span_id = str(span_id or '').strip()
                if not span_id:
                    err(
                        f"Native industry {industry_id} component {component_id}: "
                        "trackSpanIds contains a blank value"
                    )
                elif span_id not in span_ids:
                    unresolved(
                        f"Native industry {industry_id} component {component_id}: "
                        f"TrackSpan '{span_id}' is not defined in this package"
                    )

            load_id = str(component.get('loadId') or '').strip()
            if type_key in load_types and not load_id:
                err(
                    f"Native industry {industry_id} component {component_id}: "
                    f"{component_type or 'component'} needs a loadId"
                )
            elif load_id and load_id not in load_ids:
                unresolved(
                    f"Native industry {industry_id} component {component_id}: "
                    f"loadId '{load_id}' is not defined in this package"
                )

            if type_key == 'passengerstop':
                stop_id = str(
                    component.get('passengerStopId') or industry_id
                ).strip()
                code = str(component.get('timetableCode') or '').strip()
                if not stop_id:
                    err(
                        f"Native industry {industry_id} component {component_id}: "
                        "passengerStopId is required"
                    )
                if not code:
                    err(
                        f"Native industry {industry_id} component {component_id}: "
                        "timetableCode is required"
                    )
                neighbors = component.get('neighborIds') or []
                if isinstance(neighbors, str):
                    neighbors = [neighbors]
                if not isinstance(neighbors, list):
                    err(
                        f"Native industry {industry_id} component {component_id}: "
                        "neighborIds must be an array"
                    )
                    neighbors = []
                passenger_entries.append({
                    'label': f"{industry_id}/{component_id}",
                    'stop_id': stop_id,
                    'code': code,
                    'neighbors': [
                        str(value or '').strip() for value in neighbors
                        if str(value or '').strip()
                    ],
                })
                _validate_native_passenger_branches(
                    industry_id,
                    str(component_id),
                    component.get('branchDefinitions'),
                    err,
                )

    _validate_passenger_network(passenger_entries, unresolved, err, warn)

    passenger_ids = {
        item['stop_id'] for item in passenger_entries if item['stop_id']
    }
    for station_id, station in station_entries:
        stop_id = str(station.get('passengerStopId') or '').strip()
        if not stop_id:
            err(f"Native station {station_id}: passengerStopId is required")
        elif stop_id not in passenger_ids:
            unresolved(
                f"Native station {station_id}: passengerStopId '{stop_id}' "
                "is not defined in this package"
            )

    for loader_id, loader in loader_entries:
        industry_id = str(loader.get('industryId') or '').strip()
        if industry_id and industry_id not in industry_ids:
            unresolved(
                f"Native physical loader {loader_id}: industryId "
                f"'{industry_id}' is not defined in this package"
            )


def _validate_passenger_network(entries, unresolved, err, warn):
    stop_owners = {}
    code_owners = {}
    by_stop = {}
    for entry in entries:
        stop_id = entry['stop_id']
        code = entry['code']
        if stop_id:
            previous = stop_owners.get(stop_id.lower())
            if previous:
                err(
                    f"Passenger stop ID '{stop_id}' is used by both "
                    f"{previous} and {entry['label']}"
                )
            else:
                stop_owners[stop_id.lower()] = entry['label']
                by_stop[stop_id.lower()] = entry
        if code:
            previous = code_owners.get(code.lower())
            if previous:
                err(
                    f"Passenger timetable code '{code}' is used by both "
                    f"{previous} and {entry['label']}"
                )
            else:
                code_owners[code.lower()] = entry['label']

    for entry in entries:
        for neighbor_id in entry['neighbors']:
            neighbor = by_stop.get(neighbor_id.lower())
            if neighbor is None:
                unresolved(
                    f"Passenger stop {entry['stop_id']}: neighbor "
                    f"'{neighbor_id}' is not defined in this package"
                )
                continue
            if entry['stop_id'].lower() not in {
                    value.lower() for value in neighbor['neighbors']}:
                warn(
                    f"Passenger link {entry['stop_id']} -> {neighbor_id} "
                    "is one-way; add the reciprocal neighbor unless this is "
                    "intentional"
                )


def _validate_native_passenger_branches(
        industry_id, component_id, definitions, err):
    if definitions is None:
        return
    label = f"Native passenger {industry_id}/{component_id}"
    if not isinstance(definitions, list):
        err(f"{label}: branchDefinitions must be an array")
        return
    seen = set()
    for index, definition in enumerate(definitions):
        if not isinstance(definition, dict):
            err(f"{label}: branchDefinitions[{index}] must be an object")
            continue
        branch = str(definition.get('branch') or '').strip()
        if not branch:
            err(f"{label}: branchDefinitions[{index}].branch is required")
        elif branch.lower() in seen:
            err(f"{label}: branch '{branch}' is defined more than once")
        else:
            seen.add(branch.lower())
        travel = definition.get('traverseTimeToNext', 0)
        if not isinstance(travel, (int, float)) or not _math.isfinite(travel) or travel < 0:
            err(
                f"{label}: branchDefinitions[{index}].traverseTimeToNext "
                "must be zero or greater"
            )
        intermediates = definition.get('intermediates') or {}
        if not isinstance(intermediates, dict):
            err(f"{label}: branchDefinitions[{index}].intermediates must be an object")
            continue
        for stop_id, intermediate in intermediates.items():
            if not isinstance(intermediate, dict):
                err(f"{label}: intermediate '{stop_id}' must be an object")
                continue
            code = str(intermediate.get('code') or '').strip()
            travel = intermediate.get('traverseTimeToNext', 0)
            if not code:
                err(f"{label}: intermediate '{stop_id}' needs a code")
            if (not isinstance(travel, (int, float))
                    or not _math.isfinite(travel) or travel < 0):
                err(
                    f"{label}: intermediate '{stop_id}' "
                    "traverseTimeToNext must be zero or greater"
                )


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
