# Data Formats And Worked Examples

Everything the Tile Editor authors is written as portable JSON inside a map mod
folder. This page shows each format with a working example and explains what the
game does with it.

The important design point: **the runtimes that consume these files do not
require the Tile Editor.** Players install the small runtime mod; you ship the
JSON inside your map mod.

| File | Consumed by | Purpose |
| --- | --- | --- |
| `game-graph.json` | RailLoader / FUSE | Track nodes and segments |
| `grade-crossings.json` | `Hrogers.CrossingRuntime` | Functional grade crossings |
| `train-signals.json` | Railroad Operations `Hrogers.SignalRuntime` | Semaphore signals and interlockings |
| `tile-editor-telegraph-poles.json` | Tile Editor bridge | Pole nodes and wire edges |
| `Map.json` | Tile Editor | Georeference for the tile set |
| `Definition.json` | RailLoader | Mod manifest (version 8) |

## Grade Crossings

`grade-crossings.json` registers native `TrackMarkerType.Crossing` markers.
Railroader's normal Auto Engineer crossing setting then controls bell and horn
behaviour. Because the markers live in the shared track graph, they work for
player-owned equipment in Waypoint mode as well as AI equipment.

### Minimal

```json
{
  "version": 1,
  "crossings": [
    {
      "id": "bryson-main-street",
      "enabled": true,
      "nodeId": "NBrysonCrossing"
    }
  ]
}
```

With no `segmentIds`, **every segment connected to the node** receives a marker,
so both approaches detect the crossing. That is what you want almost always.

### Limiting the protected approaches

At an unusual junction where only some approaches should sound, name them:

```json
{
  "version": 1,
  "crossings": [
    {
      "id": "yard-throat-crossing",
      "enabled": true,
      "nodeId": "NYardThroat",
      "segmentIds": ["SMainWest", "SMainEast"]
    }
  ]
}
```

Only `SMainWest` and `SMainEast` get markers; the yard leads off the same node
stay silent.

### In use

1. In the editor, select the track node where the road crosses.
2. Author the crossing so it writes into your map mod's `grade-crossings.json`.
3. Ship `Hrogers.CrossingRuntime` alongside the map, or tell players to install it.
4. In game, run a train toward the node with Auto Engineer crossing behaviour on —
   the bell and horn fire on approach.

Set `"enabled": false` to keep a crossing in the file but inactive, which is
better than deleting it while you test.

## Train Signals

`train-signals.json` places Railroader's own animated semaphore assemblies. It is
loaded during ordinary gameplay by Railroad Operations — players do not install
the Tile Editor.

### A single mast

```json
{
  "formatVersion": 1,
  "signals": [
    {
      "id": "sig-bryson-west",
      "enabled": true,
      "headCount": 2,
      "initialAspect": "clear",
      "position": { "x": 1240.5, "y": 312.0, "z": -880.25 },
      "rotation": { "x": 0.0, "y": 137.5, "z": 0.0 },
      "protectedNodeId": "NBrysonWest",
      "protectedSegmentId": "SMainWest",
      "direction": "forward"
    }
  ]
}
```

`headCount` accepts one, two, or three heads. `initialAspect` is one of `clear`,
`restricting`, or `stop`. `direction` is `forward` or `reverse` relative to the
protected segment.

### Locking a mast to the track

A mast can either be snapped to the rail once, or **locked** to it. A locked mast
keeps its side, height, and facing offsets and follows later Bezier curve, grade,
and elevation edits:

```json
{
  "id": "sig-grade-approach",
  "enabled": true,
  "headCount": 1,
  "initialAspect": "restricting",
  "trackAttachment": {
    "locked": true,
    "segmentId": "SMainWest",
    "parameter": 0.62,
    "localPosition": { "x": 3.4, "y": 0.0, "z": 0.0 },
    "localRotation": { "x": 0.0, "y": 0.0, "z": 0.0 }
  },
  "protectedSegmentId": "SMainWest",
  "direction": "forward"
}
```

`parameter` is the position along the segment from 0 to 1. Lock masts you expect
to survive regrading; use a one-time snap for scenery-only signals.

### Diamond interlocking

The Diamond Interlocking builder detects the real intersection of two
non-connecting Bezier segments, rejects a grade-separated overpass, and places
four independently adjustable masts — `A1`, `A2`, `B1`, `B2`:

