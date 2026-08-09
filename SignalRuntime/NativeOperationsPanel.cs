using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Game.AccessControl;
using Game.State;
using HarmonyLib;
using UI.Builder;
using UI.CompanyWindow;
using UI.TabView;
using UnityEngine;

namespace Hrogers.SignalRuntime
{
    /// <summary>
    /// Adds the operating desk to Railroader's normal Company window. F9 is
    /// deliberately not involved: Tile Editor remains the authoring tool,
    /// while this panel is the railroad's live dispatcher/crew interface.
    /// </summary>
    [HarmonyPatch(typeof(TabView), nameof(TabView.FinishedAddingTabs))]
    [HarmonyAfter("com.hrogers.aitraffic")]
    internal static class NativeCompanyOperationsPatch
    {
        private const string OperationsTabId =
            "hrogers.signal-runtime.operations";
        private static readonly HashSet<int> PatchedViews =
            new HashSet<int>();

        private static void Prefix(TabView __instance)
        {
            try
            {
                var companyWindow =
                    __instance.GetComponentInParent<CompanyWindow>();
                if (companyWindow == null)
                    return;
                var selected = Traverse.Create(companyWindow)
                    .Field("_selectedTabState")
                    .GetValue<UIState<string>>();
                if (!ReferenceEquals(
                        __instance.SelectedTabState,
                        selected))
                {
                    return;
                }
                var instanceId = __instance.GetInstanceID();
                if (!PatchedViews.Add(instanceId))
                    return;
                var traverse = Traverse.Create(__instance);
                var tabIds = traverse.Field("_tabIds")
                    .GetValue<List<string>>();
                var closures = traverse.Field("_tabBuildClosures")
                    .GetValue<List<Action<UIPanelBuilder>>>();
                var operationsIndex = tabIds?.FindIndex(id =>
                    (id ?? string.Empty).IndexOf(
                        "operations",
                        StringComparison.OrdinalIgnoreCase) >= 0) ?? -1;
                if (operationsIndex >= 0
                    && closures != null
                    && operationsIndex < closures.Count)
                {
                    var originalBuilder = closures[operationsIndex];
                    closures[operationsIndex] = builder =>
                        NativeOperationsPanel.Build(
                            builder,
                            originalBuilder);
                    return;
                }
                __instance.AddTab(
                    "Operations",
                    OperationsTabId,
                    builder => NativeOperationsPanel.Build(builder, null));
            }
            catch (Exception ex)
            {
                NativeOperationsPanel.ReportIntegrationFailure(ex);
            }
        }
    }

    internal static class NativeOperationsPanel
    {
        private const string TrafficPageId = "trafficControl";
        private const string CtcPageId = "signalsCtc";
        private const string OrdersPageId = "trainOrders";
        private const string CrewOrdersPageId = "myOrders";
        private static readonly UIState<string> SelectedPage =
            new UIState<string>(CtcPageId);
        private static readonly Dictionary<string, string> DeliveryCrews =
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
        private static string _notice = "Ready";
        private static bool _reportedIntegrationFailure;

        private sealed class OperationsPage
        {
            internal OperationsPage(string id)
            {
                Id = id;
            }

            internal string Id { get; }
        }

        internal static void Build(
            UIPanelBuilder builder,
            Action<UIPanelBuilder> originalOperationsBuilder)
        {
            var pages = new List<UIPanelBuilder.ListItem<OperationsPage>>();
            if (FindAiTrafficBuilder() != null
                || originalOperationsBuilder != null)
            {
                pages.Add(Page(
                    TrafficPageId,
                    "Traffic Control"));
            }
            pages.Add(Page(CtcPageId, "Signals & CTC"));
            pages.Add(Page(OrdersPageId, "Train Orders"));
            pages.Add(Page(CrewOrdersPageId, "My Orders"));
            if (!pages.Any(page => page.Identifier == SelectedPage.Value))
                SelectedPage.Value = CtcPageId;

            builder.AddListDetail(
                pages,
                SelectedPage,
                (detail, page) =>
                {
                    if (page == null)
                    {
                        detail.AddExpandingVerticalSpacer();
                        detail.AddLabelEmptyState("Select an operations page");
                        detail.AddExpandingVerticalSpacer();
                        return;
                    }
                    detail.VScrollView(
                        scroll => BuildPage(
                            scroll,
                            page.Id,
                            originalOperationsBuilder),
                        new RectOffset(0, 4, 0, 0));
                },
                190f);
        }

