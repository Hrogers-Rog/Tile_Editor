using System;
using System.IO;
using UnityEngine;
using UnityModManagerNet;

namespace Hrogers.TileEditorBridge
{
    internal sealed partial class TileEditorBridgePanel : MonoBehaviour
    {
        [Serializable]
        private sealed class EditorState
        {
            public int protocolVersion;
            public long timestamp;
            public bool gameConnected;
            public bool projectLoaded;
            public string projectName;
            public string layerName;
            public string layerPath;
            public bool geoPanelOpen;
            public string geoMode;
            public string selectionKind;
            public string selectionId;
            public bool liveApply;
            public bool dirty;
            public bool graphDirty;
            public bool terrainDirty;
            public int terrainDirtyCount;
            public bool canUndo;
            public int pendingChanges;
            public int nodeCount;
            public int segmentCount;
            public int sceneryCount;
            public string status;
        }

        [Serializable]
        private sealed class EditorCommand
        {
            public string requestId;
            public string action;
            public string payload;
            public long sentAt;
        }

        [Serializable]
        private sealed class GamePanelState
        {
            public int protocolVersion;
            public long timestamp;
            public bool loaded;
            public string panelVersion;
            public string lastCommandStatus;
            public bool graphDirty;
            public bool splineyDirty;
            public bool telegraphPoleDirty;
            public bool terrainDirty;
            public string graphPath;
        }

        [Serializable]
        private sealed class BridgeCommandAck
        {
            public string requestId;
            public string action;
            public string status;
            public string message;
            public long receivedAt;
        }

        private const int WindowId = 0x544542;
        private const int NodeWindowId = 0x54454E;
        private const float ReadInterval = 0.35f;
        private const float HeartbeatInterval = 0.75f;
        private const long OnlineWindowMs = 2500;
        private const float MinWindowWidth = 430f;
        private const float MinWindowHeight = 470f;
        private const float MinNodeWindowWidth = 500f;
        private const float MinNodeWindowHeight = 470f;
        private const string WindowWidthKey =
            "Hrogers.TileEditorBridge.WindowWidth";
        private const string WindowHeightKey =
            "Hrogers.TileEditorBridge.WindowHeight";
        private const string NodeWindowXKey =
            "Hrogers.TileEditorBridge.NodeWindowX";
        private const string NodeWindowYKey =
            "Hrogers.TileEditorBridge.NodeWindowY";
        private const string NodeWindowWidthKey =
            "Hrogers.TileEditorBridge.NodeWindowWidth";
        private const string NodeWindowHeightKey =
            "Hrogers.TileEditorBridge.NodeWindowHeight";
        private const string LastGraphPathKey =
            "Hrogers.TileEditorBridge.LastGraphPath";
        private const string TrackBuildGaugeKey =
            "Hrogers.TileEditorBridge.TrackBuildGauge";
        private const string DeferredTrackRebuildsKey =
            "Hrogers.TileEditorBridge.DeferredTrackRebuilds";
        private const string NodeIdPrefixKey =
            "Hrogers.TileEditorBridge.NodeIdPrefix";
        private const string NodeIdBaseNameKey =
            "Hrogers.TileEditorBridge.NodeIdBaseName";

