using System;
using System.Globalization;
using UnityEngine;

namespace Hrogers.TileEditorBridge
{
    internal sealed partial class TileEditorBridgePanel
    {
        private string _trainSignalSearch = string.Empty;
        private int _trainSignalPage;
        private string _trainSignalSelectionKey = string.Empty;
        private string _trainSignalDeleteConfirm = string.Empty;
        private string _trainSignalId = "signal:new";
        private int _trainSignalHeadCount = 1;
        private string _trainSignalAspect = "stop";
        private string _trainSignalInterlockingId = string.Empty;
        private string _trainSignalProtectedNodeId = string.Empty;
        private string _trainSignalProtectedSegmentId = string.Empty;
        private string _trainSignalDirection = "forward";
        private bool _trainSignalEnabled = true;
        private bool _trainSignalRepeat;
        private bool _trainSignalSnapOnPlace = true;
        private bool _trainSignalLockOnPlace = true;
        private bool _trainSignalSnapRight = true;
        private string _trainSignalSnapSideOffset = "2.8";
        private string _trainSignalSnapVerticalOffset = "-0.2";
        private float _trainSignalMoveStep = 1f;
        private float _trainSignalRotateStep = 5f;
        private bool _trainSignalLocalAxes;
        private bool _showAdvancedTrainSignalTransform;
        private string _trainSignalPositionX = "0";
        private string _trainSignalPositionY = "0";
        private string _trainSignalPositionZ = "0";
        private string _trainSignalRotationX = "0";
        private string _trainSignalRotationY = "0";
        private string _trainSignalRotationZ = "0";
        private bool _showDiamondInterlockingBuilder;
        private string _diamondInterlockingId = "interlocking:diamond-new";
        private string _diamondSegmentA = string.Empty;
        private string _diamondSegmentB = string.Empty;
        private string _diamondSignalSetback = "600";
        private string _diamondSignalSideOffset = "2.8";
        private string _diamondSignalVerticalOffset = "-0.2";
        private string _diamondApproachLength = "120";
        private string _diamondReleaseLength = "60";
        private int _diamondSignalHeads = 1;

