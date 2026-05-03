using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace UADVanillaPlus.Harmony;

// Patch intent: surface vanilla's own auto-resolve win chance in the campaign
// battle popup. This stays read-only by using CampaignBattle.VictoryChance
// instead of calling BattleManager.AutoResolveBattle, which mutates battle
// state, rolls damage, calculates VP, and completes the battle.
[HarmonyPatch]
internal static class CampaignBattleAutoResolveOddsPatch
{
    private const string OddsLabel = "Auto-resolve";
    private static string lastLoggedSummary = string.Empty;

    [HarmonyPostfix]
    [HarmonyPatch(typeof(BattlePopup), nameof(BattlePopup.Init), typeof(CampaignBattle))]
    private static void BattlePopupInitPostfix(BattlePopup __instance, CampaignBattle battle)
    {
        try
        {
            Player? player = PlayerController.Instance;
            if (__instance == null || battle == null || player == null)
                return;

            StripOdds(__instance);

            if (battle.CurrentState != CampaignBattleBase.State.Active || IsResultPopup(__instance))
                return;

            float playerChance = Mathf.Clamp(battle.VictoryChance(player, false, battle), 0f, 100f);
            AppendOdds(__instance, playerChance);

            string summary = $"{battle.Id}:{playerChance:0}";
            if (summary != lastLoggedSummary)
            {
                lastLoggedSummary = summary;
                Melon<UADVanillaPlusMod>.Logger.Msg(
                    $"UADVP auto-resolve odds: displayed {playerChance:0}% player win chance for battle {battle.Id}.");
            }
        }
        catch (Exception ex)
        {
            Melon<UADVanillaPlusMod>.Logger.Warning(
                $"UADVP auto-resolve odds patch failed; leaving vanilla popup text intact. {ex.GetType().Name}: {ex.Message}");
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(BattlePopup), nameof(BattlePopup.Update))]
    private static void BattlePopupUpdatePostfix(BattlePopup __instance)
    {
        try
        {
            if (__instance != null && IsResultPopup(__instance))
                StripOdds(__instance);
        }
        catch (Exception ex)
        {
            Melon<UADVanillaPlusMod>.Logger.Warning(
                $"UADVP auto-resolve odds cleanup failed; leaving current popup text intact. {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void AppendOdds(BattlePopup popup, float playerChance)
    {
        if (popup.MissionDesc == null)
            return;

        string separator = string.IsNullOrWhiteSpace(popup.MissionDesc.text) ? string.Empty : "\n\n";
        popup.MissionDesc.text = $"{popup.MissionDesc.text}{separator}{OddsLabel} win chance: {playerChance:0}%";
    }

    private static void StripOdds(BattlePopup popup)
    {
        if (popup.MissionDesc == null || string.IsNullOrEmpty(popup.MissionDesc.text))
            return;

        popup.MissionDesc.text = StripOddsText(popup.MissionDesc.text);
    }

    private static string StripOddsText(string text)
    {
        string[] lines = text.Replace("\r", string.Empty).Split('\n');
        List<string> kept = new();
        foreach (string line in lines)
        {
            if (line.TrimStart().StartsWith($"{OddsLabel} win chance:", StringComparison.Ordinal))
                continue;

            kept.Add(line);
        }

        while (kept.Count > 0 && string.IsNullOrWhiteSpace(kept[^1]))
            kept.RemoveAt(kept.Count - 1);

        return string.Join("\n", kept);
    }

    private static bool IsResultPopup(BattlePopup popup)
        => IsActive(popup.ResultButtons) ||
           IsActive(popup.BattleResultInfoObject) ||
           IsActive(popup.BattleResultClose?.gameObject);

    private static bool IsActive(GameObject? gameObject)
        => gameObject != null && gameObject.activeInHierarchy;
}
