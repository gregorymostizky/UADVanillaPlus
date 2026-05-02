using HarmonyLib;
using Il2Cpp;
using Il2CppTMPro;
using MelonLoader;
using UADVanillaPlus.GameData;
using UnityEngine;
using UnityEngine.UI;

namespace UADVanillaPlus.Harmony;

// Patch intent: expose VP balance toggles in-game instead of requiring config
// files. This first pass ports only the menu shell and option state; no gameplay
// feature reads the Port Strike balance toggle yet.
[HarmonyPatch(typeof(Ui))]
internal static class InGameOptionsMenuPatch
{
    private const string ButtonName = "UADVP_OptionsButton";
    private const string MenuName = "UADVP Options";
    private const string PortStrikeOptionName = "UADVP_Option_Port_Strike";

    private static Button? launcherButton;
    private static Image? launcherImage;
    private static Outline? launcherOutline;
    private static GameObject? menu;
    private static bool initialized;
    private static float nextRetryTime;

    [HarmonyPostfix]
    [HarmonyPatch(nameof(Ui.Start))]
    internal static void StartPostfix()
    {
        TrySetup();
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(Ui.Update))]
    internal static void UpdatePostfix()
    {
        if (!initialized && Time.realtimeSinceStartup >= nextRetryTime)
        {
            nextRetryTime = Time.realtimeSinceStartup + 1f;
            TrySetup();
        }

        RefreshLauncherButton();
    }

    private static void TrySetup()
    {
        if (initialized)
            return;

        try
        {
            SetupLauncherButton();
        }
        catch (Exception ex)
        {
            Melon<UADVanillaPlusMod>.Logger.Warning($"UADVP options menu setup skipped. {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void SetupLauncherButton()
    {
        GameObject? options = FindPath("Global/Ui/UiMain/Common/Options");
        GameObject? helpButton = FindPath("Global/Ui/UiMain/Common/Options/Help");
        if (options == null || helpButton == null)
            return;

        GameObject buttonObject = options.transform.Find(ButtonName)?.gameObject ?? UnityEngine.Object.Instantiate(helpButton);
        buttonObject.transform.SetParent(options.transform, false);
        buttonObject.name = ButtonName;
        buttonObject.SetActive(true);
        MatchButtonSizing(buttonObject, helpButton);
        RemoveTooltipHandlers(buttonObject);
        AddLauncherTooltip(buttonObject);

        Transform? imageChild = buttonObject.transform.Find("Image");
        if (imageChild != null && imageChild.TryGetComponent(out Image image))
        {
            launcherImage = image;
            Sprite? sprite = Resources.Load<Sprite>("tabs/tech") ?? Resources.Load<Sprite>("tabs/fleet");
            if (sprite != null)
            {
                launcherImage.sprite = sprite.TryCast<Sprite>();
                launcherImage.preserveAspect = true;
            }

            ScaleLauncherIcon(imageChild);
        }

        launcherOutline = buttonObject.GetComponent<Outline>() ?? buttonObject.AddComponent<Outline>();
        launcherOutline.effectDistance = new Vector2(1f, 1f);

        launcherButton = buttonObject.GetComponent<Button>();
        if (launcherButton != null)
        {
            launcherButton.onClick.RemoveAllListeners();
            launcherButton.onClick.AddListener(new System.Action(OpenMenu));
        }

        initialized = true;
        RefreshLauncherButton();
        Melon<UADVanillaPlusMod>.Logger.Msg("UADVP options menu button added.");
    }

    private static void OpenMenu()
    {
        if (menu != null)
        {
            menu.transform.SetAsLastSibling();
            menu.SetActive(true);
            if (launcherButton != null)
                launcherButton.interactable = false;
            return;
        }

        GameObject? popupTemplate = FindPath("Global/Ui/UiMain/Popup/PopupMenu");
        GameObject? popupRoot = FindPath("Global/Ui/UiMain/Popup");
        if (popupTemplate == null || popupRoot == null)
        {
            Melon<UADVanillaPlusMod>.Logger.Warning("UADVP options menu skipped. Popup template not found.");
            return;
        }

        menu = UnityEngine.Object.Instantiate(popupTemplate);
        menu.transform.SetParent(popupRoot.transform, false);
        menu.name = MenuName;
        menu.transform.localScale = Vector3.one;
        menu.transform.localPosition = Vector3.zero;

        RectTransform? rootRect = menu.GetComponent<RectTransform>();
        if (rootRect != null)
        {
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;
        }

        ConfigureBackdrop(menu);

        GameObject? window = Child(menu, "Window");
        if (window == null)
        {
            UnityEngine.Object.Destroy(menu);
            menu = null;
            return;
        }

        ClearWindowButtons(window);
        AddMenuButton(window, "Port Strike", true, TogglePortStrikeMode);
        AddMenuButton(window, "Close", false, CloseMenu);
        RefreshMenuLabels();

        menu.transform.SetAsLastSibling();
        menu.SetActive(true);
        if (launcherButton != null)
            launcherButton.interactable = false;
    }

    private static void TogglePortStrikeMode()
    {
        ModSettings.PortStrikeBalanced = !ModSettings.PortStrikeBalanced;
        RefreshMenuLabels();
        RefreshLauncherButton();
    }

    private static void RefreshLauncherButton()
    {
        if (launcherButton == null)
            return;

        launcherButton.interactable = menu == null || !menu.activeInHierarchy;

        if (launcherImage != null)
            launcherImage.color = Color.white;

        if (launcherOutline != null)
            launcherOutline.effectColor = ModSettings.PortStrikeBalanced ? Color.white : new Color(0.55f, 0.55f, 0.55f, 1f);
    }

    private static void AddLauncherTooltip(GameObject buttonObject)
    {
        OnEnter onEnter = buttonObject.AddComponent<OnEnter>();
        onEnter.action = new System.Action(() =>
        {
            if (G.ui == null || launcherButton == null || !launcherButton.interactable)
                return;

            G.ui.ShowTooltip($"UAD:VP Options\nPort Strike: {PortStrikeModeText(ModSettings.PortStrikeBalanced)}", buttonObject);
        });

        OnLeave onLeave = buttonObject.AddComponent<OnLeave>();
        onLeave.action = new System.Action(() =>
        {
            try { G.ui?.HideTooltip(); }
            catch { }
        });
    }

    private static void ConfigureBackdrop(GameObject root)
    {
        GameObject? bg = Child(root, "Bg");
        if (bg == null)
            return;

        bg.transform.SetAsFirstSibling();
        RectTransform? bgRect = bg.GetComponent<RectTransform>();
        if (bgRect != null)
        {
            bgRect.anchorMin = Vector2.zero;
            bgRect.anchorMax = Vector2.one;
            bgRect.offsetMin = Vector2.zero;
            bgRect.offsetMax = Vector2.zero;
        }

        Image bgImage = bg.GetComponent<Image>() ?? bg.AddComponent<Image>();
        bgImage.color = new Color(0f, 0f, 0f, 0.6f);
        bgImage.raycastTarget = true;
    }

    private static void MatchButtonSizing(GameObject target, GameObject template)
    {
        target.transform.localScale = template.transform.localScale;

        RectTransform? targetRect = target.GetComponent<RectTransform>();
        RectTransform? templateRect = template.GetComponent<RectTransform>();
        if (targetRect != null && templateRect != null)
        {
            targetRect.anchorMin = templateRect.anchorMin;
            targetRect.anchorMax = templateRect.anchorMax;
            targetRect.pivot = templateRect.pivot;
            targetRect.sizeDelta = templateRect.sizeDelta;
            targetRect.localScale = templateRect.localScale;
        }

        LayoutElement? targetLayout = target.GetComponent<LayoutElement>();
        LayoutElement? templateLayout = template.GetComponent<LayoutElement>();
        if (targetLayout != null && templateLayout != null)
        {
            targetLayout.minWidth = templateLayout.minWidth;
            targetLayout.minHeight = templateLayout.minHeight;
            targetLayout.preferredWidth = templateLayout.preferredWidth;
            targetLayout.preferredHeight = templateLayout.preferredHeight;
            targetLayout.flexibleWidth = templateLayout.flexibleWidth;
            targetLayout.flexibleHeight = templateLayout.flexibleHeight;
        }

        Transform? targetImage = target.transform.Find("Image");
        Transform? templateImage = template.transform.Find("Image");
        if (targetImage == null || templateImage == null)
            return;

        RectTransform? targetImageRect = targetImage.GetComponent<RectTransform>();
        RectTransform? templateImageRect = templateImage.GetComponent<RectTransform>();
        if (targetImageRect == null || templateImageRect == null)
            return;

        targetImageRect.anchorMin = templateImageRect.anchorMin;
        targetImageRect.anchorMax = templateImageRect.anchorMax;
        targetImageRect.pivot = templateImageRect.pivot;
        targetImageRect.sizeDelta = templateImageRect.sizeDelta;
        targetImageRect.localScale = templateImageRect.localScale;
    }

    private static void ScaleLauncherIcon(Transform imageChild)
    {
        RectTransform? rect = imageChild.GetComponent<RectTransform>();
        if (rect != null)
            rect.localScale *= 0.67f;
        else
            imageChild.localScale *= 0.67f;
    }

    private static void ClearWindowButtons(GameObject window)
    {
        for (int i = window.transform.childCount - 1; i >= 0; i--)
        {
            GameObject child = window.transform.GetChild(i).gameObject;
            if (child.GetComponent<Button>() != null)
                UnityEngine.Object.Destroy(child);
        }
    }

    private static void AddMenuButton(GameObject window, string label, bool showState, System.Action onPress)
    {
        GameObject? buttonTemplate = FindPath("Global/Ui/UiMain/Popup/PopupMenu/Window/ButtonBase");
        if (buttonTemplate == null)
            return;

        GameObject buttonObject = UnityEngine.Object.Instantiate(buttonTemplate);
        buttonObject.transform.SetParent(window.transform, false);
        buttonObject.name = showState ? PortStrikeOptionName : "UADVP_Options_Close";
        buttonObject.SetActive(true);
        buttonObject.transform.localPosition = Vector3.zero;
        buttonObject.transform.localScale = Vector3.one;

        Button? button = buttonObject.GetComponent<Button>();
        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(new System.Action(onPress));
        }

        SetMenuButtonText(buttonObject, showState ? OptionLabel(label, ModSettings.PortStrikeBalanced) : label);
    }

    private static void RefreshMenuLabels()
    {
        if (menu == null)
            return;

        GameObject? option = Child(Child(menu, "Window"), PortStrikeOptionName);
        if (option != null)
            SetMenuButtonText(option, OptionLabel("Port Strike", ModSettings.PortStrikeBalanced));
    }

    private static string OptionLabel(string label, bool balanced)
        => $"{label}: {PortStrikeModeText(balanced)}";

    private static string PortStrikeModeText(bool balanced)
        => balanced ? "Balanced" : "Vanilla";

    private static void SetMenuButtonText(GameObject buttonObject, string text)
    {
        TMP_Text? tmp = Child(buttonObject, "Text (TMP)")?.GetComponent<TMP_Text>() ?? buttonObject.GetComponentInChildren<TMP_Text>();
        if (tmp != null)
        {
            RemoveComponent<LocalizeText>(tmp.gameObject);
            tmp.text = text;
            return;
        }

        Text? uiText = buttonObject.GetComponentInChildren<Text>();
        if (uiText != null)
            uiText.text = text;
    }

    private static void CloseMenu()
    {
        if (menu != null)
            menu.SetActive(false);

        if (launcherButton != null)
            launcherButton.interactable = true;
    }

    private static GameObject? FindPath(string path)
    {
        string[] parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return null;

        GameObject? current = GameObject.Find(parts[0]);
        for (int i = 1; current != null && i < parts.Length; i++)
            current = Child(current, parts[i]);

        return current;
    }

    private static GameObject? Child(GameObject? parent, string name)
    {
        Transform? child = parent == null ? null : parent.transform.Find(name);
        return child == null ? null : child.gameObject;
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
}
