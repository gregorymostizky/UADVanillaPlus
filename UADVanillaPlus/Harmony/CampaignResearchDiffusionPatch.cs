using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using UADVanillaPlus.GameData;
using UnityEngine;

namespace UADVanillaPlus.Harmony;

// Patch intent: keep the naval technology race from becoming permanently
// lopsided. Vanilla research speed still owns the base result; this only adds
// category-level diffusion for major nations that trail the current leader.
[HarmonyPatch(typeof(CampaignController))]
internal static class CampaignResearchDiffusionPatch
{
    private const float ResearchCompleteProgress = 100f;
    private static readonly float[] GradualBonuses = { 0.20f, 0.60f, 1.20f, 1.80f, 2.50f };
    private static readonly float[] SwiftBonuses = { 0.50f, 1.50f, 3.50f, 5.50f, 8.00f };
    private static readonly float[] UnrestrictedBonuses = { 1.00f, 3.00f, 7.00f, 12.00f, 20.00f };
    private static readonly Dictionary<string, TypeSnapshot> SnapshotByType = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, TurnBoostSummary> TurnSummariesByPlayer = new(StringComparer.OrdinalIgnoreCase);
    private static int snapshotFrame = -1;
    private static bool collectingTurnSummary;
    private static bool loggedFailure;

    [HarmonyPrefix]
    [HarmonyPatch(nameof(CampaignController.NextTurn))]
    internal static void NextTurnPrefix()
    {
        collectingTurnSummary = true;
        TurnSummariesByPlayer.Clear();
    }

