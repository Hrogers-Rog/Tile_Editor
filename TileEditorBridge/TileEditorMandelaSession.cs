using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Helpers;
using Map.Runtime;
using Newtonsoft.Json.Linq;
using Track;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Hrogers.TileEditorBridge
{
    internal sealed partial class TileEditorGraphSession
    {
        internal sealed class MandelaInfo
        {
            internal string TargetPath = string.Empty;
            internal string SourcePath = string.Empty;
            internal Vector3 LocalPosition;
            internal Vector3 LocalRotation;
            internal Vector3 LocalScale;
            internal bool Active;
            internal bool IsClone;
            internal bool IsBaseGameSign;
            internal bool CloneSafe;
            internal string SafetyMessage = string.Empty;
        }

        private sealed class MandelaModel
        {
            internal string TargetPath;
            internal string SourcePath;
            internal Vector3 LocalPosition;
            internal Vector3 LocalRotation;
            internal Vector3 LocalScale;
            internal bool Active;
        }

        private readonly RaycastHit[] _mandelaRaycastHits =
            new RaycastHit[128];
        private GameObject _selectedMandela;
        private Transform _mandelaClickedTransform;
        private string _selectedMandelaPath = string.Empty;
        private bool _mandelaMode;

        internal MandelaInfo SelectedMandela
        {
            get
            {
                var selected = ResolveSelectedMandela();
                if (selected == null)
                    return null;
                var sourcePath = ReadMandelaSourcePath(
                    _selectedMandelaPath);
                var safe = IsSafeMandelaCloneSource(
                    ResolveMandelaCloneSource(selected, sourcePath),
                    out var safetyMessage);
                return new MandelaInfo
                {
                    TargetPath = _selectedMandelaPath,
                    SourcePath = sourcePath,
                    LocalPosition = selected.transform.localPosition,
                    LocalRotation = selected.transform.localEulerAngles,
                    LocalScale = selected.transform.localScale,
                    Active = selected.activeSelf,
                    IsClone = !string.IsNullOrWhiteSpace(sourcePath),
                    IsBaseGameSign =
                        string.IsNullOrWhiteSpace(sourcePath)
                        && LooksLikeBaseGameSign(
                            selected,
                            _selectedMandelaPath),
                    CloneSafe = safe,
                    SafetyMessage = safetyMessage,
                };
            }
        }

        internal int MandelaOverrideCount =>
            _document?["mandelas"] is JObject mandelas
                ? mandelas.Properties().Count()
                : 0;

        internal IReadOnlyList<string> SearchMandelaOverrides(
            string query,
            int offset,
            int maximum,
            out int totalMatches)
        {
            query = (query ?? string.Empty).Trim();
            var matches = MandelasObject.Properties()
                .Where(property =>
                    query.Length == 0
                    || property.Name.IndexOf(
                        query,
                        StringComparison.OrdinalIgnoreCase) >= 0
                    || (property.Value["instantiateFrom"]?
                            .Value<string>() ?? string.Empty)
                        .IndexOf(
                            query,
                            StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            totalMatches = matches.Length;
            return matches
                .Skip(Mathf.Max(0, offset))
                .Take(Mathf.Clamp(maximum, 1, 100))
                .ToArray();
        }

        internal void SetMandelaMode(bool active)
        {
            _mandelaMode = active;
            if (!active)
                return;
            ResolveSelectedMandela();
        }

        internal bool SelectMandelaUnderPointer(
            Ray ray,
            out string status)
        {
            RequireSession();
            var clicked = FindMandelaRayTarget(ray);
            if (clicked == null)
            {
                status =
                    "No editable base-game object was found under the pointer.";
                return false;
            }

            _mandelaClickedTransform = clicked;
            var promoted = PromoteMandelaSelection(clicked);
            SelectMandelaTransform(promoted);
            status = "Selected " + _selectedMandelaPath;
            return true;
        }

        internal void SelectMandelaParent()
        {
            var selected = RequireMandela();
            var parent = selected.transform.parent;
            var reason = string.Empty;
            if (parent == null
                || WouldCrossMandelaAggregate(
                    selected.transform,
                    parent,
                    out reason))
            {
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(reason)
                        ? "The scene root cannot be edited as one object."
                        : reason);
            }
            SelectMandelaTransform(parent);
        }

        internal void SelectMandelaClickedPart()
        {
            if (_mandelaClickedTransform == null)
            {
                throw new InvalidOperationException(
                    "Click an object in the world first.");
            }
            SelectMandelaTransform(_mandelaClickedTransform);
        }

        internal void SelectMandelaOverride(string targetPath)
        {
            RequireSession();
            targetPath = (targetPath ?? string.Empty).Trim();
            if (targetPath.Length == 0)
                throw new InvalidOperationException(
                    "The object override has no target path.");
            var target = FindSceneObjectByPath(targetPath);
            if (target == null)
            {
                target = MaterializeMandelaFromDocument(targetPath);
            }
            if (target == null)
            {
                throw new InvalidOperationException(
                    "The saved object is not currently loaded in the scene: "
                    + targetPath);
            }
            _mandelaClickedTransform = target.transform;
            SelectMandelaTransform(target.transform, targetPath);
        }

        internal void ClearSelectedMandela()
        {
            _selectedMandela = null;
            _mandelaClickedTransform = null;
            _selectedMandelaPath = string.Empty;
        }

        internal bool TryGetSelectedMandelaBounds(out Bounds bounds)
        {
            bounds = default;
            var selected = ResolveSelectedMandela();
            if (selected == null)
                return false;
            var renderers = selected.GetComponentsInChildren<Renderer>(true)
                .Where(renderer => renderer != null)
                .ToArray();
            if (renderers.Length == 0)
            {
                bounds = new Bounds(
                    selected.transform.position,
                    Vector3.one * 2f);
                return true;
            }
            bounds = renderers[0].bounds;
            for (var index = 1; index < renderers.Length; index++)
                bounds.Encapsulate(renderers[index].bounds);
            return true;
        }

        internal void ShowSelectedMandela()
        {
            var selected = RequireMandela();
            if (CameraSelector.shared == null)
            {
                throw new InvalidOperationException(
                    "Railroader's camera is not ready.");
            }
            CameraSelector.shared.ZoomToPoint(selected.transform.position);
        }

        internal void MoveSelectedMandela(
            Vector3 offset,
            bool localAxes)
        {
            ValidateVector(offset, "object movement");
            EditSelectedMandela(
                "Move base-game object",
                target =>
                {
                    var worldOffset = localAxes
                        ? target.transform.rotation * offset
                        : offset;
                    target.transform.position += worldOffset;
                });
        }

        internal void RotateSelectedMandela(Vector3 localRotationOffset)
        {
            ValidateVector(localRotationOffset, "object rotation");
            EditSelectedMandela(
                "Rotate base-game object",
                target =>
                {
                    target.transform.localEulerAngles +=
                        localRotationOffset;
                });
        }

        internal void ScaleSelectedMandela(Vector3 scaleOffset)
        {
            ValidateVector(scaleOffset, "object scale");
            EditSelectedMandela(
                "Scale base-game object",
                target =>
                {
                    var scale = target.transform.localScale + scaleOffset;
                    if (Mathf.Abs(scale.x) < 0.001f
                        || Mathf.Abs(scale.y) < 0.001f
                        || Mathf.Abs(scale.z) < 0.001f
                        || Mathf.Abs(scale.x) > 100f
                        || Mathf.Abs(scale.y) > 100f
                        || Mathf.Abs(scale.z) > 100f)
                    {
                        throw new InvalidOperationException(
                            "Each scale axis must be between -100 and 100 "
                            + "and cannot be zero.");
                    }
                    target.transform.localScale = scale;
                });
        }

        internal void SetSelectedMandelaTransform(
            Vector3 localPosition,
            Vector3 localRotation,
            Vector3 localScale)
        {
            ValidateVector(localPosition, "object local position");
            ValidateVector(localRotation, "object local rotation");
            ValidateVector(localScale, "object local scale");
            if (Mathf.Abs(localScale.x) < 0.001f
                || Mathf.Abs(localScale.y) < 0.001f
                || Mathf.Abs(localScale.z) < 0.001f)
            {
                throw new InvalidOperationException(
                    "Object scale axes cannot be zero.");
            }
            EditSelectedMandela(
                "Set base-game object transform",
                target =>
                {
                    target.transform.localPosition = localPosition;
                    target.transform.localEulerAngles = localRotation;
                    target.transform.localScale = localScale;
                });
        }

        internal void SetSelectedMandelaActive(bool active)
        {
            EditSelectedMandela(
                active
                    ? "Enable base-game object"
                    : "Disable base-game object",
                target => target.SetActive(active));
        }

        internal string CloneSelectedMandelaBeside()
        {
            var selected = RequireMandela();
            return CloneSelectedMandelaAtWorldPosition(
                selected.transform.position
                + selected.transform.right * 3f);
        }

        internal string CloneSelectedMandelaAtWorldPosition(
            Vector3 worldPosition)
        {
            ValidateVector(worldPosition, "object clone position");
            var selected = RequireMandela();
            var selectedSource = ReadMandelaSourcePath(
                _selectedMandelaPath);
            var source = ResolveMandelaCloneSource(
                selected,
                selectedSource);
            if (!IsSafeMandelaCloneSource(
                    source,
                    out var safetyMessage))
            {
                throw new InvalidOperationException(safetyMessage);
            }

            var sourcePath = string.IsNullOrWhiteSpace(selectedSource)
                ? GetTransformPath(source.transform)
                : selectedSource;
            var parent = source.transform.parent;
            if (parent == null)
            {
                throw new InvalidOperationException(
                    "Scene root objects cannot be cloned.");
            }
            var targetName = NextMandelaCloneName(
                parent,
                source.name);
            var targetPath = GetTransformPath(parent)
                             + "/" + targetName;
            ExecuteMandelaEdit(
                "Clone base-game object",
                new[] { targetPath },
                () =>
                {
                    var clone = UnityEngine.Object.Instantiate(
                        source,
                        parent);
                    clone.name = targetName;
                    clone.transform.position = worldPosition;
                    clone.transform.rotation =
                        selected.transform.rotation;
                    clone.transform.localScale =
                        selected.transform.localScale;
                    var owned =
                        clone.GetComponent<TileEditorOwnedMandela>()
                        ?? clone.AddComponent<TileEditorOwnedMandela>();
                    owned.TargetPath = targetPath;
                    owned.SourcePath = sourcePath;
                    WriteMandela(
                        clone,
                        targetPath,
                        sourcePath);
                    _selectedMandela = clone;
                    _selectedMandelaPath = targetPath;
                    _mandelaClickedTransform = clone.transform;
                });
            return targetPath;
        }

        internal void RemoveSelectedMandelaOverride()
        {
            var selected = RequireMandela();
            var targetPath = _selectedMandelaPath;
            var sourcePath = ReadMandelaSourcePath(targetPath);
            if (MandelasObject.Property(targetPath) == null)
            {
                throw new InvalidOperationException(
                    "The selected base object has no saved override yet.");
            }
            ExecuteMandelaEdit(
                "Remove base-game object override",
                new[] { targetPath },
                () =>
                {
                    MandelasObject.Property(targetPath)?.Remove();
                    if (!string.IsNullOrWhiteSpace(sourcePath)
                        || selected.GetComponent<
                            TileEditorOwnedMandela>() != null)
                    {
                        var owned =
                            selected.GetComponent<
                                TileEditorOwnedMandela>()
                            ?? selected.AddComponent<
                                TileEditorOwnedMandela>();
                        owned.TargetPath = targetPath;
                        owned.SourcePath = sourcePath;
                        selected.SetActive(false);
                        UnityEngine.Object.Destroy(selected);
                        _selectedMandela = null;
                        _selectedMandelaPath = string.Empty;
                    }
                });
        }

        private void EditSelectedMandela(
            string name,
            Action<GameObject> mutation)
        {
            var target = RequireMandela();
            var targetPath = _selectedMandelaPath;
            var sourcePath = ReadMandelaSourcePath(targetPath);
            ExecuteMandelaEdit(
                name,
                new[] { targetPath },
                () =>
                {
                    mutation(target);
                    WriteMandela(
                        target,
                        targetPath,
                        sourcePath);
                });
        }

        private void ExecuteMandelaEdit(
            string name,
            IEnumerable<string> targetPaths,
            Action mutation)
        {
            RequireSession();
            RequireGraphEditOwnership();
            var ids = targetPaths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var edit = new EditRecord
            {
                Name = name,
                NodeIds = Array.Empty<string>(),
                SegmentIds = Array.Empty<string>(),
                SceneryIds = Array.Empty<string>(),
                MandelaIds = ids,
                BeforeNodes = new Dictionary<string, NodeModel>(),
                BeforeSegments = new Dictionary<string, SegmentModel>(),
                BeforeScenery = new Dictionary<string, SceneryModel>(),
                BeforeMandelas = CaptureMandelas(ids),
                BeforeDocument = (JObject)_document.DeepClone(),
                BeforeSelectedNode = _selectedNode?.id,
                BeforeSelectedSegment = _selectedSegment?.id,
                BeforeSelectedScenery = _selectedSceneryId,
                BeforeSelectedMandela = _selectedMandelaPath,
            };

            mutation();
            edit.AfterNodes = new Dictionary<string, NodeModel>();
            edit.AfterSegments = new Dictionary<string, SegmentModel>();
            edit.AfterScenery = new Dictionary<string, SceneryModel>();
            edit.AfterMandelas = CaptureMandelas(ids);
            edit.AfterDocument = (JObject)_document.DeepClone();
            edit.AfterSelectedNode = _selectedNode?.id;
            edit.AfterSelectedSegment = _selectedSegment?.id;
            edit.AfterSelectedScenery = _selectedSceneryId;
            edit.AfterSelectedMandela = _selectedMandelaPath;
            _undo.Push(edit);
            _redo.Clear();
            _dirty = true;
        }

        private void RestoreMandelaModels(EditRecord edit, bool after)
        {
            if (edit.MandelaIds == null
                || edit.MandelaIds.Length == 0)
            {
                return;
            }
            var models = after
                ? edit.AfterMandelas
                : edit.BeforeMandelas;
            foreach (var targetPath in edit.MandelaIds)
            {
                MandelaModel model = null;
                models?.TryGetValue(
                    targetPath,
                    out model);
                RestoreMandelaModel(targetPath, model);
            }
            var selectedPath = after
                ? edit.AfterSelectedMandela
                : edit.BeforeSelectedMandela;
            _selectedMandelaPath = selectedPath ?? string.Empty;
            _selectedMandela = FindSceneObjectByPath(
                _selectedMandelaPath);
            _mandelaClickedTransform =
                _selectedMandela?.transform;
        }

        private Dictionary<string, MandelaModel> CaptureMandelas(
            IEnumerable<string> targetPaths)
        {
            return targetPaths.ToDictionary(
                path => path,
                path =>
                {
                    var target = FindSceneObjectByPath(path);
                    if (target == null
                        && string.Equals(
                            path,
                            _selectedMandelaPath,
                            StringComparison.Ordinal))
                    {
                        target = _selectedMandela;
                    }
                    if (target == null)
                        return null;
                    if (target.GetComponent<
                            TileEditorOwnedMandela>() != null
                        && !target.activeSelf
                        && MandelasObject.Property(path) == null)
                    {
                        return null;
                    }
                    return new MandelaModel
                    {
                        TargetPath = path,
                        SourcePath =
                            ReadMandelaSourcePath(path),
                        LocalPosition =
                            target.transform.localPosition,
                        LocalRotation =
                            target.transform.localEulerAngles,
                        LocalScale =
                            target.transform.localScale,
                        Active = target.activeSelf,
                    };
                },
                StringComparer.Ordinal);
        }

        private void RestoreMandelaModel(
            string targetPath,
            MandelaModel model)
        {
            var current = FindSceneObjectByPath(targetPath);
            if (model == null)
            {
                if (current != null
                    && (current.GetComponent<
                            TileEditorOwnedMandela>() != null
                        || !string.IsNullOrWhiteSpace(
                            ReadMandelaSourcePath(targetPath))))
                {
                    current.SetActive(false);
                    UnityEngine.Object.Destroy(current);
                }
                return;
            }

            if (current == null
                && !string.IsNullOrWhiteSpace(model.SourcePath))
            {
                current = InstantiateMandela(
                    targetPath,
                    model.SourcePath);
            }
            if (current == null)
                return;
            current.transform.localPosition = model.LocalPosition;
            current.transform.localEulerAngles = model.LocalRotation;
            current.transform.localScale = model.LocalScale;
            current.SetActive(model.Active);
        }

        private GameObject MaterializeMandelaFromDocument(
            string targetPath)
        {
            var entry = MandelasObject[targetPath] as JObject;
            if (entry == null)
                return null;
            var sourcePath =
                entry["instantiateFrom"]?.Value<string>()
                ?? string.Empty;
            var target = FindSceneObjectByPath(targetPath);
            if (target == null
                && !string.IsNullOrWhiteSpace(sourcePath))
            {
                target = InstantiateMandela(
                    targetPath,
                    sourcePath);
            }
            if (target == null)
                return null;
            ApplyMandelaEntry(target, entry);
            return target;
        }

        private GameObject InstantiateMandela(
            string targetPath,
            string sourcePath)
        {
            var source = FindSceneObjectByPath(sourcePath);
            if (!IsSafeMandelaCloneSource(
                    source,
                    out var message))
            {
                throw new InvalidOperationException(message);
            }
            var separator = targetPath.LastIndexOf('/');
            if (separator <= 0 || separator >= targetPath.Length - 1)
            {
                throw new InvalidOperationException(
                    "Object target paths must include a scene root and child.");
            }
            var parentPath = targetPath.Substring(0, separator);
            var targetName = targetPath.Substring(separator + 1);
            var parent = FindSceneObjectByPath(parentPath);
            if (parent == null)
            {
                throw new InvalidOperationException(
                    "The target parent is not loaded: " + parentPath);
            }
            var clone = UnityEngine.Object.Instantiate(
                source,
                parent.transform);
            clone.name = targetName;
            clone.transform.localPosition = Vector3.zero;
            clone.transform.localEulerAngles = Vector3.zero;
            var owned = clone.GetComponent<TileEditorOwnedMandela>()
                        ?? clone.AddComponent<TileEditorOwnedMandela>();
            owned.TargetPath = targetPath;
            owned.SourcePath = sourcePath;
            return clone;
        }

        private static void ApplyMandelaEntry(
            GameObject target,
            JObject entry)
        {
            if (entry["localPosition"] is JObject position)
            {
                target.transform.localPosition =
                    ReadMandelaVector(
                        position,
                        target.transform.localPosition);
            }
            if (entry["localRotation"] is JObject rotation)
            {
                target.transform.localEulerAngles =
                    ReadMandelaVector(
                        rotation,
                        target.transform.localEulerAngles);
            }
            if (entry["localScale"] is JObject scale)
            {
                target.transform.localScale =
                    ReadMandelaVector(
                        scale,
                        target.transform.localScale);
            }
            if (entry["enabled"] != null)
                target.SetActive(entry["enabled"].Value<bool>());
        }

        private void WriteMandela(
            GameObject target,
            string targetPath,
            string sourcePath)
        {
            var existing = MandelasObject[targetPath] as JObject;
            var entry = existing == null
                ? new JObject()
                : (JObject)existing.DeepClone();
            if (string.IsNullOrWhiteSpace(sourcePath))
                entry.Property("instantiateFrom")?.Remove();
            else
                entry["instantiateFrom"] = sourcePath;
            entry["localPosition"] =
                Vector(target.transform.localPosition);
            entry["localRotation"] =
                Vector(target.transform.localEulerAngles);
            entry["localScale"] =
                Vector(target.transform.localScale);
            entry["enabled"] = target.activeSelf;
            MandelasObject[targetPath] = entry;
        }

        private JObject MandelasObject
        {
            get
            {
                var mandelas = _document?["mandelas"] as JObject;
                if (mandelas == null)
                {
                    mandelas = new JObject();
                    _document["mandelas"] = mandelas;
                }
                return mandelas;
            }
        }

        private string ReadMandelaSourcePath(string targetPath)
        {
            if (string.IsNullOrWhiteSpace(targetPath)
                || _document == null)
            {
                return string.Empty;
            }
            return (_document["mandelas"]?[targetPath]
                        ?["instantiateFrom"]?.Value<string>()
                    ?? string.Empty)
                .Trim();
        }

        private GameObject ResolveSelectedMandela()
        {
            if (string.IsNullOrWhiteSpace(_selectedMandelaPath))
            {
                _selectedMandela = null;
                return null;
            }
            if (_selectedMandela == null)
            {
                _selectedMandela =
                    FindSceneObjectByPath(_selectedMandelaPath);
            }
            return _selectedMandela;
        }

        private GameObject RequireMandela()
        {
            RequireSession();
            var selected = ResolveSelectedMandela();
            return selected != null
                ? selected
                : throw new InvalidOperationException(
                    "Click a base-game object in the world first.");
        }

        private void SelectMandelaTransform(
            Transform target,
            string knownPath = null)
        {
            if (IsUnsafeMandelaSelection(
                    target,
                    out var unsafeReason))
            {
                throw new InvalidOperationException(
                    unsafeReason);
            }
            var owned =
                target.GetComponentInParent<TileEditorOwnedMandela>();
            if (owned != null)
                target = owned.transform;
            var path = string.IsNullOrWhiteSpace(knownPath)
                ? GetTransformPath(target)
                : knownPath;
            if (path.IndexOf('/') < 0)
            {
                throw new InvalidOperationException(
                    "Object paths must include a scene root and child.");
            }
            _selectedMandela = target.gameObject;
            _selectedMandelaPath = path;
            _selectedNode = null;
            _selectedSegment = null;
            ClearSelectedScenery();
            ClearSelectedTelegraphPole();
        }

        private Transform FindMandelaRayTarget(Ray ray)
        {
            var count = Physics.RaycastNonAlloc(
                ray,
                _mandelaRaycastHits,
                5000f,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            Transform nearest = null;
            var nearestDistance = float.PositiveInfinity;
            for (var index = 0; index < count; index++)
            {
                var hit = _mandelaRaycastHits[index];
                if (hit.collider == null
                    || hit.distance >= nearestDistance
                    || !IsMandelaSelectable(hit.collider.transform)
                    || IsUnsafeMandelaSelection(
                        hit.collider.transform,
                        out _))
                {
                    continue;
                }
                nearest = hit.collider.transform;
                nearestDistance = hit.distance;
            }
            if (nearest != null)
                return nearest;

            var renderers = Resources
                .FindObjectsOfTypeAll<Renderer>();
            foreach (var renderer in renderers)
            {
                if (renderer == null
                    || !renderer.enabled
                    || !renderer.gameObject.activeInHierarchy
                    || !renderer.gameObject.scene.IsValid()
                    || !IsMandelaSelectable(renderer.transform)
                    || !renderer.bounds.IntersectRay(
                        ray,
                        out var distance)
                    || distance <= 0f
                    || distance >= nearestDistance)
                {
                    continue;
                }
                nearest = renderer.transform;
                nearestDistance = distance;
            }
            if (nearest != null)
                return nearest;

            // Thin signs and small props often have no collider, and their
            // renderer bounds can be only a pixel or two wide at normal edit
            // distances. Use a small screen-space halo only after both
            // physics and exact renderer-ray picking miss.
            return FindSmallMandelaScreenTarget(
                renderers,
                Input.mousePosition);
        }

        private static Transform FindSmallMandelaScreenTarget(
            IEnumerable<Renderer> renderers,
            Vector3 pointer)
        {
            var camera = Camera.main;
            if (camera == null || renderers == null)
                return null;

            Transform best = null;
            var bestScore = float.PositiveInfinity;
            var pointer2 = new Vector2(pointer.x, pointer.y);
            foreach (var renderer in renderers)
            {
                if (renderer == null
                    || !renderer.enabled
                    || !renderer.gameObject.activeInHierarchy
                    || !renderer.gameObject.scene.IsValid()
                    || !IsMandelaSelectable(renderer.transform)
                    || IsUnsafeMandelaSelection(
                        renderer.transform,
                        out _))
                {
                    continue;
                }

                var bounds = renderer.bounds;
                // This fallback is intentionally for signs and props, not
                // buildings or shared scenery groups.
                if (MaxBoundsDimension(bounds) > 20f
                    || !TryProjectMandelaBounds(
                        camera,
                        bounds,
                        out var screenBounds,
                        out var depth))
                {
                    continue;
                }

                const float pickHalo = 18f;
                screenBounds.xMin -= pickHalo;
                screenBounds.xMax += pickHalo;
                screenBounds.yMin -= pickHalo;
                screenBounds.yMax += pickHalo;
                if (!screenBounds.Contains(pointer2))
                    continue;

                var center = screenBounds.center;
                var centerDistance =
                    Vector2.Distance(pointer2, center);
                var visibleSize = Mathf.Max(
                    8f,
                    Mathf.Max(
                        screenBounds.width,
                        screenBounds.height));
                var score =
                    centerDistance / visibleSize
                    + depth * 0.00001f;
                if (score >= bestScore)
                    continue;
                best = renderer.transform;
                bestScore = score;
            }
            return best;
        }

        private static bool TryProjectMandelaBounds(
            Camera camera,
            Bounds bounds,
            out Rect screenBounds,
            out float depth)
        {
            screenBounds = default;
            depth = float.PositiveInfinity;
            var center = camera.WorldToScreenPoint(bounds.center);
            if (center.z <= 0f)
                return false;
            depth = center.z;

            var minimum = new Vector2(
                float.PositiveInfinity,
                float.PositiveInfinity);
            var maximum = new Vector2(
                float.NegativeInfinity,
                float.NegativeInfinity);
            var found = false;
            for (var x = -1; x <= 1; x += 2)
            {
                for (var y = -1; y <= 1; y += 2)
                {
                    for (var z = -1; z <= 1; z += 2)
                    {
                        var corner = bounds.center
                                     + Vector3.Scale(
                                         bounds.extents,
                                         new Vector3(x, y, z));
                        var screen =
                            camera.WorldToScreenPoint(corner);
                        if (screen.z <= 0f)
                            continue;
                        minimum.x = Mathf.Min(
                            minimum.x,
                            screen.x);
                        minimum.y = Mathf.Min(
                            minimum.y,
                            screen.y);
                        maximum.x = Mathf.Max(
                            maximum.x,
                            screen.x);
                        maximum.y = Mathf.Max(
                            maximum.y,
                            screen.y);
                        found = true;
                    }
                }
            }
            if (!found)
                return false;
            screenBounds = Rect.MinMaxRect(
                minimum.x,
                minimum.y,
                maximum.x,
                maximum.y);
            return true;
        }

        private static bool IsMandelaSelectable(Transform target)
        {
            if (target == null
                || target.parent == null
                || !target.gameObject.scene.IsValid()
                || target.GetComponentInParent<Terrain>() != null
                || target.GetComponentInParent<TrackNode>() != null
                || target.GetComponentInParent<TrackSegment>() != null
                || target.GetComponentInParent<
                    SceneryAssetInstance>() != null
                || target.GetComponentInParent<
                    TileEditorNodeOverlay>() != null
                || target.GetComponentInParent<
                    TileEditorSegmentOverlay>() != null
                || target.GetComponentInParent<
                    TileEditorSceneryOverlay>() != null)
            {
                return false;
            }
            if (IsMandelaContainer(target))
                return false;
            var rootName = target.root.name ?? string.Empty;
            if (rootName.StartsWith(
                    "TileEditor",
                    StringComparison.OrdinalIgnoreCase)
                || rootName.IndexOf(
                    "Camera",
                    StringComparison.OrdinalIgnoreCase) >= 0
                || rootName.IndexOf(
                    "Avatar",
                    StringComparison.OrdinalIgnoreCase) >= 0
                || rootName.IndexOf(
                    "UI",
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }
            return true;
        }

        private static Transform PromoteMandelaSelection(
            Transform clicked)
        {
            var owned =
                clicked.GetComponentInParent<TileEditorOwnedMandela>();
            if (owned != null)
                return owned.transform;
            var current = clicked;
            var promoted = clicked;
            TryGetHierarchyBounds(
                current,
                out var currentBounds,
                out var currentRendererCount);
            for (var depth = 0;
                 depth < 7
                 && current != null
                 && current.parent != null;
                 depth++)
            {
                promoted = current;
                if (current.GetComponent<LODGroup>() != null
                    || HasComponentNamed(current, "Animator")
                    || current.GetComponent<Rigidbody>() != null)
                {
                    break;
                }
                var parent = current.parent;
                if (WouldCrossMandelaAggregate(
                        current,
                        parent,
                        out _))
                    break;
                if (TryGetHierarchyBounds(
                        parent,
                        out var parentBounds,
                        out var parentRendererCount))
                {
                    currentBounds = parentBounds;
                    currentRendererCount =
                        parentRendererCount;
                }
                current = parent;
            }
            return promoted;
        }

        private static bool TryGetHierarchyBounds(
            Transform root,
            out Bounds bounds,
            out int rendererCount)
        {
            bounds = default;
            rendererCount = 0;
            if (root == null)
                return false;
            foreach (var renderer in root
                         .GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null)
                    continue;
                if (rendererCount == 0)
                    bounds = renderer.bounds;
                else
                    bounds.Encapsulate(renderer.bounds);
                rendererCount++;
            }
            return rendererCount > 0;
        }

        private static bool WouldCrossMandelaAggregate(
            Transform current,
            Transform parent,
            out string reason)
        {
            reason = string.Empty;
            if (parent == null
                || IsUnsafeMandelaSelection(
                    parent,
                    out reason))
            {
                return true;
            }

            if (!TryGetHierarchyBounds(
                    parent,
                    out var parentBounds,
                    out var parentRendererCount)
                || !TryGetHierarchyBounds(
                    current,
                    out var currentBounds,
                    out var currentRendererCount))
            {
                return false;
            }

            var currentSize = Mathf.Max(
                0.5f,
                MaxBoundsDimension(currentBounds));
            var parentSize = Mathf.Max(
                currentSize,
                MaxBoundsDimension(parentBounds));
            var sizeGrowth = parentSize / currentSize;
            var rendererGrowth =
                parentRendererCount
                / (float)Mathf.Max(1, currentRendererCount);
            var centerShift = Vector3.Distance(
                currentBounds.center,
                parentBounds.center);
            if ((sizeGrowth > 5f
                 && parentRendererCount > currentRendererCount + 1)
                || (rendererGrowth > 8f
                    && parentRendererCount >= 24
                    && sizeGrowth > 1.5f)
                || (centerShift > currentSize * 2f
                    && sizeGrowth > 2.5f))
            {
                reason =
                    "Stopped before the shared scene/container above "
                    + current.name
                    + ". Select and move each building or prop separately.";
                return true;
            }
            return false;
        }

        private static bool IsUnsafeMandelaSelection(
            Transform target,
            out string reason)
        {
            if (target == null || target.parent == null)
            {
                reason =
                    "Scene root objects cannot be edited as one object.";
                return true;
            }
            if (IsMandelaContainer(target))
            {
                reason =
                    "Shared world/map containers cannot be edited as one "
                    + "object. Click an individual building or prop.";
                return true;
            }
            if (TryGetHierarchyBounds(
                    target,
                    out var bounds,
                    out var rendererCount))
            {
                var maximum = MaxBoundsDimension(bounds);
                if (maximum > 2000f
                    || rendererCount > 500
                    || (maximum > 300f && rendererCount > 40)
                    || (maximum > 500f && rendererCount > 100))
                {
                    reason =
                        "This selection spans too much of the loaded scene "
                        + "to move safely. Click an individual asset.";
                    return true;
                }
            }
            reason = string.Empty;
            return false;
        }

        private static float MaxBoundsDimension(Bounds bounds)
        {
            return Mathf.Max(
                bounds.size.x,
                Mathf.Max(bounds.size.y, bounds.size.z));
        }

        private static bool IsMandelaContainer(Transform transform)
        {
            if (transform == null)
                return true;
            var name = (transform.name ?? string.Empty)
                .Trim()
                .ToLowerInvariant();
            return name == "world"
                   || name == "world scene"
                   || name == "world root"
                   || name == "game world"
                   || name == "map"
                   || name == "map scene"
                   || name == "map root"
                   || name == "scene"
                   || name == "scene root"
                   || name == "environment"
                   || name == "large scenery"
                   || name == "small scenery"
                   || name == "buildings"
                   || name == "structures"
                   || name == "props"
                   || name == "vegetation"
                   || name == "interactive scenery";
        }

        private static bool LooksLikeBaseGameSign(
            GameObject target,
            string path)
        {
            if (target == null
                || target.GetComponentInParent<SceneryAssetInstance>()
                != null)
            {
                return false;
            }
            var text = ((path ?? string.Empty) + "/" + target.name)
                .Replace('_', ' ')
                .Replace('-', ' ')
                .ToLowerInvariant();
            return text.Contains("sign")
                   || text.Contains("crossbuck")
                   || text.Contains("milepost")
                   || text.Contains("mile post")
                   || text.Contains("whistlepost")
                   || text.Contains("whistle post")
                   || text.Contains("speedboard")
                   || text.Contains("speed board")
                   || text.Contains("station board")
                   || text.Contains("road marker");
        }

        private static bool HasComponentNamed(
            Transform transform,
            string typeName)
        {
            if (transform == null)
                return false;
            foreach (var component in transform.GetComponents<Component>())
            {
                if (component != null
                    && string.Equals(
                        component.GetType().Name,
                        typeName,
                        StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private static GameObject ResolveMandelaCloneSource(
            GameObject selected,
            string sourcePath)
        {
            if (!string.IsNullOrWhiteSpace(sourcePath))
                return FindSceneObjectByPath(sourcePath);
            return selected;
        }

        private static bool IsSafeMandelaCloneSource(
            GameObject source,
            out string message)
        {
            if (source == null)
            {
                message = "The base-game source object is not loaded.";
                return false;
            }
            if (IsUnsafeMandelaSelection(
                    source.transform,
                    out message))
            {
                return false;
            }
            if (source.GetComponentInChildren<
                    SceneryAssetInstance>(true) != null)
            {
                message =
                    "This is loader scenery; clone it from the SCENERY tab.";
                return false;
            }
            foreach (var component in source
                         .GetComponentsInChildren<Component>(true))
            {
                if (component == null)
                    continue;
                var type = component.GetType();
                if (string.Equals(
                        type.Name,
                        "KeyValueObject",
                        StringComparison.Ordinal)
                    || string.Equals(
                        type.FullName,
                        "KeyValue.Runtime.KeyValueObject",
                        StringComparison.Ordinal))
                {
                    message =
                        "This object contains saved game state "
                        + "(KeyValueObject) and cannot be cloned safely.";
                    return false;
                }
            }
            message =
                "Safe to clone as a RailLoader mandela / FUSE scene clone.";
            return true;
        }

        private static string NextMandelaCloneName(
            Transform parent,
            string sourceName)
        {
            sourceName = string.IsNullOrWhiteSpace(sourceName)
                ? "Object"
                : sourceName;
            for (var number = 1; number < 10000; number++)
            {
                var candidate = sourceName
                                + " (TileEditor "
                                + number.ToString(
                                    CultureInfo.InvariantCulture)
                                + ")";
                if (parent.Find(candidate) == null)
                    return candidate;
            }
            return sourceName + " (TileEditor "
                   + Guid.NewGuid().ToString("N").Substring(0, 8)
                   + ")";
        }

        private static GameObject FindSceneObjectByPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;
            var pieces = path.Split(
                new[] { '/' },
                2,
                StringSplitOptions.RemoveEmptyEntries);
            if (pieces.Length == 0)
                return null;
            var roots = new List<GameObject>();
            for (var sceneIndex = 0;
                 sceneIndex < SceneManager.sceneCount;
                 sceneIndex++)
            {
                var scene = SceneManager.GetSceneAt(sceneIndex);
                if (scene.IsValid() && scene.isLoaded)
                    roots.AddRange(scene.GetRootGameObjects());
            }
            var root = roots.FirstOrDefault(candidate =>
                candidate != null
                && string.Equals(
                    candidate.name,
                    pieces[0],
                    StringComparison.Ordinal));
            if (root == null || pieces.Length == 1)
                return root;
            return root.transform.Find(pieces[1])?.gameObject;
        }

        private static string GetTransformPath(Transform transform)
        {
            if (transform == null)
                return string.Empty;
            var names = new Stack<string>();
            var current = transform;
            while (current != null)
            {
                names.Push(current.name);
                current = current.parent;
            }
            return string.Join("/", names.ToArray());
        }

        private static Vector3 ReadMandelaVector(
            JObject value,
            Vector3 fallback)
        {
            if (value == null)
                return fallback;
            return new Vector3(
                value["x"]?.Value<float>() ?? fallback.x,
                value["y"]?.Value<float>() ?? fallback.y,
                value["z"]?.Value<float>() ?? fallback.z);
        }

        private void ResetMandelaSession()
        {
            ClearSelectedMandela();
        }

        private void DisposeMandelaSession()
        {
            ClearSelectedMandela();
        }
    }

    internal sealed class TileEditorOwnedMandela : MonoBehaviour
    {
        internal string TargetPath = string.Empty;
        internal string SourcePath = string.Empty;
    }
}
