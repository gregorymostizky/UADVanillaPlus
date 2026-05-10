using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using UADVanillaPlus.GameData;

namespace UADVanillaPlus.Harmony;

// Patch intent: let player refit designs see researched, same-class-group
// newer towers and funnels when vanilla only blocks the old hull's exact
// positive need-tag family. Everything else remains vanilla-checked.
[HarmonyPatch(typeof(Ship))]
internal static class DesignSuperstructureRefitAvailabilityPatch
{
    private static readonly HashSet<string> LoggedGroups = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> LoggedDeniedCandidates = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> LoggedAllowedOverrides = new(StringComparer.OrdinalIgnoreCase);

    private static bool runningRelaxedCheck;
    private static int exceptionLogCount;
    private static int deniedCandidateLogCount;

    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    [HarmonyPatch(nameof(Ship.IsPartAvailable), typeof(PartData), typeof(Player), typeof(ShipType), typeof(Ship))]
    internal static void IsPartAvailablePostfix(PartData part, Player player, ShipType shipType, Ship ship, ref bool __result)
    {
        if (__result || runningRelaxedCheck)
            return;

        if (!ShouldTryFallback(part, player, shipType, ship, out string groupName, out string targetGroup, out string affinityInfo))
            return;

        if (!TryVanillaAvailabilityWithoutHullNeedTags(part, player, shipType, ship))
        {
            LogDeniedCandidate(part, shipType, groupName, targetGroup, true, true, true, affinityInfo, false);
            return;
        }

        __result = true;
        LogAllowedOverride(part, shipType, groupName, affinityInfo);
    }

    private static bool ShouldTryFallback(PartData? part, Player? player, ShipType? shipType, Ship? ship, out string groupName, out string targetGroup, out string affinityInfo)
    {
        groupName = string.Empty;
        targetGroup = string.Empty;
        affinityInfo = string.Empty;

        if (!ModSettings.SuperstructureRefitsEnabled || !GameManager.IsConstructor)
            return false;

        if (!IsSuperstructurePart(part) || !HasPositiveNeedTags(part))
            return false;

        string targetType = ShipTypeCode(shipType ?? ship?.shipType);
        groupName = InferBroadShipClassFromNeedTags(part);
        targetGroup = BroadShipClass(targetType);

        Player? effectivePlayer = player ?? ship?.player;
        bool contextGatePassed = effectivePlayer?.isMain == true && !effectivePlayer.isAi && IsAllowedDesignContext(ship);
        if (!contextGatePassed)
        {
            LogDeniedCandidate(part, shipType, groupName, targetGroup, false, null, null, affinityInfo, null);
            return false;
        }

        bool unlocked = effectivePlayer!.IsPartUnlocked(part);
        if (!unlocked)
        {
            LogDeniedCandidate(part, shipType, groupName, targetGroup, true, false, null, affinityInfo, null);
            return false;
        }

        if (!SameBroadShipClass(groupName, targetGroup))
        {
            LogDeniedCandidate(part, shipType, groupName, targetGroup, true, true, null, affinityInfo, null);
            return false;
        }

        bool familyGatePassed = HasFamilyAffinity(part, ship, out affinityInfo);
        if (!familyGatePassed)
        {
            LogDeniedCandidate(part, shipType, groupName, targetGroup, true, true, false, affinityInfo, null);
            return false;
        }

        return true;
    }

    private static bool IsAllowedDesignContext(Ship? ship)
    {
        if (IsRefitConstructorContext(ship))
            return true;

        return ModSettings.ObsoleteDesignRetentionEnabled;
    }

