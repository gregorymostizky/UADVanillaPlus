using HarmonyLib;
using Il2Cpp;
using Il2CppTMPro;
using MelonLoader;
using UADVanillaPlus.GameData;
using UnityEngine;
using UnityEngine.UI;

namespace UADVanillaPlus.Harmony;

// Patch intent: expose VP balance controls in a vanilla-like settings panel.
// Options are grouped by gameplay area so future toggles and multi-choice
// controls can be added without turning the menu into a flat debug list.
[HarmonyPatch(typeof(Ui))]
internal static class InGameOptionsMenuPatch
{
    private enum Section
    {
        Battle,
        Campaign,
        ShipDesign,
        Experimental,
    }

    private const string ButtonName = "UADVP_OptionsButton";
    private const string MenuName = "UADVP Options";
    private const string ContentName = "UADVP_OptionsContent";
    private const string BattleWeatherOptionName = "UADVP_Option_BattleWeather";
    private const string DesignAccuracyPenaltiesOptionName = "UADVP_Option_DesignAccuracyPenalties";
    private const string PortStrikeOptionName = "UADVP_Option_PortStrike";
    private const string MajorShipTorpedoesOptionName = "UADVP_Option_MajorShipTorpedoes";
    private const string ObsoleteDesignRetentionOptionName = "UADVP_Option_ObsoleteDesignRetention";
    private const string ShipyardCapacityOptionName = "UADVP_Option_ShipyardCapacity";
    private const string CampaignMapWraparoundOptionName = "UADVP_Option_CampaignMapWraparound";
    private const string CanalOpeningsOptionName = "UADVP_Option_CanalOpenings";
    private const string TechnologySpreadOptionName = "UADVP_Option_TechnologySpread";
    private const string MineWarfareOptionName = "UADVP_Option_MineWarfare";
    private const string SubmarineWarfareOptionName = "UADVP_Option_SubmarineWarfare";

    private static readonly Color Background = new(0f, 0f, 0f, 0.94f);
    private static readonly Color RowBackground = new(0.09f, 0.09f, 0.09f, 0.96f);
    private static readonly Color SelectedGold = new(0.58f, 0.44f, 0.2f, 0.95f);
    private static readonly Color SegmentIdle = new(0.28f, 0.27f, 0.2f, 0.9f);
    private static readonly Color SegmentDisabled = new(0.12f, 0.12f, 0.1f, 0.82f);
    private static readonly System.Reflection.MethodInfo? RefreshFinancesWindow = AccessTools.Method(typeof(CampaignFinancesWindow), "Refresh");
    private static readonly System.Reflection.MethodInfo? RefreshConstructorParts = AccessTools.Method(typeof(Ui), "RefreshParts");

    private static Button? launcherButton;
    private static Image? launcherImage;
    private static Outline? launcherOutline;
    private static GameObject? menu;
    private static GameObject? contentRoot;
    private static Section selectedSection = Section.Battle;
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
            RefreshMenu();
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

