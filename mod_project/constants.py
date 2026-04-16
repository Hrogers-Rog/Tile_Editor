"""mod_project.constants
All enums, known values, magic numbers, and lookup tables.
No dependencies on other mod_project modules.
"""

import random as _random

# ---------------------------------------------------------------------------
# ID generation helpers
# ---------------------------------------------------------------------------
_ID_CHARS = 'abcdefghijklmnopqrstuvwxyz0123456789'
_used_ids: set = set()

def _rand_chars(n: int = 4) -> str:
    """Return n random chars from [a-z0-9], guaranteed unique within this process."""
    while True:
        s = ''.join(_random.choice(_ID_CHARS) for _ in range(n))
        if s not in _used_ids:
            _used_ids.add(s)
            return s


# ---------------------------------------------------------------------------
# Layer type constants
# ---------------------------------------------------------------------------
LAYER_BASE      = 'base'       # base game graph-data.json
LAYER_GRAPH     = 'graph'      # mod game-graph.json
LAYER_TOWN      = 'town'       # town_X.json
LAYER_RIVERS    = 'rivers'     # rivers.json
LAYER_MIGRATION = 'migration'  # *Mig.json
LAYER_OTHER     = 'other'      # anything else

# Colour palette per layer type (R,G,B)
LAYER_COLORS = {
    LAYER_BASE:      (160, 160, 160),
    LAYER_GRAPH:     (255, 230,  50),
    LAYER_TOWN:      None,           # assigned dynamically per town
    LAYER_RIVERS:    ( 80, 160, 255),
    LAYER_MIGRATION: (255, 100, 100),
    LAYER_OTHER:     (200, 200, 200),
}

TOWN_PALETTE = [
    (100, 220, 120), (220, 120, 100), (120, 180, 255),
    (255, 180,  80), (200, 100, 255), ( 80, 220, 200),
    (255, 140, 180), (180, 220,  80), (120, 120, 255),
    (255, 200,  80),
]

# TrackClass enum from game source (Track/TrackClass.cs) -- exactly 3 values:
#   Mainline (0), Branch (1), Industrial (2)
# Default speed limits (TrackSegment.GetExpectedSpeedLimit):
#   Mainline=35, Branch=25, Industrial=15
# JSON serialized via CamelCaseNamingStrategy + StringEnumConverter -> exact enum name.
# Real mods confirmed: "Mainline", "Branch", "Industrial" -- no aliases.
TRACK_CLASS_NAMES = {
    # Integer enum -> canonical name
    0: 'Mainline', 1: 'Branch', 2: 'Industrial',
    # String -> canonical name (accept any casing variant that might appear)
    'Mainline':   'Mainline',
    'Branch':     'Branch',
    'Industrial': 'Industrial',
    # Legacy alias that was incorrectly used internally -- normalise on read
    'Industry':   'Industrial',
}

# JSON string written to disk -- identical to the canonical name (no remapping needed)
TRACK_CLASS_JSON = {
    'Mainline':   'Mainline',
    'Branch':     'Branch',
    'Industrial': 'Industrial',
}

# Default speed limits when speedLimit=0 (from GetExpectedSpeedLimit)
TRACK_CLASS_DEFAULT_SPEED = {
    'Mainline':   35,
    'Branch':     25,
    'Industrial': 15,
}

# Valid Style values -- matches TrackSegment.Style enum exactly
TRACK_STYLES = {'Standard', 'Bridge', 'Tunnel', 'Yard'}


