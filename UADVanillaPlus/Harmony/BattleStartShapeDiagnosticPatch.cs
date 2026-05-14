using HarmonyLib;
using Il2Cpp;
using MelonLoader;

namespace UADVanillaPlus.Harmony;

// Temporary diagnostic: log campaign-battle/player/ship shape around the battle
// start edge without calling expensive ship methods.
[HarmonyPatch(typeof(BattleManager), nameof(BattleManager.AcceptBattle))]
internal static class BattleStartShapeAcceptBattleDiagnosticPatch
{
    [HarmonyPrefix]
    private static void Prefix(CampaignBattle battle, bool isAi, bool autoResolve, bool fromUi)
        => BattleStartShapeDiagnosticPatch.LogAcceptBattlePrefix(battle, isAi, autoResolve, fromUi);

    [HarmonyPostfix]
    private static void Postfix(CampaignBattle battle, bool isAi, bool autoResolve, bool fromUi)
        => BattleStartShapeDiagnosticPatch.LogAcceptBattlePostfix(battle, isAi, autoResolve, fromUi);
}

[HarmonyPatch(typeof(GameManager), nameof(GameManager.OnEnterState))]
internal static class BattleStartShapeStateDiagnosticPatch
{
    [HarmonyPostfix]
    private static void Postfix(GameManager.GameState state)
        => BattleStartShapeDiagnosticPatch.LogGameStateEntered(state);
}

internal static class BattleStartShapeDiagnosticPatch
{
    private static readonly HashSet<string> LoggedAcceptBattleDetails = new(StringComparer.Ordinal);
    private static readonly HashSet<string> LoggedBattleStateDetails = new(StringComparer.Ordinal);
    private static readonly HashSet<string> LoggedAccuracySkips = new(StringComparer.Ordinal);
    private static bool LoggedNoDirectToBattlePatch;

    internal static bool ShouldSkipAccuracySummary(CampaignBattle? battle, bool autoResolve)
    {
        if (battle == null || autoResolve || !IsCampaignBattle(battle))
            return false;

        string battleId = SafeBattleId(battle);
        if (LoggedAccuracySkips.Add(battleId))
        {
            Melon<UADVanillaPlusMod>.Logger.Msg(
                $"UADVP battle-shape: temporarily skipped battle-start accuracy summary for manual campaign battle {battleId}.");
        }

        return true;
    }

    internal static void LogAcceptBattlePrefix(CampaignBattle? battle, bool isAi, bool autoResolve, bool fromUi)
    {
        try
        {
            if (!LoggedNoDirectToBattlePatch)
            {
                LoggedNoDirectToBattlePatch = true;
                Melon<UADVanillaPlusMod>.Logger.Msg(
                    "UADVP battle-shape: direct GameManager.ToBattle diagnostics are disabled in this build.");
            }

            string battleId = SafeBattleId(battle);
            Melon<UADVanillaPlusMod>.Logger.Msg(
                $"UADVP battle-shape: before BattleManager.AcceptBattle battle={battleId}, isAi={isAi}, autoResolve={autoResolve}, fromUi={fromUi}.");

            string detailKey = $"{battleId}|{isAi}|{autoResolve}|{fromUi}";
            if (LoggedAcceptBattleDetails.Add(detailKey))
                LogBattleShape(battle, "AcceptBattle prefix", includeShipDetails: true);
        }
        catch (Exception ex)
        {
            Melon<UADVanillaPlusMod>.Logger.Warning(
                $"UADVP battle-shape: AcceptBattle diagnostic failed. {ex.GetType().Name}: {ex.Message}");
        }
    }

    internal static void LogAcceptBattlePostfix(CampaignBattle? battle, bool isAi, bool autoResolve, bool fromUi)
    {
        try
        {
            Melon<UADVanillaPlusMod>.Logger.Msg(
                $"UADVP battle-shape: BattleManager.AcceptBattle returned battle={SafeBattleId(battle)}, isAi={isAi}, autoResolve={autoResolve}, fromUi={fromUi}.");
        }
        catch (Exception ex)
        {
            Melon<UADVanillaPlusMod>.Logger.Warning(
                $"UADVP battle-shape: AcceptBattle postfix diagnostic failed. {ex.GetType().Name}: {ex.Message}");
        }
    }