    private static bool IsRefitConstructorContext(Ship? ship)
    {
        try
        {
            if (ship?.isRefitDesign == true || ship?.designShipForRefit != null)
                return true;
        }
        catch
        {
            return false;
        }

        try
        {
            return G.ui?.isConstructorRefitMode == true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsSuperstructurePart(PartData? part)
    {
        if (part == null)
            return false;

        if (part.isTowerMain || part.isFunnel)
            return true;

        return IsText(part.type, "tower_sec");
    }

    private static bool HasPositiveNeedTags(PartData? part)
    {
        var needTags = part?.needTags;
        if (needTags == null || needTags.Count == 0)
            return false;

        for (int i = 0; i < needTags.Count; i++)
        {
            Il2CppSystem.Collections.Generic.HashSet<string> needSet = needTags[i];
            if (needSet != null && needSet.Count > 0)
                return true;
        }

        return false;
    }

    private static bool TryVanillaAvailabilityWithoutHullNeedTags(PartData part, Player player, ShipType shipType, Ship ship)
    {
        var originalNeedTags = part.needTags;

        try
        {
            runningRelaxedCheck = true;
            part.needTags = new Il2CppSystem.Collections.Generic.List<Il2CppSystem.Collections.Generic.HashSet<string>>();
            return Ship.IsPartAvailable(part, player, shipType, ship);
        }
        catch (Exception ex)
        {
            LogRelaxedCheckException(part, shipType, ex);
            return false;
        }
        finally
        {
            part.needTags = originalNeedTags;
            runningRelaxedCheck = false;
        }
    }

    private static bool SameBroadShipClass(string partGroup, string targetGroup)
        => partGroup.Length > 0 && string.Equals(partGroup, targetGroup, StringComparison.OrdinalIgnoreCase);

    private static string InferBroadShipClassFromNeedTags(PartData? part)
    {
        var needTags = part?.needTags;
        if (needTags != null)
        {
            for (int i = 0; i < needTags.Count; i++)
            {
                Il2CppSystem.Collections.Generic.HashSet<string> needSet = needTags[i];
                if (needSet == null)
                    continue;

                foreach (string token in needSet)
                {
                    string group = BroadShipClassFromNeedToken(token);
                    if (group.Length > 0)
                        return group;
                }
            }
        }

        return InferBroadShipClassFromParam(part?.param);
    }

    private static string InferBroadShipClassFromParam(string? param)
    {
        if (string.IsNullOrWhiteSpace(param))
            return string.Empty;

        int searchFrom = 0;
        while (searchFrom < param.Length)
        {
            int start = param.IndexOf("need(", searchFrom, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
                break;

            int contentStart = start + "need(".Length;
            int end = param.IndexOf(')', contentStart);
            if (end < 0)
                break;

            string content = param.Substring(contentStart, end - contentStart);
            foreach (string token in content.Split(new[] { ';', ',', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string group = BroadShipClassFromNeedToken(token);
                if (group.Length > 0)
                    return group;
            }

            searchFrom = end + 1;
        }

        return string.Empty;
    }

    private static string BroadShipClassFromNeedToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return string.Empty;

        string code = token.Trim();
        if (code.StartsWith("tag[", StringComparison.OrdinalIgnoreCase) && code.EndsWith("]", StringComparison.Ordinal))
            code = code.Substring(4, code.Length - 5);

        code = code.Trim().Trim('"', '\'', '[', ']', '(', ')').ToUpperInvariant();
        if (code == "BB" || code.StartsWith("BB_", StringComparison.Ordinal))
            return "capital";
        if (code == "BC" || code.StartsWith("BC_", StringComparison.Ordinal))
            return "capital";
        if (code == "CA" || code.StartsWith("CA_", StringComparison.Ordinal))
            return "cruiser";
        if (code == "CL" || code.StartsWith("CL_", StringComparison.Ordinal))
            return "cruiser";
        if (code == "DD" || code.StartsWith("DD_", StringComparison.Ordinal))
            return "screen";
        if (code == "TB" || code.StartsWith("TB_", StringComparison.Ordinal))
            return "screen";
        if (code == "SS" || code.StartsWith("SS_", StringComparison.Ordinal))
            return "submarine";

        return string.Empty;
    }

    private static bool HasFamilyAffinity(PartData? part, Ship? ship, out string affinityInfo)
    {
        PartData? hullData = GetHullData(ship);
        string hullModelFamily = ModelFamilyToken(hullData?.model);
        string partModelFamily = ModelFamilyToken(part?.model);

        if (hullModelFamily.Length > 0 &&
            partModelFamily.Length > 0 &&
            string.Equals(hullModelFamily, partModelFamily, StringComparison.OrdinalIgnoreCase))
        {
            affinityInfo = $"model:{hullModelFamily}";
            return true;
        }

        HashSet<string> hullFamilies = FamilyTokensFromPart(hullData);
        HashSet<string> partFamilies = FamilyTokensFromPart(part);
        foreach (string hullFamily in hullFamilies)
        {
            if (partFamilies.Contains(hullFamily))
            {
                affinityInfo = $"tag:{hullFamily} hullModel={ValueOrNone(hullModelFamily)} partModel={ValueOrNone(partModelFamily)}";
                return true;
            }
        }

        affinityInfo = $"none hullModel={ValueOrNone(hullModelFamily)} partModel={ValueOrNone(partModelFamily)} hullTags={JoinTokens(hullFamilies)} partTags={JoinTokens(partFamilies)}";
        return false;
    }

    private static PartData? GetHullData(Ship? ship)
    {
        try
        {
            PartData? hullData = ship?.hull?.data;
            if (hullData != null)
                return hullData;
        }
        catch
        {
        }

        try
        {
            if (ship?.hullAndParts == null)
                return null;

            foreach (Part part in ship.hullAndParts)
            {
                PartData? data = part?.data;
                if (data?.isHull == true)
                    return data;
            }
        }
        catch
        {
        }

        return null;
    }

    private static HashSet<string> FamilyTokensFromPart(PartData? part)
    {
        HashSet<string> tokens = new(StringComparer.OrdinalIgnoreCase);
        AddFamilyTokensFromNeedTags(part?.needTags, tokens);
        AddFamilyTokensFromParam(part?.param, tokens);
        return tokens;
    }

    private static void AddFamilyTokensFromNeedTags(
        Il2CppSystem.Collections.Generic.List<Il2CppSystem.Collections.Generic.HashSet<string>>? needTags,
        HashSet<string> tokens)
    {
        if (needTags == null)
            return;

        for (int i = 0; i < needTags.Count; i++)
        {
            Il2CppSystem.Collections.Generic.HashSet<string> needSet = needTags[i];
            if (needSet == null)
                continue;

            foreach (string token in needSet)
                AddMeaningfulFamilyTokens(token, tokens);
        }
    }

    private static void AddFamilyTokensFromParam(string? param, HashSet<string> tokens)
    {
        if (string.IsNullOrWhiteSpace(param))
            return;

        int searchFrom = 0;
        while (searchFrom < param.Length)
        {
            int start = param.IndexOf("need(", searchFrom, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
                break;

            int contentStart = start + "need(".Length;
            int end = param.IndexOf(')', contentStart);
            if (end < 0)
                break;

            string content = param.Substring(contentStart, end - contentStart);
            foreach (string token in content.Split(new[] { ';', ',', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                AddMeaningfulFamilyTokens(token, tokens);

            searchFrom = end + 1;
        }
    }

    private static void AddMeaningfulFamilyTokens(string? token, HashSet<string> tokens)
    {
        if (string.IsNullOrWhiteSpace(token))
            return;

        string clean = token.Trim();
        if (clean.StartsWith("tag[", StringComparison.OrdinalIgnoreCase) && clean.EndsWith("]", StringComparison.Ordinal))
            clean = clean.Substring(4, clean.Length - 5);

        foreach (string part in clean.Split(new[] { '_', '-', ' ', '[', ']', '(', ')' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string normalized = NormalizeFamilyToken(part);
            if (normalized.Length > 0)
                tokens.Add(normalized);
        }
    }

    private static string ModelFamilyToken(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
            return string.Empty;

        foreach (string part in model.Split(new[] { '_', '-', ' ', '[', ']', '(', ')' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string normalized = NormalizeFamilyToken(part);
            if (normalized.Length > 0)
                return normalized;
        }

        return string.Empty;
    }

    private static string NormalizeFamilyToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return string.Empty;

        string value = token.Trim().Trim('"', '\'').ToLowerInvariant();
        return IsMeaningfulFamilyToken(value) ? value : string.Empty;
    }

    private static bool IsMeaningfulFamilyToken(string token)
    {
        if (token.Length < 3)
            return false;

        return token is not
            "bb" and not "bc" and not "ca" and not "cl" and not "dd" and not "tb" and not "ss" and
            "type" and not "need" and not "refit" and not
            "hull" and not "tower" and not "main" and not "sec" and not "funnel" and not
            "old" and not "new" and not "semi" and not "super" and not "modern" and not "modernized" and not
            "small" and not "big" and not "large" and not "medium" and not "flat" and not "advanced" and not
            "early" and not "late" and not "gen" and not "style" and not "typical" and not "mixed" and not
            "barbette" and not "centerline" and not "forward" and not "hybrid" and not "battlecruiser" and not
            "japan" and not "japanese" and not "britain" and not "british" and not "france" and not "french" and not
            "germany" and not "german" and not "russia" and not "russian" and not "austria" and not "italy" and not
            "italian" and not "usa" and not "china" and not "spain" and not "netherlands";
    }

    private static string BroadShipClass(string shipType)
        => shipType switch
        {
            "bb" or "bc" => "capital",
            "ca" or "cl" => "cruiser",
            "dd" or "tb" => "screen",
            "ss" => "submarine",
            _ => string.Empty,
        };

    private static string ShipTypeCode(ShipType? shipType)
    {
        string? value = shipType?.name;
        if (string.IsNullOrWhiteSpace(value))
            value = shipType?.nameUi;
        if (string.IsNullOrWhiteSpace(value))
            value = shipType?.nameFull;

        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
    }

    private static bool IsText(string? value, string needle)
        => !string.IsNullOrWhiteSpace(value) && value.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

    private static string PartLabel(PartData? part)
    {
        string? label = part?.nameUi;
        if (string.IsNullOrWhiteSpace(label))
            label = part?.name;
        if (string.IsNullOrWhiteSpace(label))
            label = part?.type;

        return string.IsNullOrWhiteSpace(label) ? "unknown" : label;
    }

    private static void LogAllowedOverride(PartData part, ShipType shipType, string groupName, string affinityInfo)
    {
        string overrideKey = $"{part.name}|{ShipTypeCode(shipType)}";
        bool firstPartOverride = LoggedAllowedOverrides.Add(overrideKey);
        bool firstGroupOverride = LoggedGroups.Add(groupName);
        if (!firstPartOverride && !firstGroupOverride)
            return;

        Melon<UADVanillaPlusMod>.Logger.Msg(
            $"UADVP superstructure refits: allowed {groupName} override for {ShipTypeCode(shipType).ToUpperInvariant()}: " +
            $"part.name={PartName(part)} part.nameUi={PartLabel(part)} needUnlock={ValueOrNone(part.NeedUnlock)} " +
            $"affinity={ValueOrNone(affinityInfo)} param={ValueOrNone(part.param)}.");
    }

    private static void LogDeniedCandidate(
        PartData? part,
        ShipType? shipType,
        string partGroup,
        string targetGroup,
        bool contextGatePassed,
        bool? unlocked,
        bool? familyGatePassed,
        string affinityInfo,
        bool? relaxedAvailabilityPassed)
    {
        if (deniedCandidateLogCount >= 24)
            return;

        string key = $"{part?.name}|{ShipTypeCode(shipType)}|{partGroup}|{targetGroup}|{contextGatePassed}|{unlocked?.ToString() ?? "not-run"}|{familyGatePassed?.ToString() ?? "not-run"}|{relaxedAvailabilityPassed?.ToString() ?? "not-run"}";
        if (!LoggedDeniedCandidates.Add(key))
            return;

        deniedCandidateLogCount++;
        Melon<UADVanillaPlusMod>.Logger.Msg(
            $"UADVP superstructure refits candidate denied: part={PartName(part)} label={PartLabel(part)} type={part?.type ?? "<null>"} " +
            $"partClass={ValueOrNone(partGroup)} target={ShipTypeCode(shipType).ToUpperInvariant()} targetClass={ValueOrNone(targetGroup)} " +
            $"contextGate={contextGatePassed} unlocked={unlocked?.ToString() ?? "not-run"} familyGate={familyGatePassed?.ToString() ?? "not-run"} " +
            $"affinity={ValueOrNone(affinityInfo)} relaxedAvailability={relaxedAvailabilityPassed?.ToString() ?? "not-run"}.");
    }

    private static string PartName(PartData? part)
        => string.IsNullOrWhiteSpace(part?.name) ? "<unknown>" : part.name;

    private static string ValueOrNone(string value)
        => string.IsNullOrWhiteSpace(value) ? "<none>" : value;

    private static string JoinTokens(HashSet<string> tokens)
        => tokens.Count == 0 ? "<none>" : string.Join("|", tokens.OrderBy(token => token, StringComparer.OrdinalIgnoreCase));

    private static void LogRelaxedCheckException(PartData part, ShipType shipType, Exception ex)
    {
        if (exceptionLogCount++ >= 3)
            return;

        Melon<UADVanillaPlusMod>.Logger.Warning(
            $"UADVP superstructure refits: fallback check failed for {PartLabel(part)} on {ShipTypeCode(shipType).ToUpperInvariant()}. {ex.GetType().Name}: {ex.Message}");
    }
}
