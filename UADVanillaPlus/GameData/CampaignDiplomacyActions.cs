using Il2Cpp;
using MelonLoader;

namespace UADVanillaPlus.GameData;

internal static class CampaignDiplomacyActions
{
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
}
