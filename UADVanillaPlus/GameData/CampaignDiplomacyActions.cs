using System.Reflection;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;

namespace UADVanillaPlus.GameData;

internal static class CampaignDiplomacyActions
{
    private const float ForcePeaceAttitudeDelta = 100f;
    private const float ForcePeaceVictoryPointTolerance = 0.01f;
    private static readonly MethodInfo? TakeReparationMethod =
        AccessTools.Method(typeof(CampaignController), "TakeReparation", new[] { typeof(Relation), typeof(Player), typeof(Player) });

    internal static bool CanTriggerWar(Player? attacker, Player? defender, out string reason)
    {
        reason = string.Empty;

        if (GameManager.IsBattle || CampaignController.Instance?.CampaignData?.Relations == null)
        {
            reason = "War can only be triggered from the campaign map.";
            return false;
        }

        if (attacker == null || defender == null)
        {
            reason = "Missing campaign player.";
            return false;
        }

        if (attacker == defender)
        {
            reason = "Cannot declare war on yourself.";
            return false;
        }

        Relation relation = RelationExt.Between(CampaignController.Instance.CampaignData.Relations, attacker, defender);
        if (relation == null)
        {
            reason = "No relation exists between these nations.";
            return false;
        }

        if (relation.isWar)
        {
            reason = "These nations are already at war.";
            return false;
        }

        return true;
    }

    internal static bool CanForcePeace(Player? player, Player? target, out string reason)
    {
        reason = string.Empty;

        if (GameManager.IsBattle || CampaignController.Instance?.CampaignData?.Relations == null)
        {
            reason = "Peace can only be forced from the campaign map.";
            return false;
        }

        if (player == null || target == null)
        {
            reason = "Missing campaign player.";
            return false;
        }

        if (player == target)
        {
            reason = "Cannot force peace with yourself.";
            return false;
        }

        Relation relation = RelationExt.Between(CampaignController.Instance.CampaignData.Relations, player, target);
        if (relation == null)
        {
            reason = "No relation exists between these nations.";
            return false;
        }

        if (!relation.isWar)
        {
            reason = "These nations are not at war.";
            return false;
        }

        if (!relation.CanSignPeace())
        {
            reason = "A peace treaty is currently blocked by campaign events.";
            return false;
        }

        return true;
    }

    internal static bool TriggerWar(Player? attacker, Player? defender)
    {
        if (!CanTriggerWar(attacker, defender, out string reason))
        {
            Melon<UADVanillaPlusMod>.Logger.Warning($"UADVP diplomacy: cannot trigger war. {reason}");
            return false;
        }

        Relation relation = RelationExt.Between(CampaignController.Instance.CampaignData.Relations, attacker, defender);
        float beforeAttitude = relation.attitude;
        bool beforeWar = relation.isWar;

        // VP uses the vanilla attitude transition instead of writing relation
        // fields directly, so the game owns war dates, events, and allied war
        // side effects from the same path it already trusts.
        CampaignController.Instance.AdjustAttitude(
            relation,
            -200f,
            true,
            false,
            "UADVP diplomacy forced war",
            true,
            true,
            false);

        ActionsManager.ChoosenAction = ActionsManager.ActionType.IncreaseTension;

        Melon<UADVanillaPlusMod>.Logger.Msg(
            $"UADVP diplomacy: triggered war {attacker!.Name(false)} vs {defender!.Name(false)}; " +
            $"attitude {beforeAttitude:0.0}->{relation.attitude:0.0}, war {beforeWar}->{relation.isWar}.");

        try { G.ui.RefreshCampaignUI(); }
        catch { }

        return relation.isWar;
    }

