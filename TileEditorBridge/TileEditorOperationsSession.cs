using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Core;
using Helpers;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Hrogers.TileEditorBridge
{
    internal sealed partial class TileEditorGraphSession
    {
        internal enum OperationKind
        {
            Town,
            TrackSpan,
            Industry,
            PassengerStop,
            RailLoader,
            RailUnloader,
            RepairTrack,
            TeamTrack,
            Interchange,
            Progression,
            CustomComponent,
            Commodity,
            PhysicalLoader,
            StationAgent,
            Turntable,
        }

        internal sealed class OperationInfo
        {
            internal string Key = string.Empty;
            internal string Id = string.Empty;
            internal string Name = string.Empty;
            internal string OwnerId = string.Empty;
            internal string Type = string.Empty;
            internal string Detail = string.Empty;
            internal OperationKind Kind;
            internal Vector3 Position;
            internal Vector3 Rotation;
            internal bool HasPosition;
            internal float Radius;

            internal string DisplayLabel =>
                OperationKindLabel(Kind) + "  " + Id
                + (string.IsNullOrWhiteSpace(Name)
                    || string.Equals(
                        Name,
                        Id,
                        StringComparison.OrdinalIgnoreCase)
                    ? string.Empty
                    : "  -  " + Name);
        }

        internal sealed class IndustryComponentOptions
        {
            internal string IndustryId = string.Empty;
            internal string ComponentId = string.Empty;
            internal string Profile = string.Empty;
            internal string Name = string.Empty;
            internal string SpanIds = string.Empty;
            internal string LoadId = string.Empty;
            internal string CarTypeFilter = string.Empty;
            internal string CustomType = string.Empty;
            internal bool SharedStorage = true;
            internal float StorageChangeRate;
            internal float MaxStorage = 100000f;
            internal float CarTransferRate = 200000f;
            internal bool OrderAroundEmpties = true;
            internal bool OrderAroundLoaded = true;
            internal string FormulaInputs = string.Empty;
            internal string FormulaOutputs = string.Empty;
            internal float IdealCars = 2f;
            internal string TeamProfiles = string.Empty;
            internal bool CanOverhaul = true;
            internal string PassengerStopId = string.Empty;
            internal string TimetableCode = string.Empty;
            internal int BasePopulation = 5;
            internal string Branch = "Main";
            internal string NeighborIds = string.Empty;
            internal string OutputSpanIds = string.Empty;
            internal string ConvertedLoadId = string.Empty;
            internal float? CostPerUnit;
            internal float? NotBeforeHour;
            internal float? NotAfterHour;
            internal float? FillPercentage;
            internal string BookReasons = string.Empty;
            internal string Title = string.Empty;
            internal string CustomFieldsJson = string.Empty;
        }

        private readonly List<OperationInfo> _operations =
            new List<OperationInfo>();
        private readonly Dictionary<string, TileEditorOperationOverlay>
            _operationOverlays =
                new Dictionary<string, TileEditorOperationOverlay>(
                    StringComparer.OrdinalIgnoreCase);
        private bool _operationsMode;
        private string _selectedOperationKey = string.Empty;
        private GameObject _operationOverlayRoot;
        private string _cachedOperationSearch = string.Empty;
        private string _cachedOperationCategory = string.Empty;
        private int _cachedOperationOffset = -1;
        private int _cachedOperationMaximum = -1;
        private int _cachedOperationTotal;
        private IReadOnlyList<OperationInfo> _cachedOperationResults =
            Array.Empty<OperationInfo>();

        internal IReadOnlyList<OperationInfo> Operations => _operations;
        internal bool FuseOperationsDocument => _fuseNativeDocument;
        internal OperationInfo SelectedOperation =>
            _operations.FirstOrDefault(item => string.Equals(
                item.Key,
                _selectedOperationKey,
                StringComparison.OrdinalIgnoreCase));

        internal void SetOperationsMode(bool active)
        {
            if (_operationsMode == active)
                return;
            _operationsMode = active;
            if (active && GraphOpen)
            {
                RefreshOperationsMode(true);
                SetOverlaysVisible(_editModeActive);
            }
            SetOperationOverlaysVisible(
                active && _editModeActive && GraphOpen);
        }

        internal void RefreshOperations()
        {
            RefreshOperationsMode(true);
        }

        internal IReadOnlyList<OperationInfo> SearchOperations(
            string query,
            string category,
            int offset,
            int maximum,
            out int totalMatches)
        {
            query = (query ?? string.Empty).Trim();
            category = (category ?? string.Empty).Trim();
            offset = Mathf.Max(0, offset);
            maximum = Mathf.Clamp(maximum, 1, 100);
            if (string.Equals(
                    query,
                    _cachedOperationSearch,
                    StringComparison.Ordinal)
                && string.Equals(
                    category,
                    _cachedOperationCategory,
                    StringComparison.Ordinal)
                && offset == _cachedOperationOffset
                && maximum == _cachedOperationMaximum)
            {
                totalMatches = _cachedOperationTotal;
                return _cachedOperationResults;
            }

            var page = new List<OperationInfo>(maximum);
            var matchCount = 0;
            foreach (var item in _operations)
            {
                if (!OperationMatchesCategory(item, category)
                    || (query.Length > 0
                        && item.DisplayLabel.IndexOf(
                            query,
                            StringComparison.OrdinalIgnoreCase) < 0
                        && item.Detail.IndexOf(
                            query,
                            StringComparison.OrdinalIgnoreCase) < 0))
                {
                    continue;
                }
                if (matchCount >= offset && page.Count < maximum)
                    page.Add(item);
                matchCount++;
            }
            _cachedOperationSearch = query;
            _cachedOperationCategory = category;
            _cachedOperationOffset = offset;
            _cachedOperationMaximum = maximum;
            _cachedOperationTotal = matchCount;
            _cachedOperationResults = page;
            totalMatches = matchCount;
            return page;
        }

        internal void SelectOperation(string key)
        {
            _selectedOperationKey = key ?? string.Empty;
            RefreshOperationOverlayColors();
        }

        internal bool IsSelectedOperation(string key)
        {
            return !string.IsNullOrWhiteSpace(key)
                   && string.Equals(
                       key,
                       _selectedOperationKey,
                       StringComparison.OrdinalIgnoreCase);
        }

        internal void ShowSelectedOperation()
        {
            var selected = SelectedOperation;
            if (selected == null || !selected.HasPosition)
            {
                throw new InvalidOperationException(
                    "The selected operations entry has no position in this layer.");
            }
            if (CameraSelector.shared == null)
                throw new InvalidOperationException(
                    "Railroader's camera is not ready.");
            CameraSelector.shared.ZoomToPoint(selected.Position);
        }

        internal string CreateTown(
            string id,
            string name,
            Vector3 position,
            float radius)
        {
            RequireSession();
            id = NormalizeOperationId(id, "town");
            name = RequireOperationText(name, "town name");
            ValidateOperationPosition(position);
            if (radius < 25f || radius > 10000f)
            {
                throw new InvalidOperationException(
                    "Town radius must be between 25 and 10,000 metres.");
            }

            ExecuteOperationsEdit(
                "Create operations town",
                () =>
                {
                    var areas = EnsureOperationObject(
                        _fuseNativeDocument
                            ? EnsureOperationObject(_document, "tracks")
                            : _document,
                        "areas");
                    RequireUnusedOperationId(areas, id, "town");
                    var entry = new JObject
                    {
                        ["name"] = name,
                        ["position"] = Vector(position),
                        ["radius"] = radius,
                        ["tagColor"] = new JArray(0.18f, 0.55f, 0.9f, 1f),
                        ["order"] = 0,
                    };
                    if (_fuseNativeDocument)
                    {
                        entry["spanIds"] = new JArray();
                        entry["groupId"] = string.Empty;
                    }
                    else
                    {
                        entry["industries"] = new JObject();
                    }
                    areas[id] = entry;
                    _selectedOperationKey = OperationKey(
                        OperationKind.Town,
                        id);
                });
            return "Created town " + id;
        }

        internal string CreateSpanFromSelectedSegment(string id)
        {
            RequireSession();
            var segment = _selectedSegment;
            if (segment == null)
            {
                throw new InvalidOperationException(
                    "Click the track segment that the new TrackSpan should cover.");
            }
            return CreateSpanBetweenSegments(
                id,
                segment.id,
                0f,
                segment.id,
                segment.GetLength());
        }

        internal string CreatePartialSpanOnSelectedSegment(
            string id,
            float startDistance,
            float endDistance)
        {
            RequireSession();
            var segment = _selectedSegment;
            if (segment == null)
            {
                throw new InvalidOperationException(
                    "Click the track segment that the new TrackSpan should cover.");
            }
            return CreateSpanBetweenSegments(
                id,
                segment.id,
                startDistance,
                segment.id,
                endDistance);
        }

        internal string CreateSpanBetweenSegments(
            string id,
            string startSegmentId,
            float startDistance,
            string endSegmentId,
            float endDistance)
        {
            RequireSession();
            id = NormalizeOperationId(id, "span");
            startSegmentId = RequireOperationText(
                startSegmentId,
                "start segment");
            endSegmentId = RequireOperationText(
                endSegmentId,
                "end segment");
            var startSegment = _graph.GetSegment(startSegmentId)
                               ?? throw new InvalidOperationException(
                                   "Start segment " + startSegmentId
                                   + " is not in the live graph.");
            var endSegment = _graph.GetSegment(endSegmentId)
                             ?? throw new InvalidOperationException(
                                 "End segment " + endSegmentId
                                 + " is not in the live graph.");
            ValidateSpanDistance(
                startDistance,
                startSegment.GetLength(),
                "start");
            ValidateSpanDistance(
                endDistance,
                endSegment.GetLength(),
                "end");
            if (startSegment == endSegment
                && endDistance <= startDistance + 0.001f)
            {
                throw new InvalidOperationException(
                    "The end distance must be greater than the start distance.");
            }
            if (startSegment != endSegment
                && !SegmentsShareConnectedGraph(
                    startSegment,
                    endSegment))
            {
                throw new InvalidOperationException(
                    "The marked start and end segments are not connected.");
            }
            ExecuteOperationsEdit(
                startSegment == endSegment
                    ? "Create partial TrackSpan"
                    : "Create multi-segment TrackSpan",
                () =>
                {
                    var tracks = EnsureOperationObject(
                        _document,
                        "tracks");
                    var spans = EnsureOperationObject(
                        tracks,
                        "spans");
                    RequireUnusedOperationId(
                        spans,
                        id,
                        "TrackSpan");
                    spans[id] = new JObject
                    {
                        ["lower"] = SpanLocation(
                            startSegment,
                            startDistance),
                        ["upper"] = SpanLocation(
                            endSegment,
                            endDistance),
                        ["normalize"] = true,
                    };
                    _selectedOperationKey = OperationKey(
                        OperationKind.TrackSpan,
                        id);
                });
            return "Created TrackSpan " + id + " from "
                   + startSegment.id + " to " + endSegment.id;
        }

        internal string CreateIndustry(
            string id,
            string name,
            string areaId,
            Vector3 position,
            bool usesContract)
        {
            RequireSession();
            id = NormalizeOperationId(id, "industry");
            name = RequireOperationText(name, "industry name");
            areaId = NormalizeOperationId(areaId, "town");
            ValidateOperationPosition(position);
            ExecuteOperationsEdit(
                "Create industry",
                () =>
                {
                    JObject industries;
                    JObject entry;
                    if (_fuseNativeDocument)
                    {
                        var operations = EnsureOperationObject(
                            _document,
                            "operations");
                        industries = EnsureOperationObject(
                            operations,
                            "industries");
                        RequireUnusedOperationId(
                            industries,
                            id,
                            "industry");
                        entry = new JObject
                        {
                            ["name"] = name,
                            ["areaId"] = areaId,
                            ["position"] = Vector(position),
                            ["rotation"] = Vector(Vector3.zero),
                            ["usesContract"] = usesContract,
                            ["components"] = new JObject(),
                        };
                    }
                    else
                    {
                        var areas = EnsureOperationObject(
                            _document,
                            "areas");
                        var area = areas[areaId] as JObject;
                        if (area == null)
                        {
                            throw new InvalidOperationException(
                                "Legacy industries require the owning town "
                                + "to be present in this layer so localPosition "
                                + "can be calculated safely.");
                        }
                        industries = EnsureOperationObject(
                            area,
                            "industries");
                        RequireUnusedOperationId(
                            industries,
                            id,
                            "industry");
                        var areaPosition = ReadOperationVector(
                            area["position"]);
                        entry = new JObject
                        {
                            ["name"] = name,
                            ["localPosition"] = Vector(
                                position - areaPosition),
                            ["usesContract"] = usesContract,
                            ["components"] = new JObject(),
                        };
                    }
                    industries[id] = entry;
                    _selectedOperationKey = OperationKey(
                        OperationKind.Industry,
                        id);
                });
            return "Created industry " + id;
        }

        internal string AddIndustryComponent(
            string industryId,
            string componentId,
            string profile,
            string name,
            string spanId,
            string loadId,
            string carTypeFilter,
            string customType)
        {
            return AddIndustryComponent(
                new IndustryComponentOptions
                {
                    IndustryId = industryId,
                    ComponentId = componentId,
                    Profile = profile,
                    Name = name,
                    SpanIds = spanId,
                    LoadId = loadId,
                    CarTypeFilter = carTypeFilter,
                    CustomType = customType,
                    TimetableCode =
                        PassengerCodeFromId(industryId),
                    PassengerStopId = industryId,
                });
        }

        internal string AddIndustryComponent(
            IndustryComponentOptions options)
        {
            RequireSession();
            if (options == null)
                throw new ArgumentNullException(nameof(options));
            var industryId = NormalizeOperationId(
                options.IndustryId,
                "industry");
            var componentId = NormalizeOperationId(
                options.ComponentId,
                "component");
            var name = RequireOperationText(
                options.Name,
                "component name");
            var profile = (options.Profile ?? string.Empty).Trim();
            var loadId = (options.LoadId ?? string.Empty).Trim();
            var carTypeFilter =
                (options.CarTypeFilter ?? string.Empty).Trim();
            var type = OperationComponentType(
                profile,
                options.CustomType);
            var spanIds = ParseOperationIdList(
                options.SpanIds);
            ValidateIndustryComponentOptions(
                profile,
                options);
            ExecuteOperationsEdit(
                "Add industry component",
                () =>
                {
                    var industry = FindIndustryObject(industryId);
                    var components = EnsureOperationObject(
                        industry,
                        "components");
                    RequireUnusedOperationId(
                        components,
                        componentId,
                        "component");
                    var entry = new JObject
                    {
                        ["type"] = type,
                        ["name"] = name,
                        [_fuseNativeDocument
                            ? "trackSpanIds"
                            : "trackSpans"] = new JArray(spanIds),
                        ["carTypeFilter"] = carTypeFilter,
                        ["sharedStorage"] = options.SharedStorage,
                    };
                    if (!string.IsNullOrWhiteSpace(loadId))
                        entry["loadId"] = loadId;
                    if (string.Equals(
                            profile,
                            "Passenger",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        if (_fuseNativeDocument)
                        {
                            entry["passengerStopId"] =
                                string.IsNullOrWhiteSpace(
                                    options.PassengerStopId)
                                    ? industryId
                                    : options.PassengerStopId.Trim();
                        }
                        entry["timetableCode"] =
                            string.IsNullOrWhiteSpace(
                                options.TimetableCode)
                                ? PassengerCodeFromId(industryId)
                                : options.TimetableCode.Trim();
                        entry["basePopulation"] =
                            options.BasePopulation;
                        entry["branch"] =
                            string.IsNullOrWhiteSpace(options.Branch)
                                ? "Main"
                                : options.Branch.Trim();
                        entry["neighborIds"] = new JArray(
                            ParseOperationIdList(
                                options.NeighborIds));
                        if (string.IsNullOrWhiteSpace(loadId))
                            entry["loadId"] = "passengers";
                        if (string.IsNullOrWhiteSpace(carTypeFilter))
                            entry["carTypeFilter"] = "*";
                    }
                    else if (string.Equals(
                                 profile,
                                 "Receives",
                                 StringComparison.OrdinalIgnoreCase)
                             || string.Equals(
                                 profile,
                                 "Ships",
                                 StringComparison.OrdinalIgnoreCase))
                    {
                        entry["storageChangeRate"] =
                            options.StorageChangeRate;
                        entry["maxStorage"] =
                            options.MaxStorage;
                        entry["carTransferRate"] =
                            options.CarTransferRate;
                        entry["orderAroundEmpties"] =
                            options.OrderAroundEmpties;
                        entry["orderAroundLoaded"] =
                            options.OrderAroundLoaded;
                    }
                    else if (string.Equals(
                                 profile,
                                 "Formula",
                                 StringComparison.OrdinalIgnoreCase))
                    {
                        var inputs = ParseOperationRateMap(
                            options.FormulaInputs,
                            "formula input");
                        var outputs = ParseOperationRateMap(
                            options.FormulaOutputs,
                            "formula output");
                        if (!inputs.Properties().Any()
                            && !outputs.Properties().Any())
                        {
                            throw new InvalidOperationException(
                                "A formula needs at least one daily input "
                                + "or output term.");
                        }
                        entry["inputTermsPerDay"] = inputs;
                        entry["outputTermsPerDay"] = outputs;
                    }
                    else if (string.Equals(
                                 profile,
                                 "Repair",
                                 StringComparison.OrdinalIgnoreCase))
                    {
                        entry["canOverhaul"] =
                            options.CanOverhaul;
                    }
                    else if (string.Equals(
                                 profile,
                                 "Team Track",
                                 StringComparison.OrdinalIgnoreCase))
                    {
                        entry["idealCars"] =
                            options.IdealCars;
                        var teamProfiles = ParseTeamProfiles(
                            options.TeamProfiles);
                        if (!teamProfiles.Properties().Any())
                        {
                            throw new InvalidOperationException(
                                "A team track needs at least one import "
                                + "or export profile.");
                        }
                        entry["teamProfiles"] = teamProfiles;
                    }
                    else if (string.Equals(
                                 profile,
                                 "Interchanged Loader",
                                 StringComparison.OrdinalIgnoreCase))
                    {
                        entry["outputSpanIds"] = new JArray(
                            ParseOperationIdList(
                                options.OutputSpanIds));
                    }
                    else if (string.Equals(
                                 profile,
                                 "Interchanged Unloader",
                                 StringComparison.OrdinalIgnoreCase)
                             && !string.IsNullOrWhiteSpace(
                                 options.ConvertedLoadId))
                    {
                        entry["convertedLoadId"] =
                            options.ConvertedLoadId.Trim();
                    }
                    AddOptionalIndustryComponentFields(
                        entry,
                        options);
                    components[componentId] = entry;
                    _selectedOperationKey = OperationKey(
                        ClassifyComponent(type),
                        industryId + "/" + componentId);
                });
            return "Added " + profile + " component " + componentId;
        }

        internal string CreatePhysicalLoader(
            string id,
            string prefab,
            string industryId,
            Vector3 position,
            float yaw)
        {
            RequireSession();
            id = NormalizeOperationId(id, "loader");
            prefab = RequireOperationText(prefab, "loader prefab");
            industryId = (industryId ?? string.Empty).Trim();
            ValidateOperationPosition(position);
            ExecuteOperationsEdit(
                "Create physical service loader",
                () =>
                {
                    if (_fuseNativeDocument)
                    {
                        var operations = EnsureOperationObject(
                            _document,
                            "operations");
                        var loaders = EnsureOperationObject(
                            operations,
                            "loaders");
                        RequireUnusedOperationId(
                            loaders,
                            id,
                            "physical loader");
                        loaders[id] = new JObject
                        {
                            ["position"] = Vector(position),
                            ["rotation"] = Vector(
                                new Vector3(0f, yaw, 0f)),
                            ["prefab"] = prefab,
                            ["industryId"] = industryId,
                        };
                    }
                    else
                    {
                        var splineys = EnsureOperationObject(
                            _document,
                            "splineys");
                        RequireUnusedOperationId(
                            splineys,
                            id,
                            "physical loader");
                        splineys[id] = new JObject
                        {
                            ["Position"] = Vector(position),
                            ["Rotation"] = Vector(
                                new Vector3(0f, yaw, 0f)),
                            ["Prefab"] = prefab,
                            ["Industry"] = industryId,
                            ["Handler"] =
                                "AlinasMapMod.LoaderBuilder",
                        };
                    }
                    _selectedOperationKey = OperationKey(
                        OperationKind.PhysicalLoader,
                        id);
                });
            return "Created physical loader " + id;
        }

        internal string AddEngineFacilityProfile(
            string industryId,
            string spanId,
            string profile)
        {
            RequireSession();
            industryId = NormalizeOperationId(
                industryId,
                "industry");
            spanId = NormalizeOperationId(spanId, "TrackSpan");
            profile = RequireOperationText(
                profile,
                "facility profile");
            ExecuteOperationsEdit(
                "Add " + profile + " engine facility",
                () =>
                {
                    var industry = FindIndustryObject(industryId);
                    var components = EnsureOperationObject(
                        industry,
                        "components");
                    if (string.Equals(
                            profile,
                            "Steam",
                            StringComparison.OrdinalIgnoreCase)
                        || string.Equals(
                            profile,
                            "Combined",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        AddFacilityReceivingComponent(
                            components,
                            "service-coal",
                            "Engine Coal Delivery",
                            spanId,
                            "coal",
                            "HM*,HT*");
                    }
                    if (string.Equals(
                            profile,
                            "Diesel",
                            StringComparison.OrdinalIgnoreCase)
                        || string.Equals(
                            profile,
                            "Combined",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        AddFacilityReceivingComponent(
                            components,
                            "service-diesel",
                            "Diesel Fuel Delivery",
                            spanId,
                            "diesel-fuel",
                            "TM*");
                    }
                    RequireUnusedOperationId(
                        components,
                        "service-repair",
                        "facility component");
                    components["service-repair"] = new JObject
                    {
                        ["type"] = _fuseNativeDocument
                            ? "repairTrack"
                            : "Model.Ops.RepairTrack",
                        ["name"] = "Engine Repair Track",
                        [_fuseNativeDocument
                            ? "trackSpanIds"
                            : "trackSpans"] = new JArray(spanId),
                        ["loadId"] = "repair-parts",
                        ["carTypeFilter"] = "*",
                        ["sharedStorage"] = true,
                        ["canOverhaul"] = true,
                    };
                    _selectedOperationKey = OperationKey(
                        OperationKind.Industry,
                        industryId);
                });
            return "Added " + profile
                   + " engine-facility operations to "
                   + industryId;
        }

        internal string CreateStationAgent(
            string id,
            string prefab,
            string passengerStopId,
            Vector3 position,
            float yaw)
        {
            RequireSession();
            if (!_fuseNativeDocument)
            {
                throw new InvalidOperationException(
                    "Station-agent placement is currently native FUSE only. "
                    + "Legacy passenger-stop operations are still supported.");
            }
            id = NormalizeOperationId(id, "station agent");
            prefab = RequireOperationText(
                prefab,
                "station-agent prefab");
            passengerStopId = NormalizeOperationId(
                passengerStopId,
                "passenger stop");
            ValidateOperationPosition(position);
            ExecuteOperationsEdit(
                "Create station agent",
                () =>
                {
                    var operations = EnsureOperationObject(
                        _document,
                        "operations");
                    var stations = EnsureOperationObject(
                        operations,
                        "stations");
                    RequireUnusedOperationId(
                        stations,
                        id,
                        "station agent");
                    stations[id] = new JObject
                    {
                        ["position"] = Vector(position),
                        ["rotation"] = Vector(
                            new Vector3(0f, yaw, 0f)),
                        ["prefab"] = prefab,
                        ["passengerStopId"] = passengerStopId,
                    };
                    _selectedOperationKey = OperationKey(
                        OperationKind.StationAgent,
                        id);
                });
            return "Created station agent " + id;
        }

        internal string CreateTurntable(
            string id,
            Vector3 position,
            float yaw,
            float radius,
            int subdivisions,
            int roundhouseStalls,
            float roundhouseStartAngle,
            float roundhouseStallAngle,
            float roundhouseTrackLength,
            float bridgeGauge)
        {
            RequireSession();
            id = NormalizeOperationId(id, "turntable");
            ValidateOperationPosition(position);
            if (radius <= 0f || radius > 100f)
                throw new InvalidOperationException(
                    "Turntable radius must be greater than 0 and no more than 100 m.");
            if (subdivisions < 4 || subdivisions > 32)
                throw new InvalidOperationException(
                    "Turntable subdivisions must be between 4 and 32.");
            if (roundhouseStalls < 0 || roundhouseStalls > 64)
                throw new InvalidOperationException(
                    "Roundhouse stalls must be between 0 and 64.");
            if (roundhouseStalls > 0 && roundhouseTrackLength <= 0f)
                throw new InvalidOperationException(
                    "Roundhouse track length must be greater than 0.");
            if (bridgeGauge <= 0f || bridgeGauge > 3f)
                throw new InvalidOperationException(
                    "Bridge-track gauge must be between 0 and 3 metres.");

            ExecuteOperationsEdit(
                "Create turntable",
                () =>
                {
                    if (_fuseNativeDocument)
                    {
                        var operations = EnsureOperationObject(
                            _document,
                            "operations");
                        var turntables = EnsureOperationObject(
                            operations,
                            "turntables");
                        RequireUnusedOperationId(
                            turntables,
                            id,
                            "turntable");
                        var entry = new JObject
                        {
                            ["position"] = Vector(position),
                            ["rotation"] = Vector(
                                new Vector3(0f, yaw, 0f)),
                            ["radius"] = radius,
                            ["subdivisions"] = subdivisions,
                            ["visuals"] = new JObject
                            {
                                ["bridgeTrackEnabled"] = true,
                                ["bridgeTrackGauge"] = bridgeGauge,
                                ["bridgeTrackLength"] = radius * 2f,
                                ["bridgeTrackYOffset"] = 0.08f,
                                ["interactionRadius"] =
                                    Mathf.Max(radius + 1f, 4f),
                            },
                        };
                        if (roundhouseStalls > 0)
                        {
                            entry["roundhouse"] = new JObject
                            {
                                ["stalls"] = roundhouseStalls,
                                ["startAngle"] =
                                    roundhouseStartAngle,
                                ["stallAngle"] =
                                    roundhouseStallAngle,
                                ["trackLength"] =
                                    roundhouseTrackLength,
                            };
                        }
                        turntables[id] = entry;
                    }
                    else
                    {
                        var splineys = EnsureOperationObject(
                            _document,
                            "splineys");
                        RequireUnusedOperationId(
                            splineys,
                            id,
                            "turntable");
                        splineys[id] = new JObject
                        {
                            ["Position"] = Vector(position),
                            ["Rotation"] = Vector(
                                new Vector3(0f, yaw, 0f)),
                            ["RoundhouseStalls"] =
                                roundhouseStalls,
                            ["handler"] =
                                "AlinasMapMod.Turntable.TurntableBuilder",
                        };
                    }
                    _selectedOperationKey = OperationKey(
                        OperationKind.Turntable,
                        id);
                });
            return "Created turntable " + id
                   + (_fuseNativeDocument
                       ? " (native FUSE)"
                       : " (legacy TurntableBuilder)");
        }

        internal string DeleteSelectedOperation()
        {
            var selected = SelectedOperation;
            if (selected == null)
                throw new InvalidOperationException(
                    "Select an operations entry first.");
            if (selected.Kind == OperationKind.Commodity)
                throw new InvalidOperationException(
                    "Commodity deletion is disabled until reference validation "
                    + "can confirm it is unused.");
            ExecuteOperationsEdit(
                "Delete operations entry",
                () => DeleteOperationToken(selected));
            _selectedOperationKey = string.Empty;
            return "Removed " + selected.DisplayLabel;
        }

        private void ExecuteOperationsEdit(string name, Action mutation)
        {
            RequireSession();
            RequireGraphEditOwnership();
            var beforeDocument = (JObject)_document.DeepClone();
            var beforeOperationKey = _selectedOperationKey;
            var edit = new EditRecord
            {
                Name = name,
                NodeIds = Array.Empty<string>(),
                SegmentIds = Array.Empty<string>(),
                SceneryIds = Array.Empty<string>(),
                MandelaIds = Array.Empty<string>(),
                BeforeNodes = new Dictionary<string, NodeModel>(),
                BeforeSegments = new Dictionary<string, SegmentModel>(),
                BeforeScenery = new Dictionary<string, SceneryModel>(),
                BeforeMandelas = new Dictionary<string, MandelaModel>(),
                BeforeDocument = beforeDocument,
                BeforeSelectedNode = _selectedNode?.id,
                BeforeSelectedSegment = _selectedSegment?.id,
                BeforeSelectedScenery = _selectedSceneryId,
                BeforeSelectedMandela = _selectedMandelaPath,
            };
            try
            {
                mutation();
            }
            catch
            {
                // Profile builders can touch several related entries. Restore
                // the complete document if any validation fails midway so an
                // unsuccessful click never leaves a partial facility behind.
                _document = (JObject)beforeDocument.DeepClone();
                _selectedOperationKey = beforeOperationKey;
                if (_operationsMode)
                    RefreshOperationsMode(true);
                throw;
            }
            edit.AfterNodes = new Dictionary<string, NodeModel>();
            edit.AfterSegments = new Dictionary<string, SegmentModel>();
            edit.AfterScenery = new Dictionary<string, SceneryModel>();
            edit.AfterMandelas = new Dictionary<string, MandelaModel>();
            edit.AfterDocument = (JObject)_document.DeepClone();
            edit.AfterSelectedNode = _selectedNode?.id;
            edit.AfterSelectedSegment = _selectedSegment?.id;
            edit.AfterSelectedScenery = _selectedSceneryId;
            edit.AfterSelectedMandela = _selectedMandelaPath;
            _undo.Push(edit);
            _redo.Clear();
            _dirty = true;
            RefreshOperationsMode(true);
        }

        private void RefreshOperationsMode(bool force)
        {
            if (!GraphOpen)
                return;
            // The open JObject only changes through explicit editor actions,
            // reload, undo, or redo, and each of those calls this with force.
            // Avoid rescanning and rebuilding markers from the timed refresh.
            if (!force)
                return;
            DiscoverOperations();
            RebuildOperationOverlays();
            SetOperationOverlaysVisible(
                _editModeActive && _operationsMode);
        }

        private void ResetOperationsSession()
        {
            _operations.Clear();
            _selectedOperationKey = string.Empty;
            InvalidateOperationSearch();
            DisposeOperationOverlays();
        }

        private void DisposeOperationsSession()
        {
            SetOperationOverlaysVisible(false);
            DisposeOperationOverlays();
            _operations.Clear();
            _selectedOperationKey = string.Empty;
            InvalidateOperationSearch();
        }

        private void DiscoverOperations()
        {
            _operations.Clear();
            DiscoverAreasAndSpans();
            DiscoverFuseOperations();
            DiscoverLegacyOperations();
            _operations.Sort((left, right) =>
            {
                var kind = left.Kind.CompareTo(right.Kind);
                return kind != 0
                    ? kind
                    : string.Compare(
                        left.Id,
                        right.Id,
                        StringComparison.OrdinalIgnoreCase);
            });
            if (!_operations.Any(item => string.Equals(
                    item.Key,
                    _selectedOperationKey,
                    StringComparison.OrdinalIgnoreCase)))
            {
                _selectedOperationKey = string.Empty;
            }
            InvalidateOperationSearch();
        }

        private void InvalidateOperationSearch()
        {
            _cachedOperationSearch = string.Empty;
            _cachedOperationCategory = string.Empty;
            _cachedOperationOffset = -1;
            _cachedOperationMaximum = -1;
            _cachedOperationTotal = 0;
            _cachedOperationResults =
                Array.Empty<OperationInfo>();
        }

        private void DiscoverAreasAndSpans()
        {
            var tracks = _document["tracks"] as JObject;
            var areas = _fuseNativeDocument
                ? tracks?["areas"] as JObject
                : _document["areas"] as JObject;
            if (areas != null)
            {
                foreach (var property in areas.Properties())
                {
                    var area = property.Value as JObject;
                    if (area == null)
                        continue;
                    var position = ReadOperationVector(area["position"]);
                    AddOperation(new OperationInfo
                    {
                        Key = OperationKey(
                            OperationKind.Town,
                            property.Name),
                        Id = property.Name,
                        Name = ReadOperationString(
                            area,
                            "name",
                            property.Name),
                        Kind = OperationKind.Town,
                        Position = position,
                        HasPosition = area["position"] is JObject,
                        Radius = ReadOperationFloat(
                            area["radius"],
                            250f),
                        Detail = "Operating area"
                                 + (area["industries"] is JObject legacyIndustries
                                     ? " - " + legacyIndustries.Properties()
                                           .Count(item => item.Value.Type
                                                          != JTokenType.Null)
                                       + " industries"
                                     : string.Empty),
                    });
                }
            }

            var spans = tracks?["spans"] as JObject;
            if (spans == null)
                return;
            foreach (var property in spans.Properties())
            {
                var span = property.Value as JObject;
                if (span == null)
                    continue;
                var position = SpanPosition(span, out var hasPosition);
                AddOperation(new OperationInfo
                {
                    Key = OperationKey(
                        OperationKind.TrackSpan,
                        property.Name),
                    Id = property.Name,
                    Name = property.Name,
                    Kind = OperationKind.TrackSpan,
                    Position = position,
                    HasPosition = hasPosition,
                    Detail = SpanDetail(span),
                });
            }
        }

        private void DiscoverFuseOperations()
        {
            var operations = _document["operations"] as JObject;
            if (operations == null)
                return;
            var industries = operations["industries"] as JObject;
            if (industries != null)
            {
                foreach (var property in industries.Properties())
                {
                    var industry = property.Value as JObject;
                    if (industry == null)
                        continue;
                    var position = ReadOperationVector(
                        industry["position"]);
                    var hasPosition = industry["position"] is JObject;
                    AddIndustryAndComponents(
                        property.Name,
                        industry,
                        ReadOperationString(
                            industry,
                            "areaId",
                            string.Empty),
                        position,
                        hasPosition,
                        true);
                }
            }
            DiscoverSimplePositionedDictionary(
                operations["loaders"] as JObject,
                OperationKind.PhysicalLoader,
                "prefab");
            DiscoverSimplePositionedDictionary(
                operations["stations"] as JObject,
                OperationKind.StationAgent,
                "prefab");
            var turntables = operations["turntables"] as JObject;
            if (turntables != null)
            {
                foreach (var property in turntables.Properties())
                {
                    var turntable = property.Value as JObject;
                    if (turntable == null)
                        continue;
                    var radius = ReadOperationFloat(
                        turntable["radius"],
                        15f);
                    var stalls = ReadOperationInt(
                        turntable["roundhouse"]?["stalls"],
                        0);
                    AddOperation(new OperationInfo
                    {
                        Key = OperationKey(
                            OperationKind.Turntable,
                            property.Name),
                        Id = property.Name,
                        Name = property.Name,
                        Kind = OperationKind.Turntable,
                        Position = ReadOperationVector(
                            turntable["position"]),
                        Rotation = ReadOperationVector(
                            turntable["rotation"]),
                        HasPosition =
                            turntable["position"] is JObject,
                        Radius = radius,
                        Detail = radius.ToString(
                                     "0.0",
                                     CultureInfo.InvariantCulture)
                                 + " m radius - "
                                 + ReadOperationInt(
                                     turntable["subdivisions"],
                                     16)
                                 + " subdivisions"
                                 + (stalls > 0
                                     ? " - " + stalls
                                       + " roundhouse stalls"
                                     : string.Empty),
                    });
                }
            }
            DiscoverLoads(operations["loads"] as JObject);
        }

        private void DiscoverLegacyOperations()
        {
            if (_fuseNativeDocument)
                return;
            var areas = _document["areas"] as JObject;
            if (areas != null)
            {
                foreach (var areaProperty in areas.Properties())
                {
                    var area = areaProperty.Value as JObject;
                    var industries = area?["industries"] as JObject;
                    if (area == null || industries == null)
                        continue;
                    var areaPosition = ReadOperationVector(
                        area["position"]);
                    var areaHasPosition = area["position"] is JObject;
                    foreach (var property in industries.Properties())
                    {
                        var industry = property.Value as JObject;
                        if (industry == null)
                            continue;
                        var localPosition = ReadOperationVector(
                            industry["localPosition"]);
                        AddIndustryAndComponents(
                            property.Name,
                            industry,
                            areaProperty.Name,
                            areaPosition + localPosition,
                            areaHasPosition
                            && industry["localPosition"] is JObject,
                            false);
                    }
                }
            }
            DiscoverLoads(_document["loads"] as JObject);

            var splineys = _document["splineys"] as JObject;
            if (splineys == null)
                return;
            foreach (var property in splineys.Properties())
            {
                var entry = property.Value as JObject;
                if (entry == null)
                    continue;
                var handler = ReadOperationString(
                    entry,
                    "handler",
                    ReadOperationString(
                        entry,
                        "Handler",
                        string.Empty));
                OperationKind kind;
                if (handler.IndexOf(
                        "TurntableBuilder",
                        StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    kind = OperationKind.Turntable;
                }
                else if (handler.IndexOf(
                             "LoaderBuilder",
                             StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    kind = OperationKind.PhysicalLoader;
                }
                else
                {
                    continue;
                }
                var positionToken =
                    entry["position"] ?? entry["Position"];
                var rotationToken =
                    entry["rotation"] ?? entry["Rotation"];
                AddOperation(new OperationInfo
                {
                    Key = OperationKey(kind, property.Name),
                    Id = property.Name,
                    Name = property.Name,
                    Kind = kind,
                    Type = handler,
                    Position = ReadOperationVector(
                        positionToken),
                    Rotation = ReadOperationVector(
                        rotationToken),
                    HasPosition = positionToken is JObject,
                    Radius = kind == OperationKind.Turntable
                        ? 15f
                        : 1.5f,
                    Detail = kind == OperationKind.Turntable
                        ? "Legacy Alina TurntableBuilder - "
                          + ReadOperationInt(
                              entry["RoundhouseStalls"],
                              0)
                          + " roundhouse stalls"
                        : ReadOperationString(
                            entry,
                            "Prefab",
                            "Legacy physical loader"),
                });
            }
        }

        private void AddIndustryAndComponents(
            string id,
            JObject industry,
            string areaId,
            Vector3 position,
            bool hasPosition,
            bool fuse)
        {
            AddOperation(new OperationInfo
            {
                Key = OperationKey(
                    OperationKind.Industry,
                    id),
                Id = id,
                Name = ReadOperationString(
                    industry,
                    "name",
                    id),
                OwnerId = areaId,
                Kind = OperationKind.Industry,
                Position = position,
                Rotation = ReadOperationVector(
                    industry["rotation"]),
                HasPosition = hasPosition,
                Detail = "Town: "
                         + (string.IsNullOrWhiteSpace(areaId)
                             ? "(not assigned)"
                             : areaId),
            });
            var components = industry["components"] as JObject;
            if (components == null)
                return;
            foreach (var property in components.Properties())
            {
                var component = property.Value as JObject;
                if (component == null)
                    continue;
                var type = ReadOperationString(
                    component,
                    "type",
                    "custom");
                var kind = ClassifyComponent(type);
                var spans = component[
                    fuse ? "trackSpanIds" : "trackSpans"] as JArray;
                AddOperation(new OperationInfo
                {
                    Key = OperationKey(
                        kind,
                        id + "/" + property.Name),
                    Id = property.Name,
                    Name = ReadOperationString(
                        component,
                        "name",
                        property.Name),
                    OwnerId = id,
                    Type = type,
                    Kind = kind,
                    HasPosition = false,
                    Detail = "Industry: " + id
                             + (spans == null || spans.Count == 0
                                 ? string.Empty
                                 : " - span "
                                   + string.Join(
                                       ", ",
                                       spans.Values<string>())),
                });
            }
        }

        private void DiscoverSimplePositionedDictionary(
            JObject values,
            OperationKind kind,
            string detailProperty)
        {
            if (values == null)
                return;
            foreach (var property in values.Properties())
            {
                var value = property.Value as JObject;
                if (value == null)
                    continue;
                AddOperation(new OperationInfo
                {
                    Key = OperationKey(kind, property.Name),
                    Id = property.Name,
                    Name = property.Name,
                    OwnerId = ReadOperationString(
                        value,
                        kind == OperationKind.StationAgent
                            ? "passengerStopId"
                            : "industryId",
                        string.Empty),
                    Kind = kind,
                    Position = ReadOperationVector(
                        value["position"]),
                    Rotation = ReadOperationVector(
                        value["rotation"]),
                    HasPosition = value["position"] is JObject,
                    Radius = 1.5f,
                    Detail = ReadOperationString(
                        value,
                        detailProperty,
                        string.Empty),
                });
            }
        }

        private void DiscoverLoads(JObject loads)
        {
            if (loads == null)
                return;
            foreach (var property in loads.Properties())
            {
                var load = property.Value as JObject;
                if (load == null)
                    continue;
                AddOperation(new OperationInfo
                {
                    Key = OperationKey(
                        OperationKind.Commodity,
                        property.Name),
                    Id = property.Name,
                    Name = ReadOperationString(
                        load,
                        "name",
                        ReadOperationString(
                            load,
                            "description",
                            property.Name)),
                    Kind = OperationKind.Commodity,
                    Detail = ReadOperationString(
                        load,
                        "units",
                        "Commodity"),
                });
            }
        }

        private void AddOperation(OperationInfo item)
        {
            if (item == null
                || string.IsNullOrWhiteSpace(item.Id)
                || _operations.Any(existing => string.Equals(
                    existing.Key,
                    item.Key,
                    StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }
            _operations.Add(item);
        }

        private Vector3 SpanPosition(
            JObject span,
            out bool hasPosition)
        {
            var segmentIds = new[]
                {
                    span["upper"]?["segmentId"]?.Value<string>(),
                    span["lower"]?["segmentId"]?.Value<string>(),
                }
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var positions = new List<Vector3>();
            foreach (var segmentId in segmentIds)
            {
                var segment = _graph?.GetSegment(segmentId);
                if (segment?.a == null || segment.b == null)
                    continue;
                positions.Add(
                    (segment.a.transform.localPosition
                     + segment.b.transform.localPosition) * 0.5f);
            }
            hasPosition = positions.Count > 0;
            return hasPosition
                ? positions.Aggregate(Vector3.zero, (sum, item) => sum + item)
                  / positions.Count
                : Vector3.zero;
        }

        private static string SpanDetail(JObject span)
        {
            var upper = span["upper"]?["segmentId"]?.Value<string>();
            var lower = span["lower"]?["segmentId"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(upper))
                return "TrackSpan";
            return string.Equals(
                    upper,
                    lower,
                    StringComparison.OrdinalIgnoreCase)
                ? "Segment: " + upper
                : "Segments: " + upper + " to " + lower;
        }

        private void RebuildOperationOverlays()
        {
            EnsureOperationOverlayRoot();
            var visibleKeys = new HashSet<string>(
                _operations
                    .Where(ShouldHaveOperationOverlay)
                    .Select(item => item.Key),
                StringComparer.OrdinalIgnoreCase);
            foreach (var stale in _operationOverlays.Keys
                         .Where(key => !visibleKeys.Contains(key))
                         .ToArray())
            {
                var overlay = _operationOverlays[stale];
                _operationOverlays.Remove(stale);
                if (overlay != null)
                    UnityEngine.Object.Destroy(overlay.gameObject);
            }
            foreach (var item in _operations.Where(
                         ShouldHaveOperationOverlay))
            {
                if (!_operationOverlays.TryGetValue(
                        item.Key,
                        out var overlay)
                    || overlay == null)
                {
                    var child = new GameObject(
                        "TileEditorOperation-" + item.Id);
                    child.transform.SetParent(
                        _operationOverlayRoot.transform,
                        false);
                    overlay =
                        child.AddComponent<TileEditorOperationOverlay>();
                    _operationOverlays[item.Key] = overlay;
                }
                overlay.Initialize(this, item);
            }
        }

        private void EnsureOperationOverlayRoot()
        {
            if (_graph == null)
                return;
            if (_operationOverlayRoot == null)
            {
                _operationOverlayRoot = new GameObject(
                    "TileEditorOperationOverlays");
            }
            if (_operationOverlayRoot.transform.parent
                != _graph.transform)
            {
                _operationOverlayRoot.transform.SetParent(
                    _graph.transform,
                    false);
            }
            _operationOverlayRoot.transform.localPosition =
                Vector3.zero;
            _operationOverlayRoot.transform.localRotation =
                Quaternion.identity;
            _operationOverlayRoot.transform.localScale =
                Vector3.one;
        }

        private void SetOperationOverlaysVisible(bool visible)
        {
            foreach (var overlay in _operationOverlays.Values)
                overlay?.SetOverlayVisible(visible);
        }

        private void RefreshOperationOverlayColors()
        {
            foreach (var overlay in _operationOverlays.Values)
                overlay?.Refresh();
        }

        private void DisposeOperationOverlays()
        {
            foreach (var overlay in _operationOverlays.Values)
            {
                if (overlay != null)
                    UnityEngine.Object.Destroy(overlay.gameObject);
            }
            _operationOverlays.Clear();
            if (_operationOverlayRoot != null)
            {
                UnityEngine.Object.Destroy(_operationOverlayRoot);
                _operationOverlayRoot = null;
            }
        }

        private void DeleteOperationToken(OperationInfo selected)
        {
            switch (selected.Kind)
            {
                case OperationKind.Town:
                    {
                        var areas = _fuseNativeDocument
                            ? _document["tracks"]?["areas"] as JObject
                            : _document["areas"] as JObject;
                        RemoveOperationProperty(
                            areas,
                            selected.Id,
                            !_fuseNativeDocument);
                        break;
                    }
                case OperationKind.TrackSpan:
                    RemoveOperationProperty(
                        _document["tracks"]?["spans"] as JObject,
                        selected.Id,
                        !_fuseNativeDocument);
                    break;
                case OperationKind.Industry:
                    RemoveIndustryObject(selected.Id);
                    break;
                case OperationKind.PhysicalLoader:
                    RemoveOperationProperty(
                        _fuseNativeDocument
                            ? _document["operations"]?["loaders"]
                                as JObject
                            : _document["splineys"] as JObject,
                        selected.Id,
                        !_fuseNativeDocument);
                    break;
                case OperationKind.StationAgent:
                    RemoveOperationProperty(
                        _document["operations"]?["stations"]
                            as JObject,
                        selected.Id,
                        false);
                    break;
                case OperationKind.Turntable:
                    RemoveOperationProperty(
                        _fuseNativeDocument
                            ? _document["operations"]?["turntables"]
                                as JObject
                            : _document["splineys"] as JObject,
                        selected.Id,
                        !_fuseNativeDocument);
                    break;
                default:
                    RemoveIndustryComponent(
                        selected.OwnerId,
                        selected.Id);
                    break;
            }
        }

        private JObject FindIndustryObject(string industryId)
        {
            if (_fuseNativeDocument)
            {
                var industry = _document["operations"]?["industries"]?[
                    industryId] as JObject;
                return industry ?? throw new InvalidOperationException(
                    "Industry " + industryId
                    + " is not defined in this FUSE layer.");
            }
            var areas = _document["areas"] as JObject;
            if (areas != null)
            {
                foreach (var area in areas.Properties())
                {
                    var industry =
                        area.Value?["industries"]?[industryId]
                        as JObject;
                    if (industry != null)
                        return industry;
                }
            }
            throw new InvalidOperationException(
                "Industry " + industryId
                + " is not defined in this legacy layer.");
        }

        private void RemoveIndustryObject(string industryId)
        {
            if (_fuseNativeDocument)
            {
                RemoveOperationProperty(
                    _document["operations"]?["industries"]
                        as JObject,
                    industryId,
                    false);
                return;
            }
            var areas = _document["areas"] as JObject;
            if (areas == null)
                return;
            foreach (var area in areas.Properties())
            {
                var industries =
                    area.Value?["industries"] as JObject;
                if (industries?.Property(
                        industryId,
                        StringComparison.OrdinalIgnoreCase) == null)
                {
                    continue;
                }
                RemoveOperationProperty(
                    industries,
                    industryId,
                    true);
                return;
            }
        }

        private void RemoveIndustryComponent(
            string industryId,
            string componentId)
        {
            var industry = FindIndustryObject(industryId);
            RemoveOperationProperty(
                industry["components"] as JObject,
                componentId,
                !_fuseNativeDocument);
        }

        private static void RemoveOperationProperty(
            JObject values,
            string id,
            bool legacyNullRemoval)
        {
            if (values == null)
                return;
            var property = values.Property(
                id,
                StringComparison.OrdinalIgnoreCase);
            if (property == null)
                return;
            if (legacyNullRemoval)
                property.Value = JValue.CreateNull();
            else
                property.Remove();
        }

        private static bool ShouldHaveOperationOverlay(
            OperationInfo item)
        {
            return item != null
                   && item.HasPosition
                   && item.Kind != OperationKind.Commodity
                   && item.Kind != OperationKind.PassengerStop
                   && item.Kind != OperationKind.RailLoader
                   && item.Kind != OperationKind.RailUnloader
                   && item.Kind != OperationKind.RepairTrack
                   && item.Kind != OperationKind.TeamTrack
                   && item.Kind != OperationKind.Interchange
                   && item.Kind != OperationKind.Progression
                   && item.Kind != OperationKind.CustomComponent;
        }

        private static bool OperationMatchesCategory(
            OperationInfo item,
            string category)
        {
            if (item == null
                || string.IsNullOrWhiteSpace(category)
                || string.Equals(
                    category,
                    "All",
                    StringComparison.OrdinalIgnoreCase))
            {
                return item != null;
            }
            switch (category)
            {
                case "Towns":
                    return item.Kind == OperationKind.Town;
                case "Spans":
                    return item.Kind == OperationKind.TrackSpan;
                case "Industries":
                    return item.Kind == OperationKind.Industry
                           || item.Kind == OperationKind.RailLoader
                           || item.Kind == OperationKind.RailUnloader
                           || item.Kind == OperationKind.TeamTrack
                           || item.Kind == OperationKind.Interchange
                           || item.Kind == OperationKind.Progression
                           || item.Kind
                           == OperationKind.CustomComponent;
                case "Passenger":
                    return item.Kind == OperationKind.PassengerStop
                           || item.Kind == OperationKind.StationAgent;
                case "Facilities":
                    return item.Kind == OperationKind.PhysicalLoader
                           || item.Kind == OperationKind.RepairTrack
                           || item.Kind == OperationKind.RailLoader
                           || item.Kind == OperationKind.RailUnloader;
                case "Turntables":
                    return item.Kind == OperationKind.Turntable;
                default:
                    return true;
            }
        }

        private static OperationKind ClassifyComponent(string type)
        {
            type = type ?? string.Empty;
            if (type.IndexOf(
                    "passenger",
                    StringComparison.OrdinalIgnoreCase) >= 0
                || type.IndexOf(
                    "paxstation",
                    StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return OperationKind.PassengerStop;
            }
            if (type.IndexOf(
                    "repair",
                    StringComparison.OrdinalIgnoreCase) >= 0)
                return OperationKind.RepairTrack;
            if (type.IndexOf(
                    "teamtrack",
                    StringComparison.OrdinalIgnoreCase) >= 0)
                return OperationKind.TeamTrack;
            if (type.IndexOf(
                    "interchange",
                    StringComparison.OrdinalIgnoreCase) >= 0)
                return OperationKind.Interchange;
            if (type.IndexOf(
                    "progression",
                    StringComparison.OrdinalIgnoreCase) >= 0)
                return OperationKind.Progression;
            if (type.IndexOf(
                    "unloader",
                    StringComparison.OrdinalIgnoreCase) >= 0)
                return OperationKind.RailUnloader;
            if (type.IndexOf(
                    "loader",
                    StringComparison.OrdinalIgnoreCase) >= 0)
                return OperationKind.RailLoader;
            return OperationKind.CustomComponent;
        }

        private string OperationComponentType(
            string profile,
            string customType)
        {
            var fuse = _fuseNativeDocument;
            switch ((profile ?? string.Empty).Trim())
            {
                case "Receives":
                    return fuse
                        ? "unloader"
                        : "Model.Ops.IndustryUnloader";
                case "Ships":
                    return fuse
                        ? "loader"
                        : "Model.Ops.IndustryLoader";
                case "Formula":
                    return fuse
                        ? "formulaic"
                        : "Model.Ops.FormulaicIndustryComponent";
                case "Passenger":
                    return fuse
                        ? "passengerStop"
                        : "AlinasMapMod.PaxStationComponent";
                case "Repair":
                    return fuse
                        ? "repairTrack"
                        : "Model.Ops.RepairTrack";
                case "Team Track":
                    return fuse
                        ? "teamTrack"
                        : "Model.Ops.TeamTrack";
                case "Interchange":
                    return fuse
                        ? "interchange"
                        : "Model.Ops.Interchange";
                case "Interchanged Loader":
                    return fuse
                        ? "interchangedLoader"
                        : "Model.Ops.InterchangedIndustryLoader";
                case "Interchanged Unloader":
                    return fuse
                        ? "interchangedUnloader"
                        : "Model.Ops.InterchangedIndustryUnloader";
                case "Progression":
                    return fuse
                        ? "progression"
                        : "Model.Ops.ProgressionIndustryComponent";
                default:
                    return RequireOperationText(
                        customType,
                        "custom component type");
            }
        }

        private static void ValidateIndustryComponentOptions(
            string profile,
            IndustryComponentOptions options)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));
            if (string.Equals(
                    profile,
                    "Receives",
                    StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    profile,
                    "Ships",
                    StringComparison.OrdinalIgnoreCase))
            {
                ValidateFiniteNonNegative(
                    options.MaxStorage,
                    "maximum storage");
                ValidateFiniteNonNegative(
                    options.CarTransferRate,
                    "car transfer rate");
                if (float.IsNaN(options.StorageChangeRate)
                    || float.IsInfinity(
                        options.StorageChangeRate))
                {
                    throw new InvalidOperationException(
                        "Storage change rate must be a valid number.");
                }
            }
            if (string.Equals(
                    profile,
                    "Team Track",
                    StringComparison.OrdinalIgnoreCase))
            {
                ValidateFiniteNonNegative(
                    options.IdealCars,
                    "ideal cars");
            }
            if (string.Equals(
                    profile,
                    "Passenger",
                    StringComparison.OrdinalIgnoreCase)
                && options.BasePopulation < 0)
            {
                throw new InvalidOperationException(
                    "Passenger population cannot be negative.");
            }
            ValidateOptionalHour(
                options.NotBeforeHour,
                "not-before hour");
            ValidateOptionalHour(
                options.NotAfterHour,
                "not-after hour");
            if (options.FillPercentage.HasValue
                && (options.FillPercentage.Value < 0f
                    || options.FillPercentage.Value > 1f))
            {
                throw new InvalidOperationException(
                    "Fill percentage must be between 0 and 1.");
            }
        }

        private void AddOptionalIndustryComponentFields(
            JObject entry,
            IndustryComponentOptions options)
        {
            if (options.CostPerUnit.HasValue)
                entry["costPerUnit"] = options.CostPerUnit.Value;
            if (options.NotBeforeHour.HasValue)
                entry["notBeforeHour"] =
                    options.NotBeforeHour.Value;
            if (options.NotAfterHour.HasValue)
                entry["notAfterHour"] =
                    options.NotAfterHour.Value;
            if (options.FillPercentage.HasValue)
                entry["fillPercentage"] =
                    options.FillPercentage.Value;
            var bookReasons = ParseOperationIdList(
                options.BookReasons);
            if (bookReasons.Length > 0)
                entry["bookReasons"] = new JArray(bookReasons);
            if (!string.IsNullOrWhiteSpace(options.Title))
                entry["title"] = options.Title.Trim();
            if (!string.IsNullOrWhiteSpace(
                    options.CustomFieldsJson))
            {
                JObject custom;
                try
                {
                    custom = JObject.Parse(
                        options.CustomFieldsJson);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        "Custom fields must be a JSON object: "
                        + ex.Message,
                        ex);
                }
                if (_fuseNativeDocument)
                {
                    entry["fields"] = custom;
                }
                else
                {
                    foreach (var property
                             in custom.Properties())
                    {
                        if (entry.Property(
                                property.Name,
                                StringComparison.OrdinalIgnoreCase)
                            != null)
                        {
                            throw new InvalidOperationException(
                                "Legacy custom field "
                                + property.Name
                                + " would overwrite a standard "
                                + "component field.");
                        }
                        entry[property.Name] =
                            property.Value.DeepClone();
                    }
                }
            }
        }

        private static JObject ParseOperationRateMap(
            string text,
            string label)
        {
            var result = new JObject();
            foreach (var entry in SplitOperationEntries(text))
            {
                var equals = entry.IndexOf('=');
                if (equals <= 0
                    || equals >= entry.Length - 1)
                {
                    throw new InvalidOperationException(
                        "Each " + label
                        + " must use load-id=amount-per-day.");
                }
                var id = entry.Substring(0, equals).Trim();
                var valueText =
                    entry.Substring(equals + 1).Trim();
                if (id.Length == 0
                    || !float.TryParse(
                        valueText,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out var value)
                    || float.IsNaN(value)
                    || float.IsInfinity(value)
                    || value < 0f)
                {
                    throw new InvalidOperationException(
                        "Invalid " + label + ": " + entry);
                }
                result[id] = value;
            }
            return result;
        }

        private static JObject ParseTeamProfiles(string text)
        {
            var result = new JObject();
            foreach (var line in (text ?? string.Empty)
                         .Replace("\r", string.Empty)
                         .Split(
                             new[] { '\n' },
                             StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split('|');
                if (parts.Length != 5)
                {
                    throw new InvalidOperationException(
                        "Each team profile must use "
                        + "id|import/export|load|days|car-filter.");
                }
                var id = parts[0].Trim();
                var direction = parts[1].Trim();
                var loadId = parts[2].Trim();
                var daysText = parts[3].Trim();
                var carFilter = parts[4].Trim();
                if (id.Length == 0
                    || loadId.Length == 0
                    || !float.TryParse(
                        daysText,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out var days)
                    || days < 0f
                    || float.IsNaN(days)
                    || float.IsInfinity(days))
                {
                    throw new InvalidOperationException(
                        "Invalid team profile: " + line);
                }
                var isExport =
                    string.Equals(
                        direction,
                        "export",
                        StringComparison.OrdinalIgnoreCase)
                    || string.Equals(
                        direction,
                        "out",
                        StringComparison.OrdinalIgnoreCase)
                    || string.Equals(
                        direction,
                        "true",
                        StringComparison.OrdinalIgnoreCase);
                if (!isExport
                    && !string.Equals(
                        direction,
                        "import",
                        StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(
                        direction,
                        "in",
                        StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(
                        direction,
                        "false",
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "Team profile direction must be import or export.");
                }
                result[id] = new JObject
                {
                    ["isExport"] = isExport,
                    ["loadId"] = loadId,
                    ["loadingTimeDays"] = days,
                    ["carTypeFilter"] = carFilter,
                };
            }
            return result;
        }

        private static string[] ParseOperationIdList(string text)
        {
            return (text ?? string.Empty)
                .Split(
                    new[] { ',', ';', '\r', '\n' },
                    StringSplitOptions.RemoveEmptyEntries)
                .Select(value => value.Trim())
                .Where(value => value.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static IEnumerable<string> SplitOperationEntries(
            string text)
        {
            return (text ?? string.Empty)
                .Split(
                    new[] { ',', ';', '\r', '\n' },
                    StringSplitOptions.RemoveEmptyEntries)
                .Select(value => value.Trim())
                .Where(value => value.Length > 0);
        }

        private static void ValidateFiniteNonNegative(
            float value,
            string label)
        {
            if (float.IsNaN(value)
                || float.IsInfinity(value)
                || value < 0f)
            {
                throw new InvalidOperationException(
                    label + " must be zero or greater.");
            }
        }

        private static void ValidateOptionalHour(
            float? value,
            string label)
        {
            if (value.HasValue
                && (float.IsNaN(value.Value)
                    || float.IsInfinity(value.Value)
                    || value.Value < 0f
                    || value.Value > 24f))
            {
                throw new InvalidOperationException(
                    label + " must be between 0 and 24.");
            }
        }

        private void AddFacilityReceivingComponent(
            JObject components,
            string id,
            string name,
            string spanId,
            string loadId,
            string carTypeFilter)
        {
            RequireUnusedOperationId(
                components,
                id,
                "facility component");
            components[id] = new JObject
            {
                ["type"] = _fuseNativeDocument
                    ? "unloader"
                    : "Model.Ops.IndustryUnloader",
                ["name"] = name,
                [_fuseNativeDocument
                    ? "trackSpanIds"
                    : "trackSpans"] = new JArray(spanId),
                ["loadId"] = loadId,
                ["carTypeFilter"] = carTypeFilter,
                ["sharedStorage"] = true,
                ["storageChangeRate"] = 0f,
                ["maxStorage"] = 200000f,
                ["carTransferRate"] = 200000f,
                ["orderAroundEmpties"] = true,
                ["orderAroundLoaded"] = true,
            };
        }

        private JObject SpanLocation(
            Track.TrackSegment segment,
            float distanceFromStart)
        {
            var length = Mathf.Max(0.001f, segment.GetLength());
            var useEnd = distanceFromStart > length * 0.5f;
            return new JObject
            {
                ["segmentId"] = segment.id,
                ["distance"] = useEnd
                    ? Mathf.Max(0f, length - distanceFromStart)
                    : Mathf.Max(0f, distanceFromStart),
                ["end"] = _fuseNativeDocument
                    ? useEnd ? "B" : "A"
                    : useEnd ? "End" : "Start",
            };
        }

        private bool SegmentsShareConnectedGraph(
            Track.TrackSegment start,
            Track.TrackSegment target)
        {
            var visited = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<Track.TrackSegment>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var segment = queue.Dequeue();
                if (segment == null
                    || !visited.Add(segment.id))
                {
                    continue;
                }
                if (segment == target)
                    return true;
                foreach (var node in new[] { segment.a, segment.b })
                {
                    if (node == null)
                        continue;
                    foreach (var connected in _graph.SegmentsConnectedTo(node))
                    {
                        if (connected != null
                            && !visited.Contains(connected.id))
                        {
                            queue.Enqueue(connected);
                        }
                    }
                }
            }
            return false;
        }

        private static void ValidateSpanDistance(
            float distance,
            float length,
            string label)
        {
            if (float.IsNaN(distance)
                || float.IsInfinity(distance)
                || distance < 0f
                || distance > length + 0.001f)
            {
                throw new InvalidOperationException(
                    "The " + label + " distance must be between 0 and "
                    + length.ToString(
                        "0.0",
                        CultureInfo.InvariantCulture)
                    + " m.");
            }
        }

        private static string PassengerCodeFromId(string id)
        {
            var letters = new string((id ?? string.Empty)
                .Where(char.IsLetterOrDigit)
                .Select(char.ToUpperInvariant)
                .Take(3)
                .ToArray());
            return letters.Length == 0 ? "NEW" : letters;
        }

        private static string NormalizeOperationId(
            string value,
            string fallback)
        {
            value = (value ?? string.Empty).Trim();
            var chars = value.Where(character =>
                    char.IsLetterOrDigit(character)
                    || character == '_'
                    || character == '-'
                    || character == ':'
                    || character == '.')
                .Take(80)
                .ToArray();
            var normalized = new string(chars).Trim('_', '-', ':', '.');
            if (string.IsNullOrWhiteSpace(normalized))
            {
                throw new InvalidOperationException(
                    "Enter a valid " + fallback
                    + " ID using letters, numbers, :, -, _, or .");
            }
            return normalized;
        }

        private static string RequireOperationText(
            string value,
            string label)
        {
            value = (value ?? string.Empty).Trim();
            if (value.Length == 0)
            {
                throw new InvalidOperationException(
                    "Enter a " + label + ".");
            }
            return value;
        }

        private static void ValidateOperationPosition(Vector3 position)
        {
            if (float.IsNaN(position.x)
                || float.IsNaN(position.y)
                || float.IsNaN(position.z)
                || float.IsInfinity(position.x)
                || float.IsInfinity(position.y)
                || float.IsInfinity(position.z))
            {
                throw new InvalidOperationException(
                    "The world position is invalid.");
            }
        }

        private static void RequireUnusedOperationId(
            JObject values,
            string id,
            string label)
        {
            if (values.Property(
                    id,
                    StringComparison.OrdinalIgnoreCase) != null)
            {
                throw new InvalidOperationException(
                    "A " + label + " named " + id
                    + " already exists in this layer.");
            }
        }

        private static JObject EnsureOperationObject(
            JObject parent,
            string name)
        {
            var value = parent[name] as JObject;
            if (value != null)
                return value;
            value = new JObject();
            parent[name] = value;
            return value;
        }

        private static string ReadOperationString(
            JObject value,
            string property,
            string fallback)
        {
            return value?[property]?.Value<string>()
                   ?? fallback
                   ?? string.Empty;
        }

        private static float ReadOperationFloat(
            JToken token,
            float fallback)
        {
            return token != null
                   && float.TryParse(
                       token.ToString(),
                       NumberStyles.Float,
                       CultureInfo.InvariantCulture,
                       out var value)
                ? value
                : fallback;
        }

        private static int ReadOperationInt(
            JToken token,
            int fallback)
        {
            return token != null
                   && int.TryParse(
                       token.ToString(),
                       NumberStyles.Integer,
                       CultureInfo.InvariantCulture,
                       out var value)
                ? value
                : fallback;
        }

        private static Vector3 ReadOperationVector(JToken token)
        {
            return new Vector3(
                ReadOperationFloat(token?["x"], 0f),
                ReadOperationFloat(token?["y"], 0f),
                ReadOperationFloat(token?["z"], 0f));
        }

        private static string OperationKey(
            OperationKind kind,
            string id)
        {
            return kind + ":" + (id ?? string.Empty);
        }

        internal static string OperationKindLabel(OperationKind kind)
        {
            switch (kind)
            {
                case OperationKind.Town:
                    return "TOWN";
                case OperationKind.TrackSpan:
                    return "SPAN";
                case OperationKind.Industry:
                    return "INDUSTRY";
                case OperationKind.PassengerStop:
                    return "PASSENGER";
                case OperationKind.RailLoader:
                    return "SHIPS";
                case OperationKind.RailUnloader:
                    return "RECEIVES";
                case OperationKind.RepairTrack:
                    return "REPAIR";
                case OperationKind.TeamTrack:
                    return "TEAM";
                case OperationKind.Interchange:
                    return "INTERCHANGE";
                case OperationKind.Progression:
                    return "PROGRESSION";
                case OperationKind.Commodity:
                    return "LOAD";
                case OperationKind.PhysicalLoader:
                    return "SERVICE";
                case OperationKind.StationAgent:
                    return "STATION";
                case OperationKind.Turntable:
                    return "TURNTABLE";
                default:
                    return "CUSTOM";
            }
        }

        internal static Color OperationColor(OperationKind kind)
        {
            switch (kind)
            {
                case OperationKind.Town:
                    return new Color(0.25f, 0.72f, 1f);
                case OperationKind.TrackSpan:
                    return new Color(1f, 0.85f, 0.15f);
                case OperationKind.Industry:
                    return new Color(1f, 0.55f, 0.12f);
                case OperationKind.PassengerStop:
                case OperationKind.StationAgent:
                    return new Color(0.35f, 1f, 0.48f);
                case OperationKind.PhysicalLoader:
                    return new Color(0.12f, 0.95f, 0.95f);
                case OperationKind.Turntable:
                    return new Color(0.95f, 0.28f, 1f);
                default:
                    return new Color(0.95f, 0.72f, 0.22f);
            }
        }
    }

    internal sealed class TileEditorOperationOverlay
        : MonoBehaviour, IPickable
    {
        private const int CircleSegments = 48;
        private TileEditorGraphSession _session;
        private TileEditorGraphSession.OperationInfo _item;
        private LineRenderer _line;
        private BoxCollider _collider;

        public float MaxPickDistance => 900f;
        public int Priority => 16;
        public PickableActivationFilter ActivationFilter =>
            PickableActivationFilter.Any;

        public TooltipInfo TooltipInfo
        {
            get
            {
                if (_item == null)
                    return TooltipInfo.Empty;
                return new TooltipInfo(
                    "Tile Editor "
                    + TileEditorGraphSession.OperationKindLabel(
                        _item.Kind)
                    + " " + _item.Id,
                    _item.Name + "\n" + _item.Detail
                    + "\nPosition: "
                    + _item.Position.ToString("F2"));
            }
        }

        internal void Initialize(
            TileEditorGraphSession session,
            TileEditorGraphSession.OperationInfo item)
        {
            _session = session;
            _item = item;
            BuildVisual();
            Refresh();
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

        internal void Refresh()
        {
            if (_line == null || _item == null || _session == null)
                return;
            TileEditorOverlayVisuals.SetColor(
                _line,
                _session.IsSelectedOperation(_item.Key)
                    ? Color.white
                    : TileEditorGraphSession.OperationColor(
                        _item.Kind));
        }

        public void Activate(PickableActivateEvent evt)
        {
            if (evt.Activation == PickableActivation.Primary)
                _session?.SelectOperation(_item?.Key);
        }

        public void Deactivate()
        {
        }

        private void BuildVisual()
        {
            if (_item == null)
                return;
            gameObject.layer = Layers.Clickable;
            transform.localPosition = _item.Position;
            transform.localEulerAngles = _item.Rotation;
            transform.localScale = Vector3.one;
            _line = GetComponent<LineRenderer>()
                    ?? gameObject.AddComponent<LineRenderer>();
            _line.sharedMaterial =
                TileEditorOverlayVisuals.SharedLineMaterial;
            _line.useWorldSpace = false;
            _line.loop = false;
            _line.startWidth = 0.12f;
            _line.endWidth = 0.12f;
            if (_item.Kind == TileEditorGraphSession.OperationKind.Town
                || _item.Kind
                == TileEditorGraphSession.OperationKind.Turntable)
            {
                var radius = Mathf.Max(
                    1.5f,
                    _item.Radius);
                _line.loop = true;
                _line.positionCount = CircleSegments;
                for (var index = 0; index < CircleSegments; index++)
                {
                    var angle = index
                                / (float)CircleSegments
                                * Mathf.PI * 2f;
                    _line.SetPosition(
                        index,
                        new Vector3(
                            Mathf.Cos(angle) * radius,
                            0.25f,
                            Mathf.Sin(angle) * radius));
                }
                _collider = GetComponent<BoxCollider>()
                            ?? gameObject.AddComponent<BoxCollider>();
                _collider.center = new Vector3(0f, 0.5f, 0f);
                _collider.size = new Vector3(
                    Mathf.Max(2f, radius * 0.35f),
                    2f,
                    Mathf.Max(2f, radius * 0.35f));
            }
            else
            {
                _line.positionCount = 9;
                _line.SetPositions(new[]
                {
                    new Vector3(0f, 0.15f, 1.1f),
                    new Vector3(0.8f, 0.15f, 0f),
                    new Vector3(0f, 0.15f, -0.8f),
                    new Vector3(-0.8f, 0.15f, 0f),
                    new Vector3(0f, 0.15f, 1.1f),
                    new Vector3(0f, 3f, 0f),
                    new Vector3(0.4f, 2.35f, 0f),
                    new Vector3(-0.4f, 2.35f, 0f),
                    new Vector3(0f, 3f, 0f),
                });
                _collider = GetComponent<BoxCollider>()
                            ?? gameObject.AddComponent<BoxCollider>();
                _collider.center = new Vector3(0f, 1.2f, 0f);
                _collider.size = new Vector3(1.8f, 3f, 1.8f);
            }
        }
    }
}
