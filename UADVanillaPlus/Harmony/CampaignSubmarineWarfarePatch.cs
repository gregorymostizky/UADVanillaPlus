using System.Reflection;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using UADVanillaPlus.GameData;

namespace UADVanillaPlus.Harmony;

// Patch intent: make submarine warfare optional in existing campaigns without
// deleting saved submarines. Disabled mode blocks new construction and new
// submarine battles, then harmlessly dismisses any saved submarine battle that
// already exists.
[HarmonyPatch(typeof(PlayerController))]
internal static class CampaignSubmarineConstructionPatch
{
    internal const string DisabledReason = "submarine_warfare_disabled";

    private static bool loggedFirstBuildBlock;

    [HarmonyPostfix]
    [HarmonyPatch(nameof(PlayerController.CanBuildSubmarineForType))]
    private static void CanBuildSubmarineForTypePostfix(SubmarineType type, Player player, ref string reason, ref bool __result)
    {
        if (!ModSettings.SubmarineWarfareDisabled)
            return;

        __result = false;
        reason = DisabledReason;
        LogFirstBuildBlock(type, player);
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(PlayerController.CanBuildSubmarineOnInit))]
    private static void CanBuildSubmarineOnInitPostfix(SubmarineType submarine, Player player, ref string reason, ref bool __result)
    {
        if (!ModSettings.SubmarineWarfareDisabled)
            return;

        __result = false;
        reason = DisabledReason;
        LogFirstBuildBlock(submarine, player);
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(PlayerController.BuildSubmarines))]
    private static bool BuildSubmarinesPrefix(SubmarineType type, Player owner, ref Il2CppSystem.Collections.Generic.List<Submarine> __result)
    {
        if (!ModSettings.SubmarineWarfareDisabled)
            return true;

        __result = new Il2CppSystem.Collections.Generic.List<Submarine>();
        LogFirstBuildBlock(type, owner);
        return false;
    }

    private static void LogFirstBuildBlock(SubmarineType? type, Player? player)
    {
        if (loggedFirstBuildBlock)
            return;

        loggedFirstBuildBlock = true;
        string typeName = SubmarineWarfareUtility.SubmarineTypeLabel(type);
        string playerName = SubmarineWarfareUtility.PlayerLabel(player);
        Melon<UADVanillaPlusMod>.Logger.Msg($"UADVP submarine warfare: blocking submarine construction because Submarine Warfare is Disabled. First blocked type: {typeName}; player: {playerName}.");
    }
}

[HarmonyPatch]
internal static class CampaignAiSubmarineConstructionPatch
{
    private static bool loggedFirstAiBuildBlock;

    private static MethodBase TargetMethod()
        => AccessTools.Method(typeof(CampaignController), "BuildNewSubmarines", new[] { typeof(Player), typeof(float) })
           ?? throw new MissingMethodException(nameof(CampaignController), "BuildNewSubmarines");

    [HarmonyPrefix]
    private static bool BuildNewSubmarinesPrefix(Player player)
    {
        if (!ModSettings.SubmarineWarfareDisabled)
            return true;

        if (!loggedFirstAiBuildBlock)
        {
            loggedFirstAiBuildBlock = true;
            Melon<UADVanillaPlusMod>.Logger.Msg($"UADVP submarine warfare: skipping monthly submarine construction while disabled. First skipped player: {SubmarineWarfareUtility.PlayerLabel(player)}.");
        }

        return false;
    }
}

[HarmonyPatch(typeof(MissionGenerator))]
internal static class CampaignSubmarineBattleGenerationPatch
{
    private static bool loggedFirstPublicGenerationBlock;

    [HarmonyPrefix]
    [HarmonyPatch(nameof(MissionGenerator.AddSubmarineBattle))]
    private static bool AddSubmarineBattlePrefix(Player a, Player b, BattleTypeEx battleType)
    {
        if (!ModSettings.SubmarineWarfareDisabled)
            return true;

        LogFirstPublicGenerationBlock(a, b, battleType);
        return false;
    }

