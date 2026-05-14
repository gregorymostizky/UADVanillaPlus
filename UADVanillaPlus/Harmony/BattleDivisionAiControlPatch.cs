using System.Collections;
using System.Reflection;
using HarmonyLib;
using Il2Cpp;
using Il2CppTMPro;
using MelonLoader;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UADVanillaPlus.Harmony;

// Patch intent: restore a battle-only division handoff that lets the player
// assign selected friendly divisions back to AI control without changing
// campaign ownership, enemy AI, or hardcoded AI-controlled ship types.
internal static class BattleDivisionAiControlPatch
{
    private const string AiControlButtonName = "UADVP_OrderAiControl";
    private const string AiControlHotkey = "6";
    private const float ButtonSpacing = 4f;

    private static readonly string[] PreferredTemplateButtonNames =
    {
        "Retreat",
        "Sail",
        "Screen",
        "Scout",
        "Follow"
    };

    private static readonly string[] ModeSelectorButtonNames =
    {
        "Sail",
        "Screen",
        "Scout",
        "Follow",
        "Retreat"
    };

    private static readonly MemberInfo? BaseOrdersMember = FindInstanceMember(typeof(Ui), "baseOrders");
    private static readonly MemberInfo? OrdersFloatMember = FindInstanceMember(typeof(Ui), "ordersFloat");
    private static readonly MemberInfo? MoveOrdersMember = FindInstanceMember(typeof(Ui), "moveOrders");
    private static readonly MemberInfo? FormationOrdersMember = FindInstanceMember(typeof(Ui), "formationOrders");
    private static readonly MemberInfo? SpreadOrdersMember = FindInstanceMember(typeof(Ui), "spreadOrders");
    private static readonly MemberInfo? MainShellOrdersMember = FindInstanceMember(typeof(Ui), "mainShellOrders");
    private static readonly MemberInfo? SecShellOrdersMember = FindInstanceMember(typeof(Ui), "secShellOrders");
    private static readonly MemberInfo? ShootOrdersMember = FindInstanceMember(typeof(Ui), "shootOrders");
    private static readonly MemberInfo? AttachOrdersMember = FindInstanceMember(typeof(Ui), "attachOrders");
    private static readonly MemberInfo? SelectedShipsMember = FindInstanceMember(typeof(Ui), "selectedShips");
    private static readonly MemberInfo? IsRightDraggingMember = FindInstanceMember(typeof(Ui), "isRightDragging");
    private static readonly Color ActiveTextColor = new(0.35f, 1f, 0.55f, 1f);
    private static readonly Color InactiveTextColor = Color.white;
    private static readonly Color ActiveGraphicColor = new(0.35f, 0.9f, 0.55f, 0.95f);
    private static readonly HashSet<IntPtr> AiControlledDivisions = new();
    private static readonly Dictionary<IntPtr, Color> OriginalGraphicColors = new();

    private static bool loggedMissingUiFields;
    private static bool loggedButtonAdded;
    private static bool loggedButtonReady;
    private static bool loggedInactiveButtonParent;
    private static bool loggedHotkeyFailed;

    internal static bool IsShipAiControlledByVp(Ship ship)
    {
        if (ship == null || !GameManager.IsBattle)
            return false;

        try
        {
            Division? division = ship.division;
            return division != null &&
                   division.Pointer != IntPtr.Zero &&
                   AiControlledDivisions.Contains(division.Pointer);
        }
        catch
        {
            return false;
        }
    }

    internal static bool IsDivisionAiControlledByVp(Division division)
        => division != null &&
           GameManager.IsBattle &&
           division.Pointer != IntPtr.Zero &&
           AiControlledDivisions.Contains(division.Pointer);

    internal static void Reset(string context)
    {
        if (AiControlledDivisions.Count == 0)
            return;

        int count = AiControlledDivisions.Count;
        AiControlledDivisions.Clear();
        Melon<UADVanillaPlusMod>.Logger.Msg($"UADVP battle AI control: cleared {count} division toggle(s) during {context}.");
    }

