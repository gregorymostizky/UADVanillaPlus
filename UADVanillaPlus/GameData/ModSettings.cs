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
    private const string DesignAccuracyPenaltyModeKey = "uadvp_design_accuracy_penalty_mode";
    private const string MajorShipTorpedoesRestrictedKey = "uadvp_major_ship_torpedoes_restricted";
    private const string ShipyardCapacityBalancedKey = "uadvp_shipyard_capacity_balanced";

    private static bool? portStrikeBalanced;
    private static bool? battleWeatherAlwaysSunny;
    private static AccuracyPenaltyMode? designAccuracyPenaltyMode;
    private static bool? majorShipTorpedoesRestricted;
    private static bool? shipyardCapacityBalanced;

    internal enum AccuracyPenaltyMode
    {
        Div10 = 10,
        Div5 = 5,
        Div2 = 2,
        Vanilla = 1,
    }

    internal static bool PortStrikeBalanced
    {
        get => portStrikeBalanced ??= PlayerPrefs.GetInt(PortStrikeBalancedKey, 1) != 0;
        set
        {
            portStrikeBalanced = value;
            PlayerPrefs.SetInt(PortStrikeBalancedKey, value ? 1 : 0);
            PlayerPrefs.Save();
            Melon<UADVanillaPlusMod>.Logger.Msg($"UADVP option: Port Strike mode {(value ? "Balanced" : "Vanilla")}.");
        }
    }

    internal static bool BattleWeatherAlwaysSunny
    {
        get => battleWeatherAlwaysSunny ??= PlayerPrefs.GetInt(BattleWeatherAlwaysSunnyKey, 1) != 0;
        set
        {
            battleWeatherAlwaysSunny = value;
            PlayerPrefs.SetInt(BattleWeatherAlwaysSunnyKey, value ? 1 : 0);
            PlayerPrefs.Save();
            Melon<UADVanillaPlusMod>.Logger.Msg($"UADVP option: Battle Weather mode {(value ? "Always Sunny" : "Vanilla")}.");
        }
    }

    internal static AccuracyPenaltyMode DesignAccuracyPenaltyMode
    {
        get => designAccuracyPenaltyMode ??= LoadAccuracyPenaltyMode();
        set
        {
            if (AccuracyPenaltyBalance.IsBattleOrLoading())
            {
                Melon<UADVanillaPlusMod>.Logger.Warning("UADVP option: Accuracy Penalties cannot be changed while a battle is loading or active.");
                return;
            }

            designAccuracyPenaltyMode = value;
            PlayerPrefs.SetInt(DesignAccuracyPenaltyModeKey, (int)value);
            PlayerPrefs.Save();
            Melon<UADVanillaPlusMod>.Logger.Msg($"UADVP option: Design Accuracy Penalties mode {AccuracyPenaltyModeText(value)}.");
            AccuracyPenaltyBalance.TryReapplyLoadedStats(value);
        }
    }

    internal static bool MajorShipTorpedoesRestricted
    {
        get => majorShipTorpedoesRestricted ??= PlayerPrefs.GetInt(MajorShipTorpedoesRestrictedKey, 1) != 0;
        set
        {
            majorShipTorpedoesRestricted = value;
            PlayerPrefs.SetInt(MajorShipTorpedoesRestrictedKey, value ? 1 : 0);
            PlayerPrefs.Save();
            Melon<UADVanillaPlusMod>.Logger.Msg($"UADVP option: CA+ Torpedoes mode {(value ? "Disallowed" : "Vanilla")}.");
        }
    }

    internal static bool ShipyardCapacityBalanced
    {
        get => shipyardCapacityBalanced ??= PlayerPrefs.GetInt(ShipyardCapacityBalancedKey, 1) != 0;
        set
        {
            shipyardCapacityBalanced = value;
            PlayerPrefs.SetInt(ShipyardCapacityBalancedKey, value ? 1 : 0);
            PlayerPrefs.Save();
            Melon<UADVanillaPlusMod>.Logger.Msg($"UADVP option: Suspend Dock Overcapacity mode {(value ? "Automatic" : "Manual")}.");
        }
    }

    internal static bool DesignAccuracyPenaltiesBalanced
        => DesignAccuracyPenaltyMode != AccuracyPenaltyMode.Vanilla;

    internal static float AccuracyPenaltyDivisor(AccuracyPenaltyMode mode)
        => mode == AccuracyPenaltyMode.Vanilla ? 1f : (float)mode;

    internal static string AccuracyPenaltyModeText(AccuracyPenaltyMode mode)
        => mode == AccuracyPenaltyMode.Vanilla ? "Vanilla" : $"/{(int)mode}";

    private static AccuracyPenaltyMode LoadAccuracyPenaltyMode()
    {
        int stored = PlayerPrefs.GetInt(DesignAccuracyPenaltyModeKey, (int)AccuracyPenaltyMode.Div5);
        return Enum.IsDefined(typeof(AccuracyPenaltyMode), stored) ? (AccuracyPenaltyMode)stored : AccuracyPenaltyMode.Div5;
    }
}