    internal static bool ForcePeace(Player? player, Player? target)
    {
        if (!CanForcePeace(player, target, out string reason))
        {
            Melon<UADVanillaPlusMod>.Logger.Warning($"UADVP diplomacy: cannot force peace. {reason}");
            return false;
        }

        Relation relation = RelationExt.Between(CampaignController.Instance.CampaignData.Relations, player, target);
        bool beforeWar = relation.isWar;
        float beforeAttitude = relation.attitude;
        float beforeVictoryPointsA = relation.GetVictoryPointsA();
        float beforeVictoryPointsB = relation.GetVictoryPointsB();
        bool ranReparations = false;
        bool reparationsResult = false;
        string reparationsReason = "not evaluated";

        Melon<UADVanillaPlusMod>.Logger.Msg(
            $"UADVP diplomacy: force peace begin {player!.Name(false)} vs {target!.Name(false)}; " +
            $"attitude={beforeAttitude:0.0}, war={beforeWar}, " +
            $"vpA={beforeVictoryPointsA:0.0} ({NameOrUnknown(relation.a)}), " +
            $"vpB={beforeVictoryPointsB:0.0} ({NameOrUnknown(relation.b)}), " +
            $"canSignPeace={relation.CanSignPeace()}.");

        if (TryGetReparationSides(relation, beforeVictoryPointsA, beforeVictoryPointsB, out Player? winner, out Player? loser, out reparationsReason))
        {
            ranReparations = true;
            reparationsResult = InvokeTakeReparation(relation, winner!, loser!, out reparationsReason);
        }

        // Use the same attitude transition that ends wars in vanilla, but do it
        // after the reparation hook because vanilla may clear victory points
        // during the peace-state transition.
        CampaignController.Instance.AdjustAttitude(
            relation,
            ForcePeaceAttitudeDelta,
            true,
            false,
            "UADVP diplomacy forced peace",
            true,
            true,
            false);

        ActionsManager.AddLastActionTurn(player, target, ActionsManager.ActionType.PeaceTreaty);
        ActionsManager.UpdateLastIntersctTurn();
        ActionsManager.ChoosenPlayer = null!;

        Melon<UADVanillaPlusMod>.Logger.Msg(
            $"UADVP diplomacy: force peace complete {player.Name(false)} vs {target.Name(false)}; " +
            $"attitude {beforeAttitude:0.0}->{relation.attitude:0.0}, war {beforeWar}->{relation.isWar}, " +
            $"vpA {beforeVictoryPointsA:0.0}->{relation.GetVictoryPointsA():0.0}, " +
            $"vpB {beforeVictoryPointsB:0.0}->{relation.GetVictoryPointsB():0.0}, " +
            $"reparationsRan={ranReparations}, reparationsResult={reparationsResult}, reparationsNote='{reparationsReason}'.");

        try { G.ui.RefreshCampaignUI(); }
        catch { }

        return !relation.isWar;
    }

    private static bool TryGetReparationSides(
        Relation relation,
        float victoryPointsA,
        float victoryPointsB,
        out Player? winner,
        out Player? loser,
        out string reason)
    {
        winner = null;
        loser = null;

        float victoryPointDelta = Math.Abs(victoryPointsA - victoryPointsB);
        if (victoryPointDelta <= ForcePeaceVictoryPointTolerance)
        {
            reason = $"skipped; victory points are tied ({victoryPointsA:0.0}-{victoryPointsB:0.0})";
            Melon<UADVanillaPlusMod>.Logger.Msg($"UADVP diplomacy: force peace reparations {reason}.");
            return false;
        }

        if (victoryPointsA > victoryPointsB)
        {
            winner = relation.a;
            loser = relation.b;
        }
        else
        {
            winner = relation.b;
            loser = relation.a;
        }

        if (winner == null || loser == null)
        {
            reason = "skipped; missing winner or loser player";
            Melon<UADVanillaPlusMod>.Logger.Warning($"UADVP diplomacy: force peace reparations {reason}.");
            return false;
        }

        reason = $"winner={NameOrUnknown(winner)}, loser={NameOrUnknown(loser)}, vpDelta={victoryPointDelta:0.0}";
        return true;
    }

    private static bool InvokeTakeReparation(Relation relation, Player winner, Player loser, out string note)
    {
        if (TakeReparationMethod == null)
        {
            note = "failed; CampaignController.TakeReparation method was not found";
            Melon<UADVanillaPlusMod>.Logger.Warning($"UADVP diplomacy: force peace reparations {note}.");
            return false;
        }

        try
        {
            Melon<UADVanillaPlusMod>.Logger.Msg(
                $"UADVP diplomacy: force peace reparations invoking vanilla TakeReparation; " +
                $"winner={NameOrUnknown(winner)}, loser={NameOrUnknown(loser)}, " +
                $"vpA={relation.GetVictoryPointsA():0.0}, vpB={relation.GetVictoryPointsB():0.0}.");

            object? result = TakeReparationMethod.Invoke(
                CampaignController.Instance,
                new object[] { relation, winner, loser });

            bool success = result is bool value && value;
            note = $"TakeReparation returned {success}";

            Melon<UADVanillaPlusMod>.Logger.Msg(
                $"UADVP diplomacy: force peace reparations finished; {note}, " +
                $"warNow={relation.isWar}, vpA={relation.GetVictoryPointsA():0.0}, vpB={relation.GetVictoryPointsB():0.0}.");

            return success;
        }
        catch (Exception ex)
        {
            Exception report = ex is TargetInvocationException { InnerException: not null } ? ex.InnerException : ex;
            note = $"failed; {report.GetType().Name}: {report.Message}";
            Melon<UADVanillaPlusMod>.Logger.Warning($"UADVP diplomacy: force peace reparations {note}");
            return false;
        }
    }

    private static string NameOrUnknown(Player? player)
    {
        if (player == null)
            return "unknown";

        try { return player.Name(false); }
        catch { return "unknown"; }
    }
}
