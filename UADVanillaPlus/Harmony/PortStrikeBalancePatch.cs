using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using UADVanillaPlus.GameData;

namespace UADVanillaPlus.Harmony;

// Patch intent: vanilla undefended Port Strike missions spawn a flat 5-10 TR
// target pool, so a lone small raider can erase far too many transports. VP
// keeps the mission but caps resulting TR losses by the attacker's committed
// tonnage, then scales the transport VP to match the adjusted loss count.
[HarmonyPatch(typeof(BattleManager), "CalcVp")]
internal static class PortStrikeBalancePatch
{
    private const float MerchantTonnageEstimate = 5000f;
    private const float PortStrikeEffectiveRaidFactor = 0.5f;
    private const float MinimumRaiderTonnageForOneTransport = 1000f;

    [HarmonyPostfix]
    private static void PostfixCalcVp(CampaignBattle battle)
    {
        if (!ModSettings.PortStrikeBalanced || battle?.Type == null || !IsPortStrike(battle.Type))
            return;

        try
        {
            BalanceDefenderTransportLosses(battle);
        }
        catch (Exception ex)
        {
            Melon<UADVanillaPlusMod>.Logger.Warning($"UADVP port strike balance failed; keeping vanilla result. {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void BalanceDefenderTransportLosses(CampaignBattle battle)
    {
        int vanillaLosses = battle.DefenderTRSank;
        if (vanillaLosses <= 0)
            return;

        float attackerTonnage = TotalTonnage(battle.AttackerShips);
        int balancedLosses = BalancedTransportLossCap(attackerTonnage, vanillaLosses);
        if (balancedLosses >= vanillaLosses)
        {
            Melon<UADVanillaPlusMod>.Logger.Msg(
                $"UADVP port strike balance: no clamp needed. attackerTons={attackerTonnage:0}, vanillaTR={vanillaLosses}.");
            return;
        }

        float oldTransportVp = battle.TransportsPlayerAttacker;
        float ratio = balancedLosses / (float)vanillaLosses;
        float newTransportVp = oldTransportVp * ratio;
        float vpReduction = oldTransportVp - newTransportVp;

        battle.DefenderTRSank = balancedLosses;
        battle.TransportsPlayerAttacker = newTransportVp;
        battle.VictoryPointsAttacker = Math.Max(0f, battle.VictoryPointsAttacker - vpReduction);

        Melon<UADVanillaPlusMod>.Logger.Msg(
            $"UADVP port strike balance: attackerTons={attackerTonnage:0}, vanillaTR={vanillaLosses}, balancedTR={balancedLosses}, transportVP={oldTransportVp:0.##}->{newTransportVp:0.##}.");
    }

    private static int BalancedTransportLossCap(float attackerTonnage, int vanillaLosses)
    {
        if (attackerTonnage < MinimumRaiderTonnageForOneTransport)
            return 0;

        int tonnageCap = (int)Math.Floor(attackerTonnage * PortStrikeEffectiveRaidFactor / MerchantTonnageEstimate);
        tonnageCap = Math.Max(1, tonnageCap);
        return Math.Clamp(tonnageCap, 0, vanillaLosses);
    }

    private static float TotalTonnage(Il2CppSystem.Collections.Generic.List<Ship>? ships)
    {
        if (ships == null)
            return 0f;

        float total = 0f;
        foreach (Ship ship in ships)
        {
            if (ship == null)
                continue;

            try
            {
                total += Math.Max(0f, ship.Tonnage());
            }
            catch
            {
                // Some generated/temporary ships can be incomplete during VP
                // calculation. Skip only that ship and keep the balance pass.
            }
        }

        return total;
    }

    private static bool IsPortStrike(BattleTypeEx type)
        => ContainsParam(type.ParamAttacker, "strike_enemy_port") || ContainsParam(type.ParamDefender, "strike_enemy_port");

    private static bool ContainsParam(string? value, string param)
        => !string.IsNullOrEmpty(value) && value.IndexOf(param, StringComparison.OrdinalIgnoreCase) >= 0;
}
