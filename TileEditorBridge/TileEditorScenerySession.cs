using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Helpers;
using Map.Runtime;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Hrogers.TileEditorBridge
{
    internal sealed partial class TileEditorGraphSession
    {
        internal sealed class SceneryInfo
        {
            internal string Id = string.Empty;
            internal string ModelIdentifier = string.Empty;
            internal Vector3 Position;
            internal Vector3 Rotation;
            internal Vector3 Scale;
        }

        private sealed class SceneryModel
        {
            internal string Id;
            internal string ModelIdentifier;
            internal Vector3 Position;
            internal Vector3 Rotation;
            internal Vector3 Scale;
        }

        private SceneryAssetInstance _selectedScenery;
        private string _selectedSceneryId = string.Empty;
        private bool _sceneryMode;
        private bool _poleMode;
        private int _liveSceneryCount;
        private int _sceneryOverlaySignature;
        private List<string> _sceneryAssetIdentifiers;
        private int _runtimeSceneryAssetCount;
        private int _railLoaderSceneryAssetCount;
        private string _cachedScenerySearch = string.Empty;
        private int _cachedScenerySearchOffset = -1;
        private int _cachedScenerySearchMaximum = -1;
        private int _cachedScenerySearchTotal;
        private IReadOnlyList<string> _cachedScenerySearchResults =
            Array.Empty<string>();

        internal SceneryInfo SelectedScenery
        {
            get
            {
                var selected = ResolveSelectedScenery();
                if (selected == null)
                    return null;
                var model = CaptureScenery(selected);
                return new SceneryInfo
                {
                    Id = model.Id,
                    ModelIdentifier = model.ModelIdentifier,
                    Position = model.Position,
                    Rotation = model.Rotation,
                    Scale = model.Scale,
                };
            }
        }

        internal int LiveSceneryCount => _liveSceneryCount;
        internal string SceneryAssetLibrarySummary
        {
            get
            {
                EnsureSceneryAssetLibrary();
                return _sceneryAssetIdentifiers.Count
                       + " placeable assets ("
                       + _runtimeSceneryAssetCount
                       + " runtime, "
                       + _railLoaderSceneryAssetCount
                       + " RailLoader pack additions)";
            }
        }

        internal void SetWorkspaceMode(
            bool geoActive,
            bool sceneryActive,
            bool poleActive,
            bool splineyActive,
            bool mandelaActive,
            bool trainSignalActive)
        {
            if (_workspaceModeInitialized
                && _geoWorkspaceActive == geoActive
                && _sceneryMode == sceneryActive
                && _poleMode == poleActive
                && _splineyMode == splineyActive
                && _mandelaMode == mandelaActive
                && _trainSignalMode == trainSignalActive)
            {
                return;
            }
            _workspaceModeInitialized = true;
            _geoWorkspaceActive = geoActive;
            SetSplineyMode(splineyActive);
            SetSceneryMode(sceneryActive);
            SetPoleMode(poleActive);
            SetMandelaMode(mandelaActive);
            SetTrainSignalMode(trainSignalActive);
            SetOverlaysVisible(
                _editModeActive
                && geoActive
                && (!splineyActive || _splineTrackPickMode));
        }

        internal void SetSceneryMode(bool active)
        {
            var changed = _sceneryMode != active;
            if (!changed)
                return;
            _sceneryMode = active;
            if (active && GraphOpen && changed)
                RefreshSceneryMode();
            SetSceneryOverlaysVisible(
                active && _editModeActive && GraphOpen);
        }

        internal void SetPoleMode(bool active)
        {
            if (_poleMode == active)
                return;
            _poleMode = active;
            if (active && GraphOpen)
                RefreshTelegraphPoleMode();
            SetTelegraphPoleOverlaysVisible(
                active && _editModeActive && GraphOpen);
        }

        internal void SelectScenery(SceneryAssetInstance scenery)
        {
            if (!_sceneryMode || !_editModeActive || scenery == null)
                return;
            _selectedScenery = scenery;
            _selectedSceneryId = scenery.name;
            ClearSelectedTelegraphPole();
            RefreshSceneryOverlayColors();
        }

        internal bool IsSelectedScenery(SceneryAssetInstance scenery)
        {
            return scenery != null
                   && !string.IsNullOrWhiteSpace(_selectedSceneryId)
                   && string.Equals(
                       scenery.name,
                       _selectedSceneryId,
                       StringComparison.Ordinal);
        }

        internal void ClearSelectedScenery()
        {
            _selectedScenery = null;
            _selectedSceneryId = string.Empty;
            RefreshSceneryOverlayColors();
        }

        internal IReadOnlyList<string> SearchSceneryAssets(
            string query,
            int offset,
            int maximum,
            out int totalMatches)
        {
            EnsureSceneryAssetLibrary();
            query = (query ?? string.Empty).Trim();
            offset = Mathf.Max(0, offset);
            maximum = Mathf.Clamp(maximum, 1, 100);
            if (string.Equals(
                    query,
                    _cachedScenerySearch,
                    StringComparison.Ordinal)
                && offset == _cachedScenerySearchOffset
                && maximum == _cachedScenerySearchMaximum)
            {
                totalMatches = _cachedScenerySearchTotal;
                return _cachedScenerySearchResults;
            }

            var page = new List<string>(maximum);
            var matchCount = 0;
            foreach (var identifier in _sceneryAssetIdentifiers)
            {
                if (query.Length > 0
                    && identifier.IndexOf(
                        query,
                        StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }
                if (matchCount >= offset
                    && page.Count < maximum)
                {
                    page.Add(identifier);
                }
                matchCount++;
            }
            _cachedScenerySearch = query;
            _cachedScenerySearchOffset = offset;
            _cachedScenerySearchMaximum = maximum;
            _cachedScenerySearchTotal = matchCount;
            _cachedScenerySearchResults = page;
            totalMatches = matchCount;
            return page;
        }

        internal void RefreshSceneryAssetLibrary()
        {
            _sceneryAssetIdentifiers = null;
            InvalidateSceneryAssetSearch();
            EnsureSceneryAssetLibrary();
        }

        private void InvalidateSceneryAssetSearch()
        {
            _cachedScenerySearch = string.Empty;
            _cachedScenerySearchOffset = -1;
            _cachedScenerySearchMaximum = -1;
            _cachedScenerySearchTotal = 0;
            _cachedScenerySearchResults = Array.Empty<string>();
        }

        internal string CreateSceneryAtCamera(string modelIdentifier)
        {
            RequireSession();
            ValidateSceneryIdentifier(modelIdentifier);
            if (CameraSelector.shared == null)
                throw new InvalidOperationException(
                    "Railroader's camera is not ready.");
            var position = WorldTransformer.WorldToGame(
                CameraSelector.shared.CurrentCameraGroundPosition);
            var yaw = Camera.main == null
                ? 0f
                : Camera.main.transform.eulerAngles.y;
            return CreateSceneryAtPosition(
                modelIdentifier,
                position,
                yaw);
        }

        internal string CreateSceneryAtPosition(
            string modelIdentifier,
            Vector3 gamePosition,
            float yaw)
        {
            RequireSession();
            ValidateSceneryIdentifier(modelIdentifier);
            ValidateVector(gamePosition, "scenery placement position");
            var id = NextSceneryId();
            var model = new SceneryModel
            {
                Id = id,
                ModelIdentifier = modelIdentifier.Trim(),
                Position = gamePosition,
                Rotation = new Vector3(0f, yaw, 0f),
                Scale = Vector3.one,
            };
            ExecuteSceneryEdit(
                "Create scenery",
                new[] { id },
                () =>
                {
                    _selectedScenery = ApplySceneryModel(model);
                    _selectedSceneryId = id;
                    WriteScenery(model);
                });
            return id;
        }

        internal string DuplicateSelectedScenery()
        {
            var source = RequireScenery();
            var copy = CaptureScenery(source);
            copy.Id = NextSceneryId();
            copy.Position += Quaternion.Euler(copy.Rotation)
                             * Vector3.right * 2f;
            ExecuteSceneryEdit(
                "Duplicate scenery",
                new[] { copy.Id },
                () =>
                {
                    _selectedScenery = ApplySceneryModel(copy);
                    _selectedSceneryId = copy.Id;
                    WriteScenery(copy);
                });
            return copy.Id;
        }

        internal void MoveSelectedScenery(
            Vector3 gameOffset,
            bool localAxes)
        {
            ValidateVector(gameOffset, "scenery movement");
            EditSelectedScenery(
                "Move scenery",
                model =>
                {
                    var appliedOffset = localAxes
                        ? Quaternion.Euler(
                            0f,
                            model.Rotation.y,
                            0f) * gameOffset
                        : gameOffset;
                    model.Position += appliedOffset;
                });
        }

        internal void RotateSelectedScenery(Vector3 rotationOffset)
        {
            ValidateVector(rotationOffset, "scenery rotation");
            EditSelectedScenery(
                "Rotate scenery",
                model => model.Rotation += rotationOffset);
        }

        internal void ScaleSelectedScenery(Vector3 scaleOffset)
        {
            ValidateVector(scaleOffset, "scenery scale");
            EditSelectedScenery(
                "Scale scenery",
                model =>
                {
                    model.Scale += scaleOffset;
                    ValidateSceneryScale(model.Scale);
                });
        }

        internal void SetSelectedSceneryTransform(
            Vector3 position,
            Vector3 rotation,
            Vector3 scale)
        {
            ValidateVector(position, "scenery position");
            ValidateVector(rotation, "scenery rotation");
            ValidateSceneryScale(scale);
            EditSelectedScenery(
                "Set scenery transform",
                model =>
                {
                    model.Position = position;
                    model.Rotation = rotation;
                    model.Scale = scale;
                });
        }

        internal void SetSelectedSceneryModel(string modelIdentifier)
        {
            ValidateSceneryIdentifier(modelIdentifier);
            EditSelectedScenery(
                "Change scenery model",
                model => model.ModelIdentifier =
                    modelIdentifier.Trim());
        }

        internal void SnapSelectedSceneryToTerrain()
        {
            var scenery = RequireScenery();
            var world = scenery.transform.position;
            float? surfaceY = null;
            foreach (var terrain in Terrain.activeTerrains)
            {
                if (terrain == null || terrain.terrainData == null)
                    continue;
                var origin = terrain.transform.position;
                var size = terrain.terrainData.size;
                if (world.x < origin.x || world.x > origin.x + size.x
                    || world.z < origin.z || world.z > origin.z + size.z)
                {
                    continue;
                }
                surfaceY = terrain.SampleHeight(world) + origin.y;
                break;
            }

            var gamePosition = WorldTransformer.WorldToGame(world);
            if (surfaceY.HasValue)
            {
                gamePosition.y = WorldTransformer.WorldToGame(
                    new Vector3(world.x, surfaceY.Value, world.z)).y;
            }
            else
            {
                var manager = MapManager.Instance;
                if (manager == null)
                    throw new InvalidOperationException(
                        "Terrain is not ready at this location.");
                gamePosition.y =
                    manager.FindTerrainPointForXZ(gamePosition).y - 1f;
            }
            EditSelectedScenery(
                "Snap scenery to terrain",
                model => model.Position = gamePosition);
        }

        internal void ShowSelectedScenery()
        {
            var scenery = RequireScenery();
            if (CameraSelector.shared == null)
                throw new InvalidOperationException(
                    "Railroader's camera is not ready.");
            CameraSelector.shared.ZoomToPoint(scenery.transform.position);
        }

        internal void DeleteSelectedScenery()
        {
            var scenery = RequireScenery();
            var id = scenery.name;
            ExecuteSceneryEdit(
                "Delete scenery",
                new[] { id },
                () =>
                {
                    RemoveSceneryLive(id);
                    SceneryObject[id] = JValue.CreateNull();
                    _selectedScenery = null;
                    _selectedSceneryId = string.Empty;
                });
        }

        internal void RefreshSceneryOverlays()
        {
            _sceneryOverlaySignature = int.MinValue;
            _telegraphPoleOverlaySignature = int.MinValue;
            RefreshSceneryMode();
        }

        private void EditSelectedScenery(
            string name,
            Action<SceneryModel> mutation)
        {
            var scenery = RequireScenery();
            var id = scenery.name;
            ExecuteSceneryEdit(
                name,
                new[] { id },
                () =>
                {
                    var model = CaptureScenery(scenery);
                    mutation(model);
                    _selectedScenery = ApplySceneryModel(model);
                    _selectedSceneryId = id;
                    WriteScenery(model);
                });
        }

        private void ExecuteSceneryEdit(
            string name,
            IEnumerable<string> sceneryIds,
            Action mutation)
        {
            RequireSession();
            RequireGraphEditOwnership();
            var ids = sceneryIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct()
                .ToArray();
            var edit = new EditRecord
            {
                Name = name,
                NodeIds = Array.Empty<string>(),
                SegmentIds = Array.Empty<string>(),
                SceneryIds = ids,
                MandelaIds = Array.Empty<string>(),
                BeforeNodes = new Dictionary<string, NodeModel>(),
                BeforeSegments = new Dictionary<string, SegmentModel>(),
                BeforeScenery = CaptureScenery(ids),
                BeforeMandelas =
                    new Dictionary<string, MandelaModel>(),
                BeforeDocument = (JObject)_document.DeepClone(),
                BeforeSelectedNode = _selectedNode?.id,
                BeforeSelectedSegment = _selectedSegment?.id,
                BeforeSelectedScenery = _selectedSceneryId,
                BeforeSelectedMandela = _selectedMandelaPath,
            };

            mutation();
            RebuildSceneryOverlays(false);
            SetSceneryOverlaysVisible(_editModeActive && _sceneryMode);
            edit.AfterNodes = new Dictionary<string, NodeModel>();
            edit.AfterSegments = new Dictionary<string, SegmentModel>();
            edit.AfterScenery = CaptureScenery(ids);
            edit.AfterMandelas =
                new Dictionary<string, MandelaModel>();
            edit.AfterDocument = (JObject)_document.DeepClone();
            edit.AfterSelectedNode = _selectedNode?.id;
            edit.AfterSelectedSegment = _selectedSegment?.id;
            edit.AfterSelectedScenery = _selectedSceneryId;
            edit.AfterSelectedMandela = _selectedMandelaPath;
            _undo.Push(edit);
            _redo.Clear();
            _dirty = true;
        }

        private void RestoreSceneryModels(EditRecord edit, bool after)
        {
            if (edit.SceneryIds == null || edit.SceneryIds.Length == 0)
                return;
            var models = after
                ? edit.AfterScenery
                : edit.BeforeScenery;
            foreach (var id in edit.SceneryIds)
            {
                if (models != null
                    && models.TryGetValue(id, out var model)
                    && model != null)
                {
                    ApplySceneryModel(model);
                }
                else
                {
                    RemoveSceneryLive(id);
                }
            }
            var selectedId = after
                ? edit.AfterSelectedScenery
                : edit.BeforeSelectedScenery;
            _selectedSceneryId = selectedId ?? string.Empty;
            _selectedScenery = FindLiveScenery(selectedId);
            RebuildSceneryOverlays(false);
            SetSceneryOverlaysVisible(_editModeActive && _sceneryMode);
        }

        private Dictionary<string, SceneryModel> CaptureScenery(
            IEnumerable<string> ids)
        {
            return ids.ToDictionary(
                id => id,
                id =>
                {
                    var scenery = FindLiveScenery(id);
                    return scenery == null
                        ? null
                        : CaptureScenery(scenery);
                });
        }

        private static SceneryModel CaptureScenery(
            SceneryAssetInstance scenery)
        {
            return new SceneryModel
            {
                Id = scenery.name,
                ModelIdentifier = scenery.identifier ?? string.Empty,
                Position = SceneryPositionToGame(scenery),
                Rotation = scenery.transform.eulerAngles,
                Scale = scenery.transform.localScale,
            };
        }

        private SceneryAssetInstance ApplySceneryModel(
            SceneryModel model)
        {
            var scenery = FindLiveScenery(model.Id);
            if (scenery == null)
            {
                var go = new GameObject(model.Id);
                go.SetActive(false);
                scenery = go.AddComponent<SceneryAssetInstance>();
                go.AddComponent<TileEditorOwnedScenery>();
                scenery.identifier = model.ModelIdentifier;
                // AddObjectToMove expects an unshifted game position and
                // applies the current floating-origin offset exactly once.
                scenery.transform.position = model.Position;
                if (WorldTransformer.TryGetShared(out var transformer))
                    transformer.AddObjectToMove(scenery.transform);
                else
                    SetSceneryPositionFromGame(
                        scenery,
                        model.Position);
                scenery.transform.rotation =
                    Quaternion.Euler(model.Rotation);
                scenery.transform.localScale = model.Scale;
                go.SetActive(true);
            }
            else
            {
                var hidden =
                    scenery.GetComponent<TileEditorHiddenScenery>();
                if (hidden != null)
                {
                    UnityEngine.Object.DestroyImmediate(hidden);
                    scenery.gameObject.SetActive(true);
                }
                var identifierChanged = !string.Equals(
                    scenery.identifier,
                    model.ModelIdentifier,
                    StringComparison.Ordinal);
                var wasActive = scenery.gameObject.activeSelf;
                if (identifierChanged && wasActive)
                    scenery.gameObject.SetActive(false);
                scenery.identifier = model.ModelIdentifier;
                SetSceneryPositionFromGame(
                    scenery,
                    model.Position);
                scenery.transform.rotation =
                    Quaternion.Euler(model.Rotation);
                scenery.transform.localScale = model.Scale;
                if (identifierChanged && wasActive)
                    scenery.gameObject.SetActive(true);
            }
            VerifySceneryCoordinateRoundTrip(
                scenery,
                model.Position);
            try
            {
                scenery.RequestUpdateCullingPosition();
            }
            catch
            {
                // The culling token is created asynchronously for some assets.
            }
            return scenery;
        }

        private void RemoveSceneryLive(string id)
        {
            var scenery = FindLiveScenery(id, true);
            if (scenery == null)
                return;
            if (scenery.GetComponent<TileEditorOwnedScenery>() != null)
            {
                if (WorldTransformer.TryGetShared(out var transformer))
                    transformer.RemoveObjectToMove(scenery.transform);
                scenery.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(scenery.gameObject);
            }
            else
            {
                scenery.gameObject.SetActive(false);
                if (scenery.GetComponent<TileEditorHiddenScenery>() == null)
                    scenery.gameObject.AddComponent<TileEditorHiddenScenery>();
            }
            if (_selectedScenery == scenery)
                _selectedScenery = null;
        }

        private void WriteScenery(SceneryModel model)
        {
            var existing = SceneryObject[model.Id] as JObject;
            var entry = existing == null
                ? new JObject()
                : (JObject)existing.DeepClone();
            entry["modelIdentifier"] = model.ModelIdentifier;
            entry["position"] = Vector(model.Position);
            entry["rotation"] = Vector(model.Rotation);
            entry["scale"] = Vector(model.Scale);
            SceneryObject[model.Id] = entry;
        }

        private JObject SceneryObject
        {
            get
            {
                var scenery = _document?["scenery"] as JObject;
                if (scenery == null)
                {
                    scenery = new JObject();
                    _document["scenery"] = scenery;
                }
                return scenery;
            }
        }

        private void ValidateSceneryIdentifier(string identifier)
        {
            identifier = (identifier ?? string.Empty).Trim();
            if (identifier.Length == 0)
                throw new InvalidOperationException(
                    "Choose a scenery model first.");
            var manager = SceneryAssetManager.Shared;
            if (manager == null
                || !manager.TryGetSceneryDefinition(identifier, out var _))
            {
                throw new InvalidOperationException(
                    "Scenery model '" + identifier
                    + "' is not available in the loaded asset packs.");
            }
        }

        private static void ValidateSceneryScale(Vector3 scale)
        {
            ValidateVector(scale, "scenery scale");
            if (Mathf.Abs(scale.x) < 0.001f
                || Mathf.Abs(scale.y) < 0.001f
                || Mathf.Abs(scale.z) < 0.001f
                || Mathf.Abs(scale.x) > 100f
                || Mathf.Abs(scale.y) > 100f
                || Mathf.Abs(scale.z) > 100f)
            {
                throw new InvalidOperationException(
                    "Each scenery scale axis must be between -100 and 100 "
                    + "and cannot be zero.");
            }
        }

        private void EnsureSceneryAssetLibrary()
        {
            if (_sceneryAssetIdentifiers != null)
                return;
            var manager = SceneryAssetManager.Shared;
            var runtime = manager == null
                ? new List<string>()
                : manager.GetSceneryDefinitionIdentifiers();
            var identifiers = new HashSet<string>(
                runtime.Where(
                    identifier =>
                        !string.IsNullOrWhiteSpace(identifier)),
                StringComparer.OrdinalIgnoreCase);
            _runtimeSceneryAssetCount = identifiers.Count;
            _railLoaderSceneryAssetCount = 0;
            if (manager != null)
            {
                foreach (var identifier
                         in DiscoverRailLoaderSceneryIdentifiers())
                {
                    // GetSceneryDefinitionIdentifiers can be populated
                    // before RailLoader/Strange Customs finishes registering
                    // its SCAssetPacks. DefinitionForIdentifier is the
                    // authoritative late-bound lookup, so probe it directly.
                    if (identifiers.Contains(identifier)
                        || !manager.TryGetSceneryDefinition(
                            identifier,
                            out var _))
                    {
                        continue;
                    }
                    identifiers.Add(identifier);
                    _railLoaderSceneryAssetCount++;
                }
            }
            _sceneryAssetIdentifiers = identifiers
                .OrderBy(
                    identifier => identifier,
                    StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private IEnumerable<string>
            DiscoverRailLoaderSceneryIdentifiers()
        {
            var modsDirectory = Path.Combine(_gameRoot, "Mods");
            if (!Directory.Exists(modsDirectory))
                yield break;

            string[] files;
            try
            {
                files = Directory.GetFiles(
                    modsDirectory,
                    "Definitions.json",
                    SearchOption.AllDirectories);
            }
            catch (Exception ex)
            {
                _logger?.Warning(
                    "Could not enumerate RailLoader scenery packs: "
                    + ex.Message);
                yield break;
            }

            foreach (var path in files)
            {
                var marker = Path.DirectorySeparatorChar
                             + "SCAssetPacks"
                             + Path.DirectorySeparatorChar;
                if (path.IndexOf(
                        marker,
                        StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                JObject document;
                try
                {
                    document = JObject.Parse(
                        File.ReadAllText(path));
                }
                catch (Exception ex)
                {
                    _logger?.Warning(
                        "Could not read RailLoader scenery definitions "
                        + path + ": " + ex.Message);
                    continue;
                }

                if (!(document["objects"] is JArray objects))
                    continue;
                foreach (var item in objects.OfType<JObject>())
                {
                    var definition = item["definition"] as JObject;
                    if (!string.Equals(
                            (string)definition?["kind"],
                            "Scenery",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    var identifier =
                        ((string)item["identifier"]
                         ?? (string)definition["identifier"]
                         ?? string.Empty).Trim();
                    if (identifier.Length > 0)
                        yield return identifier;
                }
            }
        }

        private string NextSceneryId()
        {
            string id;
            do
            {
                id = "SC_TE_"
                     + Guid.NewGuid().ToString("N").Substring(0, 8);
            } while (FindLiveScenery(id) != null
                     || SceneryObject.Property(id) != null);
            return id;
        }

        private SceneryAssetInstance RequireScenery()
        {
            RequireSession();
            var selected = ResolveSelectedScenery();
            return selected != null
                ? selected
                : throw new InvalidOperationException(
                    "Click a cyan scenery marker first.");
        }

        private void ResetScenerySession()
        {
            _selectedScenery = null;
            _selectedSceneryId = string.Empty;
            _liveSceneryCount = 0;
            _sceneryOverlaySignature = 0;
            DisposeSceneryOverlays();
            DisposeTelegraphPoleSession();
            if (_sceneryMode)
                RefreshSceneryMode();
        }

        private void RefreshSceneryMode()
        {
            if (!GraphOpen)
                return;
            ReconcileLoaderScenery();
            ResolveSelectedScenery();
            var liveScenery = FindLiveScenery();
            _liveSceneryCount = liveScenery.Count;
            var signature = SceneryInstanceSignature(
                liveScenery);
            if (signature != _sceneryOverlaySignature)
            {
                RebuildSceneryOverlays(false);
                SetSceneryOverlaysVisible(
                    _editModeActive && _sceneryMode);
            }
        }

        private void RebuildSceneryOverlays(bool rebuildExisting)
        {
            var liveScenery = FindLiveScenery();
            _liveSceneryCount = liveScenery.Count;
            _sceneryOverlaySignature =
                SceneryInstanceSignature(liveScenery);
            foreach (var scenery in liveScenery)
            {
                var overlay = scenery
                    .GetComponentInChildren<TileEditorSceneryOverlay>(true);
                if (overlay == null)
                {
                    var go = new GameObject("TileEditorSceneryOverlay");
                    go.transform.SetParent(scenery.transform, false);
                    overlay = go.AddComponent<TileEditorSceneryOverlay>();
                    overlay.Initialize(this, scenery);
                }
                else if (rebuildExisting)
                {
                    overlay.Initialize(this, scenery);
                }
                else
                {
                    overlay.Refresh();
                }
            }
        }

        private void RefreshSceneryOverlayColors()
        {
            foreach (var overlay in UnityEngine.Object
                         .FindObjectsOfType<TileEditorSceneryOverlay>())
            {
                overlay?.Refresh();
            }
        }

        internal void SetSceneryOverlaysVisible(bool visible)
        {
            foreach (var overlay in UnityEngine.Object
                         .FindObjectsOfType<TileEditorSceneryOverlay>())
            {
                overlay?.SetOverlayVisible(visible);
            }
        }

        private void DisposeSceneryOverlays()
        {
            foreach (var overlay in Resources
                         .FindObjectsOfTypeAll<TileEditorSceneryOverlay>())
            {
                if (overlay != null)
                    UnityEngine.Object.Destroy(overlay.gameObject);
            }
        }

        private void DisposeScenerySession()
        {
            SetSceneryOverlaysVisible(false);
            DisposeSceneryOverlays();
            _selectedScenery = null;
            _selectedSceneryId = string.Empty;
            _liveSceneryCount = 0;
            _sceneryOverlaySignature = 0;
            DisposeTelegraphPoleSession();
        }

        private void ReconcileLoaderScenery()
        {
            var all = FindLiveScenery();
            foreach (var group in all
                         .GroupBy(
                             scenery => scenery.name,
                             StringComparer.Ordinal)
                         .Where(group => group.Count() > 1))
            {
                var preferred = group.FirstOrDefault(scenery =>
                                    scenery.GetComponent<
                                        TileEditorOwnedScenery>() == null)
                                ?? group.First();
                foreach (var duplicate in group)
                {
                    if (duplicate == preferred
                        || duplicate.GetComponent<
                            TileEditorOwnedScenery>() == null)
                    {
                        continue;
                    }
                    if (_selectedScenery == duplicate)
                        _selectedScenery = preferred;
                    if (WorldTransformer.TryGetShared(
                            out var transformer))
                    {
                        transformer.RemoveObjectToMove(
                            duplicate.transform);
                    }
                    duplicate.gameObject.SetActive(false);
                    UnityEngine.Object.Destroy(
                        duplicate.gameObject);
                }
            }
        }

        private static List<SceneryAssetInstance> FindLiveScenery()
        {
            return FindLiveScenery(false);
        }

        private static List<SceneryAssetInstance> FindLiveScenery(
            bool includeHidden)
        {
            var sceneryObjects = includeHidden
                ? Resources.FindObjectsOfTypeAll<
                    SceneryAssetInstance>()
                : UnityEngine.Object.FindObjectsOfType<
                    SceneryAssetInstance>();
            return sceneryObjects
                .Where(scenery => scenery != null
                                  && scenery.gameObject.scene.IsValid()
                                  && (includeHidden
                                      || scenery.GetComponent<
                                          TileEditorHiddenScenery>() == null)
                                  && !string.IsNullOrWhiteSpace(scenery.name))
                .ToList();
        }

        private static int SceneryInstanceSignature(
            IEnumerable<SceneryAssetInstance> scenery)
        {
            unchecked
            {
                var count = 0;
                var sum = 0;
                var xor = 0;
                foreach (var instance in scenery)
                {
                    var id = instance.GetInstanceID();
                    count++;
                    sum += id;
                    xor ^= id;
                }
                return (count * 397) ^ sum ^ (xor * 31);
            }
        }

        private static SceneryAssetInstance FindLiveScenery(string id)
        {
            return FindLiveScenery(id, false);
        }

        private static SceneryAssetInstance FindLiveScenery(
            string id,
            bool includeHidden)
        {
            if (string.IsNullOrWhiteSpace(id))
                return null;
            var scenery = FindLiveScenery(includeHidden);
            return scenery
                       .FirstOrDefault(scenery =>
                           scenery.gameObject.activeInHierarchy
                           && string.Equals(
                               scenery.name,
                               id,
                               StringComparison.Ordinal))
                   ?? scenery.FirstOrDefault(candidate =>
                       string.Equals(
                           candidate.name,
                           id,
                           StringComparison.Ordinal));
        }

        private SceneryAssetInstance ResolveSelectedScenery()
        {
            if (string.IsNullOrWhiteSpace(_selectedSceneryId))
            {
                _selectedScenery = null;
                return null;
            }
            if (_selectedScenery == null
                || !string.Equals(
                    _selectedScenery.name,
                    _selectedSceneryId,
                    StringComparison.Ordinal))
            {
                _selectedScenery = FindLiveScenery(
                    _selectedSceneryId);
            }
            return _selectedScenery;
        }

        private static Vector3 SceneryPositionToGame(
            SceneryAssetInstance scenery)
        {
            // JSON always stores stable game coordinates. Unity's visible
            // world position includes the floating-origin offset.
            return WorldTransformer.WorldToGame(
                scenery.transform.position);
        }

        private static void SetSceneryPositionFromGame(
            SceneryAssetInstance scenery,
            Vector3 gamePosition)
        {
            scenery.transform.position =
                WorldTransformer.GameToWorld(gamePosition);
        }

        private void VerifySceneryCoordinateRoundTrip(
            SceneryAssetInstance scenery,
            Vector3 expectedGamePosition)
        {
            var actualGamePosition =
                SceneryPositionToGame(scenery);
            if (Vector3.Distance(
                    actualGamePosition,
                    expectedGamePosition) <= 0.01f)
            {
                return;
            }
            _logger?.Warning(
                "Correcting scenery coordinate-frame mismatch for "
                + scenery.name
                + ": expected game "
                + expectedGamePosition
                + ", captured "
                + actualGamePosition);
            SetSceneryPositionFromGame(
                scenery,
                expectedGamePosition);
        }
    }

    internal sealed class TileEditorOwnedScenery : MonoBehaviour
    {
    }

    internal sealed class TileEditorHiddenScenery : MonoBehaviour
    {
    }

    internal sealed class TileEditorSceneryOverlay
        : MonoBehaviour, IPickable
    {
        private TileEditorGraphSession _session;
        private SceneryAssetInstance _scenery;
        private LineRenderer _line;
        private BoxCollider _collider;

        public float MaxPickDistance => 600f;
        public int Priority => 18;
        public PickableActivationFilter ActivationFilter =>
            PickableActivationFilter.Any;

        public TooltipInfo TooltipInfo
        {
            get
            {
                if (_scenery == null)
                    return TooltipInfo.Empty;
                var position = WorldTransformer.WorldToGame(
                    _scenery.transform.position);
                return new TooltipInfo(
                    "Tile Editor Scenery " + _scenery.name,
                    "Model: " + _scenery.identifier
                    + "\nPosition: "
                    + position.x.ToString("F2",
                        CultureInfo.InvariantCulture)
                    + ", "
                    + position.y.ToString("F2",
                        CultureInfo.InvariantCulture)
                    + ", "
                    + position.z.ToString("F2",
                        CultureInfo.InvariantCulture));
            }
        }

        internal void Initialize(
            TileEditorGraphSession session,
            SceneryAssetInstance scenery)
        {
            _session = session;
            _scenery = scenery;
            BuildVisual();
        }

        internal void Refresh()
        {
            if (_line == null || _session == null || _scenery == null)
                return;
            KeepMarkerWorldScale();
            TileEditorOverlayVisuals.SetColor(
                _line,
                _session.IsSelectedScenery(_scenery)
                    ? Color.magenta
                    : Color.cyan);
        }

        internal void SetOverlayVisible(bool visible)
        {
            enabled = visible;
            if (_line != null)
                _line.enabled = visible;
            if (_collider != null)
                _collider.enabled = visible;
            if (visible)
                Refresh();
        }

        public void Activate(PickableActivateEvent evt)
        {
            if (!TileEditorCameraInput.EditorWorldInputBlocked)
                _session?.SelectScenery(_scenery);
        }

        public void Deactivate()
        {
        }

        private void BuildVisual()
        {
            if (_scenery == null)
                return;
            gameObject.layer = Helpers.Layers.Clickable;
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            KeepMarkerWorldScale();

            _line = GetComponent<LineRenderer>()
                    ?? gameObject.AddComponent<LineRenderer>();
            _line.sharedMaterial =
                TileEditorOverlayVisuals.SharedLineMaterial;
            _line.startWidth = 0.09f;
            _line.endWidth = 0.09f;
            _line.useWorldSpace = false;
            _line.loop = false;
            _line.positionCount = 9;
            _line.SetPositions(new[]
            {
                new Vector3(0f, 0.05f, 0.65f),
                new Vector3(0.5f, 0.05f, 0f),
                new Vector3(0f, 0.05f, -0.5f),
                new Vector3(-0.5f, 0.05f, 0f),
                new Vector3(0f, 0.05f, 0.65f),
                new Vector3(0f, 2.2f, 0f),
                new Vector3(0.28f, 1.75f, 0f),
                new Vector3(-0.28f, 1.75f, 0f),
                new Vector3(0f, 2.2f, 0f),
            });

            _collider = GetComponent<BoxCollider>()
                        ?? gameObject.AddComponent<BoxCollider>();
            _collider.center = new Vector3(0f, 1.05f, 0f);
            _collider.size = new Vector3(1.25f, 2.3f, 1.25f);
            Refresh();
        }

        private void KeepMarkerWorldScale()
        {
            if (_scenery == null)
                return;
            var scale = _scenery.transform.lossyScale;
            transform.localScale = new Vector3(
                Mathf.Abs(scale.x) < 0.001f ? 1f : 1f / scale.x,
                Mathf.Abs(scale.y) < 0.001f ? 1f : 1f / scale.y,
                Mathf.Abs(scale.z) < 0.001f ? 1f : 1f / scale.z);
        }

    }
}