    private static void LogFirstPublicGenerationBlock(Player? a, Player? b, BattleTypeEx? battleType)
    {
        if (loggedFirstPublicGenerationBlock)
            return;

        loggedFirstPublicGenerationBlock = true;
        Melon<UADVanillaPlusMod>.Logger.Msg(
            $"UADVP submarine warfare: blocking submarine battle generation while disabled. First blocked battle: {SubmarineWarfareUtility.BattleTypeLabel(battleType)}; {SubmarineWarfareUtility.PlayerLabel(a)} vs {SubmarineWarfareUtility.PlayerLabel(b)}.");
    }
}

[HarmonyPatch]
internal static class CampaignSubmarineBattleAddPatch
{
    private static bool loggedFirstPrivateGenerationBlock;

    private static MethodBase TargetMethod()
        => AccessTools.Method(typeof(MissionGenerator), "AddBattle", new[] { typeof(CampaignBattleWithSubmarine) })
           ?? throw new MissingMethodException(nameof(MissionGenerator), "AddBattle");

    [HarmonyPrefix]
    private static bool AddBattlePrefix(CampaignBattleWithSubmarine battle)
    {
        if (!ModSettings.SubmarineWarfareDisabled)
            return true;

        try
        {
            battle?.RefreshShipsStatus();
        }
        catch (Exception ex)
        {
            Melon<UADVanillaPlusMod>.Logger.Warning($"UADVP submarine warfare: failed to release a suppressed submarine battle before registration. {ex.GetType().Name}: {ex.Message}");
        }

        if (!loggedFirstPrivateGenerationBlock)
        {
            loggedFirstPrivateGenerationBlock = true;
            Melon<UADVanillaPlusMod>.Logger.Msg($"UADVP submarine warfare: suppressing submarine battle registration while disabled. First blocked battle id: {SubmarineWarfareUtility.BattleLabel(battle)}.");
        }

        return false;
    }
}

[HarmonyPatch(typeof(BattleManager))]
internal static class CampaignSubmarineBattleResolvePatch
{
    private static bool loggedFirstAutoResolveDismiss;
    private static bool loggedFirstDamageSuppress;
    private static bool loggedFirstSubmarineDamageSuppress;

    [HarmonyPrefix]
    [HarmonyPatch(nameof(BattleManager.SubmarineAutoResolve))]
    private static bool SubmarineAutoResolvePrefix(BattleManager __instance, CampaignBattleWithSubmarine battle)
    {
        if (!ModSettings.SubmarineWarfareDisabled)
            return true;

        DismissBattle(__instance, battle, "auto-resolve");
        return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(BattleManager.CalculateDamage), typeof(CampaignBattleWithSubmarine))]
    private static bool CalculateDamagePrefix(CampaignBattleWithSubmarine battle)
    {
        if (!ModSettings.SubmarineWarfareDisabled)
            return true;

        if (!loggedFirstDamageSuppress)
        {
            loggedFirstDamageSuppress = true;
            Melon<UADVanillaPlusMod>.Logger.Msg($"UADVP submarine warfare: suppressing submarine battle damage while disabled. First battle id: {SubmarineWarfareUtility.BattleLabel(battle)}.");
        }

        return false;
    }

    private static void DismissBattle(BattleManager manager, CampaignBattleWithSubmarine battle, string context)
    {
        try
        {
            manager.RemoveBattle(battle);
        }
        catch (Exception ex)
        {
            Melon<UADVanillaPlusMod>.Logger.Warning($"UADVP submarine warfare: failed to dismiss disabled submarine battle during {context}. {ex.GetType().Name}: {ex.Message}");
        }

        if (loggedFirstAutoResolveDismiss)
            return;

        loggedFirstAutoResolveDismiss = true;
        Melon<UADVanillaPlusMod>.Logger.Msg($"UADVP submarine warfare: dismissed an existing submarine battle during {context} because Submarine Warfare is Disabled. Battle id: {SubmarineWarfareUtility.BattleLabel(battle)}.");
    }

    [HarmonyPrefix]
    [HarmonyPatch("DamageSubmarine")]
    private static bool DamageSubmarinePrefix(Submarine sub)
    {
        if (!ModSettings.SubmarineWarfareDisabled)
            return true;

        if (!loggedFirstSubmarineDamageSuppress)
        {
            loggedFirstSubmarineDamageSuppress = true;
            Melon<UADVanillaPlusMod>.Logger.Msg($"UADVP submarine warfare: suppressing direct submarine damage while disabled. First submarine: {SubmarineWarfareUtility.SubmarineLabel(sub)}.");
        }

        return false;
    }
}

