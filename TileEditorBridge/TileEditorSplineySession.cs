using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Helpers;
using Map.Runtime.MaskComponents;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Track;
using UnityEngine;
using TrestleComponent = AutoTrestle.AutoTrestle;

namespace Hrogers.TileEditorBridge
{
    internal sealed partial class TileEditorGraphSession
    {
        private enum SplineKind
        {
            Road,
            River,
            Trestle,
        }

        internal sealed class SplinePointInfo
        {
            internal string Id = string.Empty;
            internal string Kind = string.Empty;
            internal string Style = string.Empty;
            internal string FileName = string.Empty;
            internal int Index;
            internal int Count;
            internal Vector3 Position;
            internal Vector3 Rotation;
            internal float Width;
            internal bool HasWidth;
            internal string HeadStyle = string.Empty;
            internal string TailStyle = string.Empty;
        }

        private sealed class SplineSource
        {
            internal string Id;
            internal string FilePath;
            internal JObject Document;
            internal JObject Entry;
            internal string Handler;
            internal SplineKind Kind;
            internal RiverPath LivePath;
            internal TrestleComponent LiveTrestle;
            internal bool BuiltWithFuse;
        }

        private sealed class SplineEditRecord
        {
            internal SplineSource Source;
            internal JObject Before;
            internal JObject After;
            internal int BeforeIndex;
            internal int AfterIndex;
            internal bool BeforeExists = true;
            internal bool AfterExists = true;
        }

        private readonly Dictionary<string, SplineSource> _splineSources =
            new Dictionary<string, SplineSource>(StringComparer.Ordinal);
        private readonly Dictionary<string, JObject> _splineDocuments =
            new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _dirtySplineFiles =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _lastSavedSplinePaths =
            new List<string>();
        private readonly Dictionary<string, string> _splineBackups =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Stack<SplineEditRecord> _splineUndo =
            new Stack<SplineEditRecord>();
        private readonly Stack<SplineEditRecord> _splineRedo =
            new Stack<SplineEditRecord>();
        private readonly Dictionary<string, object> _splineBuilders =
            new Dictionary<string, object>(StringComparer.Ordinal);
        private SplineSource _selectedSplineSource;
        private int _selectedSplinePoint = -1;
        private bool _splineyMode;
        private bool _splineSourcesDiscovered;
        private bool _splineInitialAttachComplete;
        private float _nextSplineAttachRetryAt;
        private bool? _splineOverlayVisibility;
        private bool _splineTrackPickMode;

        internal bool SplineyDirty => _dirtySplineFiles.Count > 0;
        internal bool CanUndoSpliney => _splineUndo.Count > 0;
        internal bool CanRedoSpliney => _splineRedo.Count > 0;
        internal int SplineyCount => _splineSources.Count;
        internal int RoadSplineyCount => _splineSources.Values.Count(
            source => source.Kind == SplineKind.Road);
        internal int RiverSplineyCount => _splineSources.Values.Count(
            source => source.Kind == SplineKind.River);
        internal int TrestleSplineyCount => _splineSources.Values.Count(
            source => source.Kind == SplineKind.Trestle);
        internal bool SplineTrackPickMode => _splineTrackPickMode;
        internal IReadOnlyList<string> LastSavedSplinePaths =>
            _lastSavedSplinePaths;

        internal SplinePointInfo SelectedSplinePoint
        {
            get
            {
                var source = _selectedSplineSource;
                if (source == null
                    || _selectedSplinePoint < 0
                    || _selectedSplinePoint >= SplinePointCount(source))
                {
                    return null;
                }
                var trestle = source.LiveTrestle;
                return new SplinePointInfo
                {
                    Id = source.Id,
                    Kind = source.Kind.ToString(),
                    Style = source.Kind.ToString(),
                    FileName = Path.GetFileName(source.FilePath),
                    Index = _selectedSplinePoint,
                    Count = SplinePointCount(source),
                    Position = SplinePointPosition(source, _selectedSplinePoint),
                    Rotation = SplinePointRotation(source, _selectedSplinePoint),
                    Width = SplinePointWidth(source, _selectedSplinePoint),
                    HasWidth = source.Kind != SplineKind.Trestle,
                    HeadStyle = trestle == null
                        ? ReadEntryString(source.Entry, "headStyle", "Block")
                        : trestle.headStyle.ToString(),
                    TailStyle = trestle == null
                        ? ReadEntryString(source.Entry, "tailStyle", "Block")
                        : trestle.tailStyle.ToString(),
                };
            }
        }