    internal static void RefreshAiControlOrder(Ui ui)
    {
        if (ui == null)
            return;

        List<(string SourceName, GameObject Root)> roots = CandidateOrderRoots(ui)
            .GroupBy(root => root.Root.Pointer)
            .Select(group => group.First())
            .ToList();

        if (roots.Count == 0)
        {
            LogMissingUiFields();
            return;
        }

        OrderButtonContext? orderContext = ResolveOrderButtonContext(roots);
        if (orderContext == null)
        {
            HideAiControlButtons(roots);
            return;
        }

        Button? button = EnsureAiControlButton(orderContext, roots);
        if (button == null)
        {
            HideAiControlButtons(roots);
            return;
        }

        List<Division> divisions = SelectedEligibleDivisions(ui);
        bool canToggle = GameManager.IsBattle && divisions.Count > 0;
        PositionAiControlButton(button, orderContext);
        button.gameObject.SetActive(canToggle);
        button.interactable = canToggle;

        bool allAiControlled = canToggle && divisions.All(IsDivisionAiControlled);
        SetButtonVisual(button, allAiControlled, canToggle);

        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(new System.Action(() => ToggleSelectedDivisions(ui, button)));

        if (canToggle && !loggedButtonReady)
        {
            loggedButtonReady = true;
            Transform? parent = button.transform.parent;
            Melon<UADVanillaPlusMod>.Logger.Msg(
                $"UADVP battle AI control: AI button ready for {divisions.Count} selected division(s); root={orderContext.SourceName}/{orderContext.Root.name}, rootActive={orderContext.Root.activeInHierarchy}, parent={parent?.name ?? "<none>"}, parentActive={parent?.gameObject.activeInHierarchy ?? false}, template={orderContext.Template.gameObject.name}, anchor={orderContext.Anchor?.gameObject.name ?? "<none>"}, buttonActive={button.gameObject.activeSelf}.");
        }

        if (canToggle && !loggedInactiveButtonParent && !(button.transform.parent?.gameObject.activeInHierarchy ?? false))
        {
            loggedInactiveButtonParent = true;
            Melon<UADVanillaPlusMod>.Logger.Warning(
                $"UADVP battle AI control: AI button parent is inactive after placement; roots={DescribeOrderRoots(ui)}.");
        }
    }

    internal static void HandleBattleInputPrefix(Ui ui)
    {
        if (ui == null || !GameManager.IsBattle)
            return;

        if (Input.GetMouseButtonUp(1) && !IsPointerOverUi() && !IsRightDragging(ui))
            ClearSelectedAiControl(ui, "manual right-click order");
    }

    internal static void HandleBattleInputPostfix(Ui ui)
    {
        if (ui == null || !GameManager.IsBattle || !CanHandleBattleHotkeys())
            return;

        if (!Input.GetKeyDown(KeyCode.Alpha6) && !Input.GetKeyDown(KeyCode.Keypad6))
            return;

        // Vanilla handles registered mini-hotkeys when the button is visible.
        // This fallback exists for AI-controlled divisions, where vanilla hides
        // the whole order row and the player needs a way back to manual control.
        if (FindVisibleAiControlButton(ui) != null)
            return;

        ToggleSelectedDivisions(ui, button: null, "hotkey 6 fallback");
    }

    private static bool CanHandleBattleHotkeys()
    {
        try
        {
            return GameManager.CanHandleKeyboardInput() && !Util.FocusIsInInputField();
        }
        catch
        {
            return false;
        }
    }

    private static bool IsPointerOverUi()
    {
        try
        {
            EventSystem? eventSystem = EventSystem.current;
            return eventSystem != null && eventSystem.IsPointerOverGameObject();
        }
        catch
        {
            return false;
        }
    }

    private static bool IsRightDragging(Ui ui)
        => GetMemberValue(ui, IsRightDraggingMember) is bool isDragging && isDragging;

    private static void ToggleSelectedDivisions(Ui ui, Button? button, string context = "button")
    {
        List<Division> divisions = SelectedEligibleDivisions(ui);
        if (divisions.Count == 0)
            return;

        bool enable = !divisions.All(IsDivisionAiControlled);
        int shipCount = 0;
        foreach (Division division in divisions)
        {
            if (division == null || division.Pointer == IntPtr.Zero)
                continue;

            if (enable)
                AiControlledDivisions.Add(division.Pointer);
            else
                AiControlledDivisions.Remove(division.Pointer);

            shipCount += SafeShipCount(division);
            division.UIElement?.RefreshUI();
        }

        if (button != null)
            SetButtonVisual(button, enable, true);

        Melon<UADVanillaPlusMod>.Logger.Msg(
            $"UADVP battle AI control: {(enable ? "enabled" : "disabled")} AI control for {divisions.Count} selected division(s), {shipCount} ship(s) via {context}.");
    }

