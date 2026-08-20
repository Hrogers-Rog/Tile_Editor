# Track Editing

Building and editing track graphs on real terrain.

Every change auto-saves to `game-graph.json` and hot-reloads into a running game
in roughly 500 ms.

## Selecting

| Action | Result |
| --- | --- |
| Click a node or segment | Select — properties appear top-left |
| Click a segment | Works **anywhere along the line**, not just the midpoint |
| Click empty space | Deselect |
| Click an already-selected node | Second click starts drag mode |
| Release a drag | Commits the move and samples terrain height |
| `Esc` | Cancel drag, connect, or place |
| `Delete` | Delete the selected node, or all its segments |

The two-click drag is deliberate: one click selects, a second starts the move, so
you cannot nudge a node by accident while inspecting it.

## Node Properties

| Control | What it does |
| --- | --- |
| X / Y / Z | Direct position editing — click, type, Enter |
| RotY nudge | Fine grid: ±90 / 45 / 30 / 15 / 10 / 5 / 1 / 0.1 / 0.05 / 0.001° |
| Flatten | Zeros rotX and rotZ, levelling the node on terrain |
| Reverse | Flips heading 180° |
| Merge | Removes a middle node between exactly two segments |
| Split | Duplicates the node and rewires the selected segment to the copy |
| Copy XYZ | Copies position to the clipboard |
| Paste Y | Pastes **only the height** onto the node |

Paste Y is the tool for levelling a run: copy from a node at the right elevation,
then paste height onto each node along the tangent without disturbing plan
position.

The connected-segments list at the bottom of the node panel is clickable.

## Segment Properties

| Control | What it does |
| --- | --- |
| Track Class | Mainline / Branch / Industrial — coloured buttons |
| Style | Standard / Yard / Bridge / Tunnel |
| Speed | Nudge ±25 / 10 / 5 / 1 mph, or type a value |
| Priority | Editable box |
| GroupID | Track group membership |
| Reverse | Swaps `startId` and `endId` |
| Trestle | Wraps the segment in an AutoTrestleBuilder spliney |

A class button brightens when that class is active.

## Creating Track

| Action | Result |
| --- | --- |
| `Ctrl+click` map | Create a node at the cursor, sampling terrain Y |
| Geo → Add Node | Placement mode with a live-coordinate crosshair |
| Connect → button | Enter connect mode from the selected node |
| `Ctrl+click` a node | Finish the connection |
| Drag node → node | Snap ring; release to connect |
| Drag node → segment | **Cyan**; release to insert into that segment |
| `Shift+drag` → segment | **Yellow**; insert **and** add a turnout diverge leg |

New nodes default to Mainline, Standard, 45 mph, and their `rotY` is set
automatically to face the connection direction.

### Inserting Into A Segment

Dropping a node onto a segment deletes the original and creates two, giving the
new node a bezier heading so the alignment stays smooth. The `Shift` variant does
the same and adds a turnout diverge leg using the current **Geo → Turnout**
settings for angle and length.

For a full switch with proper geometry, use **Geo → Turnout**: select the node,
Preview, then Commit.

If the nodes and segments exist but part of a switch has no visible rail, the
branch is usually too tight for Railroader's turnout mesh builder. The Turnout
and Piece tools show an estimated radius and a red warning below 35 m. Increase
the curved lead length, reduce the divergence angle, or move the diverging node
farther from the switch. The warning is advisory because custom track renderers
can have different limits.

## Group Operations

| Action | Result |
| --- | --- |
| `Ctrl+drag` | Rubber-band select nodes |
| `Shift`+rubber-band | Add to the existing selection |
| Apply dX / dY / dZ / Rot | Bulk translate and rotate the selection |

## Spliney Control Points

Rivers, roads, trestles, and native FUSE object lines are splines with control
points — the colored dots. Zoom in to click one.

| Control | What it does |
| --- | --- |
| Click, click again | Select, then start dragging |
| Drag release | Moves the point; terrain can resample Y |
| Prev / Next | Step along the spline |
| Ins Before / Ins After | Insert a control point |
| Sample Y | Re-read terrain height |
| Auto Rot | Rebuild `rotY` from neighbouring points |
| Delete Pt | Remove a point (the spline keeps at least two) |
| Fit Terrain | Terrain-fit the entire road or river |
| Reverse Flow | **Rivers only** — reverses points and adds 180° `rotY` |

River preview arrows show flow from the first point to the last, so check them
after a Reverse Flow.

### Fence / Wall Object Lines

In a native FUSE project, open **F9 → Geo → Spliney → Fence / Wall**. Choose a
loaded scenery asset identifier (recommended) or an advanced safe scene prefab
path, then click points just like a road. FUSE places rigid copies at uniform
spacing along the polyline. Use:

- **Spacing** for the module interval;
- **Scale** and **Model rotation offset** to match the model's authored axes;
- **Side/height offset** to move every module away from the centerline;
- **Snap each module to terrain** for ground contact;
- **Follow vertical slope** when posts/panels should pitch with the grade;
- **Always place a final module** to close the endpoint gap; and
- **Safety limit** to prevent an accidental tiny spacing from spawning
  thousands of objects.

This is intended for rigid fence panels, posts, retaining-wall blocks,
guardrails, pipes, and similar repeating assets. It does not deform or stretch
the source mesh. The object line is native FUSE only; its button is visibly
disabled in legacy RailLoader projects because that schema has no equivalent.

## Gauge

Segments carry a `gauge` field consumed by FUSE Narrow Gauge:

`Standard` (default) · `Narrow` · `DualGauge` · `DualGauge_L` · `DualGauge_R` ·
`DualGauge_T`

New arcs, pieces, parallel tracks, turnouts, wyes, and direct connections inherit
the active gauge. Existing gauge and companion fields survive splits, merges, and
renames.

RailLoader ignores the `gauge` field, so one graph works for both runtimes. Visible
narrow and dual rail geometry is rendered by FUSE Narrow Gauge, not by the
Tile Editor — the field is metadata.

`DualGauge_T` must be **one short segment** between opposite explicit `L` and `R`
runs. See [Data Formats](SCHEMA_EXAMPLES.md#track-graph-and-gauge).

## Geometry Tools (Geo)

The Geo panel carries seven tabs — Spliney, Arc, Parallel, Fit Arc, Turnout, Wye,
Span, and Turntable, alongside grade work and piece-based assembly.

Guide geometry stays draft-only until you fit an arc or build a spliney, so you
can trace freely without committing anything.

## Calculators (Calc)

| Calculator | Input → output |
| --- | --- |
| Crossover | Separation + angle → leg lengths |
| Curved Turnout | Radius + gauge + angle → diverge geometry |
| Grade / Slope | Run + rise → percent, ratio, angle |
| Measure | Run, rise, grade, and heading between two picks |

## Related

- [Keybinds](KEYBINDS.md)
- [Mod Tools](MOD_TOOLS.md)
- [Data Formats](SCHEMA_EXAMPLES.md)
