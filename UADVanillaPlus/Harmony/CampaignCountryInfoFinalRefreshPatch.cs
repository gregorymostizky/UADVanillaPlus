using HarmonyLib;
using Il2Cpp;
using MelonLoader;

namespace UADVanillaPlus.Harmony;

// Patch intent: vanilla world refreshes can rebuild campaign country labels
// after individual data updates. Reapplying VP's campaign UI decorations at
// the end of Ui.Refresh gives our text the final pass without polling.
[HarmonyPatch(typeof(Ui), nameof(Ui.Refresh))]
internal static class CampaignCountryInfoFinalRefreshPatch
{
    [HarmonyPostfix]
    internal static void RefreshPostfix(Ui __instance)
    {
        try
        {
            if (!CanRefreshCampaignCountryInfo(__instance, out CampaignCountryInfoUI? countryInfo))
                return;

            CampaignCountryInfoUI safeCountryInfo = countryInfo!;
            CampaignActiveFleetStatusPatch.ApplyToInstance(safeCountryInfo);
            CampaignConstructionStatusPatch.ApplyToInstance(safeCountryInfo);
            CampaignTechnologyStatusPatch.ApplyToInstance(safeCountryInfo);
        }
        catch (Exception ex)
        {
            Melon<UADVanillaPlusMod>.Logger.Warning(
                $"UADVP campaign final UI refresh failed; leaving vanilla text intact. {ex.GetType().Name}: {ex.Message}");
        }
    }

    internal static bool CanRefreshCampaignCountryInfo(Ui? ui, out CampaignCountryInfoUI? countryInfo)
    {
        countryInfo = null;

        if (ui?.CountryInfo == null ||
            PlayerController.Instance == null ||
            CampaignController.Instance?.CampaignData == null)
        {
            return false;
        }

        countryInfo = ui.CountryInfo;
        return true;
    }
}
