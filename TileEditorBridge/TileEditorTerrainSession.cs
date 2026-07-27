using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Helpers;
using Map.Runtime;
using UnityEngine;

namespace Hrogers.TileEditorBridge
{
    internal sealed partial class TileEditorGraphSession
    {
        internal enum TerrainBrushMode
        {
            Raise,
            Lower,
            Flatten,
            LevelPath,
            GradePlane,
            Ditch,
            Berm,
            Smooth,
            SetHeight,
            Noise,
            Vegetation,
            Water,
        }

        internal enum TerrainBrushFalloff
        {
            Hard,
            Linear,
            Smooth,
            Gaussian,
        }

        internal enum TerrainBrushShape
        {
            Circle,
            Square,
        }

        internal sealed class TerrainBrushParameters
        {
            internal TerrainBrushMode Mode;
            internal TerrainBrushFalloff Falloff;
            internal TerrainBrushShape Shape;
            internal float Radius;
            internal float Strength;
            internal float HeightRate;
            internal float TargetHeight;
            internal float NoiseScale;
            internal float NoiseAmplitude;
            internal float MaximumCutFill;
            internal float FeatureDepth;
            internal float GradePercent;
            internal float GradeHeading;
            internal Vector3 ReferencePosition;
            internal int VegetationId;
            internal bool WaterEnabled;
        }

        internal sealed class TerrainPointerInfo
        {
            internal bool Available;
            internal string MapName = string.Empty;
            internal Vector2Int Tile;
            internal Vector3 GamePosition;
            internal float Height;
            internal int VegetationId;
            internal bool Water;
            internal string SourceFile = string.Empty;
        }

        private sealed class TerrainTileSnapshot
        {
            internal Vector2Int Key;
            internal readonly Dictionary<int, float> Heights =
                new Dictionary<int, float>();
            internal readonly Dictionary<int, byte> Vegetation =
                new Dictionary<int, byte>();
            internal readonly Dictionary<int, byte> Water =
                new Dictionary<int, byte>();

            internal bool HasChanges =>
                Heights.Count > 0
                || Vegetation.Count > 0
                || Water.Count > 0;
        }

        private sealed class TerrainEditRecord
        {
            internal readonly Dictionary<Vector2Int, TerrainTileSnapshot>
                Before =
                    new Dictionary<Vector2Int, TerrainTileSnapshot>();
            internal readonly Dictionary<Vector2Int, TerrainTileSnapshot>
                After =
                    new Dictionary<Vector2Int, TerrainTileSnapshot>();
        }

        private const int MaximumTerrainUndoRecords = 12;
        private static readonly FieldInfo TileDataPathField =
            typeof(TileData).GetField(
                "_dataPath",
                BindingFlags.Instance | BindingFlags.NonPublic);
        private readonly Stack<TerrainEditRecord> _terrainUndo =
            new Stack<TerrainEditRecord>();
        private readonly Stack<TerrainEditRecord> _terrainRedo =
            new Stack<TerrainEditRecord>();
        private readonly HashSet<Vector2Int> _dirtyTerrainTiles =
            new HashSet<Vector2Int>();
        private readonly Dictionary<string, string> _terrainBackups =
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
        private TerrainEditRecord _currentTerrainEdit;
        private readonly List<string> _lastSavedTerrainPaths =
            new List<string>();
        private bool _terrainRebuildPending;
        private Vector2Int _terrainRebuildWatchTile;
        private float _terrainRebuildStartedAt;

        internal bool TerrainDirty => _dirtyTerrainTiles.Count > 0;
        internal bool CanUndoTerrain => _terrainUndo.Count > 0;
        internal bool CanRedoTerrain => _terrainRedo.Count > 0;
        internal int DirtyTerrainTileCount => _dirtyTerrainTiles.Count;
        internal bool TerrainRebuildPending => _terrainRebuildPending;
        internal IReadOnlyList<string> LastSavedTerrainPaths =>
            _lastSavedTerrainPaths;

