using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using UADVanillaPlus.GameData;
using UadGameData = Il2Cpp.GameData;

namespace UADVanillaPlus.Harmony;

[HarmonyPatch(typeof(UadGameData))]
internal static class DesignSuperstructureRefitHullProxyDataPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(nameof(UadGameData.PostProcessAll))]
    internal static void PostProcessAllPostfix(UadGameData __instance)
    {
        DesignSuperstructureRefitAvailabilityPatch.RebuildHullProxyIndex(__instance);
    }
}

[HarmonyPatch(typeof(Ship))]
internal static class DesignSuperstructureRefitAvailabilityPatch
{
    private const int MaxAllowedLogs = 96;
    private const int MaxDeniedLogs = 64;
    private const int MaxGroupLogs = 8;

    private static readonly HashSet<string> LoggedGroups = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> LoggedAllowed = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> LoggedDenied = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, List<PartData>> HullProxiesByToken = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, int> HullUnlockYearByName = new(StringComparer.OrdinalIgnoreCase);

    private static bool runningRelaxedCheck;
    private static bool hullProxyIndexBuilt;
    private static int allowedLogCount;
    private static int deniedLogCount;

    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    [HarmonyPatch(nameof(Ship.IsPartAvailable), typeof(PartData), typeof(Player), typeof(ShipType), typeof(Ship))]
    internal static void IsPartAvailablePostfix(
        PartData part,
        Player player,
        ShipType shipType,
        Ship ship,
        ref bool __result)
    {
        if (__result || runningRelaxedCheck)
            return;

        try
        {
            if (!ShouldTryFallback(part, player, shipType, ship, out Player? effectivePlayer, out string partGroup, out string targetGroup, out PartData? currentHull))
                return;

            EnsureHullProxyIndex();

            if (!TryBuildNeedRelaxationPlan(part, effectivePlayer!, shipType, ship, currentHull, targetGroup, out NeedRelaxationPlan plan, out string denyReason))
            {
                LogDeniedCandidate(part, shipType, currentHull, partGroup, targetGroup, false, null, denyReason, null);
                return;
            }

            bool relaxedAvailability = TryVanillaAvailabilityWithProxyNeedGroups(part, effectivePlayer!, shipType, ship, plan);
            if (!relaxedAvailability)
            {
                LogDeniedCandidate(part, shipType, currentHull, partGroup, targetGroup, true, false, plan.NeedGroupsSummary, plan.ProxySummary);
                return;
            }

            __result = true;
            LogAllowedOverride(part, shipType, currentHull, partGroup, targetGroup, plan);
        }
        catch (Exception e)
        {
            Melon<UADVanillaPlusMod>.Logger.Warning($"UADVP superstructure compatibility fallback failed for {SafePartName(part)}: {e.GetType().Name}: {e.Message}");
        }
    }

    internal static void RebuildHullProxyIndex(UadGameData? gameData)
    {
        HullProxiesByToken.Clear();
        HullUnlockYearByName.Clear();
        hullProxyIndexBuilt = true;
        IndexHullUnlockYears(gameData);

        int hullCount = 0;
        if (gameData?.parts != null)
        {
            foreach (var entry in gameData.parts)
            {
                PartData? hull = entry.Value;
                if (hull == null || !hull.isHull)
                    continue;

                hullCount++;
                foreach (string token in ExtractMeaningfulHullProxyTokens(hull))
                {
                    if (!HullProxiesByToken.TryGetValue(token, out List<PartData>? hulls))
                    {
                        hulls = new List<PartData>();
                        HullProxiesByToken[token] = hulls;
                    }

                    if (!hulls.Contains(hull))
                        hulls.Add(hull);
                }
            }
        }

        LoggedGroups.Clear();
        LoggedAllowed.Clear();
        LoggedDenied.Clear();
        allowedLogCount = 0;
        deniedLogCount = 0;

        Melon<UADVanillaPlusMod>.Logger.Msg($"UADVP superstructure compatibility indexed {HullProxiesByToken.Count} hull-family proxy token(s) from {hullCount} hull(s); unlock years for {HullUnlockYearByName.Count} part(s).");
    }

