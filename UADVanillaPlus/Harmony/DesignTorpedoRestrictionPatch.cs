using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using UADVanillaPlus.GameData;

namespace UADVanillaPlus.Harmony;

// Patch intent: make major combatants feel less like torpedo boats by removing
// torpedo launchers from the part availability list for CA and larger designs.
// Hooking vanilla availability keeps player UI and AI/random design generation
// on the same rule without scanning or mutating ship designs after the fact.
[HarmonyPatch(typeof(Ship))]
internal static class DesignTorpedoRestrictionPatch
{
    private static readonly HashSet<string> LoggedShipTypes = new(StringComparer.OrdinalIgnoreCase);

    [HarmonyPostfix]
    [HarmonyPatch(nameof(Ship.IsPartAvailable), typeof(PartData), typeof(Player), typeof(ShipType), typeof(Ship))]
    internal static void IsPartAvailablePostfix(PartData part, ShipType shipType, ref bool __result)
    {
        if (!__result || !ModSettings.MajorShipTorpedoesRestricted || !IsTorpedo(part) || !IsMajorShipType(shipType))
            return;

        __result = false;
        LogFirstBlock(part, shipType);
    }

    private static bool IsTorpedo(PartData? part)
        => part?.isTorpedo == true
           || IsText(part?.type, "torpedo")
           || IsText(part?.name, "torpedo");

    private static bool IsMajorShipType(ShipType? shipType)
    {
        string label = ShipTypeLabel(shipType);
        return label is "CA" or "BC" or "BB"
               || label.Contains("HEAVY CRUISER", StringComparison.OrdinalIgnoreCase)
               || label.Contains("BATTLECRUISER", StringComparison.OrdinalIgnoreCase)
               || label.Contains("BATTLESHIP", StringComparison.OrdinalIgnoreCase);
    }

    private static string ShipTypeLabel(ShipType? shipType)
    {
        string? label = shipType?.nameUi;
        if (string.IsNullOrWhiteSpace(label))
            label = shipType?.name;
        if (string.IsNullOrWhiteSpace(label))
            label = shipType?.nameFull;

        return string.IsNullOrWhiteSpace(label) ? string.Empty : label.Trim().ToUpperInvariant();
    }

    private static bool IsText(string? value, string needle)
        => !string.IsNullOrWhiteSpace(value) && value.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

    private static void LogFirstBlock(PartData part, ShipType shipType)
    {
        string label = ShipTypeLabel(shipType);
        if (!LoggedShipTypes.Add(label))
            return;

        string partName = string.IsNullOrWhiteSpace(part.nameUi) ? part.name : part.nameUi;
        Melon<UADVanillaPlusMod>.Logger.Msg($"UADVP design balance: hiding torpedo parts for {label}. First blocked part: {partName}.");
    }
}
