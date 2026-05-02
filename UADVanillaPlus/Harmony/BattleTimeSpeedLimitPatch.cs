using HarmonyLib;
using Il2Cpp;
using MelonLoader;

namespace UADVanillaPlus.Harmony;

[HarmonyPatch]
internal static class BattleTimeSpeedLimitPatch
{
    private const float MaxBattleTimeScale = 30f;
    private static int lastAutomaticTimeScaleSlowdown;
    private static int lastLoggedAutomaticTimeScaleSlowdown = -1;
    private static int lastUserTimeScale = 1;
    private static bool ignoreNextAutomaticTimeScaleSlowdown;
    private static bool inCombatUpdateTimeSpeedLimit;

    [HarmonyPatch(typeof(TimeControl), nameof(TimeControl.TimeScale))]
    [HarmonyPrefix]
    private static void PrefixTimeScale(ref float scale)
    {
        int requestedScale = ScaleToInt(scale);

        if (inCombatUpdateTimeSpeedLimit)
        {
            if (lastUserTimeScale > requestedScale && requestedScale != lastAutomaticTimeScaleSlowdown)
            {
                lastAutomaticTimeScaleSlowdown = requestedScale;
                LogAutomaticSlowdown(requestedScale);

                if (ignoreNextAutomaticTimeScaleSlowdown)
                {
                    ignoreNextAutomaticTimeScaleSlowdown = false;
                    scale = lastUserTimeScale;
                    Melon<UADVanillaPlusMod>.Logger.Msg($"UADVP battle time: ignored first automatic slowdown and restored {lastUserTimeScale}x.");
                }
            }
            else
            {
                if (ScaleToInt(scale) != lastUserTimeScale)
                    Melon<UADVanillaPlusMod>.Logger.Msg($"UADVP battle time: overriding automatic {requestedScale}x -> player {lastUserTimeScale}x.");

                scale = lastUserTimeScale;
            }

            return;
        }

        if (lastUserTimeScale != requestedScale)
        {
            Melon<UADVanillaPlusMod>.Logger.Msg($"UADVP battle time: player time scale {lastUserTimeScale}x -> {requestedScale}x.");
            lastUserTimeScale = requestedScale;
            ignoreNextAutomaticTimeScaleSlowdown = true;
        }
    }

    [HarmonyPatch(typeof(BattleManager), nameof(BattleManager.CombatUpdateTimeSpeedLimit))]
    [HarmonyPrefix]
    private static void PrefixCombatUpdateTimeSpeedLimit()
    {
        inCombatUpdateTimeSpeedLimit = true;
    }

    [HarmonyPatch(typeof(BattleManager), nameof(BattleManager.CombatUpdateTimeSpeedLimit))]
    [HarmonyPostfix]
    private static void PostfixCombatUpdateTimeSpeedLimit()
    {
        inCombatUpdateTimeSpeedLimit = false;
        SetCombatTimeSpeedLimit();
    }

    [HarmonyPatch(typeof(BattleManager), nameof(BattleManager.LeaveBattle))]
    [HarmonyPostfix]
    private static void PostfixLeaveBattle()
    {
        ResetBattleTimeState();
    }

    private static void SetCombatTimeSpeedLimit()
    {
        if (BattleManager.Instance == null)
            return;

        // VP only lifts the simulation-speed cap. It does not alter the
        // player's chosen speed or unrelated battle state.
        BattleManager.Instance.CombatTimeSpeedLimit = new Il2CppSystem.Nullable<float>(MaxBattleTimeScale);
    }

    private static void ResetBattleTimeState()
    {
        lastAutomaticTimeScaleSlowdown = 0;
        lastLoggedAutomaticTimeScaleSlowdown = -1;
        lastUserTimeScale = 1;
        ignoreNextAutomaticTimeScaleSlowdown = false;
        inCombatUpdateTimeSpeedLimit = false;
        Melon<UADVanillaPlusMod>.Logger.Msg("UADVP battle time: reset state after leaving battle.");
    }

    private static int ScaleToInt(float scale)
        => (int)(scale + 0.1f);

    private static void LogAutomaticSlowdown(int scale)
    {
        if (lastLoggedAutomaticTimeScaleSlowdown == scale)
            return;

        lastLoggedAutomaticTimeScaleSlowdown = scale;
        Melon<UADVanillaPlusMod>.Logger.Msg($"UADVP battle time: observed automatic slowdown to {scale}x.");
    }
}
