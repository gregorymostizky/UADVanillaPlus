using HarmonyLib;
using Il2Cpp;
using Il2CppTMPro;
using MelonLoader;
using UADVanillaPlus.GameData;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UADVanillaPlus.Harmony;

// Experimental campaign-map wrap illusion. This intentionally duplicates the
// rendered map surface and passive visual layers, then selectively proxies
// map interactions that can safely be mirrored into side-map space.
[HarmonyPatch(typeof(CampaignMap))]
internal static class CampaignMapWrapVisualPatch
{
    private const float MapLift = 0.052f;
    private const float CountryOverlayLift = 0.06f;
    private const float LabelLift = 0.08f;
    private const int MapRenderQueue = 5000;
    private const int CountryOverlayRenderQueue = 5100;
    private const int LabelRenderQueue = 5300;
    private const int OverlayRetryFrameInterval = 180;
    private const int DynamicCleanupFrameInterval = 300;
    private const int MovementDiagnosticFrameWindow = 900;
    private const int MovementDiagnosticsMaxPerClick = 1;
    private const float RoutingProxyEdgeOffset = 0.05f;

    private static readonly List<GameObject> WrapObjects = new();
    private static readonly Dictionary<IntPtr, DynamicMarkerCopySet> DynamicMarkerCopies = new();
    private static readonly Dictionary<IntPtr, RouteCopySet> RouteCopies = new();
    private static bool CreatedCountryOverlayCopies;
    private static bool LoggedDynamicMarkerCopies;
    private static bool LoggedRouteCopies;
    private static bool LastMapClickWasWrapped;
    private static float ActiveTaskForceMapOffset;
    private static Vector3 ActiveTaskForceVisualPosition;
    private static float LastMapClickSelectionOffset;
    private static Vector3 LastMapClickSelectionVisualPosition;
    private static Vector3 LastMapClickRaw;
    private static Vector3 LastMapClickNormalized;
    private static Vector3 LastMapClickDesired;
    private static Vector3 LastMapClickEffective;
    private static bool LastMapClickUsedRoutingProxy;
    private static int LastOverlayAttemptFrame;
    private static int LastDynamicCleanupFrame;
    private static int LastMapClickFrame = -MovementDiagnosticFrameWindow;
    private static int LastMapClickId;
    private static int MovementDiagnosticsForLastClick;

    [HarmonyPatch(nameof(CampaignMap.PostInit))]
    [HarmonyPostfix]
    private static void PostfixPostInit(CampaignMap __instance)
    {
        if (ModSettings.CampaignMapWraparoundEnabled)
            CreateWrapCopies(__instance);
    }

    [HarmonyPatch(nameof(CampaignMap.OnDestroy))]
    [HarmonyPostfix]
    private static void PostfixOnDestroy()
    {
        ClearWrapObjects();
    }

    internal static void ApplyCurrentSetting()
    {
        CampaignMap? map = CampaignMap.Instance;
        if (map == null || map.MapRenderer == null)
            return;

        if (ModSettings.CampaignMapWraparoundEnabled)
        {
            CreateWrapCopies(map);
            return;
        }

        SetHorizontalBorderRenderers(map.MapRenderer.transform.parent, true);
        ClearWrapObjects();
        Melon<UADVanillaPlusMod>.Logger.Msg("UADVP map wrap: disabled and restored vanilla horizontal borders.");
    }

    internal static void RuntimeUpdate()
    {
        if (!ModSettings.CampaignMapWraparoundEnabled || CampaignMap.Instance == null)
            return;

        SyncRouteCopyVisuals();

        if (Time.frameCount - LastDynamicCleanupFrame >= DynamicCleanupFrameInterval)
        {
            LastDynamicCleanupFrame = Time.frameCount;
            CleanupDynamicMarkerCopies();
            CleanupRouteCopies();
        }

        if (!CreatedCountryOverlayCopies && Time.frameCount - LastOverlayAttemptFrame >= OverlayRetryFrameInterval)
        {
            LastOverlayAttemptFrame = Time.frameCount;
            TryCreateDelayedCountryOverlayCopies(CampaignMap.Instance);
        }
    }

    internal static void SyncDynamicMarkerCopies(CampaignMapElement source, Vector3 vanillaUiPosition, float scale)
    {
        if (!ModSettings.CampaignMapWraparoundEnabled || source == null || source.Pointer == IntPtr.Zero || IsWrapClone(source.gameObject))
            return;

        CampaignMap? map = CampaignMap.Instance;
        MapUI? mapUi = map?.UIMap;
        if (map == null || mapUi == null || mapUi.UICanvas == null || !IsSupportedDynamicMarker(source, mapUi))
            return;

        float copySpacing = CampaignMap.mapWidth;
        if (copySpacing <= 0f)
            return;

        DynamicMarkerCopySet? copies = GetOrCreateDynamicMarkerCopies(source);
        if (copies == null)
            return;

        Vector3 sourceWorld = source.WorldPos;
        SyncDynamicMarkerCopy(copies.Negative, source, mapUi, sourceWorld, -copySpacing, scale);
        SyncDynamicMarkerCopy(copies.Positive, source, mapUi, sourceWorld, copySpacing, scale);
    }

    internal static void NormalizeWrappedMapClick(ref Vector3 position)
    {
        if (!ModSettings.CampaignMapWraparoundEnabled)
            return;

        Vector3 rawPosition = position;
        Vector3 normalizedPosition = NormalizeWrappedWorldPosition(position);
        Vector3 desiredPosition = ChooseWrappedMapClickPosition(rawPosition, normalizedPosition);
        Vector3 routingPosition = ChooseRoutingProxyPosition(desiredPosition);
        LogMapClickDiagnostic(rawPosition, normalizedPosition, desiredPosition, routingPosition);
        position = routingPosition;
    }

    internal static void MarkOriginalTaskForceSelection(ShipUI sourceShip)
    {
        if (!ModSettings.CampaignMapWraparoundEnabled || sourceShip == null || IsWrapClone(sourceShip.gameObject))
            return;

        ActiveTaskForceMapOffset = 0f;
        ActiveTaskForceVisualPosition = sourceShip.WorldPos;
    }

    internal static void SyncRouteCopies(Route source)
    {
        if (!ModSettings.CampaignMapWraparoundEnabled || source == null || source.Pointer == IntPtr.Zero)
            return;

        float copySpacing = CampaignMap.mapWidth;
        if (copySpacing <= 0f)
            return;

        RouteCopySet? copies = GetOrCreateRouteCopies(source);
        if (copies == null)
            return;

        SyncRouteCopy(source, copies.Negative, -copySpacing);
        SyncRouteCopy(source, copies.Positive, copySpacing);
    }