        private UnityModManager.ModEntry.ModLogger _logger;
        private Rect _windowRect = new Rect(24f, 72f, 470f, 660f);
        private Rect _nodeWindowRect =
            new Rect(506f, 72f, 570f, 700f);
        private Vector2 _nodeWindowScroll;
        private bool _nodeEditorVisible;
        private bool _resizingNodeWindow;
        private Vector2 _nodeResizeStartMouse;
        private Vector2 _nodeResizeStartSize;
        private bool _runtimeEnabled = true;
        private bool _visible;
        private float _nextReadAt;
        private float _nextHeartbeatAt;
        private string _bridgeDirectory;
        private string _statePath;
        private string _commandPath;
        private string _gamePanelStatePath;
        private string _bridgeCommandPath;
        private string _bridgeCommandAckPath;
        private string _lastBridgeCommandId;
        private string _lastBridgeCommandStatus = "ready";
        private EditorState _state;
        private string _lastPanelMessage =
            "Desktop editor optional - choose an installed mod graph";
        private string _preferredGraphPath = string.Empty;
        private string _autoOpenAttemptPath = string.Empty;
        private string _nodeIdPrefix = "N_TE_";
        private string _nodeIdBaseName = string.Empty;
        private GUIStyle _titleStyle;
        private GUIStyle _lineStyle;
        private GUIStyle _mutedStyle;
        private GUIStyle _onlineStyle;
        private GUIStyle _offlineStyle;
        private GUIStyle _directionButtonStyle;
        private Texture2D _windowBackgroundTexture;
        private Texture2D _windowBorderTexture;
        private bool _resizingWindow;
        private Vector2 _resizeStartMouse;
        private Vector2 _resizeStartSize;
        private CursorLockMode _savedCursorLock;
        private bool _savedCursorVisible;
        private bool _cursorCaptured;
        private TileEditorGraphSession _mapEditor;
        private TileEditorBridgeFileWriter _heartbeatWriter;

        internal void Initialize(UnityModManager.ModEntry.ModLogger logger)
        {
            _logger = logger;
            var gameRoot = Directory.GetParent(Application.dataPath)?.FullName
                           ?? AppDomain.CurrentDomain.BaseDirectory;
            _bridgeDirectory = Path.Combine(gameRoot, "Mods", "TrackBridge");
            _statePath = Path.Combine(_bridgeDirectory, "editor_state.json");
            _commandPath = Path.Combine(_bridgeDirectory, "editor_commands.json");
            _gamePanelStatePath = Path.Combine(
                _bridgeDirectory, "game_panel_state.json");
            _heartbeatWriter = new TileEditorBridgeFileWriter(
                _gamePanelStatePath);
            _bridgeCommandPath = Path.Combine(
                _bridgeDirectory, "bridge_commands.json");
            _bridgeCommandAckPath = Path.Combine(
                _bridgeDirectory, "bridge_command_ack.json");
            Directory.CreateDirectory(_bridgeDirectory);
            InitializeTrackToolProfiles();
            _mapEditor = new TileEditorGraphSession(_logger, gameRoot);
            _mapEditor.SetDeferredTrackRebuilds(
                PlayerPrefs.GetInt(
                    DeferredTrackRebuildsKey,
                    0) != 0);
            InitializeOsmOverlay(gameRoot);
            _trackBuildGauge = PlayerPrefs.GetString(
                TrackBuildGaugeKey,
                "Standard");
            _mapEditor.NewTrackGauge = _trackBuildGauge;
            _nodeIdPrefix = PlayerPrefs.GetString(
                NodeIdPrefixKey,
                "N_TE_");
            _nodeIdBaseName = PlayerPrefs.GetString(
                NodeIdBaseNameKey,
                string.Empty);
            _mapEditor.ConfigureNewNodeIds(
                _nodeIdPrefix,
                _nodeIdBaseName);
            _preferredGraphPath = PlayerPrefs.GetString(
                LastGraphPathKey,
                string.Empty);
            _windowRect.width = Mathf.Max(
                MinWindowWidth,
                PlayerPrefs.GetFloat(WindowWidthKey, _windowRect.width));
            _windowRect.height = Mathf.Max(
                MinWindowHeight,
                PlayerPrefs.GetFloat(WindowHeightKey, _windowRect.height));
            _nodeWindowRect.x = PlayerPrefs.GetFloat(
                NodeWindowXKey,
                _nodeWindowRect.x);
            _nodeWindowRect.y = PlayerPrefs.GetFloat(
                NodeWindowYKey,
                _nodeWindowRect.y);
            _nodeWindowRect.width = Mathf.Max(
                MinNodeWindowWidth,
                PlayerPrefs.GetFloat(
                    NodeWindowWidthKey,
                    _nodeWindowRect.width));
            _nodeWindowRect.height = Mathf.Max(
                MinNodeWindowHeight,
                PlayerPrefs.GetFloat(
                    NodeWindowHeightKey,
                    _nodeWindowRect.height));
            WriteGamePanelHeartbeat();
        }

