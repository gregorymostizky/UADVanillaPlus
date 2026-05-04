using System.Collections;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using UADVanillaPlus.GameData;
using UnityEngine;

namespace UADVanillaPlus.Harmony;

// Patch intent: bad weather can make routine battles tedious and unreadable.
// VP's "Always Sunny" battle option keeps vanilla setup intact, then clamps
// battle weather after vanilla chooses it. Avoid early DayCycle scene-load hooks;
// that controller is fragile while the battle scene is still coming online.
internal static class BattleWeatherBalancePatch
{
    private const int BattleEntryRetryAttempts = 30;
    private const float BattleEntryRetryDelaySeconds = 0.5f;
    private const int BattleWeatherReadbackAttempts = 8;
    private const float BattleWeatherReadbackDelaySeconds = 1f;
    private const float BattleWeatherGuardDelaySeconds = 8f;

    private static bool isApplyingAlwaysSunny;
    private static bool isBattleWeatherActive;
    private static bool loggedWaitingForWeather;
    private static string lastAppliedWeatherState = string.Empty;
    private static int retryGeneration;
    private static int readbackGeneration;
    private static int guardGeneration;

    internal static bool ApplyAlwaysSunny(string context, bool allowLoadingBattle = false, DayCycleAndWeather? weather = null)
    {
        if (!ShouldForceAlwaysSunny(allowLoadingBattle) || isApplyingAlwaysSunny)
            return false;

        try
        {
            weather ??= DayCycleAndWeather.Instance();
            if (weather == null)
            {
                LogWaitingForWeather(context);
                return false;
            }

            string beforeState = SafeWeatherState(weather);
            loggedWaitingForWeather = false;
            if (IsAlwaysSunny(weather))
            {
                LogConfirmedAlwaysSunny(context, beforeState);
                return true;
            }

            isApplyingAlwaysSunny = true;
            weather.SetDaytime(DayCycleAndWeather.TimesOfDay.Day, false);
            weather.SetWeather(DayCycleAndWeather.WeatherType.Clear);
            weather.SetWind(DayCycleAndWeather.WindStrength.Calm);
            weather.SetStormIntensity(0f, true);
            weather.DesiredStormIntensity = 0f;
            weather.DesiredStormParticlesIntensity = 0f;
            weather.DesiredWavesPower = 0f;
            TryUpdateWeatherDetectionValues(context);

            string afterState = SafeWeatherState(weather);
            if (beforeState != afterState || lastAppliedWeatherState != afterState)
            {
                lastAppliedWeatherState = afterState;
                Melon<UADVanillaPlusMod>.Logger.Msg(
                    $"UADVP battle weather: forced Always Sunny via {context}. before={beforeState}; after={afterState}.");
            }

            ScheduleReadbackConfirmation(context);
            return true;
        }
        catch (Exception ex)
        {
            Melon<UADVanillaPlusMod>.Logger.Warning($"UADVP battle weather balance failed; keeping vanilla weather. {ex.GetType().Name}: {ex.Message}");
            return false;
        }
        finally
        {
            isApplyingAlwaysSunny = false;
        }
    }

    internal static void EnterBattleWeather()
    {
        if (!ModSettings.BattleWeatherAlwaysSunny)
            return;

        isBattleWeatherActive = true;
        guardGeneration++;
        if (!ApplyAlwaysSunny("GameManager.OnEnterState Battle", allowLoadingBattle: true))
            ScheduleBattleEntryRetry("GameManager.OnEnterState Battle");

        MelonCoroutines.Start(GuardBattleWeather(guardGeneration));
    }

    internal static void ScheduleBattleEntryRetry(string context)
    {
        if (!ModSettings.BattleWeatherAlwaysSunny)
            return;

        int generation = ++retryGeneration;
        MelonCoroutines.Start(RetryBattleWeatherWhenReady(context, generation));
    }

    internal static void ResetLoggedWeatherState()
    {
        isBattleWeatherActive = false;
        retryGeneration++;
        readbackGeneration++;
        guardGeneration++;
        loggedWaitingForWeather = false;
        lastAppliedWeatherState = string.Empty;
    }

    private static bool ShouldForceAlwaysSunny(bool allowLoadingBattle)
        => ModSettings.BattleWeatherAlwaysSunny && (isBattleWeatherActive || GameManager.IsBattle || allowLoadingBattle);

    private static IEnumerator RetryBattleWeatherWhenReady(string context, int generation)
    {
        for (int attempt = 1; attempt <= BattleEntryRetryAttempts; attempt++)
        {
            yield return new WaitForSeconds(BattleEntryRetryDelaySeconds);

            if (generation != retryGeneration || !ModSettings.BattleWeatherAlwaysSunny)
                yield break;

            if (!isBattleWeatherActive && !GameManager.IsBattle && !GameManager.IsLoadingBattle)
                continue;

            if (ApplyAlwaysSunny($"{context} retry {attempt}", allowLoadingBattle: true))
                yield break;
        }

        if (ModSettings.BattleWeatherAlwaysSunny && (isBattleWeatherActive || GameManager.IsBattle))
            Melon<UADVanillaPlusMod>.Logger.Warning("UADVP battle weather: DayCycleAndWeather was not ready after battle-entry retries.");
    }