        private static UIPanelBuilder.ListItem<OperationsPage> Page(
            string id,
            string text)
        {
            return new UIPanelBuilder.ListItem<OperationsPage>(
                id,
                new OperationsPage(id),
                "Operations",
                text);
        }

        private static void BuildPage(
            UIPanelBuilder builder,
            string pageId,
            Action<UIPanelBuilder> originalOperationsBuilder)
        {
            switch (pageId)
            {
                case TrafficPageId:
                    if (!TryBuildAiTrafficPage(builder))
                    {
                        originalOperationsBuilder?.Invoke(builder);
                    }
                    break;
                case OrdersPageId:
                    BuildDispatcherOrders(builder);
                    break;
                case CrewOrdersPageId:
                    BuildCrewOrders(builder);
                    break;
                default:
                    BuildCtcBoard(builder);
                    break;
            }
        }

        private static void BuildCtcBoard(UIPanelBuilder builder)
        {
            var dispatcher = HasDispatcherAccess();
            builder.AddTitle(
                "Signals & CTC",
                "Live indications and dispatcher controls");
            builder.AddSection("Desk Status", section =>
            {
                section.AddField(
                    "Access",
                    dispatcher
                        ? "Dispatcher controls enabled"
                        : "Indications only");
                section.AddField(
                    "Territory",
                    Main.CtcControlPoints.Count + " control point(s), "
                    + Main.CtcBlocks.Count + " block(s), "
                    + Main.Signals.Count + " signal mast(s)");
                section.AddField(
                    "Last Action",
                    () => _notice,
                    UIPanelBuilder.Frequency.Periodic);
                section.AddField(
                    null,
                    section.AddButton("Refresh Board", section.Rebuild)
                        .RectTransform);
            }, 6f);

            var controlPoints = Main.CtcControlPoints
                .OrderBy(point => point.BoardY)
                .ThenBy(point => point.BoardX)
                .ThenBy(point => point.Name)
                .ToArray();
            if (controlPoints.Length == 0)
            {
                builder.AddSection("CTC Board", section =>
                {
                    section.AddLabel(
                        "No CTC control points are configured. Place and "
                        + "configure them with Tile Editor F9, then reload "
                        + "the map definitions.");
                }, 4f);
            }
            foreach (var point in controlPoints)
                BuildControlPoint(builder, point, dispatcher);

            BuildDiamondInterlockings(builder, dispatcher);
            BuildBlockIndications(builder);
            builder.AddExpandingVerticalSpacer();
        }

