using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using UADVanillaPlus.GameData;

namespace UADVanillaPlus.Harmony;

// Patch intent: bad weather can make routine battles tedious and unreadable.
// VP's "Always Sunny" battle option keeps vanilla setup intact, then clamps the
// final battle state to daytime, clear weather, and calm wind once per weather
// initialization.
[HarmonyPatch(typeof(BattleManager), "SetWeather")]
internal static class BattleWeatherBalancePatch
{
    [HarmonyPostfix]
    private static void PostfixSetWeather()
    {
        if (!ModSettings.BattleWeatherAlwaysSunny)
            return;

        try
        {
            DayCycleAndWeather? weather = DayCycleAndWeather.Instance();
            if (weather == null)
                return;

            weather.SetDaytime(DayCycleAndWeather.TimesOfDay.Day, false);
            weather.SetWeather(DayCycleAndWeather.WeatherType.Clear);
            weather.SetWind(DayCycleAndWeather.WindStrength.Calm);
            weather.SetStormIntensity(0f, true);
            weather.DesiredStormIntensity = 0f;
            weather.DesiredStormParticlesIntensity = 0f;
            weather.DesiredWavesPower = 0f;

            BattleManager.Instance?.UpdateWeatherDetectionValues();
            Melon<UADVanillaPlusMod>.Logger.Msg("UADVP battle weather: forced daytime, clear weather, calm wind, and no storm.");
        }
        catch (Exception ex)
        {
            Melon<UADVanillaPlusMod>.Logger.Warning($"UADVP battle weather balance failed; keeping vanilla weather. {ex.GetType().Name}: {ex.Message}");
        }
    }
}
