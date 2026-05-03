using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using System.Text.RegularExpressions;
using UnityEngine;

namespace UADVanillaPlus.Harmony;

// Patch intent: make the campaign Active Fleet summary show how many active
// vessels are sitting in port. Vanilla shows the active fleet total but hides
// the deployed-vs-port split unless the player opens fleet details.
[HarmonyPatch(typeof(CampaignCountryInfoUI))]
internal static class CampaignActiveFleetStatusPatch
{
    private static readonly Regex PortCountSuffixRegex = new(@"\s*\(\d+ port\)\s*$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly HashSet<GameObject> TooltipTargets = new();
    private static string lastLoggedSummary = string.Empty;

    [HarmonyPostfix]
    [HarmonyPatch(nameof(CampaignCountryInfoUI.GetActiveFleetCount))]
    internal static void GetActiveFleetCountPostfix(Player player, ref string __result)
    {
        if (player == null || string.IsNullOrWhiteSpace(__result))
            return;

        __result = DecorateActiveFleetText(player, __result);
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(CampaignCountryInfoUI.Refresh))]
    internal static void RefreshPostfix(CampaignCountryInfoUI __instance)
        => ApplyToInstance(__instance);

    internal static void ApplyToInstance(CampaignCountryInfoUI __instance)
    {
        try
        {
            if (__instance?.ActiveFleetShips != null)
            {
                Player? player = PlayerController.Instance;
                if (player != null && !string.IsNullOrWhiteSpace(__instance.ActiveFleetShips.text))
                    __instance.ActiveFleetShips.text = DecorateActiveFleetText(player, __instance.ActiveFleetShips.text);

                EnsureTooltip(__instance.ActiveFleetShips.gameObject);
            }
        }
        catch (Exception ex)
        {
            Melon<UADVanillaPlusMod>.Logger.Warning(
                $"UADVP active fleet tooltip setup failed. {ex.GetType().Name}: {ex.Message}");
        }
    }

    internal static int CountActiveVesselsInPort(Player player)
    {
        int count = 0;

        if (CampaignController.Instance?.CampaignData?.VesselsByPlayer == null ||
            !CampaignController.Instance.CampaignData.VesselsByPlayer.TryGetValue(player.data, out var vessels))
        {
            return 0;
        }

        foreach (VesselEntity vessel in vessels)
        {
            if (IsActiveFleetVessel(vessel) && vessel.PortLocation != null)
                count++;
        }

        return count;
    }

    private static bool IsActiveFleetVessel(VesselEntity? vessel)
    {
        if (vessel == null ||
            vessel.isBuilding ||
            vessel.isCommissioning ||
            vessel.isRefit ||
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

    private static string DecorateActiveFleetText(Player player, string text)
    {
        int inPort = CountActiveVesselsInPort(player);
        string result = $"{StripPortCountSuffix(text)} ({inPort} port)";

        string summary = $"{player.Name(false)}:{inPort}";
        if (summary != lastLoggedSummary)
        {
            lastLoggedSummary = summary;
            Melon<UADVanillaPlusMod>.Logger.Msg($"UADVP active fleet status: displayed {inPort} active vessels in port for {player.Name(false)}.");
        }

        return result;
    }

    private static string StripPortCountSuffix(string text)
        => string.IsNullOrWhiteSpace(text) ? string.Empty : PortCountSuffixRegex.Replace(text, string.Empty);

    private static void EnsureTooltip(GameObject target)
    {
        if (TooltipTargets.Contains(target))
            return;

        TooltipTargets.Add(target);

        OnEnter onEnter = target.AddComponent<OnEnter>();
        onEnter.action = new System.Action(() =>
        {
            Player? player = PlayerController.Instance;
            string tooltip = player == null
                ? "Active fleet port count unavailable."
                : $"{CountActiveVesselsInPort(player)} active fleet vessels are currently in port.";
            G.ui.ShowTooltip(tooltip, target);
        });

        OnLeave onLeave = target.AddComponent<OnLeave>();
        onLeave.action = new System.Action(() => G.ui.HideTooltip());
    }
}
