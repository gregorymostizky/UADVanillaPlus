using System.Diagnostics;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;

namespace UADVanillaPlus.Harmony;

// Patch intent: vanilla keeps commissioning ships out of the campaign build
// summary even though they are still not active ships. VP folds commissioning
// into the existing build count as total(commissioning) so the campaign panel
// stays compact without hiding ships that are nearly ready.
[HarmonyPatch(typeof(CampaignCountryInfoUI))]
internal static class CampaignConstructionStatusPatch
{
    private static string lastLoggedSummary = string.Empty;

    [HarmonyPrefix]
    [HarmonyPatch(nameof(CampaignCountryInfoUI.GetVesselsBuildingCount))]
    internal static bool GetVesselsBuildingCountPrefix(Player player, ref string __result)
    {
        if (!TryBuildCounts(player, out ConstructionCounts counts))
            return true;

        __result = counts.TotalDisplay;
        return false;
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(CampaignCountryInfoUI.Refresh))]
    internal static void RefreshPostfix(CampaignCountryInfoUI __instance)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();

        try
        {
            Player? player = PlayerController.Instance;
            if (__instance?.BuildingList == null || player == null || !TryBuildCounts(player, out ConstructionCounts counts))
                return;

            if (__instance.BuildingLabel != null)
                __instance.BuildingLabel.text = $"Building {counts.TotalDisplay} ships:";

            __instance.BuildingList.text = counts.TypeDisplay;

            string summary = $"{counts.TotalDisplay}|{counts.TypeDisplay}";
            if (summary != lastLoggedSummary)
            {
                lastLoggedSummary = summary;
                Melon<UADVanillaPlusMod>.Logger.Msg(
                    $"UADVP campaign construction: displayed {counts.TotalDisplay} ships as {counts.TypeDisplay} in {stopwatch.ElapsedMilliseconds} ms.");
            }
        }
        catch (Exception ex)
        {
            Melon<UADVanillaPlusMod>.Logger.Warning(
                $"UADVP campaign construction status patch failed; leaving vanilla text intact. {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static bool TryBuildCounts(Player player, out ConstructionCounts counts)
    {
        counts = default;

        try
        {
            if (player == null)
                return false;

            Dictionary<string, TypeCounts> byType = new(StringComparer.OrdinalIgnoreCase);

            if (CampaignController.Instance?.CampaignData?.VesselsByPlayer == null ||
                !CampaignController.Instance.CampaignData.VesselsByPlayer.TryGetValue(player.data, out var vessels))
            {
                return false;
            }

            foreach (VesselEntity vessel in vessels)
            {
                if (vessel == null || (!vessel.isBuilding && !vessel.isCommissioning))
                    continue;

                if (vessel.vesselType == VesselEntity.VesselType.Ship)
                {
                    Ship? ship = vessel.TryCast<Ship>();
                    if (ship == null || ship.isDesign)
                        continue;

                    AddVessel(byType, ShipTypeLabel(ship), ship.isCommissioning);
                    continue;
                }

                if (vessel.vesselType == VesselEntity.VesselType.Submarine)
                {
                    Submarine? submarine = vessel.TryCast<Submarine>();
                    if (submarine == null)
                        continue;

                    AddVessel(byType, SubmarineTypeLabel(submarine), submarine.isCommissioning);
                }
            }

            int total = 0;
            int commissioning = 0;
            foreach (TypeCounts typeCounts in byType.Values)
            {
                total += typeCounts.Total;
                commissioning += typeCounts.Commissioning;
            }

            counts = new ConstructionCounts(
                $"{total}({commissioning})",
                byType.Count == 0 ? "-" : string.Join(", ", OrderedTypeDisplays(byType)));
            return true;
        }
        catch (Exception ex)
        {
            Melon<UADVanillaPlusMod>.Logger.Warning(
                $"UADVP campaign construction count failed; falling back to vanilla. {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static IEnumerable<string> OrderedTypeDisplays(Dictionary<string, TypeCounts> byType)
    {
        foreach (string type in new[] { "BB", "BC", "CA", "CL", "DD", "TB", "SS" })
        {
            if (byType.TryGetValue(type, out TypeCounts counts))
                yield return TypeDisplay(type, counts);
        }

        foreach (KeyValuePair<string, TypeCounts> entry in byType.OrderBy(static x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (IsKnownType(entry.Key))
                continue;

            yield return TypeDisplay(entry.Key, entry.Value);
        }
    }

    private static string TypeDisplay(string type, TypeCounts counts)
    {
        return $"{counts.Total}({counts.Commissioning}) {type}s";
    }

    private static void AddVessel(Dictionary<string, TypeCounts> byType, string type, bool isCommissioning)
    {
        byType.TryGetValue(type, out TypeCounts typeCounts);
        typeCounts.Total++;

        if (isCommissioning)
            typeCounts.Commissioning++;

        byType[type] = typeCounts;
    }

    private static bool IsKnownType(string type)
    {
        return type is "BB" or "BC" or "CA" or "CL" or "DD" or "TB" or "SS";
    }

    private static string ShipTypeLabel(Ship ship)
    {
        string? label = ship.shipType?.nameUi;
        if (string.IsNullOrWhiteSpace(label))
            label = ship.shipType?.name;

        return string.IsNullOrWhiteSpace(label) ? "Ship" : label.Trim().ToUpperInvariant();
    }

    private static string SubmarineTypeLabel(Submarine submarine)
    {
        string? label = submarine.Type?.nameUi;
        if (string.IsNullOrWhiteSpace(label))
            label = submarine.SpecialType;

        return string.IsNullOrWhiteSpace(label) ? "SS" : label.Trim().ToUpperInvariant();
    }

    private readonly record struct ConstructionCounts(string TotalDisplay, string TypeDisplay);

    private struct TypeCounts
    {
        internal int Total;
        internal int Commissioning;
    }
}
