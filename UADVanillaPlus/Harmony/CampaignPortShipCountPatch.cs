using System.Text.RegularExpressions;
using HarmonyLib;
using Il2Cpp;
using Il2CppTMPro;
using MelonLoader;
using UnityEngine;

namespace UADVanillaPlus.Harmony;

// Patch intent: show where fleets are parked directly on campaign-map port
// labels, matching vanilla's port popup inventory so the map and hover details
// agree even when AI ships are reassigned for repairs.
[HarmonyPatch(typeof(MapUI))]
internal static class CampaignPortShipCountPatch
{
    private const float SafetyRefreshIntervalSeconds = 2f;
    private static readonly Color OccupiedPortLabelColor = new(0.08f, 0.08f, 0.08f, 1f);
    private static readonly Color EmptyPortLabelColor = new(0.18f, 0.18f, 0.18f, 1f);
    private static readonly Regex ShipCountSuffixRegex = new(@"\s*\(\d+\)\s*$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex BoldWrapperRegex = new(@"^\s*<b>(?<text>.*)</b>\s*$", RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Dictionary<IntPtr, PortLabelStyle> OriginalLabelStyles = new();
    private static bool countsDirty = true;
    private static float nextSafetyRefreshTime;
    private static string lastLoggedSummary = string.Empty;

    [HarmonyPostfix]
    [HarmonyPatch(nameof(MapUI.InitPortsUI))]
    internal static void InitPortsUIPostfix(MapUI __instance)
    {
        countsDirty = true;
        RefreshPortLabels(__instance);
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(MapUI.UpdatePortsOwnerUI))]
    internal static void UpdatePortsOwnerUIPostfix(MapUI __instance)
    {
        countsDirty = true;
        RefreshPortLabels(__instance);
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(MapUI.LateUpdate))]
    internal static void LateUpdatePostfix(MapUI __instance)
    {
        if (__instance == null || CampaignController.Instance?.CampaignData == null)
            return;

        bool safetyRefresh = Time.realtimeSinceStartup >= nextSafetyRefreshTime;
        if (!countsDirty && !safetyRefresh)
            return;

        if (RefreshPortLabels(__instance))
        {
            countsDirty = false;
            nextSafetyRefreshTime = Time.realtimeSinceStartup + SafetyRefreshIntervalSeconds;
        }
    }

    internal static void MarkCountsDirty()
        => countsDirty = true;

    private static bool RefreshPortLabels(MapUI? mapUi)
    {
        try
        {
            if (mapUi?.PortsRoot == null)
                return false;

            Dictionary<string, int> countsByPort = BuildCampaignPortCounts();
            int decoratedPorts = 0;

            foreach (PortUI portUi in mapUi.PortsRoot.GetComponentsInChildren<PortUI>(true))
            {
                if (portUi == null || portUi.PortName == null)
                    continue;

                int count = 0;
                string portId = portUi.Id ?? string.Empty;
                if (!string.IsNullOrEmpty(portId))
                    countsByPort.TryGetValue(portId, out count);

                string baseName = StripShipCountDecoration(portUi.PortName.text);
                portUi.PortName.richText = true;
                ApplyShipCountTextStyle(portUi.PortName, count > 0);
                portUi.PortName.text = count > 0 ? $"<b>{baseName} ({count})</b>" : baseName;

                if (count > 0)
                    decoratedPorts++;
            }

            LogSummaryOnce(countsByPort, decoratedPorts);
            return true;
        }
        catch (Exception ex)
        {
            Melon<UADVanillaPlusMod>.Logger.Warning(
                $"UADVP campaign port ship counts failed; leaving current port labels intact. {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static Dictionary<string, int> BuildCampaignPortCounts()
    {
        Dictionary<string, int> countsByPort = new(StringComparer.Ordinal);

        if (CampaignController.Instance?.CampaignData?.VesselsInPort == null)
        {
            return countsByPort;
        }

        foreach (var entry in CampaignController.Instance.CampaignData.VesselsInPort)
        {
            PortElement? port = entry.Key;
            var vessels = entry.Value;
            string portId = port?.Id ?? string.Empty;
            if (string.IsNullOrEmpty(portId) || vessels == null)
                continue;

            int count = 0;
            foreach (VesselEntity vessel in vessels)
            {
                if (!IsCountedPortVessel(vessel))
                    continue;

                count++;
            }

            if (count <= 0)
                continue;

            countsByPort.TryGetValue(portId, out int current);
            countsByPort[portId] = current + count;
        }

        return countsByPort;
    }

    private static bool IsCountedPortVessel(VesselEntity? vessel)
    {
        if (vessel == null ||
            vessel.isSunk ||
            vessel.isScrapped)
        {
            return false;
        }

        if (vessel.vesselType == VesselEntity.VesselType.Ship)
        {
            Ship? ship = vessel.TryCast<Ship>();
            return ship != null && !ship.isDesign;
        }

        return vessel.vesselType == VesselEntity.VesselType.Submarine;
    }

    private static string StripShipCountDecoration(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        string result = text.Trim();
        Match boldMatch = BoldWrapperRegex.Match(result);
        if (boldMatch.Success)
            result = boldMatch.Groups["text"].Value;

        return ShipCountSuffixRegex.Replace(result, string.Empty);
    }

    private static void ApplyShipCountTextStyle(TMP_Text text, bool hasShips)
    {
        PortLabelStyle original = GetOriginalLabelStyle(text);
        if (hasShips)
        {
            text.color = OccupiedPortLabelColor;
            text.fontStyle = original.FontStyle | FontStyles.Bold;
            text.fontWeight = FontWeight.Bold;
            return;
        }

        text.color = EmptyPortLabelColor;
        text.fontStyle = original.FontStyle & ~FontStyles.Bold;
        text.fontWeight = original.FontWeight;
    }

    private static PortLabelStyle GetOriginalLabelStyle(TMP_Text text)
    {
        IntPtr key = text.Pointer;
        if (OriginalLabelStyles.TryGetValue(key, out PortLabelStyle style))
            return style;

        style = new PortLabelStyle(text.color, text.fontStyle, text.fontWeight);
        OriginalLabelStyles[key] = style;
        return style;
    }

    private readonly struct PortLabelStyle
    {
        internal readonly FontStyles FontStyle;
        internal readonly FontWeight FontWeight;

        internal PortLabelStyle(Color color, FontStyles fontStyle, FontWeight fontWeight)
        {
            FontStyle = fontStyle;
            FontWeight = fontWeight;
        }
    }

    private static void LogSummaryOnce(Dictionary<string, int> countsByPort, int decoratedPorts)
    {
        int totalShips = 0;
        foreach (int count in countsByPort.Values)
            totalShips += count;

        string summary = $"{countsByPort.Count}:{totalShips}:{decoratedPorts}";
        if (summary == lastLoggedSummary)
            return;

        lastLoggedSummary = summary;
        Melon<UADVanillaPlusMod>.Logger.Msg(
            $"UADVP campaign port ship counts: displayed {totalShips} vessels across {countsByPort.Count} campaign ports.");
    }
}

[HarmonyPatch(typeof(VesselEntity), nameof(VesselEntity.SetPortLocation))]
internal static class CampaignPortShipCountVesselLocationPatch
{
    [HarmonyPostfix]
    internal static void SetPortLocationPostfix()
        => CampaignPortShipCountPatch.MarkCountsDirty();
}
