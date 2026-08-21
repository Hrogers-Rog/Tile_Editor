using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Core;
using Helpers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Track;
using UnityEngine;
using UnityModManagerNet;

namespace Hrogers.TileEditorBridge
{
    internal sealed partial class TileEditorGraphSession : IDisposable
    {
        internal sealed class SelectionInfo
        {
            internal string Id = string.Empty;
            internal Vector3 Position;
            internal Vector3 Rotation;
            internal bool FlipSwitchStand;
            internal int ConnectedSegments;
            internal string[] ConnectedSegmentIds = Array.Empty<string>();
            internal string StartNodeId = string.Empty;
            internal string EndNodeId = string.Empty;
            internal float Length;
            internal string GroupId = string.Empty;
            internal string Gauge = "Standard";
            internal string TrackClass = "Mainline";
        }

        [Flags]
        internal enum NodePropertyFields
        {
            None = 0,
            Elevation = 1,
            Grade = 2,
            Heading = 4,
            Bank = 8,
            SwitchStand = 16,
            Rotation = Grade | Heading | Bank,
            ElevationAndGrade = Elevation | Grade,
            ElevationAndRotation = Elevation | Rotation,
            All = Elevation | Rotation | SwitchStand,
        }

        internal sealed class GraphChoice
        {
            internal string DisplayName = string.Empty;
            internal string ModKey = string.Empty;
            internal string ModName = string.Empty;
            internal string LayerName = string.Empty;
            internal string Path = string.Empty;
            internal bool IsPrimary;
        }

        private sealed class NodeModel
        {
            internal string Id;
            internal Vector3 Position;
            internal Vector3 Rotation;
            internal bool FlipSwitchStand;
        }

        private sealed class SegmentModel
        {
            internal string Id;
            internal string A;
            internal string B;
            internal int Priority;
            internal int SpeedLimit;
            internal string GroupId;
            internal string Gauge;
            internal TrackSegment.Style Style;
            internal TrackClass TrackClass;
        }

        private sealed class EditRecord
        {
            internal string Name;
            internal string[] NodeIds;
            internal string[] SegmentIds;
            internal string[] SceneryIds;
            internal string[] MandelaIds;
            internal Dictionary<string, NodeModel> BeforeNodes;
            internal Dictionary<string, SegmentModel> BeforeSegments;
            internal Dictionary<string, SceneryModel> BeforeScenery;
            internal Dictionary<string, MandelaModel> BeforeMandelas;
            internal Dictionary<string, NodeModel> AfterNodes;
            internal Dictionary<string, SegmentModel> AfterSegments;
            internal Dictionary<string, SceneryModel> AfterScenery;
            internal Dictionary<string, MandelaModel> AfterMandelas;
            internal JObject BeforeDocument;
            internal JObject AfterDocument;
            internal JObject BeforeToolshedFacilities;
            internal JObject AfterToolshedFacilities;
            internal bool BeforeToolshedFacilitiesDirty;
            internal bool AfterToolshedFacilitiesDirty;
            internal string BeforeSelectedNode;
            internal string BeforeSelectedSegment;
            internal string BeforeSelectedScenery;
            internal string BeforeSelectedMandela;
            internal string AfterSelectedNode;
            internal string AfterSelectedSegment;
            internal string AfterSelectedScenery;
            internal string AfterSelectedMandela;
            internal bool UseLightweightTrackUpdate;
        }

        private readonly UnityModManager.ModEntry.ModLogger _logger;
        private readonly string _gameRoot;
        private readonly Stack<EditRecord> _undo = new Stack<EditRecord>();
        private readonly Stack<EditRecord> _redo = new Stack<EditRecord>();
        private readonly List<GraphChoice> _graphChoices = new List<GraphChoice>();
        private Graph _graph;
        private JObject _document;
        private TrackNode _selectedNode;
        private TrackSegment _selectedSegment;
        private string _graphPath = string.Empty;
        private string _backupPath = string.Empty;
        private bool _fuseNativeDocument;
        internal bool FuseNativeDocument => _fuseNativeDocument;
        private readonly HashSet<string> _documentNodeIdsAtOpen =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _documentSegmentIdsAtOpen =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _runtimeNodeIdsAtOpen =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _runtimeSegmentIdsAtOpen =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool _editModeActive;
        private bool _geoWorkspaceActive = true;
        private bool _workspaceModeInitialized;
        private bool _trackOverlaysBuilt;
        private float _nextDynamicOverlayRefreshAt;
        private readonly HashSet<string> _pendingNodeOverlayRepairs =
            new HashSet<string>();
        private readonly HashSet<string> _pendingSegmentOverlayRepairs =
            new HashSet<string>();
        private readonly HashSet<string> _pendingSegmentGeometryRefresh =
            new HashSet<string>();
        private readonly HashSet<string> _deferredTrackNodeIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _deferredTrackSegmentIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, TileEditorSegmentOverlay>
            _segmentOverlays =
                new Dictionary<string, TileEditorSegmentOverlay>(
                    StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, TileEditorNodeOverlay>
            _nodeOverlays =
                new Dictionary<string, TileEditorNodeOverlay>(
                    StringComparer.OrdinalIgnoreCase);
        private GameObject _segmentOverlayRoot;
        private bool _trackOverlayVisibility;
        private bool _segmentGradeLabelsVisible;
        private float _nextTrackOverlayCullAt;
        private Vector3 _lastTrackOverlayCameraPosition;
        private float _lastTrackOverlayRange;
        private bool _repairAllTrackOverlays;
        private int _trackOverlayRepairPasses;
        private float _nextTrackOverlayRepairAt;
        private float _nextSegmentGeometryRefreshAt;
        private bool _deferredTrackRebuilds;
        private bool _deferredFullTrackRebuildPending;
        private bool _dirty;
        private bool _choicesLoaded;
        private bool _externalGraphEditLock;
        private bool _externalTerrainEditLock;
        private string _worldNodeShortcutStatus =
            "Click then Shift-click to connect; Ctrl-drag a node to move it.";
        private string _newNodeIdPrefix = "N_TE_";
        private string _newNodeIdBaseName = string.Empty;
        private int _newNodeIdSequence = 1;

        internal TileEditorGraphSession(
            UnityModManager.ModEntry.ModLogger logger,
            string gameRoot)
        {
            _logger = logger;
            _gameRoot = gameRoot;
        }

        internal bool Available
        {
            get
            {
                AttachGraph();
                return _graph != null;
            }
        }

        internal bool GraphOpen => Available && _document != null;
        internal bool Dirty => _dirty;
        internal int ChangeCount => _undo.Count;
        internal bool HasUnsavedContent =>
            Dirty
            || SplineyDirty
            || TelegraphPoleDirty
            || TerrainDirty
            || ToolshedFacilitiesDirty;
        internal string WorldNodeShortcutStatus =>
            _worldNodeShortcutStatus;
        internal string NewNodeIdPrefix => _newNodeIdPrefix;
        internal string NewNodeIdBaseName => _newNodeIdBaseName;
        internal string NextNodeIdPreview =>
            FindAvailableNodeId(false);

        internal void ConfigureNewNodeIds(
            string prefix,
            string baseName)
        {
            var normalizedPrefix = NormalizeNodeIdPart(
                prefix,
                "N_TE");
            if (!normalizedPrefix.EndsWith(
                    "_",
                    StringComparison.Ordinal)
                && !normalizedPrefix.EndsWith(
                    "-",
                    StringComparison.Ordinal))
            {
                normalizedPrefix += "_";
            }
            var normalizedBaseName = NormalizeNodeIdPart(
                baseName,
                string.Empty).Trim('_', '-');
            if (string.Equals(
                    normalizedPrefix,
                    _newNodeIdPrefix,
                    StringComparison.Ordinal)
                && string.Equals(
                    normalizedBaseName,
                    _newNodeIdBaseName,
                    StringComparison.Ordinal))
            {
                return;
            }
            _newNodeIdPrefix = normalizedPrefix;
            _newNodeIdBaseName = normalizedBaseName;
            _newNodeIdSequence = 1;
        }

        internal void SetExternalEditorLocks(
            bool graphLocked,
            bool terrainLocked)
        {
            _externalGraphEditLock = graphLocked;
            _externalTerrainEditLock = terrainLocked;
        }
        internal string GraphPath => _graphPath;
        internal string GraphName => string.IsNullOrWhiteSpace(_graphPath)
            ? "No edit layer"
            : Path.GetFileName(_graphPath);
        internal bool SegmentGradeLabelsVisible =>
            _segmentGradeLabelsVisible;
        internal bool DeferredTrackRebuilds =>
            _deferredTrackRebuilds;
        internal bool TrackRebuildPending =>
            _deferredFullTrackRebuildPending
            || _deferredTrackNodeIds.Count > 0
            || _deferredTrackSegmentIds.Count > 0;
        internal int DeferredTrackChangeCount =>
            _deferredTrackNodeIds.Count
            + _deferredTrackSegmentIds.Count;

        internal void SetDeferredTrackRebuilds(bool deferred)
        {
            if (_deferredTrackRebuilds == deferred)
                return;
            if (!deferred && TrackRebuildPending)
                RebuildTrack();
            _deferredTrackRebuilds = deferred;
        }

        internal void SetSegmentGradeLabelsVisible(bool visible)
        {
            _segmentGradeLabelsVisible = visible;
            foreach (var overlay in _segmentOverlays.Values)
                overlay?.RefreshGradeLabel();
        }

        internal IReadOnlyList<GraphChoice> GraphChoices
        {
            get
            {
                DiscoverGraphChoices();
                return _graphChoices;
            }
        }

        internal void RefreshGraphChoices()
        {
            _choicesLoaded = false;
            DiscoverGraphChoices();
        }

        internal string CreateNewMapMod(
            string modId,
            string displayName,
            string author,
            bool nativeFuse,
            bool completeMap = false,
            double mapOriginLatitude = 35.382614,
            double mapOriginLongitude = -83.49541)
        {
            if (HasUnsavedContent)
            {
                throw new InvalidOperationException(
                    "Save or undo current in-game changes before creating a mod.");
            }

            modId = (modId ?? string.Empty).Trim();
            displayName = (displayName ?? string.Empty).Trim();
            author = (author ?? string.Empty).Trim();
            if (!IsPortableModId(modId))
            {
                throw new InvalidOperationException(
                    "Mod ID must use only letters, numbers, underscores, and dots.");
            }
            if (string.Equals(modId, "railloader", StringComparison.OrdinalIgnoreCase)
                || string.Equals(modId, "railroader", StringComparison.OrdinalIgnoreCase)
                || string.Equals(modId, "FUSE", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "That mod ID is reserved. Choose a unique author-prefixed ID.");
            }
            if (string.IsNullOrWhiteSpace(displayName))
                throw new InvalidOperationException("Enter a display name.");
            if (completeMap && !nativeFuse)
            {
                throw new InvalidOperationException(
                    "Complete standalone maps require the Native FUSE package format.");
            }
            if (completeMap
                && (double.IsNaN(mapOriginLatitude)
                    || double.IsInfinity(mapOriginLatitude)
                    || mapOriginLatitude < -90d
                    || mapOriginLatitude > 90d))
            {
                throw new InvalidOperationException(
                    "Map origin latitude must be a number between -90 and 90.");
            }
            if (completeMap
                && (double.IsNaN(mapOriginLongitude)
                    || double.IsInfinity(mapOriginLongitude)
                    || mapOriginLongitude < -180d
                    || mapOriginLongitude > 180d))
            {
                throw new InvalidOperationException(
                    "Map origin longitude must be a number between -180 and 180.");
            }

            var modsFolder = Path.Combine(_gameRoot, "Mods");
            Directory.CreateDirectory(modsFolder);
            var modFolder = Path.Combine(modsFolder, modId);
            if (Directory.Exists(modFolder)
                && Directory.EnumerateFileSystemEntries(modFolder).Any())
            {
                throw new InvalidOperationException(
                    "The mod folder already exists and is not empty: " + modFolder);
            }
            Directory.CreateDirectory(modFolder);

            string graphPath;
            if (nativeFuse)
            {
                graphPath = Path.Combine(modFolder, "map.fuse.json");
                var info = new JObject
                {
                    ["$schema"] = "../FUSE/schemas/umm-info.schema.json",
                    ["Id"] = modId,
                    ["DisplayName"] = displayName,
                    ["Author"] = author,
                    ["Version"] = "0.1.0",
                    ["ManagerVersion"] = "0.27.10",
                    ["GameVersion"] = "2025.1",
                    ["Requirements"] = new JArray
                    {
                        new JObject
                        {
                            ["Id"] = "FUSE",
                            ["NotBefore"] = "1.0.0",
                        },
                    },
                    ["LoadAfter"] = new JArray("FUSE"),
                    ["FuseLoadPriority"] = 100,
                    ["FuseLoadAfter"] = new JArray(),
                    ["FuseLoadBefore"] = new JArray(),
                    ["FuseDataFiles"] = new JArray("map.fuse.json"),
                };
                var data = new JObject
                {
                    ["$schema"] = "../FUSE/schemas/fuse-mod.schema.json",
                    ["schemaVersion"] = "1.0",
                    ["id"] = modId,
                    ["name"] = displayName,
                    ["author"] = author,
                    ["modVersion"] = "0.1.0",
                    ["coordinateSpace"] = "world",
                    ["tracks"] = new JObject
                    {
                        ["nodes"] = new JObject(),
                        ["segments"] = new JObject(),
                        ["spans"] = new JObject(),
                        ["removals"] = new JObject
                        {
                            ["nodes"] = new JArray(),
                            ["segments"] = new JArray(),
                            ["spans"] = new JArray(),
                        },
                    },
                };
                if (completeMap)
                {
                    data["map"] = new JObject
                    {
                        ["displayName"] = displayName,
                        ["mapFolder"] = "Map",
                        ["suppressBaseWorld"] = true,
                    };

                    var mapFolder = Path.Combine(modFolder, "Map");
                    Directory.CreateDirectory(mapFolder);
                    var mapManifest = new JObject
                    {
                        ["origin"] = new JObject
                        {
                            ["latitude"] = mapOriginLatitude,
                            ["longitude"] = mapOriginLongitude,
                        },
                        ["tileDimension"] = 500d,
                        ["tiles"] = new JArray(),
                    };
                    File.WriteAllText(
                        Path.Combine(mapFolder, "Map.json"),
                        mapManifest.ToString(Formatting.Indented));
                }
                File.WriteAllText(
                    Path.Combine(modFolder, "Info.json"),
                    info.ToString(Formatting.Indented));
                File.WriteAllText(
                    graphPath,
                    data.ToString(Formatting.Indented));
            }
            else
            {
                graphPath = Path.Combine(modFolder, "game-graph.json");
                var definition = new JObject
                {
                    ["manifestVersion"] = 8,
                    ["id"] = modId,
                    ["name"] = displayName,
                    ["version"] = "0.1.0",
                    ["requires"] = new JArray
                    {
                        new JObject
                        {
                            ["id"] = "railloader",
                            ["notBefore"] = "1.8.2.1",
                        },
                    },
                    ["mixintos"] = new JObject
                    {
                        ["game-graph"] = new JArray("file(game-graph.json)"),
                    },
                };
                var graph = new JObject
                {
                    ["tracks"] = new JObject
                    {
                        ["nodes"] = new JObject(),
                        ["segments"] = new JObject(),
                        ["spans"] = new JObject(),
                    },
                    ["areas"] = new JObject(),
                    ["texts"] = new JObject(),
                    ["scenery"] = new JObject(),
                    ["splineys"] = new JObject(),
                    ["simpleGraphs"] = new JObject(),
                    ["mandelas"] = new JObject(),
                };
                File.WriteAllText(
                    Path.Combine(modFolder, "Definition.json"),
                    definition.ToString(Formatting.Indented));
                File.WriteAllText(
                    graphPath,
                    graph.ToString(Formatting.Indented));
            }

            _choicesLoaded = false;
            DiscoverGraphChoices();
            _logger?.Log(
                "Tile Editor created "
                + (nativeFuse ? "native FUSE" : "compatible")
                + (completeMap ? " standalone" : " add-on")
                + " map package: " + modFolder);
            return graphPath;
        }

        private static bool IsPortableModId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;
            foreach (var character in value)
            {
                if (!char.IsLetterOrDigit(character)
                    && character != '_'
                    && character != '.')
                {
                    return false;
                }
            }
            return true;
        }

        internal SelectionInfo SelectedNode => _selectedNode == null
            ? null
            : new SelectionInfo
            {
                Id = _selectedNode.id,
                Position = _selectedNode.transform.localPosition,
                Rotation = _selectedNode.transform.localEulerAngles,
                FlipSwitchStand = _selectedNode.flipSwitchStand,
                ConnectedSegments =
                    _graph.SegmentsConnectedTo(_selectedNode).Count(),
                ConnectedSegmentIds = _graph
                    .SegmentsConnectedTo(_selectedNode)
                    .Select(segment => segment.id)
                    .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
            };

        internal SelectionInfo SelectedSegment => _selectedSegment == null
            ? null
            : new SelectionInfo
            {
                Id = _selectedSegment.id,
                StartNodeId = _selectedSegment.a?.id ?? string.Empty,
                EndNodeId = _selectedSegment.b?.id ?? string.Empty,
                Length = _selectedSegment.GetLength(),
                GroupId = _selectedSegment.groupId ?? string.Empty,
                Gauge = GetSegmentGauge(_selectedSegment.id),
                TrackClass = _selectedSegment.trackClass.ToString(),
            };

