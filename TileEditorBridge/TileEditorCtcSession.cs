using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Hrogers.TileEditorBridge
{
    internal sealed partial class TileEditorGraphSession
    {
        internal sealed class CtcControlPointInfo
        {
            internal string Id = string.Empty;
            internal string Name = string.Empty;
            internal string SwitchNodeId = string.Empty;
            internal string NormalLabel = "Main";
            internal string ReverseLabel = "Diverging";
            internal string NormalSignalId = string.Empty;
            internal string ReverseSignalId = string.Empty;
            internal string NormalBlockIds = string.Empty;
            internal string ReverseBlockIds = string.Empty;
            internal float BoardX;
            internal float BoardY;
            internal bool IsThrown;
        }

        internal sealed class CtcBlockInfo
        {
            internal string Id = string.Empty;
            internal string Name = string.Empty;
            internal IReadOnlyList<string> SegmentIds = Array.Empty<string>();
            internal string SignalAId = string.Empty;
            internal string SignalBId = string.Empty;
            internal string NextFromAId = string.Empty;
            internal string NextFromBId = string.Empty;
            internal string Mode = "abs";
        }

        internal sealed class TrainOrderInfo
        {
            internal string Id = string.Empty;
            internal int Number;
            internal string Type = "Track Warrant";
            internal string TrainId = string.Empty;
            internal string Crew = string.Empty;
            internal string From = string.Empty;
            internal string To = string.Empty;
            internal string MeetAt = string.Empty;
            internal string Text = string.Empty;
            internal string Status = "Draft";
            internal int Priority;
            internal string Effective = string.Empty;
            internal string Expires = string.Empty;
            internal string AuthorityBlockIds = string.Empty;
            internal bool EnforceAuthority = true;
            internal int MaxSpeedMph;
        }

        private JObject _ctcDocument;
        private string _ctcPath = string.Empty;
        private string _ctcBackupPath = string.Empty;
        private readonly Stack<JObject> _ctcUndo = new Stack<JObject>();
        private readonly Stack<JObject> _ctcRedo = new Stack<JObject>();
        private string _selectedCtcControlPointId = string.Empty;
        private string _selectedCtcBlockId = string.Empty;
        private string _selectedTrainOrderId = string.Empty;

        internal IReadOnlyList<CtcControlPointInfo> CtcControlPoints =>
            CtcControlPointsArray.OfType<JObject>()
                .Select(ReadCtcControlPoint)
                .Where(item => item != null)
                .OrderBy(item => item.BoardY)
                .ThenBy(item => item.BoardX)
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

        internal IReadOnlyList<CtcBlockInfo> CtcBlocks =>
            CtcBlocksArray.OfType<JObject>()
                .Select(ReadCtcBlock)
                .Where(item => item != null)
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

        internal IReadOnlyList<TrainOrderInfo> TrainOrders =>
            TrainOrdersArray.OfType<JObject>()
                .Select(ReadTrainOrder)
                .Where(item => item != null)
                .OrderByDescending(item => item.Number)
                .ThenBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray();

        internal CtcControlPointInfo SelectedCtcControlPoint =>
            ReadCtcControlPoint(FindCtcControlPoint(
                _selectedCtcControlPointId));

        internal CtcBlockInfo SelectedCtcBlock => ReadCtcBlock(
            FindCtcBlock(_selectedCtcBlockId));

        internal TrainOrderInfo SelectedTrainOrder => ReadTrainOrder(
            FindTrainOrder(_selectedTrainOrderId));

        internal bool CanUndoCtc => _ctcUndo.Count > 0;
        internal bool CanRedoCtc => _ctcRedo.Count > 0;
        internal string CtcTerritoryMode
        {
            get
            {
                EnsureCtcDocument();
                var territory = ((JArray)_ctcDocument["territories"])
                    .OfType<JObject>().FirstOrDefault();
                return ((string)territory?["mode"] ?? "ctc")
                    .Trim().ToLowerInvariant();
            }
        }

        internal void SetCtcTerritoryMode(string mode)
        {
            var normalized = (mode ?? string.Empty).Trim().ToLowerInvariant();
            if (!(new[] { "train-orders", "abs", "ctc" })
                .Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Territory mode must be Train Orders, ABS, or CTC.");
            }
            ExecuteCtcEdit(
                "Set signal territory mode",
                () =>
                {
                    var territories = (JArray)_ctcDocument["territories"];
                    var territory = territories.OfType<JObject>()
                        .FirstOrDefault();
                    if (territory == null)
                    {
                        territory = new JObject
                        {
                            ["id"] = "territory:main",
                            ["name"] = "Main Dispatcher",
                        };
                        territories.Add(territory);
                    }
                    territory["mode"] = normalized;
                    territory["signalFamily"] = "semaphore";
                    territory["era"] = "1900-1950";
                });
        }

        internal void SelectCtcControlPoint(string id)
        {
            _selectedCtcControlPointId =
                FindCtcControlPoint(id) == null ? string.Empty : id.Trim();
        }

        internal void SelectCtcBlock(string id)
        {
            _selectedCtcBlockId =
                FindCtcBlock(id) == null ? string.Empty : id.Trim();
        }

        internal void SelectTrainOrder(string id)
        {
            _selectedTrainOrderId =
                FindTrainOrder(id) == null ? string.Empty : id.Trim();
        }

        internal string CreateCtcControlPointFromSelectedNode(
            string requestedId,
            string name,
            float boardX,
            float boardY)
        {
            RequireSession();
            if (_selectedNode == null)
                throw new InvalidOperationException(
                    "Click the turnout node in the world first.");
            var connected = _graph.SegmentsConnectedTo(_selectedNode).Count();
            if (connected < 3)
                throw new InvalidOperationException(
                    "A CTC switch control point needs a turnout node with "
                    + "at least three connected track segments.");
            var id = NormalizeCtcId(requestedId, "cp:new");
            if (FindCtcControlPoint(id) != null)
                throw new InvalidOperationException(
                    "Control point '" + id + "' already exists.");
            var nodeId = _selectedNode.id;
            var entry = new JObject
            {
                ["id"] = id,
                ["name"] = string.IsNullOrWhiteSpace(name) ? id : name.Trim(),
                ["board"] = new JObject
                {
                    ["x"] = boardX,
                    ["y"] = boardY,
                },
                ["switches"] = new JArray
                {
                    new JObject
                    {
                        ["nodeId"] = nodeId,
                        ["normalLabel"] = "Main",
                        ["reverseLabel"] = "Diverging",
                    },
                },
                ["routes"] = BuildDefaultCtcRoutes(nodeId),
            };
            ExecuteCtcEdit(
                "Create CTC control point",
                () =>
                {
                    CtcControlPointsArray.Add(entry);
                    _selectedCtcControlPointId = id;
                });
            return "Created control point " + id + " at switch " + nodeId;
        }

        internal void ConfigureSelectedCtcControlPoint(
            string requestedId,
            string name,
            string normalLabel,
            string reverseLabel,
            string normalSignalId,
            string reverseSignalId,
            string normalBlockIds,
            string reverseBlockIds,
            float boardX,
            float boardY)
        {
            var entry = RequireSelectedCtcControlPoint();
            var oldId = ((string)entry["id"] ?? string.Empty).Trim();
            var id = NormalizeCtcId(requestedId, oldId);
            var duplicate = FindCtcControlPoint(id);
            if (duplicate != null && duplicate != entry)
                throw new InvalidOperationException(
                    "Control point '" + id + "' already exists.");
            var switchEntry = (entry["switches"] as JArray)?
                .OfType<JObject>().FirstOrDefault();
            if (switchEntry == null)
                throw new InvalidOperationException(
                    "This control point has no switch assignment.");
            var nodeId = ((string)switchEntry["nodeId"]
                          ?? string.Empty).Trim();
            ExecuteCtcEdit(
                "Configure CTC control point",
                () =>
                {
                    entry["id"] = id;
                    entry["name"] = string.IsNullOrWhiteSpace(name)
                        ? id
                        : name.Trim();
                    entry["board"] = new JObject
                    {
                        ["x"] = boardX,
                        ["y"] = boardY,
                    };
                    switchEntry["normalLabel"] =
                        CleanLabel(normalLabel, "Main");
                    switchEntry["reverseLabel"] =
                        CleanLabel(reverseLabel, "Diverging");
                    entry["routes"] = new JArray
                    {
                        BuildCtcRoute(
                            "normal",
                            CleanLabel(normalLabel, "Main"),
                            nodeId,
                            false,
                            normalSignalId,
                            ParseCtcIds(normalBlockIds)),
                        BuildCtcRoute(
                            "reverse",
                            CleanLabel(reverseLabel, "Diverging"),
                            nodeId,
                            true,
                            reverseSignalId,
                            ParseCtcIds(reverseBlockIds)),
                    };
                    _selectedCtcControlPointId = id;
                });
        }

        internal void DeleteSelectedCtcControlPoint()
        {
            var entry = RequireSelectedCtcControlPoint();
            ExecuteCtcEdit(
                "Delete CTC control point",
                () =>
                {
                    entry.Remove();
                    _selectedCtcControlPointId = string.Empty;
                });
        }

        internal string CreateCtcBlockFromSelectedSegment(
            string requestedId,
            string name)
        {
            RequireSession();
            if (_selectedSegment == null)
                throw new InvalidOperationException(
                    "Click the first track segment for this block.");
            var id = NormalizeCtcId(requestedId, "block:new");
            if (FindCtcBlock(id) != null)
                throw new InvalidOperationException(
                    "CTC block '" + id + "' already exists.");
            var entry = new JObject
            {
                ["id"] = id,
                ["name"] = string.IsNullOrWhiteSpace(name) ? id : name.Trim(),
                ["segmentIds"] = new JArray(_selectedSegment.id),
                ["mode"] = "abs",
                ["signals"] = new JObject
                {
                    ["a"] = string.Empty,
                    ["b"] = string.Empty,
                },
                ["nextBlocks"] = new JObject
                {
                    ["fromA"] = string.Empty,
                    ["fromB"] = string.Empty,
                },
            };
            ExecuteCtcEdit(
                "Create CTC block",
                () =>
                {
                    CtcBlocksArray.Add(entry);
                    _selectedCtcBlockId = id;
                });
            return "Created block " + id + " with " + _selectedSegment.id;
        }

        internal string AddSelectedSegmentToCtcBlock()
        {
            var entry = RequireSelectedCtcBlock();
            if (_selectedSegment == null)
                throw new InvalidOperationException(
                    "Click the next track segment to add.");
            var ids = entry["segmentIds"] as JArray ?? new JArray();
            var segmentId = _selectedSegment.id;
            if (ids.Values<string>().Any(id => string.Equals(
                    id,
                    segmentId,
                    StringComparison.OrdinalIgnoreCase)))
            {
                return segmentId + " is already in this block";
            }
            ExecuteCtcEdit(
                "Add segment to CTC block",
                () =>
                {
                    if (entry["segmentIds"] == null)
                        entry["segmentIds"] = ids;
                    ids.Add(segmentId);
                });
            return "Added " + segmentId + " to "
                   + ((string)entry["id"] ?? string.Empty);
        }

        internal void ConfigureSelectedCtcBlock(
            string name,
            string signalAId,
            string signalBId,
            string nextFromAId,
            string nextFromBId,
            string mode)
        {
            var entry = RequireSelectedCtcBlock();
            var normalizedMode = (mode ?? "abs").Trim().ToLowerInvariant();
            if (!(new[] { "abs", "ctc", "manual" })
                .Contains(normalizedMode, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Block mode must be ABS, CTC, or Manual.");
            }
            ExecuteCtcEdit(
                "Configure signal block",
                () =>
                {
                    entry["name"] = string.IsNullOrWhiteSpace(name)
                        ? (string)entry["id"]
                        : name.Trim();
                    entry["mode"] = normalizedMode;
                    entry["signals"] = new JObject
                    {
                        ["a"] = (signalAId ?? string.Empty).Trim(),
                        ["b"] = (signalBId ?? string.Empty).Trim(),
                    };
                    entry["nextBlocks"] = new JObject
                    {
                        ["fromA"] = (nextFromAId ?? string.Empty).Trim(),
                        ["fromB"] = (nextFromBId ?? string.Empty).Trim(),
                    };
                });
        }

        internal void DeleteSelectedCtcBlock()
        {
            var entry = RequireSelectedCtcBlock();
            var id = ((string)entry["id"] ?? string.Empty).Trim();
            ExecuteCtcEdit(
                "Delete CTC block",
                () =>
                {
                    entry.Remove();
                    foreach (var route in CtcControlPointsArray
                                 .OfType<JObject>()
                                 .SelectMany(cp => (cp["routes"] as JArray
                                                    ?? new JArray())
                                     .OfType<JObject>()))
                    {
                        var kept = (route["blockIds"] as JArray
                                    ?? new JArray()).Values<string>()
                            .Where(blockId => !string.Equals(
                                blockId,
                                id,
                                StringComparison.OrdinalIgnoreCase));
                        route["blockIds"] = new JArray(kept);
                    }
                    _selectedCtcBlockId = string.Empty;
                });
        }

        internal string CreateTrainOrder(
            string requestedId,
            int number,
            string type,
            string trainId,
            string crew,
            string from,
            string to,
            string meetAt,
            string text,
            string effective,
            string expires,
            int priority,
            string authorityBlockIds,
            bool enforceAuthority,
            int maxSpeedMph)
        {
            var id = NormalizeCtcId(
                requestedId,
                "order:" + Math.Max(1, number).ToString(
                    CultureInfo.InvariantCulture));
            if (FindTrainOrder(id) != null)
                throw new InvalidOperationException(
                    "Train order '" + id + "' already exists.");
            var entry = new JObject
            {
                ["id"] = id,
                ["number"] = Math.Max(1, number),
                ["type"] = CleanLabel(type, "Track Warrant"),
                ["trainId"] = (trainId ?? string.Empty).Trim(),
                ["crew"] = (crew ?? string.Empty).Trim(),
                ["crewId"] = string.Empty,
                ["limits"] = new JObject
                {
                    ["from"] = (from ?? string.Empty).Trim(),
                    ["to"] = (to ?? string.Empty).Trim(),
                },
                ["meetAt"] = (meetAt ?? string.Empty).Trim(),
                ["text"] = (text ?? string.Empty).Trim(),
                ["status"] = "Draft",
                ["priority"] = Math.Max(0, priority),
                ["effective"] = (effective ?? string.Empty).Trim(),
                ["expires"] = (expires ?? string.Empty).Trim(),
                ["requiresAcknowledgement"] = true,
                ["authority"] = new JObject
                {
                    ["blockIds"] = new JArray(
                        ParseCtcIds(authorityBlockIds)),
                    ["enforce"] = enforceAuthority,
                    ["maxSpeedMph"] = Math.Max(0, maxSpeedMph),
                },
            };
            ExecuteCtcEdit(
                "Create train order",
                () =>
                {
                    TrainOrdersArray.Add(entry);
                    _selectedTrainOrderId = id;
                });
            return "Created draft train order No. " + Math.Max(1, number);
        }

        internal void SetSelectedTrainOrderStatus(string status)
        {
            var entry = RequireSelectedTrainOrder();
            var normalized = CleanLabel(status, "Draft");
            if (!(new[]
                {
                    "Draft", "Issued", "Delivered", "Acknowledged",
                    "Fulfilled", "Cancelled",
                })
                .Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Train order status must be Draft, Issued, Delivered, "
                    + "Acknowledged, Fulfilled, or Cancelled.");
            }
            ExecuteCtcEdit(
                "Set train order status",
                () => entry["status"] = normalized);
        }

        internal void DeleteSelectedTrainOrder()
        {
            var entry = RequireSelectedTrainOrder();
            ExecuteCtcEdit(
                "Delete train order",
                () =>
                {
                    entry.Remove();
                    _selectedTrainOrderId = string.Empty;
                });
        }

        internal string IssueSelectedTrainOrder()
        {
            return RunTrainOrderRuntimeAction(
                "TryIssueTrainOrder",
                "Issued train order");
        }

        internal string DeliverSelectedTrainOrder(string trainCrewId)
        {
            var order = RequireSelectedTrainOrder();
            var id = ((string)order["id"] ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(trainCrewId))
                throw new InvalidOperationException(
                    "Select the train crew receiving this order.");
            if (!InvokeSignalRuntimeBool(
                    "TryDeliverTrainOrder",
                    id,
                    trainCrewId.Trim()))
            {
                throw new InvalidOperationException(
                    "The runtime could not queue delivery. Confirm Signal "
                    + "Runtime is installed and the order is loaded.");
            }
            return "Delivery queued for train order " + id;
        }

        internal string AcknowledgeSelectedTrainOrder()
        {
            return RunTrainOrderRuntimeAction(
                "TryAcknowledgeTrainOrder",
                "Acknowledgement queued for train order");
        }

        internal string FulfillSelectedTrainOrder()
        {
            return RunTrainOrderRuntimeAction(
                "TryFulfillTrainOrder",
                "Fulfillment queued for train order");
        }

        internal string CancelSelectedTrainOrder()
        {
            return RunTrainOrderRuntimeAction(
                "TryCancelTrainOrder",
                "Cancellation queued for train order");
        }

        private string RunTrainOrderRuntimeAction(
            string method,
            string message)
        {
            var order = RequireSelectedTrainOrder();
            var id = ((string)order["id"] ?? string.Empty).Trim();
            if (!InvokeSignalRuntimeBool(method, id))
                throw new InvalidOperationException(
                    "The standalone Signal Runtime could not queue this "
                    + "train-order action.");
            return message + " " + id;
        }

        internal string DescribeTrainOrderRuntime(string id)
        {
            var method = ResolveSignalRuntimeMainType()?.GetMethod(
                "TryGetTrainOrder",
                BindingFlags.Public | BindingFlags.Static);
            if (method == null)
                return "Runtime not installed";
            try
            {
                var arguments = new object[] { id, null };
                if (!(bool)method.Invoke(null, arguments)
                    || arguments[1] == null)
                {
                    return "Waiting for runtime synchronization";
                }
                var value = arguments[1];
                var type = value.GetType();
                var status = type.GetProperty("Status")?
                                 .GetValue(value, null)?.ToString()
                             ?? "Draft";
                var crew = type.GetProperty("AssignedCrewId")?
                               .GetValue(value, null)?.ToString()
                           ?? string.Empty;
                var by = type.GetProperty("AcknowledgedBy")?
                             .GetValue(value, null)?.ToString()
                         ?? string.Empty;
                var reason = type.GetProperty("LastReason")?
                                 .GetValue(value, null)?.ToString()
                             ?? string.Empty;
                return status
                       + (crew.Length == 0 ? string.Empty : " / crew " + crew)
                       + (by.Length == 0 ? string.Empty : " / ack " + by)
                       + (reason.Length == 0 ? string.Empty : " / " + reason);
            }
            catch (Exception ex)
            {
                return "Runtime status unavailable: "
                       + (ex.InnerException?.Message ?? ex.Message);
            }
        }

        internal string SetSelectedCtcSwitch(bool thrown)
        {
            var cp = SelectedCtcControlPoint;
            if (cp == null)
                throw new InvalidOperationException(
                    "Select a CTC control point first.");
            if (!InvokeSignalRuntimeBool(
                    "TrySetCtcSwitch",
                    cp.Id,
                    thrown))
            {
                throw new InvalidOperationException(
                    "The runtime refused the switch command. A car may be "
                    + "on the switch, a route may be locked, or the runtime "
                    + "may still be reloading.");
            }
            return cp.Id + " switch commanded "
                   + (thrown ? "Reverse" : "Normal");
        }

        internal string LineSelectedCtcRoute(string routeId)
        {
            var cp = SelectedCtcControlPoint;
            if (cp == null)
                throw new InvalidOperationException(
                    "Select a CTC control point first.");
            if (!InvokeSignalRuntimeBool(
                    "TryLineCtcRoute",
                    cp.Id,
                    routeId))
            {
                throw new InvalidOperationException(
                    "The route could not be lined. Check block occupancy, "
                    + "switch clearance, signal assignment, and conflicts.");
            }
            return "Lined " + cp.Id + " / " + routeId;
        }

        internal string CancelSelectedCtcRoute()
        {
            var cp = SelectedCtcControlPoint;
            if (cp == null)
                throw new InvalidOperationException(
                    "Select a CTC control point first.");
            if (!InvokeSignalRuntimeBool("TryCancelCtcRoute", cp.Id))
                throw new InvalidOperationException(
                    "The runtime refused cancellation while the route is "
                    + "occupied or approach locked.");
            return "Cancelled route at " + cp.Id;
        }

        internal string DescribeCtcControlPointRuntime(string id)
        {
            var method = ResolveSignalRuntimeMainType()?.GetMethod(
                "TryGetCtcControlPoint",
                BindingFlags.Public | BindingFlags.Static);
            if (method == null)
                return "Runtime not installed";
            try
            {
                var arguments = new object[] { id, null };
                if (!(bool)method.Invoke(null, arguments)
                    || arguments[1] == null)
                {
                    return "Waiting for runtime reload";
                }
                var value = arguments[1];
                var type = value.GetType();
                var phase = type.GetProperty("Phase")?
                                .GetValue(value, null)?.ToString()
                            ?? "Stop";
                var active = type.GetProperty("ActiveRouteId")?
                                 .GetValue(value, null)?.ToString()
                             ?? string.Empty;
                var reason = type.GetProperty("LastReason")?
                                 .GetValue(value, null)?.ToString()
                             ?? string.Empty;
                return phase
                       + (string.IsNullOrWhiteSpace(active)
                           ? string.Empty
                           : " / " + active)
                       + (string.IsNullOrWhiteSpace(reason)
                           ? string.Empty
                           : " / " + reason);
            }
            catch (Exception ex)
            {
                return "Runtime status unavailable: "
                       + (ex.InnerException?.Message ?? ex.Message);
            }
        }

        internal void UndoCtc()
        {
            if (_ctcUndo.Count == 0)
                return;
            _ctcRedo.Push((JObject)_ctcDocument.DeepClone());
            _ctcDocument = _ctcUndo.Pop();
            SaveCtcDocument();
        }

        internal void RedoCtc()
        {
            if (_ctcRedo.Count == 0)
                return;
            _ctcUndo.Push((JObject)_ctcDocument.DeepClone());
            _ctcDocument = _ctcRedo.Pop();
            SaveCtcDocument();
        }

        private void ExecuteCtcEdit(string name, Action mutation)
        {
            RequireSession();
            EnsureCtcDocument();
            var before = (JObject)_ctcDocument.DeepClone();
            try
            {
                mutation();
                SaveCtcDocument();
                _ctcUndo.Push(before);
                while (_ctcUndo.Count > 30)
                {
                    var kept = _ctcUndo.Take(30).Reverse().ToArray();
                    _ctcUndo.Clear();
                    foreach (var item in kept)
                        _ctcUndo.Push(item);
                }
                _ctcRedo.Clear();
                _logger?.Log(name + " saved to " + _ctcPath);
            }
            catch
            {
                _ctcDocument = before;
                throw;
            }
        }

        private void SaveCtcDocument()
        {
            EnsureCtcDocument();
            if (string.IsNullOrWhiteSpace(_ctcBackupPath)
                && File.Exists(_ctcPath))
            {
                _ctcBackupPath = _ctcPath + ".tile-editor-backup-"
                    + DateTime.Now.ToString(
                        "yyyyMMdd-HHmmss",
                        CultureInfo.InvariantCulture);
                File.Copy(_ctcPath, _ctcBackupPath, false);
                TileEditorBackupRetention.PruneFor(_ctcPath);
            }
            var directory = Path.GetDirectoryName(_ctcPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            var temp = _ctcPath + ".tile-editor.tmp";
            File.WriteAllText(
                temp,
                _ctcDocument.ToString(Formatting.Indented));
            if (File.Exists(_ctcPath))
            {
                try
                {
                    File.Replace(temp, _ctcPath, null);
                }
                catch
                {
                    File.Delete(_ctcPath);
                    File.Move(temp, _ctcPath);
                }
            }
            else
            {
                File.Move(temp, _ctcPath);
            }
            ReloadStandaloneSignalRuntime();
        }

        private void ResetCtcSession()
        {
            _selectedCtcControlPointId = string.Empty;
            _selectedCtcBlockId = string.Empty;
            _selectedTrainOrderId = string.Empty;
            _ctcUndo.Clear();
            _ctcRedo.Clear();
            _ctcBackupPath = string.Empty;
            _ctcPath = string.IsNullOrWhiteSpace(_graphPath)
                ? string.Empty
                : Path.Combine(
                    Path.GetDirectoryName(_graphPath) ?? string.Empty,
                    "ctc-system.json");
            _ctcDocument = null;
            EnsureCtcDocument();
        }

        private void DisposeCtcSession()
        {
            _ctcDocument = null;
            _ctcPath = string.Empty;
            _ctcUndo.Clear();
            _ctcRedo.Clear();
        }

        private void EnsureCtcDocument()
        {
            if (_ctcDocument != null)
                return;
            if (!string.IsNullOrWhiteSpace(_ctcPath)
                && File.Exists(_ctcPath))
            {
                try
                {
                    _ctcDocument = JObject.Parse(File.ReadAllText(_ctcPath));
                }
                catch (Exception ex)
                {
                    _logger?.Warning(
                        "Could not read ctc-system.json; the invalid file "
                        + "will remain untouched until an edit is made: "
                        + ex.Message);
                }
            }
            if (_ctcDocument == null)
            {
                _ctcDocument = new JObject
                {
                    ["$schema"] =
                        "https://hrogers.dev/railroader/ctc-system/v1",
                    ["formatVersion"] = 1,
                    ["territories"] = new JArray
                    {
                        new JObject
                        {
                            ["id"] = "territory:main",
                            ["name"] = "Main Dispatcher",
                            ["mode"] = "ctc",
                            ["signalFamily"] = "semaphore",
                            ["era"] = "1900-1950",
                            ["controlPointIds"] = new JArray(),
                            ["blockIds"] = new JArray(),
                        },
                    },
                    ["controlPoints"] = new JArray(),
                    ["blocks"] = new JArray(),
                    ["trainOrders"] = new JArray(),
                };
            }
            if (!(_ctcDocument["territories"] is JArray))
                _ctcDocument["territories"] = new JArray();
            if (!(_ctcDocument["controlPoints"] is JArray))
                _ctcDocument["controlPoints"] = new JArray();
            if (!(_ctcDocument["blocks"] is JArray))
                _ctcDocument["blocks"] = new JArray();
            if (!(_ctcDocument["trainOrders"] is JArray))
                _ctcDocument["trainOrders"] = new JArray();
        }

        private JArray CtcControlPointsArray
        {
            get
            {
                EnsureCtcDocument();
                return (JArray)_ctcDocument["controlPoints"];
            }
        }

        private JArray CtcBlocksArray
        {
            get
            {
                EnsureCtcDocument();
                return (JArray)_ctcDocument["blocks"];
            }
        }

        private JArray TrainOrdersArray
        {
            get
            {
                EnsureCtcDocument();
                return (JArray)_ctcDocument["trainOrders"];
            }
        }

        private JObject FindCtcControlPoint(string id) =>
            FindById(CtcControlPointsArray, id);

        private JObject FindCtcBlock(string id) =>
            FindById(CtcBlocksArray, id);

        private JObject FindTrainOrder(string id) =>
            FindById(TrainOrdersArray, id);

        private static JObject FindById(JArray array, string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return null;
            return array.OfType<JObject>().FirstOrDefault(item =>
                string.Equals(
                    ((string)item["id"] ?? string.Empty).Trim(),
                    id.Trim(),
                    StringComparison.OrdinalIgnoreCase));
        }

        private JObject RequireSelectedCtcControlPoint() =>
            FindCtcControlPoint(_selectedCtcControlPointId)
            ?? throw new InvalidOperationException(
                "Select a CTC control point first.");

        private JObject RequireSelectedCtcBlock() =>
            FindCtcBlock(_selectedCtcBlockId)
            ?? throw new InvalidOperationException("Select a CTC block first.");

        private JObject RequireSelectedTrainOrder() =>
            FindTrainOrder(_selectedTrainOrderId)
            ?? throw new InvalidOperationException(
                "Select a train order first.");

        private CtcControlPointInfo ReadCtcControlPoint(JObject entry)
        {
            if (entry == null)
                return null;
            var switchEntry = (entry["switches"] as JArray
                               ?? new JArray()).OfType<JObject>()
                .FirstOrDefault();
            var routes = (entry["routes"] as JArray
                          ?? new JArray()).OfType<JObject>().ToArray();
            var normal = routes.FirstOrDefault(route => string.Equals(
                (string)route["id"],
                "normal",
                StringComparison.OrdinalIgnoreCase));
            var reverse = routes.FirstOrDefault(route => string.Equals(
                (string)route["id"],
                "reverse",
                StringComparison.OrdinalIgnoreCase));
            var nodeId = ((string)switchEntry?["nodeId"]
                          ?? string.Empty).Trim();
            var node = string.IsNullOrWhiteSpace(nodeId)
                ? null
                : _graph?.GetNode(nodeId);
            return new CtcControlPointInfo
            {
                Id = ((string)entry["id"] ?? string.Empty).Trim(),
                Name = ((string)entry["name"] ?? string.Empty).Trim(),
                SwitchNodeId = nodeId,
                NormalLabel = ((string)switchEntry?["normalLabel"]
                               ?? "Main").Trim(),
                ReverseLabel = ((string)switchEntry?["reverseLabel"]
                                ?? "Diverging").Trim(),
                NormalSignalId = ((string)normal?["entrySignalId"]
                                  ?? string.Empty).Trim(),
                ReverseSignalId = ((string)reverse?["entrySignalId"]
                                   ?? string.Empty).Trim(),
                NormalBlockIds = JoinCtcIds(normal?["blockIds"] as JArray),
                ReverseBlockIds = JoinCtcIds(reverse?["blockIds"] as JArray),
                BoardX = ReadCtcFloat(entry["board"]?["x"], 0f),
                BoardY = ReadCtcFloat(entry["board"]?["y"], 0f),
                IsThrown = node != null && node.isThrown,
            };
        }

        private static CtcBlockInfo ReadCtcBlock(JObject entry)
        {
            if (entry == null)
                return null;
            return new CtcBlockInfo
            {
                Id = ((string)entry["id"] ?? string.Empty).Trim(),
                Name = ((string)entry["name"] ?? string.Empty).Trim(),
                SegmentIds = ParseCtcIds(
                    JoinCtcIds(entry["segmentIds"] as JArray)),
                SignalAId = ((string)entry["signals"]?["a"]
                             ?? string.Empty).Trim(),
                SignalBId = ((string)entry["signals"]?["b"]
                             ?? string.Empty).Trim(),
                NextFromAId = ((string)entry["nextBlocks"]?["fromA"]
                               ?? string.Empty).Trim(),
                NextFromBId = ((string)entry["nextBlocks"]?["fromB"]
                               ?? string.Empty).Trim(),
                Mode = ((string)entry["mode"] ?? "abs")
                    .Trim().ToLowerInvariant(),
            };
        }

        private static TrainOrderInfo ReadTrainOrder(JObject entry)
        {
            if (entry == null)
                return null;
            return new TrainOrderInfo
            {
                Id = ((string)entry["id"] ?? string.Empty).Trim(),
                Number = (int?)entry["number"] ?? 0,
                Type = ((string)entry["type"] ?? "Track Warrant").Trim(),
                TrainId = ((string)entry["trainId"] ?? string.Empty).Trim(),
                Crew = ((string)entry["crew"] ?? string.Empty).Trim(),
                From = ((string)entry["limits"]?["from"]
                        ?? string.Empty).Trim(),
                To = ((string)entry["limits"]?["to"]
                      ?? string.Empty).Trim(),
                MeetAt = ((string)entry["meetAt"] ?? string.Empty).Trim(),
                Text = ((string)entry["text"] ?? string.Empty).Trim(),
                Status = ((string)entry["status"] ?? "Draft").Trim(),
                Priority = (int?)entry["priority"] ?? 0,
                Effective = ((string)entry["effective"]
                             ?? string.Empty).Trim(),
                Expires = ((string)entry["expires"]
                           ?? string.Empty).Trim(),
                AuthorityBlockIds = JoinCtcIds(
                    entry["authority"]?["blockIds"] as JArray),
                EnforceAuthority =
                    (bool?)entry["authority"]?["enforce"] ?? true,
                MaxSpeedMph =
                    (int?)entry["authority"]?["maxSpeedMph"] ?? 0,
            };
        }

        private static JArray BuildDefaultCtcRoutes(string nodeId)
        {
            return new JArray
            {
                BuildCtcRoute(
                    "normal", "Main", nodeId, false, string.Empty,
                    Array.Empty<string>()),
                BuildCtcRoute(
                    "reverse", "Diverging", nodeId, true, string.Empty,
                    Array.Empty<string>()),
            };
        }

        private static JObject BuildCtcRoute(
            string id,
            string label,
            string nodeId,
            bool thrown,
            string entrySignalId,
            IEnumerable<string> blockIds)
        {
            return new JObject
            {
                ["id"] = id,
                ["label"] = label,
                ["entrySignalId"] =
                    (entrySignalId ?? string.Empty).Trim(),
                ["blockIds"] = new JArray(blockIds),
                ["switchSettings"] = new JArray
                {
                    new JObject
                    {
                        ["nodeId"] = nodeId,
                        ["thrown"] = thrown,
                    },
                },
            };
        }

        private static string NormalizeCtcId(
            string value,
            string fallback)
        {
            var text = string.IsNullOrWhiteSpace(value)
                ? fallback
                : value.Trim();
            var normalized = new string(text.Select(character =>
                    char.IsLetterOrDigit(character)
                    || character == ':'
                    || character == '-'
                    || character == '_'
                    || character == '.'
                        ? character
                        : '-')
                .ToArray()).Trim('-');
            if (normalized.Length == 0)
                throw new InvalidOperationException("ID cannot be empty.");
            return normalized;
        }

        private static string CleanLabel(string value, string fallback) =>
            string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

        private static IReadOnlyList<string> ParseCtcIds(string value) =>
            (value ?? string.Empty).Split(
                    new[] { ',', ';', '\r', '\n' },
                    StringSplitOptions.RemoveEmptyEntries)
                .Select(id => id.Trim())
                .Where(id => id.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

        private static string JoinCtcIds(JArray array) => string.Join(
            ", ",
            (array ?? new JArray()).Values<string>()
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim()));

        private static float ReadCtcFloat(JToken token, float fallback)
        {
            return token != null && float.TryParse(
                token.ToString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var parsed)
                ? parsed
                : fallback;
        }
    }
}