        private void DrawTrainSignalPanel()
        {
            if (_mapEditor == null || !_mapEditor.Available)
            {
                GUILayout.Label(
                    "Railroader's live map is not ready.",
                    _titleStyle);
                return;
            }
            if (!_mapEditor.GraphOpen)
                return;

            GUILayout.BeginHorizontal();
            GUILayout.Label(
                "BASE-GAME SEMAPHORE SIGNALS",
                _titleStyle);
            GUILayout.FlexibleSpace();
            GUILayout.Label(
                _mapEditor.SignalRuntimeAvailable
                    ? "RUNTIME READY"
                    : "RUNTIME NOT INSTALLED",
                _mapEditor.SignalRuntimeAvailable
                    ? _onlineStyle
                    : _offlineStyle);
            GUILayout.EndHorizontal();
            GUILayout.Label(
                "The editor places Railroader's animated semaphore asset. "
                + "The separate Signal Runtime loads it in normal gameplay; "
                + "players do not need Tile Editor installed.",
                _mutedStyle);
            DrawPointerPlacementStatus();

            GUILayout.Space(5f);
            if (GUILayout.Button(
                    _showDiamondInterlockingBuilder
                        ? "HIDE DIAMOND INTERLOCKING BUILDER"
                        : "BUILD 4-SIGNAL DIAMOND INTERLOCKING...",
                    GUILayout.Height(32f)))
            {
                _showDiamondInterlockingBuilder =
                    !_showDiamondInterlockingBuilder;
            }
            if (_showDiamondInterlockingBuilder)
                DrawDiamondInterlockingBuilder();

            GUILayout.Space(5f);
            GUILayout.Label("PLACE A SIGNAL", _titleStyle);
            DrawTextField("Signal ID / prefix", ref _trainSignalId);
            DrawTrainSignalHeadButtons();
            DrawTrainSignalDirectionButtons();
            DrawTrainSignalAspectButtons(false);
            GUILayout.BeginHorizontal();
            _trainSignalSnapOnPlace = GUILayout.Toggle(
                _trainSignalSnapOnPlace,
                "Snap to track",
                GUILayout.Height(27f));
            GUI.enabled = _trainSignalSnapOnPlace;
            _trainSignalLockOnPlace = GUILayout.Toggle(
                _trainSignalLockOnPlace,
                "Keep locked",
                GUILayout.Height(27f));
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            if (_trainSignalSnapOnPlace)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Side", GUILayout.Width(70f));
                var oldColor = GUI.backgroundColor;
                if (!_trainSignalSnapRight)
                    GUI.backgroundColor = new Color(0.18f, 0.72f, 0.82f);
                if (GUILayout.Button("LEFT"))
                    _trainSignalSnapRight = false;
                GUI.backgroundColor = oldColor;
                if (_trainSignalSnapRight)
                    GUI.backgroundColor = new Color(0.18f, 0.72f, 0.82f);
                if (GUILayout.Button("RIGHT"))
                    _trainSignalSnapRight = true;
                GUI.backgroundColor = oldColor;
                GUILayout.EndHorizontal();
                DrawTextField(
                    "Side offset from rail (m)",
                    ref _trainSignalSnapSideOffset);
                DrawTextField(
                    "Vertical offset (m)",
                    ref _trainSignalSnapVerticalOffset);
            }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(
                    "PLACE SEMAPHORE WITH POINTER",
                    GUILayout.Height(34f)))
            {
                CaptureDraftTrackBinding();
                ArmPointerPlacement(
                    PointerPlacementKind.TrainSignal,
                    string.Empty,
                    _trainSignalRepeat);
            }
            var repeat = GUILayout.Toggle(
                _trainSignalRepeat,
                "Repeat",
                GUILayout.Width(78f),
                GUILayout.Height(34f));
            _trainSignalRepeat = repeat;
            GUILayout.EndHorizontal();
            GUILayout.Label(
                _trainSignalSnapOnPlace
                    ? "Aim within 35 m of a track, or click a yellow segment "
                      + "first. Snap follows the Bezier centerline and Keep "
                      + "locked makes it follow later track edits."
                    : "Free placement uses the exact pointer position and "
                      + "camera heading; Flip 180 reverses it.",
                _mutedStyle);

