using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using UADVanillaPlus.GameData;

namespace UADVanillaPlus.Harmony;

// Patch intent: let players opt out of vanilla's hard 1965 retirement while
// preserving normal campaign-ending conditions such as defeat or bankruptcy.
[HarmonyPatch]
internal static class CampaignEndDatePatch
{
    private const int VanillaRetirementYear = 1965;

    private static int lastSuppressedPopupTurn = -1;
    private static int lastBlockedFinishTurn = -1;

    [HarmonyPrefix]
    [HarmonyPatch(typeof(MessageBoxUI), nameof(MessageBoxUI.Show))]
    private static bool MessageBoxShowPrefix(string header, string text)
    {
        if (ModSettings.CampaignEndDateEnabled || !IsAtOrAfterVanillaRetirement() || !IsVanillaRetirementPopup(header, text))
            return true;

        LogSuppressedPopup();
        return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(CampaignController), nameof(CampaignController.FinishCampaign))]
    private static bool FinishCampaignPrefix(CampaignController __instance, CampaignController.FinishCampaignType finishType)
    {
        if (ModSettings.CampaignEndDateEnabled ||
            finishType != CampaignController.FinishCampaignType.Retirement ||
            !IsAtOrAfterVanillaRetirement(__instance))
        {
            return true;
        }

        LogBlockedFinish(__instance);
        return false;
    }

    private static bool IsVanillaRetirementPopup(string? header, string? text)
    {
        if (string.IsNullOrEmpty(header) || string.IsNullOrEmpty(text))
            return false;

        try
        {
            string campaignFinished = LocalizeManager.Localize("$Ui_World_CampaignFinished");
            string retirement = LocalizeManager.Localize("$Ui_World_Retirement");
            if (string.Equals(header, campaignFinished, StringComparison.Ordinal) &&
                string.Equals(text, retirement, StringComparison.Ordinal))
            {
                return true;
            }
        }
        catch
        {
            // Fall through to the English fallback below. Localization can be
            // unavailable during early boot, but the popup text itself is enough.
        }

        return string.Equals(header, "Campaign Finished", StringComparison.OrdinalIgnoreCase) &&
               string.Equals(text, "Retirement", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAtOrAfterVanillaRetirement(CampaignController? campaign = null)
    {
        try
        {
            campaign ??= CampaignController.Instance;
            return campaign != null && campaign.CurrentDate.AsDate().Year >= VanillaRetirementYear;
        }
        catch
        {
            return false;
        }
    }

    private static void LogSuppressedPopup()
    {
        CampaignController? campaign = CampaignController.Instance;
        int turn = CampaignTurn(campaign);
        if (lastSuppressedPopupTurn == turn)
            return;

        lastSuppressedPopupTurn = turn;
        Melon<UADVanillaPlusMod>.Logger.Msg(
            $"UADVP campaign end date: suppressed vanilla {VanillaRetirementYear} retirement popup at {CampaignDateText(campaign)}; campaign continues.");
    }

    private static void LogBlockedFinish(CampaignController campaign)
    {
        int turn = CampaignTurn(campaign);
        if (lastBlockedFinishTurn == turn)
            return;

        lastBlockedFinishTurn = turn;
        Melon<UADVanillaPlusMod>.Logger.Msg(
            $"UADVP campaign end date: blocked vanilla {VanillaRetirementYear} retirement finish at {CampaignDateText(campaign)}; campaign continues.");
    }

    private static int CampaignTurn(CampaignController? campaign)
    {
        try
        {
            return campaign?.CurrentDate.turn ?? -1;
        }
        catch
        {
            return -1;
        }
    }

    private static string CampaignDateText(CampaignController? campaign)
    {
        try
        {
            return campaign?.CurrentDate.AsDate().ToString("yyyy-MM") ?? "unknown date";
        }
        catch
        {
            return "unknown date";
        }
    }
}
