using System.Reflection;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

namespace UADVanillaPlus.Harmony;

// Patch intent: make campaign-map task-force icons show rough force size
// without adding text. The vanilla ownership color becomes a bottom-up fill,
// while the existing icon silhouette stays visible as a muted backing layer.
[HarmonyPatch(typeof(MapUI), nameof(MapUI.RefreshMovingGroups))]
internal static class CampaignTaskForceTonnageIndicatorPatch
{
    private const string FillImageName = "UADVP_TaskForceTonnageFill";
    private const float FullStackBattleTonnage = 100000f;
    private const float MinimumVisibleFill = 0.1f;
    private static MethodInfo? shipGroupsGetter;
    private static bool attemptedShipGroupsGetterResolve;
    private static bool loggedResolvedShipGroupsGetter;
    private static bool loggedShipGroupsGetterFailure;
    private static FieldInfo? shipGroupsField;
    private static bool attemptedShipGroupsFieldResolve;
    private static bool loggedShipGroupsFieldDump;
    private static bool loggedResolvedShipGroupsField;
    private static string lastLoggedSummary = string.Empty;
    private static bool loggedMissingShipGroups;

    [HarmonyPostfix]
    private static void Postfix(MapUI __instance)
        => RefreshTaskForceIndicators(__instance);

