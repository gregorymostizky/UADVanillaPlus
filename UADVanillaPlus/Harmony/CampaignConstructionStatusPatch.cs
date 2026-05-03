using System.Diagnostics;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

namespace UADVanillaPlus.Harmony;

// Patch intent: vanilla keeps commissioning ships out of the campaign build
// summary even though they are still not active ships. VP keeps the panel compact
// while showing three exclusive queue buckets: own builds, foreign contracts, and
// commissioning ships.
[HarmonyPatch(typeof(CampaignCountryInfoUI))]
internal static class CampaignConstructionStatusPatch
{
    private const string ForeignBuildColor = "#9A9A9A";
    private const string DockIdleColor = "#9A9A9A";
    private const string DockBuildingColor = "#A7D37A";
    private const string TransportGoodColor = "#A7D37A";
    private const string TransportWarnColor = "#D8C06A";
    private const string TransportBadColor = "#D37A7A";
    private const string MaintenanceLineName = "UADVP_MaintenanceLine";
    private const string ConstructionTooltip = "Shown as own builds / foreign contracts (commissioning).\nForeign contracts are ships this nation's yards are building for another nation.\nCommissioning ships are counted separately from ships still under construction.";
    private static readonly HashSet<GameObject> TooltipTargets = new();
    private static readonly HashSet<GameObject> DockTooltipTargets = new();
    private static string lastLoggedSummary = string.Empty;
    private static string lastLoggedMaintenanceSummary = string.Empty;
    private static string lastLoggedApplySkip = string.Empty;