        internal string RebuildTerrain()
        {
            EndTerrainEdit();
            if (_externalTerrainEditLock)
            {
                throw new InvalidOperationException(
                    "The desktop editor has unsaved terrain changes. "
                    + "Save or undo them before rebuilding terrain.");
            }
            if (TerrainDirty)
            {
                throw new InvalidOperationException(
                    "Save or undo the in-game terrain changes before "
                    + "rebuilding terrain.");
            }
            if (_terrainRebuildPending)
                return "Terrain rebuild is already in progress";

            var manager = MapManager.Instance;
            if (manager == null || !manager.isActiveAndEnabled)
            {
                throw new InvalidOperationException(
                    "Railroader's terrain manager is not ready.");
            }
            if (CameraSelector.shared == null)
            {
                throw new InvalidOperationException(
                    "Railroader's camera is not ready.");
            }

            var focusWorld =
                CameraSelector.shared.CurrentCameraGroundPosition;
            var focusGame = WorldTransformer.WorldToGame(focusWorld);
            var focusTile = manager.TilePositionFromPoint(focusGame);
            if (!manager.HasTileData(focusTile))
            {
                throw new InvalidOperationException(
                    "The camera is not over a terrain tile that can be "
                    + "rebuilt.");
            }

            manager.RebuildAll();

            // RebuildAll clears every live terrain and reloads MapStore, but
            // it does not guarantee that the camera area is queued again in
            // the same frame. Explicitly refresh the visible set so a rebuild
            // cannot leave the world empty until the camera moves.
            manager.UpdateVisibleTilesForPosition(focusGame);

            _terrainRebuildWatchTile = focusTile;
            _terrainRebuildStartedAt = Time.unscaledTime;
            _terrainRebuildPending = true;
            _logger?.Log(
                "Terrain rebuild queued around tile "
                + focusTile.x.ToString(CultureInfo.InvariantCulture)
                + ","
                + focusTile.y.ToString(CultureInfo.InvariantCulture)
                + ".");
            return "Rebuilding terrain around tile "
                   + focusTile.x.ToString(CultureInfo.InvariantCulture)
                   + ", "
                   + focusTile.y.ToString(CultureInfo.InvariantCulture)
                   + "...";
        }

        internal string PollTerrainRebuildStatus()
        {
            if (!_terrainRebuildPending)
                return string.Empty;

            var manager = MapManager.Instance;
            if (manager == null || !manager.isActiveAndEnabled)
            {
                _terrainRebuildPending = false;
                return "Terrain rebuild stopped: terrain manager unavailable";
            }
            if (manager.TryGetTerrain(
                    _terrainRebuildWatchTile,
                    out var mapTerrain)
                && mapTerrain != null
                && mapTerrain.tileData != null
                && mapTerrain.buildStatus == MapTerrain.BuildStatus.Ready)
            {
                _terrainRebuildPending = false;
                var message = "Terrain rebuilt around tile "
                              + _terrainRebuildWatchTile.x.ToString(
                                  CultureInfo.InvariantCulture)
                              + ", "
                              + _terrainRebuildWatchTile.y.ToString(
                                  CultureInfo.InvariantCulture);
                _logger?.Log(message + ".");
                return message;
            }
            if (Time.unscaledTime - _terrainRebuildStartedAt < 45f)
                return string.Empty;

            _terrainRebuildPending = false;
            var timeout = "Terrain rebuild timed out at tile "
                          + _terrainRebuildWatchTile.x.ToString(
                              CultureInfo.InvariantCulture)
                          + ", "
                          + _terrainRebuildWatchTile.y.ToString(
                              CultureInfo.InvariantCulture)
                          + "; check the Railroader log for a tile load error";
            _logger?.Warning(timeout + ".");
            return timeout;
        }

        internal TerrainPointerInfo InspectTerrainAt(
            Vector3 worldPosition)
        {
            var result = new TerrainPointerInfo();
            var manager = MapManager.Instance;
            if (manager == null)
                return result;
            var game = WorldTransformer.WorldToGame(worldPosition);
            var key = manager.TilePositionFromPoint(game);
            if (!manager.TryGetTerrain(key, out var mapTerrain)
                || mapTerrain == null
                || mapTerrain.tileData == null)
            {
                return result;
            }
            var tile = mapTerrain.tileData;
            var bounds = tile.Bounds;
            var normalizedX = Mathf.InverseLerp(
                bounds.min.x,
                bounds.max.x,
                game.x);
            var normalizedZ = Mathf.InverseLerp(
                bounds.min.z,
                bounds.max.z,
                game.z);
            var heightX = Mathf.Clamp(
                Mathf.RoundToInt(
                    normalizedX * (tile.Resolution - 1)),
                0,
                tile.Resolution - 1);
            var heightZ = Mathf.Clamp(
                Mathf.RoundToInt(
                    normalizedZ * (tile.Resolution - 1)),
                0,
                tile.Resolution - 1);
            var vegetation = tile.GetMask(TileMaskName.Vegetation);
            var water = tile.GetMask(TileMaskName.Water);
            var maskX = Mathf.Clamp(
                Mathf.FloorToInt(normalizedX * vegetation.width),
                0,
                vegetation.width - 1);
            var maskZ = Mathf.Clamp(
                Mathf.FloorToInt(normalizedZ * vegetation.height),
                0,
                vegetation.height - 1);
            var vegetationPixel =
                vegetation.GetPixel(maskX, maskZ);
            var waterPixel = water.GetPixel(maskX, maskZ);
            result.Available = true;
            result.MapName = manager.directoryName ?? string.Empty;
            result.Tile = key;
            result.GamePosition = game;
            result.Height = tile.GetHeightYX(heightZ, heightX);
            result.VegetationId = Mathf.Clamp(
                Mathf.RoundToInt((1f - vegetationPixel.r) * 7f),
                0,
                7);
            result.Water = waterPixel.r >= 0.5f;
            result.SourceFile = Path.GetFileName(
                GetTileDataPath(tile));
            return result;
        }

