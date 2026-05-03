using HarmonyLib;
using Il2Cpp;
using Il2CppTMPro;
using MelonLoader;
using UnityEngine;
using UnityEngine.UI;

namespace UADVanillaPlus.Harmony;

// Patch intent: make the common "send this sea task force back where it came
// from" action a single click from the task-force popup.
[HarmonyPatch(typeof(ShipGroupPopupUI), nameof(ShipGroupPopupUI.Init))]
internal static class CampaignTaskForceReturnToPortPatch
{
    private const string ReturnButtonName = "UADVP_ReturnToOriginPort";
    private const float MinimumButtonWidth = 180f;
    private const float MinimumButtonGap = 16f;

    [HarmonyPostfix]
    private static void Postfix(ShipGroupPopupUI __instance, CampaignController.TaskForce group)
    {
        try
        {
            Button? returnButton = EnsureReturnButton(__instance);
            if (returnButton == null)
                return;

            bool canReturn = CanReturnToOrigin(group, out PortElement? origin, out string reason);
            string portName = PortName(origin);

            SetButtonText(returnButton.gameObject, origin == null ? "Return\nto Port" : $"Return to\n{portName}");
            returnButton.interactable = canReturn;
            returnButton.onClick.RemoveAllListeners();
            returnButton.onClick.AddListener(new System.Action(() => ReturnToOrigin(__instance, group)));

            string tooltip = canReturn
                ? $"Order this task force to return to {portName}."
                : reason;
            SetTooltip(returnButton.gameObject, tooltip);
            LayoutButtonRow(__instance, returnButton);
        }
        catch (Exception ex)
        {
            Melon<UADVanillaPlusMod>.Logger.Warning(
                $"UADVP return-to-port button failed while refreshing the task-force popup. {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static Button? EnsureReturnButton(ShipGroupPopupUI popup)
    {
        Button? moveButton = popup?.Move;
        if (moveButton == null)
            return null;

        Transform? parent = moveButton.transform.parent;
        if (parent == null)
            return null;

        Transform? existing = parent.Find(ReturnButtonName);
        if (existing != null)
            return existing.GetComponent<Button>();

        GameObject buttonObject = UnityEngine.Object.Instantiate(moveButton.gameObject);
        buttonObject.name = ReturnButtonName;
        buttonObject.transform.SetParent(parent, false);
        buttonObject.transform.SetSiblingIndex(moveButton.transform.GetSiblingIndex() + 1);

        RemoveTooltipHandlers(buttonObject);
        Button button = buttonObject.GetComponent<Button>() ?? buttonObject.AddComponent<Button>();
        button.onClick.RemoveAllListeners();
        return button;
    }

    private static bool CanReturnToOrigin(CampaignController.TaskForce? group, out PortElement? origin, out string reason)
    {
        origin = group?.OriginPort;

        if (group == null)
        {
            reason = "No task force is selected.";
            return false;
        }

        if (origin == null)
        {
            reason = "This task force does not have an origin port saved.";
            return false;
        }

        if (group.Vessels == null || group.Vessels.Count == 0)
        {
            reason = "This task force has no active vessels to move.";
            return false;
        }

        if (PlayerController.Instance == null || group.Controller != PlayerController.Instance)
        {
            reason = "Only your own task forces can be ordered back to port.";
            return false;
        }

        if (origin.CurrentProvince?.ControllerPlayer != PlayerController.Instance)
        {
            reason = $"{PortName(origin)} is not currently controlled by your nation.";
            return false;
        }

        if (IsAlreadyAtOrigin(group, origin))
        {
            reason = $"This task force is already at {PortName(origin)}.";
            return false;
        }

        if (group.To == origin)
        {
            reason = $"This task force is already ordered to return to {PortName(origin)}.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static bool IsAlreadyAtOrigin(CampaignController.TaskForce group, PortElement origin)
    {
        if (group.Vessels == null || group.Vessels.Count == 0)
            return false;

        foreach (VesselEntity vessel in group.Vessels)
        {
            if (vessel == null || vessel.PortLocation != origin)
                return false;
        }

        return true;
    }

    private static void ReturnToOrigin(ShipGroupPopupUI popup, CampaignController.TaskForce group)
    {
        try
        {
            if (!CanReturnToOrigin(group, out PortElement? origin, out string reason) || origin == null)
            {
                MessageBoxUI.Show("Return to Port", reason);
                return;
            }

            group.RebuildRoute = true;
            bool moved = CampaignShipsMovementManager.MoveGroup(group, Move.Port(origin), MoveSettings.Empty, true);
            if (!moved)
            {
                MessageBoxUI.Show(
                    "Return to Port",
                    $"Could not find a route back to {PortName(origin)}.");
                return;
            }

            ClosePopup(popup);
            group.RebuildRoute = true;
            RefreshMapMovementUi();
            Melon<UADVanillaPlusMod>.Logger.Msg(
                $"UADVP return-to-port: ordered task force {group.Id} back to {PortName(origin)}.");
        }
        catch (Exception ex)
        {
            Melon<UADVanillaPlusMod>.Logger.Warning(
                $"UADVP return-to-port order failed. {ex.GetType().Name}: {ex.Message}");
            MessageBoxUI.Show("Return to Port", "UAD:VP could not order this task force back to port.");
        }
    }

    private static void ClosePopup(ShipGroupPopupUI popup)
    {
        try
        {
            Button? closeButton = popup?.Close;
            if (closeButton != null)
                closeButton.onClick.Invoke();
            else
                popup?.Hide();
        }
        catch (Exception ex)
        {
            Melon<UADVanillaPlusMod>.Logger.Warning(
                $"UADVP return-to-port popup close failed. {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void RefreshMapMovementUi()
    {
        try
        {
            CampaignMap.UpdateTaskForcePositions();
            CampaignMap.UI?.RefreshMovingGroups(false);
        }
        catch (Exception ex)
        {
            Melon<UADVanillaPlusMod>.Logger.Warning(
                $"UADVP return-to-port map refresh failed. {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static string PortName(PortElement? port)
    {
        if (port == null)
            return "Port";

        if (!string.IsNullOrWhiteSpace(port.Name))
            return port.Name;

        return string.IsNullOrWhiteSpace(port.Id) ? "Port" : port.Id;
    }

    private static void LayoutButtonRow(ShipGroupPopupUI popup, Button returnButton)
    {
        Button?[] buttons = { popup.Move, returnButton, popup.Role, popup.Repair, popup.Close };
        List<Button> activeButtons = new();
        foreach (Button? button in buttons)
        {
            if (button != null && button.gameObject.activeSelf)
                activeButtons.Add(button);
        }

        if (activeButtons.Count < 2)
            return;

        Transform? parent = activeButtons[0].transform.parent;
        HorizontalLayoutGroup? horizontalLayout = parent?.GetComponent<HorizontalLayoutGroup>();
        if (horizontalLayout != null)
        {
            float preferredWidth = activeButtons.Count >= 5 ? 230f : 285f;
            horizontalLayout.spacing = Mathf.Min(horizontalLayout.spacing, 24f);
            foreach (Button button in activeButtons)
                SetPreferredButtonWidth(button, preferredWidth);
            return;
        }

        List<ButtonBounds> currentBounds = new();
        foreach (Button button in activeButtons)
        {
            RectTransform? rect = button.GetComponent<RectTransform>();
            if (rect != null)
                currentBounds.Add(new ButtonBounds(button, rect));
        }

        if (currentBounds.Count < 2)
            return;

        float left = currentBounds.Min(x => x.Left);
        float right = currentBounds.Max(x => x.Right);
        float span = right - left;
        float originalGap = EstimateOriginalGap(currentBounds);
        float gap = Mathf.Max(MinimumButtonGap, originalGap * 0.55f);
        float width = (span - (gap * (currentBounds.Count - 1))) / currentBounds.Count;

        if (width < MinimumButtonWidth)
        {
            gap = MinimumButtonGap;
            width = Mathf.Max(MinimumButtonWidth, (span - (gap * (currentBounds.Count - 1))) / currentBounds.Count);
        }

        for (int i = 0; i < activeButtons.Count; i++)
        {
            RectTransform? rect = activeButtons[i].GetComponent<RectTransform>();
            if (rect == null)
                continue;

            Vector2 size = rect.sizeDelta;
            size.x = width;
            rect.sizeDelta = size;

            Vector2 position = rect.anchoredPosition;
            position.x = left + (i * (width + gap)) + (width * rect.pivot.x);
            rect.anchoredPosition = position;
            SetPreferredButtonWidth(activeButtons[i], width);
        }
    }

    private static float EstimateOriginalGap(List<ButtonBounds> bounds)
    {
        if (bounds.Count < 2)
            return 32f;

        bounds.Sort((a, b) => a.Left.CompareTo(b.Left));
        float gapTotal = 0f;
        int gapCount = 0;
        for (int i = 1; i < bounds.Count; i++)
        {
            float gap = bounds[i].Left - bounds[i - 1].Right;
            if (gap > 0f)
            {
                gapTotal += gap;
                gapCount++;
            }
        }

        return gapCount == 0 ? 32f : gapTotal / gapCount;
    }

    private static void SetPreferredButtonWidth(Button button, float width)
    {
        LayoutElement? layout = button.GetComponent<LayoutElement>();
        if (layout == null)
            return;

        layout.preferredWidth = width;
        layout.minWidth = Mathf.Min(layout.minWidth > 0f ? layout.minWidth : width, width);
        layout.flexibleWidth = 0f;
    }

    private static void SetButtonText(GameObject buttonObject, string text)
    {
        TMP_Text? tmp = buttonObject.GetComponentInChildren<TMP_Text>(true);
        if (tmp != null)
        {
            RemoveComponent<LocalizeText>(tmp.gameObject);
            tmp.text = text;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = false;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 12f;
            tmp.fontSizeMax = Mathf.Min(tmp.fontSizeMax > 0f ? tmp.fontSizeMax : tmp.fontSize, 22f);
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            return;
        }

        Text? uiText = buttonObject.GetComponentInChildren<Text>(true);
        if (uiText != null)
        {
            RemoveComponent<LocalizeText>(uiText.gameObject);
            uiText.text = text;
            uiText.alignment = TextAnchor.MiddleCenter;
            uiText.resizeTextForBestFit = true;
            uiText.resizeTextMinSize = 12;
            uiText.resizeTextMaxSize = Math.Min(uiText.fontSize, 22);
        }
    }

    private static void SetTooltip(GameObject target, string text)
    {
        RemoveTooltipHandlers(target);

        OnEnter onEnter = target.AddComponent<OnEnter>();
        onEnter.action = new System.Action(() =>
        {
            if (!string.IsNullOrWhiteSpace(text))
                G.ui.ShowTooltip(text, target);
        });

        OnLeave onLeave = target.AddComponent<OnLeave>();
        onLeave.action = new System.Action(() => G.ui.HideTooltip());
    }

    private static void RemoveTooltipHandlers(GameObject target)
    {
        RemoveComponent<OnEnter>(target);
        RemoveComponent<OnLeave>(target);
    }

    private static void RemoveComponent<T>(GameObject target) where T : Component
    {
        T? component = target.GetComponent<T>();
        if (component != null)
            UnityEngine.Object.Destroy(component);
    }

    private readonly struct ButtonBounds
    {
        internal readonly Button Button;
        internal readonly float Left;
        internal readonly float Right;

        internal ButtonBounds(Button button, RectTransform rect)
        {
            Button = button;
            Left = rect.anchoredPosition.x - (rect.sizeDelta.x * rect.pivot.x);
            Right = rect.anchoredPosition.x + (rect.sizeDelta.x * (1f - rect.pivot.x));
        }
    }
}
