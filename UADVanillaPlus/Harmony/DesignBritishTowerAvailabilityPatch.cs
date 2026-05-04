using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using UadGameData = Il2Cpp.GameData;

namespace UADVanillaPlus.Harmony;

// Patch intent: correct a few vanilla British late-hull tower compatibility
// omissions so historically paired G3/N3/Hood hull families can see their
// matching towers in campaign design once the normal tech gates are met.
[HarmonyPatch(typeof(UadGameData))]
internal static class DesignBritishTowerAvailabilityPatch
{
    private const string NeedKey = "need";
    private const string NeedUnlockKey = "needunlock";

    private static readonly PartNeedPatch[] PartNeedPatches =
    {
        new("hood_tower_5", "BC_Hood"),
        new("hood_tower_5_smallvar", "BC_Hood"),

        new("nelson_sec_tower_3_target3", "BB_British_G3"),
        new("nelson_sec_tower_3_target2", "BB_British_G3"),
        new("nelson_sec_tower_3_target1", "BB_British_G3"),
        new("nelson_sec_tower_3_target1_small", "BB_British_G3"),
        new("nelson_sec_tower_2_repairadv2", "BB_British_G3"),
        new("nelson_sec_tower_2_repairadv1", "BB_British_G3"),
        new("nelson_sec_tower_1_simple", "BB_British_G3"),
        new("nelson_sec_tower_1_simple_small", "BB_British_G3"),
        new("nelson_sec_tower_1_simple_small2", "BB_British_G3"),

        new("nelson_tower_main_2_barb5", "BB_British_N3"),
        new("nelson_tower_main_2_barb4", "BB_British_N3"),
        new("nelson_tower_main_2_big4", "BB_British_N3"),
        new("nelson_tower_main_2_big3", "BB_British_N3"),
    };

    private static readonly PartUnlockPatch[] PartUnlockPatches =
    {
        new("hood_tower_5_smallvar", "late_BB_towers_level_4", "late_BB_towers_level_3"),
    };

    private static bool logged;

    [HarmonyPrefix]
    [HarmonyPatch(nameof(UadGameData.PostProcessAll))]
    internal static void PostProcessAllPrefix(UadGameData __instance)
        => ApplyCorrections(__instance, updateParsedFields: false);

    [HarmonyPostfix]
    [HarmonyPatch(nameof(UadGameData.PostProcessAll))]
    internal static void PostProcessAllPostfix(UadGameData __instance)
    {
        CorrectionSummary summary = ApplyCorrections(__instance, updateParsedFields: true);

        if (logged)
            return;

        logged = true;
        string changed = summary.ChangedParts.Count == 0 ? "none" : string.Join(", ", summary.ChangedParts);
        string unlocks = summary.ChangedUnlocks.Count == 0 ? "none" : string.Join(", ", summary.ChangedUnlocks);
        string missing = summary.MissingParts.Count == 0 ? "none" : string.Join(", ", summary.MissingParts);
        Melon<UADVanillaPlusMod>.Logger.Msg(
            $"UADVP design data: British late-hull tower availability checked after vanilla parsing. Need tags patched {summary.NeedPatched}, unlock fields patched {summary.UnlockPatched}, already present {summary.AlreadyPresent}, missing {summary.MissingParts.Count}. Changed needs: {changed}. Changed unlocks: {unlocks}. Missing: {missing}.");
    }

    private static CorrectionSummary ApplyCorrections(UadGameData __instance, bool updateParsedFields)
    {
        CorrectionSummary summary = new();

        if (__instance?.parts == null)
            return summary;

        foreach (PartNeedPatch patch in PartNeedPatches)
        {
            if (!__instance.parts.TryGetValue(patch.PartId, out PartData part) || part == null)
            {
                summary.MissingParts.Add(patch.PartId);
                continue;
            }

            if (TryAddNeedTag(part, patch.Tag, updateParsedFields))
            {
                summary.NeedPatched++;
                summary.ChangedParts.Add(patch.PartId);
            }
            else
            {
                summary.AlreadyPresent++;
            }
        }

        foreach (PartUnlockPatch patch in PartUnlockPatches)
        {
            if (!__instance.parts.TryGetValue(patch.PartId, out PartData part) || part == null)
            {
                summary.MissingParts.Add(patch.PartId);
                continue;
            }

            if (TryReplaceNeedUnlock(part, patch.OldUnlock, patch.NewUnlock, updateParsedFields))
            {
                summary.UnlockPatched++;
                summary.ChangedUnlocks.Add(patch.PartId);
            }
        }

        return summary;
    }

    private static bool TryAddNeedTag(PartData part, string tag, bool updateParsedFields)
    {
        bool changed = TryAddNeedTagToParam(part, tag);

        if (updateParsedFields)
        {
            changed |= TryAddNeedTagToParamx(part, tag);
            changed |= TryAddNeedTagToParsedNeedTags(part, tag);
        }

        return changed;
    }