    private static void IndexHullUnlockYears(UadGameData? gameData)
    {
        if (gameData?.technologies == null)
            return;

        foreach (var entry in gameData.technologies)
        {
            TechnologyData? tech = entry.Value;
            if (tech == null || tech.year <= 0)
                continue;

            foreach (string token in SplitTopLevelParamTokens(tech.effect))
            {
                if (!TryExtractFunctionValue(token, "unlock", out string unlocks))
                    continue;

                foreach (string partName in SplitSemicolonTokens(unlocks))
                {
                    string normalized = NormalizeToken(partName);
                    if (string.IsNullOrEmpty(normalized))
                        continue;

                    if (!HullUnlockYearByName.TryGetValue(normalized, out int existingYear) || tech.year < existingYear)
                        HullUnlockYearByName[normalized] = tech.year;
                }
            }
        }
    }

    private static bool ShouldTryFallback(
        PartData? part,
        Player? player,
        ShipType? shipType,
        Ship? ship,
        out Player? effectivePlayer,
        out string partGroup,
        out string targetGroup,
        out PartData? currentHull)
    {
        effectivePlayer = null;
        partGroup = string.Empty;
        targetGroup = string.Empty;
        currentHull = null;

        if (!ModSettings.SuperstructureRefitsEnabled)
            return false;

        if (part == null || !IsSuperstructurePart(part) || !HasPositiveNeedTags(part))
            return false;

        ShipType? targetShipType = shipType ?? ship?.shipType;
        targetGroup = BroadShipClass(ShipTypeCode(targetShipType));
        if (string.IsNullOrEmpty(targetGroup))
            return false;

        effectivePlayer = player ?? ship?.player ?? PlayerController.Instance;
        if (effectivePlayer == null || effectivePlayer.isAi || !effectivePlayer.isMain)
            return false;

        if (!IsAllowedDesignContext(ship))
            return false;

        bool partUnlocked;
        try
        {
            partUnlocked = effectivePlayer.IsPartUnlocked(part);
        }
        catch (Exception e)
        {
            LogDeniedCandidate(part, targetShipType, null, string.Empty, targetGroup, false, null, $"part unlock check failed: {e.GetType().Name}", null);
            return false;
        }

        if (!partUnlocked)
        {
            LogDeniedCandidate(part, targetShipType, null, string.Empty, targetGroup, false, false, "explicit part unlock/needunlock not met", null);
            return false;
        }

        partGroup = InferBroadShipClassFromNeedTags(part);
        if (string.IsNullOrEmpty(partGroup) || !SameBroadShipClass(partGroup, targetGroup))
        {
            LogDeniedCandidate(part, targetShipType, null, partGroup, targetGroup, false, true, "candidate class does not match target hull class", null);
            return false;
        }

        currentHull = GetHullData(ship);
        LogGroupOnce(partGroup, targetGroup);
        return true;
    }

    private static bool IsAllowedDesignContext(Ship? ship)
    {
        return GameManager.IsConstructor;
    }