        private static void BuildControlPoint(
            UIPanelBuilder builder,
            PlacedCtcControlPoint point,
            bool dispatcher)
        {
            var title = string.IsNullOrWhiteSpace(point.Name)
                ? point.Id
                : point.Name;
            builder.AddSection(title, section =>
            {
                section.AddField(
                    "Indication",
                    () => ControlPointStatus(point.Id),
                    UIPanelBuilder.Frequency.Fast);
                var firstSwitch = point.Switches.FirstOrDefault();
                if (firstSwitch != null)
                {
                    section.ButtonStrip(buttons =>
                    {
                        var normal = buttons.AddButton(
                            "N  " + firstSwitch.NormalLabel,
                            () => Run(
                                Main.TrySetCtcSwitch(point.Id, false),
                                point.Id + " switch requested Normal",
                                "Normal switch request was rejected",
                                buttons));
                        normal.Disable(!dispatcher || firstSwitch.Locked);
                        var reverse = buttons.AddButton(
                            "R  " + firstSwitch.ReverseLabel,
                            () => Run(
                                Main.TrySetCtcSwitch(point.Id, true),
                                point.Id + " switch requested Reverse",
                                "Reverse switch request was rejected",
                                buttons));
                        reverse.Disable(!dispatcher || firstSwitch.Locked);
                    });
                }
                foreach (var route in point.Routes)
                {
                    var capturedRoute = route;
                    var line = section.AddButton(
                        "LINE  " + (string.IsNullOrWhiteSpace(route.Label)
                            ? route.Id
                            : route.Label),
                        () => Run(
                            Main.TryLineCtcRoute(
                                point.Id,
                                capturedRoute.Id),
                            point.Id + " route " + capturedRoute.Id
                            + " requested",
                            "Route request was rejected; check occupancy, "
                            + "switch locks, and entry signal",
                            section));
                    line.Disable(!dispatcher);
                    section.AddField(null, line.RectTransform);
                }
                var stop = section.AddButton(
                    "STOP / CANCEL ROUTE",
                    () => Run(
                        Main.TryCancelCtcRoute(point.Id),
                        point.Id + " route cancellation requested",
                        "No route could be cancelled",
                        section));
                stop.Disable(!dispatcher);
                section.AddField(null, stop.RectTransform);
            }, 5f);
        }

        private static void BuildDiamondInterlockings(
            UIPanelBuilder builder,
            bool dispatcher)
        {
            if (Main.Interlockings.Count == 0)
                return;
            builder.AddSection("Diamond Interlockings", section =>
            {
                foreach (var interlocking in Main.Interlockings)
                {
                    var captured = interlocking;
                    section.AddField(
                        captured.Id,
                        () => InterlockingStatus(captured.Id),
                        UIPanelBuilder.Frequency.Fast);
                    section.ButtonStrip(buttons =>
                    {
                        foreach (var route in captured.Routes)
                        {
                            var capturedRoute = route;
                            var line = buttons.AddButtonCompact(
                                "LINE " + capturedRoute.Id,
                                () => Run(
                                    Main.TryRequestInterlockingRoute(
                                        captured.Id,
                                        capturedRoute.Id),
                                    captured.Id + " " + capturedRoute.Id
                                    + " requested",
                                    "Interlocking request was rejected",
                                    buttons));
                            line.Disable(!dispatcher);
                        }
                        var release = buttons.AddButtonCompact(
                            "RELEASE",
                            () => Run(
                                Main.TryReleaseInterlocking(captured.Id),
                                captured.Id + " released",
                                "Interlocking release was rejected",
                                buttons));
                        release.Disable(!dispatcher);
                    });
                }
            }, 4f);
        }

        private static void BuildBlockIndications(UIPanelBuilder builder)
        {
            if (Main.CtcBlocks.Count == 0)
                return;
            builder.AddSection("Block Indications", section =>
            {
                foreach (var block in Main.CtcBlocks
                    .OrderBy(item => item.Name)
                    .ThenBy(item => item.Id))
                {
                    var captured = block;
                    section.AddField(
                        string.IsNullOrWhiteSpace(block.Name)
                            ? block.Id
                            : block.Name,
                        () => BlockStatus(captured.Id),
                        UIPanelBuilder.Frequency.Fast);
                }
            }, 4f);
        }