# ---------------------------------------------------------------------------
# Geometry: 24-point Gauss-Legendre constants (F6)
# ---------------------------------------------------------------------------
_GAUSS_T = [
    -0.06405689286260563,  0.06405689286260563,
    -0.1911188674736163,   0.1911188674736163,
    -0.3150426796961634,   0.3150426796961634,
    -0.4337935076260451,   0.4337935076260451,
    -0.5454214713888396,   0.5454214713888396,
    -0.6480936519369755,   0.6480936519369755,
    -0.7401241915785544,   0.7401241915785544,
    -0.820001985973903,    0.820001985973903,
    -0.8864155270044011,   0.8864155270044011,
    -0.9382745520027328,   0.9382745520027328,
    -0.9747285559713095,   0.9747285559713095,
    -0.9951872199970213,   0.9951872199970213,
]
_GAUSS_C = [
    0.12793819534675216,   0.12793819534675216,
    0.1258374563468283,    0.1258374563468283,
    0.12167047292780339,   0.12167047292780339,
    0.1155056680537256,    0.1155056680537256,
    0.10744427011596563,   0.10744427011596563,
    0.09761865210411388,   0.09761865210411388,
    0.08619016153195327,   0.08619016153195327,
    0.0733464814110803,    0.0733464814110803,
    0.05929858491543678,   0.05929858491543678,
    0.04427743881741981,   0.04427743881741981,
    0.028531388628933663,  0.028531388628933663,
    0.0123412297999872,    0.0123412297999872,
]


# ---------------------------------------------------------------------------
# Track group and marker constants (F22/F23)
# ---------------------------------------------------------------------------
TRACK_GROUP_NOTES = """
Track groupId system (F22):
  - Segments with groupId='' are always visible (no gating)
  - Segments with groupId='some_feature_id' are invisible until that feature
    is unlocked via progression. The feature must call Graph.EnableGroup().
  - To add progression-gated track: set groupId on segments AND add a
    MapFeature that enables the group when unlocked.
  - Available = enabled (GroupEnabled=True) AND graph rebuild has run
  - groupId MUST match a feature ID -- orphaned groupIds keep track hidden forever
"""


# ---------------------------------------------------------------------------
# F23/F24: TrackMarker ID generation
# Confirmed from Core/IdGenerator.cs and Track/TrackMarkerType.cs
# ---------------------------------------------------------------------------

# F23: TrackMarkerType enum values (confirmed from TrackMarkerType.cs)
TRACK_MARKER_TYPES = {
    'Generic':       0,   # general purpose marker
    'Signal':        1,   # CTC signal location
    'Flare':         2,   # flare/whistle post
    'Crossing':      3,   # road crossing
    'PassengerStop': 4,   # passenger stop location
}


# ---------------------------------------------------------------------------
# Simple graph tag constants (C3)
# ---------------------------------------------------------------------------
SIMPLE_GRAPH_TAGS = {
    'Walkable':  'Walkable',   # standard traversable walkway node
    'Platform':  'Platform',   # station platform -- crew can board/alight here
    'NoWalk':    'NoWalk',     # impassable node (blocks pathfinding)
    'Crossing':  'Crossing',   # road/track crossing point
}
# Tag may be None (absent) -- the C# field is [NullableContext(2)] string Tag




# ---------------------------------------------------------------------------
# G-section: CTC / Signal constants
# ---------------------------------------------------------------------------
SIGNAL_ASPECTS = {
    'Stop':              0,  # red -- trains must stop
    'Approach':          1,  # yellow -- proceed, prepare to stop at next signal
    'Clear':             2,  # green -- full speed
    'DivergingApproach': 3,  # yellow over yellow -- diverging route, slow
    'DivergingClear':    4,  # green over yellow -- diverging route, proceed
    'Restricting':       5,  # flashing red -- proceed at restricted speed
}
# These are stored via KeyValue: CTCKeys.SignalAspect(signalId) = "signal:{id}:aspect"
# ---------------------------------------------------------------------------

# ---------------------------------------------------------------------------
# G3: SignalHeadConfiguration enum
# SOURCE: Assembly-CSharp/Track/Signals/SignalHeadConfiguration.cs
# Confirmed exact values:
SIGNAL_HEAD_CONFIGS = {
    'Single': 0,  # one lamp -- Stop/Clear only
    'Double': 1,  # two lamps -- Stop/Approach or Stop/Clear
    'Triple': 2,  # three lamps -- Stop/Approach/Clear
}
# Physical lamp count determines which aspects the signal can display.
# A Single-head signal can only show Stop or Clear.
# ---------------------------------------------------------------------------

