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

    private static void AppendOdds(BattlePopup popup, float playerChance)
    {
        if (popup.MissionDesc == null || popup.MissionDesc.text.Contains(OddsLabel, StringComparison.Ordinal))
            return;

        string separator = string.IsNullOrWhiteSpace(popup.MissionDesc.text) ? string.Empty : "\n\n";
        popup.MissionDesc.text = $"{popup.MissionDesc.text}{separator}{OddsLabel} win chance: {playerChance:0}%";
    }
}