    private static bool TryBuildNeedRelaxationPlan(
        PartData part,
        Player player,
        ShipType shipType,
        Ship ship,
        PartData? currentHull,
        string targetGroup,
        out NeedRelaxationPlan plan,
        out string denyReason)
    {
        plan = new NeedRelaxationPlan(part);
        denyReason = string.Empty;

        var needGroups = CollectNeedGroups(part);
        if (needGroups.Count == 0)
        {
            denyReason = "no positive need groups";
            return false;
        }

        string targetType = ShipTypeCode(shipType ?? ship?.shipType);
        var currentHullTokens = ExtractRawHullTokens(currentHull);
        int currentHullUnlockYear = HullUnlockYear(currentHull);
        if (currentHullUnlockYear <= 0)
        {
            denyReason = $"current hull unlock year unknown for {SafePartName(currentHull)}";
            return false;
        }

        for (int i = 0; i < needGroups.Count; i++)
        {
            IReadOnlyCollection<string> group = needGroups[i];
            plan.AddNeedGroupSummary(group);

            if (NeedGroupSatisfiedByCurrentHull(group, currentHullTokens, targetType))
                continue;

            if (!TrySatisfyNeedGroupByUnlockedHullProxy(group, player, targetGroup, currentHullUnlockYear, out ProxyNeedGroupEvidence evidence, out string groupDenyReason))
            {
                denyReason = $"need group {FormatNeedGroup(group)} not satisfied ({groupDenyReason})";
                return false;
            }

            plan.AddProxySatisfiedGroup(i, evidence);
        }

        if (plan.ProxySatisfiedGroups.Count == 0)
        {
            denyReason = "current hull already satisfies all need groups, but vanilla still denied";
            return false;
        }

        return true;
    }

    private static bool TrySatisfyNeedGroupByUnlockedHullProxy(
        IReadOnlyCollection<string> group,
        Player player,
        string targetGroup,
        int currentHullUnlockYear,
        out ProxyNeedGroupEvidence evidence,
        out string denyReason)
    {
        evidence = default;
        denyReason = "no meaningful proxy token";

        foreach (string token in group.Select(NormalizeToken).Where(IsMeaningfulProxyToken).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string tokenGroup = BroadShipClassFromNeedToken(token);
            if (!string.IsNullOrEmpty(tokenGroup) && !SameBroadShipClass(tokenGroup, targetGroup))
            {
                denyReason = $"proxy token {token} is {tokenGroup}, target is {targetGroup}";
                continue;
            }

            if (!HullProxiesByToken.TryGetValue(token, out List<PartData>? proxyHulls) || proxyHulls.Count == 0)
            {
                denyReason = $"proxy token {token} has no indexed hull";
                continue;
            }

            foreach (PartData proxyHull in proxyHulls)
            {
                string proxyGroup = BroadShipClass(ShipTypeCode(proxyHull.shipType));
                if (string.IsNullOrEmpty(proxyGroup))
                    proxyGroup = BroadShipClass(ExtractShipTypeFromParam(proxyHull.param));

                if (!SameBroadShipClass(proxyGroup, targetGroup))
                {
                    denyReason = $"proxy hull {SafePartName(proxyHull)} is {proxyGroup}, target is {targetGroup}";
                    continue;
                }

                if (!IsProxyHullCountryCompatible(proxyHull, player, out string countryReason))
                {
                    denyReason = $"proxy hull {SafePartName(proxyHull)} country mismatch ({countryReason})";
                    continue;
                }

                int proxyHullUnlockYear = HullUnlockYear(proxyHull);
                if (proxyHullUnlockYear <= 0)
                {
                    denyReason = $"proxy hull {SafePartName(proxyHull)} unlock year unknown";
                    continue;
                }

                if (proxyHullUnlockYear <= currentHullUnlockYear)
                {
                    denyReason = $"proxy hull {SafePartName(proxyHull)} is not newer than current hull ({proxyHullUnlockYear} <= {currentHullUnlockYear})";
                    continue;
                }

                bool unlocked = IsHullUnlocked(player, proxyHull);
                evidence = new ProxyNeedGroupEvidence(
                    token,
                    proxyHull,
                    unlocked,
                    currentHullUnlockYear,
                    proxyHullUnlockYear,
                    PlayerDataLabel(player.data),
                    CountriesLabel(proxyHull));
                if (unlocked)
                    return true;

                denyReason = $"proxy hull {SafePartName(proxyHull)} for {token} not unlocked";
            }
        }

        return false;
    }

