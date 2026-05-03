using System.Text;
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
        int count = ships?.Count ?? 0;
        Melon<UADVanillaPlusMod>.Logger.Msg($"UADVP battle-start accuracy: side={side}, ships={count}.");

        if (ships == null)
            return;

        foreach (Ship ship in ships)
        {
            if (ship == null)
                continue;

            LogShip(side, ship);
        }
    }

    private static void LogShip(string side, Ship ship)
    {
        StringBuilder line = new();
        line.Append("UADVP battle-start accuracy: ");
        line.Append("side=");
        line.Append(side);
        line.Append(" | ");
        line.Append(SafePlayerName(ship));
        line.Append(" | ");
        line.Append(SafeShipType(ship));
        line.Append(" ");
        line.Append(SafeShipName(ship));
        line.Append(" | base=");
        float accuracy = SafeStat(ship, "accuracy");
        float accuracyLong = SafeStat(ship, "accuracy_long");
        float accuracyEffect = SafeStatEffect(ship, "accuracy");
        float accuracyLongEffect = SafeStatEffect(ship, "accuracy_long");
        float accuracyWavesEffect = SafeStatEffect(ship, "accuracy_waves");

        line.Append(FormatPercentFromOne(accuracy));
        line.Append(" raw=");
        line.Append(FormatFloat(accuracy));
        line.Append(" long=");
        line.Append(FormatPercentFromOne(accuracyLong));
        line.Append(" raw=");
        line.Append(FormatFloat(accuracyLong));
        line.Append(" accEffect=");
        line.Append(FormatPercentFromOne(accuracyEffect));
        line.Append(" raw=");
        line.Append(FormatFloat(accuracyEffect));
        line.Append(" longEffect=");
        line.Append(FormatPercentFromOne(accuracyLongEffect));
        line.Append(" raw=");
        line.Append(FormatFloat(accuracyLongEffect));
        line.Append(" wavesEffect=");
        line.Append(FormatPercentFromOne(accuracyWavesEffect));
        line.Append(" raw=");
        line.Append(FormatFloat(accuracyWavesEffect));
        line.Append(" | stability=");
        line.Append(FormatFloat(SafeStat(ship, "stability")));
        line.Append(" beam=");
        line.Append(FormatFloat(SafeStat(ship, "beam")));
        line.Append(" draught=");
        line.Append(FormatFloat(SafeStat(ship, "draught")));
        line.Append(" overweight=");
        line.Append(FormatFloat(SafeStat(ship, "overweight")));
        line.Append(" pitch=");
        line.Append(FormatFloat(SafeStat(ship, "pitch")));
        line.Append(" roll=");
        line.Append(FormatFloat(SafeStat(ship, "roll")));
        line.Append(" | mainGun=");
        line.Append(FormatMainGun(ship));

        Melon<UADVanillaPlusMod>.Logger.Msg(line.ToString());
        LogSplit(side, ship, "accuracy");
        LogSplit(side, ship, "accuracy_long");
        LogSplit(side, ship, "accuracy_waves");
        LogSplit(side, ship, "accuracy_cruise");
        LogAccuracyParts(side, ship);
    }

    private static string FormatMainGun(Ship ship)
    {
        try
        {
            PartData? gun = ship.mainGunsGroup;
            if (gun == null)
                return "<none>";

            StringBuilder text = new();
            text.Append(gun.nameUi ?? gun.name ?? "<gun>");
            text.Append(" caliber=");
            text.Append(FormatFloat(gun.caliber));
            text.Append(" barrels=");
            text.Append(gun.barrels);
            text.Append(" grade=");
            text.Append(SafeGunGrade(ship, gun));
            text.Append(" turretAcc=");
            text.Append(FormatPercentFromOne(SafeTurretAccuracy(ship, gun)));
            return text.ToString();
        }
        catch (Exception ex)
        {
            return $"<failed:{ex.GetType().Name}>";
        }
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

    private static void LogSplit(string side, Ship ship, string effect)
    {
        try
        {
            Il2CppSystem.Collections.Generic.Dictionary<string, float>? split = ship.StatEffectSplit(effect);
            if (split == null || split.Count == 0)
                return;

            StringBuilder line = new();
            line.Append("UADVP battle-start accuracy split: side=");
            line.Append(side);
            line.Append(" | ");
            line.Append(SafePlayerName(ship));
            line.Append(" | ");
            line.Append(SafeShipType(ship));
            line.Append(" ");
            line.Append(SafeShipName(ship));
            line.Append(" | ");
            line.Append(effect);
            line.Append(" = ");

            bool first = true;
            foreach (var entry in split)
            {
                if (!first)
                    line.Append(", ");

                first = false;
                line.Append(entry.Key ?? "<null>");
                line.Append(":");
                line.Append(FormatPercentFromOne(entry.Value));
                line.Append(" raw=");
                line.Append(FormatFloat(entry.Value));
            }

            Melon<UADVanillaPlusMod>.Logger.Msg(line.ToString());
        }
        catch (Exception ex)
        {
            Melon<UADVanillaPlusMod>.Logger.Warning(
                $"UADVP battle-start accuracy split failed for {effect} on {SafeShipName(ship)}. {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void LogAccuracyParts(string side, Ship ship)
    {
        try
        {
            List<string> importantParts = new();
            int funnelCount = 0;
            int towerCount = 0;

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

                // Diagnostic intent: identify the design choices behind the
                // accuracy split without touching live combat hit-chance code.
                if (!data.isHull && !data.isTowerAny && !data.isFunnel)
                    continue;

                importantParts.Add(FormatAccuracyPart(ship, data));
            }

            Melon<UADVanillaPlusMod>.Logger.Msg(
                $"UADVP battle-start accuracy parts: side={side} | {SafePlayerName(ship)} | {SafeShipType(ship)} {SafeShipName(ship)} | parts={parts.Count} towers={towerCount} funnels={funnelCount} | {string.Join("; ", importantParts)}");
        }
        catch (Exception ex)
        {
            Melon<UADVanillaPlusMod>.Logger.Warning(
                $"UADVP battle-start accuracy parts failed on {SafeShipName(ship)}. {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static string FormatAccuracyPart(Ship ship, PartData data)
    {
        StringBuilder text = new();
        text.Append(data.isHull ? "hull" : data.isTowerAny ? "tower" : data.isFunnel ? "funnel" : "part");
        text.Append("=");
        text.Append(SafePartName(data, ship));
        AppendPartStat(text, data, "accuracy", "acc");
        AppendPartStat(text, data, "accuracy_long", "long");
        AppendPartStat(text, data, "smoke", "smoke");
        AppendPartStat(text, data, "stability", "stab");
        AppendPartStat(text, data, "beam", "beam");
        AppendPartStat(text, data, "draught", "draught");
        return text.ToString();
    }

    private static void AppendPartStat(StringBuilder text, PartData data, string stat, string label)
    {
        float value = SafePartStat(data, stat);
        if (Math.Abs(value) < 0.0001f)
            return;

        text.Append(" ");
        text.Append(label);
        text.Append("=");
        text.Append(FormatFloat(value));
    }

    private static float SafePartStat(PartData data, string stat)
    {
        try
        {
            if (G.GameData?.stats == null || data.statsx == null || !G.GameData.stats.TryGetValue(stat, out StatData statData))
                return 0f;

            return data.statsx.TryGetValue(statData, out float value) ? value : 0f;
        }
        catch
        {
            return 0f;
        }
    }

    private static int SafeGunGrade(Ship ship, PartData gun)
    {
        try
        {
            return ship.TechGunGrade(gun, false);
        }
        catch
        {
            return 0;
        }
    }

    private static float SafeTurretAccuracy(Ship ship, PartData gun)
    {
        try
        {
            return ship.TechTurretAccuracy(gun);
        }
        catch
        {
            return 1f;
        }
    }

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

    private static string SafePartName(PartData data, Ship ship)
    {
        try
        {
            return Part.Name(data, ship, true, false, false);
        }
        catch
        {
            return data.nameUi ?? data.name ?? "<part>";
        }
    }

    private static string FormatPercentFromOne(float value)
        => $"{(value - 1f) * 100f:+0.#;-0.#;0}%";

    private static string FormatFloat(float value)
        => value.ToString("0.###");
}