        internal void BeginTerrainEdit()
        {
            if (_currentTerrainEdit == null)
                _currentTerrainEdit = new TerrainEditRecord();
        }

        internal int ApplyTerrainBrush(
            Vector3 worldPosition,
            TerrainBrushParameters brush,
            float deltaTime)
        {
            if (brush == null)
                throw new ArgumentNullException(nameof(brush));
            if (_externalTerrainEditLock)
            {
                throw new InvalidOperationException(
                    "The desktop editor has unsaved terrain changes. "
                    + "Save or undo them before painting in-game.");
            }
            var manager = MapManager.Instance;
            if (manager == null)
            {
                throw new InvalidOperationException(
                    "Railroader's terrain manager is not ready.");
            }
            BeginTerrainEdit();
            var center = WorldTransformer.WorldToGame(worldPosition);
            var radius = Mathf.Clamp(brush.Radius, 1f, 250f);
            var centerKey = manager.TilePositionFromPoint(center);
            if (!manager.TryGetTerrain(
                    centerKey,
                    out var centerTerrain)
                || centerTerrain?.tileData == null)
            {
                return 0;
            }
            var dimension = Mathf.Max(
                1f,
                centerTerrain.tileData.Bounds.size.x);
            var minimumX = Mathf.FloorToInt(
                (center.x - radius) / dimension);
            var maximumX = Mathf.FloorToInt(
                (center.x + radius) / dimension);
            var minimumZ = Mathf.FloorToInt(
                (center.z - radius) / dimension);
            var maximumZ = Mathf.FloorToInt(
                (center.z + radius) / dimension);
            var modified = 0;
            for (var tileZ = minimumZ; tileZ <= maximumZ; tileZ++)
            {
                for (var tileX = minimumX; tileX <= maximumX; tileX++)
                {
                    var key = new Vector2Int(tileX, tileZ);
                    if (!manager.TryGetTerrain(key, out var mapTerrain)
                        || mapTerrain?.tileData == null)
                    {
                        continue;
                    }
                    var before = GetOrCreateTerrainSnapshot(
                        _currentTerrainEdit.Before,
                        key);
                    var count = brush.Mode == TerrainBrushMode.Vegetation
                                || brush.Mode == TerrainBrushMode.Water
                        ? ApplyTerrainMaskBrush(
                            mapTerrain,
                            center,
                            brush,
                            radius,
                            before)
                        : ApplyTerrainHeightBrush(
                            mapTerrain,
                            center,
                            brush,
                            radius,
                            Mathf.Clamp(deltaTime, 0.001f, 0.1f),
                            before);
                    if (count <= 0)
                        continue;
                    modified += count;
                    _dirtyTerrainTiles.Add(key);
                    mapTerrain.tileData.Dirty = true;
                }
            }
            return modified;
        }

        internal void EndTerrainEdit()
        {
            if (_currentTerrainEdit == null)
                return;
            foreach (var pair in _currentTerrainEdit.Before)
            {
                if (!pair.Value.HasChanges)
                    continue;
                var manager = MapManager.Instance;
                if (manager != null
                    && manager.TryGetTerrain(
                        pair.Key,
                        out var terrain)
                    && terrain?.tileData != null)
                {
                    _currentTerrainEdit.After[pair.Key] =
                        CaptureTerrainSnapshot(
                            terrain,
                            pair.Value);
                    terrain.terrain?.terrainData?.SyncHeightmap();
                }
            }
            if (_currentTerrainEdit.After.Count > 0)
            {
                _terrainUndo.Push(_currentTerrainEdit);
                TrimTerrainUndo();
                _terrainRedo.Clear();
            }
            _currentTerrainEdit = null;
        }

        internal void UndoTerrain()
        {
            EndTerrainEdit();
            if (_terrainUndo.Count == 0)
                return;
            var edit = _terrainUndo.Pop();
            RestoreTerrainSnapshots(edit.Before);
            _terrainRedo.Push(edit);
        }

        internal void RedoTerrain()
        {
            EndTerrainEdit();
            if (_terrainRedo.Count == 0)
                return;
            var edit = _terrainRedo.Pop();
            RestoreTerrainSnapshots(edit.After);
            _terrainUndo.Push(edit);
        }