        private static void BuildDispatcherOrders(UIPanelBuilder builder)
        {
            var dispatcher = HasDispatcherAccess();
            builder.AddTitle(
                "Train Orders",
                "Dispatcher issue, delivery, and movement authority desk");
            builder.AddSection("Order Office", section =>
            {
                section.AddField(
                    "Access",
                    dispatcher
                        ? "Dispatcher controls enabled"
                        : "Read-only; Dispatcher access is required");
                section.AddField(
                    "Orders",
                    Main.TrainOrders.Count + " loaded from portable map data");
                section.AddField(
                    "Last Action",
                    () => _notice,
                    UIPanelBuilder.Frequency.Periodic);
                section.AddField(
                    null,
                    section.AddButton("Refresh Orders", section.Rebuild)
                        .RectTransform);
            }, 6f);

            var orders = Main.TrainOrders
                .OrderByDescending(order => order.Priority)
                .ThenByDescending(order => order.Number)
                .ToArray();
            if (orders.Length == 0)
            {
                builder.AddSection("Orders", section =>
                {
                    section.AddLabel(
                        "No train orders are configured. Write orders in "
                        + "Tile Editor F9 > Operations > Orders.");
                }, 4f);
            }
            foreach (var order in orders)
                BuildDispatcherOrder(builder, order, dispatcher);
            builder.AddExpandingVerticalSpacer();
        }

        private static void BuildDispatcherOrder(
            UIPanelBuilder builder,
            PlacedTrainOrder order,
            bool dispatcher)
        {
            builder.AddSection(
                "No. " + order.Number + " - " + order.Type,
                section =>
                {
                    section.AddField(
                        "Status",
                        () => OrderStatus(order.Id),
                        UIPanelBuilder.Frequency.Fast);
                    section.AddField(
                        "Train",
                        string.IsNullOrWhiteSpace(order.TrainId)
                            ? "All trains"
                            : order.TrainId);
                    section.AddField(
                        "Authority",
                        AuthorityText(order));
                    if (order.AuthorityBlockIds.Count > 0)
                    {
                        section.AddField(
                            "Blocks",
                            string.Join(", ", order.AuthorityBlockIds));
                    }
                    section.AddLabel(order.Text ?? string.Empty);
                    BuildDeliveryCrewPicker(section, order, dispatcher);
                    section.ButtonStrip(buttons =>
                    {
                        var issue = buttons.AddButtonCompact(
                            "ISSUE",
                            () => Run(
                                Main.TryIssueTrainOrder(order.Id),
                                "Issue request sent for order " + order.Number,
                                "Order issue request was rejected",
                                buttons));
                        issue.Disable(!dispatcher || IsTerminal(order.Status));
                        var deliver = buttons.AddButtonCompact(
                            "DELIVER",
                            () => Run(
                                Main.TryDeliverTrainOrder(
                                    order.Id,
                                    DeliveryCrewFor(order)),
                                "Delivery request sent for order "
                                + order.Number,
                                "Select a valid train crew before delivery",
                                buttons));
                        deliver.Disable(
                            !dispatcher
                            || IsTerminal(order.Status)
                            || string.IsNullOrWhiteSpace(
                                DeliveryCrewFor(order)));
                        var fulfilled = buttons.AddButtonCompact(
                            "FULFILLED",
                            () => Run(
                                Main.TryFulfillTrainOrder(order.Id),
                                "Fulfillment request sent for order "
                                + order.Number,
                                "Order fulfillment request was rejected",
                                buttons));
                        fulfilled.Disable(
                            !dispatcher || IsTerminal(order.Status));
                        var cancel = buttons.AddButtonCompact(
                            "CANCEL",
                            () => Run(
                                Main.TryCancelTrainOrder(order.Id),
                                "Cancellation request sent for order "
                                + order.Number,
                                "Order cancellation request was rejected",
                                buttons));
                        cancel.Disable(
                            !dispatcher || IsTerminal(order.Status));
                    });
                },
                5f);
        }