[HarmonyPatch]
internal static class CampaignSubmarineBattleCompletePatch
{
    private static bool loggedFirstCompleteDismiss;

    private static MethodBase TargetMethod()
        => AccessTools.Method(typeof(BattleManager), "CompleteBattle", new[] { typeof(CampaignBattleWithSubmarine) })
           ?? throw new MissingMethodException(nameof(BattleManager), "CompleteBattle");

    [HarmonyPrefix]
    private static bool CompleteBattlePrefix(BattleManager __instance, CampaignBattleWithSubmarine battle)
    {
        if (!ModSettings.SubmarineWarfareDisabled)
            return true;

        try
        {
            __instance.RemoveBattle(battle);
        }
        catch (Exception ex)
        {
            Melon<UADVanillaPlusMod>.Logger.Warning($"UADVP submarine warfare: failed to dismiss disabled submarine battle during completion. {ex.GetType().Name}: {ex.Message}");
        }

        if (!loggedFirstCompleteDismiss)
        {
            loggedFirstCompleteDismiss = true;
            Melon<UADVanillaPlusMod>.Logger.Msg($"UADVP submarine warfare: dismissed a submarine battle during completion because Submarine Warfare is Disabled. Battle id: {SubmarineWarfareUtility.BattleLabel(battle)}.");
        }

        return false;
    }
}

[HarmonyPatch(typeof(Ui))]
internal static class CampaignSubmarineBuildReasonPatch
{
    [HarmonyPrefix]
    [HarmonyPatch(nameof(Ui.CanNotBuildSubmarineReasonToUI))]
    private static bool CanNotBuildSubmarineReasonToUiPrefix(string reason, ref string __result)
    {
        if (!string.Equals(reason, CampaignSubmarineConstructionPatch.DisabledReason, StringComparison.OrdinalIgnoreCase))
            return true;

        __result = "Submarine warfare is disabled by UAD:VP.";
        return false;
    }
}

internal static class SubmarineWarfareUtility
{
    internal static string PlayerLabel(Player? player)
    {
        if (player == null)
            return "unknown";

        try
        {
            string? name = player.Name(false);
            if (!string.IsNullOrWhiteSpace(name))
                return name;
        }
        catch
        {
        }

        return "unknown";
    }

    internal static string SubmarineTypeLabel(SubmarineType? type)
    {
        string? label = type?.nameUi;
        if (string.IsNullOrWhiteSpace(label))
            label = type?.name;
        if (string.IsNullOrWhiteSpace(label))
            label = type?.type;

        return string.IsNullOrWhiteSpace(label) ? "unknown" : label;
    }

    internal static string SubmarineLabel(Submarine? sub)
    {
        if (sub == null)
            return "unknown";

        try
        {
            string? name = sub.Name(true);
            if (!string.IsNullOrWhiteSpace(name))
                return name;
        }
        catch
        {
        }

        return SubmarineTypeLabel(sub.Type);
    }

    internal static string BattleTypeLabel(BattleTypeEx? battleType)
    {
        string? label = battleType?.BattleType;
        if (string.IsNullOrWhiteSpace(label))
            label = battleType?.name;

        return string.IsNullOrWhiteSpace(label) ? "unknown" : label;
    }

    internal static string BattleLabel(CampaignBattleWithSubmarine? battle)
    {
        try
        {
            return battle == null ? "unknown" : battle.Id.ToString();
        }
        catch
        {
            return "unknown";
        }
    }
}