        internal string SaveTerrainTiles()
        {
            EndTerrainEdit();
            _lastSavedTerrainPaths.Clear();
            var manager = MapManager.Instance;
            if (manager == null)
            {
                throw new InvalidOperationException(
                    "Railroader's terrain manager is not ready.");
            }
            if (_dirtyTerrainTiles.Count == 0)
                return "No terrain changes to save";
            var saved = 0;
            var requiresFullRebuild = false;
            var savedKeys = new List<Vector2Int>();
            foreach (var key in _dirtyTerrainTiles.ToArray())
            {
                if (!manager.TryGetTerrain(key, out var mapTerrain)
                    || mapTerrain?.tileData == null)
                {
                    continue;
                }
                var sourcePath = GetTileDataPath(
                    mapTerrain.tileData);
                var outputPath = ResolveTerrainOutputPath(
                    manager,
                    key,
                    sourcePath,
                    out var createdOverride);
                BackupTerrainTile(outputPath, sourcePath);
                WriteTerrainTile(
                    mapTerrain.tileData,
                    outputPath);
                mapTerrain.tileData.Dirty = false;
                requiresFullRebuild |= createdOverride;
                savedKeys.Add(key);
                _lastSavedTerrainPaths.Add(outputPath);
                saved++;
            }
            foreach (var key in savedKeys)
                _dirtyTerrainTiles.Remove(key);
            if (saved == 0)
            {
                throw new InvalidOperationException(
                    "No edited terrain tiles are currently loaded.");
            }
            if (requiresFullRebuild)
            {
                manager.RebuildAll();
            }
            else
            {
                foreach (var key in savedKeys)
                    manager.Invalidate(key);
            }
            return "Saved and rebuilt "
                   + saved.ToString(
                       CultureInfo.InvariantCulture)
                   + " terrain tile"
                   + (saved == 1 ? string.Empty : "s");
        }

