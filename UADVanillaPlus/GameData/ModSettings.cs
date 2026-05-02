using MelonLoader;
using UnityEngine;

namespace UADVanillaPlus.GameData;

// VP philosophy: balance-affecting features should be controlled in-game, not
// through loose config files. Balance changes default to VP's improved behavior
// while letting players opt back into vanilla rules from the UAD:VP menu.
internal static class ModSettings
{
    private const string PortStrikeBalancedKey = "uadvp_port_strike_balanced";
    private const string BattleWeatherAlwaysSunnyKey = "uadvp_battle_weather_always_sunny";

    internal static bool PortStrikeBalanced
    {
        get => PlayerPrefs.GetInt(PortStrikeBalancedKey, 1) != 0;
        set
        {
            PlayerPrefs.SetInt(PortStrikeBalancedKey, value ? 1 : 0);
            PlayerPrefs.Save();
            Melon<UADVanillaPlusMod>.Logger.Msg($"UADVP option: Port Strike mode {(value ? "Balanced" : "Vanilla")}.");
        }
    }

    internal static bool BattleWeatherAlwaysSunny
    {
        get => PlayerPrefs.GetInt(BattleWeatherAlwaysSunnyKey, 1) != 0;
        set
        {
            PlayerPrefs.SetInt(BattleWeatherAlwaysSunnyKey, value ? 1 : 0);
            PlayerPrefs.Save();
            Melon<UADVanillaPlusMod>.Logger.Msg($"UADVP option: Battle Weather mode {(value ? "Always Sunny" : "Vanilla")}.");
        }
    }
}