        private static void BuildDeliveryCrewPicker(
            UIPanelBuilder section,
            PlacedTrainOrder order,
            bool dispatcher)
        {
            var crews = StateManager.Shared?.PlayersManager?.TrainCrews;
            if (crews == null || crews.Count == 0)
            {
                section.AddField("Delivery Crew", "No train crews available");
                return;
            }
            var selectedCrewId = DeliveryCrewFor(order);
            var index = crews.ToList().FindIndex(crew => string.Equals(
                crew.Id,
                selectedCrewId,
                StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                index = 0;
            DeliveryCrews[order.Id] = crews[index].Id;
            var dropdown = section.AddDropdown(
                crews.Select(crew => crew.Name).ToList(),
                index,
                selectedIndex =>
                {
                    if (selectedIndex >= 0 && selectedIndex < crews.Count)
                        DeliveryCrews[order.Id] = crews[selectedIndex].Id;
                });
            dropdown.GetComponent<UnityEngine.UI.Selectable>().interactable =
                dispatcher;
            section.AddField("Delivery Crew", dropdown);
        }

        private static void BuildCrewOrders(UIPanelBuilder builder)
        {
            builder.AddTitle(
                "My Orders",
                "Crew copy, repeat, and acknowledgement");
            var crew = StateManager.Shared?.PlayersManager?.MyTrainCrew;
            if (crew == null)
            {
                builder.AddSection("Train Crew", section =>
                {
                    section.AddLabel(
                        "Join a Railroader train crew to receive and "
                        + "acknowledge orders.");
                }, 4f);
                builder.AddExpandingVerticalSpacer();
                return;
            }
            builder.AddSection("Train Crew", section =>
            {
                section.AddField("Assigned Crew", crew.Name);
                section.AddField(
                    "Last Action",
                    () => _notice,
                    UIPanelBuilder.Frequency.Periodic);
                section.AddField(
                    null,
                    section.AddButton("Refresh My Orders", section.Rebuild)
                        .RectTransform);
            }, 5f);
            var orders = Main.TrainOrders.Where(order => string.Equals(
                    order.AssignedCrewId,
                    crew.Id,
                    StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(order => order.Priority)
                .ThenByDescending(order => order.Number)
                .ToArray();
            if (orders.Length == 0)
            {
                builder.AddSection("Orders", section =>
                    section.AddLabel(
                        "No orders have been delivered to this crew."));
            }
            foreach (var order in orders)
                BuildCrewOrder(builder, order);
            builder.AddSection("Quick Access", section =>
                section.AddLabel(
                    "F8 opens the compact crew-order copy without opening "
                    + "the Company window."));
            builder.AddExpandingVerticalSpacer();
        }

        private static void BuildCrewOrder(
            UIPanelBuilder builder,
            PlacedTrainOrder order)
        {
            builder.AddSection(
                "No. " + order.Number + " - " + order.Type,
                section =>
                {
                    section.AddField(
                        "Status",
                        () => OrderStatus(order.Id),
                        UIPanelBuilder.Frequency.Fast);
                    section.AddField("Authority", AuthorityText(order));
                    if (!string.IsNullOrWhiteSpace(order.MeetAt))
                        section.AddField("Meet", order.MeetAt);
                    if (order.AuthorityBlockIds.Count > 0)
                    {
                        section.AddField(
                            "Blocks",
                            string.Join(", ", order.AuthorityBlockIds));
                    }
                    section.AddLabel(order.Text ?? string.Empty);
                    var acknowledge = section.AddButton(
                        order.Type == "Form 31"
                            ? "SIGN / REPEAT / ACKNOWLEDGE"
                            : "REPEAT / ACKNOWLEDGE",
                        () => Run(
                            Main.TryAcknowledgeTrainOrder(order.Id),
                            "Acknowledgement sent for order "
                            + order.Number,
                            "Order acknowledgement was rejected",
                            section));
                    acknowledge.Disable(!string.Equals(
                        order.Status,
                        "Delivered",
                        StringComparison.OrdinalIgnoreCase));
                    section.AddField(null, acknowledge.RectTransform);
                    if (!string.IsNullOrWhiteSpace(order.AcknowledgedBy))
                    {
                        section.AddField(
                            "Acknowledged",
                            order.AcknowledgedBy + " at "
                            + order.AcknowledgedAt);
                    }
                },
                5f);
        }

        private static string DeliveryCrewFor(PlacedTrainOrder order)
        {
            if (DeliveryCrews.TryGetValue(order.Id, out var crewId)
                && !string.IsNullOrWhiteSpace(crewId))
            {
                return crewId;
            }
            return order.AssignedCrewId ?? string.Empty;
        }

        private static string ControlPointStatus(string id)
        {
            return Main.TryGetCtcControlPoint(id, out var point)
                ? point.Phase + (string.IsNullOrWhiteSpace(
                        point.ActiveRouteId)
                    ? string.Empty
                    : " / " + point.ActiveRouteId)
                  + " - " + point.LastReason
                : "Unavailable";
        }

        private static string InterlockingStatus(string id)
        {
            return Main.TryGetInterlocking(id, out var interlocking)
                ? interlocking.Phase
                  + (string.IsNullOrWhiteSpace(
                        interlocking.ActiveApproachId)
                      ? string.Empty
                      : " / " + interlocking.ActiveApproachId)
                  + " - " + interlocking.LastTransitionReason
                : "Unavailable";
        }

        private static string BlockStatus(string id)
        {
            var block = Main.CtcBlocks.FirstOrDefault(item => string.Equals(
                item.Id,
                id,
                StringComparison.OrdinalIgnoreCase));
            return block == null
                ? "Unavailable"
                : block.Mode.ToUpperInvariant() + " / "
                  + (block.IsOccupied ? "OCCUPIED" : "CLEAR");
        }

        private static string OrderStatus(string id)
        {
            if (!Main.TryGetTrainOrder(id, out var order))
                return "Unavailable";
            var result = order.Status;
            if (!string.IsNullOrWhiteSpace(order.AssignedCrewId))
            {
                var crewName = StateManager.Shared?.PlayersManager?
                    .NameForTrainCrewId(order.AssignedCrewId);
                result += " / " + (crewName ?? order.AssignedCrewId);
            }
            if (!string.IsNullOrWhiteSpace(order.LastReason))
                result += " - " + order.LastReason;
            return result;
        }

        private static string AuthorityText(PlacedTrainOrder order)
        {
            var text = (order.From ?? string.Empty) + " to "
                       + (order.To ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(order.MeetAt))
                text += " / meet at " + order.MeetAt;
            if (order.MaxSpeedMph > 0)
                text += " / max " + order.MaxSpeedMph + " mph";
            return text;
        }

        private static bool HasDispatcherAccess()
        {
            if (StateManager.IsHost)
                return true;
            var players = StateManager.Shared?.PlayersManager;
            return players != null
                   && players.TryGetAccessLevel(
                       PlayersManager.PlayerId,
                       out var level)
                   && level >= AccessLevel.Dispatcher;
        }

        private static bool IsTerminal(string status)
        {
            return string.Equals(
                       status,
                       "Fulfilled",
                       StringComparison.OrdinalIgnoreCase)
                   || string.Equals(
                       status,
                       "Cancelled",
                       StringComparison.OrdinalIgnoreCase);
        }

        private static void Run(
            bool accepted,
            string acceptedMessage,
            string rejectedMessage,
            UIPanelBuilder builder)
        {
            _notice = accepted ? acceptedMessage : rejectedMessage;
            builder.Rebuild();
        }

        private static MethodInfo FindAiTrafficBuilder()
        {
            return AccessTools.TypeByName("AITraffic.OperationsPanelBuilder")?
                .GetMethod(
                    "BuildTrafficControl",
                    BindingFlags.Static | BindingFlags.NonPublic);
        }

        private static bool TryBuildAiTrafficPage(UIPanelBuilder builder)
        {
            var method = FindAiTrafficBuilder();
            if (method == null)
                return false;
            try
            {
                method.Invoke(null, new object[] { builder });
                return true;
            }
            catch (Exception ex)
            {
                ReportIntegrationFailure(ex);
                return false;
            }
        }

        internal static void ReportIntegrationFailure(Exception ex)
        {
            if (_reportedIntegrationFailure)
                return;
            _reportedIntegrationFailure = true;
            UnityEngine.Debug.LogError(
                "Hrogers Signal Runtime could not build the native "
                + "Operations panel: " + ex);
        }
    }
}
