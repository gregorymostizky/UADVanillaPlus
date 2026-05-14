using HarmonyLib;
using Il2Cpp;
using Il2CppTMPro;
using MelonLoader;
using UnityEngine;

namespace UADVanillaPlus.Harmony;

// Patch intent: some vanilla campaign UI paths rewrite country-info labels
// outside CampaignCountryInfoUI.Refresh/Ui.Refresh, including tab/popup changes
// that happen after Ui.Update. A LateUpdate watchdog cheaply checks for missing
// VP markers and only reapplies the heavier calculations when vanilla has just
// overwritten a visible country-info label.
[HarmonyPatch(typeof(Ui), "LateUpdate")]
internal static class CampaignCountryInfoWatchdogPatch
{
    private const float InstanceCacheRefreshIntervalSeconds = 5f;
    private static readonly List<CampaignCountryInfoUI> CachedInstances = new();
    private static readonly Dictionary<IntPtr, string> LastLoggedStateByInstance = new();
    private static float nextCacheRefreshTime;
    private static string lastLoggedMaintenanceRepair = string.Empty;

    [HarmonyPostfix]
    internal static void LateUpdatePostfix(Ui __instance)
    {
        try
        {
            if (PlayerController.Instance == null ||
                CampaignController.Instance?.CampaignData == null ||
                !CampaignTechnologyStatusPatch.IsCampaignWorldReady())
            {
                return;
            }

            RefreshInstanceCacheIfNeeded();

            int applied = 0;
            bool sawDestroyedInstance = false;
            foreach (CampaignCountryInfoUI countryInfo in CachedInstances)
            {
                if (countryInfo == null)
                {
                    sawDestroyedInstance = true;
                    continue;
                }

                if (!countryInfo.gameObject.activeInHierarchy)
                    continue;

                bool missingMaintenance = !CampaignConstructionStatusPatch.HasMaintenanceMarkers(countryInfo);
                bool missingAnyMarker = MissingAnyVpMarker(countryInfo);
                if (missingAnyMarker)
                {
                    string maintenanceBefore = CampaignConstructionStatusPatch.DebugMaintenanceText(countryInfo);
                    ApplyDecorations(countryInfo);
                    string maintenanceAfter = CampaignConstructionStatusPatch.DebugMaintenanceText(countryInfo);
                    LogMaintenanceRepairOnce(countryInfo, missingMaintenance, maintenanceBefore, maintenanceAfter);
                }

                applied++;

                LogMarkerStateOnce(countryInfo, applied);
            }

            if (sawDestroyedInstance)
                nextCacheRefreshTime = 0f;
        }
        catch (Exception ex)
        {
            Melon<UADVanillaPlusMod>.Logger.Warning(
                $"UADVP campaign watchdog refresh failed; leaving current text intact. {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void RefreshInstanceCacheIfNeeded()
    {
        if (Time.realtimeSinceStartup < nextCacheRefreshTime)
            return;

        nextCacheRefreshTime = Time.realtimeSinceStartup + InstanceCacheRefreshIntervalSeconds;
        CachedInstances.Clear();
        foreach (CampaignCountryInfoUI countryInfo in UnityEngine.Object.FindObjectsOfType<CampaignCountryInfoUI>())
        {
            if (countryInfo != null)
                CachedInstances.Add(countryInfo);
        }
    }

    private static void ApplyDecorations(CampaignCountryInfoUI countryInfo)
    {
        CampaignActiveFleetStatusPatch.ApplyToInstance(countryInfo);
        CampaignConstructionStatusPatch.ApplyToInstance(countryInfo);
        CampaignTechnologyStatusPatch.ApplyToInstance(countryInfo);
    }

    private static bool MissingAnyVpMarker(CampaignCountryInfoUI countryInfo)
        => !HasText(countryInfo.ActiveFleetShips, " port)") ||
           !CampaignConstructionStatusPatch.HasMaintenanceMarkers(countryInfo) ||
           !HasText(countryInfo.Technology, "(Next ");

    private static void LogMarkerStateOnce(CampaignCountryInfoUI countryInfo, int applied)
    {
        bool hasMaintenance = CampaignConstructionStatusPatch.HasMaintenanceMarkers(countryInfo);
        string state = $"{applied}|{countryInfo.gameObject.name}|{HasText(countryInfo.ActiveFleetShips, " port)")}|{hasMaintenance}|{hasMaintenance}|{HasText(countryInfo.Technology, "(Next ")}";
        IntPtr key = countryInfo.Pointer;
        if (LastLoggedStateByInstance.TryGetValue(key, out string? lastState) && state == lastState)
            return;

        LastLoggedStateByInstance[key] = state;
        Melon<UADVanillaPlusMod>.Logger.Msg($"UADVP campaign watchdog markers instance/active/maint/tr/tech: {state}.");
    }

    private static void LogMaintenanceRepairOnce(
        CampaignCountryInfoUI countryInfo,
        bool missingMaintenance,
        string before,
        string after)
    {
        if (!missingMaintenance && before == after)
            return;

        string state = $"{countryInfo.gameObject.name}|missingMaint={missingMaintenance}|before='{before}'|after='{after}'";
        if (state == lastLoggedMaintenanceRepair)
            return;

        lastLoggedMaintenanceRepair = state;
        Melon<UADVanillaPlusMod>.Logger.Msg($"UADVP campaign watchdog maintenance repair: {state}.");
    }

    private static bool HasText(TMP_Text? text, string marker)
        => text != null && !string.IsNullOrEmpty(text.text) && text.text.Contains(marker, StringComparison.Ordinal);
}
