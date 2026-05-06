using System.Reflection;
using HarmonyLib;
using Il2Cpp;
using Il2CppTMPro;
using MelonLoader;
using UADVanillaPlus.GameData;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

#pragma warning disable CS8600
#pragma warning disable CS8602
#pragma warning disable CS8603
#pragma warning disable CS8604
#pragma warning disable CS8625

namespace UADVanillaPlus.Harmony;

[HarmonyPatch(typeof(CampaignFleetWindow))]
internal static class CampaignFleetWindowDesignViewerPatch
{
    // Designs tab QoL port: add a compact nation flag strip and rebuild only
    // the design rows, leaving fleet-tab behavior and unrelated TAF/DIP UI edits out.
    private static readonly HashSet<GameObject> ForeignDesignClickVisited = new();
    private static readonly Dictionary<Player, GameObject> DesignViewerFlagButtons = new();
    private static readonly Dictionary<GameObject, Image> DesignViewerFlagImages = new();
    private static readonly HashSet<GameObject> DesignShipCountHeaderTooltips = new();
    private static readonly MethodInfo? RefreshAllShipsUi = AccessTools.Method(typeof(CampaignFleetWindow), "RefreshAllShipsUi");
    private static readonly MethodInfo? SetDesignImageAndInfoForFirstShip = AccessTools.Method(typeof(CampaignFleetWindow), "SetDesignImageAndInfoForFirstShip");
    private static readonly MethodInfo? SetShipInfoAndImage = AccessTools.Method(typeof(CampaignFleetWindow), "SetShipInfoAndImage");
    private static readonly MethodInfo? GetRefitShipFleet = AccessTools.Method(typeof(CampaignFleetWindow), "GetRefitShipFleet");

    private const float ToolbarStripHeight = 24f;
    private const float ToolbarTopGapMargin = 4f;
    private const float ToolbarFallbackTopOffset = 18f;
    private const float ContentTopGap = 32f;

    private static Player? designViewerPlayer;
    private static GameObject? designViewerToolbar;
    private static string designViewerToolbarSignature = string.Empty;
    private static Vector2? designShipsOffsetMinOriginal;
    private static Vector2? designShipsOffsetMaxOriginal;
    private static Vector2? designHeaderOffsetMinOriginal;
    private static Vector2? designHeaderOffsetMaxOriginal;
    private static bool refreshingDesignViewerList;
    private static bool suppressSortedPlayerDesignRefresh;
    private static bool loggedRefitDesignNameCleanup;

    internal static Ship? SelectedViewedDesign { get; private set; }

    private static bool IsViewingForeignDesigns => designViewerPlayer != null && designViewerPlayer != ExtraGameData.MainPlayer();

    private struct DesignShipCounts
    {
        public int Active;
        public int Building;
        public int BuildingForOthers;
        public int Other;

        public int Total => Active + Building + Other;
        public string BuildingDisplay => $"{Building}({BuildingForOthers})";
    }

    private static bool HasDesignTab(CampaignFleetWindow window)
        => window?.DesignScroll != null;

    private static bool IsDesignTabActive(CampaignFleetWindow window)
        => window?.DesignScroll != null && window.DesignScroll.activeInHierarchy;

    private static void HideDesignViewer()
    {
        if (designViewerToolbar != null)
            designViewerToolbar.SetActive(false);

        SelectedViewedDesign = null;
        RestoreDesignViewerContentLayout(G.ui?.FleetWindow);
    }

    private static List<Player> GetDesignViewerPlayers()
    {
        List<Player> players = new();
        Player mainPlayer = ExtraGameData.MainPlayer();
        if (mainPlayer != null)
            players.Add(mainPlayer);

        var campaign = CampaignController.Instance;
        if (campaign?.CampaignData?.PlayersMajor == null)
            return players;

        foreach (Player player in campaign.CampaignData.PlayersMajor)
        {
            if (player == null || player == mainPlayer || !player.isAi)
                continue;

            players.Add(player);
        }

        players.Sort((a, b) =>
        {
            if (a == mainPlayer) return -1;
            if (b == mainPlayer) return 1;
            return string.Compare(a.Name(false), b.Name(false), StringComparison.Ordinal);
        });

        return players;
    }

    private static Player GetCurrentDesignViewerPlayer()
    {
        Player mainPlayer = ExtraGameData.MainPlayer();
        List<Player> players = GetDesignViewerPlayers();

        if (designViewerPlayer == null || !players.Contains(designViewerPlayer))
            designViewerPlayer = mainPlayer ?? players.FirstOrDefault();

        return designViewerPlayer;
    }

    private static void EnsureDesignViewerToolbar(CampaignFleetWindow window)
    {
        // The toolbar sits in the spare header gap above vanilla's Designs list,
        // then the list/header are nudged down so flags do not cover rows.
        if (!HasDesignTab(window))
        {
            HideDesignViewer();
            return;
        }

        if (designViewerToolbar == null)
        {
            designViewerToolbar = new GameObject("UADVP_DesignViewerToolbar");
            designViewerToolbar.AddComponent<RectTransform>();

            HorizontalLayoutGroup layout = designViewerToolbar.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 3f;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.childAlignment = TextAnchor.MiddleCenter;

            LayoutElement toolbarLayout = designViewerToolbar.AddComponent<LayoutElement>();
            toolbarLayout.minHeight = ToolbarStripHeight;
            toolbarLayout.preferredHeight = ToolbarStripHeight;
        }

        CanvasGroup canvasGroup = designViewerToolbar.GetComponent<CanvasGroup>() ?? designViewerToolbar.AddComponent<CanvasGroup>();
        canvasGroup.alpha = 1f;
        canvasGroup.interactable = true;
        canvasGroup.blocksRaycasts = true;

        RestoreDesignViewerContentLayout(window);
        MoveDesignViewerToolbarToTop(window);
        ApplyDesignViewerContentGap(window);

        designViewerToolbar.SetActive(true);
        RebuildDesignViewerToolbarIfNeeded();
        UpdateDesignViewerFlagSizes(window);
        UpdateDesignViewerToolbar();
    }

