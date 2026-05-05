using System.Reflection;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using UADVanillaPlus.GameData;

namespace UADVanillaPlus.Harmony;

// Patch intent: make mine warfare fully optional without mutating existing
// campaign saves. Existing minefields and mounted mine gear can remain in saved
// data, but disabled mode suppresses damage and hides future mine equipment.
[HarmonyPatch(typeof(MinesFieldManager))]
internal static class CampaignMinefieldDamagePatch
{
    private static bool loggedFirstSuppression;

    [HarmonyPrefix]
    [HarmonyPatch(nameof(MinesFieldManager.DamageTaskForce))]
    private static bool DamageTaskForcePrefix(ref float __result)
    {
        if (!ModSettings.MineWarfareDisabled)
            return true;

        __result = 0f;
        LogFirstSuppression();
        return false;
    }

    private static void LogFirstSuppression()
    {
        if (loggedFirstSuppression)
            return;

        loggedFirstSuppression = true;
        Melon<UADVanillaPlusMod>.Logger.Msg("UADVP mine warfare: minefield damage suppressed because Mine Warfare is Disabled.");
    }
}

[HarmonyPatch(typeof(Ship))]
internal static class DesignMinePartAvailabilityPatch
{
    private static bool loggedFirstPart;

    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    [HarmonyPatch(nameof(Ship.IsPartAvailable), typeof(PartData), typeof(Player), typeof(ShipType), typeof(Ship))]
    internal static void IsPartAvailablePostfix(PartData part, ref bool __result)
    {
        if (!ModSettings.MineWarfareDisabled || !MineWarfareDetector.IsMinePart(part))
            return;

        bool wasAvailable = __result;
        __result = false;

        if (wasAvailable)
            LogFirstPart(part);
    }

    private static void LogFirstPart(PartData part)
    {
        if (loggedFirstPart)
            return;

        loggedFirstPart = true;
        Melon<UADVanillaPlusMod>.Logger.Msg($"UADVP mine warfare: hiding mine-related designer parts. First blocked part: {MineWarfareDetector.PartLabel(part)}.");
    }
}

[HarmonyPatch]
internal static class DesignMineComponentAvailabilityPatch
{
    private const string DisabledReason = "mine_warfare_disabled";

    private static bool loggedFirstComponent;

    private static MethodBase TargetMethod()
        => AccessTools.Method(typeof(Ship), nameof(Ship.IsComponentAvailable), new[] { typeof(ComponentData), typeof(string).MakeByRefType() })
           ?? throw new MissingMethodException(nameof(Ship), nameof(Ship.IsComponentAvailable));

    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    internal static void IsComponentAvailablePostfix(ComponentData component, ref string reason, ref bool __result)
    {
        if (!ModSettings.MineWarfareDisabled || !MineWarfareDetector.IsMineComponent(component))
            return;

        bool wasAvailable = __result;
        __result = false;
        reason = DisabledReason;

        if (wasAvailable)
            LogFirstComponent(component);
    }

    private static void LogFirstComponent(ComponentData component)
    {
        if (loggedFirstComponent)
            return;

        loggedFirstComponent = true;
        Melon<UADVanillaPlusMod>.Logger.Msg($"UADVP mine warfare: hiding mine-related designer components. First blocked component: {MineWarfareDetector.ComponentLabel(component)}.");
    }
}

internal static class MineWarfareDetector
{
    private static readonly string[] MineComponentTypes =
    {
        "mines",
        "minesweep",
    };

    private static readonly string[] MineMarkers =
    {
        "mine",
        "mines",
        "minesweep",
        "minesweeping",
        "minelaying",
        "minedetect",
        "minefield",
        "minesweight",
        "minesweep_weight",
        "mines_cost_mod",
        "minesweep_cost_mod",
        "mine_sweeping",
    };

    internal static bool IsMinePart(PartData? part)
    {
        if (part == null)
            return false;

        return ContainsMineMarker(part.type)
               || ContainsMineMarker(part.name)
               || ContainsMineMarker(part.nameUi)
               || ContainsMineMarker(part.param)
               || ContainsMineMarker(part.stats);
    }

    internal static bool IsMineComponent(ComponentData? component)
    {
        if (component == null)
            return false;

        return IsMineComponentType(component.type)
               || IsMineComponentType(component.typex?.name)
               || ContainsMineMarker(component.name)
               || ContainsMineMarker(component.nameShort)
               || ContainsMineMarker(component.nameUi)
               || ContainsMineMarker(component.param)
               || ContainsMineMarker(component.typex?.param);
    }

    internal static string PartLabel(PartData? part)
    {
        string? label = part?.nameUi;
        if (string.IsNullOrWhiteSpace(label))
            label = part?.name;
        if (string.IsNullOrWhiteSpace(label))
            label = part?.type;

        return string.IsNullOrWhiteSpace(label) ? "unknown" : label;
    }

    internal static string ComponentLabel(ComponentData? component)
    {
        string? label = component?.nameUi;
        if (string.IsNullOrWhiteSpace(label))
            label = component?.nameShort;
        if (string.IsNullOrWhiteSpace(label))
            label = component?.name;
        if (string.IsNullOrWhiteSpace(label))
            label = component?.type;

        return string.IsNullOrWhiteSpace(label) ? "unknown" : label;
    }

    private static bool IsMineComponentType(string? value)
    {
        foreach (string type in MineComponentTypes)
        {
            if (string.Equals(value, type, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return ContainsMineMarker(value);
    }

    private static bool ContainsMineMarker(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        foreach (string marker in MineMarkers)
        {
            if (value.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }

        return false;
    }
}