    [HarmonyPrefix]
    [HarmonyPatch(nameof(CampaignCountryInfoUI.GetVesselsBuildingCount))]
    internal static bool GetVesselsBuildingCountPrefix(Player player, ref string __result)
    {
        if (!TryBuildCounts(player, out ConstructionCounts counts))
            return true;

        __result = counts.TotalDisplay;
        return false;
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(CampaignCountryInfoUI.Refresh))]
    internal static void RefreshPostfix(CampaignCountryInfoUI __instance)
        => ApplyToInstance(__instance);

    internal static void ApplyToInstance(CampaignCountryInfoUI __instance)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();

        try
        {
            Player? player = PlayerController.Instance;
            if (__instance == null || player == null)
                return;

            // Dock and transport status come from player-level campaign data and
            // can be ready before vanilla has finished populating the build list
            // during campaign load. Keep it independent so the maintenance line
            // appears on first load instead of waiting for a later tab refresh.
            UpdateMaintenanceStatus(__instance, player, stopwatch);

            if (__instance.BuildingList == null)
            {
                LogApplySkipOnce("building-list-missing");
                return;
            }

            if (TryBuildCounts(player, out ConstructionCounts counts))
            {
                if (__instance.BuildingLabel != null)
                    __instance.BuildingLabel.text = $"Building {counts.TotalDisplay} ships:";

                __instance.BuildingList.text = counts.TypeDisplay;
                EnsureConstructionTooltip(__instance);

                string summary = $"{counts.TotalDisplay}|{counts.TypeDisplay}";
                if (summary != lastLoggedSummary)
                {
                    lastLoggedSummary = summary;
                    Melon<UADVanillaPlusMod>.Logger.Msg(
                        $"UADVP campaign construction: displayed {counts.TotalDisplay} ships as {counts.TypeDisplay} in {stopwatch.ElapsedMilliseconds} ms.");
                }
            }
            else
            {
                LogApplySkipOnce("build-counts-unavailable");
            }
        }
        catch (Exception ex)
        {
            Melon<UADVanillaPlusMod>.Logger.Warning(
                $"UADVP campaign construction status patch failed; leaving vanilla text intact. {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static bool TryBuildCounts(Player player, out ConstructionCounts counts)
    {
        counts = default;

        try
        {
            if (player == null)
                return false;

            Dictionary<string, TypeCounts> byType = new(StringComparer.OrdinalIgnoreCase);

            if (CampaignController.Instance?.CampaignData?.VesselsByPlayer == null ||
                !CampaignController.Instance.CampaignData.VesselsByPlayer.TryGetValue(player.data, out var vessels))
            {
                return false;
            }

            foreach (VesselEntity vessel in vessels)
            {
                if (vessel == null || (!vessel.isBuilding && !vessel.isCommissioning))
                    continue;

                if (vessel.vesselType == VesselEntity.VesselType.Ship)
                {
                    Ship? ship = vessel.TryCast<Ship>();
                    if (ship == null || ship.isDesign)
                        continue;

                    AddVessel(byType, ShipTypeLabel(ship), ship.isCommissioning, ship.ForSaleTo != null);
                    continue;
                }

                if (vessel.vesselType == VesselEntity.VesselType.Submarine)
                {
                    Submarine? submarine = vessel.TryCast<Submarine>();
                    if (submarine == null)
                        continue;

                    AddVessel(byType, SubmarineTypeLabel(submarine), submarine.isCommissioning, false);
                }
            }

            counts = new ConstructionCounts(
                FormatCounts(TotalTypeCounts(byType.Values)),
                byType.Count == 0 ? "-" : string.Join(", ", OrderedTypeDisplays(byType)));
            return true;
        }
        catch (Exception ex)
        {
            Melon<UADVanillaPlusMod>.Logger.Warning(
                $"UADVP campaign construction count failed; falling back to vanilla. {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static void LogApplySkipOnce(string reason)
    {
        if (reason == lastLoggedApplySkip)
            return;

        lastLoggedApplySkip = reason;
        Melon<UADVanillaPlusMod>.Logger.Msg($"UADVP campaign construction partial apply: {reason}; maintenance status was still attempted.");
    }

    private static void UpdateMaintenanceStatus(CampaignCountryInfoUI ui, Player player, Stopwatch stopwatch)
    {
        if (ui.ShipyardSize == null || player == null)
            return;

        DockyardStatus status = BuildDockyardStatus(player);
        TransportCapacityStatus transport = BuildTransportCapacityStatus(player);
        string beforeText = ui.ShipyardSize.text ?? string.Empty;
        string strippedText = StripMaintenanceLine(beforeText);
        string maintenanceText = $"{status.Display} | {transport.Display}";

        ui.ShipyardSize.text = strippedText;
        RemoveFloatingMaintenanceLine(ui);
        UpdateShipbuildingCapacityMaintenance(ui, maintenanceText);
        RebuildCountryInfoLayout(ui);

        EnsureDockyardTooltip(ui);

        string summary = $"{status.LogSummary}; {transport.LogSummary}";
        if (summary != lastLoggedMaintenanceSummary)
        {
            lastLoggedMaintenanceSummary = summary;
            Melon<UADVanillaPlusMod>.Logger.Msg(
                $"UADVP campaign maintenance status: displayed {summary} in {stopwatch.ElapsedMilliseconds} ms.");
        }

    }

    internal static string DebugMaintenanceText(CampaignCountryInfoUI? ui)
    {
        if (ui?.ShipyardSize == null)
            return "<missing ShipyardSize>";

        return CompactText($"{ui.ShipyardSize.text}\n{ui.ShipbuildingCapacity?.text}");
    }

    internal static bool HasMaintenanceMarkers(CampaignCountryInfoUI? ui)
    {
        if (ui?.ShipyardSize == null)
            return false;

        string text = $"{ui.ShipyardSize.text}\n{ui.ShipbuildingCapacity?.text}";
        return HasMarker(text, "Dock ") && HasMarker(text, ">TR ");
    }

    private static void UpdateShipbuildingCapacityMaintenance(CampaignCountryInfoUI ui, string maintenanceText)
    {
        if (ui.ShipbuildingCapacity == null)
            return;

        string capacityText = StripMaintenanceLine(ui.ShipbuildingCapacity.text ?? string.Empty);
        ui.ShipbuildingCapacity.text = string.IsNullOrWhiteSpace(capacityText)
            ? maintenanceText
            : $"{maintenanceText}\n{capacityText}";
    }

    private static void RemoveFloatingMaintenanceLine(CampaignCountryInfoUI ui)
    {
        if (ui.ShipyardSize == null)
            return;

        DestroyMaintenanceLine(ui.ShipyardSize.transform.Find(MaintenanceLineName));
        DestroyMaintenanceLine(ui.ShipyardSize.transform.parent?.Find(MaintenanceLineName));
    }

    private static void DestroyMaintenanceLine(Transform? marker)
    {
        if (marker == null)
            return;

        UnityEngine.Object.Destroy(marker.gameObject);
    }

    private static void RebuildCountryInfoLayout(CampaignCountryInfoUI ui)
    {
        RectTransform? rect = ui.RTransform;
        if (rect == null)
            rect = ui.GetComponent<RectTransform>();

        if (rect != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
    }

    private static bool HasMarker(string? text, string marker)
        => !string.IsNullOrEmpty(text) && text.Contains(marker, StringComparison.Ordinal);

    private static string CompactText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        string compact = text.Replace("\r", string.Empty).Replace("\n", " | ").Trim();
        return compact.Length <= 180 ? compact : compact[..180] + "...";
    }

    private static string StripMaintenanceLine(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        IEnumerable<string> vanillaLines = text
            .Replace("\r", string.Empty)
            .Split('\n')
            .Select(StripMaintenanceSuffix)
            .Where(static line =>
                !line.Contains("Dock Idle", StringComparison.Ordinal) &&
                !line.Contains("Dock Expanding:", StringComparison.Ordinal) &&
                !line.Contains("Dock Exp ", StringComparison.Ordinal) &&
                !line.Contains(">TR ", StringComparison.Ordinal));

        return string.Join("\n", vanillaLines).TrimEnd();
    }

    private static string StripMaintenanceSuffix(string line)
    {
        int suffixStart = line.IndexOf(" | <color=", StringComparison.Ordinal);
        if (suffixStart >= 0 &&
            (line.IndexOf("Dock ", suffixStart, StringComparison.Ordinal) >= 0 ||
             line.IndexOf(">TR ", suffixStart, StringComparison.Ordinal) >= 0))
        {
            return line[..suffixStart].TrimEnd();
        }

        suffixStart = line.IndexOf(" | Dock ", StringComparison.Ordinal);
        if (suffixStart >= 0)
            return line[..suffixStart].TrimEnd();

        suffixStart = line.IndexOf(" | TR ", StringComparison.Ordinal);
        return suffixStart >= 0 ? line[..suffixStart].TrimEnd() : line;
    }

    private static DockyardStatus BuildDockyardStatus(Player player)
    {
        if (player.shipyardBuildMonthLeft <= 0 || player.shipyardBuildMonthTotal <= 0)
        {
            return new DockyardStatus(
                $"<color={DockIdleColor}>Dock Idle</color>",
                "Dock expansion is not currently active.",
                "idle");
        }

        int elapsed = Math.Max(0, player.shipyardBuildMonthTotal - player.shipyardBuildMonthLeft);
        string months = $"{elapsed}/{player.shipyardBuildMonthTotal} mo";
        string amount = FormatWeightSafe(player.shipyardTotalBuildAmount);
        return new DockyardStatus(
            $"<color={DockBuildingColor}>Dock Exp {elapsed}/{player.shipyardBuildMonthTotal}m</color>",
            $"Dock expansion is active.\nRemaining: {player.shipyardBuildMonthLeft} months\nPlanned increase: {amount}",
            $"expanding {months}, left {player.shipyardBuildMonthLeft}, amount {amount}");
    }

    private static TransportCapacityStatus BuildTransportCapacityStatus(Player player)
    {
        float capacity = player.transportCapacity;
        float percent = capacity <= 3f ? capacity * 100f : capacity;
        string color = percent >= 190f ? TransportGoodColor : percent >= 150f ? TransportWarnColor : TransportBadColor;
        string display = $"<color={color}>TR {percent:0}%</color>";
        string tooltip = $"Transport capacity: {percent:0}%\nTarget: keep this near 200% when possible.";
        return new TransportCapacityStatus(display, tooltip, $"transport {percent:0}%");
    }

    private static string FormatWeightSafe(float tons)
    {
        try
        {
            return Ui.FormatWeight(tons, true, false);
        }
        catch
        {
            return $"{Mathf.RoundToInt(tons):N0} t";
        }
    }

    private static IEnumerable<string> OrderedTypeDisplays(Dictionary<string, TypeCounts> byType)
    {
        foreach (string type in new[] { "BB", "BC", "CA", "CL", "DD", "TB", "SS" })
        {
            if (byType.TryGetValue(type, out TypeCounts counts))
                yield return TypeDisplay(type, counts);
        }

        foreach (KeyValuePair<string, TypeCounts> entry in byType.OrderBy(static x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (IsKnownType(entry.Key))
                continue;

            yield return TypeDisplay(entry.Key, entry.Value);
        }
    }

    private static string TypeDisplay(string type, TypeCounts counts)
    {
        return $"{FormatCounts(counts)} {type}s";
    }

    private static void AddVessel(Dictionary<string, TypeCounts> byType, string type, bool isCommissioning, bool isForeignContract)
    {
        byType.TryGetValue(type, out TypeCounts typeCounts);

        if (isCommissioning)
        {
            typeCounts.Commissioning++;
        }
        else if (isForeignContract)
        {
            typeCounts.BuildingForeign++;
        }
        else
        {
            typeCounts.BuildingOwn++;
        }

        byType[type] = typeCounts;
    }

    private static string FormatCounts(TypeCounts counts)
    {
        return $"{counts.BuildingOwn} / {Grey(counts.BuildingForeign)} ({counts.Commissioning})";
    }

    private static string Grey(int value)
    {
        return $"<color={ForeignBuildColor}>{value}</color>";
    }

    private static TypeCounts TotalTypeCounts(IEnumerable<TypeCounts> counts)
    {
        TypeCounts total = new();
        foreach (TypeCounts count in counts)
        {
            total.BuildingOwn += count.BuildingOwn;
            total.BuildingForeign += count.BuildingForeign;
            total.Commissioning += count.Commissioning;
        }

        return total;
    }

    private static void EnsureConstructionTooltip(CampaignCountryInfoUI ui)
    {
        AddTooltip(ui.BuildingLabel?.gameObject);
        AddTooltip(ui.BuildingList?.gameObject);
    }

    private static void EnsureDockyardTooltip(CampaignCountryInfoUI ui)
    {
        AddDockTooltip(ui.ShipyardSize?.gameObject);
    }

    private static void AddTooltip(GameObject? target)
    {
        if (target == null || TooltipTargets.Contains(target))
            return;

        TooltipTargets.Add(target);

        OnEnter onEnter = target.AddComponent<OnEnter>();
        onEnter.action = new System.Action(() => G.ui.ShowTooltip(ConstructionTooltip, target));

        OnLeave onLeave = target.AddComponent<OnLeave>();
        onLeave.action = new System.Action(() => G.ui.HideTooltip());
    }

    private static void AddDockTooltip(GameObject? target)
    {
        if (target == null || DockTooltipTargets.Contains(target))
            return;

        DockTooltipTargets.Add(target);

        OnEnter onEnter = target.AddComponent<OnEnter>();
        onEnter.action = new System.Action(() =>
        {
            Player? player = PlayerController.Instance;
            string tooltip = player == null
                ? "Campaign maintenance status unavailable."
                : $"{BuildDockyardStatus(player).Tooltip}\n\n{BuildTransportCapacityStatus(player).Tooltip}";
            G.ui.ShowTooltip(tooltip, target);
        });

        OnLeave onLeave = target.AddComponent<OnLeave>();
        onLeave.action = new System.Action(() => G.ui.HideTooltip());
    }

    private static bool IsKnownType(string type)
    {
        return type is "BB" or "BC" or "CA" or "CL" or "DD" or "TB" or "SS";
    }

    private static string ShipTypeLabel(Ship ship)
    {
        string? label = ship.shipType?.nameUi;
        if (string.IsNullOrWhiteSpace(label))
            label = ship.shipType?.name;

        return string.IsNullOrWhiteSpace(label) ? "Ship" : label.Trim().ToUpperInvariant();
    }

    private static string SubmarineTypeLabel(Submarine submarine)
    {
        string? label = submarine.Type?.nameUi;
        if (string.IsNullOrWhiteSpace(label))
            label = submarine.SpecialType;

        return string.IsNullOrWhiteSpace(label) ? "SS" : label.Trim().ToUpperInvariant();
    }

    private readonly record struct ConstructionCounts(string TotalDisplay, string TypeDisplay);

    private readonly record struct DockyardStatus(string Display, string Tooltip, string LogSummary);

    private readonly record struct TransportCapacityStatus(string Display, string Tooltip, string LogSummary);

    private struct TypeCounts
    {
        internal int BuildingOwn;
        internal int BuildingForeign;
        internal int Commissioning;
    }
}