    private static bool TryVanillaAvailabilityWithProxyNeedGroups(
        PartData part,
        Player player,
        ShipType shipType,
        Ship ship,
        NeedRelaxationPlan plan)
    {
        if (part.needTags == null || plan.ProxySatisfiedGroups.Count == 0)
            return false;

        var originalNeedTags = part.needTags;
        var relaxedNeedTags = new Il2CppSystem.Collections.Generic.List<Il2CppSystem.Collections.Generic.HashSet<string>>();

        for (int i = 0; i < originalNeedTags.Count; i++)
        {
            if (!plan.ProxySatisfiedGroups.Contains(i))
                relaxedNeedTags.Add(originalNeedTags[i]);
        }

        try
        {
            runningRelaxedCheck = true;
            part.needTags = relaxedNeedTags;
            return Ship.IsPartAvailable(part, player, shipType, ship);
        }
        catch (Exception e)
        {
            Melon<UADVanillaPlusMod>.Logger.Warning($"UADVP superstructure compatibility proxy availability failed for {SafePartName(part)}: {e.GetType().Name}: {e.Message}");
            return false;
        }
        finally
        {
            part.needTags = originalNeedTags;
            runningRelaxedCheck = false;
        }
    }

    private static void EnsureHullProxyIndex()
    {
        if (hullProxyIndexBuilt)
            return;

        try
        {
            RebuildHullProxyIndex(G.GameData);
        }
        catch (Exception e)
        {
            hullProxyIndexBuilt = true;
            Melon<UADVanillaPlusMod>.Logger.Warning($"UADVP superstructure compatibility could not build hull proxy index: {e.GetType().Name}: {e.Message}");
        }
    }

    private static bool IsSuperstructurePart(PartData part)
    {
        string type = part.type?.Trim().ToLowerInvariant() ?? string.Empty;
        return type == "tower_main" || type == "tower_sec" || type == "funnel";
    }

    private static bool HasPositiveNeedTags(PartData part)
    {
        return part.needTags != null && part.needTags.Count > 0;
    }

