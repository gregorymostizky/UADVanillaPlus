using HarmonyLib;
using Il2Cpp;
using UADVanillaPlus.GameData;

namespace UADVanillaPlus.Harmony;

// Patch intent: tone down extreme design-side accuracy penalties at data parse
// time. This keeps the actual battle accuracy path identical to vanilla: it
// reads already-parsed stat curves and pays no VP-specific per-shot cost.
[HarmonyPatch(typeof(StatData), nameof(StatData.PostProcess))]
internal static class BattleAccuracyPenaltyBalancePatch
{
    [HarmonyPrefix]
    private static void PrefixPostProcess(StatData __instance)
    {
        AccuracyPenaltyBalance.PrepareStatForVanillaPostProcess(__instance);
    }
}