    internal static void PrepareMoveWindowShowMapToMap(CampaignController.TaskForce group, Vector3 from, ref Vector3 to, PathResult result)
    {
        if (!ModSettings.CampaignMapWraparoundEnabled)
            return;

        try
        {
            bool shouldLog = ShouldLogMovementDiagnostic();
            Vector3 routingTo = to;
            if (ShouldUseDesiredMoveDestination(to))
                to = LastMapClickDesired;

            bool pathExtended = ExtendWrappedMovePathToDestination(to, result, out Vector3 extendedFrom, out Vector3 extendedTo);
            if (!shouldLog)
                return;

            string groupText = group == null ? "null" : group.Id.ToString();
            Melon<UADVanillaPlusMod>.Logger.Msg(
                $"UADVP map wrap diag move {LastMapClickId}.{MovementDiagnosticsForLastClick}: " +
                $"clickWrapped={LastMapClickWasWrapped}, group={groupText}, from={FormatVector(from)}, to={FormatVector(to)}, " +
                $"rawClick={FormatVector(LastMapClickRaw)}, normalizedClick={FormatVector(LastMapClickNormalized)}, " +
                $"desiredClick={FormatVector(LastMapClickDesired)}, routingClick={FormatVector(LastMapClickEffective)}, " +
                $"originalTo={FormatVector(routingTo)}, proxy={LastMapClickUsedRoutingProxy}, " +
                $"selectionOffset={LastMapClickSelectionOffset:0.###}, selectionVisual={FormatVector(LastMapClickSelectionVisualPosition)}, " +
                $"pathExtended={pathExtended}, extendedFrom={FormatVector(extendedFrom)}, extendedTo={FormatVector(extendedTo)}, " +
                $"{FormatPathResult(result)}.");
        }
        catch (Exception ex)
        {
            Melon<UADVanillaPlusMod>.Logger.Warning($"UADVP map wrap move-window show diagnostic failed. {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void CreateWrapCopies(CampaignMap map)
    {
        ClearWrapObjects();

        if (map == null || map.MapRenderer == null)
            return;

        Renderer mapRenderer = map.MapRenderer;
        float copySpacing = mapRenderer.bounds.size.x;
        if (copySpacing <= 0f)
        {
            Melon<UADVanillaPlusMod>.Logger.Warning("UADVP map wrap skipped: map renderer bounds were not ready.");
            return;
        }

        GameObject mapObject = mapRenderer.gameObject;
        Mesh? mapMesh = mapObject.GetComponent<MeshFilter>()?.sharedMesh;
        if (mapMesh == null)
        {
            Melon<UADVanillaPlusMod>.Logger.Warning("UADVP map wrap skipped: map mesh was not ready.");
            return;
        }

        Texture? mapTexture = mapRenderer.sharedMaterial == null ? null : mapRenderer.sharedMaterial.mainTexture;
        if (mapTexture == null)
        {
            Melon<UADVanillaPlusMod>.Logger.Warning("UADVP map wrap skipped: map texture was not ready.");
            return;
        }

        SetHorizontalBorderRenderers(mapObject.transform.parent, false);
        CreateMapCopy(mapRenderer, mapObject, mapMesh, mapTexture, -copySpacing, "NegativeX");
        CreateMapCopy(mapRenderer, mapObject, mapMesh, mapTexture, copySpacing, "PositiveX");
        CreateNativeGridCopies(map, copySpacing);
        CreateStaticVisualLayerCopies(map, copySpacing);

        Melon<UADVanillaPlusMod>.Logger.Msg($"UADVP map wrap: enabled visual copies at +/-{copySpacing:0.###} world x-units.");
    }

    private static void CreateMapCopy(Renderer sourceRenderer, GameObject sourceObject, Mesh sourceMesh, Texture sourceTexture, float xOffset, string sideName)
    {
        GameObject copy = new($"UADVP_WrapMap_{sideName}");
        copy.layer = sourceObject.layer;
        copy.tag = sourceObject.tag;
        copy.SetActive(sourceObject.activeSelf);
        copy.transform.SetParent(sourceObject.transform.parent, false);
        copy.transform.localPosition = sourceObject.transform.localPosition;
        copy.transform.localRotation = sourceObject.transform.localRotation;
        copy.transform.localScale = sourceObject.transform.localScale;

        Vector3 position = copy.transform.position;
        position.x += xOffset;
        position.y += MapLift;
        copy.transform.position = position;

        MeshFilter copyFilter = copy.AddComponent<MeshFilter>();
        copyFilter.sharedMesh = sourceMesh;

        MeshRenderer copyRenderer = copy.AddComponent<MeshRenderer>();
        copyRenderer.enabled = sourceRenderer.enabled;
        copyRenderer.sharedMaterial = CreateUnlitTextureMaterial(sourceTexture, $"{copy.name}_Material", MapRenderQueue);

        AddMapRaycastCollider(copy, sourceMesh);

        WrapObjects.Add(copy);
    }

    private static void AddMapRaycastCollider(GameObject copy, Mesh sourceMesh)
    {
        MeshCollider copyCollider = copy.AddComponent<MeshCollider>();
        copyCollider.sharedMesh = sourceMesh;
        copyCollider.convex = false;
        copyCollider.isTrigger = false;
    }

    private static Material CreateUnlitTextureMaterial(Texture texture, string name, int renderQueue)
    {
        Shader shader = FindFirstShader(
            "Unlit/Texture",
            "Legacy Shaders/Unlit/Texture",
            "Unlit/Transparent",
            "Particles/Standard Unlit",
            "Sprites/Default",
            "Standard");

        Material material = new(shader)
        {
            name = name,
            renderQueue = renderQueue
        };

        material.mainTexture = texture;
        if (material.HasProperty("_MainTex"))
            material.SetTexture("_MainTex", texture);
        if (material.HasProperty("_BaseMap"))
            material.SetTexture("_BaseMap", texture);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", Color.white);
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", Color.white);
        if (material.HasProperty("_Glossiness"))
            material.SetFloat("_Glossiness", 0f);
        if (material.HasProperty("_Metallic"))
            material.SetFloat("_Metallic", 0f);

        return material;
    }

    private static void CreateNativeGridCopies(CampaignMap map, float copySpacing)
    {
        Transform? grid = map.transform.root.Find("WorldEx/MapVisualGrid");
        if (grid == null)
        {
            Melon<UADVanillaPlusMod>.Logger.Warning("UADVP map wrap: native grid not found.");
            return;
        }

        CreateOffsetClone(grid.gameObject, -copySpacing, "UADVP_WrapNativeGrid_NegativeX", 0f, null);
        CreateOffsetClone(grid.gameObject, copySpacing, "UADVP_WrapNativeGrid_PositiveX", 0f, null);
    }

    private static void CreateStaticVisualLayerCopies(CampaignMap map, float copySpacing)
    {
        // MapVisualGrid is cloned separately at its native render queue. The
        // label roots are raised over the copied map and country overlays.
        CreateStaticVisualLayerCopies(map.LabelsAreaRoot, copySpacing, "AreaLabels");
        CreateStaticVisualLayerCopies(map.LabelsProvincesRoot, copySpacing, "ProvinceLabels");
    }

    private static void CreateStaticVisualLayerCopies(Transform? sourceRoot, float copySpacing, string layerName)
    {
        if (sourceRoot == null)
        {
            Melon<UADVanillaPlusMod>.Logger.Warning($"UADVP map wrap: {layerName} root not found.");
            return;
        }

        CreateOffsetClone(sourceRoot.gameObject, -copySpacing, $"UADVP_WrapStatic_NegativeX_{layerName}", LabelLift, LabelRenderQueue);
        CreateOffsetClone(sourceRoot.gameObject, copySpacing, $"UADVP_WrapStatic_PositiveX_{layerName}", LabelLift, LabelRenderQueue);
    }

    private static GameObject CreateOffsetClone(GameObject source, float xOffset, string name, float yLift, int? renderQueue)
    {
        GameObject copy = UnityEngine.Object.Instantiate(source, source.transform.parent);
        copy.name = name;
        copy.transform.localPosition = source.transform.localPosition;
        copy.transform.localRotation = source.transform.localRotation;
        copy.transform.localScale = source.transform.localScale;

        Vector3 position = source.transform.position;
        position.x += xOffset;
        position.y += yLift;
        copy.transform.position = position;

        if (renderQueue.HasValue)
            RaiseRenderQueues(copy, renderQueue.Value);

        DisableNonRenderingBehaviours(copy);
        DisableColliders(copy);

        WrapObjects.Add(copy);
        return copy;
    }

    private static DynamicMarkerCopySet? GetOrCreateDynamicMarkerCopies(CampaignMapElement source)
    {
        IntPtr key = source.Pointer;
        if (DynamicMarkerCopies.TryGetValue(key, out DynamicMarkerCopySet? existing) &&
            existing.Source != null &&
            existing.Negative != null &&
            existing.Positive != null)
        {
            return existing;
        }

        if (existing != null)
            DestroyDynamicMarkerCopySet(existing);

        DynamicMarkerKind kind = DynamicMarkerKindFor(source);
        GameObject? negative = CreateDynamicMarkerCopy(source, "NegativeX", kind, -CampaignMap.mapWidth);
        GameObject? positive = CreateDynamicMarkerCopy(source, "PositiveX", kind, CampaignMap.mapWidth);
        if (negative == null || positive == null)
        {
            if (negative != null)
                UnityEngine.Object.Destroy(negative);
            if (positive != null)
                UnityEngine.Object.Destroy(positive);
            return null;
        }

        DynamicMarkerCopySet created = new(source, negative, positive);
        DynamicMarkerCopies[key] = created;
        WrapObjects.Add(negative);
        WrapObjects.Add(positive);

        if (!LoggedDynamicMarkerCopies)
        {
            LoggedDynamicMarkerCopies = true;
            Melon<UADVanillaPlusMod>.Logger.Msg("UADVP map wrap: syncing wrapped dynamic marker copies.");
        }

        return created;
    }

    private static GameObject? CreateDynamicMarkerCopy(CampaignMapElement source, string sideName, DynamicMarkerKind kind, float xOffset)
    {
        if (source?.gameObject == null || source.transform.parent == null)
            return null;

        GameObject copy = UnityEngine.Object.Instantiate(source.gameObject, source.transform.parent);
        copy.name = $"UADVP_WrapDynamic_{sideName}_{kind}_{source.gameObject.name}";
        copy.SetActive(true);
        copy.transform.SetSiblingIndex(source.transform.GetSiblingIndex());
        DisableDynamicCloneInteractivity(copy);
        DisableColliders(copy);
        if (kind == DynamicMarkerKind.Port)
            ConfigurePortCloneClickProxy(source, copy);
        else if (kind == DynamicMarkerKind.TaskForce)
            ConfigureTaskForceCloneClickProxy(source, copy, xOffset);
        else if (kind == DynamicMarkerKind.Event)
            ConfigureEventCloneClickProxy(source, copy);

        return copy;
    }

    private static void SyncDynamicMarkerCopy(GameObject copy, CampaignMapElement source, MapUI mapUi, Vector3 sourceWorld, float xOffset, float scale)
    {
        if (copy == null || source == null)
            return;

        Vector3 wrappedWorld = sourceWorld;
        wrappedWorld.x += xOffset;

        Vector3 wrappedUiPosition = mapUi.WorldToUISpace(mapUi.UICanvas, wrappedWorld);

        copy.SetActive(true);

        if (copy.TryGetComponent(out ShipUI shipCopy))
        {
            shipCopy.offset = source.TryCast<ShipUI>()?.offset ?? shipCopy.offset;
            shipCopy.UpdatePositionScale(wrappedUiPosition, scale);
            SyncDynamicCloneVisuals(copy, source.gameObject, xOffset);
            return;
        }

        CampaignMapElement? copyElement = copy.GetComponent<CampaignMapElement>();
        if (copyElement != null)
        {
            copyElement.UpdatePositionScale(wrappedUiPosition, scale);
            SyncDynamicCloneVisuals(copy, source.gameObject, xOffset);
        }
    }

    private static bool IsSupportedDynamicMarker(CampaignMapElement source, MapUI mapUi)
    {
        if (source.TryCast<PortUI>() != null)
            return IsChildOf(source.transform, mapUi.PortsRoot);

        if (source.TryCast<ShipUI>() != null)
            return IsChildOf(source.transform, mapUi.ShipsRoot);

        if (source.TryCast<EventUI>() != null)
            return IsChildOf(source.transform, mapUi.EventsRoot);

        return false;
    }

    private static DynamicMarkerKind DynamicMarkerKindFor(CampaignMapElement source)
    {
        if (source.TryCast<PortUI>() != null)
            return DynamicMarkerKind.Port;

        if (source.TryCast<ShipUI>() != null)
            return DynamicMarkerKind.TaskForce;

        if (source.TryCast<EventUI>() != null)
            return DynamicMarkerKind.Event;

        return DynamicMarkerKind.Unknown;
    }

    private static bool IsChildOf(Transform? child, Transform? parent)
    {
        if (child == null || parent == null)
            return false;

        Transform? current = child;
        while (current != null)
        {
            if (current == parent)
                return true;

            current = current.parent;
        }

        return false;
    }

    private static bool IsWrapClone(GameObject? gameObject)
    {
        return gameObject != null && gameObject.name.StartsWith("UADVP_WrapDynamic_", StringComparison.Ordinal);
    }

    private static Vector3 NormalizeWrappedWorldPosition(Vector3 position)
    {
        CampaignMap? map = CampaignMap.Instance;
        Renderer? renderer = map?.MapRenderer;
        float width = CampaignMap.mapWidth > 0f ? CampaignMap.mapWidth : renderer?.bounds.size.x ?? 0f;
        if (renderer == null || width <= 0f)
            return position;

        float minX = renderer.bounds.min.x;
        float maxX = renderer.bounds.max.x;
        while (position.x < minX)
            position.x += width;
        while (position.x > maxX)
            position.x -= width;

        return position;
    }

    private static Vector3 ChooseWrappedMapClickPosition(Vector3 rawPosition, Vector3 normalizedPosition)
    {
        LastMapClickSelectionOffset = ActiveTaskForceMapOffset;
        LastMapClickSelectionVisualPosition = ActiveTaskForceVisualPosition;
        if (Mathf.Abs(ActiveTaskForceMapOffset) > 0.01f)
            return ChooseNearestSelectedCopyDestination(normalizedPosition);

        if (Mathf.Abs(rawPosition.x - normalizedPosition.x) <= 0.01f)
            return normalizedPosition;

        // Keep side-map movement internally consistent: the path, dialog, and
        // final Move command should all see the visual copy coordinate.
        return rawPosition;
    }

    private static Vector3 ChooseNearestSelectedCopyDestination(Vector3 normalizedPosition)
    {
        float mapWidth = CampaignMap.mapWidth;
        if (mapWidth <= 0f)
            return normalizedPosition;

        Vector3 bestVisualPosition = normalizedPosition;
        float bestDistance = float.MaxValue;
        for (int copyIndex = -1; copyIndex <= 1; copyIndex++)
        {
            Vector3 candidate = normalizedPosition;
            candidate.x += mapWidth * copyIndex;
            float distance = Vector3.SqrMagnitude(candidate - ActiveTaskForceVisualPosition);
            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            bestVisualPosition = candidate;
        }

        bestVisualPosition.x -= ActiveTaskForceMapOffset;
        return bestVisualPosition;
    }

    private static Vector3 ChooseRoutingProxyPosition(Vector3 desiredPosition)
    {
        LastMapClickUsedRoutingProxy = false;
        Renderer? renderer = CampaignMap.Instance?.MapRenderer;
        if (renderer == null)
            return desiredPosition;

        float minX = renderer.bounds.min.x;
        float maxX = renderer.bounds.max.x;
        Vector3 routingPosition = desiredPosition;
        if (desiredPosition.x > maxX + 0.01f)
        {
            routingPosition.x = maxX + RoutingProxyEdgeOffset;
            LastMapClickUsedRoutingProxy = true;
        }
        else if (desiredPosition.x < minX - 0.01f)
        {
            routingPosition.x = minX - RoutingProxyEdgeOffset;
            LastMapClickUsedRoutingProxy = true;
        }

        return routingPosition;
    }

    private static bool ShouldUseDesiredMoveDestination(Vector3 to)
    {
        if (!LastMapClickUsedRoutingProxy || Time.frameCount - LastMapClickFrame > MovementDiagnosticFrameWindow)
            return false;

        return Vector3.Distance(to, LastMapClickEffective) <= 0.25f;
    }

    private static bool ExtendWrappedMovePathToDestination(Vector3 destination, PathResult result, out Vector3 extendedFrom, out Vector3 extendedTo)
    {
        extendedFrom = default;
        extendedTo = default;

        try
        {
            Il2CppSystem.Collections.Generic.List<Vector3>? fullPath = result.FullPath;
            if (fullPath == null || fullPath.Count == 0)
                return false;

            Renderer? renderer = CampaignMap.Instance?.MapRenderer;
            if (renderer == null)
                return false;

            float minX = renderer.bounds.min.x;
            float maxX = renderer.bounds.max.x;
            bool destinationBeyondRight = destination.x > maxX + 0.01f;
            bool destinationBeyondLeft = destination.x < minX - 0.01f;
            if (!destinationBeyondRight && !destinationBeyondLeft)
                return false;

            Vector3 last = fullPath[fullPath.Count - 1];
            bool pathStopsAtRightEdge = destinationBeyondRight && last.x >= maxX - 0.25f;
            bool pathStopsAtLeftEdge = destinationBeyondLeft && last.x <= minX + 0.25f;
            if (!pathStopsAtRightEdge && !pathStopsAtLeftEdge)
                return false;

            Vector3 endpoint = destination;
            endpoint.y = last.y;
            if (Vector3.Distance(last, endpoint) <= 0.05f)
                return false;

            fullPath.Add(endpoint);
            extendedFrom = last;
            extendedTo = endpoint;
            return true;
        }
        catch (Exception ex)
        {
            Melon<UADVanillaPlusMod>.Logger.Warning($"UADVP map wrap path extension failed. {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    private static void LogMapClickDiagnostic(Vector3 rawPosition, Vector3 normalizedPosition, Vector3 desiredPosition, Vector3 effectivePosition)
    {
        LastMapClickId++;
        LastMapClickFrame = Time.frameCount;
        LastMapClickRaw = rawPosition;
        LastMapClickNormalized = normalizedPosition;
        LastMapClickDesired = desiredPosition;
        LastMapClickEffective = effectivePosition;
        LastMapClickWasWrapped = Mathf.Abs(rawPosition.x - normalizedPosition.x) > 0.01f;
        MovementDiagnosticsForLastClick = 0;

        Renderer? renderer = CampaignMap.Instance?.MapRenderer;
        string boundsText = renderer == null ? "n/a" : $"{renderer.bounds.min.x:0.###}..{renderer.bounds.max.x:0.###}";

        Melon<UADVanillaPlusMod>.Logger.Msg(
            $"UADVP map wrap diag click {LastMapClickId}: wrapped={LastMapClickWasWrapped}, " +
            $"raw={FormatVector(rawPosition)}, normalized={FormatVector(normalizedPosition)}, desired={FormatVector(desiredPosition)}, " +
            $"effective={FormatVector(effectivePosition)}, proxy={LastMapClickUsedRoutingProxy}, " +
            $"selectionOffset={LastMapClickSelectionOffset:0.###}, selectionVisual={FormatVector(LastMapClickSelectionVisualPosition)}, " +
            $"xDelta={(rawPosition.x - normalizedPosition.x):0.###}, mapWidth={CampaignMap.mapWidth:0.###}, boundsX={boundsText}.");
    }

    private static bool ShouldLogMovementDiagnostic()
    {
        if (Time.frameCount - LastMapClickFrame > MovementDiagnosticFrameWindow || MovementDiagnosticsForLastClick >= MovementDiagnosticsMaxPerClick)
            return false;

        MovementDiagnosticsForLastClick++;
        return true;
    }

    private static string FormatPathResult(PathResult result)
        => $"pathLength={result.PathLength:0.###}, " +
           $"fullCount={SafeVectorListCount(result.FullPath)}, " +
           $"simplifiedCount={SafeVectorListCount(result.SimplifiedPath)}, " +
           $"checkFullCount={SafeNestedVectorListCount(result.CheckFullPath)}, " +
           $"fullFirst={SafeVectorListPoint(result.FullPath, true)}, " +
           $"fullLast={SafeVectorListPoint(result.FullPath, false)}";

    private static string SafeVectorListCount(List<Vector3>? list)
    {
        try
        {
            return list == null ? "null" : list.Count.ToString();
        }
        catch (Exception ex)
        {
            return $"error:{ex.GetType().Name}";
        }
    }

    private static string SafeVectorListCount(Il2CppSystem.Collections.Generic.List<Vector3>? list)
    {
        try
        {
            return list == null ? "null" : list.Count.ToString();
        }
        catch (Exception ex)
        {
            return $"error:{ex.GetType().Name}";
        }
    }

    private static string SafeNestedVectorListCount(List<List<Vector3>>? list)
    {
        try
        {
            return list == null ? "null" : list.Count.ToString();
        }
        catch (Exception ex)
        {
            return $"error:{ex.GetType().Name}";
        }
    }

    private static string SafeNestedVectorListCount(Il2CppSystem.Collections.Generic.List<Il2CppSystem.Collections.Generic.List<Vector3>>? list)
    {
        try
        {
            return list == null ? "null" : list.Count.ToString();
        }
        catch (Exception ex)
        {
            return $"error:{ex.GetType().Name}";
        }
    }

    private static string SafeVectorListPoint(List<Vector3>? list, bool first)
    {
        try
        {
            if (list == null || list.Count == 0)
                return "n/a";

            return FormatVector(first ? list[0] : list[list.Count - 1]);
        }
        catch (Exception ex)
        {
            return $"error:{ex.GetType().Name}";
        }
    }

    private static string SafeVectorListPoint(Il2CppSystem.Collections.Generic.List<Vector3>? list, bool first)
    {
        try
        {
            if (list == null || list.Count == 0)
                return "n/a";

            return FormatVector(first ? list[0] : list[list.Count - 1]);
        }
        catch (Exception ex)
        {
            return $"error:{ex.GetType().Name}";
        }
    }

    private static string FormatVector(Vector3 value)
        => $"({value.x:0.###},{value.y:0.###},{value.z:0.###})";

    private static int DisableDynamicCloneInteractivity(GameObject root)
    {
        int disabled = 0;

        Graphic[] graphics = root.GetComponentsInChildren<Graphic>(true);
        foreach (Graphic graphic in graphics)
        {
            if (graphic != null)
                graphic.raycastTarget = false;
        }

        CanvasGroup[] canvasGroups = root.GetComponentsInChildren<CanvasGroup>(true);
        foreach (CanvasGroup canvasGroup in canvasGroups)
        {
            if (canvasGroup == null)
                continue;

            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }

        Behaviour[] behaviours = root.GetComponentsInChildren<Behaviour>(true);
        foreach (Behaviour behaviour in behaviours)
        {
            if (behaviour == null || IsDynamicCloneVisualBehaviour(behaviour))
                continue;

            behaviour.enabled = false;
            disabled++;
        }

        return disabled;
    }

    private static void ConfigurePortCloneClickProxy(CampaignMapElement source, GameObject copy)
    {
        PortUI? sourcePort = source.TryCast<PortUI>();
        PortUI? copyPort = copy.GetComponent<PortUI>();
        Button? sourceButton = sourcePort?.PortButton;
        Button? proxyButton = copyPort?.PortButton ?? copy.GetComponentInChildren<Button>(true);
        if (sourcePort == null || sourceButton == null || proxyButton == null)
            return;

        EnableProxyButtonRaycastPath(copy, proxyButton);

        proxyButton.onClick.RemoveAllListeners();
        proxyButton.onClick.AddListener(new System.Action(() => ProxyPortClick(sourcePort, null)));

        OnClickH clickProxy = proxyButton.gameObject.AddComponent<OnClickH>();
        clickProxy.action = new System.Action<PointerEventData>(eventData => ProxyPortClick(sourcePort, eventData));

        OnEnter enterProxy = proxyButton.gameObject.AddComponent<OnEnter>();
        enterProxy.action = new System.Action(() => ProxyPortEnter(sourcePort));

        OnLeave leaveProxy = proxyButton.gameObject.AddComponent<OnLeave>();
        leaveProxy.action = new System.Action(() => ProxyPortLeave(sourcePort));
    }

    private static void ConfigureTaskForceCloneClickProxy(CampaignMapElement source, GameObject copy, float xOffset)
    {
        ShipUI? sourceShip = source.TryCast<ShipUI>();
        ShipUI? copyShip = copy.GetComponent<ShipUI>();
        Button? sourceButton = sourceShip?.Btn;
        Button? proxyButton = copyShip?.Btn ?? copy.GetComponentInChildren<Button>(true);
        if (sourceShip == null || sourceButton == null || proxyButton == null)
            return;

        EnableProxyButtonRaycastPath(copy, proxyButton);

        proxyButton.onClick.RemoveAllListeners();
        proxyButton.onClick.AddListener(new System.Action(() => ProxyTaskForceClick(sourceShip, null, xOffset)));

        OnClickH clickProxy = proxyButton.gameObject.AddComponent<OnClickH>();
        clickProxy.action = new System.Action<PointerEventData>(eventData => ProxyTaskForceClick(sourceShip, eventData, xOffset));

        OnEnter enterProxy = proxyButton.gameObject.AddComponent<OnEnter>();
        enterProxy.action = new System.Action(() => ProxyTaskForceEnter(sourceShip));

        OnLeave leaveProxy = proxyButton.gameObject.AddComponent<OnLeave>();
        leaveProxy.action = new System.Action(() => ProxyTaskForceLeave(sourceShip));
    }

    private static void ConfigureEventCloneClickProxy(CampaignMapElement source, GameObject copy)
    {
        EventUI? sourceEvent = source.TryCast<EventUI>();
        EventUI? copyEvent = copy.GetComponent<EventUI>();
        Button? sourceButton = sourceEvent?.Btn;
        Button? proxyButton = copyEvent?.Btn ?? copy.GetComponentInChildren<Button>(true);
        if (sourceEvent == null || sourceButton == null || proxyButton == null)
            return;

        EnableProxyButtonRaycastPath(copy, proxyButton);

        proxyButton.onClick.RemoveAllListeners();
        proxyButton.onClick.AddListener(new System.Action(() => ProxyEventClick(sourceEvent, null)));

        OnClickH clickProxy = proxyButton.gameObject.AddComponent<OnClickH>();
        clickProxy.action = new System.Action<PointerEventData>(eventData => ProxyEventClick(sourceEvent, eventData));

        OnEnter enterProxy = proxyButton.gameObject.AddComponent<OnEnter>();
        enterProxy.action = new System.Action(() => ProxyEventEnter(sourceEvent));

        OnLeave leaveProxy = proxyButton.gameObject.AddComponent<OnLeave>();
        leaveProxy.action = new System.Action(() => ProxyEventLeave(sourceEvent));
    }

    private static void EnableProxyButtonRaycastPath(GameObject copy, Button proxyButton)
    {
        CanvasGroup[] canvasGroups = copy.GetComponentsInChildren<CanvasGroup>(true);
        foreach (CanvasGroup canvasGroup in canvasGroups)
        {
            if (canvasGroup == null)
                continue;

            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
        }

        proxyButton.enabled = true;
        proxyButton.interactable = true;

        Graphic? targetGraphic = proxyButton.targetGraphic;
        if (targetGraphic != null)
            targetGraphic.raycastTarget = true;

        Graphic[] buttonGraphics = proxyButton.GetComponentsInChildren<Graphic>(true);
        foreach (Graphic graphic in buttonGraphics)
        {
            if (graphic != null)
                graphic.raycastTarget = true;
        }
    }

    private static void ProxyPortClick(PortUI sourcePort, PointerEventData? eventData)
    {
        Button? sourceButton = sourcePort?.PortButton;
        if (sourceButton == null)
            return;

        OnClickH? sourceClick = sourceButton.GetComponent<OnClickH>();
        if (sourceClick?.action != null && eventData != null)
        {
            sourceClick.action.Invoke(eventData);
            return;
        }

        sourceButton.onClick.Invoke();
    }

    private static void ProxyPortEnter(PortUI sourcePort)
    {
        Button? sourceButton = sourcePort?.PortButton;
        OnEnter? sourceEnter = sourceButton?.GetComponent<OnEnter>();
        sourceEnter?.action?.Invoke();
    }

    private static void ProxyPortLeave(PortUI sourcePort)
    {
        Button? sourceButton = sourcePort?.PortButton;
        OnLeave? sourceLeave = sourceButton?.GetComponent<OnLeave>();
        sourceLeave?.action?.Invoke();
    }

    private static void ProxyTaskForceClick(ShipUI sourceShip, PointerEventData? eventData, float xOffset)
    {
        if (sourceShip == null)
            return;

        Button? sourceButton = sourceShip.Btn;
        if (sourceButton == null)
            return;

        OnClickH? sourceClick = sourceButton.GetComponent<OnClickH>();
        if (sourceClick?.action != null && eventData != null)
        {
            sourceClick.action.Invoke(eventData);
            MarkWrappedTaskForceSelection(sourceShip, xOffset);
            return;
        }

        if (eventData != null)
            sourceShip.OnPointerClick(eventData);

        sourceButton.onClick.Invoke();
        MarkWrappedTaskForceSelection(sourceShip, xOffset);
    }

    private static void MarkWrappedTaskForceSelection(ShipUI sourceShip, float xOffset)
    {
        ActiveTaskForceMapOffset = xOffset;
        ActiveTaskForceVisualPosition = sourceShip.WorldPos;
        ActiveTaskForceVisualPosition.x += xOffset;
    }

    private static void ProxyTaskForceEnter(ShipUI sourceShip)
    {
        Button? sourceButton = sourceShip == null ? null : sourceShip.Btn;
        OnEnter? sourceEnter = sourceButton?.GetComponent<OnEnter>();
        sourceEnter?.action?.Invoke();
        if (sourceShip != null)
            SyncRouteCopies(sourceShip.Route);
    }

    private static void ProxyTaskForceLeave(ShipUI sourceShip)
    {
        Button? sourceButton = sourceShip == null ? null : sourceShip.Btn;
        OnLeave? sourceLeave = sourceButton?.GetComponent<OnLeave>();
        sourceLeave?.action?.Invoke();
        if (sourceShip != null)
            SyncRouteCopies(sourceShip.Route);
    }

    private static void ProxyEventClick(EventUI sourceEvent, PointerEventData? eventData)
    {
        Button? sourceButton = sourceEvent?.Btn;
        if (sourceButton == null)
            return;

        OnClickH? sourceClick = sourceButton.GetComponent<OnClickH>();
        if (sourceClick?.action != null && eventData != null)
        {
            sourceClick.action.Invoke(eventData);
            return;
        }

        sourceButton.onClick.Invoke();
    }

    private static void ProxyEventEnter(EventUI sourceEvent)
    {
        Button? sourceButton = sourceEvent == null ? null : sourceEvent.Btn;
        OnEnter? sourceEnter = sourceButton?.GetComponent<OnEnter>();
        sourceEnter?.action?.Invoke();
    }

    private static void ProxyEventLeave(EventUI sourceEvent)
    {
        Button? sourceButton = sourceEvent == null ? null : sourceEvent.Btn;
        OnLeave? sourceLeave = sourceButton?.GetComponent<OnLeave>();
        sourceLeave?.action?.Invoke();
    }

    private static RouteCopySet? GetOrCreateRouteCopies(Route source)
    {
        IntPtr key = source.Pointer;
        if (RouteCopies.TryGetValue(key, out RouteCopySet? existing) &&
            existing.Source != null &&
            existing.Negative != null &&
            existing.Positive != null)
        {
            return existing;
        }

        if (existing != null)
            DestroyRouteCopySet(existing);

        Route? negative = CreateRouteCopy(source, "NegativeX");
        Route? positive = CreateRouteCopy(source, "PositiveX");
        if (negative == null || positive == null)
        {
            if (negative != null)
                UnityEngine.Object.Destroy(negative.gameObject);
            if (positive != null)
                UnityEngine.Object.Destroy(positive.gameObject);
            return null;
        }

        RouteCopySet created = new(source, negative, positive);
        RouteCopies[key] = created;
        WrapObjects.Add(negative.gameObject);
        WrapObjects.Add(positive.gameObject);

        if (!LoggedRouteCopies)
        {
            LoggedRouteCopies = true;
            Melon<UADVanillaPlusMod>.Logger.Msg("UADVP map wrap: syncing wrapped route copies.");
        }

        return created;
    }

    private static Route? CreateRouteCopy(Route source, string sideName)
    {
        if (source?.gameObject == null || source.transform.parent == null)
            return null;

        GameObject copyObject = UnityEngine.Object.Instantiate(source.gameObject, source.transform.parent);
        copyObject.name = $"UADVP_WrapRoute_{sideName}_{source.gameObject.name}";
        DisableColliders(copyObject);
        Route? copy = copyObject.GetComponent<Route>();
        if (copy == null)
        {
            UnityEngine.Object.Destroy(copyObject);
            return null;
        }

        return copy;
    }

    private static void SyncRouteCopyVisuals()
    {
        if (RouteCopies.Count == 0)
            return;

        float copySpacing = CampaignMap.mapWidth;
        if (copySpacing <= 0f)
            return;

        foreach (RouteCopySet copies in RouteCopies.Values)
        {
            if (copies.Source == null || copies.Negative == null || copies.Positive == null)
                continue;

            SyncRouteCopy(copies.Source, copies.Negative, -copySpacing);
            SyncRouteCopy(copies.Source, copies.Positive, copySpacing);
        }
    }

    private static void SyncRouteCopy(Route source, Route copy, float xOffset)
    {
        if (source == null || copy == null)
            return;

        copy.gameObject.SetActive(source.gameObject.activeSelf);
        copy.transform.localRotation = source.transform.localRotation;
        copy.transform.localScale = source.transform.localScale;

        Vector3 sourcePosition = source.transform.position;
        sourcePosition.x += xOffset;
        copy.transform.position = sourcePosition;

        SyncLineRenderer(source.RouteLine, copy.RouteLine, xOffset);
        SyncLineRenderer(source.AdditionalLine, copy.AdditionalLine, xOffset);
        SyncRouteDestination(source, copy, xOffset);
    }

    private static void SyncLineRenderer(LineRenderer source, LineRenderer copy, float xOffset)
    {
        if (source == null || copy == null)
            return;

        copy.enabled = source.enabled;
        copy.useWorldSpace = source.useWorldSpace;
        copy.positionCount = source.positionCount;
        copy.sharedMaterial = source.sharedMaterial;
        copy.gameObject.SetActive(source.gameObject.activeSelf);

        for (int i = 0; i < source.positionCount; i++)
        {
            Vector3 position = source.GetPosition(i);
            position.x += xOffset;
            copy.SetPosition(i, position);
        }
    }

    private static void SyncRouteDestination(Route source, Route copy, float xOffset)
    {
        if (source.Destination == null || copy.Destination == null)
            return;

        copy.Destination.SetActive(source.Destination.activeSelf);
        Vector3 position = source.Destination.transform.position;
        position.x += xOffset;
        copy.Destination.transform.position = position;
        copy.Destination.transform.localRotation = source.Destination.transform.localRotation;
        copy.Destination.transform.localScale = source.Destination.transform.localScale;

        if (source.DestinationRenderer != null && copy.DestinationRenderer != null)
        {
            copy.DestinationRenderer.enabled = source.DestinationRenderer.enabled;
            copy.DestinationRenderer.sharedMaterial = source.DestinationRenderer.sharedMaterial;
        }
    }

    private static void SyncDynamicCloneVisuals(GameObject copy, GameObject source, float xOffset)
    {
        SyncDynamicCloneGraphics(copy, source);
        SyncDynamicCloneTextLayout(copy, source);
        SyncDynamicCloneLineRenderers(copy, source, xOffset);
    }

    private static void SyncDynamicCloneGraphics(GameObject copy, GameObject source)
    {
        Graphic[] sourceGraphics = source.GetComponentsInChildren<Graphic>(true);
        Graphic[] copyGraphics = copy.GetComponentsInChildren<Graphic>(true);
        int count = Mathf.Min(sourceGraphics.Length, copyGraphics.Length);

        for (int i = 0; i < count; i++)
        {
            Graphic sourceGraphic = sourceGraphics[i];
            Graphic copyGraphic = copyGraphics[i];
            if (sourceGraphic == null || copyGraphic == null)
                continue;

            copyGraphic.enabled = sourceGraphic.enabled;
            copyGraphic.color = sourceGraphic.color;
            copyGraphic.material = sourceGraphic.material;

            Image? sourceImage = sourceGraphic.TryCast<Image>();
            Image? copyImage = copyGraphic.TryCast<Image>();
            if (sourceImage == null || copyImage == null)
                continue;

            copyImage.sprite = sourceImage.sprite;
            copyImage.overrideSprite = sourceImage.overrideSprite;
            copyImage.type = sourceImage.type;
            copyImage.preserveAspect = sourceImage.preserveAspect;
            copyImage.fillCenter = sourceImage.fillCenter;
            copyImage.fillMethod = sourceImage.fillMethod;
            copyImage.fillOrigin = sourceImage.fillOrigin;
            copyImage.fillAmount = sourceImage.fillAmount;
            copyImage.fillClockwise = sourceImage.fillClockwise;
        }
    }

    private static void SyncDynamicCloneLineRenderers(GameObject copy, GameObject source, float xOffset)
    {
        LineRenderer[] sourceLines = source.GetComponentsInChildren<LineRenderer>(true);
        LineRenderer[] copyLines = copy.GetComponentsInChildren<LineRenderer>(true);
        int count = Mathf.Min(sourceLines.Length, copyLines.Length);

        for (int i = 0; i < count; i++)
        {
            LineRenderer sourceLine = sourceLines[i];
            LineRenderer copyLine = copyLines[i];
            if (sourceLine == null || copyLine == null)
                continue;

            copyLine.gameObject.SetActive(sourceLine.gameObject.activeSelf);
            copyLine.enabled = sourceLine.enabled;
            copyLine.useWorldSpace = sourceLine.useWorldSpace;
            copyLine.positionCount = sourceLine.positionCount;
            copyLine.sharedMaterial = sourceLine.sharedMaterial;
            copyLine.startWidth = sourceLine.startWidth;
            copyLine.endWidth = sourceLine.endWidth;
            copyLine.startColor = sourceLine.startColor;
            copyLine.endColor = sourceLine.endColor;

            for (int point = 0; point < sourceLine.positionCount; point++)
            {
                Vector3 position = sourceLine.GetPosition(point);
                if (sourceLine.useWorldSpace)
                    position.x += xOffset;
                copyLine.SetPosition(point, position);
            }
        }
    }

    private static void SyncDynamicCloneTextLayout(GameObject copy, GameObject source)
    {
        TMP_Text[] sourceTexts = source.GetComponentsInChildren<TMP_Text>(true);
        TMP_Text[] copyTexts = copy.GetComponentsInChildren<TMP_Text>(true);
        int count = Mathf.Min(sourceTexts.Length, copyTexts.Length);

        for (int i = 0; i < count; i++)
        {
            TMP_Text sourceText = sourceTexts[i];
            TMP_Text copyText = copyTexts[i];
            if (sourceText == null || copyText == null)
                continue;

            copyText.enabled = sourceText.enabled;
            copyText.text = sourceText.text;
            copyText.alignment = sourceText.alignment;
            copyText.fontSize = sourceText.fontSize;
            copyText.fontSizeMin = sourceText.fontSizeMin;
            copyText.fontSizeMax = sourceText.fontSizeMax;
            copyText.enableAutoSizing = sourceText.enableAutoSizing;
            copyText.enableWordWrapping = sourceText.enableWordWrapping;
            copyText.overflowMode = sourceText.overflowMode;
            copyText.color = sourceText.color;
            copyText.margin = sourceText.margin;

            RectTransform? sourceRect = sourceText.rectTransform;
            RectTransform? copyRect = copyText.rectTransform;
            if (sourceRect == null || copyRect == null)
                continue;

            copyRect.anchorMin = sourceRect.anchorMin;
            copyRect.anchorMax = sourceRect.anchorMax;
            copyRect.pivot = sourceRect.pivot;
            copyRect.anchoredPosition = sourceRect.anchoredPosition;
            copyRect.sizeDelta = sourceRect.sizeDelta;
            copyRect.offsetMin = sourceRect.offsetMin;
            copyRect.offsetMax = sourceRect.offsetMax;
            copyRect.localRotation = sourceRect.localRotation;
            copyRect.localScale = sourceRect.localScale;
        }
    }

    private static bool IsDynamicCloneVisualBehaviour(Behaviour behaviour)
    {
        if (behaviour.TryCast<Graphic>() != null ||
            behaviour.TryCast<CanvasGroup>() != null ||
            behaviour.TryCast<BaseMeshEffect>() != null)
        {
            return true;
        }

        string typeName = behaviour.GetIl2CppType().FullName ?? behaviour.GetIl2CppType().Name;
        return typeName.Contains("TextMeshPro", StringComparison.OrdinalIgnoreCase) ||
               typeName.Contains("TMPro", StringComparison.OrdinalIgnoreCase);
    }

    private static void TryCreateDelayedCountryOverlayCopies(CampaignMap map)
    {
        if (!CountryOverlayMaterialsReady(map))
            return;

        CreatedCountryOverlayCopies = true;
        int createdCopies = CreateCountryOverlayCopies(map, CampaignMap.mapWidth);
        Melon<UADVanillaPlusMod>.Logger.Msg($"UADVP map wrap: copied {createdCopies} country overlay renderers.");
    }

    private static bool CountryOverlayMaterialsReady(CampaignMap map)
    {
        CampaignBordersManager? bordersManager = map.bordersManager;
        if (bordersManager == null || bordersManager.Countries == null)
            return false;

        foreach (CampaignBordersManager.Country country in bordersManager.Countries)
        {
            if (country == null || country.MeshObjects == null)
                continue;

            for (int i = 0; i < country.MeshObjects.Count; i++)
            {
                MeshRenderer? renderer = country.MeshObjects[i];
                if (renderer == null)
                    continue;

                Material[] materials = renderer.materials;
                for (int j = 0; j < materials.Length; j++)
                {
                    Material material = materials[j];
                    if (material != null && StripInstanceSuffix(material.name).StartsWith("player-", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
        }

        return false;
    }

    private static int CreateCountryOverlayCopies(CampaignMap map, float copySpacing)
    {
        CampaignBordersManager? bordersManager = map.bordersManager;
        if (bordersManager == null || bordersManager.Countries == null)
            return 0;

        int createdCopies = 0;
        foreach (CampaignBordersManager.Country country in bordersManager.Countries)
        {
            if (country == null || country.MeshObjects == null)
                continue;

            for (int i = 0; i < country.MeshObjects.Count; i++)
            {
                MeshRenderer? sourceRenderer = country.MeshObjects[i];
                if (sourceRenderer == null)
                    continue;

                if (CreateCountryOverlayCopy(sourceRenderer, -copySpacing, "NegativeX", country.Name, i))
                    createdCopies++;
                if (CreateCountryOverlayCopy(sourceRenderer, copySpacing, "PositiveX", country.Name, i))
                    createdCopies++;
            }
        }

        return createdCopies;
    }

    private static bool CreateCountryOverlayCopy(MeshRenderer sourceRenderer, float xOffset, string sideName, string countryName, int regionIndex)
    {
        GameObject source = sourceRenderer.gameObject;
        MeshFilter? sourceFilter = source.GetComponent<MeshFilter>();
        if (sourceFilter == null || sourceFilter.sharedMesh == null)
            return false;

        string safeCountryName = string.IsNullOrWhiteSpace(countryName) ? "Unknown" : countryName.Replace(" ", "_");
        GameObject copy = new($"UADVP_WrapOverlay_{sideName}_{safeCountryName}_{regionIndex}");
        copy.layer = source.layer;
        copy.tag = source.tag;
        copy.SetActive(source.activeSelf);
        copy.transform.SetParent(source.transform.parent, false);
        copy.transform.localPosition = source.transform.localPosition;
        copy.transform.localRotation = source.transform.localRotation;
        copy.transform.localScale = source.transform.localScale;

        Vector3 position = copy.transform.position;
        position.x += xOffset;
        position.y += CountryOverlayLift;
        copy.transform.position = position;

        MeshFilter copyFilter = copy.AddComponent<MeshFilter>();
        copyFilter.sharedMesh = sourceFilter.sharedMesh;

        MeshRenderer copyRenderer = copy.AddComponent<MeshRenderer>();
        copyRenderer.enabled = sourceRenderer.enabled;
        copyRenderer.sortingLayerID = sourceRenderer.sortingLayerID;
        copyRenderer.sortingOrder = sourceRenderer.sortingOrder;
        copyRenderer.sharedMaterials = CloneMaterials(sourceRenderer.materials, copy.name, CountryOverlayRenderQueue);

        WrapObjects.Add(copy);
        return true;
    }

    private static Material[] CloneMaterials(Material[] sourceMaterials, string copyName, int minimumRenderQueue)
    {
        Material[] materials = new Material[sourceMaterials.Length];
        for (int i = 0; i < sourceMaterials.Length; i++)
        {
            Material sourceMaterial = sourceMaterials[i];
            if (sourceMaterial == null)
                continue;

            materials[i] = new Material(sourceMaterial)
            {
                name = $"{copyName}_Material_{i}",
                renderQueue = Mathf.Max(sourceMaterial.renderQueue, minimumRenderQueue)
            };
        }

        return materials;
    }

    private static int RaiseRenderQueues(GameObject root, int renderQueue)
    {
        int rendererCount = 0;
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null)
                continue;

            Material[] materials = renderer.materials;
            foreach (Material material in materials)
            {
                if (material != null)
                    material.renderQueue = Mathf.Max(material.renderQueue, renderQueue);
            }

            renderer.materials = materials;
            rendererCount++;
        }

        return rendererCount;
    }

    private static int DisableNonRenderingBehaviours(GameObject root)
    {
        int disabled = 0;
        MonoBehaviour[] behaviours = root.GetComponentsInChildren<MonoBehaviour>(true);
        foreach (MonoBehaviour behaviour in behaviours)
        {
            if (behaviour == null || IsRenderingBehaviour(behaviour))
                continue;

            behaviour.enabled = false;
            disabled++;
        }

        return disabled;
    }

    private static bool IsRenderingBehaviour(MonoBehaviour behaviour)
    {
        string typeName = behaviour.GetIl2CppType().FullName ?? behaviour.GetIl2CppType().Name;
        return typeName.Contains("TextMeshPro", StringComparison.OrdinalIgnoreCase) ||
               typeName.Contains("TMPro", StringComparison.OrdinalIgnoreCase);
    }

    private static int DisableColliders(GameObject root)
    {
        int disabled = 0;
        Component[] components = root.GetComponentsInChildren<Component>(true);
        foreach (Component component in components)
        {
            if (component == null)
                continue;

            string typeName = component.GetIl2CppType().FullName ?? component.GetIl2CppType().Name;
            if (!typeName.Contains("Collider", StringComparison.OrdinalIgnoreCase))
                continue;

            UnityEngine.Object.Destroy(component);
            disabled++;
        }

        return disabled;
    }

    private static void SetHorizontalBorderRenderers(Transform? mapParent, bool enabled)
    {
        if (mapParent == null)
            return;

        string[] borderNames = { "BorderLeft", "BorderRight" };
        foreach (string borderName in borderNames)
        {
            Transform? border = mapParent.Find(borderName);
            if (border == null)
            {
                Melon<UADVanillaPlusMod>.Logger.Warning($"UADVP map wrap: border not found: {borderName}");
                continue;
            }

            Renderer[] renderers = border.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer renderer in renderers)
                renderer.enabled = enabled;
        }
    }

    private static Shader FindFirstShader(params string[] shaderNames)
    {
        foreach (string shaderName in shaderNames)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader != null)
                return shader;
        }

        throw new InvalidOperationException("No usable shader found for UADVP map wrap material.");
    }

    private static string StripInstanceSuffix(string value)
    {
        return value.Replace(" (Instance)", "");
    }

    private static void ClearWrapObjects()
    {
        foreach (GameObject copy in WrapObjects)
        {
            if (copy != null)
                UnityEngine.Object.Destroy(copy);
        }

        WrapObjects.Clear();
        DynamicMarkerCopies.Clear();
        RouteCopies.Clear();
        CreatedCountryOverlayCopies = false;
        LoggedDynamicMarkerCopies = false;
        LoggedRouteCopies = false;
        LastOverlayAttemptFrame = 0;
        LastDynamicCleanupFrame = 0;
    }

    private static void CleanupDynamicMarkerCopies()
    {
        if (DynamicMarkerCopies.Count == 0)
            return;

        List<IntPtr>? deadKeys = null;
        foreach (KeyValuePair<IntPtr, DynamicMarkerCopySet> marker in DynamicMarkerCopies)
        {
            DynamicMarkerCopySet copies = marker.Value;
            if (copies.Source != null && copies.Negative != null && copies.Positive != null)
                continue;

            deadKeys ??= new List<IntPtr>();
            deadKeys.Add(marker.Key);
            DestroyDynamicMarkerCopySet(copies);
        }

        if (deadKeys == null)
            return;

        foreach (IntPtr key in deadKeys)
            DynamicMarkerCopies.Remove(key);
    }

    private static void CleanupRouteCopies()
    {
        if (RouteCopies.Count == 0)
            return;

        List<IntPtr>? deadKeys = null;
        foreach (KeyValuePair<IntPtr, RouteCopySet> route in RouteCopies)
        {
            RouteCopySet copies = route.Value;
            if (copies.Source != null && copies.Negative != null && copies.Positive != null)
                continue;

            deadKeys ??= new List<IntPtr>();
            deadKeys.Add(route.Key);
            DestroyRouteCopySet(copies);
        }

        if (deadKeys == null)
            return;

        foreach (IntPtr key in deadKeys)
            RouteCopies.Remove(key);
    }

    private static void DestroyDynamicMarkerCopySet(DynamicMarkerCopySet copies)
    {
        if (copies.Negative != null)
            UnityEngine.Object.Destroy(copies.Negative);
        if (copies.Positive != null)
            UnityEngine.Object.Destroy(copies.Positive);
    }

    private static void DestroyRouteCopySet(RouteCopySet copies)
    {
        if (copies.Negative != null)
            UnityEngine.Object.Destroy(copies.Negative.gameObject);
        if (copies.Positive != null)
            UnityEngine.Object.Destroy(copies.Positive.gameObject);
    }

    private enum DynamicMarkerKind
    {
        Unknown,
        Port,
        TaskForce,
        Event
    }

    private sealed class DynamicMarkerCopySet
    {
        internal readonly CampaignMapElement Source;
        internal readonly GameObject Negative;
        internal readonly GameObject Positive;

        internal DynamicMarkerCopySet(CampaignMapElement source, GameObject negative, GameObject positive)
        {
            Source = source;
            Negative = negative;
            Positive = positive;
        }
    }

    private sealed class RouteCopySet
    {
        internal readonly Route Source;
        internal readonly Route Negative;
        internal readonly Route Positive;

        internal RouteCopySet(Route source, Route negative, Route positive)
        {
            Source = source;
            Negative = negative;
            Positive = positive;
        }
    }

}

[HarmonyPatch(typeof(CampaignMap), nameof(CampaignMap.OnClickDetected))]
internal static class CampaignMapWrapClickNormalizePatch
{
    [HarmonyPrefix]
    private static void Prefix(ref Vector3 position)
        => CampaignMapWrapVisualPatch.NormalizeWrappedMapClick(ref position);
}

[HarmonyPatch(typeof(MapUI), nameof(MapUI.GetRoute))]
internal static class CampaignMapWrapRouteCopyPatch
{
    [HarmonyPostfix]
    private static void Postfix(Route __result)
    {
        CampaignMapWrapVisualPatch.SyncRouteCopies(__result);
    }
}

[HarmonyPatch(typeof(MoveShipsWindow), nameof(MoveShipsWindow.ShowMapToMap))]
internal static class CampaignMapWrapMoveWindowShowMapToMapDiagnosticPatch
{
    [HarmonyPrefix]
    private static void Prefix(CampaignController.TaskForce group, Vector3 from, ref Vector3 to, PathResult result)
        => CampaignMapWrapVisualPatch.PrepareMoveWindowShowMapToMap(group, from, ref to, result);
}

[HarmonyPatch(typeof(ShipUI), nameof(ShipUI.OnPointerClick))]
internal static class CampaignMapWrapShipSelectionOffsetPatch
{
    [HarmonyPrefix]
    private static void Prefix(ShipUI __instance)
        => CampaignMapWrapVisualPatch.MarkOriginalTaskForceSelection(__instance);
}

[HarmonyPatch(typeof(Cam), "CheckCameraBorders")]
internal static class CampaignMapWrapCameraBoundsPatch
{
    [HarmonyPrefix]
    private static bool Prefix(Cam __instance)
    {
        if (!GameManager.IsWorldMap || CampaignMap.Instance == null || __instance?.cameraComp == null || !ModSettings.CampaignMapWraparoundEnabled)
            return true;

        float mapWidth = CampaignMap.mapWidth;
        if (mapWidth <= 0f)
            return true;

        float viewHalfHeight = __instance.cameraComp.orthographicSize;
        float viewHalfWidth = viewHalfHeight * __instance.screenSizeRation;

        __instance.rightBorder = CampaignMap.MapSize.x - viewHalfWidth + 1f + mapWidth;
        __instance.leftBorder = viewHalfWidth - CampaignMap.MapSize.x - 1f - mapWidth;
        __instance.topBorder = CampaignMap.MapSize.z - viewHalfHeight + 1f;
        __instance.bottomBorder = viewHalfHeight - CampaignMap.MapSize.z - 1f;

        Transform transform = __instance.transform;
        Vector3 position = transform.position;
        position.x = Mathf.Clamp(position.x, __instance.leftBorder, __instance.rightBorder);
        position.z = Mathf.Clamp(position.z, __instance.bottomBorder, __instance.topBorder);
        transform.position = position;

        CampaignMapWrapVisualPatch.RuntimeUpdate();
        return false;
    }
}

[HarmonyPatch(typeof(CampaignMapElement), nameof(CampaignMapElement.UpdatePositionScale))]
internal static class CampaignMapWrapElementPositionPatch
{
    [HarmonyPostfix]
    private static void Postfix(CampaignMapElement __instance, Vector3 newPosition, float scale)
    {
        CampaignMapWrapVisualPatch.SyncDynamicMarkerCopies(__instance, newPosition, scale);
    }
}

[HarmonyPatch(typeof(ShipUI), nameof(ShipUI.UpdatePositionScale))]
internal static class CampaignMapWrapShipPositionPatch
{
    [HarmonyPostfix]
    private static void Postfix(ShipUI __instance, Vector3 newPosition, float scale)
    {
        CampaignMapWrapVisualPatch.SyncDynamicMarkerCopies(__instance, newPosition, scale);
    }
}