```json
{
  "formatVersion": 1,
  "interlockings": [
    {
      "interlockingId": "dia-hollow-junction",
      "type": "diamond",
      "automatic": true,
      "crossingPoint": { "x": 980.0, "y": 244.5, "z": 1502.0 },
      "crossingAngleDegrees": 63.4,
      "releaseLength": 120.0,
      "releaseDelaySeconds": 8.0,
      "cancelDelaySeconds": 30.0,
      "approaches": [
        {
          "approachId": "A1",
          "signalId": "sig-dia-a1",
          "approachLength": 650.0,
          "protectedSegmentIds": ["SRouteA-01", "SRouteA-02"],
          "approachSegmentIds": ["SRouteA-03", "SRouteA-04"],
          "conflicts": ["B1", "B2"]
        },
        {
          "approachId": "B1",
          "signalId": "sig-dia-b1",
          "approachLength": 650.0,
          "protectedSegmentIds": ["SRouteB-01"],
          "approachSegmentIds": ["SRouteB-02", "SRouteB-03"],
          "conflicts": ["A1", "A2"]
        }
      ]
    }
  ]
}
```

Two things to note:

- **`protectedSegmentIds` and `approachSegmentIds` are different.** The first is
  the block from mast to diamond; the second is the approach-locking territory
  beyond the mast. Exposing them separately is what lets external interlocking
  logic reason about the plant.
- **Long setbacks follow connected track automatically.** A 500–800 m setback
  crosses segment boundaries without you listing every segment by hand — the
  arrays above record the result.

`conflicts` names the approaches this one locks out, which is what makes the
diamond mutually exclusive.

## Telegraph Poles

New poles and wire edges persist in the owning map mod's
`tile-editor-telegraph-poles.json`. Original base-game poles instead save
cumulative Alina `TelegraphPoleMover` offsets plus portable rotation overrides, so
both kinds coexist.

```json
{
  "version": 1,
  "poles": [
    { "id": "pole-001", "position": { "x": 1200.0, "y": 310.5, "z": -900.0 },
      "rotation": { "x": 0.0, "y": 42.0, "z": 0.0 } },
    { "id": "pole-002", "position": { "x": 1260.0, "y": 311.2, "z": -880.0 },
      "rotation": { "x": 0.0, "y": 42.0, "z": 0.0 } }
  ],
  "edges": [
    { "from": "pole-001", "to": "pole-002" }
  ]
}
```

Poles are nodes; `edges` are the wire runs between them. Continuous-line placement
creates the poles and the edges in one pass.

## Track Graph And Gauge

`game-graph.json` stays the portable baseline. FUSE imports it including Narrow
Gauge metadata, and RailLoader safely ignores the extra `gauge` field — which is
why one file serves both runtimes.

```json
{
  "segments": {
    "SMainWest": {
      "startId": "NBrysonWest",
      "endId": "NBrysonYard",
      "trackClass": "Mainline",
      "style": "Standard",
      "speedLimit": 45,
      "priority": 0,
      "groupId": "bryson-main",
      "gauge": "DualGauge_L"
    }
  }
}
```

Gauge values: `Standard` (or omitted), `Narrow`, `DualGauge`, `DualGauge_L`,
`DualGauge_R`, `DualGauge_T`. Visible narrow and dual rail geometry comes from
[FUSE Narrow Gauge](https://github.com/Hrogers-Rog/Narrow_Gauge), not
Railroader's standard track builder — the field here is metadata that module
reads.

`DualGauge_T` is authored as **one short segment** between opposite explicit `L`
and `R` runs. The in-game F9 workspace checks its two endpoints and refuses to
apply it through a whole chain, which is the mistake it exists to prevent.

## Native FUSE Packages

Native FUSE packages are editable directly. The desktop and F9 selectors read
`Info.json`'s `FuseDataFiles`, and `.fuse.json` track fragments keep
`startNodeId` / `endNodeId`, native removal lists, and unrelated FUSE operations
rather than being rewritten as legacy JSON.

Use RailLoader `Definition.json` (manifest version 8) for portable content, and
native FUSE fragments when the legacy schema cannot express what you need.

## Choosing A Format

| You want | Author as |
| --- | --- |
| Track that works on RailLoader and FUSE | `game-graph.json` |
| Narrow / dual gauge | `game-graph.json` with `gauge` (needs FUSE Narrow Gauge to render) |
| FUSE-only operations | native `.fuse.json` fragment |
| Crossings for any player | `grade-crossings.json` + `Hrogers.CrossingRuntime` |
| Signals for any player | `train-signals.json` + Railroad Operations |

## Related

- [Track Editing](TRACK_EDITING.md)
- [Mod Tools](MOD_TOOLS.md)
- [FUSE JSON Schema](https://github.com/F-U-S-E-E/FuseDevelopmentGroup/blob/main/schemas/FUSE_JSON_SCHEMA.md)