# ---------------------------------------------------------------------------
# G4: SignalDirection enum
# SOURCE: Assembly-CSharp/Track/Signals/SignalDirection.cs
# NOTE: This is distinct from CTCDirection (below).
# SignalDirection is the physical facing of the signal mast.
SIGNAL_DIRECTIONS = {
    'None':  0,  # no directional restriction
    'Right': 1,  # signal governs right-bound traffic
    'Left':  2,  # signal governs left-bound traffic
}
# CTCDirection (from CTCDirection.cs) is used internally for block/traffic logic:
CTC_DIRECTIONS = {
    'Left':  0,
    'Right': 1,
}
# ---------------------------------------------------------------------------

# ---------------------------------------------------------------------------
# G5: SwitchFilter enum
# SOURCE: Assembly-CSharp/Track/Signals/SwitchFilter.cs
# Used in signal route definitions to require a specific switch position.
SWITCH_FILTERS = {
    'Normal':   0,   # switch in normal (default/straight) position
    'Reversed': 1,   # switch thrown (diverging position)
    'None':    -1,   # no switch position requirement
}
# ---------------------------------------------------------------------------

# ---------------------------------------------------------------------------
# G6: HeuristicCosts -- AI route search cost constants
# SOURCE: Assembly-CSharp/Track/Search/HeuristicCosts.cs
# Confirmed exact values from HeuristicCosts.AutoEngineer:
AI_HEURISTIC_COSTS = {
    'DivergingRoute':      20,    # cost to take a diverging route vs straight
    'ThrowSwitch':         10,    # cost to throw a switch
    'ThrowSwitchCTCLocked': 1000, # cost to throw a CTC-locked switch (effectively blocked)
    'CarBlockingRoute':    5000,  # cost when a car is blocking the route
}
# DESIGN IMPLICATION: AI strongly prefers straight routes over diverging (20 penalty).
# A CTC-locked switch (1000) is treated as nearly impassable by the AutoEngineer.
# When designing track layout, diverging routes on frequently-used AI paths
# add 20 to route cost -- consider this when placing crossovers and sidings.
# ---------------------------------------------------------------------------

# ---------------------------------------------------------------------------
# G7: CTCKeys -- KeyValue key format for signal state persistence
# SOURCE: Assembly-CSharp/Track/Signals/CTCKeys.cs
# All signal state is persisted via the KeyValue system (KeyValue.Runtime).
# These key formats are confirmed from CTCKeys.cs:
CTC_KEY_FORMATS = {
    'Knob':             'knob:{id}:position',       # CTCKeys.Knob(id)
    'BlockOccupancy':   'block:{id}:occupancy',     # CTCKeys.BlockOccupancy(id)
    'BlockTrafficFilter': 'block:{id}:direction',   # CTCKeys.BlockTrafficFilter(id)
    'SignalAspect':     'signal:{id}:aspect',        # CTCKeys.SignalAspect(id)
    'SwitchPosition':   'switch:{id}:position',     # CTCKeys.SwitchPosition(id)
    'Button':           'button:{id}:active',        # CTCKeys.Button(id)
    'InterlockingDirection': 'il:{id}:direction',   # CTCKeys.InterlockingDirection(id)
}
# IMPORTANT: These keys are stored on KeyValueObject components (F17/KeyValue.Runtime).
# This is exactly why GraphPatcher blocks cloning mandelas with KeyValueObject --
# duplicating a signal component would clone its state keys, corrupting the save.
# Custom signal mods must use these exact key formats for state persistence.


# ---------------------------------------------------------------------------
# AI routing cost constants (G6)
# ---------------------------------------------------------------------------
# G6: HeuristicCosts -- AI route search cost constants
# SOURCE: Assembly-CSharp/Track/Search/HeuristicCosts.cs
# Confirmed exact values from HeuristicCosts.AutoEngineer:
AI_HEURISTIC_COSTS = {
    'DivergingRoute':      20,    # cost to take a diverging route vs straight
    'ThrowSwitch':         10,    # cost to throw a switch
    'ThrowSwitchCTCLocked': 1000, # cost to throw a CTC-locked switch (effectively blocked)
    'CarBlockingRoute':    5000,  # cost when a car is blocking the route
}
# DESIGN IMPLICATION: AI strongly prefers straight routes over diverging (20 penalty).
# A CTC-locked switch (1000) is treated as nearly impassable by the AutoEngineer.
# When designing track layout, diverging routes on frequently-used AI paths
# add 20 to route cost -- consider this when placing crossovers and sidings.