    private static bool TryAddNeedTagToParam(PartData part, string tag)
    {
        string param = part.param ?? string.Empty;
        int needStart = param.IndexOf($"{NeedKey}(", StringComparison.OrdinalIgnoreCase);
        if (needStart < 0)
        {
            part.param = string.IsNullOrWhiteSpace(param) ? $"{NeedKey}({tag})" : $"{param}, {NeedKey}({tag})";
            return true;
        }

        int valueStart = needStart + $"{NeedKey}(".Length;
        int valueEnd = param.IndexOf(')', valueStart);
        if (valueEnd < 0)
            return false;

        string needValue = param[valueStart..valueEnd];
        if (HasNeedTag(needValue, tag))
            return false;

        string separator = needValue.Trim().Length == 0 ? string.Empty : ";";
        part.param = param[..valueEnd] + separator + tag + param[valueEnd..];
        return true;
    }

    private static bool TryAddNeedTagToParamx(PartData part, string tag)
    {
        if (part.paramx == null)
            part.paramx = new Il2CppSystem.Collections.Generic.Dictionary<string, Il2CppSystem.Collections.Generic.List<string>>();

        if (!part.paramx.TryGetValue(NeedKey, out Il2CppSystem.Collections.Generic.List<string> needValues) || needValues == null)
        {
            needValues = new Il2CppSystem.Collections.Generic.List<string>();
            part.paramx[NeedKey] = needValues;
        }

        for (int i = 0; i < needValues.Count; i++)
        {
            if (HasNeedTag(needValues[i], tag))
                return false;
        }

        if (needValues.Count == 0)
        {
            needValues.Add(tag);
            return true;
        }

        needValues[0] = $"{needValues[0]};{tag}";
        return true;
    }

    private static bool TryAddNeedTagToParsedNeedTags(PartData part, string tag)
    {
        Il2CppSystem.Collections.Generic.List<Il2CppSystem.Collections.Generic.HashSet<string>> needTags = part.needTags;
        if (needTags == null)
        {
            needTags = new Il2CppSystem.Collections.Generic.List<Il2CppSystem.Collections.Generic.HashSet<string>>();
            part.needTags = needTags;
        }

        for (int i = 0; i < needTags.Count; i++)
        {
            Il2CppSystem.Collections.Generic.HashSet<string> needSet = needTags[i];
            if (needSet != null && needSet.Contains(tag))
                return false;
        }

        if (needTags.Count == 0 || needTags[0] == null)
        {
            Il2CppSystem.Collections.Generic.HashSet<string> needSet = new();
            needSet.Add(tag);

            if (needTags.Count == 0)
                needTags.Add(needSet);
            else
                needTags[0] = needSet;

            return true;
        }

        needTags[0].Add(tag);
        return true;
    }

    private static bool TryReplaceNeedUnlock(PartData part, string oldUnlock, string newUnlock, bool updateParsedFields)
    {
        bool changed = TryReplaceNeedUnlockInParam(part, oldUnlock, newUnlock);

        if (updateParsedFields)
        {
            changed |= TryReplaceNeedUnlockInParamx(part, oldUnlock, newUnlock);
            changed |= TryReplaceParsedNeedUnlock(part, oldUnlock, newUnlock);
        }

        return changed;
    }

    private static bool TryReplaceNeedUnlockInParam(PartData part, string oldUnlock, string newUnlock)
    {
        string param = part.param ?? string.Empty;
        string oldToken = $"{NeedUnlockKey}({oldUnlock})";
        string newToken = $"{NeedUnlockKey}({newUnlock})";
        if (param.IndexOf(newToken, StringComparison.OrdinalIgnoreCase) >= 0)
            return false;

        int oldTokenIndex = param.IndexOf(oldToken, StringComparison.OrdinalIgnoreCase);
        if (oldTokenIndex < 0)
            return false;

        part.param = param[..oldTokenIndex] + newToken + param[(oldTokenIndex + oldToken.Length)..];
        return true;
    }

    private static bool TryReplaceNeedUnlockInParamx(PartData part, string oldUnlock, string newUnlock)
    {
        if (part.paramx == null || !part.paramx.TryGetValue(NeedUnlockKey, out Il2CppSystem.Collections.Generic.List<string> unlockValues) || unlockValues == null)
            return false;

        bool changed = false;
        for (int i = 0; i < unlockValues.Count; i++)
        {
            if (string.Equals(unlockValues[i], oldUnlock, StringComparison.OrdinalIgnoreCase))
            {
                unlockValues[i] = newUnlock;
                changed = true;
            }
        }

        return changed;
    }

    private static bool TryReplaceParsedNeedUnlock(PartData part, string oldUnlock, string newUnlock)
    {
        if (!string.Equals(part.NeedUnlock, oldUnlock, StringComparison.OrdinalIgnoreCase))
            return false;

        part.NeedUnlock = newUnlock;
        return true;
    }

    private static bool HasNeedTag(string needValue, string tag)
    {
        foreach (string token in needValue.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (string.Equals(token.Trim(), tag, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private readonly record struct PartNeedPatch(string PartId, string Tag);
    private readonly record struct PartUnlockPatch(string PartId, string OldUnlock, string NewUnlock);
    private sealed class CorrectionSummary
    {
        internal int NeedPatched;
        internal int UnlockPatched;
        internal int AlreadyPresent;
        internal List<string> ChangedParts { get; } = new();
        internal List<string> ChangedUnlocks { get; } = new();
        internal List<string> MissingParts { get; } = new();
    }
}