        internal IReadOnlyList<string> GetSplineProfiles(string kind)
        {
            if (!_splineSourcesDiscovered && GraphOpen)
                DiscoverSplineySources();
            var wanted = ParseSplineKind(kind);
            if (wanted == SplineKind.Trestle)
                return Array.Empty<string>();
            return _splineSources.Values
                .Where(source => source.Kind == wanted)
                .Select(source => (string)source.Entry?["profile"])
                .Where(profile => !string.IsNullOrWhiteSpace(profile))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(profile => profile, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        internal void SetSplineyMode(bool active)
        {
            var changed = _splineyMode != active;
            if (!changed)
                return;
            _splineyMode = active;
            if (!active)
                _splineTrackPickMode = false;
            if (active && GraphOpen)
                RefreshSplineyMode();
            SetSplineyOverlaysVisible(
                active && _editModeActive && GraphOpen);
        }

        internal void SetSplineTrackPickMode(bool active)
        {
            _splineTrackPickMode = active && _splineyMode;
            if (_splineTrackPickMode)
                ClearSplineSelection();
            SetOverlaysVisible(
                _editModeActive
                && _geoWorkspaceActive
                && (!_splineyMode || _splineTrackPickMode));
        }

        internal void RefreshSplineySources()
        {
            if (SplineyDirty)
                throw new InvalidOperationException(
                    "Save or undo the current spline edits before refreshing.");
            DisposeSplinePointOverlays();
            _splineSources.Clear();
            _splineDocuments.Clear();
            _splineUndo.Clear();
            _splineRedo.Clear();
            _selectedSplineSource = null;
            _selectedSplinePoint = -1;
            _splineSourcesDiscovered = false;
            _splineInitialAttachComplete = false;
            RefreshSplineyMode();
        }

        internal void SelectSplinePoint(string splineId, int pointIndex)
        {
            if (!_splineyMode
                || !_editModeActive
                || string.IsNullOrWhiteSpace(splineId)
                || !_splineSources.TryGetValue(splineId, out var source)
                || pointIndex < 0
                || pointIndex >= SplinePointCount(source))
            {
                return;
            }
            var previous = _selectedSplineSource;
            _selectedSplineSource = source;
            _selectedSplinePoint = pointIndex;
            if (_splineTrackPickMode)
                SetSplineTrackPickMode(false);
            if (previous != null && previous != source)
                RefreshSplinePointOverlays(previous);
            RefreshSplinePointOverlays(source);
        }

        internal bool IsSelectedSplinePoint(string splineId, int pointIndex)
        {
            return _selectedSplineSource != null
                   && string.Equals(
                       _selectedSplineSource.Id,
                       splineId,
                       StringComparison.Ordinal)
                   && _selectedSplinePoint == pointIndex;
        }

        internal void SelectPreviousSplinePoint()
        {
            var source = RequireSplinePoint();
            _selectedSplinePoint = Mathf.Max(0, _selectedSplinePoint - 1);
            RefreshSplinePointOverlays(source);
        }

        internal void SelectNextSplinePoint()
        {
            var source = RequireSplinePoint();
            _selectedSplinePoint = Mathf.Min(
                SplinePointCount(source) - 1,
                _selectedSplinePoint + 1);
            RefreshSplinePointOverlays(source);
        }

        internal void ClearSplineSelection()
        {
            var previous = _selectedSplineSource;
            _selectedSplineSource = null;
            _selectedSplinePoint = -1;
            if (previous != null)
                RefreshSplinePointOverlays(previous);
        }

        internal void MoveSelectedSplinePoint(Vector3 offset)
        {
            ValidateVector(offset, "spline movement offset");
            EditSelectedSplinePoint(
                source =>
                {
                    if (source.LivePath != null)
                    {
                        var point = source.LivePath.points[_selectedSplinePoint];
                        point.position += SplineVectorToLocal(
                            source.LivePath.transform,
                            offset);
                        source.LivePath.points[_selectedSplinePoint] = point;
                    }
                    else
                    {
                        var point =
                            source.LiveTrestle.controlPoints[_selectedSplinePoint];
                        point.position += SplineVectorToLocal(
                            source.LiveTrestle.transform,
                            offset);
                    }
                });
        }

        internal void RotateSelectedSplinePoint(Vector3 offset)
        {
            ValidateVector(offset, "spline rotation offset");
            EditSelectedSplinePoint(
                source =>
                {
                    if (source.LivePath != null)
                    {
                        var point = source.LivePath.points[_selectedSplinePoint];
                        point.eulerAngles += offset;
                        source.LivePath.points[_selectedSplinePoint] = point;
                    }
                    else
                    {
                        var point =
                            source.LiveTrestle.controlPoints[_selectedSplinePoint];
                        point.rotation =
                            point.rotation * Quaternion.Euler(offset);
                    }
                });
        }

        internal void SetSelectedSplinePointWidth(float width)
        {
            var source = RequireSplinePoint();
            if (source.Kind == SplineKind.Trestle)
                throw new InvalidOperationException(
                    "Bridge and trestle points do not use width.");
            if (width < 0.5f || width > 500f)
                throw new InvalidOperationException(
                    "Spline width must be between 0.5 and 500 m.");
            EditSelectedSplinePoint(
                liveSource =>
                {
                    var point =
                        liveSource.LivePath.points[_selectedSplinePoint];
                    point.width = width;
                    liveSource.LivePath.points[_selectedSplinePoint] = point;
                });
        }

        internal void SetSelectedSplinePointTransform(
            Vector3 position,
            Vector3 rotation,
            float width)
        {
            ValidateVector(position, "spline point position");
            ValidateVector(rotation, "spline point rotation");
            var source = RequireSplinePoint();
            if (source.Kind != SplineKind.Trestle
                && (width < 0.5f || width > 500f))
            {
                throw new InvalidOperationException(
                    "Spline width must be between 0.5 and 500 m.");
            }
            EditSelectedSplinePoint(
                liveSource =>
                {
                    var owner = LiveSplineTransform(liveSource);
                    if (liveSource.LivePath != null)
                    {
                        var point =
                            liveSource.LivePath.points[_selectedSplinePoint];
                        point.position = SplinePointFromGame(owner, position);
                        point.eulerAngles =
                            SplineRotationFromGame(owner, rotation);
                        point.width = width;
                        liveSource.LivePath.points[_selectedSplinePoint] = point;
                    }
                    else
                    {
                        var point = liveSource.LiveTrestle
                            .controlPoints[_selectedSplinePoint];
                        point.position = SplinePointFromGame(owner, position);
                        point.rotation = Quaternion.Euler(
                            SplineRotationFromGame(owner, rotation));
                    }
                });
        }

        internal void SetTrestleEndStyles(
            string headStyle,
            string tailStyle)
        {
            var source = RequireSplinePoint();
            if (source.Kind != SplineKind.Trestle)
                throw new InvalidOperationException(
                    "End styles only apply to bridges and trestles.");
            if (!Enum.TryParse(headStyle, true,
                    out TrestleComponent.EndStyle head)
                || !Enum.TryParse(tailStyle, true,
                    out TrestleComponent.EndStyle tail))
            {
                throw new InvalidOperationException(
                    "Bridge end style must be Block or Bent.");
            }
            EditSelectedSplinePoint(
                liveSource =>
                {
                    liveSource.LiveTrestle.headStyle = head;
                    liveSource.LiveTrestle.tailStyle = tail;
                });
        }

        internal void InsertSplinePointAfter()
        {
            EditSelectedSplinePoint(
                source =>
                {
                    if (source.LivePath != null)
                    {
                        var path = source.LivePath;
                        var current = path.points[_selectedSplinePoint];
                        RiverPath.Point inserted;
                        if (_selectedSplinePoint + 1 < path.points.Count)
                        {
                            var next = path.points[_selectedSplinePoint + 1];
                            var curve = path.MakeCurve(_selectedSplinePoint + 1);
                            inserted = new RiverPath.Point(
                                path.transform.InverseTransformPoint(
                                    curve.GetPoint(0.5f)),
                                (
                                    Quaternion.Inverse(path.transform.rotation)
                                    * curve.GetRotation(0.5f)
                                ).eulerAngles,
                                Mathf.Lerp(
                                    current.width,
                                    next.width,
                                    0.5f));
                        }
                        else
                        {
                            inserted = new RiverPath.Point(
                                current.position
                                + Quaternion.Euler(current.eulerAngles)
                                * Vector3.forward * 20f,
                                current.eulerAngles,
                                current.width);
                        }
                        path.points.Insert(
                            _selectedSplinePoint + 1,
                            inserted);
                    }
                    else
                    {
                        var trestle = source.LiveTrestle;
                        var current =
                            trestle.controlPoints[_selectedSplinePoint];
                        TrestleComponent.ControlPoint inserted;
                        if (_selectedSplinePoint + 1
                            < trestle.controlPoints.Count)
                        {
                            var next = trestle.controlPoints[
                                _selectedSplinePoint + 1];
                            inserted = new TrestleComponent.ControlPoint
                            {
                                position = Vector3.Lerp(
                                    current.position,
                                    next.position,
                                    0.5f),
                                rotation = Quaternion.Slerp(
                                    current.rotation,
                                    next.rotation,
                                    0.5f),
                            };
                        }
                        else
                        {
                            inserted = new TrestleComponent.ControlPoint
                            {
                                position = current.position
                                           + current.rotation
                                           * Vector3.forward * 20f,
                                rotation = current.rotation,
                            };
                        }
                        trestle.controlPoints.Insert(
                            _selectedSplinePoint + 1,
                            inserted);
                    }
                    _selectedSplinePoint++;
                });
        }

        internal void DeleteSelectedSplinePoint()
        {
            var source = RequireSplinePoint();
            if (SplinePointCount(source) <= 2)
                throw new InvalidOperationException(
                    "A spline must keep at least two points.");
            EditSelectedSplinePoint(
                liveSource =>
                {
                    if (liveSource.LivePath != null)
                    {
                        liveSource.LivePath.points.RemoveAt(
                            _selectedSplinePoint);
                    }
                    else
                    {
                        liveSource.LiveTrestle.controlPoints.RemoveAt(
                            _selectedSplinePoint);
                    }
                    _selectedSplinePoint = Mathf.Clamp(
                        _selectedSplinePoint,
                        0,
                        SplinePointCount(liveSource) - 1);
                });
        }

        internal string CreateSplineyAtCamera(
            string requestedId,
            string kind,
            string profile,
            float length,
            float width,
            string headStyle,
            string tailStyle)
        {
            RequireGraphEditOwnership();
            RequireSession();
            if (CameraSelector.shared == null)
                throw new InvalidOperationException(
                    "Railroader's camera is not ready.");
            if (length < 2f || length > 2000f)
                throw new InvalidOperationException(
                    "New spline length must be between 2 and 2000 m.");

            var splineKind = ParseSplineKind(kind);
            if (splineKind != SplineKind.Trestle)
            {
                if (string.IsNullOrWhiteSpace(profile))
                    throw new InvalidOperationException(
                        "Choose a loaded road or river profile.");
                if (width < 0.5f || width > 500f)
                    throw new InvalidOperationException(
                        "Spline width must be between 0.5 and 500 m.");
            }
            if (splineKind == SplineKind.Trestle
                && (!Enum.TryParse(headStyle, true,
                        out TrestleComponent.EndStyle _)
                    || !Enum.TryParse(tailStyle, true,
                        out TrestleComponent.EndStyle _)))
            {
                throw new InvalidOperationException(
                    "Bridge end style must be Block or Bent.");
            }

            EnsureSplineDocument(_graphPath, _document);
            var id = UniqueSplineId(requestedId, splineKind);
            var start = WorldTransformer.WorldToGame(
                            CameraSelector.shared.CurrentCameraGroundPosition)
                        + Vector3.up * 0.05f;
            var yaw = Camera.main == null
                ? 0f
                : Camera.main.transform.eulerAngles.y;
            var rotation = new Vector3(0f, yaw, 0f);
            var end = start
                      + Quaternion.Euler(rotation)
                      * Vector3.forward * length;
            var entry = new JObject();
            if (splineKind == SplineKind.Trestle)
            {
                entry["handler"] = "StrangeCustoms.AutoTrestleBuilder";
                entry["points"] = new JArray(
                    SplinePointToken(start, rotation, null),
                    SplinePointToken(end, rotation, null));
                entry["headStyle"] = NormalizeEndStyle(headStyle);
                entry["tailStyle"] = NormalizeEndStyle(tailStyle);
            }
            else
            {
                entry["handler"] = "StrangeCustoms.FlowyThingBuilder";
                entry["profile"] = profile.Trim();
                entry["style"] = splineKind.ToString();
                entry["points"] = new JArray(
                    SplinePointToken(start, rotation, width),
                    SplinePointToken(end, rotation, width));
            }

            return AddNewSplineSource(id, splineKind, entry);
        }

        internal string CreateTrestleFromSelectedSegment(
            string requestedId,
            float belowRail,
            float pointSpacing,
            string headStyle,
            string tailStyle)
        {
            RequireGraphEditOwnership();
            RequireSession();
            var segment = _selectedSegment;
            if (segment == null)
                throw new InvalidOperationException(
                    "Click a yellow track segment first.");
            if (belowRail < 0f || belowRail > 10f)
                throw new InvalidOperationException(
                    "Below-rail offset must be between 0 and 10 m.");
            if (pointSpacing < 1f || pointSpacing > 50f)
                throw new InvalidOperationException(
                    "Bridge point spacing must be between 1 and 50 m.");
            if (!Enum.TryParse(headStyle, true,
                    out TrestleComponent.EndStyle _)
                || !Enum.TryParse(tailStyle, true,
                    out TrestleComponent.EndStyle _))
            {
                throw new InvalidOperationException(
                    "Bridge end style must be Block or Bent.");
            }

            var length = segment.GetLength();
            if (length < 2f)
                throw new InvalidOperationException(
                    "The selected track segment is too short for a bridge.");
            var sampleCount = Mathf.Clamp(
                Mathf.CeilToInt(length / pointSpacing) + 1,
                2,
                257);
            var points = new JArray();
            for (var index = 0; index < sampleCount; index++)
            {
                var distance = length * index / (sampleCount - 1f);
                segment.GetPositionRotationAtDistance(
                    distance,
                    TrackSegment.End.A,
                    PositionAccuracy.High,
                    out var position,
                    out var rotation);
                position.y -= belowRail;
                points.Add(SplinePointToken(
                    position,
                    rotation.eulerAngles,
                    null));
            }

            EnsureSplineDocument(_graphPath, _document);
            var id = UniqueSplineId(requestedId, SplineKind.Trestle);
            var entry = new JObject
            {
                ["handler"] = "StrangeCustoms.AutoTrestleBuilder",
                ["points"] = points,
                ["headStyle"] = NormalizeEndStyle(headStyle),
                ["tailStyle"] = NormalizeEndStyle(tailStyle),
            };
            var created = AddNewSplineSource(
                id,
                SplineKind.Trestle,
                entry);
            SetSplineTrackPickMode(false);
            return created;
        }

        internal void DeleteSelectedSpliney()
        {
            RequireGraphEditOwnership();
            var source = RequireSplinePoint();
            SyncSplineSourceDocument(source);
            var edit = new SplineEditRecord
            {
                Source = source,
                BeforeExists = true,
                AfterExists = false,
                Before = (JObject)source.Entry.DeepClone(),
                BeforeIndex = _selectedSplinePoint,
                AfterIndex = -1,
            };
            EnsureSplineysObject(source.Document).Remove(source.Id);
            DestroyLiveSpline(source);
            _splineSources.Remove(source.Id);
            _selectedSplineSource = null;
            _selectedSplinePoint = -1;
            _splineUndo.Push(edit);
            _splineRedo.Clear();
            _dirtySplineFiles.Add(source.FilePath);
        }

        private string AddNewSplineSource(
            string id,
            SplineKind kind,
            JObject entry)
        {
            var source = new SplineSource
            {
                Id = id,
                FilePath = _graphPath,
                Document = _document,
                Entry = entry,
                Handler = (string)entry["handler"],
                Kind = kind,
            };
            var splineys = EnsureSplineysObject(_document);
            splineys[id] = entry;
            try
            {
                BuildLiveSpline(source);
            }
            catch
            {
                splineys.Remove(id);
                throw;
            }
            _splineSources[id] = source;
            _selectedSplineSource = source;
            _selectedSplinePoint = 0;
            _splineUndo.Push(new SplineEditRecord
            {
                Source = source,
                BeforeExists = false,
                AfterExists = true,
                After = (JObject)entry.DeepClone(),
                BeforeIndex = -1,
                AfterIndex = 0,
            });
            _splineRedo.Clear();
            _dirtySplineFiles.Add(source.FilePath);
            RebuildSplineSourceOverlays(source, false);
            SetSplineyOverlaysVisible(_editModeActive && _splineyMode);
            return id;
        }

        internal void UndoSpliney()
        {
            if (_splineUndo.Count == 0)
                return;
            var edit = _splineUndo.Pop();
            RestoreSplineEdit(edit, false);
            _splineRedo.Push(edit);
        }

        internal void RedoSpliney()
        {
            if (_splineRedo.Count == 0)
                return;
            var edit = _splineRedo.Pop();
            RestoreSplineEdit(edit, true);
            _splineUndo.Push(edit);
        }

        internal void SaveSplineys()
        {
            _lastSavedSplinePaths.Clear();
            if (_dirtySplineFiles.Count == 0)
                return;
            foreach (var path in _dirtySplineFiles.ToArray())
            {
                var document = string.Equals(
                    path,
                    _graphPath,
                    StringComparison.OrdinalIgnoreCase)
                    ? _document
                    : _splineDocuments.TryGetValue(path, out var found)
                        ? found
                        : null;
                if (document == null)
                    continue;
                _splineDocuments[path] = document;
                if (!_splineBackups.ContainsKey(path) && File.Exists(path))
                {
                    var backup = path + ".tile-editor-backup-"
                                 + DateTime.Now.ToString(
                                     "yyyyMMdd-HHmmss",
                                     CultureInfo.InvariantCulture);
                    File.Copy(path, backup, false);
                    _splineBackups[path] = backup;
                }
                var temp = path + ".tile-editor.tmp";
                File.WriteAllText(temp, document.ToString(Formatting.Indented));
                if (File.Exists(path))
                {
                    try
                    {
                        File.Replace(temp, path, null);
                    }
                    catch
                    {
                        File.Delete(path);
                        File.Move(temp, path);
                    }
                }
                else
                {
                    File.Move(temp, path);
                }
                _lastSavedSplinePaths.Add(path);
            }
            _dirtySplineFiles.Clear();
        }

        private int PreserveDirtySplineyConflicts()
        {
            var preserved = 0;
            foreach (var path in _dirtySplineFiles.ToArray())
            {
                var document = string.Equals(
                    path,
                    _graphPath,
                    StringComparison.OrdinalIgnoreCase)
                    ? _document
                    : _splineDocuments.TryGetValue(
                        path,
                        out var found)
                        ? found
                        : null;
                if (document == null)
                    continue;
                var conflict = path
                               + ".game-conflict-"
                               + DateTime.Now.ToString(
                                   "yyyyMMdd-HHmmss",
                                   CultureInfo.InvariantCulture)
                               + ".json";
                File.WriteAllText(
                    conflict,
                    document.ToString(
                        Formatting.Indented));
                preserved++;
            }
            return preserved;
        }

        internal bool TryGetSplineOverlayData(
            string splineId,
            int pointIndex,
            out Vector3 localPosition,
            out Vector3 localRotation,
            out string kind,
            out int pointCount,
            out float? width)
        {
            localPosition = Vector3.zero;
            localRotation = Vector3.zero;
            kind = string.Empty;
            pointCount = 0;
            width = null;
            if (!_splineSources.TryGetValue(splineId, out var source)
                || pointIndex < 0
                || pointIndex >= SplinePointCount(source))
            {
                return false;
            }
            kind = source.Kind.ToString();
            pointCount = SplinePointCount(source);
            if (source.LivePath != null)
            {
                var point = source.LivePath.points[pointIndex];
                localPosition = point.position;
                localRotation = point.eulerAngles;
                width = point.width;
                return true;
            }
            if (source.LiveTrestle != null)
            {
                var point = source.LiveTrestle.controlPoints[pointIndex];
                localPosition = point.position;
                localRotation = point.rotation.eulerAngles;
                return true;
            }
            return false;
        }

        private void EditSelectedSplinePoint(Action<SplineSource> mutation)
        {
            RequireGraphEditOwnership();
            var source = RequireSplinePoint();
            SyncSplineSourceDocument(source);
            var edit = new SplineEditRecord
            {
                Source = source,
                Before = (JObject)source.Entry.DeepClone(),
                BeforeIndex = _selectedSplinePoint,
            };
            mutation(source);
            UpdateSplineSourceFromLive(source);
            RebuildLiveSpline(source);
            edit.After = (JObject)source.Entry.DeepClone();
            edit.AfterIndex = _selectedSplinePoint;
            _splineUndo.Push(edit);
            _splineRedo.Clear();
            _dirtySplineFiles.Add(source.FilePath);
        }

        private void RestoreSplineEdit(SplineEditRecord edit, bool after)
        {
            var exists = after ? edit.AfterExists : edit.BeforeExists;
            var entry = after ? edit.After : edit.Before;
            var pointIndex = after ? edit.AfterIndex : edit.BeforeIndex;
            var source = edit.Source;
            SyncSplineSourceDocument(source);
            var splineys = EnsureSplineysObject(source.Document);

            if (!exists)
            {
                splineys.Remove(source.Id);
                DestroyLiveSpline(source);
                _splineSources.Remove(source.Id);
                _selectedSplineSource = null;
                _selectedSplinePoint = -1;
                _dirtySplineFiles.Add(source.FilePath);
                return;
            }

            source.Entry = (JObject)entry.DeepClone();
            source.Handler = (string)source.Entry["handler"] ?? source.Handler;
            source.Kind = KindFromEntry(source.Entry);
            splineys[source.Id] = source.Entry;
            if (LiveSplineTransform(source) == null)
                BuildLiveSpline(source);
            else
                ApplySplineSourceToLive(source);
            _splineSources[source.Id] = source;
            _selectedSplineSource = source;
            _selectedSplinePoint = Mathf.Clamp(
                pointIndex,
                0,
                Mathf.Max(0, SplinePointCount(source) - 1));
            RebuildLiveSpline(source);
            _dirtySplineFiles.Add(source.FilePath);
        }

        private SplineSource RequireSplinePoint()
        {
            var source = _selectedSplineSource;
            if (source == null
                || LiveSplineTransform(source) == null
                || _selectedSplinePoint < 0
                || _selectedSplinePoint >= SplinePointCount(source))
            {
                throw new InvalidOperationException(
                    "Click a road, river, bridge, or trestle control point first.");
            }
            return source;
        }

        private void ResetSplineySources()
        {
            DisposeSplinePointOverlays();
            _splineSources.Clear();
            _splineDocuments.Clear();
            _dirtySplineFiles.Clear();
            _splineBackups.Clear();
            _splineUndo.Clear();
            _splineRedo.Clear();
            _selectedSplineSource = null;
            _selectedSplinePoint = -1;
            _splineSourcesDiscovered = false;
            _splineInitialAttachComplete = false;
            if (_splineyMode)
                RefreshSplineyMode();
        }

        private void RefreshSplineyMode()
        {
            if (!GraphOpen)
                return;
            if (!_splineSourcesDiscovered)
                DiscoverSplineySources();
            var needsRetry = _splineSources.Values.Any(
                source => LiveSplineTransform(source) == null);
            if (!_splineInitialAttachComplete
                || (needsRetry
                    && Time.unscaledTime >= _nextSplineAttachRetryAt))
            {
                AttachLiveSplinePaths();
                RebuildSplineyOverlays(false);
                _splineInitialAttachComplete = true;
                // Missing mixinto objects may appear later during map load.
                // Retrying every frame or every one-second panel heartbeat
                // causes visible stutter on maps with hundreds of trestles.
                _nextSplineAttachRetryAt = Time.unscaledTime + 5f;
            }
            SetSplineyOverlaysVisible(_editModeActive && _splineyMode);
        }

        private void DiscoverSplineySources()
        {
            _splineSourcesDiscovered = true;
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(_graphPath))
                paths.Add(Path.GetFullPath(_graphPath));
            var modDirectory = FindOwningModDirectory();
            if (!string.IsNullOrWhiteSpace(modDirectory))
            {
                var definitionPath = Path.Combine(
                    modDirectory,
                    "Definition.json");
                if (File.Exists(definitionPath))
                {
                    try
                    {
                        var definition = JObject.Parse(
                            File.ReadAllText(definitionPath));
                        var mixintos =
                            definition["mixintos"]?["game-graph"] as JArray;
                        if (mixintos != null)
                        {
                            foreach (var token in mixintos)
                            {
                                var relative =
                                    ParseFileMixinto((string)token);
                                if (!string.IsNullOrWhiteSpace(relative))
                                {
                                    paths.Add(Path.GetFullPath(Path.Combine(
                                        modDirectory,
                                        relative)));
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.Warning(
                            "Could not read spline mixintos: " + ex.Message);
                    }
                }
            }

            foreach (var path in paths.Where(File.Exists))
            {
                JObject document;
                if (string.Equals(
                        path,
                        _graphPath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    document = _document;
                }
                else
                {
                    try
                    {
                        document = JObject.Parse(File.ReadAllText(path));
                    }
                    catch
                    {
                        continue;
                    }
                }
                EnsureSplineDocument(path, document);
                if (!(document["splineys"] is JObject splineys))
                    continue;
                foreach (var property in splineys.Properties())
                {
                    if (!(property.Value is JObject entry)
                        || !TryKindFromEntry(entry, out var kind))
                    {
                        continue;
                    }
                    _splineSources[property.Name] = new SplineSource
                    {
                        Id = property.Name,
                        FilePath = path,
                        Document = document,
                        Entry = entry,
                        Handler = (string)entry["handler"] ?? string.Empty,
                        Kind = kind,
                    };
                }
            }
        }

        private string FindOwningModDirectory()
        {
            if (string.IsNullOrWhiteSpace(_graphPath))
                return null;
            var modsDirectory = Path.GetFullPath(
                    Path.Combine(_gameRoot, "Mods"))
                .TrimEnd(Path.DirectorySeparatorChar);
            var directory = new DirectoryInfo(
                Path.GetDirectoryName(_graphPath) ?? string.Empty);
            while (directory != null)
            {
                var current = directory.FullName.TrimEnd(
                    Path.DirectorySeparatorChar);
                if (!current.StartsWith(
                        modsDirectory,
                        StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
                if (File.Exists(Path.Combine(current, "Definition.json")))
                    return current;
                if (string.Equals(
                        current,
                        modsDirectory,
                        StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
                directory = directory.Parent;
            }
            return null;
        }

        private void AttachLiveSplinePaths()
        {
            var livePaths = UnityEngine.Object.FindObjectsOfType<RiverPath>()
                .Where(path => path != null && path.gameObject.scene.IsValid())
                .GroupBy(path => path.gameObject.name)
                .ToDictionary(
                    group => group.Key,
                    group => group.FirstOrDefault(
                                 path => path.gameObject.activeInHierarchy)
                             ?? group.First(),
                    StringComparer.Ordinal);
            var liveTrestles =
                UnityEngine.Object.FindObjectsOfType<TrestleComponent>()
                    .Where(trestle => trestle != null
                                      && trestle.gameObject.scene.IsValid())
                    .GroupBy(trestle => trestle.gameObject.name)
                    .ToDictionary(
                        group => group.Key,
                        group => group.FirstOrDefault(
                                     trestle =>
                                         trestle.gameObject.activeInHierarchy)
                                 ?? group.First(),
                        StringComparer.Ordinal);
            foreach (var source in _splineSources.Values)
            {
                if (source.Kind == SplineKind.Trestle)
                {
                    if (liveTrestles.TryGetValue(
                            source.Id,
                            out var trestle))
                    {
                        source.LiveTrestle = trestle;
                        source.BuiltWithFuse =
                            IsFuseSpliney(trestle.gameObject);
                    }
                }
                else if (livePaths.TryGetValue(source.Id, out var path))
                {
                    source.LivePath = path;
                }
            }
        }

        private void RebuildSplineyOverlays(bool rebuildExisting)
        {
            foreach (var source in _splineSources.Values)
                RebuildSplineSourceOverlays(source, rebuildExisting);
        }

        private void RebuildSplineSourceOverlays(
            SplineSource source,
            bool rebuildExisting)
        {
            var owner = LiveSplineTransform(source);
            if (owner == null)
                return;
            var existing = owner
                .GetComponentsInChildren<TileEditorSplinePointOverlay>(true)
                .Where(overlay => string.Equals(
                    overlay.SplineId,
                    source.Id,
                    StringComparison.Ordinal))
                .GroupBy(overlay => overlay.PointIndex)
                .ToDictionary(group => group.Key, group => group.First());
            for (var index = 0;
                 index < SplinePointCount(source);
                 index++)
            {
                if (!existing.TryGetValue(index, out var overlay))
                {
                    var go = new GameObject(
                        "TileEditorSplinePoint-" + index);
                    go.transform.SetParent(owner, false);
                    overlay =
                        go.AddComponent<TileEditorSplinePointOverlay>();
                    overlay.Initialize(this, source.Id, index);
                }
                else if (rebuildExisting)
                {
                    overlay.Initialize(this, source.Id, index);
                }
                else
                {
                    overlay.Refresh();
                }
            }
            foreach (var pair in existing)
            {
                if (pair.Key >= SplinePointCount(source)
                    && pair.Value != null)
                {
                    UnityEngine.Object.Destroy(pair.Value.gameObject);
                }
            }
        }

        private void RefreshSplinePointOverlays(SplineSource source)
        {
            var owner = LiveSplineTransform(source);
            if (owner == null)
                return;
            foreach (var overlay in owner
                         .GetComponentsInChildren<
                             TileEditorSplinePointOverlay>(true))
            {
                if (string.Equals(
                        overlay.SplineId,
                        source.Id,
                        StringComparison.Ordinal))
                {
                    overlay.Refresh();
                }
            }
        }

        private void RebuildLiveSpline(SplineSource source)
        {
            if (source?.LivePath != null)
            {
                var builder = source.LivePath.GetComponent<RiverBuilder>();
                if (builder != null)
                {
                    builder.BuildSpline();
                    builder.RequestUpdateCullingPosition();
                }
            }
            else if (source?.LiveTrestle != null
                     && source.LiveTrestle.controlPoints.Count >= 2)
            {
                source.LiveTrestle.Generate();
                source.LiveTrestle.RequestUpdateCullingPosition();
            }
            RebuildSplineSourceOverlays(source, false);
            SetSplineyOverlaysVisible(_editModeActive && _splineyMode);
        }

        private void BuildLiveSpline(SplineSource source)
        {
            var builderType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(
                    source.Handler,
                    false,
                    false))
                .FirstOrDefault(type => type != null);
            if (builderType == null)
            {
                if (source.Kind == SplineKind.Trestle
                    && TryBuildLiveTrestleWithFuse(source))
                {
                    return;
                }
                throw new InvalidOperationException(
                    source.Kind == SplineKind.Trestle
                        ? "No compatible trestle builder is loaded. "
                          + "Install FUSE or Strange Customs."
                        : "The Strange Customs spline builder is not loaded: "
                          + source.Handler);
            }
            if (!_splineBuilders.TryGetValue(
                    source.Handler,
                    out var builder))
            {
                builder = Activator.CreateInstance(builderType, true);
                _splineBuilders[source.Handler] = builder;
            }
            var method = builderType.GetMethod(
                "BuildSpliney",
                BindingFlags.Instance
                | BindingFlags.Public
                | BindingFlags.NonPublic);
            if (method == null)
                throw new InvalidOperationException(
                    source.Handler + " does not expose BuildSpliney.");
            var parent = source.Kind == SplineKind.Trestle
                ? FindTrestleParent()
                : _graph.transform;
            GameObject result;
            try
            {
                result = method.Invoke(
                    builder,
                    new object[]
                    {
                        source.Id,
                        parent,
                        source.Entry,
                    }) as GameObject;
            }
            catch (TargetInvocationException ex)
            {
                throw new InvalidOperationException(
                    ex.InnerException?.Message ?? ex.Message,
                    ex.InnerException ?? ex);
            }
            if (result == null)
                throw new InvalidOperationException(
                    source.Handler + " did not create a live spline.");
            source.LivePath = result.GetComponent<RiverPath>();
            source.LiveTrestle = result.GetComponent<TrestleComponent>();
            source.BuiltWithFuse = false;
        }

        private bool TryBuildLiveTrestleWithFuse(SplineSource source)
        {
            var apiType = FindLoadedType("FUSE.Runtime.API.SplineyAPI");
            if (apiType == null)
                return false;
            var definitionType = apiType.Assembly.GetType(
                "FUSE.Authoring.Data.FuseSpliney",
                false,
                false);
            var pointType = apiType.Assembly.GetType(
                "FUSE.Authoring.Data.FuseSplineyPoint",
                false,
                false);
            if (definitionType == null || pointType == null)
            {
                throw new InvalidOperationException(
                    "FUSE is loaded, but its spline definition API "
                    + "is unavailable.");
            }
            if (!(source.Entry["points"] is JArray pointTokens)
                || pointTokens.Count < 2)
            {
                throw new InvalidOperationException(
                    "A trestle requires at least two spline points.");
            }

            var definition = Activator.CreateInstance(definitionType);
            SetPublicProperty(definition, "Type", "trestle");
            SetPublicProperty(
                definition,
                "Profile",
                ReadEntryString(source.Entry, "profile", null));
            SetPublicProperty(
                definition,
                "HeadStyle",
                ReadEntryString(source.Entry, "headStyle", "Block"));
            SetPublicProperty(
                definition,
                "TailStyle",
                ReadEntryString(source.Entry, "tailStyle", "Block"));

            var points = Array.CreateInstance(
                pointType,
                pointTokens.Count);
            for (var index = 0; index < pointTokens.Count; index++)
            {
                var pointToken = pointTokens[index] as JObject;
                if (pointToken == null)
                {
                    throw new InvalidOperationException(
                        "Trestle point " + (index + 1)
                        + " is not a valid point object.");
                }
                var point = Activator.CreateInstance(pointType);
                SetPublicProperty(
                    point,
                    "Position",
                    ReadVector(pointToken["position"]));
                SetPublicProperty(
                    point,
                    "Rotation",
                    ReadVector(pointToken["rotation"]));
                points.SetValue(point, index);
            }
            SetPublicProperty(definition, "Points", points);

            var addMethod = apiType.GetMethod(
                "AddSpliney",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(string), definitionType },
                null);
            if (addMethod == null)
            {
                throw new InvalidOperationException(
                    "FUSE.SplineyAPI.AddSpliney was not found.");
            }

            GameObject result;
            try
            {
                result = addMethod.Invoke(
                    null,
                    new[] { (object)source.Id, definition })
                    as GameObject;
            }
            catch (TargetInvocationException ex)
            {
                throw new InvalidOperationException(
                    "FUSE could not build trestle '"
                    + source.Id + "': "
                    + (ex.InnerException?.Message ?? ex.Message),
                    ex.InnerException ?? ex);
            }
            if (result == null)
            {
                throw new InvalidOperationException(
                    "FUSE did not create a live trestle.");
            }

            source.LivePath = null;
            source.LiveTrestle =
                result.GetComponent<TrestleComponent>();
            source.BuiltWithFuse = true;
            if (source.LiveTrestle == null)
            {
                throw new InvalidOperationException(
                    "FUSE created a spline without an AutoTrestle component.");
            }
            if (source.LiveTrestle.controlPoints.Count >= 2)
            {
                source.LiveTrestle.Generate();
                source.LiveTrestle.RequestUpdateCullingPosition();
            }
            _logger?.Log(
                "Built trestle '" + source.Id
                + "' with the FUSE spline API.");
            return true;
        }

        private static Type FindLoadedType(string fullName)
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(
                    fullName,
                    false,
                    false))
                .FirstOrDefault(type => type != null);
        }

        private static void SetPublicProperty(
            object target,
            string name,
            object value)
        {
            var property = target?.GetType().GetProperty(
                name,
                BindingFlags.Instance | BindingFlags.Public);
            if (property == null || !property.CanWrite)
            {
                throw new InvalidOperationException(
                    target?.GetType().FullName
                    + "." + name + " is not writable.");
            }
            property.SetValue(target, value, null);
        }

        private static bool IsFuseSpliney(GameObject owner)
        {
            var markerType = FindLoadedType(
                "FUSE.Runtime.API.FuseSplineyMarker");
            return owner != null
                   && markerType != null
                   && owner.GetComponent(markerType) != null;
        }

        private bool TryRemoveFuseSpliney(string id)
        {
            var apiType = FindLoadedType("FUSE.Runtime.API.SplineyAPI");
            var removeMethod = apiType?.GetMethod(
                "TryRemoveSpliney",
                BindingFlags.Public | BindingFlags.Static);
            if (removeMethod == null)
                return false;
            try
            {
                return removeMethod.Invoke(
                           null,
                           new object[] { id })
                       is bool removed
                       && removed;
            }
            catch (TargetInvocationException ex)
            {
                _logger?.Warning(
                    "FUSE could not unregister trestle '" + id + "': "
                    + (ex.InnerException?.Message ?? ex.Message));
                return false;
            }
        }

        private Transform FindTrestleParent()
        {
            var existing = UnityEngine.Object
                .FindObjectOfType<TrestleComponent>();
            return existing != null && existing.transform.parent != null
                ? existing.transform.parent
                : _graph.transform;
        }

        private void DestroyLiveSpline(SplineSource source)
        {
            var owner = LiveSplineTransform(source);
            var removedByFuse = source.BuiltWithFuse
                                && TryRemoveFuseSpliney(source.Id);
            if (!removedByFuse && owner != null)
            {
                owner.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(owner.gameObject);
            }
            source.LivePath = null;
            source.LiveTrestle = null;
            source.BuiltWithFuse = false;
        }

        private void UpdateSplineSourceFromLive(SplineSource source)
        {
            SyncSplineSourceDocument(source);
            var existing = source.Entry["points"] as JArray;
            var points = new JArray();
            var owner = LiveSplineTransform(source);
            for (var index = 0;
                 index < SplinePointCount(source);
                 index++)
            {
                var token = existing != null
                            && index < existing.Count
                            && existing[index] is JObject oldPoint
                    ? (JObject)oldPoint.DeepClone()
                    : new JObject();
                token["position"] = Vector(
                    SplinePointToGame(
                        owner,
                        SplinePointLocalPosition(source, index)));
                token["rotation"] = Vector(
                    SplineRotationToGame(
                        owner,
                        SplinePointLocalRotation(source, index)));
                if (source.Kind == SplineKind.Trestle)
                    token.Remove("width");
                else
                    token["width"] = SplinePointWidth(source, index);
                points.Add(token);
            }
            source.Entry["points"] = points;
            if (source.Kind == SplineKind.Trestle)
            {
                SetEntryValue(
                    source.Entry,
                    "headStyle",
                    source.LiveTrestle.headStyle.ToString());
                SetEntryValue(
                    source.Entry,
                    "tailStyle",
                    source.LiveTrestle.tailStyle.ToString());
            }
            EnsureSplineysObject(source.Document)[source.Id] = source.Entry;
        }

        private void ApplySplineSourceToLive(SplineSource source)
        {
            if (!(source.Entry["points"] is JArray points))
                return;
            var owner = LiveSplineTransform(source);
            if (owner == null)
                return;
            if (source.LivePath != null)
            {
                var livePoints = new List<RiverPath.Point>();
                foreach (var token in points.OfType<JObject>())
                {
                    var position = ReadVector(token["position"]);
                    var rotation = ReadVector(token["rotation"]);
                    var width = (float?)token["width"] ?? 10f;
                    livePoints.Add(new RiverPath.Point(
                        SplinePointFromGame(owner, position),
                        SplineRotationFromGame(owner, rotation),
                        width));
                }
                if (livePoints.Count >= 2)
                    source.LivePath.points = livePoints;
                return;
            }

            if (source.LiveTrestle != null)
            {
                var controls = new List<TrestleComponent.ControlPoint>();
                foreach (var token in points.OfType<JObject>())
                {
                    controls.Add(new TrestleComponent.ControlPoint
                    {
                        position = SplinePointFromGame(
                            owner,
                            ReadVector(token["position"])),
                        rotation = Quaternion.Euler(
                            SplineRotationFromGame(
                                owner,
                                ReadVector(token["rotation"]))),
                    });
                }
                if (controls.Count >= 2)
                    source.LiveTrestle.controlPoints = controls;
                if (Enum.TryParse(
                        ReadEntryString(
                            source.Entry,
                            "headStyle",
                            "Block"),
                        true,
                        out TrestleComponent.EndStyle head))
                {
                    source.LiveTrestle.headStyle = head;
                }
                if (Enum.TryParse(
                        ReadEntryString(
                            source.Entry,
                            "tailStyle",
                            "Block"),
                        true,
                        out TrestleComponent.EndStyle tail))
                {
                    source.LiveTrestle.tailStyle = tail;
                }
            }
        }

        internal void SetSplineyOverlaysVisible(bool visible)
        {
            if (_splineOverlayVisibility.HasValue
                && _splineOverlayVisibility.Value == visible)
            {
                return;
            }
            _splineOverlayVisibility = visible;
            foreach (var overlay in UnityEngine.Object
                         .FindObjectsOfType<TileEditorSplinePointOverlay>())
            {
                if (overlay != null)
                    overlay.SetOverlayVisible(visible);
            }
        }

        private void DisposeSplinePointOverlays()
        {
            foreach (var overlay in Resources
                         .FindObjectsOfTypeAll<
                             TileEditorSplinePointOverlay>())
            {
                if (overlay != null)
                    UnityEngine.Object.Destroy(overlay.gameObject);
            }
        }

        private void DisposeSplineySession()
        {
            SetSplineyOverlaysVisible(false);
            DisposeSplinePointOverlays();
            _splineSources.Clear();
            _splineDocuments.Clear();
            _splineSourcesDiscovered = false;
            _splineInitialAttachComplete = false;
            _splineOverlayVisibility = null;
        }

        private void EnsureSplineDocument(string path, JObject document)
        {
            if (string.IsNullOrWhiteSpace(path) || document == null)
                return;
            _splineDocuments[Path.GetFullPath(path)] = document;
        }

        private void SyncSplineSourceDocument(SplineSource source)
        {
            if (source == null
                || !string.Equals(
                    source.FilePath,
                    _graphPath,
                    StringComparison.OrdinalIgnoreCase)
                || ReferenceEquals(source.Document, _document))
            {
                return;
            }
            source.Document = _document;
            var current = _document["splineys"]?[source.Id] as JObject;
            if (current != null)
                source.Entry = current;
            _splineDocuments[source.FilePath] = _document;
        }

        private string UniqueSplineId(
            string requestedId,
            SplineKind kind)
        {
            var root = string.IsNullOrWhiteSpace(requestedId)
                ? "TileEditor_" + kind
                : requestedId.Trim();
            var id = root;
            var suffix = 1;
            while (_splineSources.ContainsKey(id)
                   || EnsureSplineysObject(_document)[id] != null
                   || GameObject.Find(id) != null)
            {
                id = root + "_" + suffix.ToString(
                    "000",
                    CultureInfo.InvariantCulture);
                suffix++;
            }
            return id;
        }

        private static JObject EnsureSplineysObject(JObject document)
        {
            if (!(document["splineys"] is JObject splineys))
            {
                splineys = new JObject();
                document["splineys"] = splineys;
            }
            return splineys;
        }

        private static int SplinePointCount(SplineSource source)
        {
            if (source?.LivePath != null)
                return source.LivePath.points?.Count ?? 0;
            if (source?.LiveTrestle != null)
                return source.LiveTrestle.controlPoints?.Count ?? 0;
            return 0;
        }

        private static Transform LiveSplineTransform(SplineSource source)
        {
            if (source?.LivePath != null)
                return source.LivePath.transform;
            return source?.LiveTrestle == null
                ? null
                : source.LiveTrestle.transform;
        }

        private static Vector3 SplinePointLocalPosition(
            SplineSource source,
            int index)
        {
            return source.LivePath != null
                ? source.LivePath.points[index].position
                : source.LiveTrestle.controlPoints[index].position;
        }

        private static Vector3 SplinePointLocalRotation(
            SplineSource source,
            int index)
        {
            return source.LivePath != null
                ? source.LivePath.points[index].eulerAngles
                : source.LiveTrestle.controlPoints[index].rotation.eulerAngles;
        }

        private static Vector3 SplinePointPosition(
            SplineSource source,
            int index)
        {
            return SplinePointToGame(
                LiveSplineTransform(source),
                SplinePointLocalPosition(source, index));
        }

        private static Vector3 SplinePointRotation(
            SplineSource source,
            int index)
        {
            return SplineRotationToGame(
                LiveSplineTransform(source),
                SplinePointLocalRotation(source, index));
        }

        private static float SplinePointWidth(
            SplineSource source,
            int index)
        {
            return source.LivePath == null
                ? 0f
                : source.LivePath.points[index].width;
        }

        private static Vector3 SplinePointToGame(
            Transform owner,
            Vector3 localPoint)
        {
            var world = owner.TransformPoint(localPoint);
            return owner.parent == null
                ? world
                : owner.parent.InverseTransformPoint(world);
        }

        private static Vector3 SplinePointFromGame(
            Transform owner,
            Vector3 gamePoint)
        {
            var world = owner.parent == null
                ? gamePoint
                : owner.parent.TransformPoint(gamePoint);
            return owner.InverseTransformPoint(world);
        }

        private static Vector3 SplineRotationToGame(
            Transform owner,
            Vector3 localRotation)
        {
            var world = owner.rotation * Quaternion.Euler(localRotation);
            var parentRotation = owner.parent == null
                ? Quaternion.identity
                : owner.parent.rotation;
            return (Quaternion.Inverse(parentRotation) * world).eulerAngles;
        }

        private static Vector3 SplineRotationFromGame(
            Transform owner,
            Vector3 gameRotation)
        {
            var parentRotation = owner.parent == null
                ? Quaternion.identity
                : owner.parent.rotation;
            var world = parentRotation * Quaternion.Euler(gameRotation);
            return (Quaternion.Inverse(owner.rotation) * world).eulerAngles;
        }

        private static Vector3 SplineVectorToLocal(
            Transform owner,
            Vector3 gameVector)
        {
            var world = owner.parent == null
                ? gameVector
                : owner.parent.TransformVector(gameVector);
            return owner.InverseTransformVector(world);
        }

        private static JObject SplinePointToken(
            Vector3 position,
            Vector3 rotation,
            float? width)
        {
            var result = new JObject
            {
                ["position"] = Vector(position),
                ["rotation"] = Vector(rotation),
            };
            if (width.HasValue)
                result["width"] = width.Value;
            return result;
        }

        private static bool TryKindFromEntry(
            JObject entry,
            out SplineKind kind)
        {
            var handler = (string)entry?["handler"] ?? string.Empty;
            if (handler.IndexOf(
                    "AutoTrestle",
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                kind = SplineKind.Trestle;
                return true;
            }
            if (handler.IndexOf(
                    "FlowyThing",
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                kind = string.Equals(
                    (string)entry["style"],
                    "River",
                    StringComparison.OrdinalIgnoreCase)
                    ? SplineKind.River
                    : SplineKind.Road;
                return true;
            }
            kind = SplineKind.Road;
            return false;
        }

        private static SplineKind KindFromEntry(JObject entry)
        {
            if (TryKindFromEntry(entry, out var kind))
                return kind;
            throw new InvalidOperationException(
                "This object is not a supported Strange Customs spline.");
        }

        private static SplineKind ParseSplineKind(string kind)
        {
            if (string.Equals(
                    kind,
                    "Bridge",
                    StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    kind,
                    "Trestle",
                    StringComparison.OrdinalIgnoreCase))
            {
                return SplineKind.Trestle;
            }
            if (string.Equals(
                    kind,
                    "River",
                    StringComparison.OrdinalIgnoreCase))
            {
                return SplineKind.River;
            }
            if (string.Equals(
                    kind,
                    "Road",
                    StringComparison.OrdinalIgnoreCase))
            {
                return SplineKind.Road;
            }
            throw new InvalidOperationException(
                "Spline type must be Road, River, or Bridge/Trestle.");
        }

        private static string NormalizeEndStyle(string value)
        {
            return string.Equals(
                value,
                "Bent",
                StringComparison.OrdinalIgnoreCase)
                ? "Bent"
                : "Block";
        }

        private static string ReadEntryString(
            JObject entry,
            string name,
            string fallback)
        {
            var property = entry?.Properties().FirstOrDefault(
                candidate => string.Equals(
                    candidate.Name,
                    name,
                    StringComparison.OrdinalIgnoreCase));
            return (string)property?.Value ?? fallback;
        }

        private static void SetEntryValue(
            JObject entry,
            string name,
            JToken value)
        {
            var property = entry.Properties().FirstOrDefault(
                candidate => string.Equals(
                    candidate.Name,
                    name,
                    StringComparison.OrdinalIgnoreCase));
            if (property == null)
                entry[name] = value;
            else
                property.Value = value;
        }

        private static Vector3 ReadVector(JToken token)
        {
            return new Vector3(
                (float?)token?["x"] ?? 0f,
                (float?)token?["y"] ?? 0f,
                (float?)token?["z"] ?? 0f);
        }
    }

    internal sealed class TileEditorSplinePointOverlay
        : MonoBehaviour, IPickable
    {
        private TileEditorGraphSession _session;
        private string _splineId;
        private int _pointIndex;
        private string _kind = string.Empty;
        private int _pointCount;
        private float? _width;
        private LineRenderer _line;
        private BoxCollider _collider;

        internal string SplineId => _splineId;
        internal int PointIndex => _pointIndex;

        public float MaxPickDistance => 600f;
        public int Priority => 20;
        public PickableActivationFilter ActivationFilter =>
            PickableActivationFilter.Any;

        public TooltipInfo TooltipInfo
        {
            get
            {
                if (_session == null
                    || !_session.TryGetSplineOverlayData(
                        _splineId,
                        _pointIndex,
                        out _,
                        out _,
                        out var kind,
                        out var count,
                        out var width))
                {
                    return TooltipInfo.Empty;
                }
                var details = "Control point " + (_pointIndex + 1)
                              + " / " + count;
                if (width.HasValue)
                {
                    details += "\nWidth: " + width.Value.ToString(
                        "F1",
                        CultureInfo.InvariantCulture);
                }
                return new TooltipInfo(
                    "Tile Editor " + kind + " " + _splineId,
                    details);
            }
        }

        internal void Initialize(
            TileEditorGraphSession session,
            string splineId,
            int pointIndex)
        {
            _session = session;
            _splineId = splineId;
            _pointIndex = pointIndex;
            BuildVisual();
        }

        internal void Refresh()
        {
            BuildVisual();
        }

        internal void SetOverlayVisible(bool visible)
        {
            enabled = visible;
            if (_line != null)
                _line.enabled = visible;
            if (_collider != null)
                _collider.enabled = visible;
            if (visible)
                RefreshColor();
        }

        public void Activate(PickableActivateEvent evt)
        {
            _session?.SelectSplinePoint(_splineId, _pointIndex);
        }

        public void Deactivate()
        {
        }

        private void RefreshColor()
        {
            if (_line == null || _session == null)
                return;
            TileEditorOverlayVisuals.SetColor(
                _line,
                _session.IsSelectedSplinePoint(
                    _splineId,
                    _pointIndex)
                    ? Color.magenta
                    : string.Equals(
                        _kind,
                        "River",
                        StringComparison.OrdinalIgnoreCase)
                        ? new Color(0.15f, 0.65f, 1f)
                        : string.Equals(
                            _kind,
                            "Trestle",
                            StringComparison.OrdinalIgnoreCase)
                            ? new Color(0.35f, 1f, 0.35f)
                            : new Color(1f, 0.62f, 0.15f));
        }

        private void BuildVisual()
        {
            if (_session == null
                || !_session.TryGetSplineOverlayData(
                    _splineId,
                    _pointIndex,
                    out var localPosition,
                    out var localRotation,
                    out _kind,
                    out _pointCount,
                    out _width))
            {
                return;
            }
            gameObject.layer = Layers.Clickable;
            transform.localPosition = localPosition + Vector3.up * 0.12f;
            transform.localEulerAngles = localRotation;

            _line = GetComponent<LineRenderer>()
                    ?? gameObject.AddComponent<LineRenderer>();
            _line.sharedMaterial =
                TileEditorOverlayVisuals.SharedLineMaterial;
            _line.startWidth = 0.13f;
            _line.endWidth = 0.13f;
            _line.useWorldSpace = false;
            _line.loop = true;
            _line.positionCount = 5;
            _line.SetPositions(new[]
            {
                new Vector3(0f, 0f, 0.8f),
                new Vector3(0.48f, 0f, 0f),
                new Vector3(0f, 0f, -0.48f),
                new Vector3(-0.48f, 0f, 0f),
                new Vector3(0f, 0f, 0.8f),
            });

            _collider = GetComponent<BoxCollider>()
                        ?? gameObject.AddComponent<BoxCollider>();
            _collider.center = new Vector3(0f, 0.15f, 0.1f);
            _collider.size = new Vector3(1.2f, 0.7f, 1.6f);
            RefreshColor();
        }

    }
}