    private static void MoveDesignViewerToolbarToTop(CampaignFleetWindow window)
    {
        if (designViewerToolbar == null)
            return;

        GameObject parent = window?.Root?.GetChild("Root") ?? window?.Root;
        if (parent == null)
            return;

        if (designViewerToolbar.transform.parent != parent.transform)
            designViewerToolbar.transform.SetParent(parent.transform, false);

        RectTransform rect = designViewerToolbar.GetComponent<RectTransform>() ?? designViewerToolbar.AddComponent<RectTransform>();
        RectTransform parentRect = parent.GetComponent<RectTransform>();
        RectTransform designRect = GetDesignShipsRect(window);

        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 0.5f);

        float width = 900f;
        if (parentRect != null && parentRect.rect.width > 100f)
            width = Mathf.Min(width, parentRect.rect.width - 80f);
        if (designRect != null && designRect.rect.width > 100f)
            width = Mathf.Min(width, designRect.rect.width - 8f);
        width = Mathf.Max(320f, width);
        rect.sizeDelta = new Vector2(width, ToolbarStripHeight);

        float yFromTop = -ToolbarFallbackTopOffset;
        if (parentRect != null && designRect != null)
        {
            Vector3[] parentCorners = new Vector3[4];
            Vector3[] designCorners = new Vector3[4];
            parentRect.GetWorldCorners(parentCorners);
            designRect.GetWorldCorners(designCorners);

            float parentTop = parentRect.InverseTransformPoint(parentCorners[1]).y;
            float designTop = parentRect.InverseTransformPoint(designCorners[1]).y;
            float gap = parentTop - designTop;
            if (gap > ToolbarStripHeight + ToolbarTopGapMargin * 2f)
            {
                float centerFromTop = gap * 0.5f;
                float minCenterFromTop = ToolbarTopGapMargin + ToolbarStripHeight * 0.5f;
                float maxCenterFromTop = gap - minCenterFromTop;
                // Keep the selector in the window's top information band. Centering
                // the whole gap can overlap the column header on some resolutions.
                yFromTop = -Mathf.Min(ToolbarFallbackTopOffset, maxCenterFromTop);
            }

        }

