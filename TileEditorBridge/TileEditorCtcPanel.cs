using System;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace Hrogers.TileEditorBridge
{
    internal sealed partial class TileEditorBridgePanel
    {
        private string _ctcControlPointId = "cp:new";
        private string _ctcControlPointName = "New Control Point";
        private string _ctcNormalLabel = "Main";
        private string _ctcReverseLabel = "Diverging";
        private string _ctcNormalSignalId = string.Empty;
        private string _ctcReverseSignalId = string.Empty;
        private string _ctcNormalBlockIds = string.Empty;
        private string _ctcReverseBlockIds = string.Empty;
        private string _ctcBoardX = "0";
        private string _ctcBoardY = "0";
        private string _ctcControlPointSelectionKey = string.Empty;
        private string _ctcBlockId = "block:new";
        private string _ctcBlockName = "New Signal Block";
        private string _ctcBlockSignalA = string.Empty;
        private string _ctcBlockSignalB = string.Empty;
        private string _ctcBlockNextFromA = string.Empty;
        private string _ctcBlockNextFromB = string.Empty;
        private string _ctcBlockMode = "abs";
        private string _ctcBlockSelectionKey = string.Empty;
        private bool _showCtcAuthoring = true;
        private string _trainOrderId = "order:new";
        private string _trainOrderNumber = "1";
        private string _trainOrderType = "Form 19";
        private string _trainOrderTrainId = string.Empty;
        private string _trainOrderCrew = string.Empty;
        private string _trainOrderFrom = string.Empty;
        private string _trainOrderTo = string.Empty;
        private string _trainOrderMeetAt = string.Empty;
        private string _trainOrderText = string.Empty;
        private string _trainOrderEffective = string.Empty;
        private string _trainOrderExpires = string.Empty;
        private string _trainOrderPriority = "0";
        private string _trainOrderAuthorityBlocks = string.Empty;
        private string _trainOrderMaxSpeed = "0";
        private bool _trainOrderEnforceAuthority = true;
        private void DrawOperationsCtcSignals()
        {
            GUILayout.Label("SIGNALS / CTC TERRITORY EDITOR", _onlineStyle);
            GUILayout.Label(
                "Place and configure portable 1900-1950s signaling here. "
                + "Operate the railroad from Company > Operations > "
                + "Signals & CTC; F9 remains the authoring workspace.",
                _mutedStyle);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Territory", GUILayout.Width(68f));
            CtcTerritoryModeButton("ORDERS", "train-orders");
            CtcTerritoryModeButton("ABS", "abs");
            CtcTerritoryModeButton("CTC", "ctc");
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
            GUILayout.Label(
                "TERRITORY PREVIEW / SELECT CONTROL POINT",
                _titleStyle);
            var controlPoints = _mapEditor.CtcControlPoints;
            if (controlPoints.Count == 0)
            {
                GUILayout.Label(
                    "No control points yet. Click a turnout node, then use "
                    + "Create Control Point below.",
                    _mutedStyle);
            }
            foreach (var cp in controlPoints)
                DrawCtcBoardControlPoint(cp);

            GUILayout.BeginHorizontal();
            GUI.enabled = _mapEditor.CanUndoCtc;
            if (GUILayout.Button("Undo CTC"))
                RunGameAction("Undid CTC edit", _mapEditor.UndoCtc);
            GUI.enabled = _mapEditor.CanRedoCtc;
            if (GUILayout.Button("Redo CTC"))
                RunGameAction("Redid CTC edit", _mapEditor.RedoCtc);
            GUI.enabled = true;
            if (GUILayout.Button(
                    _showCtcAuthoring ? "Hide Authoring" : "Edit Territory"))
            {
                _showCtcAuthoring = !_showCtcAuthoring;
            }
            GUILayout.EndHorizontal();
            if (!_showCtcAuthoring)
                return;

            GUILayout.Space(8f);
            DrawCtcControlPointAuthoring();
            GUILayout.Space(8f);
            DrawCtcBlockAuthoring();
        }

        private void CtcTerritoryModeButton(string label, string value)
        {
            var old = GUI.backgroundColor;
            if (string.Equals(
                    _mapEditor.CtcTerritoryMode,
                    value,
                    StringComparison.OrdinalIgnoreCase))
            {
                GUI.backgroundColor = new Color(0.16f, 0.66f, 0.76f);
            }
            if (GUILayout.Button(label, GUILayout.Height(27f)))
            {
                RunGameAction(
                    "Set territory mode to " + label,
                    () => _mapEditor.SetCtcTerritoryMode(value));
            }
            GUI.backgroundColor = old;
        }

        private void DrawCtcBoardControlPoint(
            TileEditorGraphSession.CtcControlPointInfo cp)
        {
            var rect = GUILayoutUtility.GetRect(
                100f,
                70f,
                GUILayout.ExpandWidth(true));
            GUI.Box(rect, GUIContent.none);
            var selected = _mapEditor.SelectedCtcControlPoint?.Id == cp.Id;
            var old = GUI.color;
            GUI.color = selected
                ? new Color(0.20f, 0.82f, 0.92f)
                : Color.white;
            GUI.Label(
                new Rect(rect.x + 8f, rect.y + 4f, rect.width - 16f, 20f),
                cp.Name + "  [" + cp.Id + "]");
            GUI.Label(
                new Rect(rect.x + 8f, rect.y + 22f, rect.width - 160f, 18f),
                _mapEditor.DescribeCtcControlPointRuntime(cp.Id));
            GUI.color = old;

            var centerY = rect.y + 41f;
            var left = new Vector2(rect.x + 12f, centerY);
            var switchPoint = new Vector2(rect.x + rect.width * 0.47f, centerY);
            var right = new Vector2(rect.x + rect.width - 146f, centerY);
            var diverging = new Vector2(right.x, centerY + 22f);
            var trackColor = new Color(0.72f, 0.78f, 0.80f);
            DrawCtcLine(left, switchPoint, trackColor, 4f);
            DrawCtcLine(
                switchPoint,
                cp.IsThrown ? diverging : right,
                new Color(0.20f, 0.90f, 0.42f),
                5f);
            DrawCtcLine(
                switchPoint,
                cp.IsThrown ? right : diverging,
                new Color(0.28f, 0.31f, 0.33f),
                3f);

            if (GUI.Button(
                    new Rect(
                        rect.x + rect.width - 136f,
                        rect.y + 39f,
                        126f,
                        24f),
                    selected ? "SELECTED" : "SELECT / EDIT"))
            {
                _mapEditor.SelectCtcControlPoint(cp.Id);
                _ctcControlPointSelectionKey = string.Empty;
            }
        }

        private static void DrawCtcLine(
            Vector2 start,
            Vector2 end,
            Color color,
            float width)
        {
            var oldMatrix = GUI.matrix;
            var oldColor = GUI.color;
            var delta = end - start;
            var angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
            GUIUtility.RotateAroundPivot(angle, start);
            GUI.color = color;
            GUI.DrawTexture(
                new Rect(start.x, start.y - width * 0.5f,
                    delta.magnitude, width),
                Texture2D.whiteTexture);
            GUI.matrix = oldMatrix;
            GUI.color = oldColor;
        }

        private void DrawCtcControlPointAuthoring()
        {
            GUILayout.Label("CONTROL POINTS / POWER SWITCHES", _titleStyle);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("NEW CONTROL POINT..."))
            {
                _mapEditor.SelectCtcControlPoint(string.Empty);
                _ctcControlPointSelectionKey = string.Empty;
                _ctcControlPointId = "cp:new";
                _ctcControlPointName = "New Control Point";
            }
            if (GUILayout.Button("PLACE / EDIT SIGNAL MASTS..."))
            {
                _panelTab = PanelTab.Signals;
                _panelScroll = Vector2.zero;
            }
            GUILayout.EndHorizontal();
            foreach (var item in _mapEditor.CtcControlPoints)
            {
                var old = GUI.backgroundColor;
                if (_mapEditor.SelectedCtcControlPoint?.Id == item.Id)
                    GUI.backgroundColor = new Color(0.15f, 0.62f, 0.72f);
                if (GUILayout.Button(
                        item.Name + "  / switch " + item.SwitchNodeId,
                        GUILayout.Height(26f)))
                {
                    _mapEditor.SelectCtcControlPoint(item.Id);
                    _ctcControlPointSelectionKey = string.Empty;
                }
                GUI.backgroundColor = old;
            }

            var selected = _mapEditor.SelectedCtcControlPoint;
            if (selected != null)
            {
                SyncCtcControlPointForm(selected);
                DrawTextField("Control point ID", ref _ctcControlPointId);
                DrawTextField("Display name", ref _ctcControlPointName);
                DrawTextField("Normal route label", ref _ctcNormalLabel);
                DrawTextField("Reverse route label", ref _ctcReverseLabel);
                DrawTextField("Normal entry signal", ref _ctcNormalSignalId);
                DrawTextField("Reverse entry signal", ref _ctcReverseSignalId);
                DrawTextField(
                    "Normal route blocks",
                    ref _ctcNormalBlockIds);
                DrawTextField(
                    "Reverse route blocks",
                    ref _ctcReverseBlockIds);
                DrawTextField("Board X", ref _ctcBoardX);
                DrawTextField("Board Y", ref _ctcBoardY);
                if (GUILayout.Button(
                        "APPLY CONTROL POINT / ROUTES",
                        GUILayout.Height(32f)))
                {
                    RunGameAction(
                        "Saved CTC control point",
                        () => _mapEditor.ConfigureSelectedCtcControlPoint(
                            _ctcControlPointId,
                            _ctcControlPointName,
                            _ctcNormalLabel,
                            _ctcReverseLabel,
                            _ctcNormalSignalId,
                            _ctcReverseSignalId,
                            _ctcNormalBlockIds,
                            _ctcReverseBlockIds,
                            ParseFloat(_ctcBoardX, "board X"),
                            ParseFloat(_ctcBoardY, "board Y")));
                    _ctcControlPointSelectionKey = string.Empty;
                }
                if (GUILayout.Button("Delete Control Point..."))
                    RunGameAction(
                        "Deleted CTC control point",
                        _mapEditor.DeleteSelectedCtcControlPoint);
            }
            else
            {
                DrawTextField("New control point ID", ref _ctcControlPointId);
                DrawTextField("Name", ref _ctcControlPointName);
                DrawTextField("Board X", ref _ctcBoardX);
                DrawTextField("Board Y", ref _ctcBoardY);
                GUI.enabled = _mapEditor.SelectedNode != null;
                if (GUILayout.Button(
                        "CREATE FROM CLICKED TURNOUT NODE",
                        GUILayout.Height(34f)))
                {
                    RunGameAction(() => _mapEditor
                        .CreateCtcControlPointFromSelectedNode(
                            _ctcControlPointId,
                            _ctcControlPointName,
                            ParseFloat(_ctcBoardX, "board X"),
                            ParseFloat(_ctcBoardY, "board Y")));
                    _ctcControlPointSelectionKey = string.Empty;
                }
                GUI.enabled = true;
            }
            GUILayout.Label(
                "Entry signal IDs come from the Signals workspace. Block IDs "
                + "are comma-separated. A route throws its assigned switches "
                + "only after every protected block is clear.",
                _mutedStyle);
        }

        private void SyncCtcControlPointForm(
            TileEditorGraphSession.CtcControlPointInfo item)
        {
            if (item == null || _ctcControlPointSelectionKey == item.Id)
                return;
            _ctcControlPointSelectionKey = item.Id;
            _ctcControlPointId = item.Id;
            _ctcControlPointName = item.Name;
            _ctcNormalLabel = item.NormalLabel;
            _ctcReverseLabel = item.ReverseLabel;
            _ctcNormalSignalId = item.NormalSignalId;
            _ctcReverseSignalId = item.ReverseSignalId;
            _ctcNormalBlockIds = item.NormalBlockIds;
            _ctcReverseBlockIds = item.ReverseBlockIds;
            _ctcBoardX = item.BoardX.ToString(
                "0.##", CultureInfo.InvariantCulture);
            _ctcBoardY = item.BoardY.ToString(
                "0.##", CultureInfo.InvariantCulture);
        }

        private void DrawCtcBlockAuthoring()
        {
            GUILayout.Label("ABS / CTC BLOCKS", _titleStyle);
            if (GUILayout.Button("NEW SIGNAL BLOCK..."))
            {
                _mapEditor.SelectCtcBlock(string.Empty);
                _ctcBlockSelectionKey = string.Empty;
                _ctcBlockId = "block:new";
                _ctcBlockName = "New Signal Block";
            }
            foreach (var block in _mapEditor.CtcBlocks)
            {
                var old = GUI.backgroundColor;
                if (_mapEditor.SelectedCtcBlock?.Id == block.Id)
                    GUI.backgroundColor = new Color(0.78f, 0.64f, 0.16f);
                if (GUILayout.Button(
                        block.Name + "  / " + block.SegmentIds.Count
                        + " segment(s) / " + block.Mode.ToUpperInvariant(),
                        GUILayout.Height(26f)))
                {
                    _mapEditor.SelectCtcBlock(block.Id);
                    _ctcBlockSelectionKey = string.Empty;
                }
                GUI.backgroundColor = old;
            }
            var selected = _mapEditor.SelectedCtcBlock;
            if (selected == null)
            {
                DrawTextField("New block ID", ref _ctcBlockId);
                DrawTextField("Name", ref _ctcBlockName);
                GUI.enabled = _mapEditor.SelectedSegment != null;
                if (GUILayout.Button(
                        "CREATE BLOCK FROM CLICKED SEGMENT",
                        GUILayout.Height(33f)))
                {
                    RunGameAction(() => _mapEditor
                        .CreateCtcBlockFromSelectedSegment(
                            _ctcBlockId,
                            _ctcBlockName));
                    _ctcBlockSelectionKey = string.Empty;
                }
                GUI.enabled = true;
                return;
            }
            SyncCtcBlockForm(selected);
            GUILayout.Label(
                "Segments: " + string.Join(", ", selected.SegmentIds),
                _mutedStyle);
            DrawTextField("Block name", ref _ctcBlockName);
            DrawTextField("Signal at end A", ref _ctcBlockSignalA);
            DrawTextField("Signal at end B", ref _ctcBlockSignalB);
            DrawTextField("Next block entering A", ref _ctcBlockNextFromA);
            DrawTextField("Next block entering B", ref _ctcBlockNextFromB);
            GUILayout.BeginHorizontal();
            CtcBlockModeButton("ABS", "abs");
            CtcBlockModeButton("CTC", "ctc");
            CtcBlockModeButton("MANUAL", "manual");
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUI.enabled = _mapEditor.SelectedSegment != null;
            if (GUILayout.Button("ADD CLICKED SEGMENT"))
                RunGameAction(_mapEditor.AddSelectedSegmentToCtcBlock);
            GUI.enabled = true;
            if (GUILayout.Button("APPLY BLOCK"))
            {
                RunGameAction(
                    "Saved signal block",
                    () => _mapEditor.ConfigureSelectedCtcBlock(
                        _ctcBlockName,
                        _ctcBlockSignalA,
                        _ctcBlockSignalB,
                        _ctcBlockNextFromA,
                        _ctcBlockNextFromB,
                        _ctcBlockMode));
                _ctcBlockSelectionKey = string.Empty;
            }
            GUILayout.EndHorizontal();
            if (GUILayout.Button("Delete Block..."))
                RunGameAction(
                    "Deleted signal block",
                    _mapEditor.DeleteSelectedCtcBlock);
            GUILayout.Label(
                "ABS: Stop when occupied, Approach when the next block is "
                + "occupied, Clear when both are clear. CTC blocks also obey "
                + "dispatcher route direction. Manual blocks are governed by "
                + "train orders or an operator.",
                _mutedStyle);
        }

        private void CtcBlockModeButton(string label, string value)
        {
            var old = GUI.backgroundColor;
            if (string.Equals(
                    _ctcBlockMode,
                    value,
                    StringComparison.OrdinalIgnoreCase))
            {
                GUI.backgroundColor = new Color(0.18f, 0.67f, 0.77f);
            }
            if (GUILayout.Button(label))
                _ctcBlockMode = value;
            GUI.backgroundColor = old;
        }

        private void SyncCtcBlockForm(
            TileEditorGraphSession.CtcBlockInfo block)
        {
            if (block == null || _ctcBlockSelectionKey == block.Id)
                return;
            _ctcBlockSelectionKey = block.Id;
            _ctcBlockId = block.Id;
            _ctcBlockName = block.Name;
            _ctcBlockSignalA = block.SignalAId;
            _ctcBlockSignalB = block.SignalBId;
            _ctcBlockNextFromA = block.NextFromAId;
            _ctcBlockNextFromB = block.NextFromBId;
            _ctcBlockMode = block.Mode;
        }

        private void DrawOperationsTrainOrders()
        {
            GUILayout.Label("TIMETABLE & TRAIN ORDER AUTHORING", _onlineStyle);
            GUILayout.Label(
                "Write and configure portable orders here. Dispatchers issue "
                + "and deliver them from Company > Operations > Train "
                + "Orders; crews acknowledge them under My Orders or F8.",
                _mutedStyle);
            foreach (var order in _mapEditor.TrainOrders)
            {
                var old = GUI.backgroundColor;
                if (string.Equals(order.Status, "Issued",
                        StringComparison.OrdinalIgnoreCase))
                    GUI.backgroundColor = new Color(0.72f, 0.56f, 0.12f);
                if (GUILayout.Button(
                        "No. " + order.Number + "  " + order.Type + "  / "
                        + (string.IsNullOrWhiteSpace(order.TrainId)
                            ? "All Trains"
                            : order.TrainId)
                        + "  / " + order.Status,
                        GUILayout.Height(28f)))
                {
                    _mapEditor.SelectTrainOrder(order.Id);
                }
                GUI.backgroundColor = old;
            }
            var selected = _mapEditor.SelectedTrainOrder;
            if (selected != null)
            {
                GUILayout.Label(
                    "SELECTED ORDER No. " + selected.Number,
                    _titleStyle);
                GUILayout.Label(
                    selected.From + " to " + selected.To
                    + (string.IsNullOrWhiteSpace(selected.MeetAt)
                        ? string.Empty
                        : " / meet at " + selected.MeetAt),
                    _lineStyle);
                GUILayout.Label(selected.Text, _mutedStyle);
                GUILayout.Label(
                    "Live: "
                    + _mapEditor.DescribeTrainOrderRuntime(selected.Id),
                    _lineStyle);
                GUILayout.Label(
                    "Live issue, delivery, acknowledgement, fulfillment, "
                    + "and cancellation controls are in the normal Company "
                    + "> Operations window.",
                    _mutedStyle);
                if (GUILayout.Button("Delete Train Order..."))
                    RunGameAction(
                        "Deleted train order",
                        _mapEditor.DeleteSelectedTrainOrder);
            }

            GUILayout.Space(8f);
            GUILayout.Label("WRITE A NEW ORDER", _titleStyle);
            DrawTextField("Order ID", ref _trainOrderId);
            DrawTextField("Number", ref _trainOrderNumber);
            GUILayout.BeginHorizontal();
            TrainOrderTypeButton("FORM 19", "Form 19");
            TrainOrderTypeButton("FORM 31", "Form 31");
            TrainOrderTypeButton("WARRANT", "Track Warrant");
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            TrainOrderTypeButton("MEET", "Meet Order");
            TrainOrderTypeButton("HOLD", "Hold Order");
            TrainOrderTypeButton("EXTRA", "Run Extra");
            GUILayout.EndHorizontal();
            DrawTextField("Train / Extra", ref _trainOrderTrainId);
            DrawTextField("Conductor / engineer", ref _trainOrderCrew);
            DrawTextField("Authority from", ref _trainOrderFrom);
            DrawTextField("Authority to", ref _trainOrderTo);
            DrawTextField(
                "Authority block IDs",
                ref _trainOrderAuthorityBlocks);
            GUILayout.Label(
                "Click a block to add it to this authority:",
                _mutedStyle);
            GUILayout.BeginHorizontal();
            foreach (var block in _mapEditor.CtcBlocks.Take(6))
            {
                if (GUILayout.Button(block.Id))
                    _trainOrderAuthorityBlocks = AppendCtcId(
                        _trainOrderAuthorityBlocks,
                        block.Id);
            }
            GUILayout.EndHorizontal();
            DrawTextField(
                "Maximum speed mph (0 = none)",
                ref _trainOrderMaxSpeed);
            _trainOrderEnforceAuthority = GUILayout.Toggle(
                _trainOrderEnforceAuthority,
                "Host-enforce this movement authority");
            DrawTextField("Meet at", ref _trainOrderMeetAt);
            DrawTextField("Effective", ref _trainOrderEffective);
            DrawTextField("Expires", ref _trainOrderExpires);
            DrawTextField("Priority", ref _trainOrderPriority);
            GUILayout.Label("Order text", _mutedStyle);
            _trainOrderText = GUILayout.TextArea(
                _trainOrderText ?? string.Empty,
                GUILayout.MinHeight(62f));
            if (GUILayout.Button(
                    "SAVE DRAFT TRAIN ORDER",
                    GUILayout.Height(35f)))
            {
                RunGameAction(() => _mapEditor.CreateTrainOrder(
                    _trainOrderId,
                    ParseInt(_trainOrderNumber, "order number"),
                    _trainOrderType,
                    _trainOrderTrainId,
                    _trainOrderCrew,
                    _trainOrderFrom,
                    _trainOrderTo,
                    _trainOrderMeetAt,
                    _trainOrderText,
                    _trainOrderEffective,
                    _trainOrderExpires,
                    ParseInt(_trainOrderPriority, "priority"),
                    _trainOrderAuthorityBlocks,
                    _trainOrderEnforceAuthority,
                    ParseInt(_trainOrderMaxSpeed, "maximum speed")));
            }
            GUILayout.Label(
                "F8 opens the standalone crew order window. Delivered "
                + "orders hold the assigned train until a real crew member "
                + "repeats and acknowledges them. Acknowledged block limits "
                + "are then enforced by the multiplayer host for manual and "
                + "Auto Engineer trains.",
                _mutedStyle);
        }

        private static string AppendCtcId(string current, string id)
        {
            var values = (current ?? string.Empty)
                .Split(new[] { ',', ';', ' ' },
                    StringSplitOptions.RemoveEmptyEntries)
                .Select(value => value.Trim())
                .Concat(new[] { id })
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase);
            return string.Join(", ", values);
        }

        private void TrainOrderTypeButton(string label, string value)
        {
            var old = GUI.backgroundColor;
            if (string.Equals(
                    _trainOrderType,
                    value,
                    StringComparison.OrdinalIgnoreCase))
            {
                GUI.backgroundColor = new Color(0.68f, 0.54f, 0.16f);
            }
            if (GUILayout.Button(label))
                _trainOrderType = value;
            GUI.backgroundColor = old;
        }

        private void TrainOrderStatusButton(string label, string status)
        {
            if (GUILayout.Button(label))
            {
                RunGameAction(
                    "Train order marked " + status,
                    () => _mapEditor.SetSelectedTrainOrderStatus(status));
            }
        }
    }
}
