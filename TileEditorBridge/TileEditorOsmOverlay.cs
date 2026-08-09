using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Helpers;
using Map.Runtime;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Rendering;

namespace Hrogers.TileEditorBridge
{
    internal sealed partial class TileEditorBridgePanel
    {
        private readonly struct OsmTileKey : IEquatable<OsmTileKey>
        {
            internal readonly int Zoom;
            internal readonly int X;
            internal readonly int Y;

            internal OsmTileKey(int zoom, int x, int y)
            {
                Zoom = zoom;
                X = x;
                Y = y;
            }

            public bool Equals(OsmTileKey other)
            {
                return Zoom == other.Zoom
                       && X == other.X
                       && Y == other.Y;
            }

            public override bool Equals(object obj)
            {
                return obj is OsmTileKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = Zoom;
                    hash = hash * 397 ^ X;
                    hash = hash * 397 ^ Y;
                    return hash;
                }
            }

            public override string ToString()
            {
                return Zoom + "/" + X + "/" + Y;
            }
        }

        private sealed class OsmTextureRecord
        {
            internal Texture2D Texture;
            internal Material Material;
        }

        private sealed class OsmOverlayChunk
        {
            internal GameObject Object;
            internal Mesh Mesh;
            internal OsmTileKey TextureKey;
        }

        private sealed class OsmGameTileOverlay
        {
            internal GameObject Root;
            internal MapTerrain Terrain;
            internal readonly Dictionary<OsmTileKey, OsmOverlayChunk>
                Chunks =
                    new Dictionary<OsmTileKey, OsmOverlayChunk>();
        }

        private const string OsmEnabledKey =
            "Hrogers.TileEditorBridge.OsmEnabled";
        private const string OsmWindowSizeKey =
            "Hrogers.TileEditorBridge.OsmWindowSize";
        private const string OsmZoomKey =
            "Hrogers.TileEditorBridge.OsmZoom";
        private const string OsmOpacityKey =
            "Hrogers.TileEditorBridge.OsmOpacity";
        private const string OsmLineworkKey =
            "Hrogers.TileEditorBridge.OsmLinework";
        private const string OsmTileUrl =
            "https://tile.openstreetmap.org/{0}/{1}/{2}.png";
        private const int OsmMeshResolution = 32;
        private const int MaximumConcurrentOsmDownloads = 2;
        private const int MinimumOsmZoom = 15;
        private const int MaximumOsmZoom = 18;
        private const int DefaultOsmZoom = 17;
        private const double MetersPerDegree = 111111.0;

        private readonly Dictionary<Vector2Int, OsmGameTileOverlay>
            _osmGameTiles =
                new Dictionary<Vector2Int, OsmGameTileOverlay>();
        private readonly Dictionary<OsmTileKey, OsmTextureRecord>
            _osmTextures =
                new Dictionary<OsmTileKey, OsmTextureRecord>();
        private readonly HashSet<OsmTileKey> _osmPending =
            new HashSet<OsmTileKey>();
        private readonly Queue<OsmTileKey> _osmDownloadQueue =
            new Queue<OsmTileKey>();
        private readonly Dictionary<OsmTileKey, float> _osmRetryAfter =
            new Dictionary<OsmTileKey, float>();
        private readonly HashSet<OsmTileKey> _osmDesiredTextures =
            new HashSet<OsmTileKey>();
        private GameObject _osmOverlayRoot;
        private bool _osmOverlayEnabled;
        private int _osmWindowSize = 5;
        private int _osmZoom = DefaultOsmZoom;
        private float _osmOpacity = 0.55f;
        private bool _osmLineworkMode = true;
        private int _osmDownloadsActive;
        private int _osmCacheGeneration;
        private int _osmDiskCacheFiles;
        private long _osmDiskCacheBytes;
        private bool _confirmClearOsmCache;
        private float _nextOsmRefreshAt;
        private Vector2Int _osmLastCenter =
            new Vector2Int(int.MinValue, int.MinValue);
        private string _osmCacheDirectory = string.Empty;
        private string _osmMapIdentity = string.Empty;
        private string _osmMapSource = string.Empty;
        private string _osmStatus = "OSM overlay is off";
        private double _osmOriginLatitude = 35.382614;
        private double _osmOriginLongitude = -83.49541;
        private double _osmTileDimension = 500.0;
        private double _osmEastBias = 8.0;
        private double _osmNorthBias = -8.0;

        private void InitializeOsmOverlay(string gameRoot)
        {
            _osmOverlayEnabled =
                PlayerPrefs.GetInt(OsmEnabledKey, 0) != 0;
            _osmWindowSize =
                PlayerPrefs.GetInt(OsmWindowSizeKey, 5) == 8
                    ? 8
                    : 5;
            _osmZoom = Mathf.Clamp(
                PlayerPrefs.GetInt(
                    OsmZoomKey,
                    DefaultOsmZoom),
                MinimumOsmZoom,
                MaximumOsmZoom);
            _osmOpacity = Mathf.Clamp(
                PlayerPrefs.GetFloat(OsmOpacityKey, 0.55f),
                0.15f,
                0.9f);
            _osmLineworkMode =
                PlayerPrefs.GetInt(OsmLineworkKey, 1) != 0;
            _osmCacheDirectory = Path.Combine(
                gameRoot,
                "Mods",
                "Hrogers.TileEditorBridge",
                "Cache",
                "OSM");
            Directory.CreateDirectory(_osmCacheDirectory);
            RefreshOsmDiskCacheStats();
            _osmStatus = _osmOverlayEnabled
                ? "Waiting for loaded terrain"
                : "OSM overlay is off";
        }