        internal void SetRuntimeEnabled(bool enabled)
        {
            _runtimeEnabled = enabled;
            enabled = enabled && _visible;
            gameObject.SetActive(_runtimeEnabled);
            if (enabled)
            {
                _mapEditor?.SetEditMode(true);
                CaptureCursor();
                SetGameInputLock(true);
            }
            else
            {
                _mapEditor?.SetEditMode(false);
                SetGameInputLock(false);
                RestoreCursor();
            }
        }

        internal void Show()
        {
            SetVisible(true);
        }

        private void Update()
        {
            if (!_runtimeEnabled)
                return;
            if (Input.GetKeyDown(KeyCode.F9))
                SetVisible(!_visible);
            TileEditorCameraInput.PointerOverEditorWindow =
                _visible && IsPointerOverEditorWindow();
            TileEditorCameraInput.WorldEditPointerActive =
                _visible && DoesWorldEditorConsumePrimaryPointer();
            HandleCameraNavigationToggle();
            MaintainGameInputLock();
            UpdateSurveyHud();
            _mapEditor?.SetExternalEditorLocks(
                DesktopGraphHasUnsavedChanges,
                DesktopTerrainHasUnsavedChanges);
            var terrainRebuildStatus =
                _mapEditor?.PollTerrainRebuildStatus();
            if (!string.IsNullOrWhiteSpace(terrainRebuildStatus))
                _lastPanelMessage = terrainRebuildStatus;
            // Editing is independent of the camera preference. FREE keeps
            // Railroader's normal camera available; LOCKED supplies the
            // precise keyboard camera. Both modes must keep selection,
            // dragging, terrain strokes, and pointer placement alive.
            HandleUniversalDeselectInput();
            _mapEditor?.UpdateNodeDragFromPointer(
                IsPointerOverEditorWindow());
            UpdateWorldPointerTools();
            UpdateOsmOverlay();
            if (Time.unscaledTime >= _nextReadAt)
            {
                _nextReadAt = Time.unscaledTime + ReadInterval;
                ReadEditorState();
                ReadBridgeCommand();
                var editorOnline = IsEditorOnline();
                if (_visible
                    && _mapEditor != null
                    && _mapEditor.Available
                    && !_mapEditor.GraphOpen)
                {
                    var automaticPath =
                        editorOnline
                        && _state != null
                        && !string.IsNullOrWhiteSpace(_state.layerPath)
                            ? _state.layerPath
                            : _preferredGraphPath;
                    if (!string.IsNullOrWhiteSpace(automaticPath)
                        && !string.Equals(
                            automaticPath,
                            _autoOpenAttemptPath,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        _autoOpenAttemptPath = automaticPath;
                        try
                        {
                            if (_mapEditor.TryOpenGraph(automaticPath))
                            {
                                _lastPanelMessage =
                                    editorOnline
                                        ? "Opened the live desktop graph layer"
                                        : "Opened the remembered in-game mod graph";
                            }
                        }
                        catch (Exception ex)
                        {
                            _lastPanelMessage =
                                "Could not auto-open graph: " + ex.Message;
                            _logger?.Warning(
                                "Could not auto-open graph layer: " + ex);
                        }
                    }
                }
                else if (_mapEditor == null || !_mapEditor.Available)
                    _autoOpenAttemptPath = string.Empty;
                _mapEditor?.RefreshEditMode();
            }
            if (Time.unscaledTime >= _nextHeartbeatAt)
            {
                _nextHeartbeatAt = Time.unscaledTime + HeartbeatInterval;
                WriteGamePanelHeartbeat();
            }
        }

        private void OnGUI()
        {
            if (!_runtimeEnabled || !_visible)
                return;
            EnsureStyles();
            _windowRect = GUI.Window(
                WindowId,
                _windowRect,
                DrawTileEditorWindow,
                "Tile Editor - In-Game Geo v" + SuiteVersion.Value);
            _windowRect.x = Mathf.Clamp(_windowRect.x, 0f, Mathf.Max(0f, Screen.width - 80f));
            _windowRect.y = Mathf.Clamp(_windowRect.y, 0f, Mathf.Max(0f, Screen.height - 40f));
            _windowRect.width = Mathf.Clamp(
                _windowRect.width,
                MinWindowWidth,
                Mathf.Max(MinWindowWidth, Screen.width - _windowRect.x - 4f));
            _windowRect.height = Mathf.Clamp(
                _windowRect.height,
                MinWindowHeight,
                Mathf.Max(MinWindowHeight, Screen.height - _windowRect.y - 4f));
            if (_nodeEditorVisible)
            {
                _nodeWindowRect = GUI.Window(
                    NodeWindowId,
                    _nodeWindowRect,
                    DrawNodeEditorWindow,
                    "Tile Editor - Node Editor v" + SuiteVersion.Value);
                _nodeWindowRect.x = Mathf.Clamp(
                    _nodeWindowRect.x,
                    0f,
                    Mathf.Max(0f, Screen.width - 80f));
                _nodeWindowRect.y = Mathf.Clamp(
                    _nodeWindowRect.y,
                    0f,
                    Mathf.Max(0f, Screen.height - 40f));
                _nodeWindowRect.width = Mathf.Clamp(
                    _nodeWindowRect.width,
                    MinNodeWindowWidth,
                    Mathf.Max(
                        MinNodeWindowWidth,
                        Screen.width - _nodeWindowRect.x - 4f));
                _nodeWindowRect.height = Mathf.Clamp(
                    _nodeWindowRect.height,
                    MinNodeWindowHeight,
                    Mathf.Max(
                        MinNodeWindowHeight,
                        Screen.height - _nodeWindowRect.y - 4f));
            }
            DrawSurveyHud();
        }

        private void HandleCameraNavigationToggle()
        {
            if (!_visible
                || !TileEditorCameraInput.EditorInputActive
                || !Input.GetMouseButtonDown(2))
            {
                return;
            }
            ToggleCameraNavigationLock();
        }

        private void ToggleCameraNavigationLock()
        {
            var locked = !TileEditorCameraInput.MouseCameraLocked;
            TileEditorCameraInput.SetMouseCameraLocked(locked);
            MaintainGameInputLock(force: true);
            _lastPanelMessage = locked
                ? "Camera locked: WASD move, wheel zoom, Q/E rotate"
                : "Camera free: normal mouse camera; editing remains active";
        }

        private void DrawWindow(int id)
        {
            var online = IsEditorOnline();
            GUILayout.BeginVertical();
            GUILayout.BeginHorizontal();
            GUILayout.Label(online ? "● DESKTOP ONLINE" : "● DESKTOP OFFLINE",
                online ? _onlineStyle : _offlineStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("×", GUILayout.Width(28f), GUILayout.Height(22f)))
            {
                _visible = false;
                RestoreCursor();
            }
            GUILayout.EndHorizontal();

            if (_state != null && online)
            {
                GUILayout.Label(
                    _state.projectLoaded
                        ? Safe(_state.projectName) + "  /  " + Safe(_state.layerName)
                        : "No mod project loaded",
                    _titleStyle);
                GUILayout.Label(
                    $"Geo: {Pretty(_state.geoMode)}   " +
                    $"Game: {(_state.gameConnected ? "connected" : "waiting")}   " +
                    $"Save: {(_state.liveApply ? "live" : "manual")}" +
                    (_state.dirty ? $" ({Math.Max(1, _state.pendingChanges)} pending)" : ""),
                    _lineStyle);
                GUILayout.Label(
                    $"Track: {_state.nodeCount} nodes / {_state.segmentCount} segments   " +
                    $"Scenery: {_state.sceneryCount}",
                    _mutedStyle);
                var selection = string.IsNullOrWhiteSpace(_state.selectionId)
                    ? "none"
                    : Safe(_state.selectionKind) + " " + Shorten(_state.selectionId, 24);
                GUILayout.Label("Selected: " + selection, _mutedStyle);
            }
            else
            {
                GUILayout.Label("Start the desktop Tile Editor to connect.", _titleStyle);
                GUILayout.Label("Shared path: Mods/TrackBridge", _mutedStyle);
            }

            GUILayout.Space(7f);
            GUILayout.Label("Prepare desktop tools", _mutedStyle);
            GUILayout.BeginHorizontal();
            ToolButton("Arc", "set_geo_mode", "curve", online);
            ToolButton("Grade", "set_geo_mode", "grade", online);
            ToolButton("Turnout", "set_geo_mode", "turnout", online);
            ToolButton("Wye", "set_geo_mode", "wye", online);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            ToolButton("Geo Panel", "open_panel", "geo", online);
            ToolButton("Scenery", "open_panel", "scenery", online);
            ToolButton("Bring Editor Forward", "focus_editor", "", online);
            GUILayout.EndHorizontal();

            GUILayout.Space(5f);
            GUILayout.BeginHorizontal();
            GUI.enabled = online && _state != null && _state.canUndo;
            if (GUILayout.Button("Undo", GUILayout.Height(28f)))
                SendCommand("undo", "");
            GUI.enabled = online && _state != null && _state.projectLoaded;
            if (GUILayout.Button("Save + Reload", GUILayout.Height(28f)))
                SendCommand("save_reload", "");
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            if (online && _state != null && !string.IsNullOrWhiteSpace(_state.status))
                GUILayout.Label("Desktop: " + Shorten(_state.status, 48), _mutedStyle);
            GUILayout.Label(Shorten(_lastPanelMessage, 58), _mutedStyle);
            GUI.DragWindow(new Rect(0f, 0f, _windowRect.width - 42f, 28f));
            GUILayout.EndVertical();
        }

        private void ToolButton(string label, string action, string payload, bool enabled)
        {
            GUI.enabled = enabled;
            if (GUILayout.Button(label, GUILayout.Height(27f)))
                SendCommand(action, payload);
            GUI.enabled = true;
        }

        private void ReadEditorState()
        {
            try
            {
                if (!File.Exists(_statePath))
                    return;
                var json = File.ReadAllText(_statePath);
                var state = JsonUtility.FromJson<EditorState>(json);
                if (state != null)
                    _state = state;
            }
            catch (IOException)
            {
                // The desktop may be replacing the heartbeat file.
            }
            catch (Exception ex)
            {
                _lastPanelMessage = "State read failed";
                _logger?.Warning("Could not read editor_state.json: " + ex.Message);
            }
        }

        private void WriteGamePanelHeartbeat()
        {
            try
            {
                var writeError = _heartbeatWriter?.TakeLastError();
                if (!string.IsNullOrWhiteSpace(writeError))
                    _logger?.Warning(
                        "Could not write game_panel_state.json: " + writeError);
                var state = new GamePanelState
                {
                    protocolVersion = 1,
                    timestamp = UnixMilliseconds(),
                    loaded = true,
                    panelVersion = SuiteVersion.Value,
                    lastCommandStatus = _lastBridgeCommandStatus,
                    graphDirty = _mapEditor != null
                                 && _mapEditor.Dirty,
                    splineyDirty = _mapEditor != null
                                  && _mapEditor.SplineyDirty,
                    telegraphPoleDirty = _mapEditor != null
                                         && _mapEditor.TelegraphPoleDirty,
                    terrainDirty = _mapEditor != null
                                   && _mapEditor.TerrainDirty,
                    graphPath = _mapEditor?.GraphPath
                                ?? string.Empty,
                };
                _heartbeatWriter?.QueueLatest(JsonUtility.ToJson(state));
            }
            catch (Exception ex)
            {
                _logger?.Warning(
                    "Could not write game_panel_state.json: " + ex.Message);
            }
        }

        private void ReadBridgeCommand()
        {
            try
            {
                if (!File.Exists(_bridgeCommandPath))
                    return;
                var command = JsonUtility.FromJson<EditorCommand>(
                    File.ReadAllText(_bridgeCommandPath));
                if (command == null || string.IsNullOrWhiteSpace(command.action))
                    return;

                var commandId = !string.IsNullOrWhiteSpace(command.requestId)
                    ? command.requestId
                    : command.sentAt + "|" + command.action + "|" + command.payload;
                if (commandId == _lastBridgeCommandId)
                    return;
                _lastBridgeCommandId = commandId;

                if (command.sentAt > 0
                    && UnixMilliseconds() - command.sentAt > 120000)
                {
                    _lastBridgeCommandStatus = "ignored stale " + command.action;
                    return;
                }

                var status = "ok";
                var message = "";
                switch ((command.action ?? "").Trim().ToLowerInvariant())
                {
                    case "ping":
                        message = "Tile Editor Bridge is alive";
                        break;
                    case "reload_tracks":
                        if (string.IsNullOrWhiteSpace(command.payload)
                            || !File.Exists(command.payload))
                        {
                            status = "error";
                            message = "Reload target was not found";
                        }
                        else
                        {
                            // Strange Customs watches the graph file. Touching
                            // it produces a fresh change event even if the
                            // desktop save completed just before this command.
                            File.SetLastWriteTimeUtc(command.payload, DateTime.UtcNow);
                            var editorReload =
                                _mapEditor?.ReloadGraphFromDesktop(
                                    command.payload);
                            message = string.IsNullOrWhiteSpace(editorReload)
                                ? "Signaled Strange Customs reload"
                                : editorReload;
                        }
                        break;
                    case "reload_terrain_tiles":
                        if (string.IsNullOrWhiteSpace(command.payload))
                        {
                            status = "error";
                            message =
                                "No terrain tile paths were supplied";
                        }
                        else if (_mapEditor == null)
                        {
                            status = "error";
                            message =
                                "Tile Editor terrain session is not ready";
                        }
                        else
                        {
                            message =
                                _mapEditor.ReloadTerrainTilesFromDesktop(
                                    command.payload
                                        .Split(
                                            new[] { '\r', '\n' },
                                            StringSplitOptions
                                                .RemoveEmptyEntries));
                        }
                        break;
                    default:
                        status = "unsupported";
                        message = "Unknown command: " + command.action;
                        break;
                }

                _lastBridgeCommandStatus = status + " " + command.action;
                var ack = new BridgeCommandAck
                {
                    requestId = commandId,
                    action = command.action,
                    status = status,
                    message = message,
                    receivedAt = UnixMilliseconds(),
                };
                AtomicWrite(_bridgeCommandAckPath, JsonUtility.ToJson(ack));
                _logger?.Log(
                    "Bridge command " + command.action + ": "
                    + status + " - " + message);
            }
            catch (IOException)
            {
                // A desktop replace may be in flight.
            }
            catch (Exception ex)
            {
                _lastBridgeCommandStatus = "command error";
                _logger?.Warning(
                    "Could not process bridge_commands.json: " + ex.Message);
            }
        }

        private void SendCommand(string action, string payload)
        {
            try
            {
                Directory.CreateDirectory(_bridgeDirectory);
                var now = UnixMilliseconds();
                var command = new EditorCommand
                {
                    requestId = now + "-" + Guid.NewGuid().ToString("N"),
                    action = action,
                    payload = payload ?? string.Empty,
                    sentAt = now,
                };
                AtomicWrite(_commandPath, JsonUtility.ToJson(command));
                _lastPanelMessage = "Sent: " + Pretty(action)
                                    + (string.IsNullOrWhiteSpace(payload) ? "" : " / " + Pretty(payload));
            }
            catch (Exception ex)
            {
                _lastPanelMessage = "Command failed";
                _logger?.Error("Could not write editor command: " + ex);
            }
        }

        private static void AtomicWrite(string path, string contents)
        {
            var tempPath = path + ".tmp";
            File.WriteAllText(tempPath, contents);
            if (File.Exists(path))
            {
                try
                {
                    File.Replace(tempPath, path, null);
                }
                catch
                {
                    File.Delete(path);
                    File.Move(tempPath, path);
                }
            }
            else
            {
                File.Move(tempPath, path);
            }
        }

        private bool IsEditorOnline()
        {
            return _state != null
                   && _state.timestamp > 0
                   && UnixMilliseconds() - _state.timestamp <= OnlineWindowMs;
        }

        private void CaptureCursor()
        {
            if (_cursorCaptured)
                return;
            _savedCursorLock = Cursor.lockState;
            _savedCursorVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            _cursorCaptured = true;
        }

        private void RestoreCursor()
        {
            if (!_cursorCaptured)
                return;
            Cursor.lockState = _savedCursorLock;
            Cursor.visible = _savedCursorVisible;
            _cursorCaptured = false;
        }

        private void OnDestroy()
        {
            TileEditorCameraInput.PointerOverEditorWindow = false;
            TileEditorCameraInput.WorldEditPointerActive = false;
            SaveNodeWindowGeometry();
            EndTerrainStroke();
            DisposeWorldPointerTools();
            DisposeOsmOverlay();
            SetGameInputLock(false);
            _mapEditor?.Dispose();
            _mapEditor = null;
            _heartbeatWriter?.Dispose();
            _heartbeatWriter = null;
            if (_windowBackgroundTexture != null)
                Destroy(_windowBackgroundTexture);
            if (_windowBorderTexture != null)
                Destroy(_windowBorderTexture);
            RestoreCursor();
        }

        private void SetVisible(bool visible)
        {
            _visible = visible;
            if (!visible)
            {
                TileEditorCameraInput.PointerOverEditorWindow = false;
                TileEditorCameraInput.WorldEditPointerActive = false;
                SaveNodeWindowGeometry();
                _mapEditor?.CancelNodeDrag();
                CancelPointerPlacement(false);
                EndTerrainStroke();
            }
            _mapEditor?.SetEditMode(visible && _runtimeEnabled);
            if (_visible)
            {
                CaptureCursor();
                SetGameInputLock(_runtimeEnabled);
            }
            else
            {
                SetGameInputLock(false);
                RestoreCursor();
            }
        }

        private void EnsureStyles()
        {
            if (_titleStyle != null)
                return;
            _windowBackgroundTexture = MakeSolidTexture(
                new Color(0.025f, 0.032f, 0.043f, 0.985f));
            _windowBorderTexture = MakeSolidTexture(
                new Color(0.25f, 0.72f, 0.78f, 0.9f));
            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                wordWrap = false,
                normal = { textColor = new Color(0.92f, 0.96f, 0.98f) },
            };
            _lineStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                wordWrap = true,
                normal = { textColor = new Color(0.86f, 0.9f, 0.94f) },
            };
            _mutedStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                wordWrap = true,
                normal = { textColor = new Color(0.72f, 0.76f, 0.82f) },
            };
            _onlineStyle = new GUIStyle(_titleStyle)
            {
                normal = { textColor = new Color(0.35f, 0.92f, 0.52f) },
            };
            _offlineStyle = new GUIStyle(_titleStyle)
            {
                normal = { textColor = new Color(1f, 0.48f, 0.38f) },
            };
            _directionButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 17,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
                padding = new RectOffset(5, 5, 4, 4),
                normal =
                {
                    textColor = new Color(0.96f, 0.98f, 1f),
                },
                hover =
                {
                    textColor = new Color(0.38f, 0.94f, 1f),
                },
                active =
                {
                    textColor = new Color(1f, 0.86f, 0.32f),
                },
            };
        }

        private static Texture2D MakeSolidTexture(Color color)
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                name = "TileEditorPanelColor",
                hideFlags = HideFlags.HideAndDontSave,
            };
            texture.SetPixel(0, 0, color);
            texture.Apply(false, true);
            return texture;
        }

        private static long UnixMilliseconds()
        {
            return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds;
        }

        private static string Safe(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "—" : value;
        }

        private static string Pretty(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "—";
            var text = value.Replace('_', ' ').Trim();
            return char.ToUpperInvariant(text[0]) + text.Substring(1);
        }

        private static string Shorten(string value, int maxLength)
        {
            value = value ?? string.Empty;
            if (value.Length <= maxLength)
                return value;
            return value.Substring(0, Math.Max(1, maxLength - 1)) + "…";
        }
    }
}