    private static void ScheduleReadbackConfirmation(string context)
    {
        if (!ModSettings.BattleWeatherAlwaysSunny)
            return;

        int generation = ++readbackGeneration;
        MelonCoroutines.Start(ConfirmWeatherAfterReadbackDelay(context, generation));
    }

    private static IEnumerator ConfirmWeatherAfterReadbackDelay(string context, int generation)
    {
        for (int attempt = 1; attempt <= BattleWeatherReadbackAttempts; attempt++)
        {
            yield return new WaitForSeconds(BattleWeatherReadbackDelaySeconds);

            if (generation != readbackGeneration || !ModSettings.BattleWeatherAlwaysSunny)
                yield break;

            if (!isBattleWeatherActive && !GameManager.IsBattle && !GameManager.IsLoadingBattle)
                continue;

            DayCycleAndWeather? weather = DayCycleAndWeather.Instance();
            if (weather == null)
                continue;

            string state = SafeWeatherState(weather);
            if (IsWeatherStateUnavailable(state))
                continue;

            if (IsAlwaysSunny(weather))
            {
                TryUpdateWeatherDetectionValues($"{context} readback");
                LogConfirmedAlwaysSunny($"{context} readback {attempt}", state);
                yield break;
            }

            Melon<UADVanillaPlusMod>.Logger.Warning(
                $"UADVP battle weather: readback after {context} found non-sunny state; reapplying. state={state}.");
            ApplyAlwaysSunny($"{context} readback correction", allowLoadingBattle: true, weather);
            yield break;
        }

        if (generation == readbackGeneration && ModSettings.BattleWeatherAlwaysSunny && (isBattleWeatherActive || GameManager.IsBattle))
            Melon<UADVanillaPlusMod>.Logger.Warning($"UADVP battle weather: could not read back final Always Sunny state after {context}.");
    }

    private static IEnumerator GuardBattleWeather(int generation)
    {
        while (generation == guardGeneration && ModSettings.BattleWeatherAlwaysSunny)
        {
            yield return new WaitForSeconds(BattleWeatherGuardDelaySeconds);

            if (generation != guardGeneration || !isBattleWeatherActive || !ModSettings.BattleWeatherAlwaysSunny)
                yield break;

            ApplyAlwaysSunny("battle weather guard");
        }
    }

    private static void LogWaitingForWeather(string context)
    {
        if (loggedWaitingForWeather)
            return;

        loggedWaitingForWeather = true;
        Melon<UADVanillaPlusMod>.Logger.Msg($"UADVP battle weather: waiting for DayCycleAndWeather during {context}.");
    }

    private static void LogConfirmedAlwaysSunny(string context, string state)
    {
        if (lastAppliedWeatherState == state)
            return;

        lastAppliedWeatherState = state;
        Melon<UADVanillaPlusMod>.Logger.Msg($"UADVP battle weather: confirmed Always Sunny via {context}. state={state}.");
    }

