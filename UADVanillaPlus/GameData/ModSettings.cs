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
    private const string ObsoleteDesignRetentionEnabledKey = "uadvp_obsolete_design_retention_enabled";
    private const string ShipyardCapacityBalancedKey = "uadvp_shipyard_capacity_balanced";
    private const string CampaignMapWraparoundEnabledKey = "uadvp_campaign_map_wraparound_enabled";
    private const string EarlyCanalOpeningsEnabledKey = "uadvp_early_canal_openings_enabled";
    private const string OldPanamaCanalEarlyEnabledKey = "uadvp_panama_canal_early_enabled";

    private static bool? portStrikeBalanced;
    private static bool? battleWeatherAlwaysSunny;
    private static AccuracyPenaltyMode? designAccuracyPenaltyMode;
    private static bool? majorShipTorpedoesRestricted;
    private static bool? obsoleteDesignRetentionEnabled;
    private static bool? shipyardCapacityBalanced;
    private static bool? campaignMapWraparoundEnabled;
    private static bool? earlyCanalOpeningsEnabled;

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
            LogCurrentSettings("after Port Strike change");
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
            LogCurrentSettings("after Battle Weather change");
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
            LogCurrentSettings("after Accuracy Penalties change");
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
            LogCurrentSettings("after CA+ Torpedoes change");
        }
    }

    internal static bool ObsoleteDesignRetentionEnabled
    {
        get => obsoleteDesignRetentionEnabled ??= PlayerPrefs.GetInt(ObsoleteDesignRetentionEnabledKey, 0) != 0;
        set
        {
            obsoleteDesignRetentionEnabled = value;
            PlayerPrefs.SetInt(ObsoleteDesignRetentionEnabledKey, value ? 1 : 0);
            PlayerPrefs.Save();
            Melon<UADVanillaPlusMod>.Logger.Msg($"UADVP option: Obsolete Tech & Hulls mode {(value ? "Retain" : "Vanilla")}.");
            LogCurrentSettings("after Obsolete Tech & Hulls change");
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
            LogCurrentSettings("after Suspend Dock Overcapacity change");
        }
    }

    internal static bool CampaignMapWraparoundEnabled
    {
        get => campaignMapWraparoundEnabled ??= PlayerPrefs.GetInt(CampaignMapWraparoundEnabledKey, 0) != 0;
        set
        {
            campaignMapWraparoundEnabled = value;
            PlayerPrefs.SetInt(CampaignMapWraparoundEnabledKey, value ? 1 : 0);
            PlayerPrefs.Save();
            Melon<UADVanillaPlusMod>.Logger.Msg($"UADVP option: Map Geometry {(value ? "Disc World" : "Flat Earth")}.");
            LogCurrentSettings("after Map Geometry change");
        }
    }

    internal static bool EarlyCanalOpeningsEnabled
    {
        get => earlyCanalOpeningsEnabled ??= LoadEarlyCanalOpeningsEnabled();
        set
        {
            earlyCanalOpeningsEnabled = value;
            PlayerPrefs.SetInt(EarlyCanalOpeningsEnabledKey, value ? 1 : 0);
            PlayerPrefs.Save();
            Melon<UADVanillaPlusMod>.Logger.Msg($"UADVP option: Canal Openings mode {(value ? "Early" : "Historical")}.");
            LogCurrentSettings("after Canal Openings change");
        }
    }

    internal static bool DesignAccuracyPenaltiesBalanced
        => DesignAccuracyPenaltyMode != AccuracyPenaltyMode.Vanilla;

    internal static float AccuracyPenaltyDivisor(AccuracyPenaltyMode mode)
        => mode == AccuracyPenaltyMode.Vanilla ? 1f : (float)mode;

    internal static string AccuracyPenaltyModeText(AccuracyPenaltyMode mode)
        => mode == AccuracyPenaltyMode.Vanilla ? "Vanilla" : $"/{(int)mode}";

    internal static void LogCurrentSettings(string context)
    {
        Melon<UADVanillaPlusMod>.Logger.Msg($"UADVP settings ({context}): {CurrentSettingsText()}.");
    }

    private static string CurrentSettingsText()
        => $"Battle Weather={BattleWeatherModeText(BattleWeatherAlwaysSunny)}; " +
           $"Accuracy Penalties={AccuracyPenaltyModeText(DesignAccuracyPenaltyMode)}; " +
           $"Port Strike={PortStrikeModeText(PortStrikeBalanced)}; " +
           $"Suspend Dock Overcapacity={ShipyardCapacityModeText(ShipyardCapacityBalanced)}; " +
           $"Canal Openings={CanalOpeningModeText(EarlyCanalOpeningsEnabled)}; " +
           $"CA+ Torpedoes={MajorShipTorpedoesModeText(MajorShipTorpedoesRestricted)}; " +
           $"Obsolete Tech & Hulls={ObsoleteDesignRetentionModeText(ObsoleteDesignRetentionEnabled)}; " +
           $"Map Geometry={CampaignMapModeText(CampaignMapWraparoundEnabled)}";

    internal static string BattleWeatherModeText(bool alwaysSunny)
        => alwaysSunny ? "Always Sunny" : "Vanilla";

    internal static string PortStrikeModeText(bool balanced)
        => balanced ? "Balanced" : "Vanilla";

    internal static string ShipyardCapacityModeText(bool balanced)
        => balanced ? "Automatic" : "Manual";

    internal static string CanalOpeningModeText(bool early)
        => early ? "Early" : "Historical";

    internal static string MajorShipTorpedoesModeText(bool restricted)
        => restricted ? "Disallowed" : "Vanilla";

    internal static string ObsoleteDesignRetentionModeText(bool enabled)
        => enabled ? "Retain" : "Vanilla";

    internal static string CampaignMapModeText(bool enabled)
        => enabled ? "Disc World" : "Flat Earth";

    private static AccuracyPenaltyMode LoadAccuracyPenaltyMode()
    {
        int stored = PlayerPrefs.GetInt(DesignAccuracyPenaltyModeKey, (int)AccuracyPenaltyMode.Div5);
        return Enum.IsDefined(typeof(AccuracyPenaltyMode), stored) ? (AccuracyPenaltyMode)stored : AccuracyPenaltyMode.Div5;
    }

    private static bool LoadEarlyCanalOpeningsEnabled()
    {
        if (PlayerPrefs.HasKey(EarlyCanalOpeningsEnabledKey))
            return PlayerPrefs.GetInt(EarlyCanalOpeningsEnabledKey, 0) != 0;

        return PlayerPrefs.GetInt(OldPanamaCanalEarlyEnabledKey, 0) != 0;
    }
}
