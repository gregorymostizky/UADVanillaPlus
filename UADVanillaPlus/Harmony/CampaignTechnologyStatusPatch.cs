using System.Diagnostics;
using System.Text.RegularExpressions;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using UnityEngine;

namespace UADVanillaPlus.Harmony;

// Patch intent: surface the next expected technology discovery in the campaign
// country info panel. Vanilla only shows broad tech standing here, so players
// have to open the research UI to answer a routine "how long until next tech?"
// question.
[HarmonyPatch(typeof(CampaignCountryInfoUI))]
internal static class CampaignTechnologyStatusPatch
{
    private const float ResearchCompleteProgress = 100f;
    private static readonly Regex NextDiscoverySuffixRegex = new(@"\s*\(Next \d+m\)\s*$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly HashSet<GameObject> TooltipTargets = new();
    private static string lastLoggedSummary = string.Empty;

    [HarmonyPostfix]
    [HarmonyPatch(nameof(CampaignCountryInfoUI.Refresh))]
    internal static void RefreshPostfix(CampaignCountryInfoUI __instance)
        => ApplyToInstance(__instance);

    internal static void ApplyToInstance(CampaignCountryInfoUI __instance)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();

        try
        {
            Player? player = PlayerController.Instance;
            if (__instance?.Technology == null || player == null)
                return;

            if (!TryGetNextDiscovery(player, out DiscoveryEstimate estimate))
                return;

            __instance.Technology.text = $"{StripNextDiscoverySuffix(__instance.Technology.text)} (Next {estimate.Months}m)";
            EnsureTooltip(__instance.Technology.gameObject);

            string summary = $"{estimate.Months}m:{estimate.Name}";
            if (summary != lastLoggedSummary)
            {
                lastLoggedSummary = summary;
                Melon<UADVanillaPlusMod>.Logger.Msg(
                    $"UADVP campaign technology status: displayed next discovery {summary} in {stopwatch.ElapsedMilliseconds} ms. Candidates: {estimate.DebugDetails}");
            }
        }
        catch (Exception ex)
        {
            Melon<UADVanillaPlusMod>.Logger.Warning(
                $"UADVP campaign technology status patch failed; leaving vanilla text intact. {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static string StripNextDiscoverySuffix(string text)
        => string.IsNullOrWhiteSpace(text) ? string.Empty : NextDiscoverySuffixRegex.Replace(text, string.Empty);

    private static bool TryGetNextDiscovery(Player player, out DiscoveryEstimate estimate)
    {
        estimate = default;

        CampaignController? campaign = CampaignController.Instance;
        if (campaign == null || player.technologies == null)
            return false;

        int bestMonths = int.MaxValue;
        string bestName = string.Empty;
        List<string> candidateDetails = new();
        HashSet<TechType> seenTypes = new();

        foreach (Technology tech in player.technologies)
        {
            if (tech == null)
                continue;

            TechnologyData? data = tech.data;
            TechType? type = data?.typex;
            if (data == null || type == null)
                continue;

            // Vanilla's research window displays the first unresearched tech per
            // tech type. Matching that row selection keeps the campaign summary
            // aligned with the detailed research UI instead of accidentally
            // picking a queued/future tech from the same type.
            if (tech.progress >= ResearchCompleteProgress || tech.isResearched || !seenTypes.Add(type))
                continue;

            float speed = campaign.GetResearchSpeed(player, tech);
            if (speed <= 0f)
                continue;

            // Vanilla stores research progress on a 0..100 scale, not 0..1.
            // CampaignResearchWindow.UpdateTechRes uses this same completion
            // target when it renders the built-in "X months" text.
            float progress = Mathf.Clamp(tech.progress, 0f, ResearchCompleteProgress - 0.0001f);
            float progressLeft = ResearchCompleteProgress - progress;
            int months = Math.Max(1, Mathf.CeilToInt(progressLeft / speed));
            string name = TechnologyName(tech);

            candidateDetails.Add($"{months}m {name} progress={progress:0.000} speed={speed:0.0000}");
            if (months >= bestMonths)
                continue;

            bestMonths = months;
            bestName = name;
        }

        if (bestMonths == int.MaxValue)
            return false;

        estimate = new DiscoveryEstimate(bestMonths, bestName, string.Join("; ", candidateDetails.Take(5)));
        return true;
    }

    private static string TechnologyName(Technology tech)
    {
        try
        {
            string? name = tech.data?.GetName();
            if (!string.IsNullOrWhiteSpace(name))
                return name;
        }
        catch
        {
        }

        return tech.data?.name ?? "unknown technology";
    }

    private static void EnsureTooltip(GameObject? target)
    {
        if (target == null || TooltipTargets.Contains(target))
            return;

        TooltipTargets.Add(target);

        OnEnter onEnter = target.AddComponent<OnEnter>();
        onEnter.action = new System.Action(() =>
        {
            Player? player = PlayerController.Instance;
            string tooltip = player != null && TryGetNextDiscovery(player, out DiscoveryEstimate estimate)
                ? $"Next technology discovery in about {estimate.Months} months.\nSoonest current research: {estimate.Name}"
                : "Next technology discovery estimate unavailable.";
            G.ui.ShowTooltip(tooltip, target);
        });

        OnLeave onLeave = target.AddComponent<OnLeave>();
        onLeave.action = new System.Action(() => G.ui.HideTooltip());
    }

    private readonly record struct DiscoveryEstimate(int Months, string Name, string DebugDetails);
}