    internal static void LogGameStateEntered(GameManager.GameState state)
    {
        if (state != GameManager.GameState.Battle)
            return;

        try
        {
            CampaignBattle? battle = CurrentBattleForDiagnostics();
            string battleId = SafeBattleId(battle);
            Melon<UADVanillaPlusMod>.Logger.Msg(
                $"UADVP battle-shape: entered GameManager.OnEnterState Battle battle={battleId}.");

            if (LoggedBattleStateDetails.Add(battleId))
                LogBattleShape(battle, "OnEnterState Battle", includeShipDetails: false);
        }
        catch (Exception ex)
        {
            Melon<UADVanillaPlusMod>.Logger.Warning(
                $"UADVP battle-shape: OnEnterState diagnostic failed. {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void LogBattleShape(CampaignBattle? battle, string context, bool includeShipDetails)
    {
        if (battle == null)
        {
            Melon<UADVanillaPlusMod>.Logger.Msg($"UADVP battle-shape: {context}: battle=<null>.");
            return;
        }

        Melon<UADVanillaPlusMod>.Logger.Msg(
            $"UADVP battle-shape: {context}: battle={SafeBattleId(battle)}, isCampaign={SafeBool(() => battle.IsCampaignBattle)}, type={SafeBattleType(battle.Type)}, params=attacker[{SafeString(() => battle.Type?.ParamAttacker)}] defender[{SafeString(() => battle.Type?.ParamDefender)}].");
        Melon<UADVanillaPlusMod>.Logger.Msg(
            $"UADVP battle-shape: {context}: attacker={PlayerSummary(battle.Attacker)}.");
        Melon<UADVanillaPlusMod>.Logger.Msg(
            $"UADVP battle-shape: {context}: defender={PlayerSummary(battle.Defender)}.");

        LogParticipants($"{context}: participants attacker", battle.ParticipatePlayersAttacker);
        LogParticipants($"{context}: participants defender", battle.ParticipatePlayersDefender);

        LogShipList($"{context}: AttackerShips", battle.AttackerShips, includeShipDetails);
        LogShipList($"{context}: DefenderShips", battle.DefenderShips, includeShipDetails);
        LogShipList($"{context}: ShipsAdditionalAttacker", battle.ShipsAdditionalAttacker, includeShipDetails);
        LogShipList($"{context}: ShipsAdditionalDefender", battle.ShipsAdditionalDefender, includeShipDetails);
    }

    private static void LogParticipants(string label, Il2CppSystem.Collections.Generic.List<Player>? players)
    {
        int count = SafeCount(players);
        Melon<UADVanillaPlusMod>.Logger.Msg($"UADVP battle-shape: {label}: count={count}.");
        if (players == null)
            return;

        int index = 0;
        foreach (Player player in players)
        {
            Melon<UADVanillaPlusMod>.Logger.Msg(
                $"UADVP battle-shape: {label}[{index}] {PlayerSummary(player)}.");
            index++;
        }
    }

    private static void LogShipList(string label, Il2CppSystem.Collections.Generic.List<Ship>? ships, bool includeDetails)
    {
        int count = SafeCount(ships);
        Melon<UADVanillaPlusMod>.Logger.Msg($"UADVP battle-shape: {label}: count={count}.");
        if (!includeDetails || ships == null)
            return;

        int index = 0;
        foreach (Ship ship in ships)
        {
            Melon<UADVanillaPlusMod>.Logger.Msg(
                $"UADVP battle-shape: {label}[{index}] id={SafeShipId(ship)}, player={ShipPlayerSummary(ship)}, shipTypeName={SafeShipTypeName(ship)}, shipTypeNameUi={SafeShipTypeNameUi(ship)}.");
            index++;
        }
    }

    private static string PlayerSummary(Player? player)
    {
        if (player == null)
            return "<null>";

        return $"dataName={SafePlayerDataName(player)}, dataNameUi={SafePlayerDataNameUi(player)}, safeName={SafePlayerDisplayName(player)}, isMain={SafeBool(() => player.isMain)}, isAi={SafeBool(() => player.isAi)}, isMajor={SafeBool(() => player.isMajor)}, isMedium={SafeBool(() => player.isMedium)}";
    }

    private static string ShipPlayerSummary(Ship? ship)
    {
        Player? player = null;
        try
        {
            player = ship?.player;
        }
        catch
        {
        }

        if (player == null)
            return "<null>";

        return $"{SafePlayerDataName(player)}|isMajor={SafeBool(() => player.isMajor)}|isMedium={SafeBool(() => player.isMedium)}";
    }

    private static string SafeBattleId(CampaignBattle? battle)
    {
        try
        {
            return battle?.Id.ToString() ?? "<null>";
        }
        catch
        {
            return "<battle-id-error>";
        }
    }

    private static string SafeBattleType(BattleTypeEx? type)
    {
        if (type == null)
            return "<null>";

        return $"name={SafeString(() => type.name)}, battleType={SafeString(() => type.BattleType)}, currentType={SafeString(() => type.CurrentType.ToString())}";
    }

    private static string SafePlayerDataName(Player player)
        => SafeString(() => player.data?.name);

    private static string SafePlayerDataNameUi(Player player)
        => SafeString(() => player.data?.nameUi);

    private static string SafePlayerDisplayName(Player player)
        => SafeString(() => player.Name(false));

    private static string SafeShipId(Ship? ship)
    {
        try
        {
            return ship?.id.ToString() ?? "<null>";
        }
        catch
        {
            return "<ship-id-error>";
        }
    }

    private static string SafeShipTypeName(Ship? ship)
        => SafeString(() => ship?.shipType?.name);

    private static string SafeShipTypeNameUi(Ship? ship)
        => SafeString(() => ship?.shipType?.nameUi);

    private static int SafeCount<T>(Il2CppSystem.Collections.Generic.List<T>? items)
    {
        try
        {
            return items?.Count ?? 0;
        }
        catch
        {
            return -1;
        }
    }

    private static bool IsCampaignBattle(CampaignBattle battle)
    {
        try
        {
            return battle.IsCampaignBattle;
        }
        catch
        {
            return false;
        }
    }

    private static CampaignBattle? CurrentBattleForDiagnostics()
    {
        try
        {
            CampaignBattle? battle = G.Battle;
            if (battle != null)
                return battle;
        }
        catch
        {
        }

        try
        {
            return BattleManager.Instance?.CurrentBattle;
        }
        catch
        {
            return null;
        }
    }

    private static string SafeString(Func<string?> read)
    {
        try
        {
            string? value = read();
            return string.IsNullOrWhiteSpace(value) ? "<empty>" : value;
        }
        catch (Exception ex)
        {
            return $"<error:{ex.GetType().Name}>";
        }
    }

    private static string SafeBool(Func<bool> read)
    {
        try
        {
            return read().ToString();
        }
        catch (Exception ex)
        {
            return $"<error:{ex.GetType().Name}>";
        }
    }
}
