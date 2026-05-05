using System.Text.RegularExpressions;
using HarmonyLib;
using Il2Cpp;
using Il2CppTMPro;
using MelonLoader;
using UnityEngine;

namespace UADVanillaPlus.Harmony;

// Patch intent: make the campaign research window show each tech category's
// position relative to the best major nation. The game already tracks per-player
// technology progress, but vanilla hides the "am I ahead or behind?" answer.
[HarmonyPatch(typeof(CampaignResearchWindow))]
internal static class CampaignResearchStandingPatch
{
    private const float ResearchCompleteProgress = 100f;
    private const string BadgeName = "UADVP_ResearchStandingBadge";
    private static readonly Color AheadColor = new(0.65f, 0.83f, 0.48f, 1f);
    private static readonly Color BehindColor = new(0.83f, 0.48f, 0.48f, 1f);
    private static readonly Color TiedColor = new(0.85f, 0.75f, 0.42f, 1f);
    private static readonly Regex StandingSuffixRegex = new(
        @"\s*(?:<color=#[0-9A-Fa-f]{6}>)?\((?:(?:Ahead|Behind) by \d+ techs?|Tied for lead|[+-]\d+ techs?|Tied)\)(?:</color>)?\s*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static string lastLoggedSummary = string.Empty;

    [HarmonyPostfix]
    [HarmonyPatch(nameof(CampaignResearchWindow.Show))]
    private static void ShowPostfix(CampaignResearchWindow __instance)
        => ApplyToWindow(__instance, "show");

    [HarmonyPostfix]
    [HarmonyPatch(nameof(CampaignResearchWindow.UpdateTechRes))]
    private static void UpdateTechResPostfix(TechnologyGroupElement_Type techType)
        => ApplyToRow(techType);

    internal static void ApplyToWindow(CampaignResearchWindow? window, string context)
    {
        try
        {
            if (window?.Root == null)
                return;

            int decorated = 0;
            List<string> samples = new();
            foreach (TechnologyGroupElement_Type row in window.Root.GetComponentsInChildren<TechnologyGroupElement_Type>(true))
            {
                if (!ApplyToRow(row, out string summary))
                    continue;

                decorated++;
                if (samples.Count < 4)
                    samples.Add(summary);
            }

            string logSummary = $"{context}|{decorated}|{string.Join("; ", samples)}";
            if (decorated > 0 && logSummary != lastLoggedSummary)
            {
                lastLoggedSummary = logSummary;
                Melon<UADVanillaPlusMod>.Logger.Msg($"UADVP research standing: decorated {decorated} categories after {context}. {string.Join("; ", samples)}");
            }
        }
        catch (Exception ex)
        {
            Melon<UADVanillaPlusMod>.Logger.Warning(
                $"UADVP research standing refresh failed; leaving vanilla research text intact. {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static bool ApplyToRow(TechnologyGroupElement_Type? row)
        => ApplyToRow(row, out _);

    private static bool ApplyToRow(TechnologyGroupElement_Type? row, out string summary)
    {
        summary = string.Empty;

        if (row?.TechType == null)
            return false;

        string baseText = StripStandingSuffix(row.TechType.text);
        if (string.IsNullOrWhiteSpace(baseText))
            return false;

        if (!TryResolveTechType(row, baseText, out TechType techType) ||
            !TryGetStanding(techType, out Standing standing))
        {
            StripExistingStanding(row);
            HideBadge(row);
            return false;
        }

        StripExistingStanding(row);
        row.TechType.text = baseText;
        ApplyBadge(row, standing);
        summary = $"{TypeLabel(techType)}:{standing.LogText}";
        return true;
    }

    private static void StripExistingStanding(TechnologyGroupElement_Type row)
    {
        if (row.TechType != null)
            row.TechType.text = StripStandingSuffix(row.TechType.text);
        if (row.TurnsLabel != null)
            row.TurnsLabel.text = StripStandingSuffix(row.TurnsLabel.text);
        if (row.TechName != null)
            row.TechName.text = StripStandingSuffix(row.TechName.text);
    }

    private static void ApplyBadge(TechnologyGroupElement_Type row, Standing standing)
    {
        TextMeshProUGUI badge = EnsureBadge(row);
        badge.gameObject.SetActive(true);
        badge.text = BadgeText(standing);
        badge.color = BadgeColor(standing);
        badge.fontStyle = FontStyles.Bold;
        badge.fontWeight = FontWeight.Bold;
        badge.richText = false;
        badge.enableWordWrapping = false;
        badge.overflowMode = TextOverflowModes.Overflow;
        badge.alignment = TextAlignmentOptions.TopLeft;

        if (row.TechName != null)
        {
            badge.font = row.TechName.font;
            badge.fontSize = Mathf.Clamp(row.TechName.fontSize * 0.72f, 12f, 15f);
        }

        badge.transform.SetAsLastSibling();

        RectTransform rect = badge.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.one;
        rect.anchorMax = Vector2.one;
        rect.pivot = Vector2.one;
        rect.sizeDelta = new Vector2(170f, 24f);
        rect.anchoredPosition = new Vector2(-2f, -8f);
    }

    private static TextMeshProUGUI EnsureBadge(TechnologyGroupElement_Type row)
    {
        Transform existing = row.transform.Find(BadgeName);
        if (existing != null)
        {
            TextMeshProUGUI existingText = existing.GetComponent<TextMeshProUGUI>();
            if (existingText != null)
                return existingText;
        }

        GameObject badgeObject = new(BadgeName);
        badgeObject.AddComponent<RectTransform>();
        badgeObject.transform.SetParent(row.transform, false);
        TextMeshProUGUI badge = badgeObject.AddComponent<TextMeshProUGUI>();
        badge.raycastTarget = false;
        return badge;
    }

    private static void HideBadge(TechnologyGroupElement_Type row)
    {
        Transform existing = row.transform.Find(BadgeName);
        if (existing != null)
            existing.gameObject.SetActive(false);
    }

    private static bool TryResolveTechType(TechnologyGroupElement_Type row, string baseText, out TechType techType)
    {
        techType = null!;

        if (G.GameData?.techTypes == null)
            return false;

        if (!string.IsNullOrWhiteSpace(row.Id) &&
            G.GameData.techTypes.TryGetValue(row.Id, out techType) &&
            techType != null)
        {
            return true;
        }

        foreach (var pair in G.GameData.techTypes)
        {
            TechType candidate = pair.Value;
            if (candidate == null)
                continue;

            if (string.Equals(candidate.nameUi, baseText, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(candidate.name, row.Id, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(candidate.name, baseText, StringComparison.OrdinalIgnoreCase))
            {
                techType = candidate;
                return true;
            }
        }

        return false;
    }

    private static bool TryGetStanding(TechType techType, out Standing standing)
    {
        standing = default;

        Player? player = PlayerController.Instance;
        var players = CampaignController.Instance?.CampaignData?.PlayersMajor;
        if (player == null || players == null)
            return false;

        int playerLevel = CountCompletedTechs(player, techType);
        int leaderLevel = playerLevel;
        int bestOtherLevel = int.MinValue;
        Player? leaderPlayer = player;
        Player? bestOtherPlayer = null;
        int comparedPlayers = 0;

        foreach (Player other in players)
        {
            if (other == null || other.technologies == null)
                continue;

            int level = CountCompletedTechs(other, techType);
            if (level > leaderLevel)
            {
                leaderLevel = level;
                leaderPlayer = other;
            }

            if (SamePlayer(player, other))
                continue;

            comparedPlayers++;
            if (level > bestOtherLevel)
            {
                bestOtherLevel = level;
                bestOtherPlayer = other;
            }
        }

        if (comparedPlayers == 0)
            return false;

        if (playerLevel < leaderLevel)
        {
            int difference = leaderLevel - playerLevel;
            standing = new Standing(StandingKind.Behind, difference, playerLevel, leaderLevel, bestOtherLevel, ShortNationLabel(leaderPlayer));
            return true;
        }

        if (playerLevel > bestOtherLevel)
        {
            int difference = playerLevel - bestOtherLevel;
            standing = new Standing(StandingKind.Ahead, difference, playerLevel, leaderLevel, bestOtherLevel, ShortNationLabel(bestOtherPlayer));
            return true;
        }

        standing = new Standing(StandingKind.Tied, 0, playerLevel, leaderLevel, bestOtherLevel, ShortNationLabel(bestOtherPlayer));
        return true;
    }

    private static int CountCompletedTechs(Player player, TechType techType)
    {
        int count = 0;

        foreach (Technology tech in player.technologies)
        {
            if (tech == null || !TechnologyMatchesType(tech, techType))
                continue;

            if (tech.IsEndTechResearched)
            {
                count += Math.Max(1, tech.Index);
                continue;
            }

            if (tech.isResearched || tech.progress >= ResearchCompleteProgress)
                count++;
        }

        return count;
    }

    private static bool TechnologyMatchesType(Technology tech, TechType techType)
    {
        TechnologyData? data = tech.data;
        if (data == null)
            return false;

        return data.typex == techType ||
               string.Equals(data.type, techType.name, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(data.typex?.name, techType.name, StringComparison.OrdinalIgnoreCase);
    }

    private static bool SamePlayer(Player a, Player b)
        => a == b || (a.data != null && b.data != null && a.data == b.data);

    private static string BadgeText(Standing standing)
    {
        string target = standing.ReferenceLabel;
        return standing.Kind switch
        {
            StandingKind.Ahead => string.IsNullOrWhiteSpace(target)
                ? $"Ahead by {TechCountText(standing.Difference)}"
                : $"Ahead of {target} by {TechCountText(standing.Difference)}",
            StandingKind.Behind => string.IsNullOrWhiteSpace(target)
                ? $"Behind by {TechCountText(standing.Difference)}"
                : $"Behind {target} by {TechCountText(standing.Difference)}",
            _ => string.IsNullOrWhiteSpace(target) ? "Tied for lead" : $"Tied with {target}",
        };
    }

    private static Color BadgeColor(Standing standing)
    {
        return standing.Kind switch
        {
            StandingKind.Ahead => AheadColor,
            StandingKind.Behind => BehindColor,
            _ => TiedColor,
        };
    }

    private static string StripStandingSuffix(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        string result = text;
        string previous;
        do
        {
            previous = result;
            result = StandingSuffixRegex.Replace(result, string.Empty);
        }
        while (!string.Equals(result, previous, StringComparison.Ordinal));

        return result;
    }

    private static string TechCountText(int count)
        => count == 1 ? "1 tech" : $"{count} techs";

    private static string ShortNationLabel(Player? player)
    {
        if (player == null)
            return string.Empty;

        string? key = player.data?.name;
        if (!string.IsNullOrWhiteSpace(key))
        {
            switch (key.Trim().ToLowerInvariant())
            {
                case "britain":
                    return "UK";
                case "usa":
                    return "USA";
                case "austria":
                    return "A-H";
                case "russia":
                    return SafePlayerName(player).IndexOf("soviet", StringComparison.OrdinalIgnoreCase) >= 0 ? "USSR" : "Russia";
                case "france":
                    return "France";
                case "germany":
                    return "Germany";
                case "italy":
                    return "Italy";
                case "japan":
                    return "Japan";
                case "spain":
                    return "Spain";
                case "china":
                    return "China";
            }
        }

        string name = SafePlayerName(player);
        if (name.Length <= 12)
            return name;

        return name
            .Replace("British Empire", "UK", StringComparison.OrdinalIgnoreCase)
            .Replace("United States", "USA", StringComparison.OrdinalIgnoreCase)
            .Replace("Soviet Union", "USSR", StringComparison.OrdinalIgnoreCase)
            .Replace("Austro-Hungarian Empire", "A-H", StringComparison.OrdinalIgnoreCase)
            .Replace("Empire of ", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(" Empire", string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private static string SafePlayerName(Player player)
    {
        try
        {
            string? name = player.Name(false);
            if (!string.IsNullOrWhiteSpace(name))
                return name;
        }
        catch
        {
        }

        if (!string.IsNullOrWhiteSpace(player.data?.nameUi))
            return player.data.nameUi;

        return player.data?.name ?? string.Empty;
    }

    private static string TypeLabel(TechType techType)
        => !string.IsNullOrWhiteSpace(techType.nameUi) ? techType.nameUi : techType.name;

    private enum StandingKind
    {
        Ahead,
        Behind,
        Tied,
    }

    private readonly record struct Standing(StandingKind Kind, int Difference, int PlayerLevel, int LeaderLevel, int BestOtherLevel, string ReferenceLabel)
    {
        public string LogText => Kind switch
        {
            StandingKind.Ahead => $"ahead {ReferenceLabel} {Difference} ({PlayerLevel}/{BestOtherLevel})",
            StandingKind.Behind => $"behind {ReferenceLabel} {Difference} ({PlayerLevel}/{LeaderLevel})",
            _ => $"tied {ReferenceLabel} ({PlayerLevel})",
        };
    }
}
