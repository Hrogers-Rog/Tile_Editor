using System;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace Hrogers.TileEditorBridge
{
    internal sealed partial class TileEditorBridgePanel
    {
        private enum OperationsTool
        {
            Towns,
            Spans,
            Industries,
            Passenger,
            Facilities,
            Markers,
            Signals,
            All,
        }

        private OperationsTool _operationsTool =
            OperationsTool.Industries;
        private string _operationsSearch = string.Empty;
        private int _operationsPage;
        private string _opsTownId = "town:new";
        private string _opsTownName = "New Town";
        private string _opsTownRadius = "350";
        private string _opsSpanId = "span:new";
        private string _opsSpanStartDistance = "0";
        private string _opsSpanEndDistance = "0";
        private string _opsMarkedSpanStartSegment = string.Empty;
        private float _opsMarkedSpanStartDistance;
        private string _opsIndustryId = "industry:new";
        private string _opsIndustryName = "New Industry";
        private string _opsAreaId = "town:new";
        private bool _opsUsesContract = true;
        private int _opsComponentProfile;
        private string _opsComponentId = "dock";
        private string _opsComponentName = "Industry Dock";
        private string _opsComponentSpanId = "span:new";
        private string _opsComponentLoadId = "coal";
        private string _opsComponentCarFilter = "*";
        private string _opsCustomComponentType = string.Empty;
        private bool _opsSharedStorage = true;
        private string _opsStorageChangeRate = "25000";
        private string _opsMaxStorage = "100000";
        private string _opsCarTransferRate = "200000";
        private bool _opsOrderAroundEmpties = true;
        private bool _opsOrderAroundLoaded = true;
        private string _opsFormulaInputs =
            "input-load=10000";
        private string _opsFormulaOutputs =
            "output-load=10000";
        private string _opsIdealCars = "2";
        private string _opsTeamProfiles =
            "a|import|boxcar-generic|1|XM\n"
            + "b|export|lumber-dimensional|1|FM";
        private bool _opsCanOverhaul = true;
        private string _opsPassengerStopId = string.Empty;
        private string _opsPassengerCode = "NEW";
        private string _opsPassengerPopulation = "5";
        private string _opsPassengerBranch = "Main";
        private string _opsPassengerNeighbors = string.Empty;
        private string _opsOutputSpanIds = string.Empty;
        private string _opsConvertedLoadId = string.Empty;
        private bool _opsShowAdvancedComponent;
        private string _opsComponentCostPerUnit = string.Empty;
        private string _opsComponentNotBefore = string.Empty;
        private string _opsComponentNotAfter = string.Empty;
        private string _opsComponentFill = string.Empty;
        private string _opsComponentBookReasons = string.Empty;
        private string _opsComponentTitle = string.Empty;
        private string _opsCustomFieldsJson = string.Empty;
        private string _opsPhysicalLoaderId = "loader:new";
        private string _opsPhysicalLoaderPrefab =
            "vanilla://waterTower";
        private string _opsStationAgentId = "station-agent:new";
        private string _opsStationAgentPrefab =
            "empty://stationAgent";
        private string _opsTurntableId = "turntable:new";
        private string _opsTurntableRadius = "15";
        private string _opsTurntableSubdivisions = "16";
        private string _opsTurntableStalls = "0";
        private string _opsTurntableStartAngle = "-18";
        private string _opsTurntableStallAngle = "12";
        private string _opsTurntableTrackLength = "46";
        private string _opsTurntableGauge = "1.435";
        private bool _opsOperatingMetadataLoaded;
        private string _opsMarkerId = "marker:new";
        private string _opsMarkerName = "New Operating Marker";
        private string _opsMarkerType = "clearance-point";
        private string _opsMarkerCompanyId = "tn";
        private string _opsMarkerTargetId = string.Empty;
        private string _opsMarkerTargetField = string.Empty;
        private string _opsMarkerNativeIndustryId = string.Empty;
        private string _opsMarkerNativeComponentId = string.Empty;
        private string _opsMarkerNativeComponentType = string.Empty;
        private string _opsMarkerNativePassengerStopId = string.Empty;
        private string _opsMarkerServiceIds = string.Empty;
        private string _opsMarkerSpanIds = string.Empty;
        private string _opsMarkerTrackGroupIds = string.Empty;
        private string _opsMarkerAllowedCompanyIds = string.Empty;
        private string _opsMarkerRole = string.Empty;
        private string _opsMarkerDirection = string.Empty;
        private string _opsMarkerTolerance = string.Empty;
        private string _opsMarkerCapacity = string.Empty;
        private string _opsMarkerApproach = string.Empty;
        private string _opsMarkerDwell = string.Empty;
        private string _opsMarkerMaxCars = string.Empty;
        private string _opsMarkerNotes = string.Empty;
        private static readonly string[] OperatingMarkerTypes =
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
        };
        private static readonly string[] OperatingMarkerLabels =
        {
            "Crossing", "Passenger stop", "Platform clear",
            "Clearance point", "Fouling point", "Switching lead",
            "Runaround limit", "Portal", "Portal entry", "Portal exit",
            "Yard track role", "Interchange spot", "Main clear",
            "North lead", "Interchange limit", "Freight-house spot",
            "Freight-house clear", "Freight spot", "Recovery",
            "Mail spot", "Shop bay", "Roundhouse track", "Shop stores",
            "Fuel / supply", "Authority limit", "Ownership boundary",
            "Territory rights", "Caboose drop",
        };
        private string _opsTerritoryId = "territory:new";
        private string _opsTerritoryName = "New Operating Territory";
        private string _opsTerritoryOwner = "tn";
        private string _opsTerritoryGroups = string.Empty;
        private string _opsTerritoryAllowed = "tn";

        private void DrawOperationsPanel()
        {
            if (_mapEditor == null || !_mapEditor.Available)
            {
                GUILayout.Label(
                    "Railroader's live graph is not ready.",
                    _titleStyle);
                return;
            }
            if (!_mapEditor.GraphOpen)
                return;

            GUILayout.BeginHorizontal();
            GUILayout.Label(
                _mapEditor.FuseOperationsDocument
                    ? "NATIVE FUSE OPERATIONS"
                    : "RAILLOADER / STRANGE CUSTOMS OPERATIONS",
                _onlineStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(
                    "Refresh",
                    GUILayout.Width(78f),
                    GUILayout.Height(25f)))
            {
                RunGameAction(
                    "Refreshed operations discovery",
                    _mapEditor.RefreshOperations);
            }
            GUILayout.EndHorizontal();
            GUILayout.Label(
                "Layer: " + _mapEditor.GraphName
                + "  -  " + _mapEditor.Operations.Count
                + " operations entries",
                _mutedStyle);
            if (!_mapEditor.FuseOperationsDocument)
            {
                GUILayout.Label(
                    "Legacy turntables and physical service loaders are stored "
                    + "in the buildings/splineys layer. Towns and industries are "
                    + "usually stored in industries.json.",
                    _mutedStyle);
            }

            GUILayout.BeginHorizontal();
            DrawOperationsToolButton("Towns", OperationsTool.Towns);
            DrawOperationsToolButton("Spans", OperationsTool.Spans);
            DrawOperationsToolButton(
                "Industries",
                OperationsTool.Industries);
            DrawOperationsToolButton(
                "Passenger",
                OperationsTool.Passenger);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            DrawOperationsToolButton(
                "Facilities",
                OperationsTool.Facilities);
            DrawOperationsToolButton("Markers", OperationsTool.Markers);
            DrawOperationsToolButton("Signals", OperationsTool.Signals);
            DrawOperationsToolButton("All", OperationsTool.All);
            GUILayout.EndHorizontal();

            GUILayout.Space(5f);
            DrawPointerPlacementStatus();
            if (_operationsTool != OperationsTool.Signals)
            {
                DrawOperationsSearchAndList();
                DrawSelectedOperation();
            }
            GUILayout.Space(6f);

            switch (_operationsTool)
            {
                case OperationsTool.Towns:
                    DrawTownBuilder();
                    break;
                case OperationsTool.Spans:
                    DrawSpanBuilder();
                    break;
                case OperationsTool.Industries:
                    DrawIndustryBuilder();
                    break;
                case OperationsTool.Passenger:
                    DrawPassengerBuilder();
                    break;
                case OperationsTool.Facilities:
                    DrawFacilityBuilder();
                    break;
                case OperationsTool.Markers:
                    DrawOperatingMarkerBuilder();
                    break;
                case OperationsTool.Signals:
                    DrawOperationsCtcSignals();
                    break;
            }
        }

        private void DrawOperationsToolButton(
            string label,
            OperationsTool tool)
        {
            var oldColor = GUI.backgroundColor;
            if (_operationsTool == tool)
                GUI.backgroundColor = new Color(0.18f, 0.72f, 0.82f);
            if (GUILayout.Button(label, GUILayout.Height(27f)))
            {
                if (_operationsTool != tool)
                {
                    _operationsTool = tool;
                    _operationsPage = 0;
                    if (tool == OperationsTool.Markers)
                        _opsOperatingMetadataLoaded = false;
                }
            }
            GUI.backgroundColor = oldColor;
        }

        private void DrawOperationsSearchAndList()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Find", GUILayout.Width(42f));
            var search = GUILayout.TextField(
                _operationsSearch ?? string.Empty);
            if (!string.Equals(
                    search,
                    _operationsSearch,
                    StringComparison.Ordinal))
            {
                _operationsSearch = search;
                _operationsPage = 0;
            }
            GUILayout.EndHorizontal();

            const int pageSize = 10;
            var category = OperationsCategory(_operationsTool);
            var offset = _operationsPage * pageSize;
            var items = _mapEditor.SearchOperations(
                _operationsSearch,
                category,
                offset,
                pageSize,
                out var total);
            if (total == 0)
            {
                GUILayout.Label(
                    "No " + category.ToLowerInvariant()
                    + " entries were found in this layer.",
                    _mutedStyle);
            }
            foreach (var item in items)
            {
                var oldColor = GUI.backgroundColor;
                if (_mapEditor.IsSelectedOperation(item.Key))
                {
                    GUI.backgroundColor =
                        TileEditorGraphSession.OperationColor(
                            item.Kind);
                }
                if (GUILayout.Button(
                        Shorten(item.DisplayLabel, 68),
                        GUILayout.Height(25f)))
                {
                    _mapEditor.SelectOperation(item.Key);
                    UseOperationAsFormInput(item);
                }
                GUI.backgroundColor = oldColor;
            }
            if (total > pageSize)
            {
                GUILayout.BeginHorizontal();
                GUI.enabled = _operationsPage > 0;
                if (GUILayout.Button("Previous"))
                    _operationsPage--;
                GUI.enabled = true;
                GUILayout.Label(
                    (_operationsPage + 1) + " / "
                    + Mathf.CeilToInt(total / (float)pageSize),
                    GUILayout.Width(62f));
                GUI.enabled = offset + pageSize < total;
                if (GUILayout.Button("Next"))
                    _operationsPage++;
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
        }

        private void DrawSelectedOperation()
        {
            var selected = _mapEditor.SelectedOperation;
            if (selected == null)
                return;
            GUILayout.Space(4f);
            GUILayout.Label(selected.DisplayLabel, _titleStyle);
            GUILayout.Label(selected.Detail, _lineStyle);
            if (!string.IsNullOrWhiteSpace(selected.OwnerId))
            {
                GUILayout.Label(
                    "Owner: " + selected.OwnerId,
                    _mutedStyle);
            }
            if (selected.HasPosition)
            {
                GUILayout.Label(
                    "Position "
                    + selected.Position.x.ToString(
                        "0.00",
                        CultureInfo.InvariantCulture)
                    + ", "
                    + selected.Position.y.ToString(
                        "0.00",
                        CultureInfo.InvariantCulture)
                    + ", "
                    + selected.Position.z.ToString(
                        "0.00",
                        CultureInfo.InvariantCulture),
                    _mutedStyle);
            }
            GUILayout.BeginHorizontal();
            GUI.enabled = selected.HasPosition;
            if (GUILayout.Button("Show", GUILayout.Height(27f)))
            {
                RunGameAction(
                    "Centered " + selected.Id,
                    _mapEditor.ShowSelectedOperation);
            }
            GUI.enabled = selected.Kind
                          != TileEditorGraphSession.OperationKind.Commodity;
            if (GUILayout.Button(
                    "Delete...",
                    GUILayout.Height(27f)))
            {
                _deleteConfirmId = "operation:" + selected.Key;
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            if (_deleteConfirmId == "operation:" + selected.Key)
            {
                var oldColor = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.85f, 0.28f, 0.20f);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(
                        "CONFIRM DELETE " + selected.Id,
                        GUILayout.Height(29f)))
                {
                    RunGameAction(
                        _mapEditor.DeleteSelectedOperation);
                    _deleteConfirmId = string.Empty;
                }
                GUI.backgroundColor = oldColor;
                if (GUILayout.Button(
                        "Cancel",
                        GUILayout.Width(82f),
                        GUILayout.Height(29f)))
                {
                    _deleteConfirmId = string.Empty;
                }
                GUILayout.EndHorizontal();
            }
        }

        private void DrawTownBuilder()
        {
            GUILayout.Label("CREATE A TOWN / OPERATING AREA", _titleStyle);
            GUILayout.Label(
                "Place the town center with the mouse. The radius controls its "
                + "operating area and the blue world overlay.",
                _lineStyle);
            DrawTextField("Town ID", ref _opsTownId);
            DrawTextField("Display name", ref _opsTownName);
            DrawTextField("Radius (m)", ref _opsTownRadius);
            if (GUILayout.Button(
                    "PLACE TOWN WITH POINTER",
                    GUILayout.Height(34f)))
            {
                ArmPointerPlacement(
                    PointerPlacementKind.OperationsTown,
                    string.Empty,
                    false);
            }
        }

        private void DrawSpanBuilder()
        {
            GUILayout.Label("CREATE A TRACKSPAN", _titleStyle);
            GUILayout.Label(
                "Click a yellow track segment in the world, name the span, "
                + "then create it. A TrackSpan is what passenger stops, "
                + "industries, and repair facilities attach to.",
                _lineStyle);
            var segment = _mapEditor.SelectedSegment;
            GUILayout.Label(
                segment == null
                    ? "SELECT A TRACK SEGMENT"
                    : "Selected segment: " + segment.Id
                      + "  (" + segment.Length.ToString(
                          "0.0",
                          CultureInfo.InvariantCulture)
                      + " m)",
                segment == null ? _offlineStyle : _onlineStyle);
            DrawTextField("Span ID", ref _opsSpanId);
            GUI.enabled = segment != null;
            if (GUILayout.Button(
                    "USE WHOLE SELECTED SEGMENT",
                    GUILayout.Height(34f)))
            {
                RunGameAction(
                    () => _mapEditor.CreateSpanFromSelectedSegment(
                        _opsSpanId));
            }
            GUI.enabled = true;
            GUILayout.Space(6f);
            GUILayout.Label("PARTIAL SPAN", _titleStyle);
            GUILayout.Label(
                "Distances are measured along the selected segment from its A/"
                + "Start node.",
                _mutedStyle);
            DrawTextField(
                "Start distance (m)",
                ref _opsSpanStartDistance);
            DrawTextField(
                "End distance (m)",
                ref _opsSpanEndDistance);
            if (segment != null
                && GUILayout.Button(
                    "Set End to Selected Length ("
                    + segment.Length.ToString(
                        "0.0",
                        CultureInfo.InvariantCulture)
                    + " m)",
                    GUILayout.Height(27f)))
            {
                _opsSpanEndDistance = segment.Length.ToString(
                    "0.###",
                    CultureInfo.InvariantCulture);
            }
            GUI.enabled = segment != null;
            if (GUILayout.Button(
                    "CREATE PARTIAL SPAN",
                    GUILayout.Height(32f)))
            {
                RunGameAction(
                    () => _mapEditor.CreatePartialSpanOnSelectedSegment(
                        _opsSpanId,
                        ParseFloat(
                            _opsSpanStartDistance,
                            "span start distance"),
                        ParseFloat(
                            _opsSpanEndDistance,
                            "span end distance")));
            }
            GUI.enabled = true;

            GUILayout.Space(7f);
            GUILayout.Label("MULTI-SEGMENT SPAN", _titleStyle);
            GUILayout.Label(
                "Select the first segment and mark its distance. Then select "
                + "the ending segment, enter its distance, and build.",
                _mutedStyle);
            GUI.enabled = segment != null;
            if (GUILayout.Button(
                    "MARK SELECTED AS SPAN START",
                    GUILayout.Height(29f)))
            {
                try
                {
                    _opsMarkedSpanStartDistance = ParseFloat(
                        _opsSpanStartDistance,
                        "span start distance");
                    _opsMarkedSpanStartSegment = segment.Id;
                    _lastPanelMessage =
                        "Marked span start on " + segment.Id;
                }
                catch (Exception ex)
                {
                    _lastPanelMessage = ex.Message;
                }
            }
            GUI.enabled = true;
            GUILayout.Label(
                string.IsNullOrWhiteSpace(
                    _opsMarkedSpanStartSegment)
                    ? "Start: not marked"
                    : "Start: " + _opsMarkedSpanStartSegment
                      + " at "
                      + _opsMarkedSpanStartDistance.ToString(
                          "0.###",
                          CultureInfo.InvariantCulture)
                      + " m",
                string.IsNullOrWhiteSpace(
                    _opsMarkedSpanStartSegment)
                    ? _offlineStyle
                    : _onlineStyle);
            GUI.enabled = segment != null
                          && !string.IsNullOrWhiteSpace(
                              _opsMarkedSpanStartSegment);
            if (GUILayout.Button(
                    "BUILD SPAN TO SELECTED SEGMENT",
                    GUILayout.Height(34f)))
            {
                RunGameAction(
                    () =>
                    {
                        var result =
                            _mapEditor.CreateSpanBetweenSegments(
                                _opsSpanId,
                                _opsMarkedSpanStartSegment,
                                _opsMarkedSpanStartDistance,
                                segment.Id,
                                ParseFloat(
                                    _opsSpanEndDistance,
                                    "span end distance"));
                        _opsMarkedSpanStartSegment =
                            string.Empty;
                        return result;
                    });
            }
            GUI.enabled = true;
        }

        private void DrawIndustryBuilder()
        {
            GUILayout.Label("CREATE AN INDUSTRY", _titleStyle);
            GUILayout.Label(
                "Select a town above or enter its ID, then place the industry "
                + "anchor with the mouse.",
                _lineStyle);
            DrawTextField("Industry ID", ref _opsIndustryId);
            DrawTextField("Display name", ref _opsIndustryName);
            DrawTextField("Town / area ID", ref _opsAreaId);
            _opsUsesContract = GUILayout.Toggle(
                _opsUsesContract,
                " Uses contract");
            if (GUILayout.Button(
                    "PLACE INDUSTRY WITH POINTER",
                    GUILayout.Height(34f)))
            {
                ArmPointerPlacement(
                    PointerPlacementKind.OperationsIndustry,
                    string.Empty,
                    false);
            }
            GUILayout.Space(6f);
            GUILayout.Label("ADD RAIL OPERATIONS", _titleStyle);
            DrawComponentBuilder(false);
        }

        private void DrawPassengerBuilder()
        {
            GUILayout.Label("PASSENGER STATION", _titleStyle);
            GUILayout.Label(
                "Choose an industry/depot and TrackSpan. This creates a "
                + "passenger-stop component with safe defaults; timetable code, "
                + "population, neighbors, and branch details remain editable "
                + "in the saved JSON until the advanced form is added.",
                _lineStyle);
            DrawTextField("Depot industry ID", ref _opsIndustryId);
            DrawTextField("Component ID", ref _opsComponentId);
            DrawTextField("Station name", ref _opsComponentName);
            DrawTextField("Passenger span ID", ref _opsComponentSpanId);
            DrawTextField(
                "Passenger stop ID",
                ref _opsPassengerStopId);
            DrawTextField(
                "Timetable code",
                ref _opsPassengerCode);
            DrawTextField(
                "Base population",
                ref _opsPassengerPopulation);
            DrawTextField(
                "Branch",
                ref _opsPassengerBranch);
            DrawTextField(
                "Neighbor stop IDs",
                ref _opsPassengerNeighbors);
            _opsSharedStorage = GUILayout.Toggle(
                _opsSharedStorage,
                " Shared storage");
            if (GUILayout.Button(
                    "ADD PASSENGER STOP",
                    GUILayout.Height(34f)))
            {
                RunGameAction(
                    () => _mapEditor.AddIndustryComponent(
                        BuildIndustryComponentOptions(
                            "Passenger")));
            }
            GUILayout.Space(6f);
            GUILayout.Label("OPTIONAL STATION AGENT", _titleStyle);
            DrawTextField(
                "Station agent ID",
                ref _opsStationAgentId);
            DrawTextField(
                "Agent prefab",
                ref _opsStationAgentPrefab);
            GUI.enabled = _mapEditor.FuseOperationsDocument;
            if (GUILayout.Button(
                    "PLACE STATION AGENT WITH POINTER",
                    GUILayout.Height(32f)))
            {
                ArmPointerPlacement(
                    PointerPlacementKind.OperationsStationAgent,
                    string.Empty,
                    false);
            }
            GUI.enabled = true;
            if (!_mapEditor.FuseOperationsDocument)
            {
                GUILayout.Label(
                    "The passenger stop works in RailLoader, but the separate "
                    + "station-agent object currently uses native FUSE.",
                    _mutedStyle);
            }
        }

        private void DrawFacilityBuilder()
        {
            GUILayout.Label("ENGINE & FREIGHT FACILITIES", _titleStyle);
            GUILayout.Label(
                "Rail delivery components handle cars on a TrackSpan. Physical "
                + "water towers, coal chutes, sand towers, and fuel stands are "
                + "placed separately below.",
                _lineStyle);
            DrawComponentBuilder(true);
            GUILayout.Space(7f);
            GUILayout.Label("ENGINE TERMINAL PROFILES", _titleStyle);
            GUILayout.Label(
                "Creates coal and/or diesel receiving plus an overhaul-capable "
                + "repair track on the selected TrackSpan.",
                _mutedStyle);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Steam"))
            {
                RunGameAction(
                    () => _mapEditor.AddEngineFacilityProfile(
                        _opsIndustryId,
                        _opsComponentSpanId,
                        "Steam"));
            }
            if (GUILayout.Button("Diesel"))
            {
                RunGameAction(
                    () => _mapEditor.AddEngineFacilityProfile(
                        _opsIndustryId,
                        _opsComponentSpanId,
                        "Diesel"));
            }
            if (GUILayout.Button("Combined"))
            {
                RunGameAction(
                    () => _mapEditor.AddEngineFacilityProfile(
                        _opsIndustryId,
                        _opsComponentSpanId,
                        "Combined"));
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(7f);
            GUILayout.Label("PLACE A PHYSICAL SERVICE OBJECT", _titleStyle);
            DrawTextField("Object ID", ref _opsPhysicalLoaderId);
            DrawTextField("Prefab", ref _opsPhysicalLoaderPrefab);
            DrawTextField("Owning industry", ref _opsIndustryId);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Water Tower"))
                _opsPhysicalLoaderPrefab = "vanilla://waterTower";
            if (GUILayout.Button("Coal Conveyor"))
                _opsPhysicalLoaderPrefab = "vanilla://coalConveyor";
            GUILayout.EndHorizontal();
            if (GUILayout.Button(
                    "PLACE SERVICE OBJECT WITH POINTER",
                    GUILayout.Height(34f)))
            {
                ArmPointerPlacement(
                    PointerPlacementKind.OperationsPhysicalLoader,
                    string.Empty,
                    false);
            }
            if (!_mapEditor.FuseOperationsDocument)
            {
                GUILayout.Label(
                    "Legacy placement writes AlinasMapMod.LoaderBuilder in "
                    + "the current splineys layer. Native FUSE placement has "
                    + "no Alina dependency.",
                    _mutedStyle);
            }
        }

        private void DrawOperatingMarkerBuilder()
        {
            if (!_opsOperatingMetadataLoaded)
            {
                try
                {
                    _mapEditor.RefreshOperatingMetadata();
                    _opsOperatingMetadataLoaded = true;
                }
                catch (Exception ex)
                {
                    _lastPanelMessage = ex.Message;
                }
            }
            GUILayout.Label("AUTONOMOUS OPERATING MARKERS", _titleStyle);
            GUILayout.Label(
                "Markers describe operating intent without changing track "
                + "geometry. Select the owning segment (or node for a grade "
                + "crossing), choose a type, and place it. Railroad Operations "
                + "loads the saved metadata on traffic.json reload.",
                _lineStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label(
                _mapEditor.OperatingMarkers.Count + " marker(s); "
                + _mapEditor.OperatingTerritories.Count + " territory record(s)",
                _mutedStyle);
            if (GUILayout.Button("Refresh", GUILayout.Width(82f)))
                RunGameAction(() =>
                {
                    _mapEditor.RefreshOperatingMetadata();
                    return "Refreshed Railroad Operations metadata.";
                });
            GUILayout.EndHorizontal();

            foreach (var marker in _mapEditor.OperatingMarkers.Take(12))
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(marker.Id, GUILayout.Width(165f)))
                {
                    _opsMarkerId = marker.Id;
                    _opsMarkerName = marker.DisplayName;
                    _opsMarkerType = marker.MarkerType;
                    _opsMarkerCompanyId = marker.CompanyId;
                    _opsMarkerTargetId = marker.TargetId;
                    _opsMarkerTargetField = marker.TargetField;
                    _opsMarkerNativeIndustryId = marker.NativeIndustryId;
                    _opsMarkerNativeComponentId = marker.NativeComponentId;
                    _opsMarkerNativeComponentType = marker.NativeComponentType;
                    _opsMarkerNativePassengerStopId =
                        marker.NativePassengerStopId;
                    _opsMarkerServiceIds = marker.ServiceIds;
                    _opsMarkerSpanIds = marker.TrackSpanIds;
                    _opsMarkerTrackGroupIds = marker.TrackGroupIds;
                    _opsMarkerAllowedCompanyIds = marker.AllowedCompanyIds;
                    _opsMarkerRole = marker.Role;
                    _opsMarkerDirection = marker.Direction;
                    _opsMarkerTolerance = marker.ToleranceMeters;
                    _opsMarkerCapacity = marker.CapacityMeters;
                    _opsMarkerApproach = marker.ApproachMeters;
                    _opsMarkerDwell = marker.DwellMinutes;
                    _opsMarkerMaxCars = marker.MaxCars;
                    _opsMarkerNotes = marker.Notes;
                }
                GUILayout.Label(marker.MarkerType + "  "
                                + (string.IsNullOrWhiteSpace(marker.Location)
                                    ? marker.NodeId : marker.Location),
                    _mutedStyle);
                if (GUILayout.Button("X", GUILayout.Width(30f)))
                    _deleteConfirmId = "ops-marker:" + marker.Id;
                GUILayout.EndHorizontal();
                if (_deleteConfirmId == "ops-marker:" + marker.Id)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Delete " + marker.Id + "?", _offlineStyle);
                    if (GUILayout.Button("Confirm"))
                    {
                        var captured = marker.Id;
                        RunGameAction(() => _mapEditor
                            .DeleteOperatingMarker(captured));
                        _deleteConfirmId = string.Empty;
                    }
                    if (GUILayout.Button("Cancel"))
                        _deleteConfirmId = string.Empty;
                    GUILayout.EndHorizontal();
                }
            }
            if (_mapEditor.OperatingMarkers.Count > 12)
                GUILayout.Label("Showing 12; use the JSON file for the full list.",
                    _mutedStyle);

            GUILayout.Space(6f);
            GUILayout.Label("MARKER DEFINITION", _titleStyle);
            DrawTextField("Marker ID", ref _opsMarkerId);
            DrawTextField("Display name", ref _opsMarkerName);
            DrawTextField("Marker type", ref _opsMarkerType);
            var selectedMarkerType = Array.FindIndex(OperatingMarkerTypes,
                item => string.Equals(item, _opsMarkerType,
                    StringComparison.OrdinalIgnoreCase));
            var chosenMarkerType = GUILayout.SelectionGrid(
                Math.Max(0, selectedMarkerType), OperatingMarkerLabels, 3);
            if (chosenMarkerType >= 0
                && chosenMarkerType < OperatingMarkerTypes.Length
                && chosenMarkerType != selectedMarkerType)
                _opsMarkerType = OperatingMarkerTypes[chosenMarkerType];
            DrawTextField("Company ID", ref _opsMarkerCompanyId);
            DrawTextField("Target object ID", ref _opsMarkerTargetId);
            DrawTextField("Target stop/field ID", ref _opsMarkerTargetField);
            if (string.Equals(_opsMarkerType, "supply-receiving",
                    StringComparison.OrdinalIgnoreCase))
                GUILayout.Label(
                    "For Fuel / supply, Target stop/field ID is the native load ID: coal, diesel-fuel, repair-parts, or another commodity.",
                    _mutedStyle);
            if (string.Equals(_opsMarkerType, "shop-stores",
                    StringComparison.OrdinalIgnoreCase))
                GUILayout.Label(
                    "For Shop stores, Target object ID is the mechanical shop ID. The native industry/component must be its real Railroader unloader.",
                    _mutedStyle);
            GUILayout.Label("NATIVE RAILROADER OPERATIONS BINDING", _titleStyle);
            GUILayout.Label(
                "Use the industry/component IDs created above. The marker "
                + "guides the crew; Railroader's native component retains "
                + "loading, unloading, interchange, repair, payment, and "
                + "performance authority.", _mutedStyle);
            DrawTextField("Native industry ID",
                ref _opsMarkerNativeIndustryId);
            DrawTextField("Native component ID",
                ref _opsMarkerNativeComponentId);
            DrawTextField("Native component type",
                ref _opsMarkerNativeComponentType);
            DrawTextField("Native passenger stop ID",
                ref _opsMarkerNativePassengerStopId);
            DrawTextField("Service IDs", ref _opsMarkerServiceIds);
            DrawTextField("TrackSpan IDs", ref _opsMarkerSpanIds);
            DrawTextField("Track group IDs", ref _opsMarkerTrackGroupIds);
            DrawTextField("Allowed companies", ref _opsMarkerAllowedCompanyIds);
            DrawTextField("Track / operating role", ref _opsMarkerRole);
            DrawTextField("Direction / variant", ref _opsMarkerDirection);
            DrawTextField("Tolerance (m)", ref _opsMarkerTolerance);
            DrawTextField("Capacity (m)", ref _opsMarkerCapacity);
            DrawTextField("Approach (m)", ref _opsMarkerApproach);
            DrawTextField("Dwell (minutes)", ref _opsMarkerDwell);
            DrawTextField("Maximum cars", ref _opsMarkerMaxCars);
            DrawTextField("Notes", ref _opsMarkerNotes);
            var selectedSegment = _mapEditor.SelectedSegment;
            var selectedNode = _mapEditor.SelectedNode;
            GUILayout.Label(selectedSegment == null
                    ? "Segment: select one in the world"
                    : "Segment: " + selectedSegment.Id + "; group "
                      + selectedSegment.GroupId,
                selectedSegment == null ? _offlineStyle : _onlineStyle);
            GUILayout.Label(selectedNode == null
                    ? "Node: select one for a crossing marker"
                    : "Node: " + selectedNode.Id,
                selectedNode == null ? _mutedStyle : _onlineStyle);
            GUI.enabled = selectedSegment != null;
            if (GUILayout.Button("PLACE PRECISE MARKER WITH POINTER",
                    GUILayout.Height(34f)))
                ArmPointerPlacement(PointerPlacementKind.OperationsMarker,
                    string.Empty, false);
            if (GUILayout.Button("SAVE FOR SELECTED TRACK / GROUP",
                    GUILayout.Height(30f)))
                RunGameAction(() => _mapEditor
                    .CreateOperatingMarkerForSelectedTrack(
                        BuildOperatingMarkerDraft()));
            GUI.enabled = selectedNode != null;
            if (GUILayout.Button("SAVE AT SELECTED NODE",
                    GUILayout.Height(30f)))
                RunGameAction(() => _mapEditor
                    .CreateOperatingMarkerAtSelectedNode(
                        BuildOperatingMarkerDraft()));
            GUI.enabled = true;

            GUILayout.Space(8f);
            GUILayout.Label("TRACK OWNERSHIP & RIGHTS", _titleStyle);
            GUILayout.Label(
                "Territories reference the map's existing track group IDs. "
                + "The owner and allowed companies become hard planning rules.",
                _mutedStyle);
            DrawTextField("Territory ID", ref _opsTerritoryId);
            DrawTextField("Display name", ref _opsTerritoryName);
            DrawTextField("Owner company", ref _opsTerritoryOwner);
            DrawTextField("Track group IDs", ref _opsTerritoryGroups);
            DrawTextField("Allowed companies", ref _opsTerritoryAllowed);
            if (selectedSegment != null
                && string.IsNullOrWhiteSpace(_opsTerritoryGroups)
                && GUILayout.Button("Use selected track group"))
                _opsTerritoryGroups = selectedSegment.GroupId;
            if (GUILayout.Button("SAVE / UPDATE TERRITORY",
                    GUILayout.Height(34f)))
                RunGameAction(() => _mapEditor.SaveOperatingTerritory(
                    _opsTerritoryId, _opsTerritoryName,
                    _opsTerritoryOwner, _opsTerritoryGroups,
                    _opsTerritoryAllowed));
        }

        private TileEditorGraphSession.OperatingMarkerDraft
            BuildOperatingMarkerDraft()
        {
            return new TileEditorGraphSession.OperatingMarkerDraft
            {
                Id = _opsMarkerId,
                DisplayName = _opsMarkerName,
                MarkerType = _opsMarkerType,
                CompanyId = _opsMarkerCompanyId,
                TargetId = _opsMarkerTargetId,
                TargetField = _opsMarkerTargetField,
                NativeIndustryId = _opsMarkerNativeIndustryId,
                NativeComponentId = _opsMarkerNativeComponentId,
                NativeComponentType = _opsMarkerNativeComponentType,
                NativePassengerStopId =
                    _opsMarkerNativePassengerStopId,
                ServiceIds = _opsMarkerServiceIds,
                TrackSpanIds = _opsMarkerSpanIds,
                TrackGroupIds = _opsMarkerTrackGroupIds,
                AllowedCompanyIds = _opsMarkerAllowedCompanyIds,
                Role = _opsMarkerRole,
                Direction = _opsMarkerDirection,
                ToleranceMeters = _opsMarkerTolerance,
                CapacityMeters = _opsMarkerCapacity,
                ApproachMeters = _opsMarkerApproach,
                DwellMinutes = _opsMarkerDwell,
                MaxCars = _opsMarkerMaxCars,
                Notes = _opsMarkerNotes,
            };
        }

        private void DrawComponentBuilder(bool facilityDefaults)
        {
            var labels = new[]
            {
                "Receives",
                "Ships",
                "Formula",
                "Team Track",
                "Repair",
                "Interchange",
                "Interchanged Loader",
                "Interchanged Unloader",
                "Progression",
                "Custom",
            };
            _opsComponentProfile = GUILayout.SelectionGrid(
                Mathf.Clamp(
                    _opsComponentProfile,
                    0,
                    labels.Length - 1),
                labels,
                3);
            DrawTextField("Industry ID", ref _opsIndustryId);
            DrawTextField("Component ID", ref _opsComponentId);
            DrawTextField("Display name", ref _opsComponentName);
            DrawTextField(
                "TrackSpan IDs",
                ref _opsComponentSpanId);
            GUILayout.Label(
                "Separate multiple TrackSpan IDs with commas.",
                _mutedStyle);
            var profile = labels[_opsComponentProfile];
            var usesLoad =
                profile == "Receives"
                || profile == "Ships"
                || profile == "Repair"
                || profile == "Interchanged Loader"
                || profile == "Interchanged Unloader";
            if (usesLoad)
            {
                DrawTextField("Commodity / load", ref _opsComponentLoadId);
                DrawTextField(
                    "Car type filter",
                    ref _opsComponentCarFilter);
            }
            _opsSharedStorage = GUILayout.Toggle(
                _opsSharedStorage,
                " Shared storage");

            if (profile == "Receives" || profile == "Ships")
            {
                GUILayout.Space(4f);
                GUILayout.Label("STORAGE & TRANSFER", _titleStyle);
                DrawTextField(
                    "Daily storage change",
                    ref _opsStorageChangeRate);
                DrawTextField(
                    "Maximum storage",
                    ref _opsMaxStorage);
                DrawTextField(
                    "Car transfer rate",
                    ref _opsCarTransferRate);
                GUILayout.BeginHorizontal();
                _opsOrderAroundEmpties = GUILayout.Toggle(
                    _opsOrderAroundEmpties,
                    " Order empties");
                _opsOrderAroundLoaded = GUILayout.Toggle(
                    _opsOrderAroundLoaded,
                    " Order loads");
                GUILayout.EndHorizontal();
            }
            else if (profile == "Formula")
            {
                GUILayout.Space(4f);
                GUILayout.Label("DAILY INPUTS", _titleStyle);
                GUILayout.Label(
                    "Use load-id=amount-per-day; one per line.",
                    _mutedStyle);
                _opsFormulaInputs = GUILayout.TextArea(
                    _opsFormulaInputs ?? string.Empty,
                    GUILayout.Height(54f));
                GUILayout.Label("DAILY OUTPUTS", _titleStyle);
                _opsFormulaOutputs = GUILayout.TextArea(
                    _opsFormulaOutputs ?? string.Empty,
                    GUILayout.Height(54f));
            }
            else if (profile == "Team Track")
            {
                DrawTextField("Ideal cars", ref _opsIdealCars);
                GUILayout.Label(
                    "TEAM PROFILES - one per line",
                    _titleStyle);
                GUILayout.Label(
                    "id|import/export|load-id|loading-days|car-filter",
                    _mutedStyle);
                _opsTeamProfiles = GUILayout.TextArea(
                    _opsTeamProfiles ?? string.Empty,
                    GUILayout.Height(72f));
            }
            else if (profile == "Repair")
            {
                _opsCanOverhaul = GUILayout.Toggle(
                    _opsCanOverhaul,
                    " Can overhaul locomotives");
            }
            else if (profile == "Interchanged Loader")
            {
                DrawTextField(
                    "Output TrackSpan IDs",
                    ref _opsOutputSpanIds);
            }
            else if (profile == "Interchanged Unloader")
            {
                DrawTextField(
                    "Converted load ID",
                    ref _opsConvertedLoadId);
            }
            if (profile == "Custom")
            {
                DrawTextField(
                    "Full component type",
                    ref _opsCustomComponentType);
            }
            if (facilityDefaults
                && profile == "Repair"
                && string.IsNullOrWhiteSpace(_opsComponentName))
            {
                _opsComponentName = "Engine Repair Track";
            }
            GUILayout.Space(4f);
            if (GUILayout.Button(
                    _opsShowAdvancedComponent
                        ? "HIDE ADVANCED COMPONENT FIELDS"
                        : "ADVANCED COMPONENT FIELDS...",
                    GUILayout.Height(27f)))
            {
                _opsShowAdvancedComponent =
                    !_opsShowAdvancedComponent;
            }
            if (_opsShowAdvancedComponent)
            {
                DrawTextField(
                    "Cost per unit",
                    ref _opsComponentCostPerUnit);
                DrawTextField(
                    "Not before hour",
                    ref _opsComponentNotBefore);
                DrawTextField(
                    "Not after hour",
                    ref _opsComponentNotAfter);
                DrawTextField(
                    "Fill fraction (0-1)",
                    ref _opsComponentFill);
                DrawTextField(
                    "Book reasons",
                    ref _opsComponentBookReasons);
                DrawTextField(
                    "Display title",
                    ref _opsComponentTitle);
                GUILayout.Label(
                    _mapEditor.FuseOperationsDocument
                        ? "CUSTOM FIELDS JSON (stored under fields)"
                        : "CUSTOM FIELDS JSON (merged into legacy component)",
                    _titleStyle);
                _opsCustomFieldsJson = GUILayout.TextArea(
                    _opsCustomFieldsJson ?? string.Empty,
                    GUILayout.Height(64f));
            }
            if (GUILayout.Button(
                    "ADD " + profile.ToUpperInvariant(),
                    GUILayout.Height(32f)))
            {
                RunGameAction(
                    () => _mapEditor.AddIndustryComponent(
                        BuildIndustryComponentOptions(
                            profile)));
            }
        }

        private TileEditorGraphSession.IndustryComponentOptions
            BuildIndustryComponentOptions(string profile)
        {
            var options =
                new TileEditorGraphSession.IndustryComponentOptions
                {
                    IndustryId = _opsIndustryId,
                    ComponentId = _opsComponentId,
                    Profile = profile,
                    Name = _opsComponentName,
                    SpanIds = _opsComponentSpanId,
                    LoadId = profile == "Passenger"
                        ? "passengers"
                        : _opsComponentLoadId,
                    CarTypeFilter = profile == "Passenger"
                        ? "*"
                        : _opsComponentCarFilter,
                    CustomType = _opsCustomComponentType,
                    SharedStorage = _opsSharedStorage,
                    FormulaInputs = _opsFormulaInputs,
                    FormulaOutputs = _opsFormulaOutputs,
                    TeamProfiles = _opsTeamProfiles,
                    CanOverhaul = _opsCanOverhaul,
                    PassengerStopId = _opsPassengerStopId,
                    TimetableCode = _opsPassengerCode,
                    Branch = _opsPassengerBranch,
                    NeighborIds = _opsPassengerNeighbors,
                    OutputSpanIds = _opsOutputSpanIds,
                    ConvertedLoadId = _opsConvertedLoadId,
                    CostPerUnit = ParseOptionalFloat(
                        _opsComponentCostPerUnit,
                        "cost per unit"),
                    NotBeforeHour = ParseOptionalFloat(
                        _opsComponentNotBefore,
                        "not-before hour"),
                    NotAfterHour = ParseOptionalFloat(
                        _opsComponentNotAfter,
                        "not-after hour"),
                    FillPercentage = ParseOptionalFloat(
                        _opsComponentFill,
                        "fill fraction"),
                    BookReasons = _opsComponentBookReasons,
                    Title = _opsComponentTitle,
                    CustomFieldsJson = _opsCustomFieldsJson,
                };
            if (profile == "Receives" || profile == "Ships")
            {
                options.StorageChangeRate = ParseFloat(
                    _opsStorageChangeRate,
                    "daily storage change");
                options.MaxStorage = ParseFloat(
                    _opsMaxStorage,
                    "maximum storage");
                options.CarTransferRate = ParseFloat(
                    _opsCarTransferRate,
                    "car transfer rate");
                options.OrderAroundEmpties =
                    _opsOrderAroundEmpties;
                options.OrderAroundLoaded =
                    _opsOrderAroundLoaded;
            }
            if (profile == "Team Track")
            {
                options.IdealCars = ParseFloat(
                    _opsIdealCars,
                    "ideal cars");
            }
            if (profile == "Passenger")
            {
                options.BasePopulation = ParseInt(
                    _opsPassengerPopulation,
                    "base population");
            }
            return options;
        }

        private static float? ParseOptionalFloat(
            string text,
            string label)
        {
            return string.IsNullOrWhiteSpace(text)
                ? (float?)null
                : ParseFloat(text, label);
        }

        private void DrawTurntableBuilder()
        {
            GUILayout.Label("BUILD A TURNTABLE", _titleStyle);
            GUILayout.Label(
                _mapEditor.FuseOperationsDocument
                    ? "Native FUSE creates the pit nodes, rotating bridge track, "
                      + "optional roundhouse stall tracks, and visuals definition."
                    : "Legacy RailLoader creates an Alina TurntableBuilder entry "
                      + "in the current splineys/buildings layer.",
                _lineStyle);
            DrawTextField("Turntable ID", ref _opsTurntableId);
            DrawTextField("Pit radius (m)", ref _opsTurntableRadius);
            DrawTextField(
                "Pit subdivisions (4-32)",
                ref _opsTurntableSubdivisions);
            DrawTextField(
                "Bridge gauge (m)",
                ref _opsTurntableGauge);
            GUILayout.Space(4f);
            GUILayout.Label("Optional roundhouse", _mutedStyle);
            DrawTextField("Stalls (0 = none)", ref _opsTurntableStalls);
            DrawTextField(
                "First stall angle",
                ref _opsTurntableStartAngle);
            DrawTextField(
                "Angle per stall",
                ref _opsTurntableStallAngle);
            DrawTextField(
                "Stall track length (m)",
                ref _opsTurntableTrackLength);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Standard 30 m"))
            {
                _opsTurntableRadius = "15";
                _opsTurntableGauge = "1.435";
                _opsTurntableSubdivisions = "16";
            }
            if (GUILayout.Button("Narrow 21.4 m"))
            {
                _opsTurntableRadius = "10.7";
                _opsTurntableGauge = "0.9144";
                _opsTurntableSubdivisions = "16";
            }
            GUILayout.EndHorizontal();
            if (GUILayout.Button(
                    "PLACE TURNTABLE CENTER WITH POINTER",
                    GUILayout.Height(38f)))
            {
                ArmPointerPlacement(
                    PointerPlacementKind.OperationsTurntable,
                    string.Empty,
                    false);
            }
            GUILayout.Label(
                "The camera heading sets the bridge's starting heading. "
                + "FUSE validation rejects unsafe radius, subdivision, gauge, "
                + "or roundhouse values before the file is saved.",
                _mutedStyle);
        }

        private void UseOperationAsFormInput(
            TileEditorGraphSession.OperationInfo item)
        {
            if (item == null)
                return;
            switch (item.Kind)
            {
                case TileEditorGraphSession.OperationKind.Town:
                    _opsAreaId = item.Id;
                    break;
                case TileEditorGraphSession.OperationKind.TrackSpan:
                    _opsComponentSpanId = item.Id;
                    break;
                case TileEditorGraphSession.OperationKind.Industry:
                    _opsIndustryId = item.Id;
                    if (!string.IsNullOrWhiteSpace(item.OwnerId))
                        _opsAreaId = item.OwnerId;
                    break;
                case TileEditorGraphSession.OperationKind.Commodity:
                    _opsComponentLoadId = item.Id;
                    break;
            }
        }

        private static string OperationsCategory(OperationsTool tool)
        {
            switch (tool)
            {
                case OperationsTool.Towns:
                    return "Towns";
                case OperationsTool.Spans:
                    return "Spans";
                case OperationsTool.Industries:
                    return "Industries";
                case OperationsTool.Passenger:
                    return "Passenger";
                case OperationsTool.Facilities:
                    return "Facilities";
                case OperationsTool.Signals:
                    return "Signals";
                case OperationsTool.Markers:
                    return "All";
                default:
                    return "All";
            }
        }
    }
}
