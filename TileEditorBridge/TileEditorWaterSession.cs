using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Hrogers.TileEditorBridge
{
    internal sealed partial class TileEditorGraphSession
    {
        internal sealed class WaterSurfaceInfo
        {
            internal string Id = string.Empty;
            internal string SourceLakePath = string.Empty;
            internal string MaterialName = string.Empty;
            internal bool LockHeight = true;
            internal bool SnapToTerrain;
            internal bool EnableCollider = true;
            internal float UvScale = 1f;
            internal float TriangleDensity = 0.2f;
            internal float MaximumTriangleArea = 50f;
            internal float YOffset;
            internal Vector3[] Points = Array.Empty<Vector3>();
        }

        internal sealed class BaseLakeInfo
        {
            internal string Path = string.Empty;
            internal string Name = string.Empty;
            internal string MaterialName = string.Empty;
            internal Vector3[] Points = Array.Empty<Vector3>();
        }

        private readonly HashSet<string> _editorAppliedWaterSurfaceIds =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, bool> _baseLakeOriginalActiveStates =
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        internal IReadOnlyList<WaterSurfaceInfo> WaterSurfaces =>
            ReadWaterSurfaces();

        internal IReadOnlyList<BaseLakeInfo> BaseLakes => DiscoverBaseLakes();

        internal string CreateWaterSurfaceRectangle(
            string id,
            Vector3 center,
            float yaw,
            float width,
            float length,
            string sourceLakePath,
            string materialName,
            bool lockHeight,
            bool snapToTerrain,
            bool enableCollider,
            float uvScale,
            float triangleDensity,
            float maximumTriangleArea,
            float yOffset)
        {
            RequireNativeWaterDocument();
            id = RequireWaterId(id);
            if (width <= 0f || length <= 0f)
                throw new InvalidOperationException("Water width and length must be greater than zero.");

            var rotation = Quaternion.Euler(0f, yaw, 0f);
            var halfWidth = width * 0.5f;
            var halfLength = length * 0.5f;
            var points = new[]
            {
                center + rotation * new Vector3(-halfWidth, 0f, -halfLength),
                center + rotation * new Vector3(-halfWidth, 0f, halfLength),
                center + rotation * new Vector3(halfWidth, 0f, halfLength),
                center + rotation * new Vector3(halfWidth, 0f, -halfLength),
            };
            var info = new WaterSurfaceInfo
            {
                Id = id,
                SourceLakePath = (sourceLakePath ?? string.Empty).Trim(),
                MaterialName = (materialName ?? string.Empty).Trim(),
                LockHeight = lockHeight,
                SnapToTerrain = snapToTerrain,
                EnableCollider = enableCollider,
                UvScale = uvScale,
                TriangleDensity = triangleDensity,
                MaximumTriangleArea = maximumTriangleArea,
                YOffset = yOffset,
                Points = points,
            };
            ValidateWaterSurface(info);
            ExecuteOperationsEdit("Create water surface", () =>
            {
                var surfaces = EnsureWaterSurfacesObject();
                if (surfaces[id] != null)
                    throw new InvalidOperationException($"Water surface '{id}' already exists in this package.");
                surfaces[id] = WriteWaterSurface(info);
                ApplyRuntimeWaterSurface(info);
            });
            return "Created water surface " + id;
        }

        internal string ReplaceBaseLake(
            string id,
            BaseLakeInfo source)
        {
            RequireNativeWaterDocument();
            if (source == null || string.IsNullOrWhiteSpace(source.Path))
                throw new InvalidOperationException("Choose a base lake first.");
            id = RequireWaterId(id);
            var info = new WaterSurfaceInfo
            {
                Id = id,
                SourceLakePath = source.Path,
                MaterialName = source.MaterialName,
                Points = source.Points?.ToArray() ?? Array.Empty<Vector3>(),
            };
            ValidateWaterSurface(info);
            ExecuteOperationsEdit("Replace base lake with editable water", () =>
            {
                var surfaces = EnsureWaterSurfacesObject();
                if (surfaces[id] != null)
                    throw new InvalidOperationException($"Water surface '{id}' already exists in this package.");
                surfaces[id] = WriteWaterSurface(info);
                EnsureStringArray("suppressBaseScenePaths", source.Path);
                ApplyRuntimeWaterSurface(info);
                var sourceObject = FindGameObjectByPath(source.Path);
                if (sourceObject != null)
                {
                    if (!_baseLakeOriginalActiveStates.ContainsKey(source.Path))
                        _baseLakeOriginalActiveStates[source.Path] = sourceObject.activeSelf;
                    sourceObject.SetActive(false);
                }
            });
            return "Replaced base lake with editable water surface " + id;
        }

        internal string UpdateWaterSurface(WaterSurfaceInfo info)
        {
            RequireNativeWaterDocument();
            if (info == null)
                throw new ArgumentNullException(nameof(info));
            info.Id = RequireWaterId(info.Id);
            ValidateWaterSurface(info);
            ExecuteOperationsEdit("Update water surface", () =>
            {
                var surfaces = EnsureWaterSurfacesObject();
                if (surfaces[info.Id] == null)
                    throw new InvalidOperationException($"Water surface '{info.Id}' was not found in this package.");
                surfaces[info.Id] = WriteWaterSurface(info);
                ApplyRuntimeWaterSurface(info);
            });
            return "Updated water surface " + info.Id;
        }

        internal string DeleteWaterSurface(string id)
        {
            RequireNativeWaterDocument();
            id = RequireWaterId(id);
            ExecuteOperationsEdit("Delete water surface", () =>
            {
                var surfaces = EnsureWaterSurfacesObject();
                var entry = surfaces[id] as JObject;
                if (entry == null)
                    throw new InvalidOperationException($"Water surface '{id}' was not found in this package.");
                var sourcePath = ((string)entry["sourceLakePath"] ?? string.Empty).Trim();
                surfaces.Property(id)?.Remove();
                TryRemoveRuntimeWaterSurface(id);
                _editorAppliedWaterSurfaceIds.Remove(id);
                if (!string.IsNullOrWhiteSpace(sourcePath)
                    && !surfaces.Properties().Any(property => string.Equals(
                        (string)property.Value?["sourceLakePath"],
                        sourcePath,
                        StringComparison.OrdinalIgnoreCase)))
                {
                    RemoveStringArrayValue("suppressBaseScenePaths", sourcePath);
                    RestoreEditorHiddenBaseLake(sourcePath);
                }
            });
            return "Deleted water surface " + id;
        }

        private void ResetWaterSession()
        {
            _editorAppliedWaterSurfaceIds.Clear();
            _baseLakeOriginalActiveStates.Clear();
        }

        private void SyncWaterSurfacesAfterDocumentRestore()
        {
            if (!_fuseNativeDocument || _document == null)
                return;
            var current = ReadWaterSurfaces();
            var currentIds = new HashSet<string>(
                current.Select(info => info.Id),
                StringComparer.OrdinalIgnoreCase);
            foreach (var id in _editorAppliedWaterSurfaceIds.ToArray())
            {
                if (!currentIds.Contains(id))
                {
                    TryRemoveRuntimeWaterSurface(id);
                    _editorAppliedWaterSurfaceIds.Remove(id);
                }
            }
            foreach (var info in current)
                ApplyRuntimeWaterSurface(info);
            SyncEditorHiddenBaseLakes(current);
        }

        private void SyncEditorHiddenBaseLakes(IReadOnlyList<WaterSurfaceInfo> current)
        {
            var sourcePaths = new HashSet<string>(
                (current ?? Array.Empty<WaterSurfaceInfo>())
                    .Select(info => info?.SourceLakePath)
                    .Where(path => !string.IsNullOrWhiteSpace(path)),
                StringComparer.OrdinalIgnoreCase);
            foreach (var path in _baseLakeOriginalActiveStates.Keys.ToArray())
            {
                if (sourcePaths.Contains(path))
                {
                    var sourceObject = FindGameObjectByPath(path);
                    if (sourceObject != null)
                        sourceObject.SetActive(false);
                }
                else
                {
                    RestoreEditorHiddenBaseLake(path);
                }
            }
        }

        private void RestoreEditorHiddenBaseLake(string path)
        {
            if (string.IsNullOrWhiteSpace(path)
                || !_baseLakeOriginalActiveStates.TryGetValue(path, out var wasActive))
                return;
            _baseLakeOriginalActiveStates.Remove(path);
            if (!wasActive || IsScenePathSuppressedByFuse(path))
                return;
            var sourceObject = FindGameObjectByPath(path);
            if (sourceObject != null)
                sourceObject.SetActive(true);
        }

        private static bool IsScenePathSuppressedByFuse(string path)
        {
            try
            {
                var suppressor = FindLoadedType("FUSE.Loading.FuseWorldSuppressor");
                var active = suppressor?
                    .GetMethod(
                        "GetActiveScenePathSuppressions",
                        BindingFlags.Public | BindingFlags.Static)?
                    .Invoke(null, null) as IEnumerable;
                return active != null && active.Cast<object>().Any(value => string.Equals(
                    Convert.ToString(value, CultureInfo.InvariantCulture),
                    path,
                    StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return true;
            }
        }

        private IReadOnlyList<WaterSurfaceInfo> ReadWaterSurfaces()
        {
            if (!(_document?["world"]?["waterSurfaces"] is JObject surfaces))
                return Array.Empty<WaterSurfaceInfo>();
            return surfaces.Properties()
                .Where(property => property.Value is JObject)
                .Select(property => ReadWaterSurface(property.Name, (JObject)property.Value))
                .Where(info => info != null)
                .OrderBy(info => info.Id, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static WaterSurfaceInfo ReadWaterSurface(string id, JObject entry)
        {
            if (entry == null)
                return null;
            return new WaterSurfaceInfo
            {
                Id = id,
                SourceLakePath = (string)entry["sourceLakePath"] ?? string.Empty,
                MaterialName = (string)entry["materialName"] ?? string.Empty,
                LockHeight = (bool?)entry["lockHeight"] ?? true,
                SnapToTerrain = (bool?)entry["snapToTerrain"] ?? false,
                EnableCollider = (bool?)entry["enableCollider"] ?? true,
                UvScale = (float?)entry["uvScale"] ?? 1f,
                TriangleDensity = (float?)entry["triangleDensity"] ?? 0.2f,
                MaximumTriangleArea = (float?)entry["maximumTriangleArea"] ?? 50f,
                YOffset = (float?)entry["yOffset"] ?? 0f,
                Points = (entry["points"] as JArray)?.OfType<JObject>()
                    .Select(ReadVector3)
                    .ToArray() ?? Array.Empty<Vector3>(),
            };
        }

        private static JObject WriteWaterSurface(WaterSurfaceInfo info)
        {
            var entry = new JObject
            {
                ["points"] = new JArray((info.Points ?? Array.Empty<Vector3>()).Select(WriteVector3)),
                ["lockHeight"] = info.LockHeight,
                ["snapToTerrain"] = info.SnapToTerrain,
                ["enableCollider"] = info.EnableCollider,
                ["uvScale"] = info.UvScale,
                ["triangleDensity"] = info.TriangleDensity,
                ["maximumTriangleArea"] = info.MaximumTriangleArea,
                ["yOffset"] = info.YOffset,
            };
            if (!string.IsNullOrWhiteSpace(info.SourceLakePath))
                entry["sourceLakePath"] = info.SourceLakePath.Trim();
            if (!string.IsNullOrWhiteSpace(info.MaterialName))
                entry["materialName"] = info.MaterialName.Trim();
            return entry;
        }

        private JObject EnsureWaterSurfacesObject()
        {
            if (!(_document["world"] is JObject world))
            {
                world = new JObject();
                _document["world"] = world;
            }
            if (!(world["waterSurfaces"] is JObject surfaces))
            {
                surfaces = new JObject();
                world["waterSurfaces"] = surfaces;
            }
            return surfaces;
        }

        private void EnsureStringArray(string key, string value)
        {
            var world = _document["world"] as JObject ?? new JObject();
            _document["world"] = world;
            if (!(world[key] is JArray values))
            {
                values = new JArray();
                world[key] = values;
            }
            if (!values.Values<string>().Any(existing => string.Equals(existing, value, StringComparison.OrdinalIgnoreCase)))
                values.Add(value);
        }

        private void RemoveStringArrayValue(string key, string value)
        {
            if (!(_document?["world"]?[key] is JArray values))
                return;
            foreach (var token in values.Where(token => string.Equals((string)token, value, StringComparison.OrdinalIgnoreCase)).ToArray())
                token.Remove();
        }

        private static void ValidateWaterSurface(WaterSurfaceInfo info)
        {
            if (info.Points == null || info.Points.Length < 3)
                throw new InvalidOperationException("A water surface requires at least three boundary points.");
            if (info.UvScale <= 0f)
                throw new InvalidOperationException("Water UV scale must be greater than zero.");
            if (info.TriangleDensity <= 0f || info.TriangleDensity > 1f)
                throw new InvalidOperationException("Water triangle density must be greater than zero and at most one.");
            if (info.MaximumTriangleArea <= 0f)
                throw new InvalidOperationException("Water maximum triangle area must be greater than zero.");
        }

        private void RequireNativeWaterDocument()
        {
            RequireSession();
            if (!_fuseNativeDocument)
                throw new InvalidOperationException("Water-surface authoring is native FUSE only. Legacy RailLoader JSON has no equivalent lake-polygon schema.");
        }

        private static string RequireWaterId(string id)
        {
            id = (id ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(id))
                throw new InvalidOperationException("Water surface ID is required.");
            return id;
        }

        private void ApplyRuntimeWaterSurface(WaterSurfaceInfo info)
        {
            var definitionType = FindLoadedType("FUSE.Authoring.Data.FuseWaterSurface");
            var apiType = FindLoadedType("FUSE.Runtime.API.WaterSurfaceAPI");
            if (definitionType == null || apiType == null)
            {
                _logger?.Warning("FUSE water runtime is not available; the saved surface will appear after FUSE is updated/reloaded.");
                return;
            }
            var definition = Activator.CreateInstance(definitionType);
            SetProperty(definitionType, definition, "Points", info.Points);
            SetProperty(definitionType, definition, "SourceLakePath", NullIfBlank(info.SourceLakePath));
            SetProperty(definitionType, definition, "MaterialName", NullIfBlank(info.MaterialName));
            SetProperty(definitionType, definition, "LockHeight", info.LockHeight);
            SetProperty(definitionType, definition, "SnapToTerrain", info.SnapToTerrain);
            SetProperty(definitionType, definition, "EnableCollider", info.EnableCollider);
            SetProperty(definitionType, definition, "UvScale", info.UvScale);
            SetProperty(definitionType, definition, "TriangleDensity", info.TriangleDensity);
            SetProperty(definitionType, definition, "MaximumTriangleArea", info.MaximumTriangleArea);
            SetProperty(definitionType, definition, "YOffset", info.YOffset);

            var get = apiType.GetMethod("GetWaterSurface", BindingFlags.Public | BindingFlags.Static);
            var exists = get?.Invoke(null, new object[] { info.Id }) != null;
            var method = apiType.GetMethod(
                exists ? "UpdateWaterSurface" : "AddWaterSurface",
                BindingFlags.Public | BindingFlags.Static);
            if (method == null)
                throw new InvalidOperationException("The installed FUSE water runtime is missing its add/update API.");
            method.Invoke(null, new[] { (object)info.Id, definition });
            _editorAppliedWaterSurfaceIds.Add(info.Id);
        }

        private void TryRemoveRuntimeWaterSurface(string id)
        {
            try
            {
                FindLoadedType("FUSE.Runtime.API.WaterSurfaceAPI")
                    ?.GetMethod("TryRemoveWaterSurface", BindingFlags.Public | BindingFlags.Static)
                    ?.Invoke(null, new object[] { id });
            }
            catch (TargetInvocationException ex)
            {
                _logger?.Warning("Could not remove live water surface '" + id + "': " + (ex.InnerException?.Message ?? ex.Message));
            }
        }

        private static void SetProperty(Type type, object instance, string name, object value)
        {
            type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.SetValue(instance, value, null);
        }

        private static string NullIfBlank(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static IReadOnlyList<BaseLakeInfo> DiscoverBaseLakes()
        {
            return Resources.FindObjectsOfTypeAll<LakePolygon>()
                .Where(lake => lake != null
                    && lake.gameObject.scene.IsValid()
                    && lake.GetComponents<Component>().All(component => component == null
                        || component.GetType().FullName != "FUSE.Runtime.API.FuseWaterSurfaceMarker"))
                .Select(lake => new BaseLakeInfo
                {
                    Path = TransformPath(lake.transform),
                    Name = lake.name,
                    MaterialName = lake.GetComponent<MeshRenderer>()?.sharedMaterial?.name ?? string.Empty,
                    Points = (lake.points ?? new List<Vector3>())
                        .Select(lake.transform.TransformPoint)
                        .ToArray(),
                })
                .Where(info => !string.IsNullOrWhiteSpace(info.Path) && info.Points.Length >= 3)
                .GroupBy(info => info.Path, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(info => info.Path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static GameObject FindGameObjectByPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;
            var direct = GameObject.Find(path);
            if (direct != null)
                return direct;
            return Resources.FindObjectsOfTypeAll<Transform>()
                .FirstOrDefault(transform => transform != null
                    && transform.gameObject.scene.IsValid()
                    && string.Equals(TransformPath(transform), path, StringComparison.OrdinalIgnoreCase))
                ?.gameObject;
        }

        private static string TransformPath(Transform transform)
        {
            var names = new Stack<string>();
            for (var current = transform; current != null; current = current.parent)
                names.Push(current.name);
            return string.Join("/", names.ToArray());
        }

        private static JObject WriteVector3(Vector3 value)
        {
            return new JObject
            {
                ["x"] = value.x,
                ["y"] = value.y,
                ["z"] = value.z,
            };
        }

        private static Vector3 ReadVector3(JObject value)
        {
            return new Vector3(
                (float?)value?["x"] ?? 0f,
                (float?)value?["y"] ?? 0f,
                (float?)value?["z"] ?? 0f);
        }
    }
}