        internal string DescribeSelectedWyeForwardSpace()
        {
            if (_selectedNode == null || _graph == null)
                return string.Empty;
            var connected = _graph.SegmentsConnectedTo(_selectedNode).ToList();
            if (connected.Count != 2)
                return string.Empty;
            var forwardSegment = FindForwardSegment(_selectedNode, connected);
            return forwardSegment == null
                ? "Forward segment could not be determined."
                : "Forward space: " + forwardSegment.id + " • "
                  + forwardSegment.GetLength().ToString(
                      "0.0", CultureInfo.InvariantCulture)
                  + " m available";
        }

        internal void SetEditMode(bool active)
        {
            _editModeActive = active;
            AttachGraph();
            if (_graph == null)
                return;
            if (active && !_trackOverlaysBuilt)
                RebuildOverlays(rebuildExisting: false);
            SetOverlaysVisible(
                active
                && ((_geoWorkspaceActive
                     && (!_splineyMode || _splineTrackPickMode))
                    || _operationsMode));
            SetSplineyOverlaysVisible(active && _splineyMode);
            SetSceneryOverlaysVisible(active && _sceneryMode);
            SetTelegraphPoleOverlaysVisible(active && _poleMode);
            SetOperationOverlaysVisible(
                active && _operationsMode && GraphOpen);
            SetTrainSignalOverlaysVisible(
                active && _trainSignalMode && GraphOpen);
        }

        internal void RefreshEditMode()
        {
            var previous = _graph;
            AttachGraph();
            if (_graph == null)
                return;
            RefreshPersistentTelegraphPoles();
            FlushPendingNarrowGaugeSynchronization();
            if (previous != _graph && _editModeActive)
            {
                RebuildOverlays(rebuildExisting: false);
                SetOverlaysVisible(
                    (_geoWorkspaceActive
                     && (!_splineyMode || _splineTrackPickMode))
                    || _operationsMode);
                SetSplineyOverlaysVisible(_splineyMode);
                SetSceneryOverlaysVisible(_sceneryMode);
                SetTelegraphPoleOverlaysVisible(_poleMode);
                if (_operationsMode)
                    RefreshOperationsMode(true);
                if (_trainSignalMode)
                    RefreshTrainSignalOverlays();
            }
            if (_editModeActive)
            {
                RepairPendingTrackOverlays();
                RefreshPendingSegmentGeometry();
                UpdateTrackOverlayCulling();
            }
            if (!_editModeActive
                || Time.unscaledTime < _nextDynamicOverlayRefreshAt)
            {
                return;
            }
            _nextDynamicOverlayRefreshAt =
                Time.unscaledTime + 2f;
            if (_splineyMode)
                RefreshSplineyMode();
            if (_sceneryMode)
                RefreshSceneryMode();
            if (_poleMode)
                RefreshTelegraphPoleMode();
            if (_operationsMode)
                RefreshOperationsMode(false);
            if (_trainSignalMode)
                RefreshLockedTrainSignalOverlayTransforms();
        }