    private static List<IReadOnlyCollection<string>> CollectNeedGroups(PartData part)
    {
        var groups = new List<IReadOnlyCollection<string>>();
        if (part.needTags == null)
            return groups;

        for (int i = 0; i < part.needTags.Count; i++)
        {
            var group = part.needTags[i];
            if (group == null || group.Count == 0)
                continue;

            var tokens = new List<string>();
            foreach (string token in group)
            {
                string normalized = NormalizeToken(token);
                if (!string.IsNullOrEmpty(normalized) && !tokens.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                    tokens.Add(normalized);
            }

            if (tokens.Count > 0)
                groups.Add(tokens);
        }

        return groups;
    }

    private static bool NeedGroupSatisfiedByCurrentHull(IReadOnlyCollection<string> group, HashSet<string> currentHullTokens, string targetShipType)
    {
        foreach (string token in group.Select(NormalizeToken))
        {
            if (string.IsNullOrEmpty(token))
                continue;

            if (currentHullTokens.Contains(token))
                return true;

            if (IsShipTypeToken(token) && string.Equals(token, targetShipType, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static HashSet<string> ExtractRawHullTokens(PartData? hull)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (hull == null)
            return tokens;

        foreach (string token in SplitTopLevelParamTokens(hull.param))
        {
            if (TryExtractFunctionValue(token, "type", out string typeValue))
            {
                string typeToken = NormalizeToken(typeValue);
                if (!string.IsNullOrEmpty(typeToken))
                    tokens.Add(typeToken);
                continue;
            }

            if (token.Contains('('))
                continue;

            string normalized = NormalizeToken(token);
            if (!string.IsNullOrEmpty(normalized))
                tokens.Add(normalized);
        }

        if (hull.needTags != null)
        {
            for (int i = 0; i < hull.needTags.Count; i++)
            {
                foreach (string token in hull.needTags[i])
                {
                    string normalized = NormalizeToken(token);
                    if (!string.IsNullOrEmpty(normalized))
                        tokens.Add(normalized);
                }
            }
        }

        string shipType = ShipTypeCode(hull.shipType);
        if (!string.IsNullOrEmpty(shipType))
            tokens.Add(shipType);

        return tokens;
    }

    private static IEnumerable<string> ExtractMeaningfulHullProxyTokens(PartData hull)
    {
        return ExtractRawHullTokens(hull).Where(IsMeaningfulProxyToken).Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static List<string> SplitTopLevelParamTokens(string? param)
    {
        var tokens = new List<string>();
        if (string.IsNullOrWhiteSpace(param))
            return tokens;

        int depth = 0;
        int start = 0;
        for (int i = 0; i < param.Length; i++)
        {
            char c = param[i];
            if (c == '(' || c == '[' || c == '{')
                depth++;
            else if ((c == ')' || c == ']' || c == '}') && depth > 0)
                depth--;
            else if (c == ',' && depth == 0)
            {
                AddToken(param[start..i]);
                start = i + 1;
            }
        }

        AddToken(param[start..]);
        return tokens;

        void AddToken(string token)
        {
            token = token.Trim();
            if (!string.IsNullOrEmpty(token))
                tokens.Add(token);
        }
    }

    private static IEnumerable<string> SplitSemicolonTokens(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            yield break;

        foreach (string token in value.Split(';'))
        {
            string normalized = NormalizeToken(token);
            if (!string.IsNullOrEmpty(normalized))
                yield return normalized;
        }
    }

    private static bool TryExtractFunctionValue(string token, string functionName, out string value)
    {
        value = string.Empty;
        token = token.Trim();
        string prefix = functionName + "(";
        if (!token.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !token.EndsWith(")", StringComparison.Ordinal))
            return false;

        value = token[prefix.Length..^1].Trim();
        return !string.IsNullOrEmpty(value);
    }

    private static string NormalizeToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return string.Empty;

        string normalized = token.Trim().Trim('"', '\'', ';');
        if (normalized.StartsWith("tag[", StringComparison.OrdinalIgnoreCase) && normalized.EndsWith("]", StringComparison.Ordinal))
            normalized = normalized[4..^1].Trim();

        return normalized.Trim().Trim('"', '\'', ';');
    }

    private static bool IsMeaningfulProxyToken(string token)
    {
        token = NormalizeToken(token);
        if (string.IsNullOrEmpty(token))
            return false;

        if (token.Contains('(') || token.Contains(')') || token.Contains('[') || token.Contains(']'))
            return false;

        string lower = token.ToLowerInvariant();
        if (GenericProxyTokens.Contains(lower) || IsShipTypeToken(lower))
            return false;

        if (lower.Length < 3)
            return false;

        if (lower.StartsWith("need", StringComparison.Ordinal) ||
            lower.StartsWith("type", StringComparison.Ordinal) ||
            lower.StartsWith("tag", StringComparison.Ordinal) ||
            lower.Contains("mount") ||
            lower.Contains("barbette") ||
            lower.Contains("centerline") ||
            lower.Contains("forward"))
        {
            return false;
        }

        if (lower.StartsWith("b_", StringComparison.Ordinal))
            return false;

        return StartsWithAny(token, "BB_", "BC_", "CA_", "CL_", "DD_", "TB_", "SS_");
    }

    private static readonly HashSet<string> GenericProxyTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "bb", "bc", "ca", "cl", "dd", "tb", "ss",
        "b", "g1", "g2", "g3", "g4", "g5",
        "type", "hull", "ship", "ships", "all",
        "old", "older", "modern", "modernized", "semi", "large", "small", "big", "medium",
        "heavy", "light", "early", "late", "main", "sec", "secondary",
        "barbette_need", "battlecruiser_forward", "russiancenterline", "centerline", "forward",
        "with_barbette", "funnel_mount", "big_main", "small_main", "large_main",
        "japan", "usa", "britain", "british", "france", "french", "germany", "german",
        "italy", "italian", "spain", "spanish", "russia", "russian", "austria", "austrian",
        "china", "chinese"
    };

    private static string InferBroadShipClassFromNeedTags(PartData part)
    {
        foreach (var group in CollectNeedGroups(part))
        {
            foreach (string token in group)
            {
                string broad = BroadShipClassFromNeedToken(token);
                if (!string.IsNullOrEmpty(broad))
                    return broad;
            }
        }

        string fromShipType = BroadShipClass(ShipTypeCode(part.shipType));
        if (!string.IsNullOrEmpty(fromShipType))
            return fromShipType;

        return string.Empty;
    }

    private static string BroadShipClassFromNeedToken(string token)
    {
        token = NormalizeToken(token).ToLowerInvariant();
        if (string.IsNullOrEmpty(token))
            return string.Empty;

        if (token == "bb" || token.StartsWith("bb_", StringComparison.Ordinal) ||
            token == "bc" || token.StartsWith("bc_", StringComparison.Ordinal))
        {
            return "capital";
        }

        if (token == "ca" || token.StartsWith("ca_", StringComparison.Ordinal) ||
            token == "cl" || token.StartsWith("cl_", StringComparison.Ordinal))
        {
            return "cruiser";
        }

        if (token == "dd" || token.StartsWith("dd_", StringComparison.Ordinal) ||
            token == "tb" || token.StartsWith("tb_", StringComparison.Ordinal))
        {
            return "screen";
        }

        if (token == "ss" || token.StartsWith("ss_", StringComparison.Ordinal))
            return "submarine";

        return string.Empty;
    }

    private static string BroadShipClass(string shipTypeCode)
    {
        return shipTypeCode switch
        {
            "bb" or "bc" or "b" => "capital",
            "ca" or "cl" => "cruiser",
            "dd" or "tb" => "screen",
            "ss" => "submarine",
            _ => string.Empty
        };
    }

    private static bool SameBroadShipClass(string a, string b)
    {
        return !string.IsNullOrEmpty(a) &&
               !string.IsNullOrEmpty(b) &&
               string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsShipTypeToken(string token)
    {
        return token.Equals("bb", StringComparison.OrdinalIgnoreCase) ||
               token.Equals("bc", StringComparison.OrdinalIgnoreCase) ||
               token.Equals("ca", StringComparison.OrdinalIgnoreCase) ||
               token.Equals("cl", StringComparison.OrdinalIgnoreCase) ||
               token.Equals("dd", StringComparison.OrdinalIgnoreCase) ||
               token.Equals("tb", StringComparison.OrdinalIgnoreCase) ||
               token.Equals("ss", StringComparison.OrdinalIgnoreCase) ||
               token.Equals("b", StringComparison.OrdinalIgnoreCase);
    }

    private static string ShipTypeCode(ShipType? shipType)
    {
        return shipType?.name?.Trim().ToLowerInvariant() ?? string.Empty;
    }

    private static string ExtractShipTypeFromParam(string? param)
    {
        foreach (string token in SplitTopLevelParamTokens(param))
        {
            if (TryExtractFunctionValue(token, "type", out string typeValue))
                return NormalizeToken(typeValue).ToLowerInvariant();
        }

        return string.Empty;
    }

    private static PartData? GetHullData(Ship? ship)
    {
        if (ship?.hull?.data != null)
            return ship.hull.data;

        return ship?.design?.hull?.data;
    }

    private static bool IsHullUnlocked(Player player, PartData hull)
    {
        try
        {
            if (player.IsHullUnlocked(hull))
                return true;
        }
        catch
        {
            // Fall through to the string overload; some vanilla states tolerate one path better than the other.
        }

        try
        {
            return player.IsHullUnlocked(hull.name);
        }
        catch
        {
            return false;
        }
    }

    private static int HullUnlockYear(PartData? hull)
    {
        if (hull?.name == null)
            return 0;

        return HullUnlockYearByName.TryGetValue(hull.name, out int year) ? year : 0;
    }

    private static bool IsProxyHullCountryCompatible(PartData proxyHull, Player player, out string reason)
    {
        reason = string.Empty;
        PlayerData? playerData = player.data;
        if (playerData == null)
        {
            reason = "player country missing";
            return false;
        }

        if (proxyHull.countriesx == null || proxyHull.countriesx.Count == 0)
        {
            reason = "generic";
            return true;
        }

        foreach (PlayerData country in proxyHull.countriesx)
        {
            if (PlayerDataMatches(country, playerData))
            {
                reason = $"{CountriesLabel(proxyHull)} contains {PlayerDataLabel(playerData)}";
                return true;
            }
        }

        reason = $"{CountriesLabel(proxyHull)} does not contain {PlayerDataLabel(playerData)}";
        return false;
    }

    private static bool PlayerDataMatches(PlayerData? a, PlayerData? b)
    {
        if (a == null || b == null)
            return false;

        if (a.Pointer == b.Pointer)
            return true;

        if (a.Id != 0 && a.Id == b.Id)
            return true;

        return !string.IsNullOrEmpty(a.name) &&
               !string.IsNullOrEmpty(b.name) &&
               string.Equals(a.name, b.name, StringComparison.OrdinalIgnoreCase);
    }

    private static string CountriesLabel(PartData? part)
    {
        if (part?.countriesx == null || part.countriesx.Count == 0)
            return "<generic>";

        var labels = new List<string>();
        foreach (PlayerData country in part.countriesx)
        {
            labels.Add(PlayerDataLabel(country));
            if (labels.Count >= 6)
                break;
        }

        return string.Join("|", labels);
    }

    private static string PlayerDataLabel(PlayerData? data)
    {
        if (data == null)
            return "<none>";

        if (!string.IsNullOrEmpty(data.name))
            return $"{data.name}#{data.Id}";

        if (!string.IsNullOrEmpty(data.nameUi))
            return $"{data.nameUi}#{data.Id}";

        return $"#{data.Id}";
    }

    private static bool StartsWithAny(string value, params string[] prefixes)
    {
        foreach (string prefix in prefixes)
        {
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string FormatNeedGroup(IReadOnlyCollection<string> group)
    {
        return "[" + string.Join("|", group.Select(NormalizeToken).Where(token => !string.IsNullOrEmpty(token)).Take(8)) + "]";
    }

    private static string SafePartName(PartData? part)
    {
        if (part == null)
            return "<none>";

        string uiName = part.nameUi ?? string.Empty;
        return string.IsNullOrWhiteSpace(uiName) || string.Equals(uiName, part.name, StringComparison.OrdinalIgnoreCase)
            ? part.name
            : $"{part.name}/{uiName}";
    }

    private static void LogGroupOnce(string partGroup, string targetGroup)
    {
        if (LoggedGroups.Count >= MaxGroupLogs)
            return;

        string key = $"{partGroup}->{targetGroup}";
        if (!LoggedGroups.Add(key))
            return;

        Melon<UADVanillaPlusMod>.Logger.Msg($"UADVP superstructure compatibility considering proxy need gates for {partGroup} part(s) on {targetGroup} hull(s).");
    }

    private static void LogAllowedOverride(PartData part, ShipType shipType, PartData? currentHull, string partGroup, string targetGroup, NeedRelaxationPlan plan)
    {
        if (allowedLogCount >= MaxAllowedLogs)
            return;

        string key = $"{part.name}|{shipType?.name}|{currentHull?.name}|{plan.ProxySummary}";
        if (!LoggedAllowed.Add(key))
            return;

        allowedLogCount++;
        Melon<UADVanillaPlusMod>.Logger.Msg(
            "UADVP superstructure compatibility allowed " +
            $"{SafePartName(part)} for {ShipTypeCode(shipType)} by hull proxy. " +
            $"currentHull={SafePartName(currentHull)}, partClass={partGroup}, targetClass={targetGroup}, " +
            $"needGroups={plan.NeedGroupsSummary}, proxy={plan.ProxySummary}, needunlock={part.NeedUnlock ?? "<none>"}");
    }

    private static void LogDeniedCandidate(
        PartData? part,
        ShipType? shipType,
        PartData? currentHull,
        string partGroup,
        string targetGroup,
        bool proxyGate,
        bool? relaxedAvailability,
        string needOrReason,
        string? proxySummary)
    {
        if (deniedLogCount >= MaxDeniedLogs || part == null)
            return;

        string key = $"{part.name}|{shipType?.name}|{currentHull?.name}|{needOrReason}|{proxySummary}|{relaxedAvailability}";
        if (!LoggedDenied.Add(key))
            return;

        deniedLogCount++;
        Melon<UADVanillaPlusMod>.Logger.Msg(
            "UADVP superstructure compatibility denied " +
            $"{SafePartName(part)} for {ShipTypeCode(shipType)}. " +
            $"currentHull={SafePartName(currentHull)}, partClass={partGroup}, targetClass={targetGroup}, " +
            $"proxyGate={proxyGate}, relaxedAvailability={relaxedAvailability?.ToString() ?? "<not-run>"}, " +
            $"need/reason={needOrReason}, proxy={proxySummary ?? "<none>"}");
    }

    private readonly struct ProxyNeedGroupEvidence
    {
        internal ProxyNeedGroupEvidence(
            string token,
            PartData proxyHull,
            bool unlocked,
            int currentHullUnlockYear,
            int proxyHullUnlockYear,
            string playerCountry,
            string proxyCountries)
        {
            Token = token;
            ProxyHull = proxyHull;
            Unlocked = unlocked;
            CurrentHullUnlockYear = currentHullUnlockYear;
            ProxyHullUnlockYear = proxyHullUnlockYear;
            PlayerCountry = playerCountry;
            ProxyCountries = proxyCountries;
        }

        internal string Token { get; }
        internal PartData ProxyHull { get; }
        internal bool Unlocked { get; }
        internal int CurrentHullUnlockYear { get; }
        internal int ProxyHullUnlockYear { get; }
        internal string PlayerCountry { get; }
        internal string ProxyCountries { get; }

        public override string ToString()
        {
            return $"{Token}->{SafePartName(ProxyHull)} countries={ProxyCountries} player={PlayerCountry} years={CurrentHullUnlockYear}->{ProxyHullUnlockYear} unlocked={Unlocked}";
        }
    }

    private sealed class NeedRelaxationPlan
    {
        private readonly PartData part;
        private readonly List<string> needGroupSummaries = new();
        private readonly List<ProxyNeedGroupEvidence> evidences = new();

        internal NeedRelaxationPlan(PartData part)
        {
            this.part = part;
        }

        internal HashSet<int> ProxySatisfiedGroups { get; } = new();

        internal string NeedGroupsSummary => needGroupSummaries.Count == 0
            ? "<none>"
            : string.Join(", ", needGroupSummaries.Take(6));

        internal string ProxySummary => evidences.Count == 0
            ? $"<none for {SafePartName(part)}>"
            : string.Join(", ", evidences.Select(e => e.ToString()).Take(6));

        internal void AddNeedGroupSummary(IReadOnlyCollection<string> group)
        {
            needGroupSummaries.Add(FormatNeedGroup(group));
        }

        internal void AddProxySatisfiedGroup(int index, ProxyNeedGroupEvidence evidence)
        {
            ProxySatisfiedGroups.Add(index);
            evidences.Add(evidence);
        }
    }
}
