# Hrogers Grade Crossing Runtime 1.0.0

This small Unity Mod Manager runtime loads portable `grade-crossings.json`
files from installed Railroader map mods and registers native
`TrackMarkerType.Crossing` markers.

It is independent of the Tile Editor and AI Traffic. Railroader's normal Auto
Engineer crossing setting controls bell and horn behavior. Because the markers
are registered in the shared track graph, they work for player-owned equipment
running in Waypoint Auto Engineer mode as well as unowned AI equipment.

Example map file:

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

With no `segmentIds`, every segment connected to the node receives a marker so
both approaches detect the crossing. An unusual junction may provide a
`segmentIds` array to limit the protected approaches.

Visual crossing signals remain ordinary scenery assets saved in the map mod.
They do not require this runtime or the Tile Editor after placement, but the
asset pack supplying their model must be installed.