    internal static void SyncWrappedClone(ShipUI source, ShipUI copy)
    {
        try
        {
            Image? sourceIcon = source?.Icon;
            Image? copyIcon = copy?.Icon;
            if (sourceIcon == null || copyIcon == null)
                return;

            Image? sourceFill = FindFillImage(sourceIcon);
            if (sourceFill == null)
            {
                HideFillImage(copyIcon);
                return;
            }

            Image? copyFill = EnsureFillImage(copyIcon);
            if (copyFill == null)
                return;

            CopyIconBacking(sourceIcon, copyIcon);
            CopyFillImage(sourceFill, copyFill);
        }
        catch (Exception ex)
        {
            Melon<UADVanillaPlusMod>.Logger.Warning(
                $"UADVP task-force tonnage indicator clone sync failed. {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void RefreshTaskForceIndicators(MapUI? mapUi)
    {
        try
        {
            if (mapUi == null)
                return;

            var groups = GetShipGroups(mapUi);
            if (groups == null)
                return;

            int updated = 0;
            int fullStacks = 0;
            float maxTonnage = 0f;

            foreach (var entry in groups)
            {
                CampaignController.TaskForce? group = entry.Key;
                CampaignMapElement? element = entry.Value;
                if (group == null || element == null)
                    continue;

                ShipUI? ship = element.TryCast<ShipUI>();
                if (ship == null)
                    continue;

                if (!ApplyIndicator(ship, group, out float tonnage, out bool isFullStack))
                    continue;

                updated++;
                if (isFullStack)
                    fullStacks++;
                if (tonnage > maxTonnage)
                    maxTonnage = tonnage;
            }

            LogSummaryOnce(updated, fullStacks, maxTonnage);
        }
        catch (Exception ex)
        {
            Melon<UADVanillaPlusMod>.Logger.Warning(
                $"UADVP task-force tonnage indicators failed; leaving vanilla task-force icons intact. {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static Il2CppSystem.Collections.Generic.Dictionary<CampaignController.TaskForce, CampaignMapElement>? GetShipGroups(MapUI mapUi)
    {
        if (TryGetShipGroupsByAccessor(mapUi, out var accessorGroups))
            return accessorGroups;

        FieldInfo? field = ResolveShipGroupsField(mapUi);
        if (field == null)
        {
            LogMissingShipGroups("dictionary field was not found");
            return null;
        }

        object? value = field.GetValue(mapUi);
        if (value == null)
            return null;

        if (value is Il2CppSystem.Collections.Generic.Dictionary<CampaignController.TaskForce, CampaignMapElement> groups)
            return groups;

        LogMissingShipGroups($"field had unexpected type {value.GetType().FullName}");
        DumpMapUiFieldsOnce(mapUi, "resolved field value had an unexpected type");
        return null;
    }

    private static bool TryGetShipGroupsByAccessor(
        MapUI mapUi,
        out Il2CppSystem.Collections.Generic.Dictionary<CampaignController.TaskForce, CampaignMapElement>? groups)
    {
        groups = null;
        MethodInfo? getter = ResolveShipGroupsGetter(mapUi);
        if (getter == null)
            return false;

        object? value;
        try
        {
            value = getter.Invoke(mapUi, null);
        }
        catch (Exception ex)
        {
            LogShipGroupsGetterFailureOnce(getter, ex);
            return false;
        }

        if (value == null)
            return true;

        if (value is Il2CppSystem.Collections.Generic.Dictionary<CampaignController.TaskForce, CampaignMapElement> typedGroups)
        {
            groups = typedGroups;
            LogResolvedShipGroupsGetter(getter, value, SafeDictionaryCount(typedGroups));
            return true;
        }

        LogMissingShipGroups($"accessor '{getter.Name}' returned unexpected type {value.GetType().FullName}");
        return false;
    }

    private static MethodInfo? ResolveShipGroupsGetter(MapUI mapUi)
    {
        if (shipGroupsGetter != null || attemptedShipGroupsGetterResolve)
            return shipGroupsGetter;

        attemptedShipGroupsGetterResolve = true;
        shipGroupsGetter = FindShipGroupsGetter(typeof(MapUI));

        Type runtimeType = mapUi.GetType();
        if (shipGroupsGetter == null && runtimeType != typeof(MapUI))
            shipGroupsGetter = FindShipGroupsGetter(runtimeType);

        return shipGroupsGetter;
    }

    private static MethodInfo? FindShipGroupsGetter(Type type)
    {
        MethodInfo? getter = null;

        try
        {
            getter = AccessTools.PropertyGetter(type, "shipGroups");
            if (IsShipGroupsGetter(getter))
                return getter;
        }
        catch
        {
            // Try the generated getter name below.
        }

        try
        {
            getter = AccessTools.Method(type, "get_shipGroups");
            if (IsShipGroupsGetter(getter))
                return getter;
        }
        catch
        {
            // Fall back to a method scan below.
        }

        foreach (MethodInfo method in GetMapUiMethods(type))
        {
            if (IsShipGroupsGetter(method))
                return method;
        }

        return null;
    }

    private static System.Collections.Generic.List<MethodInfo> GetMapUiMethods(Type type)
    {
        System.Collections.Generic.List<MethodInfo> methods = new();
        System.Collections.Generic.HashSet<string> seen = new();

        try
        {
            foreach (MethodInfo method in AccessTools.GetDeclaredMethods(type))
                AddMapUiMethod(method, methods, seen);
        }
        catch
        {
            // Fall back to normal reflection below.
        }

        try
        {
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                AddMapUiMethod(method, methods, seen);
        }
        catch
        {
            // Keep any methods already collected.
        }

        return methods;
    }

    private static void AddMapUiMethod(
        MethodInfo method,
        System.Collections.Generic.List<MethodInfo> methods,
        System.Collections.Generic.HashSet<string> seen)
    {
        string key = $"{method.DeclaringType?.FullName ?? "<unknown>"}.{method.Name}:{FieldTypeName(method.ReturnType)}";
        if (!seen.Add(key))
            return;

        methods.Add(method);
    }

    private static bool IsShipGroupsGetter(MethodInfo? method)
    {
        if (method == null || method.GetParameters().Length != 0)
            return false;

        return method.Name == "get_shipGroups" || IsShipGroupsDictionaryType(method.ReturnType);
    }

    private static int SafeDictionaryCount(Il2CppSystem.Collections.Generic.Dictionary<CampaignController.TaskForce, CampaignMapElement> groups)
    {
        try
        {
            return groups.Count;
        }
        catch
        {
            return -1;
        }
    }

    private static void LogResolvedShipGroupsGetter(MethodInfo getter, object value, int count)
    {
        if (loggedResolvedShipGroupsGetter)
            return;

        loggedResolvedShipGroupsGetter = true;
        Melon<UADVanillaPlusMod>.Logger.Msg(
            $"UADVP task-force tonnage indicators resolved MapUI ship-groups accessor '{getter.Name}'; " +
            $"returnType={FieldTypeName(getter.ReturnType)}, valueType={value.GetType().FullName}, count={count}.");
    }

    private static void LogShipGroupsGetterFailureOnce(MethodInfo getter, Exception ex)
    {
        if (loggedShipGroupsGetterFailure)
            return;

        loggedShipGroupsGetterFailure = true;
        Melon<UADVanillaPlusMod>.Logger.Warning(
            $"UADVP task-force tonnage indicators could not invoke MapUI ship-groups accessor '{getter.Name}'. " +
            $"{ex.GetType().Name}: {ex.Message}");
    }

    private static FieldInfo? ResolveShipGroupsField(MapUI mapUi)
    {
        if (shipGroupsField != null || attemptedShipGroupsFieldResolve)
            return shipGroupsField;

        attemptedShipGroupsFieldResolve = true;
        System.Collections.Generic.List<FieldInfo> fields = GetMapUiFields(mapUi);

        foreach (FieldInfo field in fields)
        {
            if (!IsShipGroupsDictionaryType(field.FieldType))
                continue;

            shipGroupsField = field;
            LogResolvedShipGroupsField(field, "type");
            return shipGroupsField;
        }

        foreach (FieldInfo field in fields)
        {
            try
            {
                object? value = field.GetValue(mapUi);
                if (value is not Il2CppSystem.Collections.Generic.Dictionary<CampaignController.TaskForce, CampaignMapElement>)
                    continue;

                shipGroupsField = field;
                LogResolvedShipGroupsField(field, "runtime value");
                return shipGroupsField;
            }
            catch
            {
                // Some generated Il2Cpp fields may reject reflection reads; keep scanning.
            }
        }

        DumpMapUiFieldsOnce(mapUi, "could not find TaskForce to CampaignMapElement dictionary");
        return null;
    }

    private static System.Collections.Generic.List<FieldInfo> GetMapUiFields(MapUI mapUi)
    {
        System.Collections.Generic.List<FieldInfo> fields = new();
        System.Collections.Generic.HashSet<string> seen = new();
        AddMapUiFields(typeof(MapUI), fields, seen);

        Type runtimeType = mapUi.GetType();
        if (runtimeType != typeof(MapUI))
            AddMapUiFields(runtimeType, fields, seen);

        return fields;
    }

    private static void AddMapUiFields(
        Type type,
        System.Collections.Generic.List<FieldInfo> fields,
        System.Collections.Generic.HashSet<string> seen)
    {
        try
        {
            foreach (FieldInfo field in AccessTools.GetDeclaredFields(type))
                AddMapUiField(field, fields, seen);
        }
        catch
        {
            // Fall back to normal reflection below.
        }

        try
        {
            foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                AddMapUiField(field, fields, seen);
        }
        catch
        {
            // If the runtime type resists one reflection path, keep any fields we already collected.
        }
    }

    private static void AddMapUiField(
        FieldInfo field,
        System.Collections.Generic.List<FieldInfo> fields,
        System.Collections.Generic.HashSet<string> seen)
    {
        string key = $"{field.DeclaringType?.FullName ?? "<unknown>"}.{field.Name}:{FieldTypeName(field.FieldType)}";
        if (!seen.Add(key))
            return;

        fields.Add(field);
    }

    private static bool IsShipGroupsDictionaryType(Type? type)
    {
        if (type == null)
            return false;

        try
        {
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Il2CppSystem.Collections.Generic.Dictionary<,>))
            {
                Type[] args = type.GetGenericArguments();
                if (args.Length == 2 &&
                    args[0] == typeof(CampaignController.TaskForce) &&
                    args[1] == typeof(CampaignMapElement))
                {
                    return true;
                }
            }
        }
        catch
        {
            // Fall back to name matching; Il2Cpp generated types can be prickly here.
        }

        string typeName = FieldTypeName(type);
        return typeName.Contains("Dictionary") &&
               typeName.Contains("CampaignController") &&
               typeName.Contains("TaskForce") &&
               typeName.Contains("CampaignMapElement");
    }

    private static string FieldTypeName(Type? type)
        => type?.FullName ?? type?.Name ?? "<unknown>";

    private static void LogResolvedShipGroupsField(FieldInfo field, string method)
    {
        if (loggedResolvedShipGroupsField)
            return;

        loggedResolvedShipGroupsField = true;
        Melon<UADVanillaPlusMod>.Logger.Msg(
            $"UADVP task-force tonnage indicators resolved MapUI ship-groups field '{field.Name}' by {method}; " +
            $"type={FieldTypeName(field.FieldType)}.");
    }

    private static void DumpMapUiFieldsOnce(MapUI mapUi, string reason)
    {
        if (loggedShipGroupsFieldDump)
            return;

        loggedShipGroupsFieldDump = true;
        System.Text.StringBuilder dump = new();
        dump.Append($"UADVP task-force tonnage indicators MapUI field dump ({reason}).");
        foreach (FieldInfo field in GetMapUiFields(mapUi))
            dump.AppendLine().Append("  ").Append(field.Name).Append(" : ").Append(FieldTypeName(field.FieldType));

        Melon<UADVanillaPlusMod>.Logger.Warning(dump.ToString());
    }

    private static bool ApplyIndicator(ShipUI ship, CampaignController.TaskForce group, out float tonnage, out bool isFullStack)
    {
        tonnage = SafeBattleTonnage(group);
        isFullStack = tonnage >= FullStackBattleTonnage;

        Image? icon = ship.Icon;
        if (icon == null)
            return false;

        Image? fill = EnsureFillImage(icon);
        if (fill == null)
            return false;

        Color stackColor = ResolveStackColor(ship, group);
        ConfigureBackingIcon(icon, stackColor);
        ConfigureFillImage(fill, icon, stackColor, FillAmountFor(tonnage));
        return true;
    }

    private static float SafeBattleTonnage(CampaignController.TaskForce group)
    {
        try
        {
            return Mathf.Max(0f, group.BattleTonnage());
        }
        catch
        {
            return 0f;
        }
    }

    private static float FillAmountFor(float tonnage)
    {
        if (tonnage <= 0f)
            return 0f;

        float rawFill = Mathf.Clamp01(tonnage / FullStackBattleTonnage);
        return Mathf.Max(MinimumVisibleFill, rawFill);
    }

    private static Color ResolveStackColor(ShipUI ship, CampaignController.TaskForce group)
    {
        try
        {
            Player? controller = group.Controller;
            Player? player = PlayerController.Instance;
            if (controller != null && player != null)
            {
                if (controller == player)
                    return WithVisibleAlpha(ship.FriendlyColor);

                if (IsAtWarWith(player, controller))
                    return WithVisibleAlpha(ship.EnemyColor);
            }
        }
        catch
        {
            // Fall back to vanilla's current icon color if relation data is unavailable.
        }

        Color current = ship.Icon != null ? ship.Icon.color : ship.DefaultColor;
        if (!LooksLikeMutedBacking(current))
            return WithVisibleAlpha(current);

        return WithVisibleAlpha(ship.DefaultColor);
    }

    private static bool IsAtWarWith(Player player, Player controller)
    {
        var relations = CampaignController.Instance?.CampaignData?.Relations;
        if (relations == null)
            return false;

        foreach (var entry in relations)
        {
            Relation? relation = entry.Value;
            if (relation == null || !relation.isWar)
                continue;

            if ((SamePlayer(relation.a, player) && SamePlayer(relation.b, controller)) ||
                (SamePlayer(relation.a, controller) && SamePlayer(relation.b, player)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool SamePlayer(Player? a, Player? b)
    {
        if (a == null || b == null)
            return false;

        return a == b || (a.data != null && b.data != null && a.data == b.data);
    }

    private static bool LooksLikeMutedBacking(Color color)
        => Mathf.Abs(color.r - color.g) < 0.02f &&
           Mathf.Abs(color.g - color.b) < 0.02f &&
           color.r >= 0.1f &&
           color.r <= 0.3f &&
           color.a < 0.9f;

    private static Color WithVisibleAlpha(Color color)
    {
        if (color.a < 0.05f)
            color.a = 1f;
        return color;
    }

    private static void ConfigureBackingIcon(Image icon, Color stackColor)
    {
        icon.type = Image.Type.Simple;
        icon.fillAmount = 1f;
        icon.preserveAspect = true;
        icon.color = MutedBackingColor(stackColor);
    }

    private static void ConfigureFillImage(Image fill, Image icon, Color stackColor, float fillAmount)
    {
        fill.gameObject.SetActive(fillAmount > 0f);
        fill.enabled = icon.enabled;
        fill.sprite = icon.overrideSprite != null ? icon.overrideSprite : icon.sprite;
        fill.overrideSprite = icon.overrideSprite;
        fill.material = icon.material;
        fill.preserveAspect = icon.preserveAspect;
        fill.raycastTarget = false;
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.fillOrigin = (int)Image.OriginHorizontal.Left;
        fill.fillClockwise = true;
        fill.fillCenter = true;
        fill.fillAmount = fillAmount;
        fill.color = stackColor;
        fill.transform.SetAsLastSibling();
    }

    private static Color MutedBackingColor(Color stackColor)
    {
        float luminance = stackColor.r * 0.299f + stackColor.g * 0.587f + stackColor.b * 0.114f;
        float gray = Mathf.Clamp(luminance * 0.35f, 0.12f, 0.24f);
        float alpha = Mathf.Clamp(stackColor.a * 0.62f, 0.45f, 0.72f);
        return new Color(gray, gray, gray, alpha);
    }

    private static Image? EnsureFillImage(Image icon)
    {
        Image? existing = FindFillImage(icon);
        if (existing != null)
            return existing;

        GameObject fillObject = new(FillImageName);
        fillObject.AddComponent<RectTransform>();
        fillObject.transform.SetParent(icon.transform, false);

        RectTransform rect = fillObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.anchoredPosition = Vector2.zero;
        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one;

        Image fill = fillObject.AddComponent<Image>();
        fill.raycastTarget = false;
        return fill;
    }

    private static Image? FindFillImage(Image icon)
    {
        Transform? existing = icon.transform.Find(FillImageName);
        return existing == null ? null : existing.GetComponent<Image>();
    }

    private static void HideFillImage(Image icon)
    {
        Image? fill = FindFillImage(icon);
        if (fill != null)
            fill.gameObject.SetActive(false);
    }

    private static void CopyIconBacking(Image source, Image copy)
    {
        copy.enabled = source.enabled;
        copy.color = source.color;
        copy.material = source.material;
        copy.sprite = source.sprite;
        copy.overrideSprite = source.overrideSprite;
        copy.type = source.type;
        copy.preserveAspect = source.preserveAspect;
        copy.fillCenter = source.fillCenter;
        copy.fillMethod = source.fillMethod;
        copy.fillOrigin = source.fillOrigin;
        copy.fillAmount = source.fillAmount;
        copy.fillClockwise = source.fillClockwise;
    }

    private static void CopyFillImage(Image source, Image copy)
    {
        copy.gameObject.SetActive(source.gameObject.activeSelf);
        copy.enabled = source.enabled;
        copy.color = source.color;
        copy.material = source.material;
        copy.sprite = source.sprite;
        copy.overrideSprite = source.overrideSprite;
        copy.type = source.type;
        copy.preserveAspect = source.preserveAspect;
        copy.fillCenter = source.fillCenter;
        copy.fillMethod = source.fillMethod;
        copy.fillOrigin = source.fillOrigin;
        copy.fillAmount = source.fillAmount;
        copy.fillClockwise = source.fillClockwise;
        copy.raycastTarget = false;
    }

    private static void LogSummaryOnce(int updated, int fullStacks, float maxTonnage)
    {
        if (updated <= 0)
            return;

        string summary = $"{updated}:{fullStacks}:{Mathf.RoundToInt(maxTonnage)}";
        if (summary == lastLoggedSummary)
            return;

        lastLoggedSummary = summary;
        Melon<UADVanillaPlusMod>.Logger.Msg(
            $"UADVP task-force tonnage indicators: updated {updated} map markers; " +
            $"{fullStacks} at full fill, max {maxTonnage:0}t, cap {FullStackBattleTonnage:0}t.");
    }

    private static void LogMissingShipGroups(string reason)
    {
        if (loggedMissingShipGroups)
            return;

        loggedMissingShipGroups = true;
        Melon<UADVanillaPlusMod>.Logger.Warning(
            $"UADVP task-force tonnage indicators unavailable because MapUI.shipGroups {reason}.");
    }
}
