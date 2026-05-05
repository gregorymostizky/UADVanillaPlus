using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using UADVanillaPlus.GameData;

namespace UADVanillaPlus.Harmony;

// Patch intent: optionally treat the late-opening Panama and Kiel canals like
// the already-open early canals by moving only their availability years forward,
// then letting vanilla canal logic open obstacles and route passability.
[HarmonyPatch(typeof(MapCanals))]
internal static class CampaignCanalOpeningPatch
{
    private const int EarlyAvailableYear = 1890;

    private static readonly CanalOpening[] CanalOpenings =
    {
        new("panama_canal", "Panama Canal", 1914),
        new("kiel_canal", "Kiel Canal", 1895),
    };

    [HarmonyPrefix]
    [HarmonyPatch(nameof(MapCanals.Init))]
    private static void InitPrefix(MapCanals __instance)
    {
        ApplyAvailabilityYears(__instance);
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(MapCanals.Init))]
    private static void InitPostfix(MapCanals __instance)
    {
        SyncVisuals(__instance);
        LogState(__instance, "campaign map init");
    }

    internal static void ApplyCurrentSetting()
    {
        MapCanals canals = MapCanals.Instance;
        if (canals == null)
        {
            Melon<UADVanillaPlusMod>.Logger.Msg("UADVP canal openings: option stored; campaign canals are not loaded yet.");
            return;
        }

        ApplyAvailabilityYears(canals);

        try
        {
            canals.CheckCanals(false);
        }
        catch (Exception ex)
        {
            Melon<UADVanillaPlusMod>.Logger.Warning($"UADVP canal openings: canal state refresh failed. {ex.GetType().Name}: {ex.Message}");
        }

        SyncVisuals(canals);

        try
        {
            CampaignController.Instance?.UpdateNavmeshPassableAreas();
        }
        catch (Exception ex)
        {
            Melon<UADVanillaPlusMod>.Logger.Warning($"UADVP canal openings: navmesh refresh failed. {ex.GetType().Name}: {ex.Message}");
        }

        LogState(canals, "option change");
    }

    private static void ApplyAvailabilityYears(MapCanals canals)
    {
        bool early = ModSettings.EarlyCanalOpeningsEnabled;
        foreach (CanalOpening opening in CanalOpenings)
        {
            MapCanal? canal = FindCanal(canals, opening.Id);
            if (canal != null)
                canal.AvailableFrom = early ? EarlyAvailableYear : opening.HistoricalYear;
        }
    }

    private static void SyncVisuals(MapCanals canals)
    {
        foreach (CanalOpening opening in CanalOpenings)
        {
            MapCanal? canal = FindCanal(canals, opening.Id);
            if (canal?.VisualCanal != null)
                canal.VisualCanal.SetActive(canal.IsAvailable);
        }
    }

    private static MapCanal? FindCanal(MapCanals? canals, string id)
    {
        if (canals?.Canals == null)
            return null;

        for (int i = 0; i < canals.Canals.Length; i++)
        {
            MapCanal? canal = canals.Canals[i];
            if (canal != null && string.Equals(canal.Id, id, StringComparison.OrdinalIgnoreCase))
                return canal;
        }

        return null;
    }

    private static void LogState(MapCanals canals, string context)
    {
        string year = CurrentCampaignYearText();
        string mode = ModSettings.CanalOpeningModeText(ModSettings.EarlyCanalOpeningsEnabled);

        foreach (CanalOpening opening in CanalOpenings)
        {
            MapCanal? canal = FindCanal(canals, opening.Id);
            if (canal == null)
            {
                Melon<UADVanillaPlusMod>.Logger.Warning($"UADVP canal openings: {context} could not find {opening.Id}.");
                continue;
            }

            Melon<UADVanillaPlusMod>.Logger.Msg(
                $"UADVP canal openings: {context}; {opening.Label} mode {mode}, availability {canal.AvailableFrom}, campaign year {year}, open {canal.IsAvailable}.");
        }
    }

    private static string CurrentCampaignYearText()
    {
        try
        {
            CampaignController campaign = CampaignController.Instance;
            return campaign == null ? "unknown" : campaign.CurrentDate.AsDate().Year.ToString();
        }
        catch
        {
            return "unknown";
        }
    }

    private readonly record struct CanalOpening(string Id, string Label, int HistoricalYear);
}