            GUILayout.Space(7f);
            DrawTrainSignalList();
            var signal = _mapEditor.SelectedTrainSignal;
            if (signal == null)
                return;
            SyncTrainSignalForm(signal);
            DrawSelectedTrainSignal(signal);
        }

        private void DrawDiamondInterlockingBuilder()
        {
            GUILayout.Space(4f);
            GUILayout.Label(
                "RAILROAD DIAMOND INTERLOCKING",
                _titleStyle);
            GUILayout.Label(
                "Choose the two non-connecting segments that physically "
                + "cross. Build finds their exact plan-view intersection and "
                + "creates four separate semaphore records. Every generated "
                + "signal remains individually clickable and adjustable below. "
                + "Long setbacks automatically follow connected track across "
                + "multiple segments.",
                _mutedStyle);
            DrawTextField(
                "Interlocking ID",
                ref _diamondInterlockingId);

            GUILayout.BeginHorizontal();
            GUILayout.Label(
                "Railroad A: "
                + (string.IsNullOrWhiteSpace(_diamondSegmentA)
                    ? "(not marked)"
                    : _diamondSegmentA),
                _lineStyle);
            GUI.enabled = _mapEditor.SelectedSegment != null;
            if (GUILayout.Button(
                    "MARK SELECTED AS A",
                    GUILayout.Width(160f),
                    GUILayout.Height(27f)))
            {
                _diamondSegmentA = _mapEditor.SelectedSegment.Id;
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label(
                "Railroad B: "
                + (string.IsNullOrWhiteSpace(_diamondSegmentB)
                    ? "(not marked)"
                    : _diamondSegmentB),
                _lineStyle);
            GUI.enabled = _mapEditor.SelectedSegment != null;
            if (GUILayout.Button(
                    "MARK SELECTED AS B",
                    GUILayout.Width(160f),
                    GUILayout.Height(27f)))
            {
                _diamondSegmentB = _mapEditor.SelectedSegment.Id;
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Swap A / B"))
            {
                var swap = _diamondSegmentA;
                _diamondSegmentA = _diamondSegmentB;
                _diamondSegmentB = swap;
            }
            if (GUILayout.Button("Clear Segments"))
            {
                _diamondSegmentA = string.Empty;
                _diamondSegmentB = string.Empty;
            }
            GUILayout.EndHorizontal();

            DrawTextField(
                "Signal setback from diamond (m)",
                ref _diamondSignalSetback);
            DrawTextField(
                "Signal side offset from track (m)",
                ref _diamondSignalSideOffset);
            DrawTextField(
                "Signal vertical offset (m)",
                ref _diamondSignalVerticalOffset);
            DrawTextField(
                "Approach locking length (m)",
                ref _diamondApproachLength);
            DrawTextField(
                "Release block length (m)",
                ref _diamondReleaseLength);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Signal heads", GUILayout.Width(130f));
            TrainSignalChoiceButton(
                "1 Main",
                1,
                ref _diamondSignalHeads);
            TrainSignalChoiceButton(
                "2 Route",
                2,
                ref _diamondSignalHeads);
            TrainSignalChoiceButton(
                "3 Route",
                3,
                ref _diamondSignalHeads);
            GUILayout.EndHorizontal();

            var haveSegments = !string.IsNullOrWhiteSpace(_diamondSegmentA)
                               && !string.IsNullOrWhiteSpace(_diamondSegmentB);
            GUILayout.BeginHorizontal();
            GUI.enabled = haveSegments;
            if (GUILayout.Button(
                    "CHECK CROSSING",
                    GUILayout.Height(29f)))
            {
                RunGameAction(() =>
                    _mapEditor.DescribeDiamondInterlockingSegments(
                        _diamondSegmentA,
                        _diamondSegmentB));
            }
            if (GUILayout.Button(
                    "BUILD 4 INDEPENDENT SIGNALS",
                    GUILayout.Height(34f)))
            {
                RunGameAction(() =>
                    _mapEditor.BuildDiamondInterlocking(
                        _diamondInterlockingId,
                        _diamondSegmentA,
                        _diamondSegmentB,
                        ParseFloat(
                            _diamondSignalSetback,
                            "diamond signal setback"),
                        ParseFloat(
                            _diamondSignalSideOffset,
                            "diamond side offset"),
                        ParseFloat(
                            _diamondSignalVerticalOffset,
                            "diamond vertical offset"),
                        ParseFloat(
                            _diamondApproachLength,
                            "diamond approach length"),
                        ParseFloat(
                            _diamondReleaseLength,
                            "diamond release length"),
                        _diamondSignalHeads));
                _trainSignalSelectionKey = string.Empty;
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.Label(
                _mapEditor.DiamondInterlockingCount
                + " diamond interlocking definition(s) in this map. "
                + "Initial aspects are Stop; generated IDs end in A1, A2, "
                + "B1, and B2.",
                _mutedStyle);
        }

        private void DrawTrainSignalList()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(
                "PLACED (" + _mapEditor.TrainSignalCount + ")",
                _titleStyle,
                GUILayout.Width(105f));
            var search = GUILayout.TextField(
                _trainSignalSearch ?? string.Empty);
            if (!string.Equals(
                    search,
                    _trainSignalSearch,
                    StringComparison.Ordinal))
            {
                _trainSignalSearch = search;
                _trainSignalPage = 0;
            }
            GUILayout.EndHorizontal();

            const int pageSize = 8;
            var offset = _trainSignalPage * pageSize;
            var items = _mapEditor.SearchTrainSignals(
                _trainSignalSearch,
                offset,
                pageSize,
                out var total);
            foreach (var item in items)
            {
                var oldColor = GUI.backgroundColor;
                if (_mapEditor.IsSelectedTrainSignal(item.Id))
                    GUI.backgroundColor = new Color(0.85f, 0.28f, 0.78f);
                var suffix = string.IsNullOrWhiteSpace(item.InterlockingId)
                    ? ""
                    : "  ->  " + item.InterlockingId;
                if (GUILayout.Button(
                        Shorten(item.Id + suffix, 68),
                        GUILayout.Height(25f)))
                {
                    _mapEditor.SelectTrainSignal(item.Id);
                    _trainSignalSelectionKey = string.Empty;
                }
                GUI.backgroundColor = oldColor;
            }
            if (total > pageSize)
            {
                GUILayout.BeginHorizontal();
                GUI.enabled = _trainSignalPage > 0;
                if (GUILayout.Button("Previous"))
                    _trainSignalPage--;
                GUI.enabled = true;
                GUILayout.Label(
                    (_trainSignalPage + 1) + " / "
                    + Mathf.CeilToInt(total / (float)pageSize),
                    GUILayout.Width(62f));
                GUI.enabled = offset + pageSize < total;
                if (GUILayout.Button("Next"))
                    _trainSignalPage++;
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
        }

        private void DrawSelectedTrainSignal(
            TileEditorGraphSession.TrainSignalInfo signal)
        {
            GUILayout.Space(7f);
            GUILayout.BeginHorizontal();
            GUILayout.Label("SELECTED " + signal.Id, _titleStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Show", GUILayout.Width(78f)))
                RunGameAction("Centered selected signal",
                    _mapEditor.ShowSelectedTrainSignal);
            GUILayout.EndHorizontal();

            DrawTextField("Signal ID", ref _trainSignalId);
            DrawTrainSignalHeadButtons();
            DrawTrainSignalDirectionButtons();
            DrawTextField(
                "Interlocking ID",
                ref _trainSignalInterlockingId);
            DrawTextField(
                "Protected node",
                ref _trainSignalProtectedNodeId);
            DrawTextField(
                "Protected segment",
                ref _trainSignalProtectedSegmentId);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(
                    "BIND + LOCK CLICKED TRACK",
                    GUILayout.Height(29f)))
            {
                RunGameAction(() =>
                {
                    var result = _mapEditor.UseSelectedTrackForTrainSignal();
                    _trainSignalSelectionKey = string.Empty;
                    return result;
                });
            }
            _trainSignalEnabled = GUILayout.Toggle(
                _trainSignalEnabled,
                "Enabled",
                GUILayout.Width(90f),
                GUILayout.Height(29f));
            GUILayout.EndHorizontal();
            GUILayout.Label(
                signal.TrackLocked
                    ? "TRACK LOCKED: " + signal.TrackSegmentId
                      + " at " + signal.TrackParameter.ToString(
                          "0.000",
                          CultureInfo.InvariantCulture)
                    : "TRACK UNLOCKED: the mast keeps an independent world "
                      + "transform.",
                _mutedStyle);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("SNAP ONCE", GUILayout.Height(29f)))
            {
                SnapSelectedTrainSignal(false);
            }
            if (GUILayout.Button("SNAP + LOCK", GUILayout.Height(29f)))
            {
                SnapSelectedTrainSignal(true);
            }
            GUI.enabled = signal.TrackLocked;
            if (GUILayout.Button("UNLOCK", GUILayout.Height(29f)))
            {
                RunGameAction(() => _mapEditor
                    .SetSelectedTrainSignalTrackLocked(false));
                _trainSignalSelectionKey = string.Empty;
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.Label(
                "Snap aligns the mast to the clicked or nearest rail using "
                + "the placement side and offsets above. Lock stores a "
                + "segment-relative attachment in train-signals.json.",
                _mutedStyle);
            if (!string.IsNullOrWhiteSpace(signal.ApproachId))
            {
                GUILayout.Label(
                    "Diamond approach: " + signal.ApproachId
                    + " / protected block: "
                    + signal.ProtectedSegmentIds.Count
                    + " segment(s) to the diamond / traced approach: "
                    + signal.ApproachSegmentIds.Count
                    + " segment(s). This signal remains an independent object.",
                    _mutedStyle);
                if (GUILayout.Button(
                        "RECALCULATE BLOCK FROM MOVED MAST",
                        GUILayout.Height(31f)))
                {
                    RunGameAction(() =>
                    {
                        var result = _mapEditor
                            .RecalculateSelectedTrainSignalRoute();
                        _trainSignalSelectionKey = string.Empty;
                        return result;
                    });
                }
                GUILayout.Label(
                    "Use after moving a diamond signal along its saved "
                    + "approach. The mast transform stays exactly where you "
                    + "put it; the protected segment chain is retraced to "
                    + "the closest route segment.",
                    _mutedStyle);

                GUILayout.Space(5f);
                GUILayout.Label("LIVE INTERLOCK CONTROL", _titleStyle);
                GUILayout.Label(
                    _mapEditor.SelectedTrainSignalInterlockingStatus(),
                    _lineStyle);
                var automatic = _mapEditor
                    .SelectedTrainSignalInterlockingAutomatic;
                var requestedAutomatic = GUILayout.Toggle(
                    automatic,
                    "Automatic approach detection and route release",
                    GUILayout.Height(28f));
                if (requestedAutomatic != automatic)
                {
                    RunGameAction(
                        requestedAutomatic
                            ? "Enabled automatic diamond interlocking"
                            : "Disabled automatic diamond interlocking",
                        () => _mapEditor
                            .SetSelectedTrainSignalInterlockingAutomatic(
                                requestedAutomatic));
                }
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(
                        "REQUEST " + signal.ApproachId,
                        GUILayout.Height(31f)))
                {
                    RunGameAction(() => _mapEditor
                        .RequestSelectedTrainSignalInterlockingRoute());
                }
                if (GUILayout.Button(
                        "RELEASE INTERLOCK",
                        GUILayout.Height(31f)))
                {
                    RunGameAction(() => _mapEditor
                        .ReleaseSelectedTrainSignalInterlocking());
                }
                GUILayout.EndHorizontal();
                GUILayout.Label(
                    "Only one of the four approaches can clear. A train on "
                    + "the outer approach requests its route automatically; "
                    + "the other three signals stay at Stop until the train "
                    + "clears the diamond. Release is refused while the "
                    + "crossing is occupied.",
                    _mutedStyle);
            }

            if (GUILayout.Button(
                    "APPLY ID / HEADS / BINDING",
                    GUILayout.Height(32f)))
            {
                ApplyTrainSignalForm();
            }

            GUILayout.Space(6f);
            DrawTrainSignalTransformControls();
            GUILayout.Space(5f);
            GUILayout.Label("TEST / STARTING ASPECT", _titleStyle);
            DrawTrainSignalAspectButtons(true);

            if (_trainSignalDeleteConfirm == signal.Id)
            {
                var oldColor = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.85f, 0.28f, 0.20f);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(
                        "CONFIRM DELETE " + signal.Id,
                        GUILayout.Height(30f)))
                {
                    RunGameAction("Deleted train signal",
                        _mapEditor.DeleteSelectedTrainSignal);
                    _trainSignalDeleteConfirm = string.Empty;
                    _trainSignalSelectionKey = string.Empty;
                }
                GUI.backgroundColor = oldColor;
                if (GUILayout.Button("Cancel", GUILayout.Width(82f)))
                    _trainSignalDeleteConfirm = string.Empty;
                GUILayout.EndHorizontal();
            }
            else if (GUILayout.Button(
                         "Delete Train Signal...",
                         GUILayout.Height(29f)))
            {
                _trainSignalDeleteConfirm = signal.Id;
            }
            GUILayout.Label(
                "Signal changes are written immediately to train-signals.json "
                + "beside the selected graph.",
                _mutedStyle);
        }

        private void DrawTrainSignalTransformControls()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("MOVE", _titleStyle, GUILayout.Width(52f));
            TrainSignalStepButton("0.1", 0.1f, ref _trainSignalMoveStep);
            TrainSignalStepButton("1", 1f, ref _trainSignalMoveStep);
            TrainSignalStepButton("5", 5f, ref _trainSignalMoveStep);
            TrainSignalStepButton("10", 10f, ref _trainSignalMoveStep);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(
                    _trainSignalLocalAxes ? "LOCAL" : "WORLD",
                    GUILayout.Width(82f)))
            {
                _trainSignalLocalAxes = !_trainSignalLocalAxes;
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            TrainSignalMoveButton("\u2191  FORWARD +Z", Vector3.forward);
            TrainSignalMoveButton("\u2193  BACK -Z", Vector3.back);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            TrainSignalMoveButton("\u2190  LEFT -X", Vector3.left);
            TrainSignalMoveButton("\u2192  RIGHT +X", Vector3.right);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            TrainSignalMoveButton("\u25bc  LOWER -Y", Vector3.down);
            TrainSignalMoveButton("\u25b2  RAISE +Y", Vector3.up);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("ROTATE", _titleStyle, GUILayout.Width(66f));
            TrainSignalStepButton("1", 1f, ref _trainSignalRotateStep);
            TrainSignalStepButton("5", 5f, ref _trainSignalRotateStep);
            TrainSignalStepButton("15", 15f, ref _trainSignalRotateStep);
            TrainSignalStepButton("45", 45f, ref _trainSignalRotateStep);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("\u21ba  YAW LEFT", GUILayout.Height(30f)))
                RotateTrainSignal(0f, -_trainSignalRotateStep, 0f);
            if (GUILayout.Button("YAW RIGHT  \u21bb", GUILayout.Height(30f)))
                RotateTrainSignal(0f, _trainSignalRotateStep, 0f);
            if (GUILayout.Button("FLIP 180", GUILayout.Height(30f)))
                RunGameAction("Flipped signal 180 degrees",
                    _mapEditor.FlipSelectedTrainSignal);
            GUILayout.EndHorizontal();

            if (GUILayout.Button(
                    _showAdvancedTrainSignalTransform
                        ? "HIDE EXACT TRANSFORM"
                        : "MORE / EXACT TRANSFORM...",
                    GUILayout.Height(27f)))
            {
                _showAdvancedTrainSignalTransform =
                    !_showAdvancedTrainSignalTransform;
            }
            if (!_showAdvancedTrainSignalTransform)
                return;
            DrawTextField("Position X", ref _trainSignalPositionX);
            DrawTextField("Position Y", ref _trainSignalPositionY);
            DrawTextField("Position Z", ref _trainSignalPositionZ);
            DrawTextField("Pitch X", ref _trainSignalRotationX);
            DrawTextField("Heading Y", ref _trainSignalRotationY);
            DrawTextField("Roll Z", ref _trainSignalRotationZ);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("PITCH -"))
                RotateTrainSignal(-_trainSignalRotateStep, 0f, 0f);
            if (GUILayout.Button("PITCH +"))
                RotateTrainSignal(_trainSignalRotateStep, 0f, 0f);
            if (GUILayout.Button("ROLL -"))
                RotateTrainSignal(0f, 0f, -_trainSignalRotateStep);
            if (GUILayout.Button("ROLL +"))
                RotateTrainSignal(0f, 0f, _trainSignalRotateStep);
            GUILayout.EndHorizontal();
            if (GUILayout.Button("APPLY EXACT TRANSFORM", GUILayout.Height(30f)))
            {
                RunGameAction(
                    "Applied exact train signal transform",
                    () => _mapEditor.SetSelectedTrainSignalTransform(
                        new Vector3(
                            ParseFloat(_trainSignalPositionX, "signal X"),
                            ParseFloat(_trainSignalPositionY, "signal Y"),
                            ParseFloat(_trainSignalPositionZ, "signal Z")),
                        new Vector3(
                            ParseFloat(_trainSignalRotationX, "signal pitch"),
                            ParseFloat(_trainSignalRotationY, "signal heading"),
                            ParseFloat(_trainSignalRotationZ, "signal roll"))));
                _trainSignalSelectionKey = string.Empty;
            }
        }

        private void DrawTrainSignalHeadButtons()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Heads", GUILayout.Width(70f));
            TrainSignalChoiceButton("1 Main", 1, ref _trainSignalHeadCount);
            TrainSignalChoiceButton("2 Route", 2, ref _trainSignalHeadCount);
            TrainSignalChoiceButton("3 Route", 3, ref _trainSignalHeadCount);
            GUILayout.EndHorizontal();
        }

        private void DrawTrainSignalDirectionButtons()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Protects", GUILayout.Width(70f));
            TrainSignalTextChoiceButton(
                "Forward",
                "forward",
                ref _trainSignalDirection);
            TrainSignalTextChoiceButton(
                "Reverse",
                "reverse",
                ref _trainSignalDirection);
            GUILayout.EndHorizontal();
        }

        private void DrawTrainSignalAspectButtons(bool applyImmediately)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Aspect", GUILayout.Width(70f));
            TrainSignalAspectButton("STOP", "stop", applyImmediately);
            TrainSignalAspectButton(
                "APPROACH",
                "approach",
                applyImmediately);
            TrainSignalAspectButton("CLEAR", "clear", applyImmediately);
            GUILayout.EndHorizontal();
            if (_trainSignalHeadCount > 1)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Space(74f);
                TrainSignalAspectButton(
                    "DIV APPROACH",
                    "diverging-approach",
                    applyImmediately);
                TrainSignalAspectButton(
                    "DIV CLEAR",
                    "diverging-clear",
                    applyImmediately);
                TrainSignalAspectButton(
                    "RESTRICTING",
                    "restricting",
                    applyImmediately);
                GUILayout.EndHorizontal();
            }
        }

        private void TrainSignalAspectButton(
            string label,
            string value,
            bool applyImmediately)
        {
            var oldColor = GUI.backgroundColor;
            if (string.Equals(
                    _trainSignalAspect,
                    value,
                    StringComparison.OrdinalIgnoreCase))
            {
                GUI.backgroundColor = value == "stop"
                    ? new Color(0.82f, 0.24f, 0.20f)
                    : value.Contains("approach")
                        ? new Color(0.88f, 0.64f, 0.12f)
                        : new Color(0.18f, 0.68f, 0.32f);
            }
            if (GUILayout.Button(label, GUILayout.Height(27f)))
            {
                _trainSignalAspect = value;
                if (applyImmediately
                    && _mapEditor.SelectedTrainSignal != null)
                {
                    ApplyTrainSignalForm();
                }
            }
            GUI.backgroundColor = oldColor;
        }

        private void ApplyTrainSignalForm()
        {
            RunGameAction(
                "Saved train signal and refreshed runtime",
                () => _mapEditor.ConfigureSelectedTrainSignal(
                    _trainSignalId,
                    _trainSignalHeadCount,
                    _trainSignalAspect,
                    _trainSignalInterlockingId,
                    _trainSignalProtectedNodeId,
                    _trainSignalProtectedSegmentId,
                    _trainSignalDirection,
                    _trainSignalEnabled));
            _trainSignalSelectionKey = string.Empty;
        }

        private void CaptureDraftTrackBinding()
        {
            var node = _mapEditor.SelectedNode;
            var segment = _mapEditor.SelectedSegment;
            if (node != null)
                _trainSignalProtectedNodeId = node.Id;
            if (segment != null)
                _trainSignalProtectedSegmentId = segment.Id;
        }

        private void SyncTrainSignalForm(
            TileEditorGraphSession.TrainSignalInfo signal)
        {
            var key = signal.Id + "|"
                      + signal.Position + "|"
                      + signal.Rotation + "|"
                      + signal.HeadCount + "|"
                      + signal.InitialAspect + "|"
                      + signal.InterlockingId + "|"
                      + signal.ProtectedNodeId + "|"
                      + signal.ProtectedSegmentId + "|"
                      + signal.Direction + "|"
                      + signal.ApproachId + "|"
                      + signal.TrackLocked + "|"
                      + signal.TrackSegmentId + "|"
                      + signal.TrackParameter + "|"
                      + signal.Enabled;
            if (string.Equals(
                    key,
                    _trainSignalSelectionKey,
                    StringComparison.Ordinal))
            {
                return;
            }
            _trainSignalSelectionKey = key;
            _trainSignalId = signal.Id;
            _trainSignalHeadCount = signal.HeadCount;
            _trainSignalAspect = signal.InitialAspect;
            _trainSignalInterlockingId = signal.InterlockingId;
            _trainSignalProtectedNodeId = signal.ProtectedNodeId;
            _trainSignalProtectedSegmentId = signal.ProtectedSegmentId;
            _trainSignalDirection = signal.Direction;
            _trainSignalEnabled = signal.Enabled;
            _trainSignalPositionX = FormatSignalNumber(signal.Position.x);
            _trainSignalPositionY = FormatSignalNumber(signal.Position.y);
            _trainSignalPositionZ = FormatSignalNumber(signal.Position.z);
            _trainSignalRotationX = FormatSignalNumber(signal.Rotation.x);
            _trainSignalRotationY = FormatSignalNumber(signal.Rotation.y);
            _trainSignalRotationZ = FormatSignalNumber(signal.Rotation.z);
        }

        private void TrainSignalMoveButton(string label, Vector3 direction)
        {
            if (!GUILayout.Button(label, GUILayout.Height(30f)))
                return;
            RunGameAction(
                "Moved train signal",
                () => _mapEditor.MoveSelectedTrainSignal(
                    direction * _trainSignalMoveStep,
                    _trainSignalLocalAxes));
            _trainSignalSelectionKey = string.Empty;
        }

        private void SnapSelectedTrainSignal(bool lockToTrack)
        {
            RunGameAction(() => _mapEditor.SnapSelectedTrainSignalToTrack(
                ParseFloat(
                    _trainSignalSnapSideOffset,
                    "signal side offset"),
                ParseFloat(
                    _trainSignalSnapVerticalOffset,
                    "signal vertical offset"),
                _trainSignalSnapRight,
                lockToTrack));
            _trainSignalSelectionKey = string.Empty;
        }

        private void RotateTrainSignal(float x, float y, float z)
        {
            RunGameAction(
                "Rotated train signal",
                () => _mapEditor.RotateSelectedTrainSignal(
                    new Vector3(x, y, z)));
            _trainSignalSelectionKey = string.Empty;
        }

        private static void TrainSignalStepButton(
            string label,
            float value,
            ref float selected)
        {
            var oldColor = GUI.backgroundColor;
            if (Mathf.Approximately(value, selected))
                GUI.backgroundColor = new Color(0.18f, 0.72f, 0.82f);
            if (GUILayout.Button(label, GUILayout.Height(24f)))
                selected = value;
            GUI.backgroundColor = oldColor;
        }

        private static void TrainSignalChoiceButton(
            string label,
            int value,
            ref int selected)
        {
            var oldColor = GUI.backgroundColor;
            if (value == selected)
                GUI.backgroundColor = new Color(0.18f, 0.72f, 0.82f);
            if (GUILayout.Button(label, GUILayout.Height(27f)))
                selected = value;
            GUI.backgroundColor = oldColor;
        }

        private static void TrainSignalTextChoiceButton(
            string label,
            string value,
            ref string selected)
        {
            var oldColor = GUI.backgroundColor;
            if (string.Equals(
                    value,
                    selected,
                    StringComparison.OrdinalIgnoreCase))
            {
                GUI.backgroundColor = new Color(0.18f, 0.72f, 0.82f);
            }
            if (GUILayout.Button(label, GUILayout.Height(27f)))
                selected = value;
            GUI.backgroundColor = oldColor;
        }

        private static string FormatSignalNumber(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
}