        internal string ReloadTerrainTilesFromDesktop(
            IEnumerable<string> paths)
        {
            EndTerrainEdit();
            var manager = MapManager.Instance;
            if (manager == null)
            {
                throw new InvalidOperationException(
                    "Railroader's terrain manager is not ready.");
            }
            var incoming = (paths ?? Array.Empty<string>())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(Path.GetFullPath)
                .Where(File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (incoming.Length == 0)
            {
                throw new InvalidOperationException(
                    "No desktop terrain tile files were found.");
            }

            var conflicts = 0;
            foreach (var key in _dirtyTerrainTiles.ToArray())
            {
                if (!manager.TryGetTerrain(key, out var mapTerrain)
                    || mapTerrain?.tileData == null)
                {
                    continue;
                }
                var sourcePath = GetTileDataPath(
                    mapTerrain.tileData);
                if (string.IsNullOrWhiteSpace(sourcePath))
                    continue;
                var conflict = sourcePath
                               + ".game-conflict-"
                               + DateTime.Now.ToString(
                                   "yyyyMMdd-HHmmss",
                                   CultureInfo.InvariantCulture);
                WriteTerrainTile(
                    mapTerrain.tileData,
                    conflict);
                conflicts++;
            }

            _currentTerrainEdit = null;
            _terrainUndo.Clear();
            _terrainRedo.Clear();
            _dirtyTerrainTiles.Clear();
            _lastSavedTerrainPaths.Clear();
            manager.RebuildAll();
            return "Reloaded "
                   + incoming.Length.ToString(
                       CultureInfo.InvariantCulture)
                   + " desktop terrain tile"
                   + (incoming.Length == 1 ? string.Empty : "s")
                   + (conflicts == 0
                       ? string.Empty
                       : "; preserved "
                         + conflicts.ToString(
                             CultureInfo.InvariantCulture)
                         + " in-game conflict cop"
                         + (conflicts == 1 ? "y" : "ies"));
        }

        private int ApplyTerrainHeightBrush(
            MapTerrain mapTerrain,
            Vector3 center,
            TerrainBrushParameters brush,
            float radius,
            float deltaTime,
            TerrainTileSnapshot before)
        {
            var tile = mapTerrain.tileData;
            var resolution = tile.Resolution;
            var bounds = tile.Bounds;
            var spacingX = bounds.size.x / (resolution - 1);
            var spacingZ = bounds.size.z / (resolution - 1);
            var minimumX = Mathf.Clamp(
                Mathf.FloorToInt(
                    (center.x - radius - bounds.min.x) / spacingX),
                0,
                resolution - 1);
            var maximumX = Mathf.Clamp(
                Mathf.CeilToInt(
                    (center.x + radius - bounds.min.x) / spacingX),
                0,
                resolution - 1);
            var minimumZ = Mathf.Clamp(
                Mathf.FloorToInt(
                    (center.z - radius - bounds.min.z) / spacingZ),
                0,
                resolution - 1);
            var maximumZ = Mathf.Clamp(
                Mathf.CeilToInt(
                    (center.z + radius - bounds.min.z) / spacingZ),
                0,
                resolution - 1);
            var width = maximumX - minimumX + 1;
            var height = maximumZ - minimumZ + 1;
            if (width <= 0 || height <= 0)
                return 0;
            var smoothMinimumX = Mathf.Max(0, minimumX - 1);
            var smoothMinimumZ = Mathf.Max(0, minimumZ - 1);
            var smoothMaximumX = Mathf.Min(
                resolution - 1,
                maximumX + 1);
            var smoothMaximumZ = Mathf.Min(
                resolution - 1,
                maximumZ + 1);
            float[,] smoothSource = null;
            if (brush.Mode == TerrainBrushMode.Smooth)
            {
                smoothSource = new float[
                    smoothMaximumZ - smoothMinimumZ + 1,
                    smoothMaximumX - smoothMinimumX + 1];
                for (var sourceZ = smoothMinimumZ;
                     sourceZ <= smoothMaximumZ;
                     sourceZ++)
                {
                    for (var sourceX = smoothMinimumX;
                         sourceX <= smoothMaximumX;
                         sourceX++)
                    {
                        smoothSource[
                            sourceZ - smoothMinimumZ,
                            sourceX - smoothMinimumX] =
                            tile.GetHeightYX(sourceZ, sourceX);
                    }
                }
            }
            var changed = 0;
            for (var z = minimumZ; z <= maximumZ; z++)
            {
                var gameZ = bounds.min.z + z * spacingZ;
                for (var x = minimumX; x <= maximumX; x++)
                {
                    var gameX = bounds.min.x + x * spacingX;
                    var falloff = TerrainBrushWeight(
                        gameX - center.x,
                        gameZ - center.z,
                        radius,
                        brush.Shape,
                        brush.Falloff);
                    if (falloff <= 0f)
                        continue;
                    var current = tile.GetHeightYX(z, x);
                    var index = z * resolution + x;
                    var original = before.Heights.TryGetValue(
                        index,
                        out var originalHeight)
                        ? originalHeight
                        : current;
                    var rate = Mathf.Max(0.01f, brush.HeightRate)
                               * Mathf.Clamp01(brush.Strength)
                               * deltaTime;
                    float target;
                    switch (brush.Mode)
                    {
                        case TerrainBrushMode.Raise:
                            target = current
                                     + brush.HeightRate
                                     * brush.Strength
                                     * falloff
                                     * deltaTime;
                            break;
                        case TerrainBrushMode.Lower:
                            target = current
                                     - brush.HeightRate
                                     * brush.Strength
                                     * falloff
                                     * deltaTime;
                            break;
                        case TerrainBrushMode.Flatten:
                        case TerrainBrushMode.LevelPath:
                        case TerrainBrushMode.SetHeight:
                            target = Mathf.MoveTowards(
                                current,
                                brush.TargetHeight,
                                rate
                                * falloff
                                * (brush.Mode
                                   == TerrainBrushMode.SetHeight
                                    ? 4f
                                    : 1f));
                            break;
                        case TerrainBrushMode.GradePlane:
                            var gradeForward = HorizontalForward(
                                brush.GradeHeading);
                            var gradeRun = Vector3.Dot(
                                new Vector3(
                                    gameX
                                    - brush.ReferencePosition.x,
                                    0f,
                                    gameZ
                                    - brush.ReferencePosition.z),
                                gradeForward);
                            var gradeTarget =
                                brush.ReferencePosition.y
                                + gradeRun
                                * brush.GradePercent
                                / 100f;
                            target = Mathf.MoveTowards(
                                current,
                                gradeTarget,
                                rate * falloff);
                            break;
                        case TerrainBrushMode.Ditch:
                        case TerrainBrushMode.Berm:
                            var signedDepth =
                                brush.Mode == TerrainBrushMode.Ditch
                                    ? -Mathf.Abs(brush.FeatureDepth)
                                    : Mathf.Abs(brush.FeatureDepth);
                            var featureTarget =
                                original + signedDepth * falloff;
                            target = Mathf.MoveTowards(
                                current,
                                featureTarget,
                                rate);
                            break;
                        case TerrainBrushMode.Smooth:
                            target = Mathf.MoveTowards(
                                current,
                                AverageTerrainHeight(
                                    smoothSource,
                                    x - smoothMinimumX,
                                    z - smoothMinimumZ),
                                rate * falloff);
                            break;
                        case TerrainBrushMode.Noise:
                            var scale = Mathf.Max(
                                1f,
                                brush.NoiseScale);
                            var noise =
                                Mathf.PerlinNoise(
                                    gameX / scale,
                                    gameZ / scale) * 2f - 1f;
                            var noiseTarget =
                                original
                                + noise
                                * brush.NoiseAmplitude
                                * falloff;
                            target = Mathf.MoveTowards(
                                current,
                                noiseTarget,
                                rate);
                            break;
                        default:
                            continue;
                    }
                    var maximumCutFill = Mathf.Clamp(
                        brush.MaximumCutFill,
                        0.05f,
                        1000f);
                    target = Mathf.Clamp(
                        target,
                        original - maximumCutFill,
                        original + maximumCutFill);
                    target = Mathf.Clamp(target, 500f, 1500f);
                    if (Mathf.Abs(target - current) < 0.00001f)
                        continue;
                    if (!before.Heights.ContainsKey(index))
                        before.Heights[index] = current;
                    tile.SetHeightYX(z, x, target);
                    changed++;
                }
            }
            if (changed > 0
                && mapTerrain.terrain?.terrainData != null)
            {
                var patch = new float[height, width];
                for (var z = 0; z < height; z++)
                {
                    for (var x = 0; x < width; x++)
                    {
                        patch[z, x] = Mathf.InverseLerp(
                            500f,
                            1500f,
                            tile.GetHeightYX(
                                minimumZ + z,
                                minimumX + x));
                    }
                }
                mapTerrain.terrain.terrainData.SetHeightsDelayLOD(
                    minimumX,
                    minimumZ,
                    patch);
            }
            return changed;
        }

        private int ApplyTerrainMaskBrush(
            MapTerrain mapTerrain,
            Vector3 center,
            TerrainBrushParameters brush,
            float radius,
            TerrainTileSnapshot before)
        {
            var tile = mapTerrain.tileData;
            var maskName = brush.Mode == TerrainBrushMode.Water
                ? TileMaskName.Water
                : TileMaskName.Vegetation;
            var texture = tile.GetMask(maskName);
            var pixels = texture.GetRawTextureData<byte>();
            var bounds = tile.Bounds;
            var spacingX = bounds.size.x / texture.width;
            var spacingZ = bounds.size.z / texture.height;
            var minimumX = Mathf.Clamp(
                Mathf.FloorToInt(
                    (center.x - radius - bounds.min.x) / spacingX),
                0,
                texture.width - 1);
            var maximumX = Mathf.Clamp(
                Mathf.CeilToInt(
                    (center.x + radius - bounds.min.x) / spacingX),
                0,
                texture.width - 1);
            var minimumZ = Mathf.Clamp(
                Mathf.FloorToInt(
                    (center.z - radius - bounds.min.z) / spacingZ),
                0,
                texture.height - 1);
            var maximumZ = Mathf.Clamp(
                Mathf.CeilToInt(
                    (center.z + radius - bounds.min.z) / spacingZ),
                0,
                texture.height - 1);
            var vegetationTarget = (byte)Mathf.RoundToInt(
                (1f - Mathf.Clamp(brush.VegetationId, 0, 7) / 7f)
                * 255f);
            var waterTarget = brush.WaterEnabled
                ? byte.MaxValue
                : byte.MinValue;
            var changed = 0;
            for (var z = minimumZ; z <= maximumZ; z++)
            {
                var gameZ = bounds.min.z + (z + 0.5f) * spacingZ;
                for (var x = minimumX; x <= maximumX; x++)
                {
                    var gameX = bounds.min.x + (x + 0.5f) * spacingX;
                    var falloff = TerrainBrushWeight(
                        gameX - center.x,
                        gameZ - center.z,
                        radius,
                        brush.Shape,
                        brush.Falloff);
                    if (falloff <= 0f)
                        continue;
                    var index = z * texture.width + x;
                    var old = pixels[index];
                    var target = brush.Mode == TerrainBrushMode.Water
                        ? waterTarget
                        : vegetationTarget;
                    var value = (byte)Mathf.RoundToInt(
                        Mathf.Lerp(
                            old,
                            target,
                            Mathf.Clamp01(
                                brush.Strength * falloff)));
                    if (value == old)
                        continue;
                    var values = brush.Mode
                                 == TerrainBrushMode.Water
                        ? before.Water
                        : before.Vegetation;
                    if (!values.ContainsKey(index))
                        values[index] = old;
                    pixels[index] = value;
                    changed++;
                }
            }
            if (changed > 0)
            {
                texture.Apply(false, false);
                tile.SetMask(maskName, texture);
            }
            return changed;
        }

        private static float TerrainBrushWeight(
            float deltaX,
            float deltaZ,
            float radius,
            TerrainBrushShape shape,
            TerrainBrushFalloff falloff)
        {
            var distance = shape == TerrainBrushShape.Square
                ? Mathf.Max(Mathf.Abs(deltaX), Mathf.Abs(deltaZ))
                : Mathf.Sqrt(deltaX * deltaX + deltaZ * deltaZ);
            if (distance > radius)
                return 0f;
            var t = Mathf.Clamp01(distance / Mathf.Max(radius, 0.001f));
            switch (falloff)
            {
                case TerrainBrushFalloff.Hard:
                    return 1f;
                case TerrainBrushFalloff.Linear:
                    return 1f - t;
                case TerrainBrushFalloff.Gaussian:
                    return Mathf.Exp(-4f * t * t);
                default:
                    var inverse = 1f - t;
                    return inverse * inverse
                           * (3f - 2f * inverse);
            }
        }

        private static float AverageTerrainHeight(
            float[,] source,
            int centerX,
            int centerZ)
        {
            if (source == null)
                return 0f;
            var total = 0f;
            var count = 0;
            for (var z = Mathf.Max(0, centerZ - 1);
                 z <= Mathf.Min(
                     source.GetLength(0) - 1,
                     centerZ + 1);
                 z++)
            {
                for (var x = Mathf.Max(0, centerX - 1);
                     x <= Mathf.Min(
                         source.GetLength(1) - 1,
                         centerX + 1);
                     x++)
                {
                    total += source[z, x];
                    count++;
                }
            }
            return count == 0
                ? source[
                    Mathf.Clamp(
                        centerZ,
                        0,
                        source.GetLength(0) - 1),
                    Mathf.Clamp(
                        centerX,
                        0,
                        source.GetLength(1) - 1)]
                : total / count;
        }

        private static TerrainTileSnapshot GetOrCreateTerrainSnapshot(
            IDictionary<Vector2Int, TerrainTileSnapshot> snapshots,
            Vector2Int key)
        {
            if (snapshots.TryGetValue(key, out var snapshot))
                return snapshot;
            snapshot = new TerrainTileSnapshot { Key = key };
            snapshots[key] = snapshot;
            return snapshot;
        }

        private static TerrainTileSnapshot CaptureTerrainSnapshot(
            MapTerrain mapTerrain,
            TerrainTileSnapshot template)
        {
            var tile = mapTerrain.tileData;
            var snapshot = new TerrainTileSnapshot
            {
                Key = template.Key,
            };
            foreach (var index in template.Heights.Keys)
            {
                var z = index / tile.Resolution;
                var x = index % tile.Resolution;
                snapshot.Heights[index] =
                    tile.GetHeightYX(z, x);
            }
            if (template.Vegetation.Count > 0)
            {
                var pixels = tile
                    .GetMask(TileMaskName.Vegetation)
                    .GetRawTextureData<byte>();
                foreach (var index in template.Vegetation.Keys)
                    snapshot.Vegetation[index] = pixels[index];
            }
            if (template.Water.Count > 0)
            {
                var pixels = tile
                    .GetMask(TileMaskName.Water)
                    .GetRawTextureData<byte>();
                foreach (var index in template.Water.Keys)
                    snapshot.Water[index] = pixels[index];
            }
            return snapshot;
        }

        private void RestoreTerrainSnapshots(
            IDictionary<Vector2Int, TerrainTileSnapshot> snapshots)
        {
            var manager = MapManager.Instance;
            if (manager == null)
                return;
            foreach (var pair in snapshots)
            {
                if (!manager.TryGetTerrain(
                        pair.Key,
                        out var mapTerrain)
                    || mapTerrain?.tileData == null)
                {
                    continue;
                }
                var tile = mapTerrain.tileData;
                var snapshot = pair.Value;
                var minimumX = tile.Resolution;
                var minimumZ = tile.Resolution;
                var maximumX = -1;
                var maximumZ = -1;
                foreach (var height in snapshot.Heights)
                {
                    var z = height.Key / tile.Resolution;
                    var x = height.Key % tile.Resolution;
                    if (x < 0
                        || x >= tile.Resolution
                        || z < 0
                        || z >= tile.Resolution)
                    {
                        continue;
                    }
                    tile.SetHeightYX(z, x, height.Value);
                    minimumX = Mathf.Min(minimumX, x);
                    maximumX = Mathf.Max(maximumX, x);
                    minimumZ = Mathf.Min(minimumZ, z);
                    maximumZ = Mathf.Max(maximumZ, z);
                }
                if (maximumX >= minimumX
                    && maximumZ >= minimumZ
                    && mapTerrain.terrain?.terrainData != null)
                {
                    var width = maximumX - minimumX + 1;
                    var height = maximumZ - minimumZ + 1;
                    var normalized = new float[height, width];
                    for (var z = 0; z < height; z++)
                    {
                        for (var x = 0; x < width; x++)
                        {
                            normalized[z, x] = Mathf.InverseLerp(
                                500f,
                                1500f,
                                tile.GetHeightYX(
                                    minimumZ + z,
                                    minimumX + x));
                        }
                    }
                    mapTerrain.terrain.terrainData.SetHeightsDelayLOD(
                        minimumX,
                        minimumZ,
                        normalized);
                    mapTerrain.terrain.terrainData.SyncHeightmap();
                }
                RestoreTerrainMask(
                    tile,
                    TileMaskName.Vegetation,
                    snapshot.Vegetation);
                RestoreTerrainMask(
                    tile,
                    TileMaskName.Water,
                    snapshot.Water);
                tile.Dirty = true;
                _dirtyTerrainTiles.Add(pair.Key);
            }
        }

        private static void RestoreTerrainMask(
            TileData tile,
            TileMaskName name,
            IDictionary<int, byte> values)
        {
            if (values.Count == 0)
                return;
            var texture = tile.GetMask(name);
            var pixels = texture.GetRawTextureData<byte>();
            foreach (var pair in values)
            {
                if (pair.Key < 0 || pair.Key >= pixels.Length)
                    continue;
                pixels[pair.Key] = pair.Value;
            }
            texture.Apply(false, false);
            tile.SetMask(name, texture);
        }

        private string ResolveTerrainOutputPath(
            MapManager manager,
            Vector2Int key,
            string sourcePath,
            out bool createdOverride)
        {
            createdOverride = false;
            var modsRoot = Path.GetFullPath(
                Path.Combine(_gameRoot, "Mods"))
                .TrimEnd(Path.DirectorySeparatorChar);
            if (!string.IsNullOrWhiteSpace(sourcePath))
            {
                var fullSource = Path.GetFullPath(sourcePath);
                if (fullSource.StartsWith(
                        modsRoot + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return fullSource;
                }
            }
            var modDirectory = FindOwningModDirectory();
            if (string.IsNullOrWhiteSpace(modDirectory))
            {
                throw new InvalidOperationException(
                    "This is a base-game terrain tile. Open a graph layer "
                    + "from the mod that should own the terrain override.");
            }
            var mapDirectory = Path.Combine(
                modDirectory,
                "Maps",
                manager.directoryName);
            Directory.CreateDirectory(mapDirectory);
            var path = Path.Combine(
                mapDirectory,
                FormatTerrainTileName(key));
            createdOverride = !File.Exists(path);
            return path;
        }

        private void BackupTerrainTile(
            string outputPath,
            string sourcePath)
        {
            if (_terrainBackups.ContainsKey(outputPath))
                return;
            var backupSource = File.Exists(outputPath)
                ? outputPath
                : sourcePath;
            if (string.IsNullOrWhiteSpace(backupSource)
                || !File.Exists(backupSource))
            {
                return;
            }
            var backup = outputPath
                         + ".tile-editor-backup-"
                         + DateTime.Now.ToString(
                             "yyyyMMdd-HHmmss",
                             CultureInfo.InvariantCulture);
            Directory.CreateDirectory(
                Path.GetDirectoryName(outputPath)
                ?? string.Empty);
            File.Copy(backupSource, backup, false);
            _terrainBackups[outputPath] = backup;
        }

        private static void WriteTerrainTile(
            TileData tile,
            string outputPath)
        {
            var resolution = tile.Resolution;
            var vegetation = tile
                .GetMask(TileMaskName.Vegetation)
                .GetRawTextureData<byte>();
            var water = tile
                .GetMask(TileMaskName.Water)
                .GetRawTextureData<byte>();
            var maskResolution = resolution - 1;
            var pixels = new Color32[resolution * resolution];
            for (var z = 0; z < resolution; z++)
            {
                for (var x = 0; x < resolution; x++)
                {
                    var encoded = (ushort)Mathf.Clamp(
                        Mathf.FloorToInt(
                            (tile.GetHeightYX(z, x) - 500f)
                            * 65.535f),
                        0,
                        65535);
                    byte alpha = 0;
                    if (x < maskResolution && z < maskResolution)
                    {
                        var maskIndex = z * maskResolution + x;
                        var vegetationValue = (byte)Mathf.Clamp(
                            Mathf.RoundToInt(
                                (255f - vegetation[maskIndex])
                                / 255f * 7f),
                            0,
                            7);
                        var waterValue = water[maskIndex] >= 128
                            ? 1
                            : 0;
                        alpha = (byte)(
                            waterValue << 7
                            | vegetationValue << 4);
                    }
                    pixels[z * resolution + x] = new Color32(
                        (byte)(encoded >> 8),
                        (byte)(encoded & 0xff),
                        0,
                        alpha);
                }
            }
            var texture = new Texture2D(
                resolution,
                resolution,
                TextureFormat.RGBA32,
                false);
            try
            {
                texture.SetPixels32(pixels);
                texture.Apply(false, false);
                var bytes = ImageConversion.EncodeToPNG(texture);
                if (bytes == null || bytes.Length == 0)
                {
                    throw new InvalidOperationException(
                        "Unity could not encode the terrain tile.");
                }
                Directory.CreateDirectory(
                    Path.GetDirectoryName(outputPath)
                    ?? string.Empty);
                var temporary = outputPath + ".tile-editor.tmp";
                File.WriteAllBytes(temporary, bytes);
                if (File.Exists(outputPath))
                {
                    try
                    {
                        File.Replace(temporary, outputPath, null);
                    }
                    catch
                    {
                        File.Delete(outputPath);
                        File.Move(temporary, outputPath);
                    }
                }
                else
                {
                    File.Move(temporary, outputPath);
                }
            }
            finally
            {
                UnityEngine.Object.Destroy(texture);
            }
        }

        private static string GetTileDataPath(TileData tile)
        {
            return TileDataPathField?.GetValue(tile) as string
                   ?? string.Empty;
        }

        private static string FormatTerrainTileName(Vector2Int key)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "tile_{0:000}_{1:000}.data",
                key.x,
                key.y);
        }

        private void TrimTerrainUndo()
        {
            if (_terrainUndo.Count <= MaximumTerrainUndoRecords)
                return;
            var retained = _terrainUndo
                .Take(MaximumTerrainUndoRecords)
                .Reverse()
                .ToArray();
            _terrainUndo.Clear();
            foreach (var item in retained)
                _terrainUndo.Push(item);
        }

        private void DisposeTerrainSession()
        {
            _currentTerrainEdit = null;
            _terrainUndo.Clear();
            _terrainRedo.Clear();
            _dirtyTerrainTiles.Clear();
            _terrainBackups.Clear();
        }
    }
}
