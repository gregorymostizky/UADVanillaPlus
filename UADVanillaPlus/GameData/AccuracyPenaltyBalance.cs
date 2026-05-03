using System.Globalization;
using System.Text.RegularExpressions;
using Il2Cpp;
using MelonLoader;

namespace UADVanillaPlus.GameData;

// Balance intent: vanilla design-side accuracy penalties from smoke,
// stability, and instability can stack into extreme values. VP changes the
// raw stat effect text before vanilla parses it, so the game builds the toned
// curves itself and combat accuracy calculation has no extra runtime work.
internal static class AccuracyPenaltyBalance
{
    private sealed record AccuracyCurve(string EffectName, float First, float Second);

    private static readonly Dictionary<string, AccuracyCurve> VanillaAccuracyCurves = new(StringComparer.Ordinal)
    {
        ["smoke"] = new("accuracy", 0f, -15f),
        ["stability"] = new("accuracy", -50f, 25f),
        ["instability_z"] = new("accuracy", 0f, -30f),
        ["instability_x"] = new("accuracy", 0f, -40f),
        ["instability_zz"] = new("accuracy", 0f, -25f),
        ["instability_xx"] = new("accuracy", 0f, -25f),
    };

    private static readonly Regex EffectTermRegex = new(
        @"(?<effect>[A-Za-z_]+)\s*\(\s*(?<first>[+-]?\d+(?:\.\d+)?)\s*;\s*(?<second>[+-]?\d+(?:\.\d+)?)\s*\)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly HashSet<string> LoggedStats = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> OriginalEffectText = new(StringComparer.Ordinal);

    internal static void PrepareStatForVanillaPostProcess(StatData stat)
    {
        if (stat == null || string.IsNullOrEmpty(stat.effect))
            return;

        string statName = stat.name ?? string.Empty;
        if (!VanillaAccuracyCurves.TryGetValue(statName, out AccuracyCurve? vanillaCurve))
            return;

        RememberOriginalEffectText(statName, stat.effect, vanillaCurve);

        ModSettings.AccuracyPenaltyMode mode = ModSettings.DesignAccuracyPenaltyMode;
        if (mode == ModSettings.AccuracyPenaltyMode.Vanilla)
            return;

        string balancedCurve = BalancedCurve(vanillaCurve, ModSettings.AccuracyPenaltyDivisor(mode));
        string originalEffect = stat.effect;
        string updatedEffect = EffectTermRegex.Replace(stat.effect, match => ReplaceAccuracyCurve(match, vanillaCurve, balancedCurve));
        if (updatedEffect == originalEffect)
        {
            LogOnce(statName, $"UADVP accuracy penalties: expected {statName}.{CurveText(vanillaCurve)} was not found in '{stat.effect}'; leaving vanilla effect text.");
            return;
        }

        stat.effect = updatedEffect;
        LogOnce(statName, $"UADVP accuracy penalties: {statName} {CurveText(vanillaCurve)} -> {balancedCurve}.");
    }

    internal static bool IsBattleOrLoading()
        => GameManager.IsBattle || GameManager.IsLoadingBattle;

    internal static void TryReapplyLoadedStats(ModSettings.AccuracyPenaltyMode mode)
    {
        if (IsBattleOrLoading())
        {
            Melon<UADVanillaPlusMod>.Logger.Warning("UADVP accuracy penalties: live reapply skipped because a battle is loading or active.");
            return;
        }

        if (G.GameData?.stats == null)
        {
            Melon<UADVanillaPlusMod>.Logger.Warning("UADVP accuracy penalties: live reapply deferred because game data is not loaded yet.");
            return;
        }

        int rebuilt = 0;
        int missing = 0;
        foreach (string statName in VanillaAccuracyCurves.Keys)
        {
            if (!G.GameData.stats.TryGetValue(statName, out StatData stat) || stat == null)
            {
                missing++;
                continue;
            }

            if (!OriginalEffectText.TryGetValue(statName, out string? originalEffect))
            {
                originalEffect = RestoreVanillaEffectText(stat.effect, VanillaAccuracyCurves[statName]);
                OriginalEffectText[statName] = originalEffect;
                Melon<UADVanillaPlusMod>.Logger.Warning($"UADVP accuracy penalties: rebuilt original effect text for {statName} from currently loaded data.");
            }

            // Live changes rebuild the vanilla parsed effect dictionary from
            // original text. The Harmony prefix then applies the selected VP
            // curve before vanilla PostProcess parses it, avoiding cumulative
            // divide-by-divide rewrites and avoiding battle hot-path patches.
            stat.effect = originalEffect;
            stat.PostProcess();
            rebuilt++;
        }

        Melon<UADVanillaPlusMod>.Logger.Msg($"UADVP accuracy penalties: reapplied {ModSettings.AccuracyPenaltyModeText(mode)} mode to {rebuilt} loaded stat curves{(missing > 0 ? $" ({missing} missing)" : string.Empty)}.");
    }

    private static void RememberOriginalEffectText(string statName, string effectText, AccuracyCurve vanillaCurve)
    {
        if (OriginalEffectText.ContainsKey(statName))
            return;

        OriginalEffectText[statName] = RestoreVanillaEffectText(effectText, vanillaCurve);
    }

    private static string ReplaceAccuracyCurve(Match match, AccuracyCurve expected, string balancedCurve)
    {
        if (!string.Equals(match.Groups["effect"].Value, expected.EffectName, StringComparison.Ordinal))
            return match.Value;

        float first = float.Parse(match.Groups["first"].Value, CultureInfo.InvariantCulture);
        float second = float.Parse(match.Groups["second"].Value, CultureInfo.InvariantCulture);
        if (Math.Abs(first - expected.First) > 0.001f || Math.Abs(second - expected.Second) > 0.001f)
            return match.Value;

        return balancedCurve;
    }

    private static string RestoreVanillaEffectText(string effectText, AccuracyCurve vanillaCurve)
        => EffectTermRegex.Replace(effectText, match =>
            string.Equals(match.Groups["effect"].Value, vanillaCurve.EffectName, StringComparison.Ordinal)
                ? CurveText(vanillaCurve)
                : match.Value);

    private static string BalancedCurve(AccuracyCurve vanillaCurve, float divisor)
        => $"{vanillaCurve.EffectName}({Format(DampenNegative(vanillaCurve.First, divisor))};{Format(DampenNegative(vanillaCurve.Second, divisor))})";

    private static string CurveText(AccuracyCurve curve)
        => $"{curve.EffectName}({Format(curve.First)};{Format(curve.Second)})";

    private static float DampenNegative(float value, float divisor)
        => value < 0f ? value / divisor : value;

    private static string Format(float value)
        => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static void LogOnce(string statName, string message)
    {
        if (LoggedStats.Add(statName))
            Melon<UADVanillaPlusMod>.Logger.Msg(message);
    }
}
