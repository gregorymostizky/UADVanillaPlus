using HarmonyLib;
using Il2Cpp;
using MelonLoader;

namespace UADVanillaPlus.Harmony;

// Patch intent: log design-side accuracy at the moment a campaign battle is
// accepted, before the battle scene and live Ship.HitChance code are involved.
// This keeps the diagnostic near "battle start" while avoiding the unsafe
// combat hit-chance detour that caused CoreCLR access violations.
[HarmonyPatch(typeof(BattleManager), nameof(BattleManager.AcceptBattle))]
internal static class BattleStartAccuracyBreakdownPatch
{
    private static readonly HashSet<string> LoggedBattles = new(StringComparer.Ordinal);

    [HarmonyPrefix]
    private static void PrefixAcceptBattle(CampaignBattle battle, bool autoResolve)
    {
        if (BattleStartShapeDiagnosticPatch.ShouldSkipAccuracySummary(battle, autoResolve))
            return;

        if (battle == null || autoResolve || !LoggedBattles.Add(battle.Id.ToString()))
            return;

        try
        {
            CampaignBattle acceptedBattle = battle;
            LogBattleSide("attacker", acceptedBattle.AttackerShips);
            LogBattleSide("defender", acceptedBattle.DefenderShips);
        }
        catch (Exception ex)
        {
            Melon<UADVanillaPlusMod>.Logger.Warning($"UADVP battle-start accuracy breakdown failed. {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void LogBattleSide(string side, Il2CppSystem.Collections.Generic.List<Ship>? ships)
    {
        AccuracySideSummary summary = new(side);
        if (ships == null)
        {
            Melon<UADVanillaPlusMod>.Logger.Msg(summary.Format());
            return;
        }

        foreach (Ship ship in ships)
        {
            if (ship != null)
                summary.Add(ship);
        }

        Melon<UADVanillaPlusMod>.Logger.Msg(summary.Format());
    }

    private sealed class AccuracySideSummary
    {
        private readonly string side;
        private readonly EffectRange accuracy = new();
        private readonly EffectRange accuracyLong = new();
        private readonly EffectRange accuracyWaves = new();
        private readonly EffectRange accuracyCruise = new();
        private int shipCount;
        private int towerCount;
        private int funnelCount;
        private float accuracySum;
        private float accuracyLongSum;
        private float accuracyWavesSum;
        private float accuracyCruiseSum;
        private float stabilitySum;
        private float beamSum;
        private float draughtSum;
        private float overweightSum;

        internal AccuracySideSummary(string side)
        {
            this.side = side;
        }

        internal void Add(Ship ship)
        {
            shipCount++;
            string shipLabel = SafeShipLabel(ship);

            AddEffect(accuracy, ref accuracySum, SafeStatEffect(ship, "accuracy"), shipLabel);
            AddEffect(accuracyLong, ref accuracyLongSum, SafeStatEffect(ship, "accuracy_long"), shipLabel);
            AddEffect(accuracyWaves, ref accuracyWavesSum, SafeStatEffect(ship, "accuracy_waves"), shipLabel);
            AddEffect(accuracyCruise, ref accuracyCruiseSum, SafeStatEffect(ship, "accuracy_cruise"), shipLabel);

            stabilitySum += SafeStat(ship, "stability");
            beamSum += SafeStat(ship, "beam");
            draughtSum += SafeStat(ship, "draught");
            overweightSum += SafeStat(ship, "overweight");

            CountAccuracyParts(ship, out int towers, out int funnels);
            towerCount += towers;
            funnelCount += funnels;
        }

        internal string Format()
        {
            if (shipCount == 0)
                return $"UADVP battle-start accuracy summary: side={side}, ships=0.";

            return $"UADVP battle-start accuracy summary: side={side}, ships={shipCount}; " +
                $"accuracy avg={FormatPercentFromOne(Average(accuracySum))} worst={accuracy.MinText()} best={accuracy.MaxText()}; " +
                $"long avg={FormatPercentFromOne(Average(accuracyLongSum))}; " +
                $"waves avg={FormatPercentFromOne(Average(accuracyWavesSum))} peak={accuracyWaves.MaxText()}; " +
                $"cruise avg={FormatPercentFromOne(Average(accuracyCruiseSum))} worst={accuracyCruise.MinText()}; " +
                $"hull avg stability={FormatFloat(Average(stabilitySum))}, beam={FormatFloat(Average(beamSum))}, draught={FormatFloat(Average(draughtSum))}, overweight={FormatFloat(Average(overweightSum))}; " +
                $"avg towers={FormatFloat((float)towerCount / shipCount)}, funnels={FormatFloat((float)funnelCount / shipCount)}.";
        }

        private void AddEffect(EffectRange range, ref float sum, float value, string shipLabel)
        {
            sum += value;
            range.Add(value, shipLabel);
        }

        private float Average(float sum)
            => sum / shipCount;
    }

    private sealed class EffectRange
    {
        private bool hasValue;
        private float min;
        private float max;
        private string minShip = string.Empty;
        private string maxShip = string.Empty;

        internal void Add(float value, string shipLabel)
        {
            if (!hasValue)
            {
                hasValue = true;
                min = value;
                max = value;
                minShip = shipLabel;
                maxShip = shipLabel;
                return;
            }

            if (value < min)
            {
                min = value;
                minShip = shipLabel;
            }

            if (value > max)
            {
                max = value;
                maxShip = shipLabel;
            }
        }

        internal string MinText()
            => hasValue ? $"{FormatPercentFromOne(min)} {minShip}" : "n/a";

        internal string MaxText()
            => hasValue ? $"{FormatPercentFromOne(max)} {maxShip}" : "n/a";
    }

    private static float SafeStat(Ship ship, string stat)
    {
        try
        {
            if (G.GameData?.stats == null || ship.stats == null || !G.GameData.stats.TryGetValue(stat, out StatData statData))
                return 1f;

            return ship.stats.TryGetValue(statData, out Ship.StatValue statValue) ? statValue.total : 1f;
        }
        catch
        {
            return 1f;
        }
    }

    private static float SafeStatEffect(Ship ship, string effect)
    {
        try
        {
            return ship.StatEffect(effect);
        }
        catch
        {
            return 1f;
        }
    }

    private static void CountAccuracyParts(Ship ship, out int towerCount, out int funnelCount)
    {
        towerCount = 0;
        funnelCount = 0;

        try
        {
            Il2CppSystem.Collections.Generic.List<Part>? parts = ship.parts;
            if (parts == null)
                return;

            foreach (Part part in parts)
            {
                PartData? data = part?.data;
                if (data == null)
                    continue;

                if (data.isFunnel)
                    funnelCount++;
                if (data.isTowerAny)
                    towerCount++;
            }
        }
        catch
        {
            towerCount = 0;
            funnelCount = 0;
        }
    }

    private static string SafeShipLabel(Ship ship)
        => $"{SafeShipType(ship)} {SafeShipName(ship)}";

    private static string SafeShipName(Ship ship)
    {
        try
        {
            return ship.Name(false, false, false, false, true);
        }
        catch
        {
            return "<ship>";
        }
    }

    private static string SafePlayerName(Ship ship)
    {
        try
        {
            return ship.player?.Name(false) ?? "<player>";
        }
        catch
        {
            return "<player>";
        }
    }

    private static string SafeShipType(Ship ship)
    {
        try
        {
            return ship.shipType?.nameUi ?? ship.shipType?.name ?? "?";
        }
        catch
        {
            return "?";
        }
    }

    private static string FormatPercentFromOne(float value)
        => $"{(value - 1f) * 100f:+0.#;-0.#;0}%";

    private static string FormatFloat(float value)
        => value.ToString("0.###");
}
