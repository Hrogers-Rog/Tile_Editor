# Hrogers Train Signal Runtime 1.7.0

This small Unity Mod Manager mod loads functional base-game semaphore
assets from `train-signals.json` files stored in installed map mods. The Tile
Editor is only an authoring tool and is not required during normal gameplay.

The runtime deliberately removes the cloned base signal's original CTC signal
component and pickable. That prevents duplicate vanilla signal IDs and leaves
the base semaphore model, blade animation, materials, and culling intact.

Version 1.5 also loads portable `ctc-system.json` files. They describe
historical train-order, ABS, and CTC territories independently from the Tile
Editor UI:

- ABS blocks calculate Stop/Approach/Clear in both directions from live car
  occupancy and the configured next block.
- CTC routes verify protected blocks and switch clearance, reject conflicts,
  throw switches through Railroader's host-authoritative state message, lock
  the power switches, hold every conflicting signal at Stop, and release only
  after the routed movement clears.
- CTC commands and indications are synchronized through an authenticated
  dispatcher channel. Remote dispatcher clients can line/cancel routes and
  command switches; every client receives the host's active route, phase,
  explanation, and matching semaphore indication.
- Manual blocks remain at Stop pending operator or train-order authority.
- Form 19, Form 31, warrants, meets, holds, and extras run through a complete
  Draft, Issued, Delivered, Acknowledged, Fulfilled/Cancelled lifecycle.
- Press F8 for the standalone crew order window. Delivery and acknowledgement
  work without Tile Editor and synchronize through Railroader's saved,
  host-authoritative multiplayer property state.
- Acknowledgements are authenticated against the sender's actual Railroader
  player ID and assigned train-crew membership. Dispatcher actions require
  dispatcher access or the game host.
- Delivered orders hold their assigned train until acknowledged. Effective
  orders enforce authored block limits and optional speed limits for manually
  driven and Auto Engineer/Waypoint trains on the host.

Version 1.6 adds the live railroad desk to Railroader's normal **Company >
Operations** window. If Railroad Operations/AI Traffic is installed, its
existing **Traffic Control** and **Clerk's Office** pages are preserved and the same Operations tab gains
**Signals & CTC**, **Train Orders**, and **My Orders** pages. Without AI Traffic,
Signal Runtime creates the Operations tab itself. Dispatchers can line routes,
command power switches, watch block and diamond indications, and administer
orders there; crews can read and acknowledge their assigned orders. F8 remains
the compact crew-copy shortcut. Tile Editor F9 is only for placing and
configuring the territory.

Version 1.7 supports persistent signal-to-track attachments. A locked signal
stores its segment ID, Bezier parameter, and local position/rotation offsets in
`train-signals.json`. The runtime resolves that attachment against the live
graph, so the mast follows later curve, grade, and elevation changes while
retaining its authored side, height, and facing. Older free-position signal
records remain compatible.

Interlocking integrations can reference `Hrogers.SignalRuntime.dll` or use
reflection:

- `Main.Signals` lists live signal records and their binding metadata.
- `Main.TryGetSignal(id, out signal)` retrieves a live signal.
- `Main.TrySetAspect(id, "stop|approach|clear|diverging-approach|diverging-clear|restricting")`
  animates the base-game semaphore heads.
- Each signal exposes `InterlockingId`, `ApproachId`, `ProtectedNodeId`,
  `ProtectedSegmentId`, full `ProtectedSegmentIds` and
  `ApproachSegmentIds` chains, `Direction`, `HeadCount`, and `GameObject`.
- Generated four-signal diamonds operate automatically from Railroader's live
  car locations on their saved track-segment chains. Only one approach can
  clear; all conflicting signals remain at Stop. The route stays locked after
  a train enters, then releases after the train clears both crossing segments
  and the configured release delay expires.
- `Main.Interlockings` exposes portable diamond crossing points, the two
  conflicting railroad routes, their four signal IDs, approach nodes,
  approach/release locking lengths, live phase, active approach, requests,
  and occupied segment IDs.
- `Main.TryRequestInterlockingRoute(interlockingId, approachId)` requests a
  specific approach manually. `Main.TryReleaseInterlocking(interlockingId)`
  requests a fail-safe release; the runtime refuses it while the diamond is
  occupied. `Main.TrySetInterlockingAutomatic(interlockingId, enabled)` changes
  live automatic detection.
- `Main.CtcControlPoints`, `Main.CtcBlocks`, and `Main.TrainOrders` expose the
  complete portable operating model and current occupancy/route state.
- `Main.TrySetCtcSwitch`, `Main.TryLineCtcRoute`, and
  `Main.TryCancelCtcRoute` are the dispatcher-control API. Switch and route
  commands are accepted only by the Railroader host and fail safe when a car,
  occupied block, conflicting route, or lost switch correspondence prevents
  the command.
- `Main.TryIssueTrainOrder`, `Main.TryDeliverTrainOrder`,
  `Main.TryAcknowledgeTrainOrder`, `Main.TryFulfillTrainOrder`, and
  `Main.TryCancelTrainOrder` queue permission-checked multiplayer actions.
  `Main.TryGetTrainOrder` exposes live delivery, acknowledgement, assigned
  crew, authority-block, and audit state.

Approach occupancy is read from each car's authoritative Railroader track
location. The diamond detector also checks the axle's actual position against
the saved crossing point and release distance, so a long crossing segment does
not falsely occupy the interlocking while a train is still hundreds of meters
away.

Map authors distribute `train-signals.json` with their map mod and list
`Hrogers.SignalRuntime` as a dependency. Players do not need the Tile Editor.
