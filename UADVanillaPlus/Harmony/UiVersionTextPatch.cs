using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using UnityEngine.UI;

#pragma warning disable CS8602

namespace UADVanillaPlus.Harmony;

[HarmonyPatch(typeof(Ui))]
internal static class UiVersionTextPatch
{
    private static bool hasLoggedVersionText;

    [HarmonyPatch(nameof(Ui.Start))]
    [HarmonyPostfix]
    private static void PostfixStart(Ui __instance)
    {
        UpdateVersionText(__instance);
    }

    [HarmonyPatch(nameof(Ui.RefreshVersion))]
    [HarmonyPostfix]
    private static void PostfixRefreshVersion(Ui __instance)
    {
        UpdateVersionText(__instance);
    }

    private static void UpdateVersionText(Ui ui)
    {
        // VP identity patch: keep the visible corner text aligned with MelonInfo
        // so screenshots/log reports always show the same mod version.
        var versionText = ui.overlayUi.Get("Version", false, false).Get<Text>("VersionText", false, false);
        versionText.text = ModInfo.DisplayText;

        if (hasLoggedVersionText)
            return;

        hasLoggedVersionText = true;
        Melon<UADVanillaPlusMod>.Logger.Msg($"Version overlay set to: {ModInfo.DisplayText}");
    }
}