        rect.anchoredPosition = new Vector2(0f, yFromTop);
        designViewerToolbar.transform.SetAsLastSibling();
    }

    private static RectTransform GetDesignShipsRect(CampaignFleetWindow window)
    {
        GameObject designShips = window?.Root?.GetChild("Root")?.GetChild("Design Ships");
        return designShips != null ? designShips.GetComponent<RectTransform>() : null;
    }

    private static void ApplyDesignViewerContentGap(CampaignFleetWindow window)
    {
        RectTransform designRect = GetDesignShipsRect(window);
        if (designRect != null)
        {
            designShipsOffsetMinOriginal ??= designRect.offsetMin;
            designShipsOffsetMaxOriginal ??= designRect.offsetMax;
            Vector2 originalMax = designShipsOffsetMaxOriginal.Value;
            designRect.offsetMax = new Vector2(originalMax.x, originalMax.y - ContentTopGap);
        }

        RectTransform headerRect = window?.DesignHeader != null ? window.DesignHeader.GetComponent<RectTransform>() : null;
        if (headerRect != null)
        {
            designHeaderOffsetMinOriginal ??= headerRect.offsetMin;
            designHeaderOffsetMaxOriginal ??= headerRect.offsetMax;
            Vector2 originalMin = designHeaderOffsetMinOriginal.Value;
            Vector2 originalMax = designHeaderOffsetMaxOriginal.Value;
            headerRect.offsetMin = new Vector2(originalMin.x, originalMin.y - ContentTopGap);
            headerRect.offsetMax = new Vector2(originalMax.x, originalMax.y - ContentTopGap);
        }
    }

    private static void RestoreDesignViewerContentLayout(CampaignFleetWindow window)
    {
        RectTransform designRect = GetDesignShipsRect(window);
        if (designRect != null)
        {
            designShipsOffsetMinOriginal ??= designRect.offsetMin;
            designShipsOffsetMaxOriginal ??= designRect.offsetMax;
            designRect.offsetMin = designShipsOffsetMinOriginal.Value;
            designRect.offsetMax = designShipsOffsetMaxOriginal.Value;
        }

        RectTransform headerRect = window?.DesignHeader != null ? window.DesignHeader.GetComponent<RectTransform>() : null;
        if (headerRect != null)
        {
            designHeaderOffsetMinOriginal ??= headerRect.offsetMin;
            designHeaderOffsetMaxOriginal ??= headerRect.offsetMax;
            headerRect.offsetMin = designHeaderOffsetMinOriginal.Value;
            headerRect.offsetMax = designHeaderOffsetMaxOriginal.Value;
        }
    }

    private static void RebuildDesignViewerToolbarIfNeeded()
    {
        // Rebuild only when the campaign/player identity changes; refreshing
        // the Designs tab itself should just update selection state and row data.
        if (designViewerToolbar == null)
            return;

        List<Player> players = GetDesignViewerPlayers();
        string signature = BuildDesignViewerToolbarSignature(players);
        bool hasStaleButton = DesignViewerFlagButtons.Values.Any(button => button == null || button.transform == null || button.transform.parent != designViewerToolbar.transform);
        if (designViewerToolbarSignature == signature && DesignViewerFlagButtons.Count == players.Count && !hasStaleButton)
            return;

        foreach (GameObject child in designViewerToolbar.GetChildren())
        {
            if (child != null)
                UnityEngine.Object.Destroy(child);
        }

        DesignViewerFlagButtons.Clear();
        DesignViewerFlagImages.Clear();

        foreach (Player player in players)
        {
            if (player == null)
                continue;

            GameObject flagButton = new("UADVP_DesignViewerFlag");
            flagButton.AddComponent<RectTransform>();
            flagButton.transform.SetParent(designViewerToolbar.transform, false);
            flagButton.name = $"UADVP_DesignViewerFlag_{player.data?.name ?? player.Name(false)}";
            flagButton.SetActive(true);

            LayoutElement layout = flagButton.GetComponent<LayoutElement>() ?? flagButton.AddComponent<LayoutElement>();
            layout.minWidth = 32f;
            layout.preferredWidth = 32f;
            layout.minHeight = 20f;
            layout.preferredHeight = 20f;

            RectTransform buttonRect = flagButton.GetComponent<RectTransform>();
            buttonRect.sizeDelta = new Vector2(32f, 20f);

            Image background = flagButton.AddComponent<Image>();
            background.color = new Color(1f, 1f, 1f, 0.18f);

            Button button = flagButton.AddComponent<Button>();
            button.targetGraphic = background;
            Player capturedPlayer = player;
            button.onClick.AddListener(new System.Action(() => OnDesignViewerFlagClicked(capturedPlayer)));

            GameObject flagImageObject = new("UADVP_FlagImage");
            flagImageObject.AddComponent<RectTransform>();
            flagImageObject.transform.SetParent(flagButton.transform, false);

            RectTransform rect = flagImageObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            Image flagImage = flagImageObject.AddComponent<Image>();
            flagImage.sprite = player.Flag(true) ?? player.Flag(false);
            flagImage.preserveAspect = true;
            flagImage.raycastTarget = false;
            flagImage.color = Color.white;

            OnEnter onEnter = flagButton.AddComponent<OnEnter>();
            Player tooltipPlayer = player;
            onEnter.action = new System.Action(() =>
            {
                if (!flagButton.active || (button != null && !button.interactable))
                {
                    G.ui.HideTooltip();
                    return;
                }

                G.ui.ShowTooltip(BuildDesignViewerTooltip(tooltipPlayer), flagButton);
            });

            OnLeave onLeave = flagButton.AddComponent<OnLeave>();
            onLeave.action = new System.Action(() => G.ui.HideTooltip());

            DesignViewerFlagButtons[player] = flagButton;
            DesignViewerFlagImages[flagButton] = flagImage;
        }

        RectTransform toolbarRect = designViewerToolbar.GetComponent<RectTransform>();
        if (toolbarRect != null)
            toolbarRect.sizeDelta = new Vector2(toolbarRect.sizeDelta.x, ToolbarStripHeight);

        designViewerToolbarSignature = signature;
        Melon<UADVanillaPlusMod>.Logger.Msg($"UADVP design viewer: rebuilt nation selector for {players.Count} player(s); campaign={RuntimeObjectKey(CampaignController.Instance)}.");
    }

    private static string BuildDesignViewerToolbarSignature(List<Player> players)
    {
        string campaignKey = RuntimeObjectKey(CampaignController.Instance);
        string playerKeys = string.Join("|", players.Select(p => $"{PlayerKey(p)}@{RuntimeObjectKey(p)}"));
        return $"{campaignKey}:{playerKeys}";
    }

    private static string PlayerKey(Player player)
    {
        if (player == null)
            return "<null>";

        try
        {
            return player.data?.name ?? player.Name(false) ?? "<unknown>";
        }
        catch
        {
            return "<unprintable>";
        }
    }

    private static string PlayerLabel(Player player)
    {
        if (player == null)
            return "<null>";

        try
        {
            return player.Name(false);
        }
        catch
        {
            return PlayerKey(player);
        }
    }

    private static string RuntimeObjectKey(object value)
    {
        if (value == null)
            return "null";

        try
        {
            return value switch
            {
                CampaignController campaign => campaign.Pointer.ToString(),
                Player player => player.Pointer.ToString(),
                GameObject gameObject => gameObject.GetInstanceID().ToString(),
                _ => value.GetHashCode().ToString("X"),
            };
        }
        catch
        {
            return "unknown";
        }
    }

    private static void UpdateDesignViewerFlagSizes(CampaignFleetWindow window)
    {
        int count = DesignViewerFlagButtons.Count;
        if (count == 0)
            return;

        float availableWidth = 900f;
        RectTransform toolbarRect = designViewerToolbar != null ? designViewerToolbar.GetComponent<RectTransform>() : null;
        if (toolbarRect != null && toolbarRect.rect.width > 10f)
            availableWidth = toolbarRect.rect.width;
        else
        {
            RectTransform designRect = GetDesignShipsRect(window);
            if (designRect != null && designRect.rect.width > 10f)
                availableWidth = designRect.rect.width - 8f;
        }

        float spacing = 3f;
        float flagWidth = Mathf.Clamp((availableWidth - spacing * Math.Max(0, count - 1)) / count, 28f, 44f);
        float flagHeight = Mathf.Clamp(flagWidth * 0.62f, 17f, 24f);

        foreach (GameObject flagButton in DesignViewerFlagButtons.Values)
        {
            if (flagButton == null)
                continue;

            LayoutElement layout = flagButton.GetComponent<LayoutElement>() ?? flagButton.AddComponent<LayoutElement>();
            layout.minWidth = flagWidth;
            layout.preferredWidth = flagWidth;
            layout.minHeight = flagHeight;
            layout.preferredHeight = flagHeight;

            RectTransform buttonRect = flagButton.GetComponent<RectTransform>();
            if (buttonRect != null)
                buttonRect.sizeDelta = new Vector2(flagWidth, flagHeight);
        }

        LayoutElement toolbarLayout = designViewerToolbar?.GetComponent<LayoutElement>();
        if (toolbarLayout != null)
        {
            toolbarLayout.minHeight = flagHeight;
            toolbarLayout.preferredHeight = flagHeight;
        }
    }

    private static void SetDesignViewerPlayer(Player player)
        => SetDesignViewerPlayer(G.ui?.FleetWindow, player);

    private static void OnDesignViewerFlagClicked(Player player)
    {
        Melon<UADVanillaPlusMod>.Logger.Msg(
            $"UADVP design viewer: flag clicked {PlayerLabel(GetCurrentDesignViewerPlayer())} -> {PlayerLabel(player)}; campaign={RuntimeObjectKey(CampaignController.Instance)}.");
        SetDesignViewerPlayer(player);
    }

    private static void SetDesignViewerPlayer(CampaignFleetWindow window, Player player)
    {
        if (window == null || player == null || !HasDesignTab(window))
            return;

        if (!GetDesignViewerPlayers().Contains(player))
        {
            Melon<UADVanillaPlusMod>.Logger.Warning($"UADVP design viewer: ignored stale nation selector target {PlayerLabel(player)}; rebuilding selector.");
            designViewerToolbarSignature = string.Empty;
            RebuildDesignViewerToolbarIfNeeded();
            UpdateDesignViewerToolbar();
            return;
        }

        designViewerPlayer = player;
        UpdateDesignViewerToolbar();
        ClearCurrentDesignList(window);

        try
        {
            window.Refresh(true);
        }
        catch (Exception ex)
        {
            Melon<UADVanillaPlusMod>.Logger.Warning($"Design viewer country switch failed; restoring player designs. {ex.GetType().Name}: {ex.Message}");
            designViewerPlayer = ExtraGameData.MainPlayer();
            HideDesignViewer();
        }
    }

    private static void UpdateDesignViewerToolbar()
    {
        if (designViewerToolbar == null)
            return;

        Player current = GetCurrentDesignViewerPlayer();
        foreach (var kvp in DesignViewerFlagButtons)
        {
            GameObject flagButton = kvp.Value;
            if (flagButton == null)
                continue;

            bool selected = kvp.Key == current;
            Image background = flagButton.GetComponent<Image>();
            if (background != null)
                background.color = selected ? new Color(1f, 0.86f, 0.28f, 1f) : new Color(0.72f, 0.72f, 0.72f, 0.78f);

            if (DesignViewerFlagImages.TryGetValue(flagButton, out Image flagImage) && flagImage != null)
                flagImage.color = selected ? Color.white : new Color(0.74f, 0.74f, 0.74f, 0.92f);

            Outline outline = flagButton.GetComponent<Outline>() ?? flagButton.AddComponent<Outline>();
            outline.enabled = selected;
            outline.effectColor = new Color(1f, 0.72f, 0.08f, 1f);
            outline.effectDistance = new Vector2(3f, -3f);
        }
    }

    private static string BuildDesignViewerTooltip(Player player)
    {
        if (player == null)
            return "Designs";

        var designs = GetViewedDesigns(player);
        Dictionary<string, int> designsByClass = new();
        foreach (Ship design in designs)
        {
            string cls = ShipClassLabel(design);
            designsByClass[cls] = designsByClass.TryGetValue(cls, out int count) ? count + 1 : 1;
        }

        DesignShipCounts total = new();
        foreach (Ship ship in player.GetFleetAll())
        {
            if (ship == null || ship.isDesign)
                continue;

            AddShipStateToCounts(ship, ref total);
        }

        List<string> lines = new()
        {
            player.Name(false),
            $"Designs: {designs.Count}",
            $"Ships: {total.Total} ({total.Active}/{total.BuildingDisplay}/{total.Other})"
        };

        if (designsByClass.Count > 0)
            lines.Add($"Designs by class: {FormatSimpleClassCounts(designsByClass)}");

        return string.Join("\n", lines);
    }

    private static DesignShipCounts GetDesignShipCounts(Player player, Ship design)
    {
        DesignShipCounts counts = new();
        if (player == null || design == null)
            return counts;

        foreach (Ship ship in player.GetFleetAll())
        {
            if (ship == null || ship.design != design)
                continue;

            AddShipStateToCounts(ship, ref counts);
        }

        return counts;
    }

    private static void SetDesignShipCountText(FleetWindow_ShipElementUI ui, Player player, Ship design)
    {
        if (ui?.ShipCount == null || player == null || design == null)
            return;

        DesignShipCounts counts = GetDesignShipCounts(player, design);
        ui.ShipCount.text = $"{counts.Active}/{counts.BuildingDisplay}/{counts.Other}";
    }

    private static void SetDesignRowNameText(FleetWindow_ShipElementUI ui, Ship design)
    {
        if (ui?.Name == null || design == null)
            return;

        ui.Name.fontStyle &= ~FontStyles.Italic;

        string name = DesignRowDisplayName(design);
        ui.Name.text = design.isErased ? $"<color=#8F2F2F><s>{EscapeTextMeshProRichText(name)}</s></color>" : name;
    }

    private static string DesignRowDisplayName(Ship design)
    {
        string name = design.Name(false, false, false, false, true);
        if (design.isRefitDesign)
        {
            name = StripRefitDesignCloneSuffix(name);
            LogRefitDesignNameCleanupOnce();
        }

        return name;
    }

    private static string StripRefitDesignCloneSuffix(string name)
    {
        if (string.IsNullOrEmpty(name))
            return string.Empty;

        int end = name.Length - 1;
        while (end >= 0 && char.IsWhiteSpace(name[end]))
            end--;

        int digitEnd = end;
        while (end >= 0 && char.IsDigit(name[end]))
            end--;

        if (end == digitEnd)
            return name;

        while (end >= 0 && char.IsWhiteSpace(name[end]))
            end--;

        if (end <= 0 || name[end] != '-' || !char.IsWhiteSpace(name[end - 1]))
            return name;

        return name.Substring(0, end).TrimEnd();
    }

    private static void LogRefitDesignNameCleanupOnce()
    {
        if (loggedRefitDesignNameCleanup)
            return;

        loggedRefitDesignNameCleanup = true;
        Melon<UADVanillaPlusMod>.Logger.Msg("Design viewer: normalized refit design row names.");
    }

    private static string EscapeTextMeshProRichText(string text)
    {
        return (text ?? string.Empty)
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");
    }

    private static void AddShipStateToCounts(Ship ship, ref DesignShipCounts counts)
    {
        if (ship.isBuilding || ship.isCommissioning)
        {
            // ForSaleTo is vanilla's "this yard is building the ship for another nation"
            // marker. Keep those ships inside the normal building total, but show the
            // foreign-contract subset in parentheses so the Designs tab explains where
            // the queue pressure is coming from.
            counts.Building++;
            if (ship.ForSaleTo != null)
                counts.BuildingForOthers++;
        }
        else if (ship.isAlive && !ship.isRefit && !ship.isRepairing)
            counts.Active++;
        else
            counts.Other++;
    }

    private static string ShipClassLabel(Ship ship)
        => ship?.shipType?.name?.ToUpperInvariant() ?? "?";

    private static string FormatSimpleClassCounts(Dictionary<string, int> counts)
        => string.Join(", ", counts.OrderBy(kvp => kvp.Key).Select(kvp => $"{kvp.Key} {kvp.Value}"));

    private static void EnsureDesignShipCountHeaderTooltip(CampaignFleetWindow window)
    {
        GameObject header = window?.DesignHeader;
        if (header == null)
            return;

        GameObject target = null;
        foreach (GameObject child in header.GetChildren())
        {
            if (child == null)
                continue;

            string childName = child.name ?? string.Empty;
            TMP_Text childText = child.GetComponent<TMP_Text>() ?? child.GetComponentInChildren<TMP_Text>();
            string text = childText?.text ?? string.Empty;
            if (childName.Contains("ShipCount", StringComparison.OrdinalIgnoreCase) ||
                childName.Contains("Count", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("Ship Count", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("Count", StringComparison.OrdinalIgnoreCase))
            {
                target = child;
                break;
            }
        }

        if (target == null || DesignShipCountHeaderTooltips.Contains(target))
            return;

        DesignShipCountHeaderTooltips.Add(target);
        AddRawTooltip(target, "Shown as active/building/other.\nBuilding uses total(foreign contracts).\nActive: afloat and available.\nOther: refit, repair, mothball, or otherwise unavailable.");
    }

    private static void AddRawTooltip(GameObject ui, string content)
    {
        if (ui == null)
            return;

        OnEnter onEnter = ui.AddComponent<OnEnter>();
        onEnter.action = new System.Action(() => G.ui.ShowTooltip(content, ui));

        OnLeave onLeave = ui.AddComponent<OnLeave>();
        onLeave.action = new System.Action(() => G.ui.HideTooltip());
    }

    private static Il2CppSystem.Collections.Generic.List<Ship> GetViewedDesigns(Player player)
    {
        // Vanilla only shows the current player's design list. VP also includes
        // erased/old designs still referenced by ships, so obsolete classes remain inspectable.
        Il2CppSystem.Collections.Generic.List<Ship> designs = new();
        if (player == null)
            return designs;

        List<Ship> sortedDesigns = new();

        void AddDesignCandidate(Ship ship, bool requireShips)
        {
            if (ship == null || (!ship.isDesign && !ship.isRefitDesign) || sortedDesigns.Contains(ship))
                return;

            DesignShipCounts counts = GetDesignShipCounts(player, ship);
            if ((ship.isErased || requireShips) && counts.Total == 0)
                return;

            sortedDesigns.Add(ship);
        }

        var sourceDesigns = new Il2CppSystem.Collections.Generic.List<Ship>(player.designs);
        foreach (Ship ship in sourceDesigns)
            AddDesignCandidate(ship, false);

        foreach (Ship ship in player.GetFleetAll())
        {
            if (ship == null || ship.isDesign)
                continue;

            AddDesignCandidate(ship.design, true);
        }

        sortedDesigns.Sort(CompareDesignsByDefaultClassOrder);
        foreach (Ship ship in sortedDesigns)
            designs.Add(ship);

        return designs;
    }

    private static int CompareDesignsByDefaultClassOrder(Ship a, Ship b)
    {
        int order = ShipTypeSortRank(a).CompareTo(ShipTypeSortRank(b));
        if (order != 0)
            return order;

        int year = ShipDesignYear(a).CompareTo(ShipDesignYear(b));
        if (year != 0)
            return year;

        return string.Compare(a?.Name(false, false, false, false, true), b?.Name(false, false, false, false, true), StringComparison.Ordinal);
    }

    private static int ShipTypeSortRank(Ship ship)
    {
        return (ship?.shipType?.name ?? string.Empty).ToLowerInvariant() switch
        {
            "bb" => 0,
            "bc" => 1,
            "ca" => 2,
            "cl" => 3,
            "dd" => 4,
            "tb" => 5,
            "ss" => 6,
            "tr" => 7,
            _ => 100
        };
    }

    private static int ShipDesignYear(Ship ship)
    {
        if (ship == null)
            return 0;

        return (ship.isRefitDesign ? ship.dateCreatedRefit : ship.dateCreated).AsDate().Year;
    }

    private static void SetForeignDesignButtonsInteractable(CampaignFleetWindow window, bool interactable)
    {
        // Foreign nations are browse-only. Keeping action buttons disabled avoids
        // accidental build/delete/refit calls against AI-owned designs.
        if (window?.DesignButtonsRoot == null)
            return;

        foreach (GameObject child in window.DesignButtonsRoot.GetChildren())
        {
            if (child == designViewerToolbar || child.name.StartsWith("UADVP_DesignViewer"))
                continue;

            Button button = child.GetComponent<Button>();
            if (button != null)
                button.interactable = interactable;
        }

        SetDesignActionButtonsInteractable(window, interactable);
    }

    private static void SetDesignActionButtonsInteractable(CampaignFleetWindow window, bool interactable)
    {
        if (window == null)
            return;

        if (window.DesignView != null) window.DesignView.interactable = interactable;
        if (window.Delete != null) window.Delete.interactable = interactable;
        if (window.NewDesign != null) window.NewDesign.interactable = interactable;
        if (window.BuildShip != null) window.BuildShip.interactable = interactable;
        if (window.DesignRefit != null) window.DesignRefit.interactable = interactable;
        if (window.CancelSale != null) window.CancelSale.interactable = interactable;
    }

    private static void UpdateDesignSelectionActions(CampaignFleetWindow window, Player player, Ship ship, bool allowActions)
    {
        if (window == null || ship == null)
            return;

        if (!allowActions)
        {
            SetForeignDesignButtonsInteractable(window, false);
            return;
        }

        SetDesignActionButtonsInteractable(window, true);

        DesignShipCounts counts = GetDesignShipCounts(player, ship);
        if (window.Delete != null)
            window.Delete.interactable = !ship.isErased && counts.Total == 0;

        if (window.BuildShip != null)
            window.BuildShip.interactable = !ship.isErased && (ship.isDesign || ship.isRefitDesign);

        if (window.DesignRefit != null && ship.isErased)
            window.DesignRefit.interactable = false;
    }

    private static void DisableDesignSelectionActionsIfNothingSelected(CampaignFleetWindow window)
    {
        // Vanilla's design action listeners read selectedElements[0]. VP can
        // rebuild the design list without a selected row, so keep selection-
        // dependent buttons inactive until the player clicks a design row.
        if (window?.selectedElements != null && window.selectedElements.Count > 0)
            return;

        if (window?.DesignView != null) window.DesignView.interactable = false;
        if (window?.Delete != null) window.Delete.interactable = false;
        if (window?.BuildShip != null) window.BuildShip.interactable = false;
        if (window?.DesignRefit != null) window.DesignRefit.interactable = false;
        if (window?.CancelSale != null) window.CancelSale.interactable = false;
    }

    private static Ship? GetSelectedViewedDesign(CampaignFleetWindow window)
    {
        if (SelectedViewedDesign != null)
            return SelectedViewedDesign;

        if (window?.selectedElements != null && window.selectedElements.Count > 0)
        {
            Ship selected = window.selectedElements[0]?.CurrentShip;
            if (selected != null)
                return selected;
        }

        return null;
    }

    private static void InstallDesignDeleteButtonHandler(CampaignFleetWindow window, bool allowActions, Ship capturedTarget = null)
    {
        // The rebuilt design list can desync vanilla's selectedElements target.
        // Capture the clicked row so Delete acts on the visible selected design.
        if (window?.Delete == null || !allowActions)
            return;

        window.Delete.onClick.RemoveAllListeners();
        window.Delete.onClick.AddListener(new System.Action(() =>
        {
            Ship target = capturedTarget ?? GetSelectedViewedDesign(window);
            Player player = GetCurrentDesignViewerPlayer();
            DesignShipCounts counts = GetDesignShipCounts(player, target);
            if (target == null || target.isErased || counts.Total > 0)
                return;

            if (CampaignController.Instance != null)
            {
                CampaignController.Instance.DeleteDesign(target);
                window.Refresh(true);
            }
        }));
    }

    private static void ClearCurrentDesignList(CampaignFleetWindow window)
    {
        if (window == null)
            return;

        SelectedViewedDesign = null;
        foreach (var element in window.designUiByShip)
        {
            if (element.Value != null)
                element.Value.gameObject.SetActive(false);
        }

        window.designUiByShip.Clear();
        window.selectedElements.Clear();
        ForeignDesignClickVisited.Clear();
    }

    private static Il2CppSystem.Collections.Generic.List<Ship> BuildUiBackedDesignList(CampaignFleetWindow window, Il2CppSystem.Collections.Generic.List<Ship> requestedDesigns, string context)
    {
        Il2CppSystem.Collections.Generic.List<Ship> safeDesigns = new();
        if (window?.designUiByShip == null)
            return safeDesigns;

        int requestedCount = requestedDesigns?.Count ?? 0;
        int missingCount = 0;
        List<string> missingSamples = new();

        if (requestedDesigns != null)
        {
            foreach (Ship design in requestedDesigns)
            {
                if (design != null && window.designUiByShip.ContainsKey(design))
                {
                    safeDesigns.Add(design);
                    continue;
                }

                missingCount++;
                if (missingSamples.Count < 3)
                    missingSamples.Add(DesignLogName(design));
            }
        }

        int appendedUiOnlyCount = 0;
        foreach (var element in window.designUiByShip)
        {
            Ship design = element.Key;
            if (design == null || safeDesigns.Contains(design))
                continue;

            safeDesigns.Add(design);
            appendedUiOnlyCount++;
        }

        if (missingCount > 0 || appendedUiOnlyCount > 0 || requestedCount != safeDesigns.Count)
        {
            Melon<UADVanillaPlusMod>.Logger.Warning(
                $"UADVP DesignViewer list mismatch during {context}: " +
                $"requested={requestedCount}, ui={window.designUiByShip.Count}, safe={safeDesigns.Count}, " +
                $"missing={missingCount}, uiOnly={appendedUiOnlyCount}, samples=[{string.Join("; ", missingSamples)}].");
        }

        return safeDesigns;
    }

    private static bool TrySetDesignImageAndInfo(CampaignFleetWindow window, Il2CppSystem.Collections.Generic.List<Ship> requestedDesigns, Ship nextShip, bool isDesign, string context)
    {
        if (SetDesignImageAndInfoForFirstShip == null || window == null)
            return false;

        Il2CppSystem.Collections.Generic.List<Ship> safeDesigns = BuildUiBackedDesignList(window, requestedDesigns, context);
        Ship safeNextShip = nextShip;
        if (safeNextShip != null && !window.designUiByShip.ContainsKey(safeNextShip))
        {
            Melon<UADVanillaPlusMod>.Logger.Warning(
                $"UADVP DesignViewer stale selection during {context}: selected={DesignLogName(safeNextShip)} is not present in designUiByShip; clearing selection.");
            safeNextShip = null;
            SelectedViewedDesign = null;
        }

        try
        {
            object?[] args = { safeDesigns, safeNextShip, isDesign };
            SetDesignImageAndInfoForFirstShip.Invoke(window, args);
            return true;
        }
        catch (Exception ex)
        {
            Exception detail = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
            Melon<UADVanillaPlusMod>.Logger.Warning(
                $"UADVP DesignViewer SetDesignImageAndInfo failed during {context}: " +
                $"selected={DesignLogName(safeNextShip)}, safeCount={safeDesigns.Count}, ui={window.designUiByShip?.Count ?? 0}, " +
                $"{detail.GetType().Name}: {detail.Message}");
            return false;
        }
    }

    private static string DesignLogName(Ship ship)
    {
        if (ship == null)
            return "<null>";

        try
        {
            return $"{ship.Name(false, false, false, false, true)} ({ShipClassLabel(ship)} {ShipDesignYear(ship)})";
        }
        catch
        {
            return "<unprintable>";
        }
    }

    private static void RefreshViewedDesigns(CampaignFleetWindow window, bool allowActions)
    {
        Player player = GetCurrentDesignViewerPlayer();
        if (player == null || !IsDesignTabActive(window) || RefreshAllShipsUi == null || refreshingDesignViewerList || suppressSortedPlayerDesignRefresh)
            return;

        try
        {
            refreshingDesignViewerList = true;
            var designs = GetViewedDesigns(player);
            ClearCurrentDesignList(window);
            RefreshAllShipsUi.Invoke(window, new object[] { true, designs });
            var uiBackedDesigns = BuildUiBackedDesignList(window, designs, $"refresh {player.Name(false)}");
            TrySetDesignImageAndInfo(window, uiBackedDesigns, null, true, $"refresh {player.Name(false)}");

            AttachDesignSelectionHandlers(window, uiBackedDesigns, allowActions);
            SetForeignDesignButtonsInteractable(window, allowActions);
            RebuildDesignRefitButton(window, allowActions);
            InstallDesignDeleteButtonHandler(window, allowActions);
            DisableDesignSelectionActionsIfNothingSelected(window);
            UpdateDesignViewerToolbar();
        }
        catch (Exception ex)
        {
            Melon<UADVanillaPlusMod>.Logger.Warning($"Design viewer refresh failed; restoring vanilla player designs. {ex.GetType().Name}: {ex.Message}");
            if (allowActions)
            {
                suppressSortedPlayerDesignRefresh = true;
                try
                {
                    window.Refresh(true);
                }
                finally
                {
                    suppressSortedPlayerDesignRefresh = false;
                }
            }
            else
            {
                designViewerPlayer = ExtraGameData.MainPlayer();
                HideDesignViewer();
            }
        }
        finally
        {
            refreshingDesignViewerList = false;
        }
    }

    private static void AttachDesignSelectionHandlers(CampaignFleetWindow window, Il2CppSystem.Collections.Generic.List<Ship> designs, bool allowActions)
    {
        if (window == null)
            return;

        foreach (var element in window.designUiByShip)
        {
            Ship ship = element.Key;
            FleetWindow_ShipElementUI ui = element.Value;
            if (ship == null || ui?.Btn == null)
                continue;

            ui.CurrentShip = ship;
            System.Action selectAction = new(() => SelectViewedDesign(window, designs, ship, ui, allowActions));
            ui.Btn.onClick.AddListener(selectAction);

            Button rowButton = ui.gameObject.GetComponent<Button>();
            if (rowButton != null && rowButton != ui.Btn)
                rowButton.onClick.AddListener(selectAction);

            foreach (Transform t in ui.gameObject.GetComponentsInChildren<Transform>(true))
            {
                GameObject clickTarget = t.gameObject;
                if (clickTarget == null || ForeignDesignClickVisited.Contains(clickTarget))
                    continue;

                ForeignDesignClickVisited.Add(clickTarget);
                OnClickH click = clickTarget.AddComponent<OnClickH>();
                click.action = new System.Action<PointerEventData>(_ => SelectViewedDesign(window, designs, ship, ui, allowActions));
            }
        }
    }

    private static void SelectViewedDesign(CampaignFleetWindow window, Il2CppSystem.Collections.Generic.List<Ship> designs, Ship ship, FleetWindow_ShipElementUI ui, bool allowActions)
    {
        if (window == null || ship == null || ui == null)
            return;

        if (window.designUiByShip == null || !window.designUiByShip.ContainsKey(ship))
        {
            Melon<UADVanillaPlusMod>.Logger.Warning($"UADVP DesignViewer ignored stale design click: {DesignLogName(ship)} is no longer present in designUiByShip.");
            SelectedViewedDesign = null;
            return;
        }

        SelectedViewedDesign = ship;
        ui.CurrentShip = ship;
        window.selectedElements.Clear();
        window.selectedElements.Add(ui);

        foreach (var element in window.designUiByShip)
        {
            if (element.Value?.Highlighted != null)
                element.Value.Highlighted.gameObject.SetActive(element.Value == ui);
        }

        SetShipInfoAndImage?.Invoke(window, new object[] { ship });
        TrySetDesignImageAndInfo(window, designs, ship, true, $"select {DesignLogName(ship)}");
        SelectedViewedDesign = ship;
        ui.CurrentShip = ship;
        window.selectedElements.Clear();
        window.selectedElements.Add(ui);
        UpdateDesignSelectionActions(window, GetCurrentDesignViewerPlayer(), ship, allowActions);
        RebuildDesignRefitButton(window, allowActions);
        InstallDesignDeleteButtonHandler(window, allowActions, ship);
    }

    private static void RebuildDesignRefitButton(CampaignFleetWindow window, bool allowActions)
    {
        if (window?.DesignRefit == null)
            return;

        window.DesignRefit.onClick.RemoveAllListeners();
        if (!allowActions || window.selectedElements == null || window.selectedElements.Count == 0)
        {
            window.DesignRefit.interactable = false;
            return;
        }

        Ship selectedDesign = window.selectedElements[0]?.CurrentShip;
        if (selectedDesign == null || selectedDesign.isErased || !selectedDesign.isRefitDesign || GetRefitShipFleet == null)
        {
            window.DesignRefit.interactable = false;
            return;
        }

        try
        {
            object[] args = new object[] { 0 };
            var refitFleet = GetRefitShipFleet.Invoke(window, args) as Il2CppSystem.Collections.Generic.Dictionary<Ship, FleetWindow_ShipElementUI>;
            bool hasCandidates = refitFleet != null && refitFleet.Count > 0;
            window.DesignRefit.interactable = hasCandidates;
            if (!hasCandidates)
                return;

            window.DesignRefit.onClick.AddListener(new System.Action(() =>
            {
                if (window.selectShipsRefit == null || window.selectedElements == null || window.selectedElements.Count == 0)
                    return;

                Ship refitDesign = window.selectedElements[0]?.CurrentShip;
                if (refitDesign == null || !refitDesign.isRefitDesign)
                    return;

                window.selectShipsRefit.Show(new System.Action<Il2CppSystem.Collections.Generic.List<Ship>>((selection) =>
                {
                    if (PlayerController.Instance == null || selection == null)
                        return;

                    PlayerController.Instance.RefitShipsStart(selection, refitDesign, true);
                    GameManager.Instance?.ChangeStateUI((GameManager.UIState)3, true);
                }), refitFleet, refitDesign);
            }));
        }
        catch (Exception ex)
        {
            window.DesignRefit.interactable = false;
            Melon<UADVanillaPlusMod>.Logger.Warning($"Design refit button rebuild failed. {ex.GetType().Name}: {ex.Message}");
        }
    }

    [HarmonyPatch(nameof(CampaignFleetWindow.Refresh))]
    [HarmonyPrefix]
    private static void PrefixRefresh(CampaignFleetWindow __instance, bool isDesign)
    {
        if (!isDesign || !HasDesignTab(__instance))
        {
            if (!isDesign)
                designViewerPlayer = ExtraGameData.MainPlayer();

            HideDesignViewer();
            return;
        }

        EnsureDesignViewerToolbar(__instance);
    }

    [HarmonyPatch(nameof(CampaignFleetWindow.Refresh))]
    [HarmonyPostfix]
    private static void PostfixRefresh(CampaignFleetWindow __instance, bool isDesign)
    {
        try
        {
            if (isDesign && HasDesignTab(__instance))
            {
                EnsureDesignViewerToolbar(__instance);
                EnsureDesignShipCountHeaderTooltip(__instance);
                RefreshViewedDesigns(__instance, !IsViewingForeignDesigns);
            }
            else
            {
                if (!isDesign)
                    designViewerPlayer = ExtraGameData.MainPlayer();

                HideDesignViewer();
                return;
            }

            foreach (var element in __instance.designUiByShip)
            {
                FleetWindow_ShipElementUI ui = element.Value;
                Ship ship = element.Key ?? ui?.CurrentShip;
                if (ui == null || ship == null)
                    continue;

                ui.CurrentShip = ship;
                Player designPlayer = GetCurrentDesignViewerPlayer();
                SetDesignShipCountText(ui, designPlayer, ship);
                SetDesignRowNameText(ui, ship);

                if (ui.Year != null)
                    ui.Year.text = $"{ShipDesignYear(ship)}";

                if (__instance.selectedElements.Count > 0 && __instance.selectedElements[0] == ui)
                    UpdateDesignSelectionActions(__instance, designPlayer, ship, !IsViewingForeignDesigns);
            }

            if (isDesign && IsViewingForeignDesigns)
                SetForeignDesignButtonsInteractable(__instance, false);

            if (isDesign && !IsViewingForeignDesigns)
                DisableDesignSelectionActionsIfNothingSelected(__instance);
        }
        catch (Exception ex)
        {
            Melon<UADVanillaPlusMod>.Logger.Warning($"Fleet window design viewer patch failed; leaving vanilla UI intact. {ex.GetType().Name}: {ex.Message}");
            try
            {
                HideDesignViewer();
            }
            catch
            {
            }
        }
    }
}

[HarmonyPatch(typeof(CampaignController))]
internal static class CampaignControllerDesignViewerDeletePatch
{
    [HarmonyPatch(nameof(CampaignController.DeleteDesign))]
    [HarmonyPrefix]
    private static void PrefixDeleteDesign(ref Ship ship)
    {
        Ship selected = CampaignFleetWindowDesignViewerPatch.SelectedViewedDesign;
        CampaignFleetWindow window = G.ui?.FleetWindow;
        if (selected == null || window == null || ship == selected)
            return;

        if (window.selectedElements != null &&
            window.selectedElements.Count > 0 &&
            window.selectedElements[0]?.CurrentShip == selected)
        {
            ship = selected;
        }
    }
}
