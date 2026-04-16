"""mod_project.progression
ProgressionProject — sections, features, area data.
"""

import copy as _copy
from pathlib import Path
from typing import Dict, List, Optional

from .layer import Layer


class ProgressionSection:
    """One purchasable section in the progression tree."""
    __slots__ = ('id', 'display_name', 'description', 'prerequisites',
                 'delivery_phases', 'enable_features', 'disable_features',
                 'enable_features_on_available')
    def __init__(self, sid, d):
        self.id               = sid
        self.display_name     = d.get('displayName', sid)
        self.description      = d.get('description', '')
        self.prerequisites    = list(d.get('prerequisiteSections', {}).keys())
        self.delivery_phases  = d.get('deliveryPhases', [{'cost': 0}])
        self.enable_features  = list(d.get('enableFeaturesOnUnlock', {}).keys())
        self.disable_features = list(d.get('disableFeaturesOnUnlock', {}).keys())
        # Confirmed in AlinasMapMod/Definitions/SerializedSection.cs --
        # features enabled when section becomes available (purchasable),
        # not just when it is fully unlocked.
        self.enable_features_on_available = list(d.get('enableFeaturesOnAvailable', {}).keys())

    def to_dict(self):
        d = {
            'displayName':            self.display_name,
            'description':            self.description,
            'prerequisiteSections':   {k: True for k in self.prerequisites},
            'deliveryPhases':         self.delivery_phases,
            'enableFeaturesOnUnlock': {k: True for k in self.enable_features},
        }
        if self.disable_features:
            d['disableFeaturesOnUnlock'] = {k: True for k in self.disable_features}
        if self.enable_features_on_available:
            d['enableFeaturesOnAvailable'] = {k: True for k in self.enable_features_on_available}
        return d


class MapFeature:
    """A purchasable map feature unlocked by a progression section."""
    __slots__ = ('id', 'display_name', 'name', 'description', 'prerequisites',
                 'areas_enable', 'track_groups_available', 'track_groups_enable',
                 'sandbox_default', 'unlock_exclude', 'unlock_include',
                 'unlock_include_components')
    def __init__(self, fid, d):
        self.id                       = fid
        self.display_name             = d.get('displayName', fid)
        self.name                     = d.get('name', fid)
        self.description              = d.get('description', '')
        self.prerequisites            = list(d.get('prerequisites', {}).keys())
        self.areas_enable             = list(d.get('areasEnableOnUnlock', {}).keys())
        self.track_groups_available   = list(d.get('trackGroupsAvailableOnUnlock', {}).keys())
        self.track_groups_enable      = list(d.get('trackGroupsEnableOnUnlock', {}).keys())
        self.sandbox_default          = bool(d.get('defaultEnableInSandbox', True))
        self.unlock_exclude           = list(d.get('unlockExcludeIndustries', {}).keys())
        self.unlock_include           = list(d.get('unlockIncludeIndustries', {}).keys())
        self.unlock_include_components = dict(d.get('unlockIncludeIndustryComponents', {}))

    def to_dict(self):
        d = {
            'displayName':             self.display_name,
            'name':                    self.name,
            'description':             self.description,
            'prerequisites':           {k: True for k in self.prerequisites},
            'areasEnableOnUnlock':     {k: True for k in self.areas_enable},
            'trackGroupsAvailableOnUnlock': {k: True for k in self.track_groups_available},
            'trackGroupsEnableOnUnlock':    {k: True for k in self.track_groups_enable},
            'defaultEnableInSandbox':  self.sandbox_default,
            'gameObjectsEnableOnUnlock': {},
        }
        if self.unlock_exclude:
            d['unlockExcludeIndustries'] = {k: True for k in self.unlock_exclude}
        if self.unlock_include:
            d['unlockIncludeIndustries'] = {k: True for k in self.unlock_include}
        if self.unlock_include_components:
            d['unlockIncludeIndustryComponents'] = self.unlock_include_components
        return d


class AreaIndustry:
    """One industry inside an area."""
    def __init__(self, iid, d):
        d = d or {}
        self.id            = iid
        self.name          = d.get('name', iid)
        self.local_pos     = _copy.deepcopy(d.get('localPosition', {'x':0,'y':0,'z':0}))
        self.uses_contract = bool(d.get('usesContract', False))
        self.components    = _copy.deepcopy(d.get('components', {}) or {})
        self.extra         = _copy.deepcopy({
            k: v for k, v in d.items()
            if k not in ('name', 'localPosition', 'usesContract', 'components')
        })

    @property
    def local_position(self):
        return self.local_pos

    @local_position.setter
    def local_position(self, value):
        self.local_pos = value

    def to_dict(self):
        d = _copy.deepcopy(self.extra)
        d.update({
            'name':          self.name,
            'localPosition': _copy.deepcopy(self.local_pos),
            'usesContract':  self.uses_contract,
            'components':    _copy.deepcopy(self.components),
        })
        return d