        private void DrawOsmOverlayControls()
        {
            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            GUILayout.Label("LOCAL OSM MAP GUIDE", _titleStyle);
            GUILayout.FlexibleSpace();
            var oldColor = GUI.backgroundColor;
            if (_osmOverlayEnabled)
                GUI.backgroundColor =
                    new Color(0.18f, 0.72f, 0.82f);
            if (GUILayout.Button(
                    _osmOverlayEnabled ? "ON" : "OFF",
                    GUILayout.Width(64f),
                    GUILayout.Height(26f)))
            {
                SetOsmOverlayEnabled(!_osmOverlayEnabled);
            }
            GUI.backgroundColor = oldColor;
            GUILayout.EndHorizontal();

            GUILayout.Label(
                "Streams only the terrain tiles around the camera and "
                + "drapes the map slightly above the ground.",
                _mutedStyle);
            GUILayout.BeginHorizontal();
            GUI.enabled = _osmOverlayEnabled;
            var styleColor = GUI.backgroundColor;
            if (_osmLineworkMode)
                GUI.backgroundColor =
                    new Color(0.18f, 0.72f, 0.82f);
            if (GUILayout.Button(
                    "CLEAR LINES",
                    GUILayout.Height(26f)))
            {
                SetOsmLineworkMode(true);
            }
            GUI.backgroundColor = !_osmLineworkMode
                ? new Color(0.18f, 0.72f, 0.82f)
                : styleColor;
            if (GUILayout.Button(
                    "FULL MAP",
                    GUILayout.Height(26f)))
            {
                SetOsmLineworkMode(false);
            }
            GUI.backgroundColor = styleColor;
            GUILayout.EndHorizontal();
            GUILayout.Label(
                "MAP RESOLUTION  \u2022  higher detail is concentrated "
                + "around the camera",
                _mutedStyle);
            GUILayout.BeginHorizontal();
            DrawOsmZoomPreset("OVERVIEW z15", 15);
            DrawOsmZoomPreset("DETAIL z16", 16);
            DrawOsmZoomPreset("SHARP z17", 17);
            DrawOsmZoomPreset("ULTRA z18", 18);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(
                    "5 x 5 COVERAGE",
                    GUILayout.Height(26f)))
            {
                SetOsmWindowSize(5);
            }
            if (GUILayout.Button(
                    "8 x 8 COVERAGE",
                    GUILayout.Height(26f)))
            {
                SetOsmWindowSize(8);
            }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(
                    "Fade -",
                    GUILayout.Height(26f)))
            {
                SetOsmOpacity(_osmOpacity - 0.1f);
            }
            GUILayout.Label(
                Mathf.RoundToInt(_osmOpacity * 100f) + "%",
                _lineStyle,
                GUILayout.Width(48f));
            if (GUILayout.Button(
                    "Fade +",
                    GUILayout.Height(26f)))
            {
                SetOsmOpacity(_osmOpacity + 0.1f);
            }
            if (GUILayout.Button(
                    "Refresh",
                    GUILayout.Height(26f)))
            {
                ReconfigureOsmOverlay(true);
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label(
                "CACHE  "
                + FormatOsmCacheSize(_osmDiskCacheBytes)
                + "  \u2022  "
                + _osmDiskCacheFiles.ToString(
                    "N0",
                    CultureInfo.InvariantCulture)
                + " tiles",
                _mutedStyle);
            GUILayout.FlexibleSpace();
            if (_confirmClearOsmCache)
            {
                var cacheColor = GUI.backgroundColor;
                GUI.backgroundColor =
                    new Color(0.72f, 0.28f, 0.18f);
                if (GUILayout.Button(
                        "CONFIRM CLEAR",
                        GUILayout.Width(130f),
                        GUILayout.Height(26f)))
                {
                    ClearOsmDiskCache();
                }
                GUI.backgroundColor = cacheColor;
                if (GUILayout.Button(
                        "Cancel",
                        GUILayout.Width(70f),
                        GUILayout.Height(26f)))
                {
                    _confirmClearOsmCache = false;
                }
            }
            else
            {
                GUI.enabled = _osmDiskCacheFiles > 0;
                if (GUILayout.Button(
                        "CLEAR CACHE...",
                        GUILayout.Width(130f),
                        GUILayout.Height(26f)))
                {
                    _confirmClearOsmCache = true;
                }
                GUI.enabled = true;
            }
            GUILayout.EndHorizontal();
            if (_osmOverlayEnabled)
            {
                GUILayout.Label(
                    (_osmLineworkMode
                        ? "Clear Lines"
                        : "Full Map")
                    + "  \u2022  "
                    + "z" + _osmZoom + " "
                    + FormatOsmGroundResolution(_osmZoom)
                    + "  \u2022  "
                    + _osmWindowSize + " x " + _osmWindowSize
                    + " game-tile window  \u2022  " + _osmStatus,
                    _mutedStyle);
                if (_osmZoom >= 17)
                {
                    GUILayout.Label(
                        _osmZoom == 18
                            ? "Adaptive detail: Ultra under the camera, "
                              + "Sharp nearby, Detail farther out."
                            : "Adaptive detail: Sharp near the camera, "
                              + "Detail farther out.",
                        _mutedStyle);
                }
                GUILayout.Label(
                    "\u00a9 OpenStreetMap contributors",
                    _mutedStyle);
            }
            GUILayout.Space(3f);
        }

        private void DrawOsmZoomPreset(string label, int zoom)
        {
            var oldColor = GUI.backgroundColor;
            if (_osmZoom == zoom)
            {
                GUI.backgroundColor =
                    new Color(0.18f, 0.72f, 0.82f);
            }
            if (GUILayout.Button(
                    label,
                    GUILayout.Height(26f)))
            {
                SetOsmZoom(zoom);
            }
            GUI.backgroundColor = oldColor;
        }

        private string FormatOsmGroundResolution(int zoom)
        {
            var metersPerPixel =
                156543.03392
                * Math.Cos(
                    _osmOriginLatitude
                    * Math.PI / 180.0)
                / Math.Pow(2.0, zoom);
            return metersPerPixel.ToString(
                       metersPerPixel < 1.0 ? "0.00" : "0.0",
                       CultureInfo.InvariantCulture)
                   + " m/px";
        }

        private void SetOsmOverlayEnabled(bool enabled)
        {
            _osmOverlayEnabled = enabled;
            PlayerPrefs.SetInt(
                OsmEnabledKey,
                enabled ? 1 : 0);
            PlayerPrefs.Save();
            if (_osmOverlayRoot != null)
                _osmOverlayRoot.SetActive(enabled);
            if (enabled)
            {
                _osmLastCenter =
                    new Vector2Int(int.MinValue, int.MinValue);
                _nextOsmRefreshAt = 0f;
                _osmStatus = "Starting local tile window";
            }
            else
            {
                _osmStatus = "OSM overlay is off";
                ClearQueuedOsmDownloads();
                _osmDesiredTextures.Clear();
                ClearOsmGameTileMeshes();
            }
        }

        private void SetOsmWindowSize(int size)
        {
            size = size == 8 ? 8 : 5;
            if (_osmWindowSize == size)
                return;
            _osmWindowSize = size;
            PlayerPrefs.SetInt(OsmWindowSizeKey, size);
            PlayerPrefs.Save();
            ClearQueuedOsmDownloads();
            _osmLastCenter =
                new Vector2Int(int.MinValue, int.MinValue);
            _nextOsmRefreshAt = 0f;
        }

        private void SetOsmZoom(int zoom)
        {
            zoom = Mathf.Clamp(
                zoom,
                MinimumOsmZoom,
                MaximumOsmZoom);
            if (_osmZoom == zoom)
                return;
            _osmZoom = zoom;
            PlayerPrefs.SetInt(OsmZoomKey, zoom);
            PlayerPrefs.Save();
            ClearQueuedOsmDownloads();
            ClearOsmOverlayContent();
            _nextOsmRefreshAt = 0f;
            _osmStatus =
                "Loading "
                + (zoom == 18
                    ? "Ultra"
                    : zoom == 17
                        ? "Sharp"
                        : zoom == 16
                            ? "Detail"
                            : "Overview")
                + " OSM";
        }

        private void SetOsmOpacity(float opacity)
        {
            _osmOpacity = Mathf.Clamp(opacity, 0.15f, 0.9f);
            PlayerPrefs.SetFloat(OsmOpacityKey, _osmOpacity);
            PlayerPrefs.Save();
            foreach (var record in _osmTextures.Values)
            {
                if (record?.Material != null)
                    ApplyOsmMaterialOpacity(record.Material);
            }
        }

        private void SetOsmLineworkMode(bool linework)
        {
            if (_osmLineworkMode == linework)
                return;
            _osmLineworkMode = linework;
            PlayerPrefs.SetInt(
                OsmLineworkKey,
                linework ? 1 : 0);
            PlayerPrefs.Save();
            ClearOsmOverlayContent();
            _nextOsmRefreshAt = 0f;
            _osmStatus = linework
                ? "Loading high-contrast linework"
                : "Loading full-color map";
        }

        private void UpdateOsmOverlay()
        {
            if (_osmOverlayRoot != null)
            {
                _osmOverlayRoot.transform.position =
                    WorldTransformer.GameToWorld(Vector3.zero);
                _osmOverlayRoot.SetActive(
                    _runtimeEnabled && _osmOverlayEnabled);
            }
            if (!_runtimeEnabled || !_osmOverlayEnabled)
                return;
            if (Time.unscaledTime < _nextOsmRefreshAt)
                return;
            _nextOsmRefreshAt = Time.unscaledTime + 0.45f;

            var manager = MapManager.Instance;
            var camera = Camera.main;
            if (manager == null || camera == null)
            {
                _osmStatus = "Waiting for map and camera";
                return;
            }
            ReconfigureOsmOverlay(false);

            var gamePosition =
                WorldTransformer.WorldToGame(
                    camera.transform.position);
            var center = manager.TilePositionFromPoint(gamePosition);
            if (center != _osmLastCenter)
                ClearQueuedOsmDownloads();
            var forceTerrainRefresh =
                _mapEditor != null && _mapEditor.TerrainDirty;
            _osmLastCenter = center;

            var desired = BuildOsmGameTileWindow(
                center,
                gamePosition,
                _osmWindowSize,
                Mathf.Max(1, manager.tileDimension));
            _osmDesiredTextures.Clear();
            foreach (var key in _osmGameTiles.Keys
                         .Where(key => !desired.Contains(key))
                         .ToArray())
            {
                DestroyOsmGameTile(key);
            }

            var loaded = 0;
            foreach (var key in desired
                         .OrderBy(key => Math.Max(
                             Math.Abs(key.x - center.x),
                             Math.Abs(key.y - center.y)))
                         .ThenBy(key =>
                             (key.x - center.x)
                             * (key.x - center.x)
                             + (key.y - center.y)
                             * (key.y - center.y)))
            {
                if (!manager.HasTileData(key)
                    || !manager.TryGetTerrain(
                        key,
                        out var terrain)
                    || terrain?.tileData == null
                    || terrain.buildStatus
                    == MapTerrain.BuildStatus.Pending)
                {
                    continue;
                }
                EnsureOsmGameTile(
                    key,
                    terrain,
                    ResolveOsmZoomForGameTile(
                        key,
                        center),
                    forceTerrainRefresh);
                loaded++;
            }
            TrimOsmTextureMemory();
            _osmStatus =
                loaded.ToString(CultureInfo.InvariantCulture)
                + " terrain tiles shown"
                + (_osmPending.Count > 0
                    ? " \u2022 "
                      + _osmPending.Count.ToString(
                          CultureInfo.InvariantCulture)
                      + " map images loading"
                    : string.Empty);
        }

        private static HashSet<Vector2Int> BuildOsmGameTileWindow(
            Vector2Int center,
            Vector3 gamePosition,
            int size,
            int tileDimension)
        {
            var result = new HashSet<Vector2Int>();
            var minimumX = -(size / 2);
            var minimumY = -(size / 2);
            if (size % 2 == 0)
            {
                var localX = gamePosition.x
                             - Mathf.Floor(
                                 gamePosition.x / tileDimension)
                             * tileDimension;
                var localZ = gamePosition.z
                             - Mathf.Floor(
                                 gamePosition.z / tileDimension)
                             * tileDimension;
                if (localX >= tileDimension * 0.5f)
                    minimumX++;
                if (localZ >= tileDimension * 0.5f)
                    minimumY++;
            }
            for (var x = minimumX; x < minimumX + size; x++)
            {
                for (var y = minimumY; y < minimumY + size; y++)
                    result.Add(center + new Vector2Int(x, y));
            }
            return result;
        }

        private int ResolveOsmZoomForGameTile(
            Vector2Int key,
            Vector2Int center)
        {
            var radius = Math.Max(
                Math.Abs(key.x - center.x),
                Math.Abs(key.y - center.y));
            if (_osmZoom >= 18)
            {
                if (radius == 0)
                    return 18;
                if (radius <= 1)
                    return 17;
                return 16;
            }
            if (_osmZoom == 17)
                return radius <= 1 ? 17 : 16;
            return _osmZoom;
        }

        private void EnsureOsmGameTile(
            Vector2Int key,
            MapTerrain terrain,
            int zoom,
            bool refreshGeometry)
        {
            if (!_osmGameTiles.TryGetValue(
                    key,
                    out var overlay)
                || overlay.Terrain != terrain)
            {
                DestroyOsmGameTile(key);
                EnsureOsmOverlayRoot();
                overlay = new OsmGameTileOverlay
                {
                    Terrain = terrain,
                    Root = new GameObject(
                        "OSM Game Tile "
                        + key.x + " " + key.y),
                };
                overlay.Root.hideFlags =
                    HideFlags.HideAndDontSave;
                overlay.Root.transform.SetParent(
                    _osmOverlayRoot.transform,
                    false);
                _osmGameTiles[key] = overlay;
            }

            var intersections = GetOsmIntersections(
                terrain.tileData.Bounds,
                zoom);
            var allTexturesReady = true;
            foreach (var textureKey in intersections.Keys)
            {
                _osmDesiredTextures.Add(textureKey);
                if (!EnsureOsmTexture(textureKey))
                    allTexturesReady = false;
            }
            var changingResolution =
                overlay.Chunks.Count > 0
                && overlay.Chunks.Keys.Any(
                    existing => existing.Zoom != zoom);
            if (changingResolution && !allTexturesReady)
            {
                // Keep the already-visible lower-resolution coverage until
                // the complete replacement is ready. This prevents blank
                // game-tile squares while adaptive detail streams in.
                return;
            }
            foreach (var textureKey in overlay.Chunks.Keys
                         .Where(existing =>
                             !intersections.ContainsKey(existing))
                         .ToArray())
            {
                DestroyOsmChunk(overlay.Chunks[textureKey]);
                overlay.Chunks.Remove(textureKey);
            }
            foreach (var pair in intersections)
            {
                if (!_osmTextures.ContainsKey(pair.Key))
                    continue;
                if (overlay.Chunks.TryGetValue(
                        pair.Key,
                        out var existing))
                {
                    if (refreshGeometry)
                    {
                        UpdateOsmChunkGeometry(
                            existing.Mesh,
                            terrain,
                            pair.Key,
                            pair.Value);
                    }
                    continue;
                }
                overlay.Chunks[pair.Key] = CreateOsmChunk(
                    overlay.Root.transform,
                    terrain,
                    pair.Key,
                    pair.Value);
            }
        }

        private readonly struct OsmIntersection
        {
            internal readonly double TileXMinimum;
            internal readonly double TileXMaximum;
            internal readonly double TileYMinimum;
            internal readonly double TileYMaximum;

            internal OsmIntersection(
                double tileXMinimum,
                double tileXMaximum,
                double tileYMinimum,
                double tileYMaximum)
            {
                TileXMinimum = tileXMinimum;
                TileXMaximum = tileXMaximum;
                TileYMinimum = tileYMinimum;
                TileYMaximum = tileYMaximum;
            }
        }

        private Dictionary<OsmTileKey, OsmIntersection>
            GetOsmIntersections(
                Bounds gameBounds,
                int zoom)
        {
            GameToLatitudeLongitude(
                gameBounds.min.x,
                gameBounds.min.z,
                out var south,
                out var west);
            GameToLatitudeLongitude(
                gameBounds.max.x,
                gameBounds.max.z,
                out var north,
                out var east);
            LatitudeLongitudeToOsmTile(
                north,
                west,
                zoom,
                out var tileXMinimum,
                out var tileYMinimum);
            LatitudeLongitudeToOsmTile(
                south,
                east,
                zoom,
                out var tileXMaximum,
                out var tileYMaximum);

            var result =
                new Dictionary<OsmTileKey, OsmIntersection>();
            var firstX = (int)Math.Floor(tileXMinimum);
            var lastX = (int)Math.Floor(
                tileXMaximum - 0.000000001);
            var firstY = (int)Math.Floor(tileYMinimum);
            var lastY = (int)Math.Floor(
                tileYMaximum - 0.000000001);
            for (var x = firstX; x <= lastX; x++)
            {
                for (var y = firstY; y <= lastY; y++)
                {
                    result[new OsmTileKey(zoom, x, y)] =
                        new OsmIntersection(
                            Math.Max(tileXMinimum, x),
                            Math.Min(tileXMaximum, x + 1.0),
                            Math.Max(tileYMinimum, y),
                            Math.Min(tileYMaximum, y + 1.0));
                }
            }
            return result;
        }

        private OsmOverlayChunk CreateOsmChunk(
            Transform parent,
            MapTerrain terrain,
            OsmTileKey key,
            OsmIntersection intersection)
        {
            var go = new GameObject("OSM " + key);
            go.hideFlags = HideFlags.HideAndDontSave;
            go.layer = LayerMask.NameToLayer("Ignore Raycast");
            go.transform.SetParent(parent, false);
            var mesh = new Mesh
            {
                name = "Tile Editor OSM " + key,
                hideFlags = HideFlags.HideAndDontSave,
            };
            UpdateOsmChunkGeometry(
                mesh,
                terrain,
                key,
                intersection);
            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial =
                _osmTextures[key].Material;
            renderer.shadowCastingMode =
                ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage =
                LightProbeUsage.Off;
            renderer.reflectionProbeUsage =
                ReflectionProbeUsage.Off;
            return new OsmOverlayChunk
            {
                Object = go,
                Mesh = mesh,
                TextureKey = key,
            };
        }

        private void UpdateOsmChunkGeometry(
            Mesh mesh,
            MapTerrain terrain,
            OsmTileKey key,
            OsmIntersection intersection)
        {
            OsmTileToLatitudeLongitude(
                intersection.TileXMinimum,
                intersection.TileYMinimum,
                key.Zoom,
                out var north,
                out var west);
            OsmTileToLatitudeLongitude(
                intersection.TileXMaximum,
                intersection.TileYMaximum,
                key.Zoom,
                out var south,
                out var east);
            LatitudeLongitudeToGame(
                north,
                west,
                out var westX,
                out var northZ);
            LatitudeLongitudeToGame(
                south,
                east,
                out var eastX,
                out var southZ);

            var bounds = terrain.tileData.Bounds;
            westX = Math.Max(westX, bounds.min.x);
            eastX = Math.Min(eastX, bounds.max.x);
            southZ = Math.Max(southZ, bounds.min.z);
            northZ = Math.Min(northZ, bounds.max.z);

            var side = OsmMeshResolution + 1;
            var vertices = new Vector3[side * side];
            var uv = new Vector2[side * side];
            var triangles =
                new int[OsmMeshResolution
                        * OsmMeshResolution * 6];
            var triangleIndex = 0;
            for (var row = 0; row < side; row++)
            {
                var rowT = row / (float)OsmMeshResolution;
                var z = Mathf.Lerp(
                    (float)southZ,
                    (float)northZ,
                    rowT);
                var tileY = Lerp(
                    intersection.TileYMaximum,
                    intersection.TileYMinimum,
                    rowT);
                for (var column = 0; column < side; column++)
                {
                    var columnT =
                        column / (float)OsmMeshResolution;
                    var x = Mathf.Lerp(
                        (float)westX,
                        (float)eastX,
                        columnT);
                    var tileX = Lerp(
                        intersection.TileXMinimum,
                        intersection.TileXMaximum,
                        columnT);
                    var index = row * side + column;
                    vertices[index] = new Vector3(
                        x,
                        SampleOsmTerrainHeight(
                            terrain.tileData,
                            x,
                            z) + 0.12f,
                        z);
                    uv[index] = new Vector2(
                        (float)(tileX - key.X),
                        (float)(1.0 - (tileY - key.Y)));
                    if (row >= OsmMeshResolution
                        || column >= OsmMeshResolution)
                    {
                        continue;
                    }
                    var nextRow = index + side;
                    triangles[triangleIndex++] = index;
                    triangles[triangleIndex++] = nextRow;
                    triangles[triangleIndex++] = index + 1;
                    triangles[triangleIndex++] = index + 1;
                    triangles[triangleIndex++] = nextRow;
                    triangles[triangleIndex++] =
                        nextRow + 1;
                }
            }
            mesh.Clear();
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
        }

        private static float SampleOsmTerrainHeight(
            TileData tile,
            float gameX,
            float gameZ)
        {
            var bounds = tile.Bounds;
            var resolution = tile.Resolution;
            if (resolution < 2)
                return bounds.center.y;
            var sampleX = Mathf.Clamp(
                (gameX - bounds.min.x)
                / Mathf.Max(0.001f, bounds.size.x)
                * (resolution - 1),
                0f,
                resolution - 1);
            var sampleZ = Mathf.Clamp(
                (gameZ - bounds.min.z)
                / Mathf.Max(0.001f, bounds.size.z)
                * (resolution - 1),
                0f,
                resolution - 1);
            var x0 = Mathf.FloorToInt(sampleX);
            var z0 = Mathf.FloorToInt(sampleZ);
            var x1 = Mathf.Min(x0 + 1, resolution - 1);
            var z1 = Mathf.Min(z0 + 1, resolution - 1);
            var xT = sampleX - x0;
            var zT = sampleZ - z0;
            var bottom = Mathf.Lerp(
                tile.GetHeightYX(z0, x0),
                tile.GetHeightYX(z0, x1),
                xT);
            var top = Mathf.Lerp(
                tile.GetHeightYX(z1, x0),
                tile.GetHeightYX(z1, x1),
                xT);
            return Mathf.Lerp(bottom, top, zT);
        }

        private bool EnsureOsmTexture(OsmTileKey key)
        {
            if (_osmTextures.ContainsKey(key))
                return true;
            var cachePath = OsmCachePath(key);
            if (File.Exists(cachePath))
            {
                try
                {
                    var texture = CreateOsmTexture(
                        File.ReadAllBytes(cachePath));
                    if (texture != null)
                    {
                        RegisterOsmTexture(key, texture);
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    _logger?.Warning(
                        "Could not read cached OSM tile "
                        + key + ": " + ex.Message);
                }
            }

            if (_osmPending.Contains(key))
                return false;
            if (_osmRetryAfter.TryGetValue(
                    key,
                    out var retryAt)
                && Time.unscaledTime < retryAt)
            {
                return false;
            }
            _osmPending.Add(key);
            _osmDownloadQueue.Enqueue(key);
            StartQueuedOsmDownloads();
            return false;
        }

        private void StartQueuedOsmDownloads()
        {
            while (_osmDownloadsActive
                   < MaximumConcurrentOsmDownloads
                   && _osmDownloadQueue.Count > 0)
            {
                var key = _osmDownloadQueue.Dequeue();
                _osmDownloadsActive++;
                StartCoroutine(
                    DownloadOsmTile(
                        key,
                        _osmCacheGeneration));
            }
        }

        private IEnumerator DownloadOsmTile(
            OsmTileKey key,
            int cacheGeneration)
        {
            var url = string.Format(
                CultureInfo.InvariantCulture,
                OsmTileUrl,
                key.Zoom,
                key.X,
                key.Y);
            using (var request = UnityWebRequest.Get(url))
            {
                request.SetRequestHeader(
                    "User-Agent",
                    "Hrogers-Tile-Editor/"
                    + SuiteVersion.Value
                    + " (Railroader map editing)");
                yield return request.SendWebRequest();
                if (request.result
                    != UnityWebRequest.Result.Success)
                {
                    _logger?.Warning(
                        "OSM tile download failed "
                        + key + ": " + request.error);
                    _osmRetryAfter[key] =
                        Time.unscaledTime + 30f;
                }
                else
                {
                    try
                    {
                        var bytes = request.downloadHandler.data;
                        if (cacheGeneration
                            != _osmCacheGeneration)
                        {
                            throw new OperationCanceledException(
                                "OSM cache was cleared while this tile "
                                + "was downloading.");
                        }
                        var path = OsmCachePath(key);
                        var priorLength =
                            File.Exists(path)
                                ? new FileInfo(path).Length
                                : 0L;
                        Directory.CreateDirectory(
                            Path.GetDirectoryName(path)
                            ?? _osmCacheDirectory);
                        File.WriteAllBytes(path, bytes);
                        if (priorLength == 0L)
                            _osmDiskCacheFiles++;
                        _osmDiskCacheBytes =
                            Math.Max(
                                0L,
                                _osmDiskCacheBytes
                                - priorLength
                                + bytes.LongLength);
                        var texture = CreateOsmTexture(bytes);
                        if (texture == null)
                        {
                            throw new InvalidDataException(
                                "OSM server returned an invalid image.");
                        }
                        if (_osmOverlayEnabled
                            && _osmDesiredTextures.Contains(key))
                        {
                            RegisterOsmTexture(
                                key,
                                texture);
                        }
                        else
                        {
                            Destroy(texture);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // A manual clear invalidates downloads already in
                        // flight so they cannot immediately repopulate disk.
                    }
                    catch (Exception ex)
                    {
                        _logger?.Warning(
                            "Could not cache OSM tile "
                            + key + ": " + ex.Message);
                        _osmRetryAfter[key] =
                            Time.unscaledTime + 30f;
                    }
                }
            }
            if (cacheGeneration == _osmCacheGeneration)
                _osmPending.Remove(key);
            _osmDownloadsActive =
                Mathf.Max(0, _osmDownloadsActive - 1);
            _nextOsmRefreshAt = 0f;
            StartQueuedOsmDownloads();
        }

        private static Texture2D CreateOsmTexture(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return null;
            var texture = new Texture2D(
                2,
                2,
                TextureFormat.RGBA32,
                true);
            if (texture.LoadImage(bytes, false))
                return texture;
            Destroy(texture);
            return null;
        }

        private void RegisterOsmTexture(
            OsmTileKey key,
            Texture2D texture)
        {
            if (texture == null)
                return;
            texture.name = "Tile Editor OSM " + key;
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Trilinear;
            texture.anisoLevel = 8;
            texture.mipMapBias = -0.25f;
            texture.hideFlags = HideFlags.HideAndDontSave;
            if (_osmLineworkMode)
                PrepareOsmLineworkTexture(texture);
            var shader =
                Shader.Find("Sprites/Default")
                ?? Shader.Find("Unlit/Transparent")
                ?? Shader.Find(
                    "Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                Destroy(texture);
                throw new InvalidOperationException(
                    "No unlit overlay shader is available.");
            }
            var material = new Material(shader)
            {
                name = "Tile Editor OSM " + key,
                mainTexture = texture,
                hideFlags = HideFlags.HideAndDontSave,
                renderQueue = 3100,
            };
            ApplyOsmMaterialOpacity(material);
            if (material.HasProperty("_ZWrite"))
                material.SetInt("_ZWrite", 0);
            _osmTextures[key] = new OsmTextureRecord
            {
                Texture = texture,
                Material = material,
            };
        }

        private static void PrepareOsmLineworkTexture(
            Texture2D texture)
        {
            if (texture == null || !texture.isReadable)
                return;
            var pixels = texture.GetPixels32();
            for (var index = 0; index < pixels.Length; index++)
            {
                var pixel = pixels[index];
                var maximum = Mathf.Max(
                    pixel.r,
                    Mathf.Max(pixel.g, pixel.b));
                var minimum = Mathf.Min(
                    pixel.r,
                    Mathf.Min(pixel.g, pixel.b));
                var luminance =
                    pixel.r * 0.2126f
                    + pixel.g * 0.7152f
                    + pixel.b * 0.0722f;
                var darkness = Mathf.Clamp01(
                    (247f - luminance) / 150f);
                var chroma = Mathf.Clamp01(
                    (maximum - minimum) / 95f);
                var waterBlue = Mathf.Clamp01(
                    (pixel.b - pixel.r) / 90f);
                var ink = Mathf.Max(
                    darkness,
                    Mathf.Max(
                        chroma * 0.72f,
                        waterBlue * 0.55f));
                ink = ink * ink * (3f - 2f * ink);
                var alpha = Mathf.Lerp(0.025f, 1f, ink);

                // A small contrast lift keeps labels, railways, roads, and
                // building outlines readable after their pale background is
                // removed.
                pixel.r = ContrastOsmChannel(pixel.r);
                pixel.g = ContrastOsmChannel(pixel.g);
                pixel.b = ContrastOsmChannel(pixel.b);
                pixel.a = (byte)Mathf.RoundToInt(
                    alpha * byte.MaxValue);
                pixels[index] = pixel;
            }
            texture.SetPixels32(pixels);
            texture.Apply(true, false);
        }

        private static byte ContrastOsmChannel(byte value)
        {
            return (byte)Mathf.Clamp(
                Mathf.RoundToInt(
                    220f + (value - 220f) * 1.22f),
                0,
                byte.MaxValue);
        }

        private void ApplyOsmMaterialOpacity(Material material)
        {
            if (material == null)
                return;
            var tint = new Color(
                1f,
                1f,
                1f,
                _osmOpacity);
            material.color = tint;
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", tint);
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", tint);
            if (material.HasProperty("_Surface"))
                material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_SrcBlend"))
            {
                material.SetInt(
                    "_SrcBlend",
                    (int)BlendMode.SrcAlpha);
            }
            if (material.HasProperty("_DstBlend"))
            {
                material.SetInt(
                    "_DstBlend",
                    (int)BlendMode.OneMinusSrcAlpha);
            }
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = 3100;
        }

        private string OsmCachePath(OsmTileKey key)
        {
            return Path.Combine(
                _osmCacheDirectory,
                key.Zoom.ToString(
                    CultureInfo.InvariantCulture),
                key.X.ToString(
                    CultureInfo.InvariantCulture),
                key.Y.ToString(
                    CultureInfo.InvariantCulture)
                + ".png");
        }

        private void RefreshOsmDiskCacheStats()
        {
            _osmDiskCacheFiles = 0;
            _osmDiskCacheBytes = 0L;
            if (string.IsNullOrWhiteSpace(_osmCacheDirectory)
                || !Directory.Exists(_osmCacheDirectory))
            {
                return;
            }
            try
            {
                foreach (var path in Directory.EnumerateFiles(
                             _osmCacheDirectory,
                             "*.png",
                             SearchOption.AllDirectories))
                {
                    _osmDiskCacheFiles++;
                    try
                    {
                        _osmDiskCacheBytes +=
                            new FileInfo(path).Length;
                    }
                    catch
                    {
                        // A tile may disappear while statistics are read.
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.Warning(
                    "Could not inspect the OSM cache: "
                    + ex.Message);
            }
        }

        private void ClearOsmDiskCache()
        {
            _confirmClearOsmCache = false;
            var removedFiles = 0;
            var removedBytes = 0L;
            try
            {
                _osmCacheGeneration++;
                ClearQueuedOsmDownloads();
                _osmPending.Clear();
                _osmRetryAfter.Clear();
                ClearOsmOverlayContent();

                if (Directory.Exists(_osmCacheDirectory))
                {
                    foreach (var path in Directory.EnumerateFiles(
                                 _osmCacheDirectory,
                                 "*.png",
                                 SearchOption.AllDirectories)
                             .ToArray())
                    {
                        try
                        {
                            var length = new FileInfo(path).Length;
                            File.Delete(path);
                            removedFiles++;
                            removedBytes += length;
                        }
                        catch (Exception ex)
                        {
                            _logger?.Warning(
                                "Could not delete cached OSM tile "
                                + path + ": " + ex.Message);
                        }
                    }
                    foreach (var directory in Directory
                                 .EnumerateDirectories(
                                     _osmCacheDirectory,
                                     "*",
                                     SearchOption.AllDirectories)
                                 .OrderByDescending(path => path.Length)
                                 .ToArray())
                    {
                        try
                        {
                            if (!Directory.EnumerateFileSystemEntries(
                                    directory).Any())
                            {
                                Directory.Delete(directory);
                            }
                        }
                        catch
                        {
                            // Leaving an empty cache directory is harmless.
                        }
                    }
                }

                RefreshOsmDiskCacheStats();
                _osmStatus =
                    "Cleared "
                    + removedFiles.ToString(
                        "N0",
                        CultureInfo.InvariantCulture)
                    + " cached tiles ("
                    + FormatOsmCacheSize(removedBytes)
                    + ")";
                _nextOsmRefreshAt = 0f;
            }
            catch (Exception ex)
            {
                RefreshOsmDiskCacheStats();
                _osmStatus =
                    "Could not clear OSM cache: " + ex.Message;
                _logger?.Warning(_osmStatus);
            }
        }

        private static string FormatOsmCacheSize(long bytes)
        {
            if (bytes >= 1024L * 1024L * 1024L)
            {
                return (bytes / (1024.0 * 1024.0 * 1024.0))
                           .ToString("0.00", CultureInfo.InvariantCulture)
                       + " GB";
            }
            if (bytes >= 1024L * 1024L)
            {
                return (bytes / (1024.0 * 1024.0))
                           .ToString("0.0", CultureInfo.InvariantCulture)
                       + " MB";
            }
            if (bytes >= 1024L)
            {
                return (bytes / 1024.0)
                           .ToString("0.0", CultureInfo.InvariantCulture)
                       + " KB";
            }
            return bytes.ToString(
                       CultureInfo.InvariantCulture)
                   + " B";
        }

        private void ReconfigureOsmOverlay(bool force)
        {
            var manager = MapManager.Instance;
            if (manager == null)
                return;
            var identity =
                manager.directoryName + "|"
                + (_mapEditor?.GraphPath ?? string.Empty);
            if (!force
                && string.Equals(
                    identity,
                    _osmMapIdentity,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var source = FindOsmMapJson(
                manager.directoryName,
                _mapEditor?.GraphPath);
            var latitude = 35.382614;
            var longitude = -83.49541;
            var dimension =
                manager.tileDimension > 0
                    ? manager.tileDimension
                    : 500.0;
            var eastBias = 8.0;
            var northBias = -8.0;
            if (!string.IsNullOrWhiteSpace(source))
            {
                try
                {
                    var document = JObject.Parse(
                        File.ReadAllText(source));
                    var origin = document["origin"] as JObject;
                    latitude =
                        origin?["latitude"]?.Value<double>()
                        ?? latitude;
                    longitude =
                        origin?["longitude"]?.Value<double>()
                        ?? longitude;
                    dimension =
                        document["tileDimension"]?.Value<double>()
                        ?? dimension;
                    eastBias =
                        origin?["eastBiasMeters"]?.Value<double>()
                        ?? eastBias;
                    northBias =
                        origin?["northBiasMeters"]?.Value<double>()
                        ?? northBias;
                }
                catch (Exception ex)
                {
                    _logger?.Warning(
                        "Could not read OSM map calibration from "
                        + source + ": " + ex.Message);
                }
            }
            _osmOriginLatitude = latitude;
            _osmOriginLongitude = longitude;
            _osmTileDimension = dimension;
            _osmEastBias = eastBias;
            _osmNorthBias = northBias;
            _osmMapSource = string.IsNullOrWhiteSpace(source)
                ? "Railroader map defaults"
                : source;
            _osmMapIdentity = identity;
            ClearOsmOverlayContent();
            _osmStatus =
                "Aligned to "
                + Path.GetFileName(_osmMapSource);
        }

        private static string FindOsmMapJson(
            string mapDirectory,
            string graphPath)
        {
            var candidates = new List<string>();
            if (!string.IsNullOrWhiteSpace(graphPath))
            {
                var directory =
                    Path.GetDirectoryName(graphPath);
                for (var depth = 0;
                     depth < 5
                     && !string.IsNullOrWhiteSpace(directory);
                     depth++)
                {
                    candidates.Add(
                        Path.Combine(directory, "Map.json"));
                    candidates.Add(
                        Path.Combine(directory, "map.json"));
                    if (!string.IsNullOrWhiteSpace(mapDirectory))
                    {
                        candidates.Add(
                            Path.Combine(
                                directory,
                                "Maps",
                                mapDirectory,
                                "Map.json"));
                    }
                    directory =
                        Directory.GetParent(directory)?.FullName;
                }
            }
            if (!string.IsNullOrWhiteSpace(mapDirectory))
            {
                candidates.Add(
                    Path.Combine(
                        Application.streamingAssetsPath,
                        "Maps",
                        mapDirectory,
                        "Map.json"));
            }
            return candidates.FirstOrDefault(File.Exists)
                   ?? string.Empty;
        }

        private void EnsureOsmOverlayRoot()
        {
            if (_osmOverlayRoot != null)
                return;
            _osmOverlayRoot =
                new GameObject("TileEditorLocalOsmOverlay");
            _osmOverlayRoot.hideFlags =
                HideFlags.HideAndDontSave;
            _osmOverlayRoot.transform.SetParent(
                transform,
                false);
            _osmOverlayRoot.transform.position =
                WorldTransformer.GameToWorld(Vector3.zero);
        }

        private void TrimOsmTextureMemory()
        {
            var used = new HashSet<OsmTileKey>(
                _osmGameTiles.Values.SelectMany(
                    overlay => overlay.Chunks.Keys));
            used.UnionWith(_osmDesiredTextures);
            foreach (var key in _osmTextures.Keys
                         .Where(key => !used.Contains(key))
                         .ToArray())
            {
                DestroyOsmTexture(key);
            }
        }

        private void DestroyOsmGameTile(Vector2Int key)
        {
            if (!_osmGameTiles.TryGetValue(
                    key,
                    out var overlay))
            {
                return;
            }
            foreach (var chunk in overlay.Chunks.Values)
                DestroyOsmChunk(chunk);
            overlay.Chunks.Clear();
            if (overlay.Root != null)
                Destroy(overlay.Root);
            _osmGameTiles.Remove(key);
        }

        private void DestroyOsmChunk(OsmOverlayChunk chunk)
        {
            if (chunk == null)
                return;
            if (chunk.Mesh != null)
                Destroy(chunk.Mesh);
            if (chunk.Object != null)
                Destroy(chunk.Object);
        }

        private void DestroyOsmTexture(OsmTileKey key)
        {
            if (!_osmTextures.TryGetValue(
                    key,
                    out var record))
            {
                return;
            }
            if (record.Material != null)
                Destroy(record.Material);
            if (record.Texture != null)
                Destroy(record.Texture);
            _osmTextures.Remove(key);
        }

        private void ClearOsmGameTileMeshes()
        {
            foreach (var key in _osmGameTiles.Keys.ToArray())
                DestroyOsmGameTile(key);
            TrimOsmTextureMemory();
        }

        private void ClearQueuedOsmDownloads()
        {
            while (_osmDownloadQueue.Count > 0)
            {
                var key = _osmDownloadQueue.Dequeue();
                _osmPending.Remove(key);
            }
        }

        private void ClearOsmOverlayContent()
        {
            ClearOsmGameTileMeshes();
            _osmDesiredTextures.Clear();
            foreach (var key in _osmTextures.Keys.ToArray())
                DestroyOsmTexture(key);
            _osmLastCenter =
                new Vector2Int(int.MinValue, int.MinValue);
        }

        private void DisposeOsmOverlay()
        {
            ClearOsmOverlayContent();
            _osmPending.Clear();
            _osmDownloadQueue.Clear();
            _osmRetryAfter.Clear();
            if (_osmOverlayRoot != null)
                Destroy(_osmOverlayRoot);
            _osmOverlayRoot = null;
        }

        private void GameToLatitudeLongitude(
            double gameX,
            double gameZ,
            out double latitude,
            out double longitude)
        {
            latitude =
                _osmOriginLatitude
                + (gameZ + _osmNorthBias)
                / MetersPerDegree;
            var longitudeScale =
                MetersPerDegree
                * Math.Cos(
                    _osmOriginLatitude
                    * Math.PI / 180.0);
            longitude =
                _osmOriginLongitude
                + (gameX + _osmEastBias)
                / longitudeScale;
        }

        private void LatitudeLongitudeToGame(
            double latitude,
            double longitude,
            out double gameX,
            out double gameZ)
        {
            var longitudeScale =
                MetersPerDegree
                * Math.Cos(
                    _osmOriginLatitude
                    * Math.PI / 180.0);
            gameX =
                (longitude - _osmOriginLongitude)
                * longitudeScale
                - _osmEastBias;
            gameZ =
                (latitude - _osmOriginLatitude)
                * MetersPerDegree
                - _osmNorthBias;
        }

        private static void LatitudeLongitudeToOsmTile(
            double latitude,
            double longitude,
            int zoom,
            out double x,
            out double y)
        {
            latitude = Math.Max(
                -85.05112878,
                Math.Min(85.05112878, latitude));
            var count = Math.Pow(2.0, zoom);
            x = (longitude + 180.0) / 360.0 * count;
            var radians = latitude * Math.PI / 180.0;
            y = (1.0
                 - Math.Log(
                     Math.Tan(radians)
                     + 1.0 / Math.Cos(radians))
                 / Math.PI)
                / 2.0 * count;
        }

        private static void OsmTileToLatitudeLongitude(
            double x,
            double y,
            int zoom,
            out double latitude,
            out double longitude)
        {
            var count = Math.Pow(2.0, zoom);
            longitude = x / count * 360.0 - 180.0;
            var radians = Math.Atan(
                Math.Sinh(
                    Math.PI * (1.0 - 2.0 * y / count)));
            latitude = radians * 180.0 / Math.PI;
        }

        private static double Lerp(
            double a,
            double b,
            float t)
        {
            return a + (b - a) * t;
        }
    }
}
