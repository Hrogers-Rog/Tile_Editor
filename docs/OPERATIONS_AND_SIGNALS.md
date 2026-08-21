# Operations And Signals

Press `F9`, open **Operations**, and choose the focused tool across the top. The
workspace discovers both native FUSE operations and supported legacy
RailLoader/Strange Customs graph content, while preserving each owning file.

## Operations Tool Map

| Tool | Creates or edits |
| --- | --- |
| Towns | Operating areas, name, position, radius, display order |
| Spans | Whole, partial, or connected multi-segment TrackSpans |
| Industries | Industries and freight/production/interchange components |
| Passenger | Passenger components, station codes, population, neighbours, station agents |
| Facilities | Steam/diesel/combined profiles and physical service objects |
| Markers | Autonomous operating markers and native track/territory binding |
| Signals | Base-game semaphore assemblies and diamond interlockings |
| All | Search and inspect everything together |

Every creation flow writes to the active owning layer. World placement uses the
pointer; `Esc` cancels a pending placement. Search results can be selected and
shown in the world before editing or deletion.

## Create An Industry

1. In **Towns**, select an existing town or create one at the pointer.
2. In **Spans**, select a track segment and create a whole/partial span. For a
   connected route, mark start and end segments and build a multi-segment span.
3. In **Industries**, enter a stable ID/name and place the industry anchor.
4. Add rail behavior: loader, unloader, formulaic production, team track,
   repair, interchange, progression, passenger, or a custom component.
5. Select the TrackSpan and load, configure capacity/rates, then save.
6. Refresh Operations and check `/fuse.operations` in a new sandbox session.

The relationship is important: a component operates on a TrackSpan; the
industry's world position is primarily its label/anchor.

## Remove Or Disable An Industry

Delete removes an industry owned by an editable mod layer. For a base-game
industry, use the exact runtime industry ID and a native FUSE
`operations.removals.industries` entry. The native Industries page now has an
**Add Industry Removal** form for this. The editor must not guess from a display
name because several company-window rows can have similar text.

Removing an industry does not automatically remove its track. Delete/suppress
track, scenery, progression references, and spans separately only when the mod
actually owns those objects.

## Place A Loader

There are two unrelated things commonly called a loader:

- A rail industry `loader` component changes freight on cars over a TrackSpan.
- A physical service object is the visible coal chute, water column, diesel
  stand, bunker-C pipe, or station agent placed in the world.

Use **Facilities** for the visible object. The Steam, Diesel, and Combined
profiles create the common operation pieces together; individual Water Tower,
Coal Conveyor, and custom-prefab controls place only the selected service object.

Native `operations.loaders` is for cloning a vanilla-style loader prefab that
already has working service components. For those, choose **Water Tower** or
**Coal Conveyor**, set the object/industry IDs, choose track snapping and its
offsets, then place with the pointer.

For a custom diesel, bunker-C, coal, water, or wood asset, use **Custom Toolshed
Service Asset** instead:

1. Set a unique scenery object ID and Toolshed facility ID.
2. Enter the asset identifier, or click **Find Installed Toolshed Assets** and
   choose an asset whose definition contains `ToolshedServiceLoadPoint`.
3. Set the service load, optional authored load-point ID, source industry, and
   the TrackSpan IDs used to refill finite storage.
4. Select/approach the service track, configure side/along/vertical/heading
   offsets, and place with the pointer.
5. Save. The editor writes `world.scenery` to the native FUSE file and the
   matching binding to `ToolshedServiceFacilities.json`, replacing each file
   atomically. Both documents
   participate in the same undo/redo edit and receive timestamped backups.

The FUSE scenery ID is written as `targetObjectName`; the tank and its working
outlet therefore remain one placed object instead of splitting into two visuals.
Delivery spans refill storage and do not position the physical chute/hose.
FUSE owns placement and the route industry/span; Toolshed owns service point,
transfer, storage, animation, and interaction behavior. This workflow is
disabled in RailLoader output mode because that schema cannot express native
`world.scenery` plus the portable Toolshed binding safely.

## Turntables And Custom Models

Native FUSE turntables create their own rotating bridge track. A custom
turntable model supplies pit/bridge visuals; do not lay a duplicate bridge
segment through it. Optional roundhouse stalls generate approach/stall track,
while other approach tracks remain ordinary authored track. Legacy mode writes
an Alina TurntableBuilder entry and cannot represent all native visual and gauge
settings, so use native FUSE for new custom turntables.

## Passenger Service

Create/select the town and TrackSpan first, then add a passenger component. Set
the station code, population, branch, and neighbours. Use reciprocal neighbour
links unless the route intentionally models one-way service. The passenger form
also accepts the travel time to the next stop, an optional required map feature,
and intermediate timing points written one per line as
`intermediate-id|timetable-code|minutes`.

Native FUSE projects store those route details under `branchDefinitions` with
native `intermediates`; RailLoader projects use the reduced legacy `branches`
and `Intermediates` representation. The validator rejects duplicate station IDs
or timetable codes, negative travel times, and malformed intermediate rows. An
optional station agent is a separate placed object tied to the station/industry
ID and is available only in native FUSE mode.

## Place A Semaphore

1. Choose **Signals** and **Place a Signal**.
2. Click near the protected track. Enable snap-on-place if desired.
3. Choose one, two, or three heads and the governing direction.
4. Use **Snap Once** for a fixed transform or **Snap + Lock** to keep the mast
   following later Bezier/grade/elevation edits.
5. Set the protected node/segment and recalculate the route.
6. Test the starting aspect, save, and refresh the standalone signal runtime.

Signal edits auto-save to `train-signals.json` and have their own undo/redo
history. Players need the Railroad Operations signal runtime, not the Tile
Editor. On the first Signals or CTC save, the editor adds `AITraffic` to the
package manifest's requirements and load order so a map cannot silently ship
working editor previews without its gameplay runtime.

The package validator reads `train-signals.json` and `ctc-system.json`
together. It rejects duplicate IDs, unsupported aspects/head counts, missing
nodes or segments, broken signal/block/switch/route references, malformed
track locks, and missing runtime requirements. It also warns when a valid block
or control point was not assigned to a territory.

## Build A Diamond Interlocking

Select two non-connecting track segments that physically cross, then use the
Diamond Interlocking builder. It finds the actual 3D Bezier intersection and
rejects grade-separated overpasses. Configure setback, side/height offsets, head
count, approach locking, and release length, then build A1/A2/B1/B2.

Long setbacks can follow connected track beyond the initially selected segment.
Each of the four resulting masts remains independently movable and can be track
locked.

## Refresh And Stale Previews

Operations overlays refresh on explicit mode entry/refresh, not on a repeating
timer. Track topology edits rebuild affected track objects so old previews do not
remain stacked over the selected geometry. If the world still differs from the
file:

1. Cancel pointer placement with `Esc`.
2. Click **Refresh** in the relevant tool.
3. Force one track reload.
4. Re-select the object by stable ID before continuing.

Do not keep editing an ambiguous stacked preview; report the package, object IDs,
active layer, and before/after JSON if a forced reload does not clear it.

## Release Checklist

- Reopen the saved package in the desktop editor.
- Confirm every industry component references an existing TrackSpan/load.
- Confirm passenger codes are unique and neighbour IDs exist.
- Confirm physical service objects have the correct runtime dependency.
- Run **Validate** and resolve every Signals/CTC cross-reference error; do not
  release a package whose control-point routes still contain blank signal or
  block IDs.
- Open a new sandbox session and test company menus, car loading/unloading,
  signal aspects, and interlocking release.
- Verify the player package does not include or require the Tile Editor.