    private static void ClearSelectedAiControl(Ui ui, string context)
    {
        List<Division> divisions = SelectedEligibleDivisions(ui)
            .Where(IsDivisionAiControlled)
            .ToList();
        if (divisions.Count == 0)
            return;

        int shipCount = 0;
        foreach (Division division in divisions)
        {
            if (division == null || division.Pointer == IntPtr.Zero)
                continue;

            AiControlledDivisions.Remove(division.Pointer);
            shipCount += SafeShipCount(division);
            division.UIElement?.RefreshUI();
        }

        Melon<UADVanillaPlusMod>.Logger.Msg(
            $"UADVP battle AI control: disabled AI control for {divisions.Count} selected division(s), {shipCount} ship(s) via {context}.");
    }

    private static List<Division> SelectedEligibleDivisions(Ui ui)
    {
        Dictionary<IntPtr, Division> divisionsByPointer = new();

        foreach (Ship ship in SelectedShips(ui))
            TryAddEligibleDivision(ship, divisionsByPointer);

        try
        {
            Ship? main = ui.GetSelectedShipMain();
            if (main != null)
                TryAddEligibleDivision(main, divisionsByPointer);
        }
        catch
        {
            // Selection can be mid-refresh while vanilla rebuilds the controls.
        }

        return divisionsByPointer.Values.ToList();
    }

    private static IEnumerable<Ship> SelectedShips(Ui ui)
    {
        object? value = GetMemberValue(ui, SelectedShipsMember);
        if (value is not IEnumerable ships)
            yield break;

        foreach (object? item in ships)
        {
            if (item is Ship ship && ship != null)
                yield return ship;
        }
    }

    private static void TryAddEligibleDivision(Ship ship, Dictionary<IntPtr, Division> divisionsByPointer)
    {
        if (!IsEligibleShip(ship))
            return;

        Division? division = ship.division;
        if (division == null || division.Pointer == IntPtr.Zero)
            return;

        divisionsByPointer[division.Pointer] = division;
    }