    private static void TryUpdateWeatherDetectionValues(string context)
    {
        if (!GameManager.IsBattle)
            return;

        try
        {
            BattleManager.Instance?.UpdateWeatherDetectionValues();
        }
        catch (Exception ex)
        {
            Melon<UADVanillaPlusMod>.Logger.Warning(
                $"UADVP battle weather: skipped detection refresh during {context}; battle weather data was not ready. {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static bool IsAlwaysSunny(DayCycleAndWeather weather)
    {
        try
        {
            float windProgress;
            return weather.GetTimeOfDay() == DayCycleAndWeather.TimesOfDay.Day
                && weather.GetWeatherType() == DayCycleAndWeather.WeatherType.Clear
                && weather.GetWind(out windProgress) == DayCycleAndWeather.WindStrength.Calm
                && Math.Abs(weather.StormIntensity) < 0.001f
                && Math.Abs(weather.DesiredStormIntensity) < 0.001f
                && Math.Abs(weather.DesiredStormParticlesIntensity) < 0.001f
                && Math.Abs(weather.DesiredWavesPower) < 0.001f;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsWeatherStateUnavailable(string state)
        => state.StartsWith("unavailable:", StringComparison.Ordinal);

    private static string SafeWeatherState(DayCycleAndWeather weather)
    {
        try
        {
            float windProgress;
            DayCycleAndWeather.TimesOfDay time = weather.GetTimeOfDay();
            DayCycleAndWeather.WeatherType weatherType = weather.GetWeatherType();
            DayCycleAndWeather.WindStrength wind = weather.GetWind(out windProgress);
            return $"time={time}, weather={weatherType}, wind={wind}, storm={weather.StormIntensity:0.###}/{weather.DesiredStormIntensity:0.###}, waves={weather.DesiredWavesPower:0.###}";
        }
        catch (Exception ex)
        {
            return $"unavailable:{ex.GetType().Name}";
        }
    }

    internal static void CorrectLiveWeather(string context, DayCycleAndWeather __instance)
        => ApplyAlwaysSunny(context, allowLoadingBattle: false, weather: __instance);
}

[HarmonyPatch(typeof(BattleManager), "SetWeather")]
internal static class BattleWeatherBattleManagerSetWeatherPatch
{
    [HarmonyPostfix]
    private static void PostfixSetWeather()
    {
        if (!BattleWeatherBalancePatch.ApplyAlwaysSunny("BattleManager.SetWeather", allowLoadingBattle: true))
            BattleWeatherBalancePatch.ScheduleBattleEntryRetry("BattleManager.SetWeather");
    }
}

[HarmonyPatch(typeof(GameManager), nameof(GameManager.OnEnterState))]
internal static class BattleWeatherGameManagerEnterStatePatch
{
    [HarmonyPostfix]
    private static void PostfixEnterState(GameManager.GameState state)
    {
        if (state == GameManager.GameState.Battle)
            BattleWeatherBalancePatch.EnterBattleWeather();
    }
}

[HarmonyPatch(typeof(GameManager), nameof(GameManager.OnLeaveState))]
internal static class BattleWeatherGameManagerLeaveStatePatch
{
    [HarmonyPostfix]
    private static void PostfixLeaveState(GameManager.GameState state)
    {
        if (state == GameManager.GameState.Battle)
            BattleWeatherBalancePatch.ResetLoggedWeatherState();
    }
}

[HarmonyPatch(typeof(BattleManager), nameof(BattleManager.LeaveBattle))]
internal static class BattleWeatherBattleManagerLeavePatch
{
    [HarmonyPostfix]
    private static void PostfixLeaveBattle()
        => BattleWeatherBalancePatch.ResetLoggedWeatherState();
}

[HarmonyPatch(typeof(DayCycleAndWeather), nameof(DayCycleAndWeather.SetDaytime))]
internal static class BattleWeatherDaytimePatch
{
    [HarmonyPostfix]
    private static void PostfixSetDaytime(DayCycleAndWeather __instance)
        => BattleWeatherBalancePatch.CorrectLiveWeather("DayCycleAndWeather.SetDaytime", __instance);
}

[HarmonyPatch(typeof(DayCycleAndWeather), nameof(DayCycleAndWeather.SetWeather))]
internal static class BattleWeatherDayCycleSetWeatherPatch
{
    [HarmonyPostfix]
    private static void PostfixSetWeather(DayCycleAndWeather __instance)
        => BattleWeatherBalancePatch.CorrectLiveWeather("DayCycleAndWeather.SetWeather", __instance);
}

[HarmonyPatch(typeof(DayCycleAndWeather), nameof(DayCycleAndWeather.SetWind), new[] { typeof(DayCycleAndWeather.WindStrength) })]
internal static class BattleWeatherDayCycleSetWindPatch
{
    [HarmonyPostfix]
    private static void PostfixSetWind(DayCycleAndWeather __instance)
        => BattleWeatherBalancePatch.CorrectLiveWeather("DayCycleAndWeather.SetWind", __instance);
}

[HarmonyPatch(typeof(DayCycleAndWeather), nameof(DayCycleAndWeather.SetWind), new[] { typeof(DayCycleAndWeather.WindStrength), typeof(DayCycleAndWeather.WindStrength) })]
internal static class BattleWeatherDayCycleSetWindRangePatch
{
    [HarmonyPostfix]
    private static void PostfixSetWindRange(DayCycleAndWeather __instance)
        => BattleWeatherBalancePatch.CorrectLiveWeather("DayCycleAndWeather.SetWind range", __instance);
}

[HarmonyPatch(typeof(DayCycleAndWeather), nameof(DayCycleAndWeather.SetStormIntensity))]
internal static class BattleWeatherDayCycleStormPatch
{
    [HarmonyPostfix]
    private static void PostfixSetStormIntensity(DayCycleAndWeather __instance)
        => BattleWeatherBalancePatch.CorrectLiveWeather("DayCycleAndWeather.SetStormIntensity", __instance);
}

[HarmonyPatch(typeof(DayCycleAndWeather), nameof(DayCycleAndWeather.SetPreset), new[] { typeof(int), typeof(bool) })]
internal static class BattleWeatherDayCyclePresetPatch
{
    [HarmonyPostfix]
    private static void PostfixSetPreset(DayCycleAndWeather __instance)
        => BattleWeatherBalancePatch.CorrectLiveWeather("DayCycleAndWeather.SetPreset", __instance);
}