    [HarmonyPostfix]
    [HarmonyPatch("OnNewTurn")]
    internal static void OnNewTurnPostfix()
    {
        FlushTurnSummary();
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(CampaignController.GetResearchSpeed))]
    internal static void GetResearchSpeedPostfix(Player player, Technology tech, ref float __result)
    {
        ModSettings.TechnologySpreadMode mode = ModSettings.TechnologySpread;
        if (mode == ModSettings.TechnologySpreadMode.Vanilla || __result <= 0f)
            return;

        try
        {
            if (!TryGetTechType(tech, out TechType techType) ||
                !TryGetSnapshot(techType, out TypeSnapshot snapshot) ||
                !snapshot.LevelsByPlayer.TryGetValue(PlayerKey(player), out int playerLevel))
            {
                return;
            }

            int gap = snapshot.LeaderLevel - playerLevel;
            if (gap <= 0)
                return;

            float multiplier = DiffusionMultiplier(mode, gap);
            if (multiplier <= 1f)
                return;

            float baseSpeed = __result;
            __result *= multiplier;
            RecordTurnBoost(player, gap, multiplier, baseSpeed, __result);
        }
        catch (Exception ex)
        {
            if (loggedFailure)
                return;

            loggedFailure = true;
            Melon<UADVanillaPlusMod>.Logger.Warning(
                $"UADVP research diffusion skipped after an unexpected error. {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static float DiffusionMultiplier(ModSettings.TechnologySpreadMode mode, int gap)
    {
        int bucket = Mathf.Clamp(gap, 1, 5) - 1;
        float[] bonuses = mode switch
        {
            ModSettings.TechnologySpreadMode.Gradual => GradualBonuses,
            ModSettings.TechnologySpreadMode.Swift => SwiftBonuses,
            ModSettings.TechnologySpreadMode.Unrestricted => UnrestrictedBonuses,
            _ => Array.Empty<float>(),
        };

        return bonuses.Length == 0 ? 1f : 1f + bonuses[bucket];
    }

    private static bool TryGetSnapshot(TechType techType, out TypeSnapshot snapshot)
    {
        snapshot = default!;

        string typeKey = TechTypeKey(techType);
        if (string.IsNullOrWhiteSpace(typeKey))
            return false;

        RefreshSnapshotFrame();
        if (SnapshotByType.TryGetValue(typeKey, out TypeSnapshot? cachedSnapshot))
        {
            snapshot = cachedSnapshot;
            return true;
        }

        var players = CampaignController.Instance?.CampaignData?.PlayersMajor;
        if (players == null)
            return false;

        Dictionary<string, int> levels = new(StringComparer.OrdinalIgnoreCase);
        int leaderLevel = int.MinValue;
        int compared = 0;

        foreach (Player major in players)
        {
            if (major?.technologies == null)
                continue;

            int level = CountCompletedTechs(major, techType);
            levels[PlayerKey(major)] = level;
            leaderLevel = Math.Max(leaderLevel, level);
            compared++;
        }

        if (compared == 0)
            return false;

        snapshot = new TypeSnapshot(leaderLevel, levels);
        SnapshotByType[typeKey] = snapshot;
        return true;
    }

    private static void RefreshSnapshotFrame()
    {
        int frame = Time.frameCount;
        if (snapshotFrame == frame)
            return;

        snapshotFrame = frame;
        SnapshotByType.Clear();
    }

    private static bool TryGetTechType(Technology? tech, out TechType techType)
    {
        techType = null!;

        TechnologyData? data = tech?.data;
        if (data == null)
            return false;

        if (data.typex != null)
        {
            techType = data.typex;
            return true;
        }

        if (string.IsNullOrWhiteSpace(data.type) || G.GameData?.techTypes == null)
            return false;

        return G.GameData.techTypes.TryGetValue(data.type, out techType) && techType != null;
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

    private static string PlayerKey(Player? player)
    {
        if (!string.IsNullOrWhiteSpace(player?.data?.name))
            return player.data.name;

        return player?.GetHashCode().ToString() ?? string.Empty;
    }

    private static string TechTypeKey(TechType techType)
        => !string.IsNullOrWhiteSpace(techType.name) ? techType.name : techType.GetHashCode().ToString();

    private static string TechTypeLabel(TechType techType)
        => SafeLocalize(!string.IsNullOrWhiteSpace(techType.nameUi) ? techType.nameUi : techType.name);

    private static void RecordTurnBoost(Player player, int gap, float multiplier, float baseSpeed, float boostedSpeed)
    {
        if (!collectingTurnSummary)
            return;

        string key = PlayerKey(player);
        if (!TurnSummariesByPlayer.TryGetValue(key, out TurnBoostSummary? summary))
        {
            summary = new TurnBoostSummary(PlayerLabel(player));
            TurnSummariesByPlayer[key] = summary;
        }

        summary.Record(gap, multiplier, baseSpeed, boostedSpeed);
    }

    private static void FlushTurnSummary()
    {
        if (!collectingTurnSummary)
            return;

        collectingTurnSummary = false;
        if (TurnSummariesByPlayer.Count == 0)
            return;

        ModSettings.TechnologySpreadMode mode = ModSettings.TechnologySpread;
        if (mode == ModSettings.TechnologySpreadMode.Vanilla)
        {
            TurnSummariesByPlayer.Clear();
            return;
        }

        Melon<UADVanillaPlusMod>.Logger.Msg(
            $"UADVP technology spread turn summary: {ModSettings.TechnologySpreadModeText(mode)}{CampaignDateText()}; {TurnSummariesByPlayer.Count} nations received diffusion boosts.");

        foreach (TurnBoostSummary summary in TurnSummariesByPlayer.Values
                     .OrderByDescending(static summary => summary.AverageMultiplier)
                     .ThenBy(static summary => summary.PlayerLabel))
        {
            Melon<UADVanillaPlusMod>.Logger.Msg($"UADVP technology spread turn summary: {summary.LogText}.");
        }

        TurnSummariesByPlayer.Clear();
    }

    private static string SafeLocalize(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return "unknown category";

        if (!label.StartsWith("$", StringComparison.Ordinal))
            return label;

        try
        {
            string localized = LocalizeManager.Localize(label);
            if (!string.IsNullOrWhiteSpace(localized) &&
                !string.Equals(localized, label, StringComparison.Ordinal))
            {
                return localized;
            }
        }
        catch
        {
        }

        return CleanLocalizationKey(label);
    }

    private static string CleanLocalizationKey(string label)
    {
        string clean = label.TrimStart('$')
            .Replace("techType_name_", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("compTypes_name_short_", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("compTypes_name_", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("techEffect_desc_", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("Ui_Constr_", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("_", " ", StringComparison.Ordinal);

        return string.IsNullOrWhiteSpace(clean) ? label : clean;
    }

    private static string CampaignDateText()
    {
        try
        {
            CampaignController? campaign = CampaignController.Instance;
            if (campaign == null)
                return string.Empty;

            var date = campaign.CurrentDate.AsDate();
            return $" {date.Year:0000}-{date.Month:00}-{date.Day:00}";
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string PlayerLabel(Player? player)
    {
        try
        {
            string? name = player?.Name(false);
            if (!string.IsNullOrWhiteSpace(name))
                return name;
        }
        catch
        {
        }

        if (!string.IsNullOrWhiteSpace(player?.data?.nameUi))
            return player.data.nameUi;

        return player?.data?.name ?? "unknown nation";
    }

    private sealed record TypeSnapshot(int LeaderLevel, Dictionary<string, int> LevelsByPlayer);

    private sealed class TurnBoostSummary
    {
        internal TurnBoostSummary(string playerLabel)
        {
            PlayerLabel = playerLabel;
        }

        internal string PlayerLabel { get; }
        private int Count { get; set; }
        private float SumMultiplier { get; set; }
        private float SumGap { get; set; }
        private float SumBaseSpeed { get; set; }
        private float SumBoostedSpeed { get; set; }
        private int MaxGap { get; set; }

        internal float AverageMultiplier => Count == 0 ? 1f : SumMultiplier / Count;

        internal string LogText
        {
            get
            {
                float averageGap = Count == 0 ? 0f : SumGap / Count;
                float averageBonus = (AverageMultiplier - 1f) * 100f;
                return $"{PlayerLabel}: avg x{AverageMultiplier:0.##} (+{averageBonus:0}%) over {Count} boosted research checks, avg gap {averageGap:0.0}, max gap {GapText(MaxGap)}, speed {SumBaseSpeed:0.###}->{SumBoostedSpeed:0.###}";
            }
        }

        internal void Record(int gap, float multiplier, float baseSpeed, float boostedSpeed)
        {
            Count++;
            SumMultiplier += multiplier;
            SumGap += gap;
            SumBaseSpeed += baseSpeed;
            SumBoostedSpeed += boostedSpeed;
            MaxGap = Math.Max(MaxGap, gap);
        }
    }

    private static string GapText(int gap)
        => gap >= 5 ? "5+" : gap.ToString();
}