class Area:
    """A town/area with position, radius, and industries."""
    def __init__(self, aid, d):
        d = d or {}
        self.id         = aid
        pos             = d.get('position', {'x':0,'y':0,'z':0})
        self.x          = float(pos.get('x', 0))
        self.y          = float(pos.get('y', 0))
        self.z          = float(pos.get('z', 0))
        self.name       = d.get('name', aid)
        self.radius     = float(d.get('radius', 500))
        self.order      = int(d.get('order', 0))
        self.tag_color  = _copy.deepcopy(d.get('tagColor', [0.5, 0.5, 0.5]))
        raw_ind         = d.get('industries', {}) or {}
        self.industries = {iid: AreaIndustry(iid, iv)
                           for iid, iv in raw_ind.items() if iv}
        self.extra      = _copy.deepcopy({
            k: v for k, v in d.items()
            if k not in ('name', 'position', 'radius', 'order', 'tagColor', 'industries')
        })

    def to_dict(self):
        d = _copy.deepcopy(self.extra)
        d.update({
            'name':       self.name,
            'position':   {'x': self.x, 'y': self.y, 'z': self.z},
            'radius':     self.radius,
            'order':      self.order,
            'tagColor':   _copy.deepcopy(self.tag_color),
            'industries': {iid: ind.to_dict()
                           for iid, ind in self.industries.items()},
        })
        return d


class ProgressionProject:
    """Loads and manages progressions.json + all area data across town files."""

    def __init__(self, mod_project: 'ModProject'):
        self.mod         = mod_project
        self.dirty       = False

        # Load progressions.json
        self._prog_layer = self._find_layer('progressions.json')
        self.sections:  Dict[str, ProgressionSection] = {}
        self.features:  Dict[str, MapFeature]         = {}
        self._load_progressions()

        # Load areas from all town layers
        self.areas: Dict[str, Area] = {}       # area_id -> Area
        self.area_layer: Dict[str, int] = {}   # area_id -> layer_idx
        self._load_areas()

    def _find_layer(self, filename: str) -> 'Optional[Layer]':
        for layer in self.mod.layers:
            if layer.path.name.lower() == filename.lower():
                return layer
        return None

    def _load_progressions(self):
        if not self._prog_layer:
            return
        d = self._prog_layer._raw
        for fid, fv in (d.get('mapFeatures') or {}).items():
            if fv:
                self.features[fid] = MapFeature(fid, fv)
        progs = d.get('progressions') or {}
        for pid, pv in progs.items():
            if pv:
                for sid, sv in (pv.get('sections') or {}).items():
                    if sv:
                        self.sections[sid] = ProgressionSection(sid, sv)

    def _load_areas(self):
        for li, layer in enumerate(self.mod.layers):
            for aid, area_dict in layer.areas.items():
                if area_dict:
                    try:
                        self.areas[aid]       = Area(aid, area_dict)
                        self.area_layer[aid]  = li
                    except Exception as e:
                        print(f"[prog] area {aid} failed: {e}")

    def save(self):
        """Write back progressions.json."""
        if not self._prog_layer:
            return
        # Reconstruct raw
        raw = self._prog_layer._raw
        # mapFeatures
        raw['mapFeatures'] = {fid: f.to_dict()
                               for fid, f in self.features.items()}
        # progressions -- preserve ALL existing progression keys.
        # Previously used next(iter(progs)) which saved only the first key,
        # silently discarding every other progression in multi-progression files.
        progs = raw.get('progressions') or {}
        if not progs:
            # No existing progressions: create a single default key
            progs = {'progression': {}}
        new_progs = {}
        for pid, existing_prog in progs.items():
            existing_prog = dict(existing_prog or {})
            existing_prog = {k: v for k, v in existing_prog.items() if k != 'sections'}
            existing_prog['sections'] = {sid: s.to_dict() for sid, s in self.sections.items()}
            new_progs[pid] = existing_prog
        raw['progressions'] = new_progs
        self._prog_layer.dirty = True
        self._prog_layer.save()
        self.dirty = False

    def save_area(self, area_id: str):
        """Write back a modified area to its town layer."""
        li = self.area_layer.get(area_id)
        if li is None:
            return
        layer = self.mod.layers[li]
        area  = self.areas[area_id]
        layer.areas[area_id] = area.to_dict()
        # Also update raw
        if 'areas' not in layer._raw:
            layer._raw['areas'] = {}
        layer._raw['areas'][area_id] = area.to_dict()
        layer.dirty = True
        layer.save()

    def add_section(self, sid: str, display_name: str,
                    prereqs: list, cost: int, feature_id: str):
        self.sections[sid] = ProgressionSection(sid, {
            'displayName':            display_name,
            'description':            '',
            'prerequisiteSections':   {k: True for k in prereqs},
            'deliveryPhases':         [{'cost': cost}],
            'enableFeaturesOnUnlock': {feature_id: True} if feature_id else {},
        })
        self.dirty = True

    def add_feature(self, fid: str, display_name: str):
        self.features[fid] = MapFeature(fid, {
            'displayName': display_name,
            'name':        display_name,
            'description': '',
        })
        self.dirty = True

    def delete_section(self, sid: str):
        self.sections.pop(sid, None)
        self.dirty = True

    def delete_feature(self, fid: str):
        self.features.pop(fid, None)
        self.dirty = True

    def section_chain(self) -> list:
        """Return sections sorted topologically (prerequisites first).

        Uses iterative DFS with an in-progress set to detect cycles.
        Cycles are broken by skipping the back-edge and emitting a warning.
        """
        visited:     list = []
        visited_set: set  = set()
        in_progress: set  = set()

        def visit(sid: str):
            if sid in visited_set:
                return
            if sid in in_progress:
                print(f"[progression] cycle detected at section '{sid}' -- skipping back-edge")
                return
            in_progress.add(sid)
            sec = self.sections.get(sid)
            if sec:
                for p in sec.prerequisites:
                    # Guard: prerequisite may reference a section not in this file
                    if p in self.sections:
                        visit(p)
            in_progress.discard(sid)
            if sid not in visited_set:
                visited_set.add(sid)
                visited.append(sid)

        for sid in self.sections:
            visit(sid)
        return [self.sections[sid] for sid in visited if sid in self.sections]