        BuildSettingsWindow(window);
        menu.transform.SetAsLastSibling();
        menu.SetActive(true);
        if (launcherButton != null)
            launcherButton.interactable = false;
    }

    private static void BuildSettingsWindow(GameObject window)
    {
        ClearChildren(window);
        ConfigureWindow(window);

        contentRoot = new GameObject(ContentName);
        contentRoot.transform.SetParent(window.transform, false);
        RectTransform contentRect = contentRoot.AddComponent<RectTransform>();
        contentRect.anchorMin = Vector2.zero;
        contentRect.anchorMax = Vector2.one;
        contentRect.offsetMin = new Vector2(32f, 28f);
        contentRect.offsetMax = new Vector2(-32f, -28f);

        VerticalLayoutGroup contentLayout = contentRoot.AddComponent<VerticalLayoutGroup>();
        contentLayout.spacing = 18f;
        contentLayout.childControlHeight = true;
        contentLayout.childControlWidth = true;
        contentLayout.childForceExpandHeight = false;
        contentLayout.childForceExpandWidth = true;

        Text title = AddText(contentRoot.transform, "UAD:VP Options", 18, TextAnchor.MiddleLeft);
        title.name = "UADVP_OptionsTitle";
        AddLayout(title.gameObject, minHeight: 24f, preferredHeight: 24f, flexibleWidth: 1f);

        GameObject body = new("UADVP_OptionsBody");
        body.transform.SetParent(contentRoot.transform, false);
        HorizontalLayoutGroup bodyLayout = body.AddComponent<HorizontalLayoutGroup>();
        bodyLayout.spacing = 12f;
        bodyLayout.childControlHeight = true;
        bodyLayout.childControlWidth = true;
        bodyLayout.childForceExpandHeight = true;
        bodyLayout.childForceExpandWidth = true;
        AddLayout(body, minHeight: 205f, flexibleHeight: 1f, flexibleWidth: 1f);

        BuildSectionList(body.transform);
        BuildSectionPane(body.transform);

        GameObject footer = new("UADVP_OptionsFooter");
        footer.transform.SetParent(contentRoot.transform, false);
        HorizontalLayoutGroup footerLayout = footer.AddComponent<HorizontalLayoutGroup>();
        footerLayout.childAlignment = TextAnchor.MiddleRight;
        footerLayout.childControlWidth = false;
        footerLayout.childControlHeight = true;
        footerLayout.childForceExpandWidth = true;
        footerLayout.spacing = 10f;
        AddLayout(footer, minHeight: 26f, flexibleWidth: 1f);
        AddActionButton(footer.transform, "Close", CloseMenu, width: 105f);
    }

    private static void BuildSectionList(Transform parent)
    {
        GameObject sections = new("UADVP_OptionsSections");
        sections.transform.SetParent(parent, false);
        VerticalLayoutGroup layout = sections.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 4f;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;
        AddLayout(sections, minWidth: 112f, preferredWidth: 112f, flexibleHeight: 1f);

        AddSectionButton(sections.transform, Section.Battle, "Battle");
        AddSectionButton(sections.transform, Section.Campaign, "Campaign");
        AddSectionButton(sections.transform, Section.ShipDesign, "Ship Design");
        AddSectionButton(sections.transform, Section.Experimental, "Experimental");
    }

    private static void BuildSectionPane(Transform parent)
    {
        GameObject pane = new("UADVP_OptionsPane");
        pane.transform.SetParent(parent, false);
        Image paneImage = pane.AddComponent<Image>();
        paneImage.color = new Color(0f, 0f, 0f, 0.12f);
        VerticalLayoutGroup layout = pane.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 6f;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;
        AddLayout(pane, flexibleWidth: 1f, flexibleHeight: 1f);

        AddText(pane.transform, SectionTitle(selectedSection), 15, TextAnchor.MiddleLeft);

        switch (selectedSection)
        {
            case Section.Battle:
                AddSegmentedOption(
                    pane.transform,
                    BattleWeatherOptionName,
                    "Battle Weather",
                    "Always Sunny forces battles to start in daytime fair weather. Vanilla keeps the game's random battle time and weather rolls.",
                    true,
                    ("Always Sunny", ModSettings.BattleWeatherAlwaysSunny, () => SetBattleWeatherMode(true)),
                    ("Vanilla", !ModSettings.BattleWeatherAlwaysSunny, () => SetBattleWeatherMode(false)));
                AddSegmentedOption(
                    pane.transform,
                    DesignAccuracyPenaltiesOptionName,
                    "Accuracy Penalties",
                    "Reduces extreme smoke, stability, and instability accuracy penalties from ship design. This helps prevent otherwise decent ships from becoming unrealistically unable to hit. The active effect remains in battles, but changing this option is disabled there because the game has already parsed battle stats.",
                    CanChangeAccuracyPenalties(),
                    ("/10", ModSettings.DesignAccuracyPenaltyMode == ModSettings.AccuracyPenaltyMode.Div10, () => SetDesignAccuracyPenaltiesMode(ModSettings.AccuracyPenaltyMode.Div10)),
                    ("/5", ModSettings.DesignAccuracyPenaltyMode == ModSettings.AccuracyPenaltyMode.Div5, () => SetDesignAccuracyPenaltiesMode(ModSettings.AccuracyPenaltyMode.Div5)),
                    ("/2", ModSettings.DesignAccuracyPenaltyMode == ModSettings.AccuracyPenaltyMode.Div2, () => SetDesignAccuracyPenaltiesMode(ModSettings.AccuracyPenaltyMode.Div2)),
                    ("Vanilla", ModSettings.DesignAccuracyPenaltyMode == ModSettings.AccuracyPenaltyMode.Vanilla, () => SetDesignAccuracyPenaltiesMode(ModSettings.AccuracyPenaltyMode.Vanilla)));
                break;
            case Section.Campaign:
                AddSegmentedOption(
                    pane.transform,
                    PortStrikeOptionName,
                    "Port Strike",
                    "Balanced scales port strike transport losses to the attacking force instead of letting tiny raids destroy excessive transport capacity.",
                    true,
                    ("Balanced", ModSettings.PortStrikeBalanced, () => SetPortStrikeMode(true)),
                    ("Vanilla", !ModSettings.PortStrikeBalanced, () => SetPortStrikeMode(false)));
                AddSegmentedOption(
                    pane.transform,
                    ShipyardCapacityOptionName,
                    "Suspend Dock Overcapacity",
                    "Automatic temporarily suspends lower-priority repairs, builds, and refits during the monthly advance when dock work exceeds shipyard capacity. Manual keeps vanilla behavior, where players must manage overcapacity themselves and the game applies its global over-capacity time penalty.",
                    true,
                    ("Automatic", ModSettings.ShipyardCapacityBalanced, () => SetShipyardCapacityMode(true)),
                    ("Manual", !ModSettings.ShipyardCapacityBalanced, () => SetShipyardCapacityMode(false)));
                AddSegmentedOption(
                    pane.transform,
                    CanalOpeningsOptionName,
                    "Canal Openings",
                    "Early opens the Panama and Kiel canals from 1890 when a campaign map loads, like Suez and the other early canals. Historical keeps vanilla's 1914 and 1895 opening years.",
                    true,
                    ("Early", ModSettings.EarlyCanalOpeningsEnabled, () => SetCanalOpeningsMode(true)),
                    ("Historical", !ModSettings.EarlyCanalOpeningsEnabled, () => SetCanalOpeningsMode(false)));
                AddSegmentedOption(
                    pane.transform,
                    TechnologySpreadOptionName,
                    "Technology Spread",
                    "Gradual, Swift, and Unrestricted multiply vanilla research speed for major nations that trail the current leader in a category. Economy, tech budget, priorities, and historical-year gates still apply.",
                    true,
                    ("Vanilla", ModSettings.TechnologySpread == ModSettings.TechnologySpreadMode.Vanilla, () => SetTechnologySpreadMode(ModSettings.TechnologySpreadMode.Vanilla)),
                    ("Gradual", ModSettings.TechnologySpread == ModSettings.TechnologySpreadMode.Gradual, () => SetTechnologySpreadMode(ModSettings.TechnologySpreadMode.Gradual)),
                    ("Swift", ModSettings.TechnologySpread == ModSettings.TechnologySpreadMode.Swift, () => SetTechnologySpreadMode(ModSettings.TechnologySpreadMode.Swift)),
                    ("Unrestricted", ModSettings.TechnologySpread == ModSettings.TechnologySpreadMode.Unrestricted, () => SetTechnologySpreadMode(ModSettings.TechnologySpreadMode.Unrestricted)));
                AddSegmentedOption(
                    pane.transform,
                    MineWarfareOptionName,
                    "Mine Warfare",
                    "Disabled prevents minefield damage and hides mine and minesweeping equipment from the ship designer. Enabled keeps the game's normal minefields and mine equipment.",
                    true,
                    ("Disabled", ModSettings.MineWarfareDisabled, () => SetMineWarfareMode(true)),
                    ("Enabled", !ModSettings.MineWarfareDisabled, () => SetMineWarfareMode(false)));
                AddSegmentedOption(
                    pane.transform,
                    SubmarineWarfareOptionName,
                    "Submarine Warfare",
                    "Disabled prevents submarine construction and submarine campaign battles while leaving existing submarines in saved campaigns untouched. Enabled keeps the game's normal submarine warfare.",
                    true,
                    ("Disabled", ModSettings.SubmarineWarfareDisabled, () => SetSubmarineWarfareMode(true)),
                    ("Enabled", !ModSettings.SubmarineWarfareDisabled, () => SetSubmarineWarfareMode(false)));
                break;
            case Section.ShipDesign:
                AddSegmentedOption(
                    pane.transform,
                    MajorShipTorpedoesOptionName,
                    "CA+ Torpedoes",
                    "Disallowed prevents heavy cruisers and larger ships from mounting torpedoes. This nudges designs toward more plausible fleet roles and avoids oversized torpedo platforms.",
                    true,
                    ("Disallowed", ModSettings.MajorShipTorpedoesRestricted, () => SetMajorShipTorpedoesMode(true)),
                    ("Vanilla", !ModSettings.MajorShipTorpedoesRestricted, () => SetMajorShipTorpedoesMode(false)));
                AddSegmentedOption(
                    pane.transform,
                    ObsoleteDesignRetentionOptionName,
                    "Obsolete Tech & Hulls",
                    "Retain keeps already researched obsolete hulls and components available for player ship designs. Vanilla hides older options as newer equivalents become available. AI design availability remains vanilla.",
                    true,
                    ("Retain", ModSettings.ObsoleteDesignRetentionEnabled, () => SetObsoleteDesignRetentionMode(true)),
                    ("Vanilla", !ModSettings.ObsoleteDesignRetentionEnabled, () => SetObsoleteDesignRetentionMode(false)));
                break;
            case Section.Experimental:
                AddSegmentedOption(
                    pane.transform,
                    CampaignMapWraparoundOptionName,
                    "Map Geometry",
                    "Disc World enables the experimental campaign-map wrap illusion at the Pacific edge: neighboring map copies, wider horizontal panning, and wrapped marker and movement interactions. Flat Earth keeps vanilla one-map geometry and bounds.",
                    true,
                    ("Disc World", ModSettings.CampaignMapWraparoundEnabled, () => SetCampaignMapWraparoundMode(true)),
                    ("Flat Earth", !ModSettings.CampaignMapWraparoundEnabled, () => SetCampaignMapWraparoundMode(false)));
                break;
        }
    }

    private static void AddSegmentedOption(Transform parent, string name, string label, string tooltip, bool interactable, params (string Label, bool Selected, Action OnPress)[] segments)
    {
        GameObject row = new(name);
        row.transform.SetParent(parent, false);
        Image rowImage = row.AddComponent<Image>();
        rowImage.color = RowBackground;
        AddTooltip(row, $"{label}\n{tooltip}");
        HorizontalLayoutGroup rowLayout = row.AddComponent<HorizontalLayoutGroup>();
        rowLayout.padding = new RectOffset
        {
            left = 8,
            right = 8,
            top = 4,
            bottom = 4,
        };
        rowLayout.spacing = 8f;
        rowLayout.childAlignment = TextAnchor.MiddleCenter;
        rowLayout.childControlHeight = true;
        rowLayout.childControlWidth = true;
        rowLayout.childForceExpandHeight = false;
        rowLayout.childForceExpandWidth = false;
        AddLayout(row, minHeight: 34f, preferredHeight: 34f, flexibleWidth: 1f);

        Text labelText = AddText(row.transform, label, 13, TextAnchor.MiddleLeft);
        AddLayout(labelText.gameObject, minWidth: 155f, flexibleWidth: 1f);

        foreach (var segment in segments)
            AddSegmentButton(row.transform, segment.Label, segment.Selected, segment.OnPress, segments.Length > 2 ? 92f : 112f, interactable, $"{label}: {segment.Label}\n{tooltip}");
    }

    private static void AddSectionButton(Transform parent, Section section, string label)
    {
        Button button = AddActionButton(parent, label, () => SelectSection(section), width: 102f);
        Image image = button.GetComponent<Image>() ?? button.gameObject.AddComponent<Image>();
        image.color = selectedSection == section ? SelectedGold : SegmentIdle;
    }

    private static void AddSegmentButton(Transform parent, string label, bool selected, Action onPress, float width, bool interactable, string tooltip)
    {
        Button button = AddActionButton(parent, label, onPress, width);
        button.interactable = interactable;
        AddTooltip(button.gameObject, tooltip);
        Image image = button.GetComponent<Image>() ?? button.gameObject.AddComponent<Image>();
        image.color = selected && interactable ? SelectedGold : SegmentDisabled;
    }

    private static Button AddActionButton(Transform parent, string label, Action onPress, float width)
    {
        GameObject? buttonTemplate = FindPath("Global/Ui/UiMain/Popup/PopupMenu/Window/ButtonBase");
        GameObject buttonObject = buttonTemplate != null ? UnityEngine.Object.Instantiate(buttonTemplate) : new GameObject($"UADVP_Button_{label}");
        buttonObject.transform.SetParent(parent, false);
        buttonObject.name = $"UADVP_Button_{label.Replace(" ", string.Empty)}";
        buttonObject.SetActive(true);
        buttonObject.transform.localPosition = Vector3.zero;
        buttonObject.transform.localScale = Vector3.one;
        // The vanilla popup button prefab carries tall menu geometry; clamp both
        // layout and rect height so compact option rows do not balloon vertically.
        AddLayout(buttonObject, minWidth: width, preferredWidth: width, minHeight: 26f, preferredHeight: 26f, flexibleHeight: 0f);
        RectTransform? buttonRect = buttonObject.GetComponent<RectTransform>();
        if (buttonRect != null)
            buttonRect.sizeDelta = new Vector2(buttonRect.sizeDelta.x, 26f);

        Button button = buttonObject.GetComponent<Button>() ?? buttonObject.AddComponent<Button>();
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(new System.Action(onPress));

        Image image = buttonObject.GetComponent<Image>() ?? buttonObject.AddComponent<Image>();
        button.targetGraphic = image;
        SetMenuButtonText(buttonObject, label);
        return button;
    }

    private static Text AddText(Transform parent, string text, int fontSize, TextAnchor alignment)
    {
        GameObject textObject = new($"UADVP_Text_{text.Replace(" ", string.Empty).Replace(":", string.Empty)}");
        textObject.transform.SetParent(parent, false);
        Text uiText = textObject.AddComponent<Text>();
        uiText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        uiText.fontSize = fontSize;
        uiText.color = Color.white;
        uiText.alignment = alignment;
        uiText.text = text;
        uiText.horizontalOverflow = HorizontalWrapMode.Overflow;
        uiText.verticalOverflow = VerticalWrapMode.Overflow;
        AddLayout(textObject, minHeight: fontSize + 6f, preferredHeight: fontSize + 6f, flexibleWidth: 1f);
        return uiText;
    }

    private static void SelectSection(Section section)
    {
        selectedSection = section;
        RefreshMenu();
    }

    private static void SetBattleWeatherMode(bool alwaysSunny)
    {
        if (ModSettings.BattleWeatherAlwaysSunny != alwaysSunny)
            ModSettings.BattleWeatherAlwaysSunny = alwaysSunny;

        RefreshMenu();
        RefreshLauncherButton();
    }

    private static void SetDesignAccuracyPenaltiesMode(ModSettings.AccuracyPenaltyMode mode)
    {
        if (!CanChangeAccuracyPenalties())
        {
            Melon<UADVanillaPlusMod>.Logger.Warning("UADVP option: Accuracy Penalties cannot be changed while a battle is loading or active.");
            RefreshMenu();
            RefreshLauncherButton();
            return;
        }

        if (ModSettings.DesignAccuracyPenaltyMode != mode)
            ModSettings.DesignAccuracyPenaltyMode = mode;

        RefreshMenu();
        RefreshLauncherButton();
    }

    private static bool CanChangeAccuracyPenalties()
        => !AccuracyPenaltyBalance.IsBattleOrLoading();

    private static void SetPortStrikeMode(bool balanced)
    {
        if (ModSettings.PortStrikeBalanced != balanced)
            ModSettings.PortStrikeBalanced = balanced;

        RefreshMenu();
        RefreshLauncherButton();
    }

    private static void SetMajorShipTorpedoesMode(bool restricted)
    {
        if (ModSettings.MajorShipTorpedoesRestricted != restricted)
            ModSettings.MajorShipTorpedoesRestricted = restricted;

        RefreshMenu();
        RefreshLauncherButton();
    }

    private static void SetObsoleteDesignRetentionMode(bool enabled)
    {
        if (ModSettings.ObsoleteDesignRetentionEnabled != enabled)
        {
            ModSettings.ObsoleteDesignRetentionEnabled = enabled;
            RefreshConstructorAvailabilityUi();
        }

        RefreshMenu();
        RefreshLauncherButton();
    }

    private static void SetShipyardCapacityMode(bool balanced)
    {
        if (ModSettings.ShipyardCapacityBalanced != balanced)
        {
            ModSettings.ShipyardCapacityBalanced = balanced;
            RefreshCampaignCostUi();
        }

        RefreshMenu();
        RefreshLauncherButton();
    }

    private static void SetCampaignMapWraparoundMode(bool enabled)
    {
        if (ModSettings.CampaignMapWraparoundEnabled != enabled)
        {
            ModSettings.CampaignMapWraparoundEnabled = enabled;
            CampaignMapWrapVisualPatch.ApplyCurrentSetting();
        }

        RefreshMenu();
        RefreshLauncherButton();
    }

    private static void SetCanalOpeningsMode(bool early)
    {
        if (ModSettings.EarlyCanalOpeningsEnabled != early)
        {
            ModSettings.EarlyCanalOpeningsEnabled = early;
            CampaignCanalOpeningPatch.ApplyCurrentSetting();
        }

        RefreshMenu();
        RefreshLauncherButton();
    }

    private static void SetTechnologySpreadMode(ModSettings.TechnologySpreadMode mode)
    {
        if (ModSettings.TechnologySpread != mode)
            ModSettings.TechnologySpread = mode;

        RefreshMenu();
        RefreshLauncherButton();
    }

    private static void SetMineWarfareMode(bool disabled)
    {
        if (ModSettings.MineWarfareDisabled != disabled)
        {
            ModSettings.MineWarfareDisabled = disabled;
            RefreshConstructorAvailabilityUi("Mine Warfare");
        }

        RefreshMenu();
        RefreshLauncherButton();
    }

    private static void SetSubmarineWarfareMode(bool disabled)
    {
        if (ModSettings.SubmarineWarfareDisabled != disabled)
        {
            ModSettings.SubmarineWarfareDisabled = disabled;
            RefreshSubmarineWarfareUi();
        }

        RefreshMenu();
        RefreshLauncherButton();
    }

    private static void RefreshConstructorAvailabilityUi()
        => RefreshConstructorAvailabilityUi("Obsolete Tech & Hulls");

    private static void RefreshConstructorAvailabilityUi(string optionName)
    {
        try
        {
            if (!GameManager.IsConstructor)
            {
                Melon<UADVanillaPlusMod>.Logger.Msg(
                    $"UADVP option: stored {optionName} mode change; constructor UI is not active.");
                return;
            }

            Ui? ui = G.ui;
            if (ui == null || PlayerController.Instance == null)
                return;

            try { ui.RefreshConstructorInfo(); }
            catch (Exception ex) { Melon<UADVanillaPlusMod>.Logger.Warning($"UADVP option: constructor info refresh failed. {ex.GetType().Name}: {ex.Message}"); }

            try { RefreshConstructorParts?.Invoke(ui, Array.Empty<object>()); }
            catch (Exception ex) { Melon<UADVanillaPlusMod>.Logger.Warning($"UADVP option: constructor parts refresh failed. {ex.GetType().Name}: {ex.Message}"); }

            Melon<UADVanillaPlusMod>.Logger.Msg($"UADVP option: refreshed constructor availability UI after {optionName} mode change.");
        }
        catch (Exception ex)
        {
            Melon<UADVanillaPlusMod>.Logger.Warning(
                $"UADVP option: constructor availability refresh skipped. {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void RefreshSubmarineWarfareUi()
    {
        try
        {
            Ui? ui = G.ui;
            if (ui == null || PlayerController.Instance == null || CampaignController.Instance?.CampaignData == null)
                return;

            try { ui.CountryInfo?.Refresh(); }
            catch (Exception ex) { Melon<UADVanillaPlusMod>.Logger.Warning($"UADVP option: submarine warfare country-info refresh failed. {ex.GetType().Name}: {ex.Message}"); }

            try { ui.SubmarineWindow?.Refresh(); }
            catch (Exception ex) { Melon<UADVanillaPlusMod>.Logger.Warning($"UADVP option: submarine warfare window refresh failed. {ex.GetType().Name}: {ex.Message}"); }

            Melon<UADVanillaPlusMod>.Logger.Msg("UADVP option: refreshed submarine warfare UI after mode change.");
        }
        catch (Exception ex)
        {
            Melon<UADVanillaPlusMod>.Logger.Warning(
                $"UADVP option: submarine warfare UI refresh skipped. {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void RefreshCampaignCostUi()
    {
        try
        {
            Ui? ui = G.ui;
            if (ui == null || PlayerController.Instance == null || CampaignController.Instance?.CampaignData == null)
                return;

            try { ui.CountryInfo?.Refresh(); }
            catch (Exception ex) { Melon<UADVanillaPlusMod>.Logger.Warning($"UADVP option: country-info cost refresh failed. {ex.GetType().Name}: {ex.Message}"); }

            try
            {
                if (ui.FinancesWindow != null)
                    RefreshFinancesWindow?.Invoke(ui.FinancesWindow, Array.Empty<object>());
            }
            catch (Exception ex) { Melon<UADVanillaPlusMod>.Logger.Warning($"UADVP option: finances cost refresh failed. {ex.GetType().Name}: {ex.Message}"); }

            try { ui.FleetWindow?.Refresh(false); }
            catch (Exception ex) { Melon<UADVanillaPlusMod>.Logger.Warning($"UADVP option: fleet cost refresh failed. {ex.GetType().Name}: {ex.Message}"); }

            try { ui.SubmarineWindow?.Refresh(); }
            catch (Exception ex) { Melon<UADVanillaPlusMod>.Logger.Warning($"UADVP option: submarine cost refresh failed. {ex.GetType().Name}: {ex.Message}"); }

            try { ui.RefreshCampaignUI(); }
            catch (Exception ex) { Melon<UADVanillaPlusMod>.Logger.Warning($"UADVP option: campaign UI refresh failed. {ex.GetType().Name}: {ex.Message}"); }

            Melon<UADVanillaPlusMod>.Logger.Msg("UADVP option: refreshed campaign cost UI after Suspend Dock Overcapacity mode change.");
        }
        catch (Exception ex)
        {
            Melon<UADVanillaPlusMod>.Logger.Warning(
                $"UADVP option: campaign cost UI refresh skipped. {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void RefreshMenu()
    {
        if (contentRoot == null)
            return;

        GameObject? window = Child(menu, "Window");
        if (window == null)
            return;

        BuildSettingsWindow(window);
    }

    private static void RefreshLauncherButton()
    {
        if (launcherButton == null)
            return;

        launcherButton.interactable = menu == null || !menu.activeInHierarchy;

        if (launcherImage != null)
            launcherImage.color = Color.white;

        if (launcherOutline != null)
            launcherOutline.effectColor = AnyBalanceOptionEnabled() ? Color.white : new Color(0.55f, 0.55f, 0.55f, 1f);
    }

    private static bool AnyBalanceOptionEnabled()
        => ModSettings.BattleWeatherAlwaysSunny || ModSettings.DesignAccuracyPenaltiesBalanced || ModSettings.PortStrikeBalanced || ModSettings.MajorShipTorpedoesRestricted || ModSettings.ObsoleteDesignRetentionEnabled || ModSettings.ShipyardCapacityBalanced || ModSettings.EarlyCanalOpeningsEnabled || ModSettings.TechnologySpread != ModSettings.TechnologySpreadMode.Vanilla || ModSettings.MineWarfareDisabled || ModSettings.SubmarineWarfareDisabled || ModSettings.CampaignMapWraparoundEnabled;

    private static void AddLauncherTooltip(GameObject buttonObject)
        => AddTooltip(
            buttonObject,
            $"UAD:VP Options\nBattle Weather: {BattleWeatherModeText(ModSettings.BattleWeatherAlwaysSunny)}\nAccuracy Penalties: {DesignAccuracyPenaltiesModeText(ModSettings.DesignAccuracyPenaltyMode)}\nPort Strike: {PortStrikeModeText(ModSettings.PortStrikeBalanced)}\nSuspend Dock Overcapacity: {ShipyardCapacityModeText(ModSettings.ShipyardCapacityBalanced)}\nCanal Openings: {CanalOpeningModeText(ModSettings.EarlyCanalOpeningsEnabled)}\nTechnology Spread: {TechnologySpreadModeText(ModSettings.TechnologySpread)}\nMine Warfare: {MineWarfareModeText(ModSettings.MineWarfareDisabled)}\nSubmarine Warfare: {SubmarineWarfareModeText(ModSettings.SubmarineWarfareDisabled)}\nCA+ Torpedoes: {MajorShipTorpedoesModeText(ModSettings.MajorShipTorpedoesRestricted)}\nObsolete Tech & Hulls: {ObsoleteDesignRetentionModeText(ModSettings.ObsoleteDesignRetentionEnabled)}\nMap Geometry: {CampaignMapWraparoundModeText(ModSettings.CampaignMapWraparoundEnabled)}",
            () => launcherButton != null && launcherButton.interactable);

    private static void AddTooltip(GameObject target, string text, Func<bool>? canShow = null)
    {
        RemoveTooltipHandlers(target);
        OnEnter onEnter = target.AddComponent<OnEnter>();
        onEnter.action = new System.Action(() =>
        {
            if (G.ui == null || canShow?.Invoke() == false)
                return;

            G.ui.ShowTooltip(text, target);
        });

        OnLeave onLeave = target.AddComponent<OnLeave>();
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

    private static void ConfigureWindow(GameObject window)
    {
        Image image = window.GetComponent<Image>() ?? window.AddComponent<Image>();
        image.color = Background;

        RectTransform? rect = window.GetComponent<RectTransform>();
        if (rect == null)
            return;

        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(980f, 390f);
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

    private static LayoutElement AddLayout(
        GameObject target,
        float minWidth = -1f,
        float preferredWidth = -1f,
        float minHeight = -1f,
        float preferredHeight = -1f,
        float flexibleWidth = -1f,
        float flexibleHeight = -1f)
    {
        LayoutElement layout = target.GetComponent<LayoutElement>() ?? target.AddComponent<LayoutElement>();
        if (minWidth >= 0f)
            layout.minWidth = minWidth;
        if (preferredWidth >= 0f)
            layout.preferredWidth = preferredWidth;
        if (minHeight >= 0f)
            layout.minHeight = minHeight;
        if (preferredHeight >= 0f)
            layout.preferredHeight = preferredHeight;
        if (flexibleWidth >= 0f)
            layout.flexibleWidth = flexibleWidth;
        if (flexibleHeight >= 0f)
            layout.flexibleHeight = flexibleHeight;
        return layout;
    }

    private static void ClearChildren(GameObject target)
    {
        for (int i = target.transform.childCount - 1; i >= 0; i--)
            UnityEngine.Object.Destroy(target.transform.GetChild(i).gameObject);
    }

    private static string SectionTitle(Section section)
        => section switch
        {
            Section.Battle => "Battle",
            Section.Campaign => "Campaign",
            Section.ShipDesign => "Ship Design",
            Section.Experimental => "Experimental",
            _ => "Options",
        };

    private static string BattleWeatherModeText(bool alwaysSunny)
        => alwaysSunny ? "Always Sunny" : "Vanilla";

    private static string PortStrikeModeText(bool balanced)
        => balanced ? "Balanced" : "Vanilla";

    private static string DesignAccuracyPenaltiesModeText(ModSettings.AccuracyPenaltyMode mode)
        => ModSettings.AccuracyPenaltyModeText(mode);

    private static string MajorShipTorpedoesModeText(bool restricted)
        => restricted ? "Disallowed" : "Vanilla";

    private static string ObsoleteDesignRetentionModeText(bool enabled)
        => enabled ? "Retain" : "Vanilla";

    private static string ShipyardCapacityModeText(bool balanced)
        => balanced ? "Automatic" : "Manual";

    private static string CanalOpeningModeText(bool early)
        => ModSettings.CanalOpeningModeText(early);

    private static string TechnologySpreadModeText(ModSettings.TechnologySpreadMode mode)
        => ModSettings.TechnologySpreadModeText(mode);

    private static string MineWarfareModeText(bool disabled)
        => ModSettings.MineWarfareModeText(disabled);

    private static string SubmarineWarfareModeText(bool disabled)
        => ModSettings.SubmarineWarfareModeText(disabled);

    private static string CampaignMapWraparoundModeText(bool enabled)
        => enabled ? "Disc World" : "Flat Earth";

    private static void SetMenuButtonText(GameObject buttonObject, string text)
    {
        TMP_Text? tmp = Child(buttonObject, "Text (TMP)")?.GetComponent<TMP_Text>() ?? buttonObject.GetComponentInChildren<TMP_Text>();
        if (tmp != null)
        {
            RemoveComponent<LocalizeText>(tmp.gameObject);
            tmp.text = text;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontSize = 13f;
            tmp.enableWordWrapping = false;
            return;
        }

        Text? uiText = buttonObject.GetComponentInChildren<Text>();
        if (uiText != null)
        {
            uiText.text = text;
            uiText.alignment = TextAnchor.MiddleCenter;
            uiText.fontSize = 12;
        }
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