        internal bool TryOpenGraph(string path)
        {
            if (GraphOpen || string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return false;
            OpenGraph(path);
            return true;
        }

        internal void OpenGraph(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                throw new FileNotFoundException("The graph edit layer was not found.", path);
            AttachGraph();
            if (_graph == null)
                throw new InvalidOperationException(
                    "Railroader's live track graph is not ready yet.");
            if (TrackRebuildPending)
                RebuildTrack();

            var text = File.ReadAllText(path);
            var document = JObject.Parse(text);
            EnsureTrackObjects(document);
            _document = document;
            _fuseNativeDocument = IsFuseNativeDocument(path, document);
            ResetOpenGraphIdentitySets();
            _graphPath = Path.GetFullPath(path);
            TileEditorTrackOverrides.LoadForGraph(_graphPath);
            RefreshAutoEngineerCrossings();
            _backupPath = string.Empty;
            _undo.Clear();
            _redo.Clear();
            _dirty = false;
            _selectedNode = null;
            _selectedSegment = null;
            ResetGaugeSession();
            RebuildOverlays(rebuildExisting: false);
            SetOverlaysVisible(
                _editModeActive
                && ((_geoWorkspaceActive
                     && (!_splineyMode || _splineTrackPickMode))
                    || _operationsMode));
            _logger?.Log("Tile Editor opened graph edit layer: " + _graphPath);
            ResetSplineySources();
            ResetScenerySession();
            ResetMandelaSession();
            ResetOperationsSession();
            ResetWaterSession();
            ResetTrainSignalSession();
            ResetCtcSession();
            if (_operationsMode)
                RefreshOperationsMode(true);
        }

        internal void SelectNode(TrackNode node)
        {
            if (!_editModeActive || node == null)
                return;
            var previousNode = _selectedNode;
            var previousSegment = _selectedSegment;
            _selectedNode = node;
            _selectedSegment = null;
            RefreshTrackSelectionColors(
                previousNode,
                previousSegment,
                _selectedNode,
                _selectedSegment);
        }

        internal void SelectNodeById(string nodeId)
        {
            if (_graph == null || string.IsNullOrWhiteSpace(nodeId))
                return;
            SelectNode(_graph.GetNode(nodeId));
        }

        internal void SelectSegmentById(string segmentId)
        {
            if (_graph == null || string.IsNullOrWhiteSpace(segmentId))
                return;
            SelectSegment(_graph.GetSegment(segmentId));
        }

        internal void ActivateNodeFromWorld(
            TrackNode node,
            bool connectFromSelected)
        {
            if (!_editModeActive || node == null)
                return;
            var start = _selectedNode;
            if (!connectFromSelected
                || start == null
                || start == node)
            {
                SelectNode(node);
                _worldNodeShortcutStatus =
                    "Selected " + node.id
                    + ". Shift-click another node to connect, or Ctrl-drag "
                    + "this node to move it.";
                return;
            }

            SelectNode(node);
            try
            {
                ConnectFrom(start.id);
                _worldNodeShortcutStatus =
                    "Connected " + start.id + " to " + node.id
                    + ". " + node.id
                    + " remains selected; Shift-click again to continue.";
            }
            catch (Exception ex)
            {
                _worldNodeShortcutStatus =
                    "Could not connect " + start.id + " to " + node.id
                    + ": " + ex.Message;
                _logger?.Warning(
                    "Shift-click node connection failed: " + ex);
            }
        }

        internal void SelectSegment(TrackSegment segment)
        {
            if (!_editModeActive || segment == null)
                return;
            var previousNode = _selectedNode;
            var previousSegment = _selectedSegment;
            _selectedSegment = segment;
            _selectedNode = null;
            _worldNodeShortcutStatus =
                "Selected segment " + segment.id
                + ". Use Track to edit its group, gauge, style, class, "
                + "or insert a control node.";
            RefreshTrackSelectionColors(
                previousNode,
                previousSegment,
                _selectedNode,
                _selectedSegment);
        }

        private void ClearTrackSelection()
        {
            var previousNode = _selectedNode;
            var previousSegment = _selectedSegment;
            _selectedNode = null;
            _selectedSegment = null;
            RefreshTrackSelectionColors(
                previousNode,
                previousSegment,
                null,
                null);
        }

        internal void ClearAllSelections()
        {
            CancelNodeDrag();
            ClearTrackSelection();
            ClearSplineSelection();
            if (_splineTrackPickMode)
                SetSplineTrackPickMode(false);
            ClearSelectedScenery();
            ClearSelectedTelegraphPole();
            ClearSelectedMandela();
            ClearSelectedTrainSignal();
            SelectOperation(string.Empty);
            _worldNodeShortcutStatus =
                "Selection cleared. Click any editable object to continue.";
        }

        internal bool IsSelected(TrackNode node)
        {
            return node != null && _selectedNode == node;
        }

        internal bool IsSelected(TrackSegment segment)
        {
            return segment != null && _selectedSegment == segment;
        }

        internal void AddNextNode()
        {
            var start = RequireNode();
            var id = NextNodeId();
            var segmentId = NextSegmentId();
            var rotation = start.transform.localEulerAngles;
            var grade = SelectedNodeGrade();
            var position = start.transform.localPosition
                           + HorizontalForward(rotation.y) * 10f;
            position.y += 10f * grade / 100f;
            rotation.x = PitchFromGrade(grade);
            ExecuteEdit(
                "Add connected node",
                new[] { start.id, id },
                new[] { segmentId },
                () =>
                {
                    var node = CreateNodeLive(new NodeModel
                    {
                        Id = id,
                        Position = position,
                        Rotation = rotation,
                    });
                    WriteNode(node);
                    var segment = CreateSegmentLive(new SegmentModel
                    {
                        Id = segmentId,
                        A = start.id,
                        B = id,
                        GroupId = string.Empty,
                        Style = TrackSegment.Style.Standard,
                        TrackClass = TrackClass.Mainline,
                    });
                    WriteSegment(segment);
                    _selectedNode = node;
                    _selectedSegment = null;
                },
                useTargetedTrackRebuild: true);
        }

        internal void AddNodeAtCamera()
        {
            RequireSession();
            if (CameraSelector.shared == null)
                throw new InvalidOperationException("Railroader's camera is not ready.");
            var worldPosition = CameraSelector.shared.CurrentCameraGroundPosition;
            var gamePosition = WorldTransformer.WorldToGame(worldPosition)
                               + Vector3.up * 0.2f;
            var yaw = Camera.main == null
                ? 0f
                : Camera.main.transform.eulerAngles.y;
            AddNodeAtPosition(gamePosition, yaw, false);
        }

        internal string AddNodeAtPosition(
            Vector3 gamePosition,
            float yaw,
            bool connectFromSelected)
        {
            RequireSession();
            ValidateVector(gamePosition, "track node position");
            var start = connectFromSelected ? RequireNode() : null;
            var id = NextNodeId();
            var segmentId = connectFromSelected
                ? NextSegmentId()
                : string.Empty;
            var rotation = new Vector3(0f, yaw, 0f);
            if (start != null)
            {
                var delta = gamePosition
                            - start.transform.localPosition;
                var horizontal = new Vector2(delta.x, delta.z);
                if (horizontal.sqrMagnitude > 0.0001f)
                {
                    rotation.y = Mathf.Atan2(delta.x, delta.z)
                                 * Mathf.Rad2Deg;
                    rotation.x = PitchFromGrade(
                        delta.y / horizontal.magnitude * 100f);
                }
            }
            ExecuteEdit(
                connectFromSelected
                    ? "Place connected node"
                    : "Place free node",
                start == null
                    ? new[] { id }
                    : new[] { start.id, id },
                connectFromSelected
                    ? new[] { segmentId }
                    : Array.Empty<string>(),
                () =>
                {
                    var node = CreateNodeLive(new NodeModel
                    {
                        Id = id,
                        Position = gamePosition,
                        Rotation = rotation,
                    });
                    WriteNode(node);
                    if (start != null)
                    {
                        var segment = CreateSegmentLive(
                            new SegmentModel
                            {
                                Id = segmentId,
                                A = start.id,
                                B = id,
                                GroupId = string.Empty,
                                Style = TrackSegment.Style.Standard,
                                TrackClass = TrackClass.Mainline,
                            });
                        WriteSegment(segment);
                    }
                    _selectedNode = node;
                    _selectedSegment = null;
                },
                useTargetedTrackRebuild: true);
            return start == null
                ? "Placed free node " + id
                : "Placed node " + id + " connected from "
                  + start.id;
        }

        internal void SetConnectStart(out string nodeId)
        {
            nodeId = RequireNode().id;
        }

        internal void ConnectFrom(string startNodeId)
        {
            var end = RequireNode();
            var start = _graph.GetNode(startNodeId);
            if (start == null)
                throw new InvalidOperationException("The saved connect-start node no longer exists.");
            if (start == end)
                throw new InvalidOperationException("Select a different end node.");
            if (_graph.Segments.Any(segment =>
                    (segment.a == start && segment.b == end)
                    || (segment.a == end && segment.b == start)))
            {
                throw new InvalidOperationException("Those nodes are already connected.");
            }

            var segmentId = NextSegmentId();
            ExecuteEdit(
                "Connect nodes",
                new[] { start.id, end.id },
                new[] { segmentId },
                () =>
                {
                    var segment = CreateSegmentLive(new SegmentModel
                    {
                        Id = segmentId,
                        A = start.id,
                        B = end.id,
                        GroupId = string.Empty,
                        Style = TrackSegment.Style.Standard,
                        TrackClass = TrackClass.Mainline,
                    });
                    WriteSegment(segment);
                },
                useTargetedTrackRebuild: true);
        }

        internal void InjectSelectedSegment()
        {
            InjectSelectedSegmentAtParameter(0.5f);
        }

        internal string InjectSelectedSegmentAtPosition(
            Vector3 gamePosition)
        {
            var original = RequireSegment();
            ValidateVector(gamePosition, "track insertion position");
            var t = ClosestCurveParameter(
                original.Curve,
                gamePosition);
            if (t <= 0.01f || t >= 0.99f)
            {
                throw new InvalidOperationException(
                    "Click farther from the segment endpoint. Use the "
                    + "existing endpoint node when working at the end of track.");
            }
            InjectSelectedSegmentAtParameter(t);
            return "Inserted node at "
                   + (t * 100f).ToString(
                       "0.#",
                       CultureInfo.InvariantCulture)
                   + "% of the selected segment";
        }

        private void InjectSelectedSegmentAtParameter(float parameter)
        {
            var original = RequireSegment();
            var originalModel = CaptureSegment(original);
            var curve = original.Curve;
            var t = Mathf.Clamp01(parameter);
            var nodeId = NextNodeId();
            var firstId = NextSegmentId();
            var secondId = NextSegmentId();

            ExecuteEdit(
                "Inject node",
                new[]
                {
                    original.a.id,
                    original.b.id,
                    nodeId,
                },
                new[] { original.id, firstId, secondId },
                () =>
                {
                    RemoveSegmentLive(original.id);
                    WriteSegmentDeletion(original.id);
                    var node = CreateNodeLive(new NodeModel
                    {
                        Id = nodeId,
                        Position = curve.GetPoint(t),
                        Rotation = curve.GetRotation(t).eulerAngles,
                    });
                    WriteNode(node);
                    var first = CreateSegmentLive(CopySegment(
                        originalModel, firstId, originalModel.A, nodeId));
                    var second = CreateSegmentLive(CopySegment(
                        originalModel, secondId, nodeId, originalModel.B));
                    WriteSegment(first);
                    WriteSegment(second);
                    _selectedNode = node;
                    _selectedSegment = null;
                },
                useTargetedTrackRebuild: true);
        }

        private static float ClosestCurveParameter(
            BezierCurve curve,
            Vector3 position)
        {
            const int samples = 80;
            var bestT = 0f;
            var bestDistance = float.PositiveInfinity;
            for (var index = 0; index <= samples; index++)
            {
                var t = index / (float)samples;
                var distance = (curve.GetPoint(t) - position).sqrMagnitude;
                if (distance >= bestDistance)
                    continue;
                bestDistance = distance;
                bestT = t;
            }

            var radius = 1f / samples;
            var left = Mathf.Max(0f, bestT - radius);
            var right = Mathf.Min(1f, bestT + radius);
            for (var pass = 0; pass < 8; pass++)
            {
                var first = Mathf.Lerp(left, right, 1f / 3f);
                var second = Mathf.Lerp(left, right, 2f / 3f);
                var firstDistance =
                    (curve.GetPoint(first) - position).sqrMagnitude;
                var secondDistance =
                    (curve.GetPoint(second) - position).sqrMagnitude;
                if (firstDistance <= secondDistance)
                    right = second;
                else
                    left = first;
            }
            return (left + right) * 0.5f;
        }

        internal void SplitSelectedNode()
        {
            var node = RequireNode();
            var connected = _graph.SegmentsConnectedTo(node).ToList();
            if (connected.Count < 2)
                throw new InvalidOperationException(
                    "Split requires a node with at least two connected segments.");

            var removed = connected.Skip(1).ToList();
            var newNodeIds = removed.Select(_ => NextNodeId()).ToArray();
            var newSegmentIds = removed.Select(_ => NextSegmentId()).ToArray();
            var affectedSegments = removed.Select(segment => segment.id)
                .Concat(newSegmentIds)
                .ToArray();

            ExecuteEdit(
                "Split junction node",
                new[] { node.id }
                    .Concat(newNodeIds),
                affectedSegments,
                () =>
                {
                    for (var index = 0; index < removed.Count; index++)
                    {
                        var old = removed[index];
                        var model = CaptureSegment(old);
                        var other = old.GetOtherNode(node);
                        var direction = (other.transform.localPosition
                                         - node.transform.localPosition).normalized;
                        RemoveSegmentLive(old.id);
                        WriteSegmentDeletion(old.id);
                        var detached = CreateNodeLive(new NodeModel
                        {
                            Id = newNodeIds[index],
                            Position = node.transform.localPosition + direction * 3f,
                            Rotation = node.transform.localEulerAngles,
                        });
                        WriteNode(detached);
                        var replacement = CreateSegmentLive(CopySegment(
                            model,
                            newSegmentIds[index],
                            other.id,
                            detached.id));
                        WriteSegment(replacement);
                    }
                },
                useTargetedTrackRebuild: true);
        }

        internal void LevelSelectedNode()
        {
            EditSelectedNodeTransform(
                "Level node",
                node =>
                {
                    var rotation = node.transform.localEulerAngles;
                    node.transform.localEulerAngles = new Vector3(0f, rotation.y, 0f);
                });
        }

        internal void FlipSelectedNode()
        {
            EditSelectedNodeTransform(
                "Flip node",
                node =>
                {
                    node.transform.localEulerAngles += Vector3.up * 180f;
                });
        }

        internal void ToggleSelectedSwitchStand()
        {
            var node = RequireNode();
            if (_graph.SegmentsConnectedTo(node).Count() < 3)
            {
                throw new InvalidOperationException(
                    "A turnout stand requires a junction with at least "
                    + "three connected track segments.");
            }
            EditSelectedNodeTransform(
                "Flip turnout stand",
                selected => selected.flipSwitchStand =
                    !selected.flipSwitchStand);
        }

        internal bool SelectedNodeBumperEnabled =>
            _selectedNode == null
            || TileEditorTrackOverrides.BumperEnabled(_selectedNode.id);

        internal void ToggleSelectedTrackBumper()
        {
            var node = RequireNode();
            if (!_graph.NodeIsDeadEnd(node, out _))
            {
                throw new InvalidOperationException(
                    "Track bumpers can only be changed at a dead-end node.");
            }
            var enabled = !TileEditorTrackOverrides.BumperEnabled(node.id);
            TileEditorTrackOverrides.SetBumperEnabled(node.id, enabled);
            TrackObjectManager.Instance?.SetNeedsRebuild(node);
        }

        internal void MoveSelectedNode(Vector3 offset, bool localAxes)
        {
            ValidateVector(offset, "movement offset");
            EditSelectedNodeTransform(
                "Move node",
                node =>
                {
                    var appliedOffset = localAxes
                        ? Quaternion.Euler(
                            node.transform.localEulerAngles) * offset
                        : offset;
                    node.transform.localPosition += appliedOffset;
                });
        }

        internal void RotateSelectedNode(Vector3 offset)
        {
            ValidateVector(offset, "rotation offset");
            EditSelectedNodeTransform(
                "Rotate node",
                node => node.transform.localEulerAngles += offset);
        }

        internal void SetSelectedNodeTransform(Vector3 position, Vector3 rotation)
        {
            ValidateVector(position, "node position");
            ValidateVector(rotation, "node rotation");
            EditSelectedNodeTransform(
                "Set node transform",
                node =>
                {
                    node.transform.localPosition = position;
                    node.transform.localEulerAngles = rotation;
                });
        }

        internal void PasteSelectedNodeProperties(
            float elevation,
            Vector3 rotation,
            bool flipSwitchStand,
            NodePropertyFields fields)
        {
            if (fields == NodePropertyFields.None)
            {
                throw new InvalidOperationException(
                    "Choose at least one node property to paste.");
            }
            ValidateVector(
                new Vector3(0f, elevation, 0f),
                "copied node elevation");
            ValidateVector(rotation, "copied node rotation");
            EditSelectedNodeTransform(
                "Paste node properties",
                node =>
                {
                    if ((fields & NodePropertyFields.Elevation) != 0)
                    {
                        var position = node.transform.localPosition;
                        position.y = elevation;
                        node.transform.localPosition = position;
                    }

                    var targetRotation =
                        node.transform.localEulerAngles;
                    if ((fields & NodePropertyFields.Grade) != 0)
                        targetRotation.x = rotation.x;
                    if ((fields & NodePropertyFields.Heading) != 0)
                        targetRotation.y = rotation.y;
                    if ((fields & NodePropertyFields.Bank) != 0)
                        targetRotation.z = rotation.z;
                    node.transform.localEulerAngles =
                        targetRotation;

                    if ((fields & NodePropertyFields.SwitchStand) != 0)
                        node.flipSwitchStand = flipSwitchStand;
                });
        }

        internal void ResetSelectedNodeRotation()
        {
            EditSelectedNodeTransform(
                "Reset node rotation",
                node => node.transform.localEulerAngles = Vector3.zero);
        }

        private void EditSelectedNodeTransform(
            string name,
            Action<TrackNode> mutation)
        {
            var node = RequireNode();
            var connectedSegmentIds = _graph.SegmentsConnectedTo(node)
                .Select(segment => segment.id)
                .ToArray();
            ExecuteEdit(
                name,
                new[] { node.id },
                connectedSegmentIds,
                () =>
                {
                    mutation(node);
                    WriteNode(node);
                },
                useLightweightTrackUpdate: true);
        }

        internal void DeleteSelectedNode(bool reconnect)
        {
            var node = RequireNode();
            var connected = _graph.SegmentsConnectedTo(node).ToList();
            var nodeId = node.id;
            var segmentIds = connected.Select(segment => segment.id).ToArray();
            ExecuteEdit(
                "Delete node",
                new[] { nodeId },
                segmentIds,
                () =>
                {
                    foreach (var segmentId in segmentIds)
                    {
                        RemoveSegmentLive(segmentId);
                        WriteSegmentDeletion(segmentId);
                    }
                    RemoveNodeLive(nodeId);
                    WriteNodeDeletion(nodeId);
                    _selectedNode = null;
                });
        }

        internal void DeleteSelectedSegment()
        {
            var segment = RequireSegment();
            var id = segment.id;
            ExecuteEdit(
                "Delete segment",
                Array.Empty<string>(),
                new[] { id },
                () =>
                {
                    RemoveSegmentLive(id);
                    WriteSegmentDeletion(id);
                    _selectedSegment = null;
                });
        }

        internal void SetSelectedSegmentStyle(int style)
        {
            var segment = RequireSegment();
            var id = segment.id;
            ExecuteEdit(
                "Change segment style",
                new[] { segment.a.id, segment.b.id },
                new[] { id },
                () =>
                {
                    segment.style = (TrackSegment.Style)style;
                    segment.InvalidateCurve();
                    WriteSegment(segment);
                },
                useTargetedTrackRebuild: true);
        }

        internal void SetSelectedSegmentTrackClass(string trackClass)
        {
            TrackClass normalized;
            switch ((trackClass ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "main":
                case "mainline":
                    normalized = TrackClass.Mainline;
                    break;
                case "branch":
                    normalized = TrackClass.Branch;
                    break;
                case "industrial":
                    normalized = TrackClass.Industrial;
                    break;
                default:
                    throw new InvalidOperationException(
                        "Track class must be Mainline, Branch, or Industrial.");
            }

            var segment = RequireSegment();
            if (segment.trackClass == normalized)
                return;
            var id = segment.id;
            ExecuteEdit(
                "Change segment track class",
                new[] { segment.a.id, segment.b.id },
                new[] { id },
                () =>
                {
                    segment.trackClass = normalized;
                    segment.InvalidateCurve();
                    WriteSegment(segment);
                },
                useTargetedTrackRebuild: true);
        }

        internal void SetSelectedSegmentGroup(string groupId)
        {
            var segment = RequireSegment();
            var normalized = (groupId ?? string.Empty).Trim();
            if (normalized.Any(char.IsControl))
            {
                throw new InvalidOperationException(
                    "Segment group IDs cannot contain control characters.");
            }
            if (string.Equals(
                    segment.groupId ?? string.Empty,
                    normalized,
                    StringComparison.Ordinal))
            {
                return;
            }
            var id = segment.id;
            ExecuteEdit(
                string.IsNullOrWhiteSpace(normalized)
                    ? "Clear segment group"
                    : "Change segment group",
                new[] { segment.a.id, segment.b.id },
                new[] { id },
                () =>
                {
                    segment.groupId = normalized;
                    WriteSegment(segment);
                },
                useTargetedTrackRebuild: true);
        }

        internal string RenameSelectedSegment(string requestedId)
        {
            var segment = RequireSegment();
            var oldId = segment.id;
            var newId = (requestedId ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(newId))
            {
                throw new InvalidOperationException(
                    "Segment ID cannot be blank.");
            }
            if (newId.Length > 80
                || newId.Any(character =>
                    char.IsControl(character)
                    || char.IsWhiteSpace(character)
                    || character == '/'
                    || character == '\\'
                    || character == '|'))
            {
                throw new InvalidOperationException(
                    "Segment IDs must be 80 characters or fewer and cannot "
                    + "contain spaces, control characters, /, \\, or |.");
            }
            if (string.Equals(oldId, newId, StringComparison.Ordinal))
                return oldId;
            if (_graph.GetSegment(newId) != null
                || SegmentsObject.Property(
                    newId,
                    StringComparison.OrdinalIgnoreCase) != null)
            {
                throw new InvalidOperationException(
                    "A segment named " + newId + " already exists.");
            }
            if (IsGeneratedNarrowGaugeId(oldId))
            {
                throw new InvalidOperationException(
                    "Generated narrow-gauge ghost segments inherit their "
                    + "ID from a source segment and cannot be renamed directly.");
            }

            var model = CaptureSegment(segment);
            model.Id = newId;
            ExecuteEdit(
                "Rename segment " + oldId + " to " + newId,
                new[] { segment.a.id, segment.b.id },
                new[] { oldId, newId },
                () =>
                {
                    RenameSegmentReferencesInGraphDocument(oldId, newId);
                    RemoveSegmentLive(oldId);
                    WriteSegmentDeletion(oldId);
                    var renamed = CreateSegmentLive(model);
                    WriteSegment(renamed);
                    _selectedSegment = renamed;
                    _selectedNode = null;
                });
            return newId;
        }

        private void RenameSegmentReferencesInGraphDocument(
            string oldId,
            string newId)
        {
            foreach (var property in _document.Properties().ToArray())
            {
                if (string.Equals(
                        property.Name,
                        "tracks",
                        StringComparison.OrdinalIgnoreCase)
                    && property.Value is JObject tracks)
                {
                    foreach (var trackProperty in
                             tracks.Properties().ToArray())
                    {
                        if (string.Equals(
                                trackProperty.Name,
                                "segments",
                                StringComparison.OrdinalIgnoreCase)
                            || string.Equals(
                                trackProperty.Name,
                                "removals",
                                StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        ReplaceSegmentReferenceValues(
                            trackProperty.Value,
                            oldId,
                            newId,
                            trackProperty.Name.IndexOf(
                                "segment",
                                StringComparison.OrdinalIgnoreCase) >= 0);
                    }
                    continue;
                }
                ReplaceSegmentReferenceValues(
                    property.Value,
                    oldId,
                    newId,
                    property.Name.IndexOf(
                        "segment",
                        StringComparison.OrdinalIgnoreCase) >= 0);
            }
        }

        private static void ReplaceSegmentReferenceValues(
            JToken token,
            string oldId,
            string newId,
            bool segmentContext)
        {
            if (token is JObject objectToken)
            {
                foreach (var property in objectToken.Properties().ToArray())
                {
                    ReplaceSegmentReferenceValues(
                        property.Value,
                        oldId,
                        newId,
                        segmentContext
                        || property.Name.IndexOf(
                            "segment",
                            StringComparison.OrdinalIgnoreCase) >= 0);
                }
                return;
            }
            if (token is JArray array)
            {
                foreach (var child in array.ToArray())
                {
                    ReplaceSegmentReferenceValues(
                        child,
                        oldId,
                        newId,
                        segmentContext);
                }
                return;
            }
            if (!segmentContext
                || token.Type != JTokenType.String
                || !string.Equals(
                    token.Value<string>(),
                    oldId,
                    StringComparison.Ordinal))
            {
                return;
            }
            token.Replace(new JValue(newId));
        }

        internal void SetSelectedSegmentGauge(string gauge)
        {
            var segment = RequireSegment();
            var normalized = NormalizeTrackGauge(gauge);
            if (IsGeneratedNarrowGaugeId(segment.id))
            {
                throw new InvalidOperationException(
                    "Generated narrow-gauge ghost routes inherit from their "
                    + "dual-gauge source segment and cannot be edited directly.");
            }
            if (string.Equals(
                    GetSegmentGauge(segment.id),
                    normalized,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            var id = segment.id;
            ExecuteEdit(
                "Change segment gauge",
                new[] { segment.a.id, segment.b.id },
                new[] { id },
                () =>
                {
                    _segmentGauges[id] = normalized;
                    WriteSegment(segment);
                },
                useTargetedTrackRebuild: true);
        }

        internal int SelectedGaugeChainCount()
        {
            return _selectedSegment == null
                ? 0
                : CollectThroughChain(_selectedSegment).Count;
        }

        internal void SetSelectedGaugeThroughChain(string gauge)
        {
            var start = RequireSegment();
            var normalized = NormalizeTrackGauge(gauge);
            if (IsDualGaugeTransition(normalized))
            {
                throw new InvalidOperationException(
                    "DUAL T is a single shared-rail transition segment. "
                    + "Apply it only to the short segment between a DUAL L "
                    + "run and a DUAL R run.");
            }
            var chain = CollectThroughChain(start)
                .Where(segment =>
                    !IsGeneratedNarrowGaugeId(segment.id))
                .ToArray();
            if (chain.Length == 0)
                return;
            var ids = chain.Select(segment => segment.id).ToArray();
            var nodeIds = chain
                .SelectMany(segment =>
                    new[] { segment.a?.id, segment.b?.id })
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct()
                .ToArray();
            ExecuteEdit(
                "Change connected track gauge",
                nodeIds,
                ids,
                () =>
                {
                    foreach (var segment in chain)
                    {
                        _segmentGauges[segment.id] = normalized;
                        WriteSegment(segment);
                    }
                },
                useTargetedTrackRebuild: true);
        }

        internal void ShowSelected()
        {
            var point = _selectedNode != null
                ? _selectedNode.transform.localPosition
                : _selectedSegment != null
                    ? (_selectedSegment.a.transform.localPosition
                       + _selectedSegment.b.transform.localPosition) * 0.5f
                    : throw new InvalidOperationException(
                        "Click a cyan node or yellow segment first.");
            if (CameraSelector.shared == null)
                throw new InvalidOperationException("Railroader's camera is not ready.");
            CameraSelector.shared.ZoomToPoint(point);
        }

        internal void Undo()
        {
            if (_undo.Count == 0)
                return;
            var edit = _undo.Pop();
            Restore(edit, after: false);
            SyncWaterSurfacesAfterDocumentRestore();
            if (edit.BeforeToolshedFacilities != null
                || edit.AfterToolshedFacilities != null)
            {
                _toolshedFacilitiesDirty = true;
            }
            _redo.Push(edit);
            _dirty = true;
        }

        internal void Redo()
        {
            if (_redo.Count == 0)
                return;
            var edit = _redo.Pop();
            Restore(edit, after: true);
            SyncWaterSurfacesAfterDocumentRestore();
            if (edit.BeforeToolshedFacilities != null
                || edit.AfterToolshedFacilities != null)
            {
                _toolshedFacilitiesDirty = true;
            }
            _undo.Push(edit);
            _dirty = true;
        }

        internal void Save()
        {
            RequireSession();
            if (string.IsNullOrWhiteSpace(_backupPath) && File.Exists(_graphPath))
            {
                _backupPath = _graphPath + ".tile-editor-backup-"
                              + DateTime.Now.ToString("yyyyMMdd-HHmmss",
                                  CultureInfo.InvariantCulture);
                File.Copy(_graphPath, _backupPath, false);
                TileEditorBackupRetention.PruneFor(_graphPath);
            }

            var temp = _graphPath + ".tile-editor.tmp";
            File.WriteAllText(temp, _document.ToString(Formatting.Indented));
            if (File.Exists(_graphPath))
            {
                try
                {
                    File.Replace(temp, _graphPath, null);
                }
                catch
                {
                    File.Delete(_graphPath);
                    File.Move(temp, _graphPath);
                }
            }
            else
            {
                File.Move(temp, _graphPath);
            }
            SaveToolshedFacilities();
            _dirty = false;
            _logger?.Log("Tile Editor saved graph edit layer: " + _graphPath);
        }

        internal string ReloadGraphFromDesktop(string path)
        {
            if (string.IsNullOrWhiteSpace(path)
                || !File.Exists(path))
            {
                throw new FileNotFoundException(
                    "The desktop graph file was not found.",
                    path);
            }
            var fullPath = Path.GetFullPath(path);
            var conflicts = 0;
            if (SplineyDirty)
                conflicts += PreserveDirtySplineyConflicts();
            if (GraphOpen
                && string.Equals(
                    fullPath,
                    _graphPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                if (_dirty && _document != null)
                {
                    var conflict = _graphPath
                                   + ".game-conflict-"
                                   + DateTime.Now.ToString(
                                       "yyyyMMdd-HHmmss",
                                       CultureInfo.InvariantCulture)
                                   + ".json";
                    File.WriteAllText(
                        conflict,
                        _document.ToString(
                            Formatting.Indented));
                    conflicts++;
                }
                OpenGraph(fullPath);
            }
            else
            {
                ResetSplineySources();
                ResetScenerySession();
                ResetMandelaSession();
                ResetOperationsSession();
                ResetTrainSignalSession();
                ResetCtcSession();
            }
            return "Desktop content reloaded"
                   + (conflicts == 0
                       ? string.Empty
                       : "; preserved in-game conflict copy");
        }

        internal void RebuildTrack()
        {
            RequireSession();
            var nodeIds = _deferredTrackNodeIds.ToArray();
            var segmentIds = _deferredTrackSegmentIds.ToArray();
            var targeted = !_deferredFullTrackRebuildPending
                           && (nodeIds.Length > 0
                               || segmentIds.Length > 0);
            RebuildLiveGraph(
                nodeIds,
                segmentIds,
                rebuildAllOverlays: true,
                useTargetedTrackRebuild: targeted,
                forceRuntimeTrackRebuild: true);
            if (_narrowGaugeFullSyncPending)
                RequestNarrowGaugeSynchronization();
            ClearDeferredTrackRebuildState();
        }

        internal float SelectedNodeGrade()
        {
            var pitch = NormalizeSignedAngle(
                RequireNode().transform.localEulerAngles.x);
            return -Mathf.Tan(pitch * Mathf.Deg2Rad) * 100f;
        }

        internal string BuildStraightPiece(
            float length,
            float targetGrade,
            int sections)
        {
            var start = RequireNode();
            ValidateLength(length);
            ValidateGrade(targetGrade);
            sections = Mathf.Clamp(sections, 1, 32);

            var position = start.transform.localPosition;
            var rotation = start.transform.localEulerAngles;
            var step = length / sections;
            var points = new List<PathPoint>(sections);
            for (var index = 0; index < sections; index++)
            {
                position += HorizontalForward(rotation.y) * step;
                position.y += step * targetGrade / 100f;
                points.Add(new PathPoint(
                    position,
                    new Vector3(
                        PitchFromGrade(targetGrade),
                        rotation.y,
                        rotation.z)));
            }
            return CommitPath(start.id, points);
        }

        internal string BuildParallelSelectedSegment(
            float separation,
            int trackCount,
            int side)
        {
            var source = RequireSegment();
            if (separation < 0.1f || separation > 100f)
                throw new InvalidOperationException(
                    "Separation must be between 0.1 and 100 m.");
            trackCount = Mathf.Clamp(trackCount, 1, 10);
            side = Mathf.Clamp(side, 0, 2);

            var offsets = new List<float>();
            for (var index = 1; index <= trackCount; index++)
            {
                if (side == 0 || side == 2)
                    offsets.Add(-separation * index);
                if (side == 1 || side == 2)
                    offsets.Add(separation * index);
            }

            var nodeIds = offsets
                .SelectMany(_ => new[] { NextNodeId(), NextNodeId() })
                .ToArray();
            var segmentIds = offsets.Select(_ => NextSegmentId()).ToArray();
            var curve = source.Curve;
            var rightA = curve.GetRotation(0f) * Vector3.right;
            var rightB = curve.GetRotation(1f) * Vector3.right;

            ExecuteEdit(
                "Build parallel track",
                nodeIds,
                segmentIds,
                () =>
                {
                    for (var index = 0; index < offsets.Count; index++)
                    {
                        var offset = offsets[index];
                        var aId = nodeIds[index * 2];
                        var bId = nodeIds[index * 2 + 1];
                        var aNode = CreateNodeLive(new NodeModel
                        {
                            Id = aId,
                            Position = source.a.transform.localPosition
                                       + rightA * offset,
                            Rotation = source.a.transform.localEulerAngles,
                            FlipSwitchStand = source.a.flipSwitchStand,
                        });
                        var bNode = CreateNodeLive(new NodeModel
                        {
                            Id = bId,
                            Position = source.b.transform.localPosition
                                       + rightB * offset,
                            Rotation = source.b.transform.localEulerAngles,
                            FlipSwitchStand = source.b.flipSwitchStand,
                        });
                        WriteNode(aNode);
                        WriteNode(bNode);
                        var segment = CreateSegmentLive(new SegmentModel
                        {
                            Id = segmentIds[index],
                            A = aId,
                            B = bId,
                            Priority = source.priority,
                            SpeedLimit = source.speedLimit,
                            GroupId = source.groupId ?? string.Empty,
                            Style = source.style,
                            TrackClass = source.trackClass,
                        });
                        WriteSegment(segment);
                    }
                    _selectedSegment = source;
                    _selectedNode = null;
                },
                useTargetedTrackRebuild: true);
            return offsets.Count + " parallel track"
                   + (offsets.Count == 1 ? string.Empty : "s");
        }

        internal string FitArcToNodes(IList<string> orderedNodeIds)
        {
            RequireSession();
            if (orderedNodeIds == null || orderedNodeIds.Count < 3)
                throw new InvalidOperationException(
                    "Fit Arc needs at least three nodes in route order.");

            var ids = orderedNodeIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct()
                .ToArray();
            if (ids.Length < 3)
                throw new InvalidOperationException(
                    "Fit Arc needs at least three different nodes.");
            var nodes = ids.Select(id => _graph.GetNode(id)).ToArray();
            if (nodes.Any(node => node == null))
                throw new InvalidOperationException(
                    "One or more Fit Arc nodes no longer exist.");

            for (var index = 0; index < nodes.Length; index++)
            {
                if (_graph.SegmentsConnectedTo(nodes[index]).Count() > 2)
                {
                    throw new InvalidOperationException(
                        "Fit Arc cannot reshape a turnout or junction node.");
                }
                if (index == 0)
                    continue;
                var previous = nodes[index - 1];
                var current = nodes[index];
                if (!_graph.Segments.Any(segment =>
                        (segment.a == previous && segment.b == current)
                        || (segment.a == current && segment.b == previous)))
                {
                    throw new InvalidOperationException(
                        "Fit Arc nodes must form one connected route in order.");
                }
            }

            var count = nodes.Length;
            var meanX = nodes.Average(node => (double)node.transform.localPosition.x);
            var meanZ = nodes.Average(node => (double)node.transform.localPosition.z);
            var shifted = nodes.Select(node => new Vector2(
                    node.transform.localPosition.x - (float)meanX,
                    node.transform.localPosition.z - (float)meanZ))
                .ToArray();
            double suu = 0d;
            double svv = 0d;
            double suv = 0d;
            double suuu = 0d;
            double svvv = 0d;
            double suvv = 0d;
            double svuu = 0d;
            foreach (var point in shifted)
            {
                var u = (double)point.x;
                var v = (double)point.y;
                suu += u * u;
                svv += v * v;
                suv += u * v;
                suuu += u * u * u;
                svvv += v * v * v;
                suvv += u * v * v;
                svuu += v * u * u;
            }
            var determinant = suu * svv - suv * suv;
            if (Math.Abs(determinant) < 1e-9)
                throw new InvalidOperationException(
                    "Fit Arc could not solve a stable circle from those nodes.");
            var rhsU = 0.5d * (suuu + suvv);
            var rhsV = 0.5d * (svvv + svuu);
            var centerX = meanX + (rhsU * svv - rhsV * suv) / determinant;
            var centerZ = meanZ + (rhsV * suu - rhsU * suv) / determinant;
            var radii = nodes.Select(node =>
            {
                var position = node.transform.localPosition;
                var dx = position.x - centerX;
                var dz = position.z - centerZ;
                return Math.Sqrt(dx * dx + dz * dz);
            }).ToArray();
            var radius = radii.Average();
            if (radius < 0.1d || radius > 100000d)
                throw new InvalidOperationException(
                    "Fit Arc solved an invalid radius.");
            var rms = Math.Sqrt(radii.Sum(value =>
                (value - radius) * (value - radius)) / count);

            double signedTurn = 0d;
            for (var index = 1; index < count - 1; index++)
            {
                var a = nodes[index - 1].transform.localPosition;
                var b = nodes[index].transform.localPosition;
                var c = nodes[index + 1].transform.localPosition;
                signedTurn += (b.x - a.x) * (c.z - b.z)
                              - (b.z - a.z) * (c.x - b.x);
            }
            var turnSign = signedTurn >= 0d ? 1d : -1d;
            var angles = new double[count];
            for (var index = 0; index < count; index++)
            {
                var position = nodes[index].transform.localPosition;
                var angle = Math.Atan2(
                    position.z - centerZ,
                    position.x - centerX);
                if (index > 0)
                {
                    var previous = angles[index - 1];
                    while (angle - previous > Math.PI)
                        angle -= Math.PI * 2d;
                    while (angle - previous < -Math.PI)
                        angle += Math.PI * 2d;
                    if (turnSign >= 0d && angle < previous)
                        angle += Math.PI * 2d;
                    else if (turnSign < 0d && angle > previous)
                        angle -= Math.PI * 2d;
                }
                angles[index] = angle;
            }

            var stations = new double[count];
            for (var index = 1; index < count; index++)
            {
                stations[index] = stations[index - 1]
                                  + Vector3.Distance(
                                      nodes[index - 1].transform.localPosition,
                                      nodes[index].transform.localPosition);
            }
            var totalStation = stations[count - 1];
            if (totalStation < 0.1d)
                throw new InvalidOperationException(
                    "Fit Arc source route is too short.");

            var affectedSegments = ids
                .SelectMany(id => _graph.SegmentsConnectedTo(_graph.GetNode(id)))
                .Select(segment => segment.id)
                .Distinct()
                .ToArray();
            var startAngle = angles[0];
            var deltaAngle = angles[count - 1] - startAngle;
            ExecuteEdit(
                "Fit nodes to circular arc",
                ids,
                affectedSegments,
                () =>
                {
                    for (var index = 0; index < count; index++)
                    {
                        var t = stations[index] / totalStation;
                        var angle = startAngle + deltaAngle * t;
                        var x = centerX + radius * Math.Cos(angle);
                        var z = centerZ + radius * Math.Sin(angle);
                        var tangentX = -Math.Sin(angle)
                                       * (deltaAngle >= 0d ? 1d : -1d);
                        var tangentZ = Math.Cos(angle)
                                       * (deltaAngle >= 0d ? 1d : -1d);
                        var yaw = Math.Atan2(tangentX, tangentZ)
                                  * Mathf.Rad2Deg;
                        var node = nodes[index];
                        var position = node.transform.localPosition;
                        var rotation = node.transform.localEulerAngles;
                        node.transform.localPosition = new Vector3(
                            (float)x,
                            position.y,
                            (float)z);
                        node.transform.localEulerAngles = new Vector3(
                            rotation.x,
                            (float)yaw,
                            rotation.z);
                        _graph.OnNodeDidChange(node);
                        WriteNode(node);
                    }
                    _selectedNode = nodes[nodes.Length - 1];
                    _selectedSegment = null;
                });
            return string.Format(
                CultureInfo.InvariantCulture,
                "R {0:F1} m, angle {1:F1} deg, RMS {2:F2} m",
                radius,
                deltaAngle * 180d / Math.PI,
                rms);
        }

        internal string BuildGradeTransition(float length, float targetGrade, int steps)
        {
            var start = RequireNode();
            ValidateLength(length);
            ValidateGrade(targetGrade);
            steps = Mathf.Clamp(steps, 2, 32);

            var startGrade = GradeFromRotation(start.transform.localEulerAngles);
            var points = new List<PathPoint>(steps);
            var position = start.transform.localPosition;
            var yaw = start.transform.localEulerAngles.y;
            var horizontalStep = length / steps;
            for (var index = 1; index <= steps; index++)
            {
                var t0 = (index - 1f) / steps;
                var t1 = index / (float)steps;
                var midGrade = Mathf.Lerp(
                    startGrade, targetGrade, SmoothStep((t0 + t1) * 0.5f));
                var endGrade = Mathf.Lerp(startGrade, targetGrade, SmoothStep(t1));
                position += HorizontalForward(yaw) * horizontalStep;
                position.y += horizontalStep * midGrade / 100f;
                points.Add(new PathPoint(
                    position,
                    new Vector3(PitchFromGrade(endGrade), yaw, 0f)));
            }
            return CommitPath(start.id, points);
        }

        internal void ReadGradeChainEndpointGrades(
            IList<string> orderedNodeIds,
            out float startGrade,
            out float endGrade)
        {
            var nodes = ResolveGradeChain(
                orderedNodeIds,
                2);
            startGrade = GradeBetween(
                nodes[0],
                nodes[1]);
            endGrade = GradeBetween(
                nodes[nodes.Length - 2],
                nodes[nodes.Length - 1]);
        }

        internal string SmoothExistingGradeChain(
            IList<string> orderedNodeIds,
            float startGrade,
            float endGrade)
        {
            RequireSession();
            ValidateGrade(startGrade);
            ValidateGrade(endGrade);
            var nodes = ResolveGradeChain(
                orderedNodeIds,
                3);
            var stations = new float[nodes.Length];
            for (var index = 1; index < nodes.Length; index++)
            {
                var delta = nodes[index].transform.localPosition
                            - nodes[index - 1].transform.localPosition;
                var run = Mathf.Sqrt(
                    delta.x * delta.x + delta.z * delta.z);
                if (run < 0.05f)
                {
                    throw new InvalidOperationException(
                        "Grade-chain nodes "
                        + nodes[index - 1].id + " and "
                        + nodes[index].id
                        + " have no usable horizontal separation.");
                }
                stations[index] = stations[index - 1] + run;
            }

            var length = stations[stations.Length - 1];
            var startY = nodes[0].transform.localPosition.y;
            var endY = nodes[nodes.Length - 1]
                .transform.localPosition.y;
            var startSlope = startGrade / 100f;
            var endSlope = endGrade / 100f;
            var maxGrade = 0f;
            for (var sample = 0; sample <= 64; sample++)
            {
                var t = sample / 64f;
                maxGrade = Mathf.Max(
                    maxGrade,
                    Mathf.Abs(HermiteGrade(
                        t,
                        length,
                        startY,
                        endY,
                        startSlope,
                        endSlope)));
            }
            if (maxGrade > 15.0001f)
            {
                throw new InvalidOperationException(
                    "That endpoint height and grade combination creates up to "
                    + maxGrade.ToString(
                        "0.00",
                        CultureInfo.InvariantCulture)
                    + "% inside the curve. Lengthen the chain, reduce the end "
                    + "grade difference, or adjust an endpoint elevation.");
            }

            var nodeIds = nodes.Select(node => node.id).ToArray();
            var segmentIds = nodes
                .SelectMany(node => _graph.SegmentsConnectedTo(node))
                .Where(segment => segment != null)
                .Select(segment => segment.id)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            ExecuteEdit(
                "Smooth existing grade chain",
                nodeIds,
                segmentIds,
                () =>
                {
                    for (var index = 0;
                         index < nodes.Length;
                         index++)
                    {
                        var t = stations[index] / length;
                        var node = nodes[index];
                        var position = node.transform.localPosition;
                        var rotation = node.transform.localEulerAngles;
                        position.y = HermiteElevation(
                            t,
                            length,
                            startY,
                            endY,
                            startSlope,
                            endSlope);
                        rotation.x = PitchForChainNode(
                            nodes,
                            index,
                            HermiteGrade(
                                t,
                                length,
                                startY,
                                endY,
                                startSlope,
                                endSlope));
                        node.transform.localPosition = position;
                        node.transform.localEulerAngles = rotation;
                        _graph.OnNodeDidChange(node);
                        WriteNode(node);
                    }
                    _selectedNode = nodes[nodes.Length - 1];
                    _selectedSegment = null;
                },
                useTargetedTrackRebuild: true);
            return "Smoothed " + nodes.Length
                   + " nodes over "
                   + length.ToString(
                       "0.0",
                       CultureInfo.InvariantCulture)
                   + " m; maximum grade "
                   + maxGrade.ToString(
                       "0.00",
                       CultureInfo.InvariantCulture)
                   + "%";
        }

        private TrackNode[] ResolveGradeChain(
            IList<string> orderedNodeIds,
            int minimumCount)
        {
            if (orderedNodeIds == null
                || orderedNodeIds.Count < minimumCount)
            {
                throw new InvalidOperationException(
                    "The grade chain needs at least "
                    + minimumCount + " nodes.");
            }
            var ids = orderedNodeIds
                .Select(id => (id ?? string.Empty).Trim())
                .ToArray();
            if (ids.Any(string.IsNullOrWhiteSpace)
                || ids.Distinct(StringComparer.OrdinalIgnoreCase).Count()
                   != ids.Length)
            {
                throw new InvalidOperationException(
                    "The grade chain contains an empty or duplicate node.");
            }
            var nodes = ids
                .Select(id => _graph.GetNode(id))
                .ToArray();
            if (nodes.Any(node => node == null))
            {
                throw new InvalidOperationException(
                    "One or more grade-chain nodes no longer exist.");
            }
            foreach (var node in nodes)
            {
                if (_graph.SegmentsConnectedTo(node).Count() > 2)
                {
                    throw new InvalidOperationException(
                        "Grade smoothing cannot reshape switch or junction node '"
                        + node.id + "'.");
                }
            }
            for (var index = 1; index < nodes.Length; index++)
            {
                var previous = nodes[index - 1];
                var current = nodes[index];
                if (!_graph.Segments.Any(segment =>
                        (segment.a == previous && segment.b == current)
                        || (segment.a == current
                            && segment.b == previous)))
                {
                    throw new InvalidOperationException(
                        "Grade-chain nodes must be directly connected in travel "
                        + "order. '" + previous.id + "' is not connected to '"
                        + current.id + "'.");
                }
            }
            return nodes;
        }

        private static float GradeBetween(
            TrackNode start,
            TrackNode end)
        {
            var delta = end.transform.localPosition
                        - start.transform.localPosition;
            var run = Mathf.Sqrt(
                delta.x * delta.x + delta.z * delta.z);
            if (run < 0.05f)
            {
                throw new InvalidOperationException(
                    "The grade-chain endpoint segment has no usable horizontal "
                    + "length.");
            }
            return delta.y / run * 100f;
        }

        private static float HermiteElevation(
            float t,
            float length,
            float startY,
            float endY,
            float startSlope,
            float endSlope)
        {
            var t2 = t * t;
            var t3 = t2 * t;
            return (2f * t3 - 3f * t2 + 1f) * startY
                   + (t3 - 2f * t2 + t) * length * startSlope
                   + (-2f * t3 + 3f * t2) * endY
                   + (t3 - t2) * length * endSlope;
        }

        private static float HermiteGrade(
            float t,
            float length,
            float startY,
            float endY,
            float startSlope,
            float endSlope)
        {
            var t2 = t * t;
            var elevationDerivative =
                (6f * t2 - 6f * t) * startY
                + (3f * t2 - 4f * t + 1f)
                * length * startSlope
                + (-6f * t2 + 6f * t) * endY
                + (3f * t2 - 2f * t)
                * length * endSlope;
            return elevationDerivative / length * 100f;
        }

        private static float PitchForChainNode(
            IReadOnlyList<TrackNode> nodes,
            int index,
            float gradeInChainDirection)
        {
            var first = index == 0
                ? nodes[0]
                : nodes[index - 1];
            var last = index == nodes.Count - 1
                ? nodes[nodes.Count - 1]
                : nodes[index + 1];
            var direction = last.transform.localPosition
                            - first.transform.localPosition;
            direction.y = 0f;
            var forward = HorizontalForward(
                nodes[index].transform.localEulerAngles.y);
            var orientation = Vector3.Dot(
                forward,
                direction) < 0f
                ? -1f
                : 1f;
            return PitchFromGrade(
                gradeInChainDirection * orientation);
        }

        internal string BuildArc(
            float radius,
            float degrees,
            bool turnRight,
            float targetGrade,
            int controlNodes)
        {
            var start = RequireNode();
            if (radius < 5f || radius > 5000f)
                throw new InvalidOperationException(
                    "Radius must be between 5 and 5000 m.");
            if (degrees < 0.5f || degrees > 180f)
                throw new InvalidOperationException(
                    "Arc angle must be between 0.5 and 180 degrees.");
            ValidateGrade(targetGrade);
            if (controlNodes < 1 || controlNodes > 64)
            {
                throw new InvalidOperationException(
                    "Arc control nodes must be between 1 and 64.");
            }

            var steps = controlNodes;
            var signedDegrees = turnRight ? degrees : -degrees;
            var stepDegrees = signedDegrees / steps;
            var stepRadians = Mathf.Abs(stepDegrees) * Mathf.Deg2Rad;
            var horizontalArcStep = radius * stepRadians;
            var chord = 2f * radius * Mathf.Sin(stepRadians * 0.5f);
            var startGrade = GradeFromRotation(start.transform.localEulerAngles);
            var position = start.transform.localPosition;
            var yaw = start.transform.localEulerAngles.y;
            var points = new List<PathPoint>(steps);
            for (var index = 1; index <= steps; index++)
            {
                var t0 = (index - 1f) / steps;
                var t1 = index / (float)steps;
                var midYaw = yaw + stepDegrees * 0.5f;
                var midGrade = Mathf.Lerp(
                    startGrade, targetGrade, SmoothStep((t0 + t1) * 0.5f));
                var endGrade = Mathf.Lerp(startGrade, targetGrade, SmoothStep(t1));
                position += HorizontalForward(midYaw) * chord;
                position.y += horizontalArcStep * midGrade / 100f;
                yaw += stepDegrees;
                points.Add(new PathPoint(
                    position,
                    new Vector3(PitchFromGrade(endGrade), yaw, 0f)));
            }
            return CommitPath(start.id, points);
        }

        internal string BuildTurnout(
            float leadLength,
            float degrees,
            bool turnRight,
            float targetGrade)
        {
            var start = RequireNode();
            ValidateLength(leadLength);
            if (degrees < 0.5f || degrees > 45f)
                throw new InvalidOperationException(
                    "Turnout angle must be between 0.5 and 45 degrees.");
            ValidateGrade(targetGrade);

            var signedDegrees = turnRight ? degrees : -degrees;
            var radians = degrees * Mathf.Deg2Rad;
            var radius = leadLength / radians;
            var chord = 2f * radius * Mathf.Sin(radians * 0.5f);
            var startGrade = GradeFromRotation(start.transform.localEulerAngles);
            var averageGrade = (startGrade + targetGrade) * 0.5f;
            var position = start.transform.localPosition
                           + HorizontalForward(
                               start.transform.localEulerAngles.y
                               + signedDegrees * 0.5f) * chord;
            position.y += leadLength * averageGrade / 100f;
            var rotation = new Vector3(
                PitchFromGrade(targetGrade),
                start.transform.localEulerAngles.y + signedDegrees,
                0f);
            return CommitPath(
                start.id,
                new List<PathPoint> { new PathPoint(position, rotation) });
        }

        internal string BuildWye(
            float legLength,
            float leftDegrees,
            float rightDegrees,
            float targetGrade)
        {
            var start = RequireNode();
            ValidateLength(legLength);
            if (leftDegrees < 0.5f || leftDegrees > 45f
                || rightDegrees < 0.5f || rightDegrees > 45f)
            {
                throw new InvalidOperationException(
                    "Wye angles must be between 0.5 and 45 degrees.");
            }
            ValidateGrade(targetGrade);

            var leftNodeId = NextNodeId();
            var rightNodeId = NextNodeId();
            var leftSegmentId = NextSegmentId();
            var rightSegmentId = NextSegmentId();
            var startPosition = start.transform.localPosition;
            var startRotation = start.transform.localEulerAngles;
            var startGrade = GradeFromRotation(startRotation);
            var averageGrade = (startGrade + targetGrade) * 0.5f;

            PathPoint Endpoint(float signedDegrees)
            {
                var radians = Mathf.Abs(signedDegrees) * Mathf.Deg2Rad;
                var radius = legLength / radians;
                var chord = 2f * radius * Mathf.Sin(radians * 0.5f);
                var position = startPosition
                               + HorizontalForward(
                                   startRotation.y + signedDegrees * 0.5f) * chord;
                position.y += legLength * averageGrade / 100f;
                return new PathPoint(
                    position,
                    new Vector3(
                        PitchFromGrade(targetGrade),
                        startRotation.y + signedDegrees,
                        0f));
            }

            var left = Endpoint(-leftDegrees);
            var right = Endpoint(rightDegrees);
            ExecuteEdit(
                "Build wye",
                new[] { start.id, leftNodeId, rightNodeId },
                new[] { leftSegmentId, rightSegmentId },
                () =>
                {
                    var leftNode = CreateNodeLive(new NodeModel
                    {
                        Id = leftNodeId,
                        Position = left.Position,
                        Rotation = left.Rotation,
                    });
                    var rightNode = CreateNodeLive(new NodeModel
                    {
                        Id = rightNodeId,
                        Position = right.Position,
                        Rotation = right.Rotation,
                    });
                    WriteNode(leftNode);
                    WriteNode(rightNode);
                    var leftSegment = CreateSegmentLive(new SegmentModel
                    {
                        Id = leftSegmentId,
                        A = start.id,
                        B = leftNodeId,
                        GroupId = string.Empty,
                        Style = TrackSegment.Style.Standard,
                        TrackClass = TrackClass.Mainline,
                    });
                    var rightSegment = CreateSegmentLive(new SegmentModel
                    {
                        Id = rightSegmentId,
                        A = start.id,
                        B = rightNodeId,
                        GroupId = string.Empty,
                        Style = TrackSegment.Style.Standard,
                        TrackClass = TrackClass.Mainline,
                    });
                    WriteSegment(leftSegment);
                    WriteSegment(rightSegment);
                    _selectedNode = start;
                    _selectedSegment = null;
                },
                useTargetedTrackRebuild: true);
            return leftNodeId + " / " + rightNodeId;
        }

        internal string BuildPerfectWye(
            float throughLength,
            float triangleDepth,
            float stubLength,
            float exitLength,
            float mainlineGrade,
            bool tailRight)
        {
            var start = RequireNode();
            ValidateWyeDimension(
                throughLength, 30f, 2000f, "Through length");
            ValidateWyeDimension(
                triangleDepth, 10f, 1000f, "Triangle depth");
            ValidateWyeDimension(
                stubLength, 5f, 1000f, "Tail stub length");
            ValidateWyeDimension(
                exitLength, 5f, 1000f, "Through exit length");
            ValidateGrade(mainlineGrade);

            var approachSegments = _graph.SegmentsConnectedTo(start).ToList();
            if (approachSegments.Count == 2)
            {
                return BuildPerfectWyeFromThroughTrack(
                    start,
                    approachSegments,
                    throughLength,
                    triangleDepth,
                    stubLength,
                    exitLength,
                    tailRight);
            }
            if (approachSegments.Count != 1)
            {
                throw new InvalidOperationException(
                    "A complete wye needs a selected node with one approach "
                    + "segment or a normal through-track node with two segments.");
            }

            var startPosition = start.transform.localPosition;
            var buildYaw = start.transform.localEulerAngles.y;
            var approachNode = approachSegments[0].GetOtherNode(start);
            var approachOffset =
                approachNode.transform.localPosition - startPosition;
            approachOffset.y = 0f;
            if (approachOffset.sqrMagnitude > 0.001f
                && Vector3.Dot(
                    HorizontalForward(buildYaw),
                    approachOffset.normalized) > 0f)
            {
                buildYaw += 180f;
            }

            var forward = HorizontalForward(buildYaw);
            var tailYaw = buildYaw + (tailRight ? 90f : -90f);
            var tailForward = HorizontalForward(tailYaw);
            var risePerMetre = mainlineGrade / 100f;

            var secondPosition =
                startPosition + forward * throughLength;
            secondPosition.y =
                startPosition.y + throughLength * risePerMetre;
            var tailPosition =
                startPosition
                + forward * (throughLength * 0.5f)
                + tailForward * triangleDepth;
            tailPosition.y =
                startPosition.y + throughLength * 0.5f * risePerMetre;
            var exitPosition =
                secondPosition + forward * exitLength;
            exitPosition.y =
                secondPosition.y + exitLength * risePerMetre;
            var stubPosition =
                tailPosition + tailForward * stubLength;
            stubPosition.y = tailPosition.y;

            var mainlineRotation = new Vector3(
                PitchFromGrade(mainlineGrade),
                buildYaw,
                0f);
            var tailRotation = new Vector3(0f, tailYaw, 0f);

            var secondNodeId = NextNodeId();
            var tailNodeId = NextNodeId();
            var exitNodeId = NextNodeId();
            var stubNodeId = NextNodeId();
            var baseSegmentId = NextSegmentId();
            var firstLegSegmentId = NextSegmentId();
            var secondLegSegmentId = NextSegmentId();
            var exitSegmentId = NextSegmentId();
            var stubSegmentId = NextSegmentId();
            var nodeIds = new[]
            {
                start.id,
                secondNodeId,
                tailNodeId,
                exitNodeId,
                stubNodeId,
            };
            var segmentIds = new[]
            {
                approachSegments[0].id,
                baseSegmentId,
                firstLegSegmentId,
                secondLegSegmentId,
                exitSegmentId,
                stubSegmentId,
            };

            ExecuteEdit(
                "Build complete three-turnout wye",
                nodeIds,
                segmentIds,
                () =>
                {
                    start.transform.localEulerAngles = mainlineRotation;
                    _graph.OnNodeDidChange(start);
                    WriteNode(start);

                    var secondNode = CreateNodeLive(new NodeModel
                    {
                        Id = secondNodeId,
                        Position = secondPosition,
                        Rotation = mainlineRotation,
                    });
                    var tailNode = CreateNodeLive(new NodeModel
                    {
                        Id = tailNodeId,
                        Position = tailPosition,
                        Rotation = tailRotation,
                    });
                    var exitNode = CreateNodeLive(new NodeModel
                    {
                        Id = exitNodeId,
                        Position = exitPosition,
                        Rotation = mainlineRotation,
                    });
                    var stubNode = CreateNodeLive(new NodeModel
                    {
                        Id = stubNodeId,
                        Position = stubPosition,
                        Rotation = tailRotation,
                    });
                    WriteNode(secondNode);
                    WriteNode(tailNode);
                    WriteNode(exitNode);
                    WriteNode(stubNode);

                    CreateAndWriteStandardSegment(
                        baseSegmentId, start.id, secondNodeId);
                    CreateAndWriteStandardSegment(
                        firstLegSegmentId, start.id, tailNodeId);
                    CreateAndWriteStandardSegment(
                        secondLegSegmentId, tailNodeId, secondNodeId);
                    CreateAndWriteStandardSegment(
                        exitSegmentId, secondNodeId, exitNodeId);
                    CreateAndWriteStandardSegment(
                        stubSegmentId, tailNodeId, stubNodeId);

                    _selectedNode = exitNode;
                    _selectedSegment = null;
                },
                useTargetedTrackRebuild: true);

            return "three-turnout wye; through exit "
                   + exitNodeId + " selected, stub " + stubNodeId;
        }

        private string BuildPerfectWyeFromThroughTrack(
            TrackNode start,
            IList<TrackSegment> connected,
            float throughLength,
            float triangleDepth,
            float stubLength,
            float exitLength,
            bool tailRight)
        {
            var outgoing = FindForwardSegment(start, connected);
            if (outgoing == null)
            {
                throw new InvalidOperationException(
                    "Could not determine the forward track from the selected node.");
            }

            var requiredLength = throughLength + exitLength;
            var availableLength = outgoing.GetLength();
            if (availableLength < requiredLength + 1f)
            {
                throw new InvalidOperationException(
                    "The forward segment " + outgoing.id + " has "
                    + availableLength.ToString("0.0", CultureInfo.InvariantCulture)
                    + " m available. This wye needs at least "
                    + (requiredLength + 1f).ToString(
                        "0.0", CultureInfo.InvariantCulture)
                    + " m. Reduce Through or Exit length.");
            }

            var startEnd = outgoing.a == start
                ? TrackSegment.End.A
                : TrackSegment.End.B;
            outgoing.GetPositionRotationAtDistance(
                throughLength,
                startEnd,
                PositionAccuracy.High,
                out var secondPosition,
                out var secondQuaternion);
            outgoing.GetPositionRotationAtDistance(
                throughLength + exitLength,
                startEnd,
                PositionAccuracy.High,
                out var exitPosition,
                out var exitQuaternion);
            outgoing.GetPositionRotationAtDistance(
                throughLength * 0.5f,
                startEnd,
                PositionAccuracy.High,
                out var midpointPosition,
                out var midpointQuaternion);

            var secondRotation = secondQuaternion.eulerAngles;
            var exitRotation = exitQuaternion.eulerAngles;
            var tailYaw = midpointQuaternion.eulerAngles.y
                          + (tailRight ? 90f : -90f);
            var tailForward = HorizontalForward(tailYaw);
            var tailPosition =
                midpointPosition + tailForward * triangleDepth;
            tailPosition.y = midpointPosition.y;
            var stubPosition =
                tailPosition + tailForward * stubLength;
            stubPosition.y = tailPosition.y;
            var tailRotation = new Vector3(0f, tailYaw, 0f);

            var originalModel = CaptureSegment(outgoing);
            var otherNode = outgoing.GetOtherNode(start);
            var secondNodeId = NextNodeId();
            var tailNodeId = NextNodeId();
            var exitNodeId = NextNodeId();
            var stubNodeId = NextNodeId();
            var baseSegmentId = NextSegmentId();
            var firstLegSegmentId = NextSegmentId();
            var secondLegSegmentId = NextSegmentId();
            var exitSegmentId = NextSegmentId();
            var remainderSegmentId = NextSegmentId();
            var stubSegmentId = NextSegmentId();
            var nodeIds = new[]
            {
                start.id,
                otherNode.id,
                secondNodeId,
                tailNodeId,
                exitNodeId,
                stubNodeId,
            };
            var segmentIds = new[]
            {
                outgoing.id,
                baseSegmentId,
                firstLegSegmentId,
                secondLegSegmentId,
                exitSegmentId,
                remainderSegmentId,
                stubSegmentId,
            };

            ExecuteEdit(
                "Build complete wye in existing through track",
                nodeIds,
                segmentIds,
                () =>
                {
                    RemoveSegmentLive(outgoing.id);
                    WriteSegmentDeletion(outgoing.id);

                    var secondNode = CreateNodeLive(new NodeModel
                    {
                        Id = secondNodeId,
                        Position = secondPosition,
                        Rotation = secondRotation,
                    });
                    var tailNode = CreateNodeLive(new NodeModel
                    {
                        Id = tailNodeId,
                        Position = tailPosition,
                        Rotation = tailRotation,
                    });
                    var exitNode = CreateNodeLive(new NodeModel
                    {
                        Id = exitNodeId,
                        Position = exitPosition,
                        Rotation = exitRotation,
                    });
                    var stubNode = CreateNodeLive(new NodeModel
                    {
                        Id = stubNodeId,
                        Position = stubPosition,
                        Rotation = tailRotation,
                    });
                    WriteNode(secondNode);
                    WriteNode(tailNode);
                    WriteNode(exitNode);
                    WriteNode(stubNode);

                    if (originalModel.A == start.id)
                    {
                        CreateAndWriteSegment(CopySegment(
                            originalModel,
                            baseSegmentId,
                            start.id,
                            secondNodeId));
                        CreateAndWriteSegment(CopySegment(
                            originalModel,
                            exitSegmentId,
                            secondNodeId,
                            exitNodeId));
                        CreateAndWriteSegment(CopySegment(
                            originalModel,
                            remainderSegmentId,
                            exitNodeId,
                            otherNode.id));
                    }
                    else
                    {
                        CreateAndWriteSegment(CopySegment(
                            originalModel,
                            remainderSegmentId,
                            otherNode.id,
                            exitNodeId));
                        CreateAndWriteSegment(CopySegment(
                            originalModel,
                            exitSegmentId,
                            exitNodeId,
                            secondNodeId));
                        CreateAndWriteSegment(CopySegment(
                            originalModel,
                            baseSegmentId,
                            secondNodeId,
                            start.id));
                    }
                    CreateAndWriteStandardSegment(
                        firstLegSegmentId, start.id, tailNodeId);
                    CreateAndWriteStandardSegment(
                        secondLegSegmentId, tailNodeId, secondNodeId);
                    CreateAndWriteStandardSegment(
                        stubSegmentId, tailNodeId, stubNodeId);

                    _selectedNode = exitNode;
                    _selectedSegment = null;
                },
                useTargetedTrackRebuild: true);

            return "three-turnout wye in " + outgoing.id
                   + "; existing alignment and grade preserved, exit "
                   + exitNodeId + " selected, stub " + stubNodeId;
        }

        private static TrackSegment FindForwardSegment(
            TrackNode start,
            IEnumerable<TrackSegment> connected)
        {
            var startPosition = start.transform.localPosition;
            var forward = HorizontalForward(
                start.transform.localEulerAngles.y);
            TrackSegment result = null;
            var bestProjection = float.NegativeInfinity;
            foreach (var candidate in connected)
            {
                var offset = candidate.GetOtherNode(start)
                                 .transform.localPosition
                             - startPosition;
                offset.y = 0f;
                var projection = offset.sqrMagnitude < 0.001f
                    ? float.NegativeInfinity
                    : Vector3.Dot(forward, offset.normalized);
                if (projection <= bestProjection)
                    continue;
                bestProjection = projection;
                result = candidate;
            }
            return result;
        }

        public void Dispose()
        {
            SetOverlaysVisible(false);
            DisposeTerrainSession();
            DisposeSplineySession();
            DisposeScenerySession();
            DisposeMandelaSession();
            DisposeOperationsSession();
            DisposeCtcSession();
            DisposeTrainSignalOverlays();
            foreach (var overlay in Resources.FindObjectsOfTypeAll<TileEditorNodeOverlay>())
            {
                if (overlay != null)
                    UnityEngine.Object.Destroy(overlay.gameObject);
            }
            foreach (var overlay in Resources.FindObjectsOfTypeAll<TileEditorSegmentOverlay>())
            {
                if (overlay != null)
                    UnityEngine.Object.Destroy(overlay.gameObject);
            }
            DestroySegmentOverlayRoot();
        }

        private string CommitPath(string startNodeId, IList<PathPoint> points)
        {
            RequireSession();
            if (points == null || points.Count == 0)
                throw new InvalidOperationException("The generated path has no points.");

            var nodeIds = points.Select(_ => NextNodeId()).ToArray();
            var segmentIds = points.Select(_ => NextSegmentId()).ToArray();
            ExecuteEdit(
                "Build generated track",
                new[] { startNodeId }
                    .Concat(nodeIds),
                segmentIds,
                () =>
                {
                    var previousId = startNodeId;
                    TrackNode finalNode = null;
                    for (var index = 0; index < points.Count; index++)
                    {
                        var point = points[index];
                        finalNode = CreateNodeLive(new NodeModel
                        {
                            Id = nodeIds[index],
                            Position = point.Position,
                            Rotation = point.Rotation,
                        });
                        WriteNode(finalNode);
                        var segment = CreateSegmentLive(new SegmentModel
                        {
                            Id = segmentIds[index],
                            A = previousId,
                            B = nodeIds[index],
                            GroupId = string.Empty,
                            Style = TrackSegment.Style.Standard,
                            TrackClass = TrackClass.Mainline,
                        });
                        WriteSegment(segment);
                        previousId = nodeIds[index];
                    }
                    _selectedNode = finalNode;
                    _selectedSegment = null;
                },
                useTargetedTrackRebuild: true);
            return nodeIds[nodeIds.Length - 1];
        }

        private void ExecuteEdit(
            string name,
            IEnumerable<string> nodeIds,
            IEnumerable<string> segmentIds,
            Action mutation,
            bool useLightweightTrackUpdate = false,
            bool useTargetedTrackRebuild = false)
        {
            RequireSession();
            RequireGraphEditOwnership();
            var nodes = nodeIds.Distinct().ToArray();
            var segments = segmentIds.Distinct().ToArray();
            var edit = new EditRecord
            {
                Name = name,
                NodeIds = nodes,
                SegmentIds = segments,
                SceneryIds = Array.Empty<string>(),
                MandelaIds = Array.Empty<string>(),
                BeforeNodes = CaptureNodes(nodes),
                BeforeSegments = CaptureSegments(segments),
                BeforeScenery =
                    new Dictionary<string, SceneryModel>(),
                BeforeMandelas =
                    new Dictionary<string, MandelaModel>(),
                BeforeDocument = useLightweightTrackUpdate
                    ? null
                    : (JObject)_document.DeepClone(),
                BeforeSelectedNode = _selectedNode?.id,
                BeforeSelectedSegment = _selectedSegment?.id,
                BeforeSelectedScenery = _selectedSceneryId,
                BeforeSelectedMandela = _selectedMandelaPath,
                UseLightweightTrackUpdate =
                    useLightweightTrackUpdate,
            };

            _pendingFuseSegmentDefinitions.Clear();
            try
            {
                mutation();
                FlushQueuedFuseSegmentDefinitions(segments);
                ScheduleNarrowGaugeSynchronizationForEdit(
                    nodes,
                    segments);
            }
            catch
            {
                _pendingFuseSegmentDefinitions.Clear();
                throw;
            }
            var topologyChanged = segments.Any(segmentId =>
            {
                edit.BeforeSegments.TryGetValue(segmentId, out var before);
                var current = _graph.GetSegment(segmentId);
                return before == null != (current == null)
                       || (before != null
                           && current != null
                           && (!string.Equals(
                                   before.A,
                                   current.a?.id,
                                   StringComparison.Ordinal)
                               || !string.Equals(
                                   before.B,
                                   current.b?.id,
                                   StringComparison.Ordinal)));
            });
            RebuildLiveGraph(
                nodes,
                segments,
                useTargetedTrackRebuild:
                    (useLightweightTrackUpdate
                     || useTargetedTrackRebuild)
                    && !topologyChanged,
                nodeTransformOnly: useLightweightTrackUpdate);
            edit.AfterNodes = CaptureNodes(nodes);
            edit.AfterSegments = CaptureSegments(segments);
            edit.AfterScenery =
                new Dictionary<string, SceneryModel>();
            edit.AfterMandelas =
                new Dictionary<string, MandelaModel>();
            edit.AfterDocument = useLightweightTrackUpdate
                ? null
                : (JObject)_document.DeepClone();
            edit.AfterSelectedNode = _selectedNode?.id;
            edit.AfterSelectedSegment = _selectedSegment?.id;
            edit.AfterSelectedScenery = _selectedSceneryId;
            edit.AfterSelectedMandela = _selectedMandelaPath;
            _undo.Push(edit);
            _redo.Clear();
            _dirty = true;
            RefreshTrackSelectionColors(
                string.IsNullOrWhiteSpace(edit.BeforeSelectedNode)
                    ? null
                    : _graph.GetNode(edit.BeforeSelectedNode),
                string.IsNullOrWhiteSpace(edit.BeforeSelectedSegment)
                    ? null
                    : _graph.GetSegment(edit.BeforeSelectedSegment),
                _selectedNode,
                _selectedSegment);
        }

        private void RequireGraphEditOwnership()
        {
            if (_externalGraphEditLock)
            {
                throw new InvalidOperationException(
                    "The desktop editor has unsaved map changes. "
                    + "Save or undo them before editing this content in-game.");
            }
        }

        private void Restore(EditRecord edit, bool after)
        {
            var previousNode = _selectedNode;
            var previousSegment = _selectedSegment;
            _pendingFuseSegmentDefinitions.Clear();
            var nodes = after ? edit.AfterNodes : edit.BeforeNodes;
            var segments = after ? edit.AfterSegments : edit.BeforeSegments;
            var document = after
                ? edit.AfterDocument
                : edit.BeforeDocument;
            if (document != null)
                _document = (JObject)document.DeepClone();
            var toolshedFacilities = after
                ? edit.AfterToolshedFacilities
                : edit.BeforeToolshedFacilities;
            if (toolshedFacilities != null)
            {
                _toolshedFacilitiesDocument =
                    (JObject)toolshedFacilities.DeepClone();
                _toolshedFacilitiesDirty = after
                    ? edit.AfterToolshedFacilitiesDirty
                    : edit.BeforeToolshedFacilitiesDirty;
            }

            if (!edit.UseLightweightTrackUpdate)
            {
                foreach (var id in edit.SegmentIds)
                    RemoveSegmentLive(id);

                foreach (var id in edit.NodeIds)
                {
                    var model = nodes[id];
                    var current = _graph.GetNode(id);
                    if (model == null)
                    {
                        if (current != null)
                            RemoveNodeLive(id);
                    }
                    else if (current == null)
                    {
                        CreateNodeLive(model);
                    }
                    else
                    {
                        ApplyNodeModel(current, model);
                    }
                }

                _graph.RebuildCollections();
                foreach (var id in edit.SegmentIds)
                {
                    var model = segments[id];
                    if (model != null)
                        CreateSegmentLive(model);
                }
            }
            else
            {
                foreach (var id in edit.NodeIds)
                {
                    var model = nodes[id];
                    var current = _graph.GetNode(id);
                    if (model != null && current != null)
                    {
                        ApplyNodeModel(current, model);
                        WriteNode(current);
                    }
                }
                foreach (var id in edit.SegmentIds)
                {
                    var model = segments[id];
                    var current = _graph.GetSegment(id);
                    if (model != null && current != null)
                    {
                        ApplySegmentModel(current, model);
                        WriteSegment(current);
                    }
                }
            }

            if (edit.NodeIds.Length > 0
                || edit.SegmentIds.Length > 0)
            {
                foreach (var segmentId in edit.SegmentIds)
                {
                    if (_graph.GetSegment(segmentId) != null)
                        QueueFuseSegmentDefinition(segmentId);
                }
                FlushQueuedFuseSegmentDefinitions(
                    edit.SegmentIds);
                ScheduleNarrowGaugeSynchronizationForEdit(
                    edit.NodeIds,
                    edit.SegmentIds);
                RebuildLiveGraph(
                    edit.NodeIds,
                    edit.SegmentIds,
                    useTargetedTrackRebuild:
                        edit.UseLightweightTrackUpdate,
                    nodeTransformOnly:
                        edit.UseLightweightTrackUpdate);
            }
            var selectedNode = after ? edit.AfterSelectedNode : edit.BeforeSelectedNode;
            var selectedSegment = after
                ? edit.AfterSelectedSegment
                : edit.BeforeSelectedSegment;
            _selectedNode = string.IsNullOrWhiteSpace(selectedNode)
                ? null
                : _graph.GetNode(selectedNode);
            _selectedSegment = string.IsNullOrWhiteSpace(selectedSegment)
                ? null
                : _graph.GetSegment(selectedSegment);
            RestoreSceneryModels(edit, after);
            RestoreMandelaModels(edit, after);
            RefreshTrackSelectionColors(
                previousNode,
                previousSegment,
                _selectedNode,
                _selectedSegment);
            if (_operationsMode)
                RefreshOperationsMode(true);
        }

        private Dictionary<string, NodeModel> CaptureNodes(IEnumerable<string> ids)
        {
            return ids.ToDictionary(
                id => id,
                id =>
                {
                    var node = _graph.GetNode(id);
                    return node == null ? null : CaptureNode(node);
                });
        }

        private Dictionary<string, SegmentModel> CaptureSegments(IEnumerable<string> ids)
        {
            return ids.ToDictionary(
                id => id,
                id =>
                {
                    var segment = _graph.GetSegment(id);
                    return segment == null ? null : CaptureSegment(segment);
                });
        }

        private static NodeModel CaptureNode(TrackNode node)
        {
            return new NodeModel
            {
                Id = node.id,
                Position = node.transform.localPosition,
                Rotation = node.transform.localEulerAngles,
                FlipSwitchStand = node.flipSwitchStand,
            };
        }

        private SegmentModel CaptureSegment(TrackSegment segment)
        {
            return new SegmentModel
            {
                Id = segment.id,
                A = segment.a.id,
                B = segment.b.id,
                Priority = segment.priority,
                SpeedLimit = segment.speedLimit,
                GroupId = segment.groupId ?? string.Empty,
                Gauge = GetSegmentGauge(segment.id),
                Style = segment.style,
                TrackClass = segment.trackClass,
            };
        }

        private static SegmentModel CopySegment(
            SegmentModel source,
            string id,
            string a,
            string b)
        {
            return new SegmentModel
            {
                Id = id,
                A = a,
                B = b,
                Priority = source.Priority,
                SpeedLimit = source.SpeedLimit,
                GroupId = source.GroupId,
                Gauge = source.Gauge,
                Style = source.Style,
                TrackClass = source.TrackClass,
            };
        }

        private TrackNode CreateNodeLive(NodeModel model)
        {
            var existing = _graph.GetNode(model.Id);
            if (existing != null)
            {
                ApplyNodeModel(existing, model);
                return existing;
            }
            var node = _graph.AddNode(
                model.Id,
                model.Position,
                Quaternion.Euler(model.Rotation));
            node.transform.SetParent(_graph.transform);
            ApplyNodeModel(node, model);
            return node;
        }

        private static void ApplyNodeModel(TrackNode node, NodeModel model)
        {
            node.transform.localPosition = model.Position;
            node.transform.localEulerAngles = model.Rotation;
            node.flipSwitchStand = model.FlipSwitchStand;
        }

        private TrackSegment CreateSegmentLive(SegmentModel model)
        {
            model.Gauge = string.IsNullOrWhiteSpace(model.Gauge)
                ? NewTrackGauge
                : NormalizeTrackGauge(model.Gauge);
            _segmentGauges[model.Id] = model.Gauge;
            var existing = _graph.GetSegment(model.Id);
            if (existing != null)
                RemoveSegmentLive(model.Id);
            var a = _graph.GetNode(model.A);
            var b = _graph.GetNode(model.B);
            if (a == null || b == null)
                throw new InvalidOperationException(
                    $"Cannot create {model.Id}; endpoint node is missing.");
            var segment = _graph.AddSegment(model.Id, a, b);
            segment.transform.SetParent(_graph.transform);
            ApplySegmentModel(segment, model);
            return segment;
        }

        private static void ApplySegmentModel(
            TrackSegment segment,
            SegmentModel model)
        {
            segment.priority = model.Priority;
            segment.speedLimit = model.SpeedLimit;
            segment.groupId = model.GroupId ?? string.Empty;
            segment.style = model.Style;
            segment.trackClass = model.TrackClass;
            segment.InvalidateCurve();
        }

        private void CreateAndWriteStandardSegment(
            string id,
            string a,
            string b)
        {
            CreateAndWriteSegment(new SegmentModel
            {
                Id = id,
                A = a,
                B = b,
                GroupId = string.Empty,
                Style = TrackSegment.Style.Standard,
                TrackClass = TrackClass.Mainline,
            });
        }

        private void CreateAndWriteSegment(SegmentModel model)
        {
            var segment = CreateSegmentLive(model);
            WriteSegment(segment);
        }

        private void RemoveNodeLive(string id)
        {
            var node = _graph.GetNode(id);
            if (node == null)
                return;
            _nodeOverlays.Remove(id);
            node.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(node.gameObject);
        }

        private void RemoveSegmentLive(string id)
        {
            RemoveSegmentOverlay(id);
            var segment = _graph.GetSegment(id);
            if (segment == null)
                return;
            segment.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(segment.gameObject);
        }

        private void RebuildLiveGraph(
            IEnumerable<string> affectedNodeIds = null,
            IEnumerable<string> affectedSegmentIds = null,
            bool rebuildAllOverlays = false,
            bool useTargetedTrackRebuild = false,
            bool nodeTransformOnly = false,
            bool forceRuntimeTrackRebuild = false)
        {
            if (_graph == null)
                return;
            var nodeIds = (affectedNodeIds
                           ?? Array.Empty<string>())
                .Distinct()
                .ToArray();
            var segmentIds = (affectedSegmentIds
                              ?? Array.Empty<string>())
                .Distinct()
                .ToArray();
            if (!useTargetedTrackRebuild)
                _graph.RebuildCollections();
            var trackManager = TrackObjectManager.Instance;
            var segmentsToRefresh = new HashSet<string>(
                segmentIds,
                StringComparer.OrdinalIgnoreCase);
            foreach (var nodeId in nodeIds)
            {
                var node = _graph.GetNode(nodeId);
                if (node == null)
                    continue;
                foreach (var segment in _graph.SegmentsConnectedTo(node))
                    segmentsToRefresh.Add(segment.id);
            }
            foreach (var segmentId in segmentsToRefresh)
                _graph.GetSegment(segmentId)?.InvalidateCurve();
            var previewOnly = _deferredTrackRebuilds
                              && !forceRuntimeTrackRebuild;
            if (previewOnly)
            {
                foreach (var nodeId in nodeIds)
                    _deferredTrackNodeIds.Add(nodeId);
                foreach (var segmentId in segmentsToRefresh)
                    _deferredTrackSegmentIds.Add(segmentId);
                if (!useTargetedTrackRebuild)
                    _deferredFullTrackRebuildPending = true;
            }
            if (trackManager != null && !previewOnly)
            {
                if (useTargetedTrackRebuild)
                {
                    foreach (var node in TrackRebuildNeighborhood(
                                 nodeIds,
                                 nodeTransformOnly
                                     ? Array.Empty<string>()
                                     : segmentIds))
                    {
                        if (node != null
                            && node.gameObject.activeInHierarchy)
                        {
                            trackManager.SetNeedsRebuild(node);
                        }
                    }
                }
                else
                {
                    trackManager.Rebuild();
                }
            }
            if (rebuildAllOverlays || !_trackOverlaysBuilt)
            {
                RebuildOverlays(rebuildAllOverlays);
            }
            else
            {
                foreach (var nodeId in nodeIds)
                {
                    EnsureNodeOverlay(_graph.GetNode(nodeId), false);
                }
                foreach (var segmentId in segmentIds)
                {
                    EnsureSegmentOverlay(
                        _graph.GetSegment(segmentId),
                        false);
                }
            }

            if (!rebuildAllOverlays)
            {
                foreach (var segmentId in segmentsToRefresh)
                {
                    var segment = _graph.GetSegment(segmentId);
                    var overlay = segment == null
                        ? null
                        : GetSegmentOverlay(segment);
                    if (useTargetedTrackRebuild || previewOnly)
                    {
                        overlay?.RefreshCurveLine();
                        _pendingSegmentGeometryRefresh.Add(segmentId);
                    }
                    else
                    {
                        overlay?.Rebuild();
                    }
                }
                if ((useTargetedTrackRebuild || previewOnly)
                    && _pendingSegmentGeometryRefresh.Count > 0)
                {
                    _nextSegmentGeometryRefreshAt =
                        Time.unscaledTime + 0.18f;
                }
            }
            if (rebuildAllOverlays)
            {
                SetOverlaysVisible(
                    _editModeActive
                    && _geoWorkspaceActive
                    && (!_splineyMode || _splineTrackPickMode));
            }
            ScheduleTrackOverlayRepair(
                nodeIds,
                segmentIds,
                rebuildAllOverlays);
            if (_trainSignalMode)
                RefreshLockedTrainSignalOverlayTransforms();
        }

        private void ClearDeferredTrackRebuildState()
        {
            _deferredTrackNodeIds.Clear();
            _deferredTrackSegmentIds.Clear();
            _deferredFullTrackRebuildPending = false;
        }

        private IEnumerable<TrackNode> TrackRebuildNeighborhood(
            IEnumerable<string> nodeIds,
            IEnumerable<string> segmentIds)
        {
            var nodes = new HashSet<TrackNode>();
            foreach (var nodeId in nodeIds ?? Array.Empty<string>())
            {
                var node = _graph.GetNode(nodeId);
                if (node != null)
                    nodes.Add(node);
            }
            foreach (var segmentId in segmentIds ?? Array.Empty<string>())
            {
                var segment = _graph.GetSegment(segmentId);
                if (segment?.a != null)
                    nodes.Add(segment.a);
                if (segment?.b != null)
                    nodes.Add(segment.b);
            }

            foreach (var node in nodes.ToArray())
            {
                foreach (var segment in _graph.SegmentsConnectedTo(node))
                {
                    if (segment.a != null)
                        nodes.Add(segment.a);
                    if (segment.b != null)
                        nodes.Add(segment.b);
                }
            }
            foreach (var switchNode in nodes
                         .Where(node => _graph.IsSwitch(node))
                         .ToArray())
            {
                foreach (var segment in _graph.SegmentsConnectedTo(switchNode))
                {
                    if (segment.a != null)
                        nodes.Add(segment.a);
                    if (segment.b != null)
                        nodes.Add(segment.b);
                }
            }
            return nodes;
        }

        private void RebuildOverlays(bool rebuildExisting)
        {
            if (_graph == null)
                return;
            PruneNodeOverlays();
            PruneSegmentOverlays();
            foreach (var node in _graph.Nodes.ToArray())
            {
                EnsureNodeOverlay(node, rebuildExisting);
            }
            foreach (var segment in _graph.Segments.ToArray())
            {
                EnsureSegmentOverlay(segment, rebuildExisting);
            }
            _trackOverlaysBuilt = true;
            UpdateTrackOverlayCulling(true);
        }

        private void EnsureNodeOverlay(
            TrackNode node,
            bool rebuildExisting)
        {
            if (node == null)
                return;
            _nodeOverlays.TryGetValue(node.id, out var overlay);
            if (overlay == null)
            {
                overlay = node.GetComponentInChildren<
                    TileEditorNodeOverlay>(true);
            }
            if (overlay == null)
            {
                var go = new GameObject(
                    "TileEditorNodeOverlay");
                go.transform.SetParent(node.transform, false);
                overlay = go.AddComponent<
                    TileEditorNodeOverlay>();
                overlay.Initialize(this, node);
            }
            else if (rebuildExisting || !overlay.IsHealthyFor(node))
            {
                overlay.Initialize(this, node);
            }
            _nodeOverlays[node.id] = overlay;
            overlay.SetOverlayVisible(
                ShouldShowTrackOverlay(overlay));
        }

        private void EnsureSegmentOverlay(
            TrackSegment segment,
            bool rebuildExisting)
        {
            if (segment == null
                || segment.a == null
                || segment.b == null)
            {
                return;
            }
            var root = EnsureSegmentOverlayRoot();
            if (root == null)
                return;
            _segmentOverlays.TryGetValue(
                segment.id,
                out var overlay);
            if (overlay == null)
            {
                var go = new GameObject(
                    "TileEditorSegmentOverlay-" + segment.id);
                go.transform.SetParent(
                    root.transform,
                    false);
                overlay = go.AddComponent<
                    TileEditorSegmentOverlay>();
                overlay.Initialize(this, segment);
            }
            else
            {
                if (overlay.transform.parent != root.transform)
                {
                    overlay.transform.SetParent(
                        root.transform,
                        false);
                }
                if (rebuildExisting || !overlay.IsHealthyFor(segment))
                    overlay.Initialize(this, segment);
            }
            _segmentOverlays[segment.id] = overlay;
            overlay.SetOverlayVisible(
                ShouldShowTrackOverlay(overlay));
        }

        private void ScheduleTrackOverlayRepair(
            IEnumerable<string> affectedNodeIds,
            IEnumerable<string> affectedSegmentIds,
            bool repairAll)
        {
            if (repairAll)
            {
                _repairAllTrackOverlays = true;
                _pendingNodeOverlayRepairs.Clear();
                _pendingSegmentOverlayRepairs.Clear();
            }
            else if (!_repairAllTrackOverlays)
            {
                foreach (var nodeId in affectedNodeIds
                             ?? Array.Empty<string>())
                {
                    if (!string.IsNullOrWhiteSpace(nodeId))
                        _pendingNodeOverlayRepairs.Add(nodeId);
                }
                foreach (var segmentId in affectedSegmentIds
                             ?? Array.Empty<string>())
                {
                    if (!string.IsNullOrWhiteSpace(segmentId))
                        _pendingSegmentOverlayRepairs.Add(segmentId);
                }
            }
            _trackOverlayRepairPasses = Math.Max(
                _trackOverlayRepairPasses,
                3);
            _nextTrackOverlayRepairAt =
                Time.unscaledTime + 0.05f;
        }

        private void RepairPendingTrackOverlays()
        {
            if (_graph == null
                || _trackOverlayRepairPasses <= 0
                || Time.unscaledTime < _nextTrackOverlayRepairAt)
            {
                return;
            }

            if (_repairAllTrackOverlays)
            {
                foreach (var node in _graph.Nodes.ToArray())
                    EnsureNodeOverlay(node, false);
                foreach (var segment in _graph.Segments.ToArray())
                    EnsureSegmentOverlay(segment, false);
            }
            else
            {
                foreach (var nodeId in _pendingNodeOverlayRepairs)
                    EnsureNodeOverlay(_graph.GetNode(nodeId), false);
                foreach (var segmentId in _pendingSegmentOverlayRepairs)
                {
                    EnsureSegmentOverlay(
                        _graph.GetSegment(segmentId),
                        false);
                }
            }

            _trackOverlayRepairPasses--;
            if (_trackOverlayRepairPasses > 0)
            {
                _nextTrackOverlayRepairAt =
                    Time.unscaledTime + 0.35f;
                return;
            }
            _repairAllTrackOverlays = false;
            _pendingNodeOverlayRepairs.Clear();
            _pendingSegmentOverlayRepairs.Clear();
        }

        private void RefreshPendingSegmentGeometry()
        {
            if (_graph == null
                || _pendingSegmentGeometryRefresh.Count == 0
                || Time.unscaledTime
                   < _nextSegmentGeometryRefreshAt)
            {
                return;
            }
            foreach (var segmentId in
                     _pendingSegmentGeometryRefresh)
            {
                var segment = _graph.GetSegment(segmentId);
                GetSegmentOverlay(segment)?.Rebuild();
            }
            _pendingSegmentGeometryRefresh.Clear();
        }

        private void SetOverlaysVisible(bool visible)
        {
            _trackOverlayVisibility = visible;
            if (visible)
            {
                UpdateTrackOverlayCulling(true);
                return;
            }
            foreach (var overlay in _nodeOverlays.Values)
            {
                if (overlay != null)
                    overlay.SetOverlayVisible(false);
            }
            foreach (var overlay in _segmentOverlays.Values)
            {
                if (overlay != null)
                    overlay.SetOverlayVisible(false);
            }
        }

        private void UpdateTrackOverlayCulling(bool force = false)
        {
            if (!_trackOverlayVisibility)
                return;
            var camera = Camera.main;
            if (camera == null)
            {
                foreach (var overlay in _nodeOverlays.Values)
                    overlay?.SetOverlayVisible(true);
                foreach (var overlay in _segmentOverlays.Values)
                    overlay?.SetOverlayVisible(true);
                return;
            }

            var cameraPosition = camera.transform.position;
            var groundPosition = CameraSelector.shared == null
                ? cameraPosition
                : CameraSelector.shared.CurrentCameraGroundPosition;
            var cameraHeight = Mathf.Abs(
                cameraPosition.y - groundPosition.y);
            var range = Mathf.Clamp(
                650f + cameraHeight * 1.35f,
                650f,
                2400f);
            if (!force
                && Time.unscaledTime < _nextTrackOverlayCullAt
                && (cameraPosition
                    - _lastTrackOverlayCameraPosition).sqrMagnitude
                   < 100f
                && Mathf.Abs(range - _lastTrackOverlayRange) < 25f)
            {
                return;
            }
            _nextTrackOverlayCullAt = Time.unscaledTime + 0.35f;
            _lastTrackOverlayCameraPosition = cameraPosition;
            _lastTrackOverlayRange = range;
            var rangeSquared = range * range;
            foreach (var overlay in _nodeOverlays.Values)
            {
                if (overlay == null)
                    continue;
                overlay.SetOverlayVisible(
                    IsSelected(overlay.Node)
                    || overlay.IsWithinWorldRange(
                        cameraPosition,
                        rangeSquared));
            }
            foreach (var overlay in _segmentOverlays.Values)
            {
                if (overlay == null)
                    continue;
                overlay.SetOverlayVisible(
                    IsSelected(overlay.Segment)
                    || overlay.IsWithinWorldRange(
                        cameraPosition,
                        rangeSquared));
            }
        }

        private bool ShouldShowTrackOverlay(
            TileEditorNodeOverlay overlay)
        {
            if (!_trackOverlayVisibility || overlay == null)
                return false;
            var camera = Camera.main;
            if (camera == null)
                return true;
            var range = Mathf.Max(650f, _lastTrackOverlayRange);
            return IsSelected(overlay.Node)
                   || overlay.IsWithinWorldRange(
                       camera.transform.position,
                       range * range);
        }

        private bool ShouldShowTrackOverlay(
            TileEditorSegmentOverlay overlay)
        {
            if (!_trackOverlayVisibility || overlay == null)
                return false;
            var camera = Camera.main;
            if (camera == null)
                return true;
            var range = Mathf.Max(650f, _lastTrackOverlayRange);
            return IsSelected(overlay.Segment)
                   || overlay.IsWithinWorldRange(
                       camera.transform.position,
                       range * range);
        }

        private void RefreshTrackSelectionColors(
            TrackNode previousNode,
            TrackSegment previousSegment,
            TrackNode selectedNode,
            TrackSegment selectedSegment)
        {
            previousNode?.GetComponentInChildren<
                    TileEditorNodeOverlay>(true)
                ?.RefreshColor();
            GetSegmentOverlay(previousSegment)?.RefreshColor();
            if (selectedNode != previousNode)
            {
                selectedNode?.GetComponentInChildren<
                        TileEditorNodeOverlay>(true)
                    ?.RefreshColor();
            }
            if (selectedSegment != previousSegment)
            {
                GetSegmentOverlay(selectedSegment)?.RefreshColor();
            }
        }

        private void AttachGraph()
        {
            var current = Graph.Shared;
            if (current == _graph)
                return;
            DestroySegmentOverlayRoot();
            _nodeOverlays.Clear();
            _graph = current;
            _trackOverlaysBuilt = false;
            _repairAllTrackOverlays = false;
            _trackOverlayRepairPasses = 0;
            _pendingNodeOverlayRepairs.Clear();
            _pendingSegmentOverlayRepairs.Clear();
            _pendingSegmentGeometryRefresh.Clear();
            ClearDeferredTrackRebuildState();
            _selectedNode = null;
            _selectedSegment = null;
        }

        private GameObject EnsureSegmentOverlayRoot()
        {
            if (_graph == null)
                return null;
            if (_segmentOverlayRoot == null)
            {
                _segmentOverlayRoot = new GameObject(
                    "TileEditorSegmentOverlays");
            }
            if (_segmentOverlayRoot.transform.parent
                != _graph.transform)
            {
                _segmentOverlayRoot.transform.SetParent(
                    _graph.transform,
                    false);
            }
            _segmentOverlayRoot.transform.localPosition = Vector3.zero;
            _segmentOverlayRoot.transform.localEulerAngles = Vector3.zero;
            _segmentOverlayRoot.transform.localScale = Vector3.one;
            return _segmentOverlayRoot;
        }

        private TileEditorSegmentOverlay GetSegmentOverlay(
            TrackSegment segment)
        {
            if (segment == null
                || string.IsNullOrWhiteSpace(segment.id))
            {
                return null;
            }
            if (_segmentOverlays.TryGetValue(
                    segment.id,
                    out var overlay)
                && overlay != null)
            {
                return overlay;
            }
            EnsureSegmentOverlay(segment, false);
            _segmentOverlays.TryGetValue(
                segment.id,
                out overlay);
            return overlay;
        }

        private void RemoveSegmentOverlay(string segmentId)
        {
            if (string.IsNullOrWhiteSpace(segmentId))
                return;
            if (!_segmentOverlays.TryGetValue(
                    segmentId,
                    out var overlay))
            {
                return;
            }
            _segmentOverlays.Remove(segmentId);
            if (overlay != null)
            {
                overlay.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(overlay.gameObject);
            }
        }

        private void PruneSegmentOverlays()
        {
            if (_graph == null)
                return;
            var activeIds = new HashSet<string>(
                _graph.Segments
                    .Where(segment =>
                        segment != null
                        && !string.IsNullOrWhiteSpace(segment.id))
                    .Select(segment => segment.id),
                StringComparer.OrdinalIgnoreCase);
            foreach (var segmentId in _segmentOverlays.Keys
                         .Where(id => !activeIds.Contains(id))
                         .ToArray())
            {
                RemoveSegmentOverlay(segmentId);
            }
        }

        private void PruneNodeOverlays()
        {
            if (_graph == null)
                return;
            var activeIds = new HashSet<string>(
                _graph.Nodes
                    .Where(node =>
                        node != null
                        && !string.IsNullOrWhiteSpace(node.id))
                    .Select(node => node.id),
                StringComparer.OrdinalIgnoreCase);
            foreach (var nodeId in _nodeOverlays.Keys
                         .Where(id => !activeIds.Contains(id))
                         .ToArray())
            {
                _nodeOverlays.Remove(nodeId);
            }
        }

        private void DestroySegmentOverlayRoot()
        {
            _segmentOverlays.Clear();
            if (_segmentOverlayRoot != null)
            {
                _segmentOverlayRoot.SetActive(false);
                UnityEngine.Object.Destroy(_segmentOverlayRoot);
                _segmentOverlayRoot = null;
            }
        }

        private void DiscoverGraphChoices()
        {
            if (_choicesLoaded)
                return;
            _choicesLoaded = true;
            _graphChoices.Clear();
            var mods = Path.Combine(_gameRoot, "Mods");
            if (!Directory.Exists(mods))
                return;

            foreach (var folder in Directory.GetDirectories(mods))
            {
                var definitionPath = Path.Combine(folder, "Definition.json");
                if (!File.Exists(definitionPath))
                    continue;
                try
                {
                    var definition = JObject.Parse(File.ReadAllText(definitionPath));
                    var mixintos = definition["mixintos"]?["game-graph"];
                    if (mixintos == null)
                        continue;
                    var modName = (string)definition["name"]
                                  ?? (string)definition["id"]
                                  ?? Path.GetFileName(folder);
                    var modChoices = new List<GraphChoice>();
                    foreach (var reference in EnumerateMixintoReferences(mixintos))
                    {
                        var relative = ParseFileMixinto(reference);
                        if (string.IsNullOrWhiteSpace(relative))
                            continue;
                        var graphPath = Path.GetFullPath(Path.Combine(folder, relative));
                        if (!File.Exists(graphPath)
                            || !graphPath.EndsWith(".json",
                                StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                        var layerName = Path.GetFileName(graphPath);
                        modChoices.Add(new GraphChoice
                        {
                            DisplayName = modName + " / " + layerName,
                            ModKey = Path.GetFullPath(folder),
                            ModName = modName,
                            LayerName = layerName,
                            Path = graphPath,
                        });
                    }
                    var primary = modChoices
                        .OrderBy(GraphLayerPriority)
                        .ThenBy(
                            choice => choice.LayerName,
                            StringComparer.OrdinalIgnoreCase)
                        .FirstOrDefault();
                    if (primary != null)
                        primary.IsPrimary = true;
                    _graphChoices.AddRange(modChoices);
                }
                catch (Exception ex)
                {
                    _logger?.Warning(
                        "Could not inspect " + definitionPath + ": " + ex.Message);
                }
            }

            // Native FUSE packages declare one or more JSON fragments in
            // Info.json. Track fragments use the same runtime graph but have
            // FUSE-native endpoint/removal field names.
            var knownPaths = new HashSet<string>(
                _graphChoices.Select(choice => choice.Path),
                StringComparer.OrdinalIgnoreCase);
            foreach (var folder in Directory.GetDirectories(mods))
            {
                var infoPath = Path.Combine(folder, "Info.json");
                if (!File.Exists(infoPath))
                    continue;
                try
                {
                    var info = JObject.Parse(File.ReadAllText(infoPath));
                    var dataFiles = info["FuseDataFiles"];
                    if (dataFiles == null)
                        continue;
                    var modName = (string)info["DisplayName"]
                                  ?? (string)info["Id"]
                                  ?? Path.GetFileName(folder);
                    var modChoices = new List<GraphChoice>();
                    foreach (var relative in EnumerateStringValues(dataFiles))
                    {
                        if (string.IsNullOrWhiteSpace(relative))
                            continue;
                        var graphPath = Path.GetFullPath(
                            Path.Combine(folder, relative));
                        if (!File.Exists(graphPath)
                            || !graphPath.EndsWith(
                                ".json",
                                StringComparison.OrdinalIgnoreCase)
                            || knownPaths.Contains(graphPath))
                        {
                            continue;
                        }
                        var document = JObject.Parse(
                            File.ReadAllText(graphPath));
                        if (!(document["tracks"] is JObject))
                            continue;
                        var layerName = Path.GetFileName(graphPath);
                        modChoices.Add(new GraphChoice
                        {
                            DisplayName = modName + " / " + layerName
                                          + " [FUSE]",
                            ModKey = Path.GetFullPath(folder),
                            ModName = modName,
                            LayerName = layerName,
                            Path = graphPath,
                        });
                        knownPaths.Add(graphPath);
                    }
                    var primary = modChoices
                        .OrderBy(GraphLayerPriority)
                        .ThenBy(
                            choice => choice.LayerName,
                            StringComparer.OrdinalIgnoreCase)
                        .FirstOrDefault();
                    if (primary != null)
                        primary.IsPrimary = true;
                    _graphChoices.AddRange(modChoices);
                }
                catch (Exception ex)
                {
                    _logger?.Warning(
                        "Could not inspect FUSE package "
                        + infoPath + ": " + ex.Message);
                }
            }
            _graphChoices.Sort((left, right) =>
            {
                var modOrder = string.Compare(
                    left.ModName,
                    right.ModName,
                    StringComparison.OrdinalIgnoreCase);
                if (modOrder != 0)
                    return modOrder;
                if (left.IsPrimary != right.IsPrimary)
                    return left.IsPrimary ? -1 : 1;
                return string.Compare(
                    left.LayerName,
                    right.LayerName,
                    StringComparison.OrdinalIgnoreCase);
            });
        }

        private static IEnumerable<string> EnumerateMixintoReferences(
            JToken token)
        {
            foreach (var item in EnumerateTokens(token))
            {
                if (item.Type == JTokenType.String)
                {
                    yield return item.Value<string>();
                    continue;
                }
                if (item is JObject conditional)
                {
                    var reference = (string)conditional["mixinto"]
                                    ?? (string)conditional["value"]
                                    ?? (string)conditional["file"];
                    if (!string.IsNullOrWhiteSpace(reference))
                        yield return reference;
                }
            }
        }

        private static IEnumerable<string> EnumerateStringValues(JToken token)
        {
            foreach (var item in EnumerateTokens(token))
            {
                if (item.Type == JTokenType.String)
                    yield return item.Value<string>();
            }
        }

        private static IEnumerable<JToken> EnumerateTokens(JToken token)
        {
            if (token is JArray array)
            {
                foreach (var item in array)
                    yield return item;
                yield break;
            }
            if (token != null)
                yield return token;
        }

        private static int GraphLayerPriority(GraphChoice choice)
        {
            var name = choice?.LayerName ?? string.Empty;
            if (string.Equals(
                    name,
                    "Main-game-graph.json",
                    StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }
            if (string.Equals(
                    name,
                    "game-graph.json",
                    StringComparison.OrdinalIgnoreCase))
            {
                return 1;
            }
            if (name.IndexOf(
                    "game-graph",
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return 2;
            }
            if (name.IndexOf(
                    "track",
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return 3;
            }
            if (string.Equals(
                    name,
                    "Map.json",
                    StringComparison.OrdinalIgnoreCase))
            {
                return 4;
            }
            return 10;
        }

        private static string ParseFileMixinto(string reference)
        {
            if (string.IsNullOrWhiteSpace(reference))
                return null;
            reference = reference.Trim();
            if (reference.StartsWith("file(", StringComparison.OrdinalIgnoreCase)
                && reference.EndsWith(")", StringComparison.Ordinal))
            {
                return reference.Substring(5, reference.Length - 6)
                    .Trim()
                    .Trim('"');
            }
            return reference;
        }

        private void WriteNode(TrackNode node)
        {
            var existing = NodesObject[node.id] as JObject;
            var entry = existing == null
                ? new JObject()
                : (JObject)existing.DeepClone();
            entry["position"] = Vector(node.transform.localPosition);
            entry["rotation"] = Vector(node.transform.localEulerAngles);
            entry["flipSwitchStand"] = node.flipSwitchStand;
            NodesObject[node.id] = entry;
            RemoveFuseTrackRemoval("nodes", node.id);
        }

        private void WriteSegment(TrackSegment segment)
        {
            var existing = SegmentsObject[segment.id] as JObject;
            var entry = existing == null
                ? new JObject()
                : (JObject)existing.DeepClone();
            entry["style"] = _fuseNativeDocument
                ? segment.style.ToString().ToLowerInvariant()
                : segment.style.ToString();
            entry["trackClass"] = _fuseNativeDocument
                ? segment.trackClass == TrackClass.Mainline
                    ? "main"
                    : segment.trackClass.ToString().ToLowerInvariant()
                : segment.trackClass.ToString();
            if (_fuseNativeDocument)
            {
                entry["startNodeId"] = segment.a.id;
                entry["endNodeId"] = segment.b.id;
                entry.Remove("startId");
                entry.Remove("endId");
            }
            else
            {
                entry["startId"] = segment.a.id;
                entry["endId"] = segment.b.id;
                entry.Remove("startNodeId");
                entry.Remove("endNodeId");
            }
            entry["priority"] = segment.priority;
            entry["speedLimit"] = segment.speedLimit;
            if (_fuseNativeDocument)
            {
                if (string.IsNullOrWhiteSpace(segment.groupId))
                    entry.Remove("groupId");
                else
                    entry["groupId"] = segment.groupId;
            }
            else
            {
                entry["groupId"] = segment.groupId ?? string.Empty;
            }
            var gauge = GetSegmentGauge(segment.id);
            entry["gauge"] = gauge;
            SegmentsObject[segment.id] = entry;
            RemoveFuseTrackRemoval("segments", segment.id);
            QueueFuseSegmentDefinition(segment.id);
        }

        private void WriteNodeDeletion(string id)
        {
            if (_fuseNativeDocument)
            {
                NodesObject.Property(id)?.Remove();
                if (!_documentNodeIdsAtOpen.Contains(id)
                    && _runtimeNodeIdsAtOpen.Contains(id))
                {
                    AddFuseTrackRemoval("nodes", id);
                }
            }
            else
            {
                NodesObject[id] = JValue.CreateNull();
            }
        }

        private void WriteSegmentDeletion(string id)
        {
            if (_fuseNativeDocument)
            {
                SegmentsObject.Property(id)?.Remove();
                if (!_documentSegmentIdsAtOpen.Contains(id)
                    && _runtimeSegmentIdsAtOpen.Contains(id))
                {
                    AddFuseTrackRemoval("segments", id);
                }
            }
            else
            {
                SegmentsObject[id] = JValue.CreateNull();
            }
            _segmentGauges.Remove(id);
        }

        private void AddFuseTrackRemoval(string kind, string id)
        {
            if (!_fuseNativeDocument || string.IsNullOrWhiteSpace(id))
                return;
            var array = EnsureFuseRemovalArray(kind);
            if (!array.Values<string>().Any(
                    value => string.Equals(
                        value,
                        id,
                        StringComparison.OrdinalIgnoreCase)))
            {
                array.Add(id);
            }
        }

        private void RemoveFuseTrackRemoval(string kind, string id)
        {
            if (!_fuseNativeDocument || string.IsNullOrWhiteSpace(id))
                return;
            var removals = _document?["tracks"]?["removals"] as JObject;
            var array = removals?[kind] as JArray;
            array?.Where(token => string.Equals(
                    token.Value<string>(),
                    id,
                    StringComparison.OrdinalIgnoreCase))
                .ToList()
                .ForEach(token => token.Remove());
        }

        private JArray EnsureFuseRemovalArray(string kind)
        {
            var tracks = (JObject)_document["tracks"];
            var removals = tracks["removals"] as JObject;
            if (removals == null)
            {
                removals = new JObject();
                tracks["removals"] = removals;
            }
            var array = removals[kind] as JArray;
            if (array == null)
            {
                array = new JArray();
                removals[kind] = array;
            }
            return array;
        }

        private void ResetOpenGraphIdentitySets()
        {
            _documentNodeIdsAtOpen.Clear();
            _documentSegmentIdsAtOpen.Clear();
            _runtimeNodeIdsAtOpen.Clear();
            _runtimeSegmentIdsAtOpen.Clear();
            foreach (var property in NodesObject.Properties())
                _documentNodeIdsAtOpen.Add(property.Name);
            foreach (var property in SegmentsObject.Properties())
                _documentSegmentIdsAtOpen.Add(property.Name);
            foreach (var node in _graph.Nodes)
            {
                if (node != null)
                    _runtimeNodeIdsAtOpen.Add(node.id);
            }
            foreach (var segment in _graph.Segments)
            {
                if (segment != null)
                    _runtimeSegmentIdsAtOpen.Add(segment.id);
            }
        }

        private JObject NodesObject =>
            (JObject)((JObject)_document["tracks"])["nodes"];

        private JObject SegmentsObject =>
            (JObject)((JObject)_document["tracks"])["segments"];

        private static JObject Vector(Vector3 value)
        {
            return new JObject
            {
                ["x"] = value.x,
                ["y"] = value.y,
                ["z"] = value.z,
            };
        }

        private static void EnsureTrackObjects(JObject document)
        {
            var tracks = document["tracks"] as JObject;
            if (tracks == null)
            {
                tracks = new JObject();
                document["tracks"] = tracks;
            }
            if (!(tracks["nodes"] is JObject))
                tracks["nodes"] = new JObject();
            if (!(tracks["segments"] is JObject))
                tracks["segments"] = new JObject();
            if (!(tracks["spans"] is JObject))
                tracks["spans"] = new JObject();
        }

        private static bool IsFuseNativeDocument(
            string path,
            JObject document)
        {
            if (!string.IsNullOrWhiteSpace(path)
                && path.EndsWith(
                    ".fuse.json",
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            var segments = document?["tracks"]?["segments"] as JObject;
            return segments?.Properties()
                .Select(property => property.Value as JObject)
                .Any(segment =>
                    segment?["startNodeId"] != null
                    || segment?["endNodeId"] != null) == true;
        }

        private string NextNodeId()
        {
            return FindAvailableNodeId(true);
        }

        private string FindAvailableNodeId(bool consume)
        {
            var sequence = Mathf.Max(1, _newNodeIdSequence);
            string id;
            do
            {
                id = _newNodeIdPrefix
                     + (string.IsNullOrWhiteSpace(
                            _newNodeIdBaseName)
                         ? string.Empty
                         : _newNodeIdBaseName + "_")
                     + sequence.ToString(
                         "000",
                         CultureInfo.InvariantCulture);
                sequence++;
            } while (_graph != null && _graph.GetNode(id) != null);
            if (consume)
                _newNodeIdSequence = sequence;
            return id;
        }

        private static string NormalizeNodeIdPart(
            string value,
            string fallback)
        {
            var normalized = new string(
                (value ?? string.Empty)
                .Trim()
                .Where(character =>
                    char.IsLetterOrDigit(character)
                    || character == '_'
                    || character == '-')
                .Take(40)
                .ToArray());
            return string.IsNullOrWhiteSpace(normalized)
                ? fallback
                : normalized;
        }

        private string NextSegmentId()
        {
            string id;
            do
            {
                id = "S_TE_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            } while (_graph.GetSegment(id) != null);
            return id;
        }

        private TrackNode RequireNode()
        {
            RequireSession();
            return _selectedNode != null
                ? _selectedNode
                : throw new InvalidOperationException("Click a cyan node first.");
        }

        private TrackSegment RequireSegment()
        {
            RequireSession();
            return _selectedSegment != null
                ? _selectedSegment
                : throw new InvalidOperationException("Click a yellow segment first.");
        }

        private void RequireSession()
        {
            if (!GraphOpen)
                throw new InvalidOperationException(
                    "Choose a Tile Editor graph edit layer first.");
        }

        private static float GradeFromRotation(Vector3 rotation)
        {
            return -Mathf.Tan(
                NormalizeSignedAngle(rotation.x) * Mathf.Deg2Rad) * 100f;
        }

        private static float PitchFromGrade(float grade)
        {
            return -Mathf.Atan(grade / 100f) * Mathf.Rad2Deg;
        }

        private static Vector3 HorizontalForward(float yaw)
        {
            var radians = yaw * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians));
        }

        private static float NormalizeSignedAngle(float degrees)
        {
            degrees %= 360f;
            if (degrees > 180f)
                degrees -= 360f;
            if (degrees < -180f)
                degrees += 360f;
            return degrees;
        }

        private static float SmoothStep(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * (3f - 2f * value);
        }

        private static void ValidateLength(float length)
        {
            if (length < 0.5f || length > 5000f)
                throw new InvalidOperationException(
                    "Length must be between 0.5 and 5000 m.");
        }

        private static void ValidateWyeDimension(
            float value,
            float minimum,
            float maximum,
            string label)
        {
            if (value < minimum || value > maximum)
            {
                throw new InvalidOperationException(
                    label + " must be between "
                    + minimum.ToString("0.#", CultureInfo.InvariantCulture)
                    + " and "
                    + maximum.ToString("0.#", CultureInfo.InvariantCulture)
                    + " m.");
            }
        }

        private static void ValidateGrade(float grade)
        {
            if (grade < -15f || grade > 15f)
                throw new InvalidOperationException(
                    "Grade must be between -15% and +15%.");
        }

        private static void ValidateVector(Vector3 value, string label)
        {
            if (float.IsNaN(value.x) || float.IsInfinity(value.x)
                || float.IsNaN(value.y) || float.IsInfinity(value.y)
                || float.IsNaN(value.z) || float.IsInfinity(value.z))
            {
                throw new InvalidOperationException(
                    "Enter finite numbers for " + label + ".");
            }
        }

        private readonly struct PathPoint
        {
            internal readonly Vector3 Position;
            internal readonly Vector3 Rotation;

            internal PathPoint(Vector3 position, Vector3 rotation)
            {
                Position = position;
                Rotation = rotation;
            }
        }
    }
}