    private static bool IsEligibleShip(Ship ship)
    {
        if (ship == null || !GameManager.IsBattle)
            return false;

        try
        {
            if (ship.division == null || ship.player == null || PlayerController.Instance == null)
                return false;

            CampaignBattle? battle = G.Battle;
            return battle == null
                ? ship.player.Pointer == PlayerController.Instance.Pointer
                : battle.SameAlliance(PlayerController.Instance, ship.player);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsDivisionAiControlled(Division division)
        => IsDivisionAiControlledByVp(division);

    private static int SafeShipCount(Division division)
    {
        try { return division?.ships?.Count ?? 0; }
        catch { return 0; }
    }

    private sealed class OrderButtonContext
    {
        internal OrderButtonContext(GameObject root, Transform parent, Button template, Button? anchor, string sourceName)
        {
            Root = root;
            Parent = parent;
            Template = template;
            Anchor = anchor;
            SourceName = sourceName;
        }

        internal GameObject Root { get; }
        internal Transform Parent { get; }
        internal Button Template { get; }
        internal Button? Anchor { get; }
        internal string SourceName { get; }
    }

    private static OrderButtonContext? ResolveOrderButtonContext(IReadOnlyList<(string SourceName, GameObject Root)> roots)
    {
        foreach ((string sourceName, GameObject root) in roots)
        {
            if (!root.activeInHierarchy)
                continue;

            OrderButtonContext? context = ResolveModeSelectorButtonContext(root, sourceName);
            if (context != null)
                return context;
        }

        return null;
    }

    private static OrderButtonContext? ResolveModeSelectorButtonContext(GameObject root, string sourceName)
    {
        Button[] buttons = root.GetComponentsInChildren<Button>(true);
        List<Button> modeButtons = buttons
            .Where(button => IsUsableTemplateButton(button, activeOnly: true) && IsModeSelectorButton(button))
            .ToList();

        if (modeButtons.Count == 0)
            return null;

        foreach (IGrouping<int, Button> group in modeButtons
                     .Where(button => button.transform.parent != null)
                     .GroupBy(button => button.transform.parent.gameObject.GetInstanceID())
                     .OrderByDescending(group => group.Count())
                     .ThenByDescending(group => group.Any(ButtonIsPreferredAnchor)))
        {
            Button first = group.First();
            Transform? parent = first.transform.parent;
            if (parent == null || !parent.gameObject.activeInHierarchy)
                continue;

            Button template = PreferredButtonInCluster(group) ?? first;
            Button anchor = PreferredAnchorInCluster(group) ?? template;
            return new OrderButtonContext(root, parent, template, anchor, sourceName);
        }

        return null;
    }

    private static IEnumerable<(string SourceName, GameObject Root)> CandidateOrderRoots(Ui ui)
    {
        foreach ((string sourceName, MemberInfo? member) in OrderRootMembers())
        {
            GameObject? root = GetMemberValue(ui, member) as GameObject;
            if (root != null)
                yield return (sourceName, root);
        }
    }

    private static IEnumerable<(string SourceName, MemberInfo? Member)> OrderRootMembers()
    {
        yield return ("formationOrders", FormationOrdersMember);
        yield return ("attachOrders", AttachOrdersMember);
        yield return ("moveOrders", MoveOrdersMember);
        yield return ("spreadOrders", SpreadOrdersMember);
        yield return ("shootOrders", ShootOrdersMember);
        yield return ("mainShellOrders", MainShellOrdersMember);
        yield return ("secShellOrders", SecShellOrdersMember);
        yield return ("baseOrders", BaseOrdersMember);
        yield return ("ordersFloat", OrdersFloatMember);
    }

    private static Button? EnsureAiControlButton(
        OrderButtonContext orderContext,
        IReadOnlyList<(string SourceName, GameObject Root)> roots)
    {
        if (!orderContext.Root.activeInHierarchy || !orderContext.Parent.gameObject.activeInHierarchy)
            return null;

        List<Button> existingButtons = FindExistingAiControlButtons(roots);
        Button? existing = existingButtons.FirstOrDefault(button => IsDescendantOf(button.transform, orderContext.Root.transform))
            ?? existingButtons.FirstOrDefault();
        RemoveDuplicateAiControlButtons(existingButtons, existing);

        if (existing != null)
        {
            if (existing.transform.parent != orderContext.Parent)
                existing.transform.SetParent(orderContext.Parent, false);

            return existing;
        }

        GameObject buttonObject = UnityEngine.Object.Instantiate(orderContext.Template.gameObject, orderContext.Parent);

        buttonObject.name = AiControlButtonName;
        buttonObject.SetActive(false);
        TryRemoveInheritedPointerHandlers(buttonObject);
        TryRemoveInheritedHotkeyIndicators(buttonObject);

        Button button = buttonObject.GetComponent<Button>() ?? buttonObject.GetComponentInChildren<Button>(true);
        button.onClick.RemoveAllListeners();
        CacheOriginalGraphicColor(button);

        SetButtonText(buttonObject, "AI");
        TryRegisterMiniHotkey(button);

        if (!loggedButtonAdded)
        {
            loggedButtonAdded = true;
            Melon<UADVanillaPlusMod>.Logger.Msg("UADVP battle AI control: added AI control button to battle division orders.");
        }

        return button;
    }

    private static List<Button> FindExistingAiControlButtons(IEnumerable<(string SourceName, GameObject Root)> roots)
    {
        List<Button> buttons = new();
        HashSet<IntPtr> seen = new();

        foreach ((_, GameObject root) in roots)
        {
            if (root == null)
                continue;

            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
            {
                if (transform == null || transform.gameObject.name != AiControlButtonName)
                    continue;

                Button? button = transform.GetComponent<Button>() ?? transform.gameObject.GetComponentInChildren<Button>(true);
                if (button == null || button.Pointer == IntPtr.Zero || !seen.Add(button.Pointer))
                    continue;

                buttons.Add(button);
            }
        }

        return buttons;
    }

    private static void HideAiControlButtons(IReadOnlyList<(string SourceName, GameObject Root)> roots)
    {
        foreach (Button button in FindExistingAiControlButtons(roots))
            button.gameObject.SetActive(false);
    }

    private static void RemoveDuplicateAiControlButtons(List<Button> buttons, Button? keep)
    {
        IntPtr keepPointer = keep?.Pointer ?? IntPtr.Zero;
        foreach (Button button in buttons)
        {
            if (button == null || button.Pointer == keepPointer)
                continue;

            UnityEngine.Object.Destroy(button.gameObject);
        }
    }

    private static Button? FindVisibleAiControlButton(Ui ui)
    {
        foreach (Button button in FindExistingAiControlButtons(CandidateOrderRoots(ui)))
            if (button.gameObject.activeInHierarchy && button.interactable)
                return button;

        return null;
    }

    private static Button? PreferredButtonInCluster(IEnumerable<Button> buttons)
    {
        foreach (string preferredName in PreferredTemplateButtonNames)
        {
            foreach (Button button in buttons)
            {
                if (button != null && ButtonNameMatches(button, preferredName))
                    return button;
            }
        }

        return null;
    }

    private static Button? PreferredAnchorInCluster(IEnumerable<Button> buttons)
    {
        foreach (string preferredName in PreferredTemplateButtonNames)
        {
            foreach (Button button in buttons)
            {
                if (button != null && ButtonNameMatches(button, preferredName))
                    return button;
            }
        }

        return null;
    }

    private static bool IsUsableTemplateButton(Button button, bool activeOnly)
    {
        if (button == null || button.gameObject.name == AiControlButtonName)
            return false;

        return !activeOnly ||
               (button.gameObject.activeInHierarchy &&
                (button.transform.parent?.gameObject.activeInHierarchy ?? false));
    }

    private static bool IsModeSelectorButton(Button button)
    {
        foreach (string modeName in ModeSelectorButtonNames)
            if (ButtonNameMatches(button, modeName))
                return true;

        return false;
    }

    private static bool ButtonIsPreferredAnchor(Button button)
        => button != null && ButtonNameMatches(button, "Retreat");

    private static bool ButtonNameMatches(Button button, string preferredName)
    {
        string name = button.gameObject.name;
        return name.Equals(preferredName, StringComparison.OrdinalIgnoreCase) ||
               name.Contains(preferredName, StringComparison.OrdinalIgnoreCase);
    }

    private static void PositionAiControlButton(Button button, OrderButtonContext orderContext)
    {
        if (button == null)
            return;

        try
        {
            Transform parent = orderContext.Parent;
            if (button.transform.parent != parent)
                button.transform.SetParent(parent, false);

            Button? anchor = orderContext.Anchor;
            if (anchor != null && anchor.transform.parent == parent)
                button.transform.SetSiblingIndex(anchor.transform.GetSiblingIndex() + 1);
            else
                button.transform.SetAsLastSibling();

            GameObject parentObject = parent.gameObject;

            if (parentObject.GetComponent<HorizontalLayoutGroup>() != null ||
                parentObject.GetComponent<VerticalLayoutGroup>() != null ||
                parentObject.GetComponent<GridLayoutGroup>() != null)
            {
                return;
            }

            RectTransform? buttonRect = button.GetComponent<RectTransform>();
            if (buttonRect == null)
                return;

            RectTransform? rightmostRect = null;
            float rightmostEdge = float.NegativeInfinity;
            foreach (Button sibling in parent.GetComponentsInChildren<Button>(true))
            {
                if (sibling == null || sibling.gameObject == button.gameObject || sibling.gameObject.name == AiControlButtonName)
                    continue;

                if (sibling.transform.parent != parent)
                    continue;

                if (!sibling.gameObject.activeInHierarchy || !IsModeSelectorButton(sibling))
                    continue;

                RectTransform? siblingRect = sibling.GetComponent<RectTransform>();
                if (siblingRect == null)
                    continue;

                float siblingWidth = RectWidth(siblingRect);
                float rightEdge = siblingRect.anchoredPosition.x + siblingWidth * (1f - siblingRect.pivot.x);
                if (rightEdge <= rightmostEdge)
                    continue;

                rightmostEdge = rightEdge;
                rightmostRect = siblingRect;
            }

            if (rightmostRect == null)
                return;

            buttonRect.anchorMin = rightmostRect.anchorMin;
            buttonRect.anchorMax = rightmostRect.anchorMax;
            buttonRect.pivot = rightmostRect.pivot;
            if (RectWidth(buttonRect) < 1f)
                buttonRect.sizeDelta = rightmostRect.sizeDelta;

            float buttonWidth = RectWidth(buttonRect);
            buttonRect.anchoredPosition = new Vector2(
                rightmostEdge + ButtonSpacing + buttonWidth * buttonRect.pivot.x,
                rightmostRect.anchoredPosition.y);
        }
        catch (Exception ex)
        {
            Melon<UADVanillaPlusMod>.Logger.Warning(
                $"UADVP battle AI control: could not position AI button; using cloned layout. {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static bool IsDescendantOf(Transform child, Transform ancestor)
    {
        for (Transform? current = child; current != null; current = current.parent)
        {
            if (current == ancestor)
                return true;
        }

        return false;
    }

    private static float RectWidth(RectTransform rect)
    {
        float width = rect.rect.width;
        if (width > 1f)
            return width;

        width = Math.Abs(rect.sizeDelta.x);
        return width > 1f ? width : 32f;
    }

    private static string DescribeOrderRoots(Ui ui)
    {
        try
        {
            return string.Join(", ", CandidateOrderRoots(ui)
                .GroupBy(root => root.Root.Pointer)
                .Select(group => group.First())
                .Select(root => $"{root.SourceName}/{root.Root.name}:active={root.Root.activeInHierarchy}"));
        }
        catch (Exception ex)
        {
            return $"unavailable:{ex.GetType().Name}";
        }
    }

    private static void TryRegisterMiniHotkey(Button button)
    {
        try
        {
            Ui.MiniHotkey(button, AiControlHotkey, false);
        }
        catch (Exception ex)
        {
            if (loggedHotkeyFailed)
                return;

            loggedHotkeyFailed = true;
            Melon<UADVanillaPlusMod>.Logger.Warning(
                $"UADVP battle AI control: could not register AI hotkey {AiControlHotkey}. {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void TryRemoveInheritedHotkeyIndicators(GameObject buttonObject)
    {
        try
        {
            foreach (Transform child in buttonObject.GetComponentsInChildren<Transform>(true).ToArray())
            {
                if (child == null || child.gameObject == buttonObject)
                    continue;

                if (LooksLikeHotkeyIndicator(child.gameObject))
                    UnityEngine.Object.Destroy(child.gameObject);
            }
        }
        catch
        {
        }
    }

    private static bool LooksLikeHotkeyIndicator(GameObject gameObject)
    {
        string name = gameObject.name;
        if (name.Contains("hotkey", StringComparison.OrdinalIgnoreCase))
            return true;

        foreach (Text text in gameObject.GetComponents<Text>())
        {
            if (IsSingleHotkeyText(text?.text))
                return true;
        }

        foreach (TMP_Text text in gameObject.GetComponents<TMP_Text>())
        {
            if (IsSingleHotkeyText(text?.text))
                return true;
        }

        return false;
    }

    private static bool IsSingleHotkeyText(string? text)
        => !string.IsNullOrWhiteSpace(text) &&
           text.Trim().Length == 1 &&
           (char.IsDigit(text.Trim()[0]) || text.Trim()[0] is '-' or '=');

    private static void TryRemoveInheritedPointerHandlers(GameObject buttonObject)
    {
        try { RemoveComponent<OnClickH>(buttonObject); } catch { }
        try { RemoveComponent<OnEnter>(buttonObject); } catch { }
        try { RemoveComponent<OnLeave>(buttonObject); } catch { }
    }

    private static void RemoveComponent<T>(GameObject buttonObject)
        where T : Component
    {
        T? component = buttonObject.GetComponent<T>();
        if (component != null)
            UnityEngine.Object.Destroy(component);
    }

    private static void SetButtonVisual(Button button, bool active, bool canToggle)
    {
        if (button == null)
            return;

        SetButtonText(button.gameObject, "AI");

        Color textColor = canToggle
            ? (active ? ActiveTextColor : InactiveTextColor)
            : new Color(0.55f, 0.55f, 0.55f, 1f);

        foreach (Text text in button.gameObject.GetComponentsInChildren<Text>(true))
            text.color = textColor;

        foreach (TMP_Text text in button.gameObject.GetComponentsInChildren<TMP_Text>(true))
            text.color = textColor;

        if (button.targetGraphic != null)
        {
            CacheOriginalGraphicColor(button);
            Color inactiveColor = OriginalGraphicColors.TryGetValue(button.targetGraphic.Pointer, out Color original)
                ? original
                : button.targetGraphic.color;
            button.targetGraphic.color = active ? ActiveGraphicColor : inactiveColor;
        }
    }

    private static void SetButtonText(GameObject buttonObject, string text)
    {
        Text[] uiTexts = buttonObject.GetComponentsInChildren<Text>(true);
        Text? primaryUiText = uiTexts.FirstOrDefault(t => t != null && t.gameObject.name == "Text") ?? uiTexts.FirstOrDefault();
        if (primaryUiText != null)
            primaryUiText.text = text;

        TMP_Text[] tmpTexts = buttonObject.GetComponentsInChildren<TMP_Text>(true);
        TMP_Text? primaryTmpText = tmpTexts.FirstOrDefault(t => t != null && t.gameObject.name == "Text") ?? tmpTexts.FirstOrDefault();
        if (primaryTmpText != null)
            primaryTmpText.text = text;
    }

    private static void CacheOriginalGraphicColor(Button button)
    {
        if (button?.targetGraphic == null || button.targetGraphic.Pointer == IntPtr.Zero)
            return;

        OriginalGraphicColors.TryAdd(button.targetGraphic.Pointer, button.targetGraphic.color);
    }

    private static void LogMissingUiFields()
    {
        if (loggedMissingUiFields)
            return;

        loggedMissingUiFields = true;
        Melon<UADVanillaPlusMod>.Logger.Warning("UADVP battle AI control: could not read vanilla battle order UI fields; AI control button unavailable.");
    }

    private static MemberInfo? FindInstanceMember(Type type, string name)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        return type.GetField(name, flags) ?? (MemberInfo?)type.GetProperty(name, flags);
    }

    private static object? GetMemberValue(object instance, MemberInfo? member)
    {
        try
        {
            return member switch
            {
                FieldInfo field => field.GetValue(instance),
                PropertyInfo property when property.GetIndexParameters().Length == 0 => property.GetValue(instance),
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }
}

[HarmonyPatch(typeof(Ui), "RefreshShipControls")]
internal static class BattleDivisionAiControlUiPatch
{
    [HarmonyPostfix]
    private static void PostfixRefreshShipControls(Ui __instance)
        => BattleDivisionAiControlPatch.RefreshAiControlOrder(__instance);
}

[HarmonyPatch(typeof(Ui), "UpdateBattle")]
internal static class BattleDivisionAiControlInputPatch
{
    [HarmonyPrefix]
    private static void PrefixUpdateBattle(Ui __instance)
        => BattleDivisionAiControlPatch.HandleBattleInputPrefix(__instance);

    [HarmonyPostfix]
    private static void PostfixUpdateBattle(Ui __instance)
        => BattleDivisionAiControlPatch.HandleBattleInputPostfix(__instance);
}

[HarmonyPatch]
internal static class BattleDivisionAiControlShipPatch
{
    private static MethodBase TargetMethod()
        => AccessTools.PropertyGetter(typeof(Ship), nameof(Ship.isAiControlled))
           ?? AccessTools.Method(typeof(Ship), "get_isAiControlled")
           ?? throw new MissingMethodException(nameof(Ship), "get_isAiControlled");

    [HarmonyPostfix]
    private static void PostfixIsAiControlled(Ship __instance, ref bool __result)
    {
        if (!__result && BattleDivisionAiControlPatch.IsShipAiControlledByVp(__instance))
            __result = true;
    }
}

[HarmonyPatch(typeof(GameManager), nameof(GameManager.OnEnterState))]
internal static class BattleDivisionAiControlEnterStatePatch
{
    [HarmonyPostfix]
    private static void PostfixEnterState(GameManager.GameState state)
    {
        if (state == GameManager.GameState.Battle)
            BattleDivisionAiControlPatch.Reset("battle entry");
    }
}

[HarmonyPatch(typeof(GameManager), nameof(GameManager.OnLeaveState))]
internal static class BattleDivisionAiControlLeaveStatePatch
{
    [HarmonyPostfix]
    private static void PostfixLeaveState(GameManager.GameState state)
    {
        if (state == GameManager.GameState.Battle)
            BattleDivisionAiControlPatch.Reset("battle state exit");
    }
}

[HarmonyPatch(typeof(BattleManager), nameof(BattleManager.LeaveBattle))]
internal static class BattleDivisionAiControlLeaveBattlePatch
{
    [HarmonyPostfix]
    private static void PostfixLeaveBattle()
        => BattleDivisionAiControlPatch.Reset("LeaveBattle");
}
