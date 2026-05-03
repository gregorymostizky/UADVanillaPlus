using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using UADVanillaPlus.GameData;
using UnityEngine;

namespace UADVanillaPlus.Harmony;

// Experimental campaign-map wrap illusion. This intentionally duplicates the
// rendered map surface and passive visual layers only; campaign interactions
// and dynamic markers remain vanilla until they are handled explicitly.
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

    private static readonly List<GameObject> WrapObjects = new();
    private static bool CreatedCountryOverlayCopies;
    private static int LastOverlayAttemptFrame;

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
        if (!ModSettings.CampaignMapWraparoundEnabled || CreatedCountryOverlayCopies || CampaignMap.Instance == null)
            return;

        if (Time.frameCount - LastOverlayAttemptFrame < OverlayRetryFrameInterval)
            return;

        LastOverlayAttemptFrame = Time.frameCount;
        TryCreateDelayedCountryOverlayCopies(CampaignMap.Instance);
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

        WrapObjects.Add(copy);
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
        CreatedCountryOverlayCopies = false;
        LastOverlayAttemptFrame = 0;
    }
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
