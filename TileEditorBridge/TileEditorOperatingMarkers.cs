using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Hrogers.TileEditorBridge
{
    internal sealed partial class TileEditorGraphSession
    {
        internal sealed class OperatingMarkerInfo
        {
            internal string Id = string.Empty;
            internal string DisplayName = string.Empty;
            internal string MarkerType = string.Empty;
            internal string CompanyId = string.Empty;
            internal string TargetId = string.Empty;
            internal string TargetField = string.Empty;
            internal string NativeIndustryId = string.Empty;
            internal string NativeComponentId = string.Empty;
            internal string NativeComponentType = string.Empty;
            internal string NativePassengerStopId = string.Empty;
            internal string ServiceIds = string.Empty;
            internal string Location = string.Empty;
            internal string NodeId = string.Empty;
            internal string SegmentId = string.Empty;
            internal string TrackSpanIds = string.Empty;
            internal string TrackGroupIds = string.Empty;
            internal string AllowedCompanyIds = string.Empty;
            internal string Role = string.Empty;
            internal string Direction = string.Empty;
            internal string ToleranceMeters = string.Empty;
            internal string CapacityMeters = string.Empty;
            internal string ApproachMeters = string.Empty;
            internal string DwellMinutes = string.Empty;
            internal string MaxCars = string.Empty;
            internal string Notes = string.Empty;
            internal Vector3 Position;
            internal bool HasPosition;
        }

        internal sealed class OperatingTerritoryInfo
        {
            internal string Id = string.Empty;
            internal string DisplayName = string.Empty;
            internal string OwnerCompanyId = string.Empty;
            internal string TrackGroupIds = string.Empty;
            internal string AllowedCompanyIds = string.Empty;
        }

        internal sealed class OperatingMarkerDraft
        {
            internal string Id;
            internal string DisplayName;
            internal string MarkerType;
            internal string CompanyId;
            internal string TargetId;
            internal string TargetField;
            internal string NativeIndustryId;
            internal string NativeComponentId;
            internal string NativeComponentType;
            internal string NativePassengerStopId;
            internal string ServiceIds;
            internal string TrackSpanIds;
            internal string TrackGroupIds;
            internal string AllowedCompanyIds;
            internal string Role;
            internal string Direction;
            internal string ToleranceMeters;
            internal string CapacityMeters;
            internal string ApproachMeters;
            internal string DwellMinutes;
            internal string MaxCars;
            internal string Notes;
        }

        private readonly List<OperatingMarkerInfo> _operatingMarkers =
            new List<OperatingMarkerInfo>();
        private readonly List<OperatingTerritoryInfo> _operatingTerritories =
            new List<OperatingTerritoryInfo>();
        private static readonly HashSet<string> SupportedOperatingMarkerTypes =
            new HashSet<string>(new[]
            {
                "grade-crossing", "passenger-stop",
                "passenger-platform-clear", "clearance-point",
                "fouling-point", "switching-lead", "runaround-limit",
                "portal", "portal-entry", "portal-exit", "yard-track-role",
                "interchange-spot", "interchange-main-clear",
                "interchange-north-lead", "interchange-limit",
                "freight-house-spot", "freight-house-clear", "freight-spot",
                "recovery-checkpoint", "mail-spot", "shop-bay",
                "roundhouse-track", "shop-stores", "supply-receiving",
                "authority-limit", "ownership-boundary", "territory-rights",
                "caboose-drop",
            }, StringComparer.OrdinalIgnoreCase);
        private string _selectedOperatingMarkerId = string.Empty;
        private string _operatingMarkerPath = string.Empty;
        private string _operatingTerritoryPath = string.Empty;

        internal IReadOnlyList<OperatingMarkerInfo> OperatingMarkers
            => _operatingMarkers;
        internal IReadOnlyList<OperatingTerritoryInfo> OperatingTerritories
            => _operatingTerritories;
        internal string OperatingMarkerPath => _operatingMarkerPath;
        internal string OperatingTerritoryPath => _operatingTerritoryPath;

        internal void RefreshOperatingMetadata()
        {
            var root = Path.Combine(_gameRoot, "Mods", "AITraffic",
                "territory");
            _operatingMarkerPath = Path.Combine(root,
                "operating-markers.json");
            _operatingTerritoryPath = Path.Combine(root, "ownership.json");
            _operatingMarkers.Clear();
            _operatingTerritories.Clear();
            var markerRoot = ReadJsonObject(_operatingMarkerPath);
            foreach (var token in markerRoot?["markers"] as JArray
                     ?? new JArray())
            {
                var row = token as JObject;
                if (row == null)
                    continue;
                var position = row["position"] as JObject;
                _operatingMarkers.Add(new OperatingMarkerInfo
                {
                    Id = ReadText(row, "id"),
                    DisplayName = ReadText(row, "displayName"),
                    MarkerType = ReadText(row, "markerType"),
                    CompanyId = ReadText(row, "companyId"),
                    TargetId = ReadText(row, "targetId"),
                    TargetField = ReadText(row, "targetField"),
                    NativeIndustryId = ReadText(row, "nativeIndustryId"),
                    NativeComponentId = ReadText(row, "nativeComponentId"),
                    NativeComponentType = ReadText(row,
                        "nativeComponentType"),
                    NativePassengerStopId = ReadText(row,
                        "nativePassengerStopId"),
                    ServiceIds = JoinArray(row["serviceIds"]),
                    Location = ReadText(row, "location"),
                    NodeId = ReadText(row, "nodeId"),
                    SegmentId = ReadText(row, "segmentId"),
                    TrackSpanIds = JoinArray(row["trackSpanIds"]),
                    TrackGroupIds = JoinArray(row["trackGroupIds"]),
                    AllowedCompanyIds = JoinArray(row["allowedCompanyIds"]),
                    Role = ReadText(row, "role"),
                    Direction = ReadText(row, "direction"),
                    ToleranceMeters = ReadNumber(row, "toleranceMeters"),
                    CapacityMeters = ReadNumber(row, "capacityMeters"),
                    ApproachMeters = ReadNumber(row, "approachMeters"),
                    DwellMinutes = ReadNumber(row, "dwellMinutes"),
                    MaxCars = ReadNumber(row, "maxCars"),
                    Notes = ReadText(row, "notes"),
                    HasPosition = position != null,
                    Position = position == null ? Vector3.zero : new Vector3(
                        ReadFloat(position["x"]), ReadFloat(position["y"]),
                        ReadFloat(position["z"])),
                });
            }
            var territoryToken = ReadJsonToken(_operatingTerritoryPath);
            var territoryRows = territoryToken is JArray array
                ? array : territoryToken?["territories"] as JArray
                    ?? new JArray();
            foreach (var token in territoryRows)
            {
                var row = token as JObject;
                if (row == null)
                    continue;
                _operatingTerritories.Add(new OperatingTerritoryInfo
                {
                    Id = ReadText(row, "id"),
                    DisplayName = ReadText(row, "displayName"),
                    OwnerCompanyId = ReadText(row, "ownerCompanyId"),
                    TrackGroupIds = JoinArray(row["trackGroupIds"]),
                    AllowedCompanyIds = JoinArray(row["allowedCompanyIds"]),
                });
            }
        }

        internal string CreateOperatingMarkerAtPosition(
            OperatingMarkerDraft draft, Vector3 gamePosition)
        {
            RequireSession();
            var segment = _selectedSegment;
            if (segment == null)
                throw new InvalidOperationException(
                    "Select the track segment that owns this marker first.");
            var t = ClosestCurveParameter(segment.Curve, gamePosition);
            var distance = Mathf.Clamp01(t) * segment.GetLength();
            return SaveOperatingMarker(draft,
                segment.id + "|a|" + distance.ToString("0.###",
                    CultureInfo.InvariantCulture),
                string.Empty, segment.id, gamePosition);
        }

        internal string CreateOperatingMarkerAtSelectedNode(
            OperatingMarkerDraft draft)
        {
            RequireSession();
            if (_selectedNode == null)
                throw new InvalidOperationException(
                    "Select the track node that owns this marker first.");
            var segmentId = _graph.SegmentsConnectedTo(_selectedNode)
                .Select(item => item?.id).FirstOrDefault(item =>
                    !string.IsNullOrWhiteSpace(item)) ?? string.Empty;
            return SaveOperatingMarker(draft, string.Empty,
                _selectedNode.id, segmentId,
                _selectedNode.transform.localPosition);
        }

        internal string CreateOperatingMarkerForSelectedTrack(
            OperatingMarkerDraft draft)
        {
            RequireSession();
            if (_selectedSegment == null)
                throw new InvalidOperationException(
                    "Select a track segment before creating this metadata.");
            if (string.IsNullOrWhiteSpace(draft.TrackGroupIds))
                draft.TrackGroupIds = _selectedSegment.groupId;
            return SaveOperatingMarker(draft, string.Empty, string.Empty,
                _selectedSegment.id,
                _selectedSegment.Curve.GetPoint(0.5f));
        }

        private string SaveOperatingMarker(OperatingMarkerDraft draft,
            string location, string nodeId, string segmentId,
            Vector3 position)
        {
            if (draft == null)
                throw new InvalidOperationException("Marker data is missing.");
            var id = NormalizeMetadataId(draft.Id, "marker");
            var markerType = (draft.MarkerType ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(markerType))
                throw new InvalidOperationException("Marker type is required.");
            if (!SupportedOperatingMarkerTypes.Contains(markerType))
                throw new InvalidOperationException("Unsupported marker type '"
                                                    + markerType + "'.");
            var root = LoadOrCreateMarkerRoot();
            var rows = root["markers"] as JArray
                       ?? (JArray)(root["markers"] = new JArray());
            var row = rows.OfType<JObject>().FirstOrDefault(item =>
                string.Equals(ReadText(item, "id"), id,
                    StringComparison.OrdinalIgnoreCase));
            var updating = row != null;
            if (!updating)
            {
                row = new JObject();
                rows.Add(row);
            }
            row["id"] = id;
            row["displayName"] = string.IsNullOrWhiteSpace(draft.DisplayName)
                ? id : draft.DisplayName.Trim();
            row["markerType"] = markerType;
            row["enabled"] = true;
            row["position"] = Vector(position);
            SetOptionalOrRemove(row, "companyId", draft.CompanyId);
            SetOptionalOrRemove(row, "targetId", draft.TargetId);
            SetOptionalOrRemove(row, "targetField", draft.TargetField);
            SetOptionalOrRemove(row, "nativeIndustryId",
                draft.NativeIndustryId);
            SetOptionalOrRemove(row, "nativeComponentId",
                draft.NativeComponentId);
            SetOptionalOrRemove(row, "nativeComponentType",
                draft.NativeComponentType);
            SetOptionalOrRemove(row, "nativePassengerStopId",
                draft.NativePassengerStopId);
            SetArrayOrRemove(row, "serviceIds", draft.ServiceIds);
            SetOptionalOrRemove(row, "location", location);
            SetOptionalOrRemove(row, "nodeId", nodeId);
            SetOptionalOrRemove(row, "segmentId", segmentId);
            SetArrayOrRemove(row, "trackSpanIds", draft.TrackSpanIds);
            SetArrayOrRemove(row, "trackGroupIds", draft.TrackGroupIds);
            SetArrayOrRemove(row, "allowedCompanyIds",
                draft.AllowedCompanyIds);
            SetOptionalOrRemove(row, "role", draft.Role);
            SetOptionalOrRemove(row, "direction", draft.Direction);
            SetOptionalNumberOrRemove(row, "toleranceMeters",
                draft.ToleranceMeters);
            SetOptionalNumberOrRemove(row, "capacityMeters",
                draft.CapacityMeters);
            SetOptionalNumberOrRemove(row, "approachMeters",
                draft.ApproachMeters);
            SetOptionalNumberOrRemove(row, "dwellMinutes",
                draft.DwellMinutes);
            SetOptionalIntOrRemove(row, "maxCars", draft.MaxCars);
            SetOptionalOrRemove(row, "notes", draft.Notes);
            WriteJson(_operatingMarkerPath, root);
            _selectedOperatingMarkerId = id;
            RefreshOperatingMetadata();
            return (updating ? "Updated" : "Saved")
                   + " operating marker " + id + " to "
                   + _operatingMarkerPath;
        }

        internal string DeleteOperatingMarker(string id)
        {
            var root = LoadOrCreateMarkerRoot();
            var rows = root["markers"] as JArray;
            var row = rows?.OfType<JObject>().FirstOrDefault(item =>
                string.Equals(ReadText(item, "id"), id,
                    StringComparison.OrdinalIgnoreCase));
            if (row == null)
                throw new InvalidOperationException(
                    "Operating marker " + id + " was not found.");
            row.Remove();
            WriteJson(_operatingMarkerPath, root);
            RefreshOperatingMetadata();
            return "Deleted operating marker " + id;
        }

        internal string SaveOperatingTerritory(string id,
            string displayName, string ownerCompanyId,
            string trackGroupIds, string allowedCompanyIds)
        {
            id = NormalizeMetadataId(id, "territory");
            ownerCompanyId = (ownerCompanyId ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(ownerCompanyId))
                throw new InvalidOperationException(
                    "Territory owner company is required.");
            if (string.IsNullOrWhiteSpace(trackGroupIds)
                && _selectedSegment != null)
                trackGroupIds = _selectedSegment.groupId;
            var root = ReadJsonObject(_operatingTerritoryPath)
                       ?? new JObject { ["territories"] = new JArray() };
            var rows = root["territories"] as JArray
                       ?? (JArray)(root["territories"] = new JArray());
            var row = rows.OfType<JObject>().FirstOrDefault(item =>
                string.Equals(ReadText(item, "id"), id,
                    StringComparison.OrdinalIgnoreCase));
            if (row == null)
            {
                row = new JObject();
                rows.Add(row);
            }
            row["id"] = id;
            row["displayName"] = string.IsNullOrWhiteSpace(displayName)
                ? id : displayName.Trim();
            row["ownerCompanyId"] = ownerCompanyId;
            row["trackGroupIds"] = ValuesArray(trackGroupIds);
            row["allowedCompanyIds"] = ValuesArray(allowedCompanyIds);
            WriteJson(_operatingTerritoryPath, root);
            RefreshOperatingMetadata();
            return "Saved territory " + id + " to "
                   + _operatingTerritoryPath;
        }

        private JObject LoadOrCreateMarkerRoot()
        {
            if (string.IsNullOrWhiteSpace(_operatingMarkerPath))
                RefreshOperatingMetadata();
            return ReadJsonObject(_operatingMarkerPath)
                   ?? new JObject
                   {
                       ["schemaVersion"] = 1,
                       ["markers"] = new JArray(),
                   };
        }

        private static JToken ReadJsonToken(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return null;
            return JToken.Parse(File.ReadAllText(path));
        }

        private static JObject ReadJsonObject(string path)
            => ReadJsonToken(path) as JObject;

        private static void WriteJson(string path, JToken token)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(path, token.ToString(Formatting.Indented));
        }

        private static string NormalizeMetadataId(string value,
            string fallback)
        {
            value = (value ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException(
                    fallback + " ID is required.");
            return value;
        }

        private static void SetOptional(JObject row, string key,
            string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                row[key] = value.Trim();
        }

        private static void SetOptionalOrRemove(JObject row, string key,
            string value)
        {
            if (string.IsNullOrWhiteSpace(value)) row.Remove(key);
            else row[key] = value.Trim();
        }

        private static void SetArray(JObject row, string key, string value)
        {
            var array = ValuesArray(value);
            if (array.Count > 0)
                row[key] = array;
        }

        private static void SetArrayOrRemove(JObject row, string key,
            string value)
        {
            var array = ValuesArray(value);
            if (array.Count == 0) row.Remove(key);
            else row[key] = array;
        }

        private static JArray ValuesArray(string value)
            => new JArray((value ?? string.Empty).Split(new[] { ',', '\n' },
                    StringSplitOptions.RemoveEmptyEntries)
                .Select(item => item.Trim()).Where(item => item.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase));

        private static void SetOptionalNumber(JObject row, string key,
            string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;
            if (!float.TryParse(value, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var parsed) || parsed < 0f)
                throw new InvalidOperationException(key
                    + " must be a non-negative number.");
            row[key] = parsed;
        }

        private static void SetOptionalNumberOrRemove(JObject row, string key,
            string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                row.Remove(key);
                return;
            }
            SetOptionalNumber(row, key, value);
        }

        private static void SetOptionalInt(JObject row, string key,
            string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;
            if (!int.TryParse(value, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var parsed) || parsed < 0)
                throw new InvalidOperationException(key
                    + " must be a non-negative whole number.");
            row[key] = parsed;
        }

        private static void SetOptionalIntOrRemove(JObject row, string key,
            string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                row.Remove(key);
                return;
            }
            SetOptionalInt(row, key, value);
        }

        private static string ReadText(JObject row, string key)
            => row?[key]?.Type == JTokenType.Null
                ? string.Empty : row?[key]?.ToString() ?? string.Empty;

        private static string ReadNumber(JObject row, string key)
            => row?[key] == null ? string.Empty
                : Convert.ToString((object)row[key],
                    CultureInfo.InvariantCulture);

        private static string JoinArray(JToken token)
            => token is JArray array
                ? string.Join(", ", array.Values<string>())
                : string.Empty;

        private static float ReadFloat(JToken token)
            => token == null ? 0f : token.Value<float>();
    }
}
