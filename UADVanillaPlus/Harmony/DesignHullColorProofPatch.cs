using System.Collections;
using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using UADVanillaPlus.GameData;
using UnityEngine;

namespace UADVanillaPlus.Harmony;

// Optional experimental visual probe: tint hull-side-looking materials while preserving
// texture detail and leaving decks/topside fittings alone. It must stay fully inert
// unless the player enables Experimental Nation Ship Paints in the UAD:VP menu.
[HarmonyPatch(typeof(Part))]
internal static class DesignHullColorProofPatch
{
    private enum PaintArea
    {
        HullSide,
        Barbette,
        Superstructure,
        Gun
    }

    private readonly struct PaintProfile
    {
        internal PaintProfile(Color materialColor, Color32 textureTarget, float textureBlend, string suffix)
        {
            MaterialColor = materialColor;
            TextureTarget = textureTarget;
            TextureBlend = textureBlend;
            Suffix = suffix;
        }

        internal Color MaterialColor { get; }
        internal Color32 TextureTarget { get; }
        internal float TextureBlend { get; }
        internal string Suffix { get; }
    }

    private readonly struct ShipPaintScheme
    {
        internal ShipPaintScheme(string id, PaintProfile hullSide, PaintProfile superstructure, PaintProfile gun)
        {
            Id = id;
            HullSide = hullSide;
            Superstructure = superstructure;
            Gun = gun;
        }

        internal string Id { get; }
        internal PaintProfile HullSide { get; }
        internal PaintProfile Superstructure { get; }
        internal PaintProfile Gun { get; }

        internal PaintProfile Profile(PaintArea paintArea)
            => paintArea switch
            {
                PaintArea.Superstructure => Superstructure,
                PaintArea.Barbette => Gun,
                PaintArea.Gun => Gun,
                _ => HullSide
            };
    }

    internal readonly struct NationPaintUiInfo
    {
        internal NationPaintUiInfo(string key, string label, string value, string template)
        {
            Key = key;
            Label = label;
            Value = value;
            Template = template;
        }

        internal string Key { get; }
        internal string Label { get; }
        internal string Value { get; }
        internal string Template { get; }
    }

    private sealed class NationPaintDefinition
    {
        internal NationPaintDefinition(string key, string label, string[] matchTokens, ShipPaintScheme builtInScheme)
        {
            Key = key;
            Label = label;
            MatchTokens = matchTokens;
            BuiltInScheme = builtInScheme;
        }

        internal string Key { get; }
        internal string Label { get; }
        internal string[] MatchTokens { get; }
        internal ShipPaintScheme BuiltInScheme { get; }
    }

    private sealed class PaintedMaterialSet
    {
        internal PaintedMaterialSet(Material[] materials, bool changedRenderer, int paintedMaterialCount, int skippedMaterialCount)
        {
            Materials = materials;
            ChangedRenderer = changedRenderer;
            PaintedMaterialCount = paintedMaterialCount;
            SkippedMaterialCount = skippedMaterialCount;
        }

        internal Material[] Materials { get; }
        internal bool ChangedRenderer { get; }
        internal int PaintedMaterialCount { get; }
        internal int SkippedMaterialCount { get; }
    }

    private sealed class RendererOriginalMaterialSet
    {
        internal RendererOriginalMaterialSet(Renderer renderer, Material[] materials)
        {
            Renderer = renderer;
            Materials = materials;
        }

        internal Renderer Renderer { get; }
        internal Material[] Materials { get; }
    }

    private readonly struct PaintedRendererResult
    {
        internal PaintedRendererResult(bool changedRenderer, int paintedMaterialCount, int skippedMaterialCount)
        {
            ChangedRenderer = changedRenderer;
            PaintedMaterialCount = paintedMaterialCount;
            SkippedMaterialCount = skippedMaterialCount;
        }

        internal bool ChangedRenderer { get; }
        internal int PaintedMaterialCount { get; }
        internal int SkippedMaterialCount { get; }
    }

    private static readonly HashSet<string> LoggedPaintParts = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> AppliedRendererSignatureByPart = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, PaintedMaterialSet> PaintedMaterialSets = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, bool> PaintMaterialCandidateCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Texture> GeneratedTextures = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<int, Texture> OriginalTextureByGeneratedTexture = new();
    private static readonly Dictionary<string, Material> GeneratedMaterials = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<int, Material> OriginalMaterialByGeneratedMaterial = new();
    private static readonly Dictionary<int, string> ProfileSuffixByGeneratedMaterial = new();
    private static readonly Dictionary<int, RendererOriginalMaterialSet> OriginalMaterialsByPaintedRenderer = new();
    private static readonly Dictionary<string, string> BattleCountryByShipId = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, ShipPaintScheme> ConfiguredNationPaintSchemes = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> InvalidNationPaintWarnings = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> FailedTextureCopies = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> FailedMaterialCopies = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> DamagePaintSuppressedPartKeys = new(StringComparer.OrdinalIgnoreCase);
    private const string GeneratedMarker = "_uadvp_";
    private static readonly ShipPaintScheme DefaultScheme = new(
        "DefaultWhiteBuff",
        Profile(0.94f, 0.93f, 0.86f, 236, 234, 218, 0.16f, "hull_warmwhite"),
        Profile(0.73f, 0.56f, 0.33f, 184, 140, 82, 0.26f, "top_buff"),
        Profile(0.66f, 0.52f, 0.35f, 164, 132, 92, 0.24f, "gun_buff"));
    private static readonly ShipPaintScheme UsaScheme = new(
        "DefaultWhiteBuff",
        Profile(0.95686275f, 0.95686275f, 0.93333334f, 244, 244, 238, 0.16f, "hull_warmwhite"),
        Profile(0.7607843f, 0.627451f, 0.42352942f, 194, 160, 108, 0.26f, "top_buff"),
        Profile(0.7019608f, 0.57254905f, 0.3882353f, 179, 146, 99, 0.24f, "gun_buff"));
    private static readonly ShipPaintScheme BritainScheme = new(
        "BritainBlackWhite",
        Profile(0.06f, 0.06f, 0.055f, 18, 18, 16, 0.72f, "hull_black"),
        Profile(0.90f, 0.88f, 0.78f, 224, 218, 194, 0.32f, "top_warmwhite"),
        Profile(0.84f, 0.82f, 0.74f, 210, 204, 184, 0.30f, "gun_warmwhite"));
    private static readonly ShipPaintScheme GermanyScheme = new(
        "GermanyMediumGray",
        Profile(0.43f, 0.45f, 0.45f, 106, 112, 112, 0.44f, "hull_mediumgray"),
        Profile(0.47f, 0.49f, 0.48f, 116, 122, 120, 0.38f, "top_mediumgray"),
        Profile(0.16f, 0.165f, 0.165f, 42, 43, 43, 0.44f, "gun_blackgray"));
    private static readonly ShipPaintScheme FranceScheme = new(
        "FranceBlueGray",
        Profile(0.68f, 0.78f, 0.84f, 168, 192, 206, 0.44f, "hull_palebluegray"),
        Profile(0.72f, 0.80f, 0.84f, 178, 198, 208, 0.40f, "top_palebluegray"),
        Profile(0.48f, 0.57f, 0.62f, 120, 142, 154, 0.36f, "gun_bluegray"));
    private static readonly ShipPaintScheme RussiaScheme = new(
        "RussiaDarkBuff",
        Profile(0.08f, 0.085f, 0.075f, 24, 25, 22, 0.66f, "hull_dark"),
        Profile(0.78f, 0.60f, 0.34f, 198, 150, 86, 0.32f, "top_buff"),
        Profile(0.10f, 0.095f, 0.085f, 30, 28, 25, 0.52f, "gun_dark"));
    private static readonly ShipPaintScheme JapanScheme = new(
        "JapanGreenGray",
        Profile(0.38f, 0.43f, 0.32f, 94, 106, 80, 0.54f, "hull_greengray"),
        Profile(0.46f, 0.51f, 0.39f, 114, 126, 96, 0.50f, "top_greengray"),
        Profile(0.30f, 0.35f, 0.27f, 76, 88, 68, 0.44f, "gun_greengray"));
    private static readonly ShipPaintScheme ItalyScheme = new(
        "ItalyWarmGray",
        Profile(0.80f, 0.78f, 0.69f, 200, 194, 172, 0.42f, "hull_lightwarmgray"),
        Profile(0.82f, 0.79f, 0.69f, 204, 196, 172, 0.38f, "top_lightwarmgray"),
        Profile(0.62f, 0.59f, 0.51f, 154, 146, 128, 0.34f, "gun_warmgray"));
    private static readonly ShipPaintScheme AustriaScheme = new(
        "AustriaLightGrayOchre",
        Profile(0.74f, 0.76f, 0.74f, 186, 190, 184, 0.34f, "hull_lightgray"),
        Profile(0.78f, 0.55f, 0.18f, 198, 136, 46, 0.38f, "top_imperialochre"),
        Profile(0.62f, 0.44f, 0.20f, 154, 110, 52, 0.34f, "gun_ochre"));
    private static readonly ShipPaintScheme SpainScheme = new(
        "SpainWarmWhiteBuff",
        Profile(0.06666667f, 0.0627451f, 0.050980393f, 17, 16, 13, 0.34f, "hull_spanishwarmwhite"),
        Profile(0.9490196f, 0.93333334f, 0.8666667f, 242, 238, 221, 0.32f, "top_deepbuff"),
        Profile(0.76862746f, 0.6039216f, 0.27058825f, 196, 154, 69, 0.32f, "gun_warmdark"));
    private static readonly ShipPaintScheme ChinaScheme = new(
        "ChinaWhiteYellow",
        Profile(0.96f, 0.94f, 0.84f, 238, 232, 208, 0.34f, "hull_chinawhite"),
        Profile(0.90f, 0.68f, 0.16f, 228, 166, 38, 0.40f, "top_yellowfunnels"),
        Profile(0.58f, 0.46f, 0.28f, 146, 116, 72, 0.34f, "gun_yellowbuff"));
    private static readonly NationPaintDefinition[] NationPaintDefinitions =
    {
        new("usa", "USA", new[] { "united states", "usa", "america" }, UsaScheme),
        new("britain", "UK", new[] { "britain", "british", "uk", "england" }, BritainScheme),
        new("germany", "Germany", new[] { "germany", "german" }, GermanyScheme),
        new("france", "France", new[] { "france", "french" }, FranceScheme),
        new("russia", "Russia", new[] { "russia", "russian", "soviet" }, RussiaScheme),
        new("japan", "Japan", new[] { "japan", "japanese" }, JapanScheme),
        new("italy", "Italy", new[] { "italy", "italian" }, ItalyScheme),
        new("austria_hungary", "Austria-Hungary", new[] { "austria", "austro", "hungary", "hungarian" }, AustriaScheme),
        new("spain", "Spain", new[] { "spain", "spanish" }, SpainScheme),
        new("china", "China", new[] { "china", "chinese" }, ChinaScheme),
    };
    private static readonly string[] ColorProperties = { "_Color", "_BaseColor" };
    private static readonly string[] TextureNameProperties = { "_MainTex", "_BaseMap", "_Albedo", "_DiffuseTex", "_BaseColorMap" };
    private static readonly string[] HullSkipTokens =
    {
        "deck", "wood", "plank", "floor", "top", "detail", "roof", "roofing", "boat", "lifeboat", "rail", "rope", "chain",
        "flag", "mast", "tower", "bridge", "barbette", "turret", "gun", "barrel", "anchor",
        "propeller", "crew", "canvas", "window", "glass", "ladder", "vent", "funnel", "smoke",
        "waterline", "hull_bottom", "bottom", "underwater", "keel"
    };
    private static readonly string[] SideTokens =
    {
        "hull", "steel_", "steelboard", "steel_board", "side", "belt", "armor", "armour", "casemate", "paint"
    };
    private static readonly string[] SuperstructureSkipTokens =
    {
        "deck", "wood", "plank", "floor", "boat", "lifeboat", "rail", "rope", "chain", "flag",
        "barbette", "turret", "gun", "barrel", "anchor", "propeller", "crew", "canvas", "window",
        "glass", "ladder", "vent", "smoke", "black", "cap", "top", "roof", "waterline",
        "hull_bottom", "bottom", "underwater", "keel"
    };
    private static readonly string[] SuperstructureTokens =
    {
        "tower", "bridge", "funnel", "stack", "mast", "conning", "superstructure", "steel_", "steelboard",
        "steel_board", "metal", "body"
    };
    private static readonly string[] GunSkipTokens =
    {
        "deck", "wood", "plank", "floor", "boat", "lifeboat", "rail", "rope", "chain", "flag",
        "anchor", "propeller", "crew", "canvas", "window", "glass", "smoke", "waterline",
        "hull_bottom", "bottom", "underwater", "keel"
    };
    private static readonly string[] GunTokens =
    {
        "gun", "turret", "barrel", "cannon", "steel_", "steelboard", "steel_board", "metal", "body"
    };
    private static readonly string[] BarbetteSkipTokens =
    {
        "deck", "wood", "plank", "floor", "boat", "lifeboat", "rail", "rope", "chain", "flag",
        "anchor", "propeller", "crew", "canvas", "window", "glass", "smoke", "waterline",
        "hull_bottom", "bottom", "underwater", "keel"
    };
    private static readonly string[] BarbetteTokens =
    {
        "barbette", "steel_", "steelboard", "steel_board", "armor", "armour", "metal", "body"
    };
    private static int HullDetailedLogCount;
    private static int BarbetteDetailedLogCount;
    private static int SuperstructureDetailedLogCount;
    private static int GunDetailedLogCount;
    private static int BattleLoadLogCount;
    private static int RestoredBrokenMaterialLogCount;
    private static int PropertyBlockFailureLogCount;
    private static int GeneratedTextureLogCount;
    private static int SceneCacheResetLogCount;
    private static int BattleRepaintLogCount;
    private static int BattleCountryMapLogCount;
    private static int BattleRepaintCoalesceLogCount;
    private static int BattleRepaintThresholdLogCount;
    private static int GeneratedObjectCleanupLogCount;
    private static int DamagePaintPolicyLogCount;
    private static int LodRendererLogCount;
    private static int MissedGunLodRendererLogCount;
    private static int NationPaintSettingsLogCount;
    private static int ResolvedNationPaintRevision = -1;
    private static int BattleRepaintGeneration;
    private static bool BattleRepaintScheduled;
    private static int BattleRepaintScheduledGeneration;
    private static string LastCampaignBattleCountryMapId = string.Empty;
    private const int MaxApplicationLogsPerArea = 4;
    private const int MaxLodRendererLogs = 8;
    private const int MaxMissedGunLodRendererLogs = 12;
    private const int BattleRepaintCandidateWarningThreshold = 240;
    private const int BattleRepaintBattleReadyWaitAttempts = 60;
    private const float BattleRepaintBattleReadyWaitDelaySeconds = 0.2f;
    private const float BattleRepaintRetryDelaySeconds = 0.85f;
    private const float BattleRepaintLateRetryDelaySeconds = 1.5f;
    private static readonly bool EnableTextureTintCopies = false;
    private static readonly Dictionary<PaintArea, int> ApplicationLogCountByArea = new();
    private static readonly HashSet<PaintArea> SuppressedApplicationLogAreas = new();

    internal static bool IsEnabled => ModSettings.ExperimentalNationShipPaintsEnabled;

    private static PaintProfile Profile(
        float materialR,
        float materialG,
        float materialB,
        byte textureR,
        byte textureG,
        byte textureB,
        float textureBlend,
        string suffix)
        => new(
            new Color(materialR, materialG, materialB, 1f),
            new Color32(textureR, textureG, textureB, byte.MaxValue),
            textureBlend,
            "_uadvp_" + suffix);

    internal static IEnumerable<NationPaintUiInfo> NationPaintOptions()
    {
        foreach (NationPaintDefinition definition in NationPaintDefinitions)
        {
            yield return new NationPaintUiInfo(
                definition.Key,
                definition.Label,
                ModSettings.NationShipPaintString(definition.Key),
                BuiltInPaintString(definition));
        }
    }

    internal static void RefreshNationPaintSettingsCache(string context)
    {
        if (!IsEnabled)
            return;

        EnsureNationPaintSchemeCache();
        if (NationPaintSettingsLogCount++ < 3)
            Melon<UADVanillaPlusMod>.Logger.Msg($"UADVP ship paint proof: refreshed Nation Ship Paints settings during {context}.");
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(Part.ModelLoadedOrReused))]
    private static void ModelLoadedOrReusedPostfix(Part __instance)
    {
        if (!IsEnabled)
            return;

        if (DeferAutoPaintDuringBattleLoad("model loaded"))
            return;

        TryApplyProofColor(__instance, "model loaded", force: false);
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(Part.RefreshOnlyRenderers))]
    private static void RefreshOnlyRenderersPostfix(Part __instance)
    {
        if (!IsEnabled)
            return;

        if (DeferAutoPaintDuringBattleLoad("renderers refreshed"))
            return;

        TryApplyProofColor(__instance, "renderers refreshed", force: false);
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(Part.LoadBattle))]
    private static void LoadBattlePrefix(Part __instance)
    {
        if (IsEnabled)
            RestoreOriginalMaterials(__instance, "battle load pre");
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(Part.LoadBattle))]
    private static void LoadBattlePostfix(Part __instance)
    {
        if (!IsEnabled)
            return;

        if (GameManager.IsBattle)
            TryApplyProofColor(__instance, "battle loaded", force: true);

        ScheduleBattleRepaintRetries("battle loaded", repaintImmediately: false);
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(Part.LoadBattle2))]
    private static void LoadBattle2Prefix(Part __instance)
    {
        if (IsEnabled)
            RestoreOriginalMaterials(__instance, "battle load2 pre");
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(Part.LoadBattle2))]
    private static void LoadBattle2Postfix(Part __instance)
    {
        if (!IsEnabled)
            return;

        if (GameManager.IsBattle)
            TryApplyProofColor(__instance, "battle loaded2", force: true);

        ScheduleBattleRepaintRetries("battle loaded2", repaintImmediately: false);
    }

    internal static void ApplyCurrentSetting()
    {
        ResolvedNationPaintRevision = -1;
        ConfiguredNationPaintSchemes.Clear();

        if (!IsEnabled)
        {
            ResetScenePaintCache("Experimental Nation Ship Paints disabled");
            return;
        }

        EnsureNationPaintSchemeCache();
        Melon<UADVanillaPlusMod>.Logger.Msg("UADVP ship paint proof: Experimental Nation Ship Paints enabled.");
        if (GameManager.IsBattle)
            ScheduleBattleRepaintRetries("Experimental Nation Ship Paints enabled", repaintImmediately: true);
        else if (GameManager.IsConstructor)
            RepaintAllLoadedParts("Experimental Nation Ship Paints enabled");
    }

    internal static void ApplyNationPaintSettingsChange(string context)
    {
        ResolvedNationPaintRevision = -1;
        ConfiguredNationPaintSchemes.Clear();

        if (!IsEnabled)
            return;

        EnsureNationPaintSchemeCache();
        ResetScenePaintCache($"Nation Ship Paints changed ({context})");
        if (GameManager.IsBattle || GameManager.IsConstructor)
            RepaintAllLoadedParts($"Nation Ship Paints changed ({context})");
        else if (NationPaintSettingsLogCount++ < 6)
            Melon<UADVanillaPlusMod>.Logger.Msg($"UADVP ship paint proof: stored Nation Ship Paints change during {context}; no active ship scene repaint needed.");
    }

    internal static void ResetScenePaintCache(string context)
    {
        bool shouldScanLoadedParts = HasPaintStateToRestore();
        int restoredLoadedRenderers = shouldScanLoadedParts ? RestoreLoadedPartMaterials(context) : 0;
        int restoredTrackedRenderers = RestoreTrackedRenderers(context);
        int materialSets = PaintedMaterialSets.Count;
        int materials = GeneratedMaterials.Count;
        int generatedObjects = DestroyGeneratedPaintObjects(context);

        AppliedRendererSignatureByPart.Clear();
        PaintedMaterialSets.Clear();
        GeneratedMaterials.Clear();
        OriginalMaterialByGeneratedMaterial.Clear();
        ProfileSuffixByGeneratedMaterial.Clear();
        GeneratedTextures.Clear();
        OriginalTextureByGeneratedTexture.Clear();
        FailedMaterialCopies.Clear();
        FailedTextureCopies.Clear();
        PaintMaterialCandidateCache.Clear();
        DamagePaintSuppressedPartKeys.Clear();
        BattleCountryByShipId.Clear();
        LastCampaignBattleCountryMapId = string.Empty;
        BattleRepaintGeneration++;
        BattleRepaintScheduled = false;
        BattleRepaintScheduledGeneration = 0;

        if ((restoredLoadedRenderers > 0 || restoredTrackedRenderers > 0 || materialSets > 0 || materials > 0 || generatedObjects > 0) && SceneCacheResetLogCount++ < 8)
        {
            Melon<UADVanillaPlusMod>.Logger.Msg(
                $"UADVP ship paint proof: reset scene paint cache during {context}; restoredLoadedRenderers={restoredLoadedRenderers}, restoredTrackedRenderers={restoredTrackedRenderers}, materialSets={materialSets}, generatedMaterials={materials}, destroyedGeneratedObjects={generatedObjects}.");
        }
    }

    private static bool HasPaintStateToRestore()
        => OriginalMaterialsByPaintedRenderer.Count > 0
           || AppliedRendererSignatureByPart.Count > 0
           || PaintedMaterialSets.Count > 0
           || GeneratedMaterials.Count > 0
           || GeneratedTextures.Count > 0;

    private static int RestoreLoadedPartMaterials(string context)
    {
        try
        {
            int restoredRenderers = 0;
            Part[] parts = UnityEngine.Object.FindObjectsOfType<Part>();
            foreach (Part part in parts)
            {
                if (part != null)
                    restoredRenderers += RestoreOriginalMaterials(part, $"{context} cache reset", logDetails: false);
            }

            return restoredRenderers;
        }
        catch (Exception ex)
        {
            Melon<UADVanillaPlusMod>.Logger.Warning(
                $"UADVP ship paint proof failed to restore loaded parts during {context}. {ex.GetType().Name}: {ex.Message}");
            return 0;
        }
    }

    private static int DestroyGeneratedPaintObjects(string context)
    {
        int destroyed = 0;
        foreach (Material material in GeneratedMaterials.Values.Distinct())
        {
            if (DestroyUnityObject(material))
                destroyed++;
        }

        foreach (Texture texture in GeneratedTextures.Values.Distinct())
        {
            if (DestroyUnityObject(texture))
                destroyed++;
        }

        if (destroyed > 0 && GeneratedObjectCleanupLogCount++ < 6)
        {
            Melon<UADVanillaPlusMod>.Logger.Msg(
                $"UADVP ship paint proof: destroyed {destroyed} generated paint object(s) during {context}.");
        }

        return destroyed;
    }

    private static bool DestroyUnityObject(UnityEngine.Object? obj)
    {
        if (obj == null)
            return false;

        try
        {
            UnityEngine.Object.Destroy(obj);
            return true;
        }
        catch (Exception ex)
        {
            if (GeneratedObjectCleanupLogCount++ < 6)
            {
                Melon<UADVanillaPlusMod>.Logger.Warning(
                    $"UADVP ship paint proof failed to destroy generated paint object '{obj.name ?? "<object>"}'. {ex.GetType().Name}: {ex.Message}");
            }

            return false;
        }
    }

    internal static void ScheduleBattleRepaintRetries(string context, bool repaintImmediately)
    {
        if (!IsEnabled)
            return;

        if (repaintImmediately && GameManager.IsBattle)
            RepaintAllLoadedParts(context);

        if (BattleRepaintScheduled && ShouldContinueBattleRepaintRetry(BattleRepaintScheduledGeneration))
        {
            if (BattleRepaintCoalesceLogCount++ < 4)
            {
                Melon<UADVanillaPlusMod>.Logger.Msg(
                    $"UADVP ship paint proof: coalesced battle repaint request during {context}; pendingGeneration={BattleRepaintScheduledGeneration}.");
            }

            return;
        }

        int generation = ++BattleRepaintGeneration;
        BattleRepaintScheduled = true;
        BattleRepaintScheduledGeneration = generation;
        MelonCoroutines.Start(RepaintBattleAfterLoadSettles(context, generation));
    }

    private static IEnumerator RepaintBattleAfterLoadSettles(string context, int generation)
    {
        try
        {
            for (int attempt = 1; attempt <= BattleRepaintBattleReadyWaitAttempts; attempt++)
            {
                yield return new WaitForSeconds(BattleRepaintBattleReadyWaitDelaySeconds);
                if (!ShouldContinueBattleRepaintRetry(generation))
                    yield break;

                if (!GameManager.IsBattle)
                    continue;

                RepaintAllLoadedParts($"{context} battle ready");

                yield return new WaitForSeconds(BattleRepaintRetryDelaySeconds);
                if (!ShouldContinueBattleRepaintRetry(generation) || !GameManager.IsBattle)
                    yield break;

                RepaintAllLoadedParts($"{context} retry");

                yield return new WaitForSeconds(BattleRepaintLateRetryDelaySeconds);
                if (!ShouldContinueBattleRepaintRetry(generation) || !GameManager.IsBattle)
                    yield break;

                RepaintAllLoadedParts($"{context} late retry");
                yield break;
            }
        }
        finally
        {
            if (BattleRepaintScheduledGeneration == generation)
            {
                BattleRepaintScheduled = false;
                BattleRepaintScheduledGeneration = 0;
            }
        }
    }

    private static bool ShouldContinueBattleRepaintRetry(int generation)
        => IsEnabled && generation == BattleRepaintGeneration;

    private static bool DeferAutoPaintDuringBattleLoad(string context)
    {
        if (!IsEnabled)
            return false;

        if (!GameManager.IsLoadingBattle || GameManager.IsBattle)
            return false;

        ScheduleBattleRepaintRetries(context, repaintImmediately: false);
        return true;
    }

    internal static void RepaintAllLoadedParts(string context)
    {
        if (!IsEnabled)
            return;

        try
        {
            Part[] parts = UnityEngine.Object.FindObjectsOfType<Part>();
            foreach (Part part in parts)
            {
                if (part != null && PaintAreaFor(part) != null)
                    RestoreOriginalMaterials(part, $"{context} pre");
            }

            DestroyGeneratedPaintObjects(context);
            PaintedMaterialSets.Clear();
            GeneratedMaterials.Clear();
            OriginalMaterialByGeneratedMaterial.Clear();
            ProfileSuffixByGeneratedMaterial.Clear();
            GeneratedTextures.Clear();
            OriginalTextureByGeneratedTexture.Clear();
            FailedMaterialCopies.Clear();
            FailedTextureCopies.Clear();
            PaintMaterialCandidateCache.Clear();
            AppliedRendererSignatureByPart.Clear();

            int repaintCandidates = 0;
            int candidatesOverThreshold = 0;
            foreach (Part part in parts)
            {
                if (part == null || PaintAreaFor(part) == null)
                    continue;

                repaintCandidates++;
                if (repaintCandidates > BattleRepaintCandidateWarningThreshold)
                    candidatesOverThreshold++;

                TryApplyProofColor(part, context, force: true);
            }

            if (BattleRepaintLogCount++ < 4)
            {
                Melon<UADVanillaPlusMod>.Logger.Msg(
                    $"UADVP ship paint proof: repainted loaded parts during {context}; parts={parts.Length}, candidates={repaintCandidates}, overThreshold={candidatesOverThreshold}.");
            }
            else if (candidatesOverThreshold > 0 && BattleRepaintThresholdLogCount++ < 4)
            {
                Melon<UADVanillaPlusMod>.Logger.Warning(
                    $"UADVP ship paint proof: repaint candidate count exceeded threshold by {candidatesOverThreshold} during {context}; threshold={BattleRepaintCandidateWarningThreshold}.");
            }
        }
        catch (Exception ex)
        {
            Melon<UADVanillaPlusMod>.Logger.Warning(
                $"UADVP ship paint proof failed to repaint loaded parts during {context}. {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void TryApplyProofColor(Part part, string context, bool force)
    {
        if (!IsEnabled)
            return;

        try
        {
            PaintArea? paintArea = PaintAreaFor(part);
            if (paintArea == null)
                return;

            if (IsDamagePaintSuppressed(part))
                return;

            ShipPaintScheme scheme = SchemeFor(part);
            PaintProfile profile = scheme.Profile(paintArea.Value);
            string partKey = PaintPartKey(part, paintArea.Value, profile);
            if (!force)
            {
                string beforeSignature = RendererMaterialSignature(part);
                if (AppliedRendererSignatureByPart.TryGetValue(partKey, out string? appliedSignature)
                    && string.Equals(appliedSignature, beforeSignature, StringComparison.OrdinalIgnoreCase)
                    && RendererMaterialsUsable(part))
                {
                    return;
                }
            }

            int rendererCount = 0;
            int changedMaterialCount = 0;
            int skippedMaterialCount = 0;
            foreach (Renderer renderer in HullRenderers(part))
            {
                if (renderer == null)
                    continue;

                Material[] materials = renderer.sharedMaterials;
                if (materials == null || materials.Length == 0)
                    continue;

                PaintedMaterialSet paintedMaterials = GetOrCreatePaintedMaterialSet(materials, paintArea.Value, scheme, profile);
                changedMaterialCount += paintedMaterials.PaintedMaterialCount;
                skippedMaterialCount += paintedMaterials.SkippedMaterialCount;

                if (paintedMaterials.ChangedRenderer)
                {
                    RememberOriginalMaterials(renderer, materials);
                    renderer.sharedMaterials = paintedMaterials.Materials;
                    rendererCount++;
                }
            }

            AppliedRendererSignatureByPart[partKey] = RendererMaterialSignature(part);
            LogFirstApplication(part, paintArea.Value, scheme, context, rendererCount, changedMaterialCount, skippedMaterialCount);
        }
        catch (Exception ex)
        {
            Melon<UADVanillaPlusMod>.Logger.Warning($"UADVP ship paint proof failed during {context}. {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static PaintArea? PaintAreaFor(Part? part)
    {
        PartData? data = part?.data;
        if (data == null)
            return null;

        if (data.isHull)
            return PaintArea.HullSide;

        if (data.isTowerAny || data.isFunnel)
            return PaintArea.Superstructure;

        if (data.isBarbette)
            return PaintArea.Barbette;

        if (data.isGun)
            return PaintArea.Gun;

        return null;
    }

    private static ShipPaintScheme SchemeFor(Part part)
    {
        string key = BattleCountryKey(part.ship);
        if (string.IsNullOrWhiteSpace(key))
            key = PlayerKey(part.ship?.player);

        EnsureNationPaintSchemeCache();
        foreach (NationPaintDefinition definition in NationPaintDefinitions)
        {
            if (!ContainsAny(key, definition.MatchTokens))
                continue;

            return ConfiguredNationPaintSchemes.TryGetValue(definition.Key, out ShipPaintScheme configuredScheme)
                ? configuredScheme
                : definition.BuiltInScheme;
        }

        return DefaultScheme;
    }

    private static void EnsureNationPaintSchemeCache()
    {
        int revision = ModSettings.NationShipPaintsRevision;
        if (ResolvedNationPaintRevision == revision)
            return;

        ConfiguredNationPaintSchemes.Clear();
        foreach (NationPaintDefinition definition in NationPaintDefinitions)
        {
            string rawValue = ModSettings.NationShipPaintString(definition.Key);
            if (string.IsNullOrWhiteSpace(rawValue))
                continue;

            if (TryParsePaintScheme(definition, rawValue, out ShipPaintScheme scheme, out string error))
            {
                ConfiguredNationPaintSchemes[definition.Key] = scheme;
                continue;
            }

            string warningKey = $"{definition.Key}:{rawValue}";
            if (InvalidNationPaintWarnings.Add(warningKey))
            {
                Melon<UADVanillaPlusMod>.Logger.Warning(
                    $"UADVP ship paint proof: invalid Nation Ship Paints string for {definition.Label}; using built-in scheme. {error}");
            }
        }

        ResolvedNationPaintRevision = revision;
    }

    private static bool TryParsePaintScheme(NationPaintDefinition definition, string rawValue, out ShipPaintScheme scheme, out string error)
    {
        Color32 hull = Color32FromColor(definition.BuiltInScheme.HullSide.MaterialColor);
        Color32 superstructure = Color32FromColor(definition.BuiltInScheme.Superstructure.MaterialColor);
        Color32 gun = Color32FromColor(definition.BuiltInScheme.Gun.MaterialColor);
        bool sawRecognizedValue = false;

        string[] fields = rawValue.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (string field in fields)
        {
            int separator = field.IndexOf('=');
            if (separator <= 0 || separator >= field.Length - 1)
            {
                scheme = definition.BuiltInScheme;
                error = $"Expected key=#RRGGBB but got '{field}'.";
                return false;
            }

            string key = NormalizePaintFieldKey(field[..separator]);
            if (string.IsNullOrWhiteSpace(key))
            {
                scheme = definition.BuiltInScheme;
                error = $"Unknown color key '{field[..separator]}'.";
                return false;
            }

            if (!TryParseHexColor(field[(separator + 1)..], out Color32 color))
            {
                scheme = definition.BuiltInScheme;
                error = $"Invalid color '{field[(separator + 1)..]}' for {key}; use #RRGGBB.";
                return false;
            }

            sawRecognizedValue = true;
            switch (key)
            {
                case "hull":
                    hull = color;
                    break;
                case "super":
                    superstructure = color;
                    break;
                case "gun":
                    gun = color;
                    break;
            }
        }

        if (!sawRecognizedValue)
        {
            scheme = definition.BuiltInScheme;
            error = "No paint values were found.";
            return false;
        }

        scheme = new ShipPaintScheme(
            $"{definition.BuiltInScheme.Id}_Custom",
            CustomProfile(hull, definition.BuiltInScheme.HullSide, definition.Key, "hull"),
            CustomProfile(superstructure, definition.BuiltInScheme.Superstructure, definition.Key, "super"),
            CustomProfile(gun, definition.BuiltInScheme.Gun, definition.Key, "gun"));
        error = string.Empty;
        return true;
    }

    private static string NormalizePaintFieldKey(string key)
    {
        string normalized = (key ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "hull" => "hull",
            "super" or "top" or "superstructure" => "super",
            "gun" or "guns" => "gun",
            _ => string.Empty
        };
    }

    private static PaintProfile CustomProfile(Color32 color, PaintProfile fallback, string nationKey, string area)
        => new(
            ColorFromColor32(color),
            color,
            fallback.TextureBlend,
            $"_uadvp_custom_{nationKey}_{area}_{HexString(color).TrimStart('#').ToLowerInvariant()}");

    private static string BuiltInPaintString(NationPaintDefinition definition)
        => $"hull={HexString(Color32FromColor(definition.BuiltInScheme.HullSide.MaterialColor))}; " +
           $"super={HexString(Color32FromColor(definition.BuiltInScheme.Superstructure.MaterialColor))}; " +
           $"gun={HexString(Color32FromColor(definition.BuiltInScheme.Gun.MaterialColor))}";

    private static bool TryParseHexColor(string value, out Color32 color)
    {
        color = default;
        string hex = (value ?? string.Empty).Trim();
        if (hex.StartsWith("#", StringComparison.Ordinal))
            hex = hex[1..];

        if (hex.Length != 6)
            return false;

        try
        {
            color = new Color32(
                Convert.ToByte(hex[..2], 16),
                Convert.ToByte(hex.Substring(2, 2), 16),
                Convert.ToByte(hex.Substring(4, 2), 16),
                byte.MaxValue);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static Color32 Color32FromColor(Color color)
        => new(
            (byte)Mathf.Clamp(Mathf.RoundToInt(color.r * byte.MaxValue), 0, byte.MaxValue),
            (byte)Mathf.Clamp(Mathf.RoundToInt(color.g * byte.MaxValue), 0, byte.MaxValue),
            (byte)Mathf.Clamp(Mathf.RoundToInt(color.b * byte.MaxValue), 0, byte.MaxValue),
            byte.MaxValue);

    private static Color ColorFromColor32(Color32 color)
        => new(color.r / 255f, color.g / 255f, color.b / 255f, 1f);

    private static string HexString(Color32 color)
        => $"#{color.r:X2}{color.g:X2}{color.b:X2}";

    internal static void RememberCurrentCampaignBattleCountries(string context)
    {
        if (!IsEnabled)
            return;

        if (BattleCountryMapLogCount++ < 2)
        {
            Melon<UADVanillaPlusMod>.Logger.Msg(
                $"UADVP ship paint proof: campaign battle country mapping skipped during {context}; only saved custom-battle country maps are considered reliable for Experimental Nation Ship Paints.");
        }
    }

    private static void AddCampaignBattleCountry(Il2CppSystem.Collections.Generic.List<Ship>? ships, Player? player)
    {
        if (ships == null || player == null)
            return;

        string country = PlayerLabel(player);
        if (string.IsNullOrWhiteSpace(country))
            return;

        foreach (Ship ship in ships)
        {
            if (ship == null)
                continue;

            try
            {
                string id = ship.id.ToString();
                if (!string.IsNullOrWhiteSpace(id))
                    BattleCountryByShipId[id] = country;
            }
            catch
            {
                // Ignore ships that do not have a stable battle id yet.
            }
        }
    }

    internal static void RememberRealBattleCountries(GameManager.RealBattleSave? save)
    {
        if (!IsEnabled)
            return;

        BattleCountryByShipId.Clear();
        LastCampaignBattleCountryMapId = string.Empty;
        if (save == null)
            return;

        AddRealBattleCountry(save.Player);
        AddRealBattleCountry(save.Enemy);

        if (BattleCountryByShipId.Count > 0 && BattleCountryMapLogCount++ < 4)
        {
            string countries = string.Join(", ", BattleCountryByShipId.Values.Distinct(StringComparer.OrdinalIgnoreCase));
            Melon<UADVanillaPlusMod>.Logger.Msg(
                $"UADVP ship paint proof: remembered battle paint countries for {BattleCountryByShipId.Count} ship(s): {countries}.");
        }
    }

    private static void AddRealBattleCountry(GameManager.RealBattlePlayer? player)
    {
        if (player == null || string.IsNullOrWhiteSpace(player.Country) || player.Ships == null)
            return;

        foreach (Ship.BattleStore ship in player.Ships)
        {
            if (ship == null)
                continue;

            string id = ship.Id.ToString();
            if (!string.IsNullOrWhiteSpace(id))
                BattleCountryByShipId[id] = player.Country;
        }
    }

    private static string BattleCountryKey(Ship? ship)
    {
        if (ship == null || BattleCountryByShipId.Count == 0)
            return string.Empty;

        try
        {
            string id = ship.id.ToString();
            if (!string.IsNullOrWhiteSpace(id) && BattleCountryByShipId.TryGetValue(id, out string? country))
                return country.ToLowerInvariant();
        }
        catch
        {
            // Battle preview ships may not have a stable runtime id yet.
        }

        return string.Empty;
    }

    private static string PlayerKey(Player? player)
    {
        if (player == null)
        {
            if (GameManager.IsBattle || GameManager.IsLoadingBattle)
                return string.Empty;

            player = PlayerController.Instance;
        }

        if (player == null)
            return string.Empty;

        List<string> labels = new();

        try
        {
            if (!string.IsNullOrWhiteSpace(player.data?.name))
                labels.Add(player.data.name);
        }
        catch
        {
            // Ignore player metadata that is unavailable in this scene.
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(player.data?.nameUi))
                labels.Add(player.data.nameUi);
        }
        catch
        {
            // Ignore player metadata that is unavailable in this scene.
        }

        try
        {
            string name = player.Name(false);
            if (!string.IsNullOrWhiteSpace(name))
                labels.Add(name);
        }
        catch
        {
            // Ignore labels that cannot be resolved in constructor previews.
        }

        return string.Join(" ", labels).ToLowerInvariant();
    }

    private static string PlayerLabel(Player? player)
    {
        if (player == null)
            return string.Empty;

        List<string> labels = new();

        try
        {
            if (!string.IsNullOrWhiteSpace(player.data?.name))
                labels.Add(player.data.name);
        }
        catch
        {
            // Ignore player metadata that is unavailable in this scene.
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(player.data?.nameUi))
                labels.Add(player.data.nameUi);
        }
        catch
        {
            // Ignore player metadata that is unavailable in this scene.
        }

        try
        {
            string name = player.Name(false);
            if (!string.IsNullOrWhiteSpace(name))
                labels.Add(name);
        }
        catch
        {
            // Ignore labels that cannot be resolved in this scene.
        }

        return string.Join(" ", labels);
    }

    internal static void PrepareForDamagedVisuals(Part? part, Part.Damage damageState)
    {
        if (!IsEnabled)
            return;

        if (part == null || damageState == Part.Damage.None)
            return;

        RestoreOriginalMaterials(part, "damage visuals pre", logDetails: false);
    }

    internal static void RememberDamageVisualPolicy(Part? part, Part.Damage damageState)
    {
        if (!IsEnabled)
            return;

        if (part == null)
            return;

        string key = DamagePartKey(part);
        if (damageState == Part.Damage.None)
        {
            DamagePaintSuppressedPartKeys.Remove(key);
            return;
        }

        DamagePaintSuppressedPartKeys.Add(key);
        RemoveAppliedPartSignatures(part);

        if (DamagePaintPolicyLogCount++ < 6)
        {
            Melon<UADVanillaPlusMod>.Logger.Msg(
                $"UADVP ship paint proof: yielding paint to vanilla damage visuals on {SafePartName(part.data)}; damage={damageState}.");
        }
    }

    private static bool IsDamagePaintSuppressed(Part part)
        => DamagePaintSuppressedPartKeys.Contains(DamagePartKey(part));

    private static string DamagePartKey(Part part)
        => part.Pointer.ToString();

    private static void RemoveAppliedPartSignatures(Part part)
    {
        AppliedRendererSignatureByPart.Remove(PaintPartKey(part, PaintArea.HullSide, DefaultScheme.HullSide));
        AppliedRendererSignatureByPart.Remove(PaintPartKey(part, PaintArea.Superstructure, DefaultScheme.Superstructure));
        AppliedRendererSignatureByPart.Remove(PaintPartKey(part, PaintArea.Barbette, DefaultScheme.Gun));
        AppliedRendererSignatureByPart.Remove(PaintPartKey(part, PaintArea.Gun, DefaultScheme.Gun));
    }

    private static IEnumerable<Renderer> HullRenderers(Part part)
    {
        HashSet<int> seen = new();
        bool yieldedVisualRenderer = false;

        if (part.visualRenderers != null && part.visualRenderers.Count > 0)
        {
            foreach (Renderer renderer in part.visualRenderers)
            {
                if (renderer != null && seen.Add(renderer.GetInstanceID()))
                {
                    yieldedVisualRenderer = true;
                    yield return renderer;
                }
            }
        }

        if (yieldedVisualRenderer)
        {
            PaintArea? paintArea = PaintAreaFor(part);
            if (paintArea == PaintArea.Gun)
                LogMissedGunChildRenderers(part, seen);

            foreach (Renderer lodRenderer in LodRenderers(part, seen))
                yield return lodRenderer;

            yield break;
        }

        GameObject root = part.gameObject;
        if (root == null)
            yield break;

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in renderers)
        {
            if (renderer != null && seen.Add(renderer.GetInstanceID()))
                yield return renderer;
        }
    }

    private static IEnumerable<Renderer> LodRenderers(Part part, HashSet<int> seen)
    {
        GameObject root = part.gameObject;
        if (root == null)
            yield break;

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        List<Renderer> lodRenderers = new();
        Transform rootTransform = root.transform;
        PaintArea? paintArea = PaintAreaFor(part);

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null || !seen.Add(renderer.GetInstanceID()))
                continue;

            if (RendererPathLooksLikeLod(renderer, rootTransform))
                lodRenderers.Add(renderer);
        }

        if (lodRenderers.Count > 0 && LodRendererLogCount++ < MaxLodRendererLogs)
        {
            Melon<UADVanillaPlusMod>.Logger.Msg(
                $"UADVP ship paint proof: included {lodRenderers.Count} extra {AreaLabel(paintArea ?? PaintArea.Superstructure)} LOD renderer(s) on {SafePartName(part.data)}.");
        }

        foreach (Renderer renderer in lodRenderers)
            yield return renderer;
    }

    private static void LogMissedGunChildRenderers(Part part, HashSet<int> visualRendererIds)
    {
        if (MissedGunLodRendererLogCount >= MaxMissedGunLodRendererLogs)
            return;

        GameObject root = part.gameObject;
        if (root == null)
            return;

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        Transform rootTransform = root.transform;
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null || visualRendererIds.Contains(renderer.GetInstanceID()))
                continue;

            Transform rendererTransform = renderer.transform;
            bool looksLikeLod = RendererPathLooksLikeLod(renderer, rootTransform);
            string rendererPath = TransformPath(rendererTransform, rootTransform);
            string parentPath = rendererTransform != null && rendererTransform.parent != null
                ? TransformPath(rendererTransform.parent, rootTransform)
                : "<none>";
            string materialNames = RendererMaterialNames(renderer);

            MissedGunLodRendererLogCount++;
            Melon<UADVanillaPlusMod>.Logger.Msg(
                $"UADVP ship paint proof: gun child renderer outside visualRenderers on {SafePartName(part.data)}; " +
                $"renderer={rendererPath}; parent={parentPath}; activeSelf={renderer.gameObject.activeSelf}; " +
                $"activeInHierarchy={renderer.gameObject.activeInHierarchy}; looksLikeLod={looksLikeLod}; " +
                $"materials={materialNames}.");

            if (MissedGunLodRendererLogCount >= MaxMissedGunLodRendererLogs)
                break;
        }
    }

    private static bool RendererPathLooksLikeLod(Renderer renderer, Transform root)
    {
        Transform current = renderer.transform;
        while (current != null)
        {
            if (LooksLikeLodName(current.name))
                return true;

            if (current == root)
                break;

            current = current.parent;
        }

        return RendererIsUnderLodGroup(renderer, root);
    }

    private static bool RendererIsUnderLodGroup(Renderer renderer, Transform root)
    {
        try
        {
            LODGroup lodGroup = renderer.GetComponentInParent<LODGroup>();
            if (lodGroup == null)
                return false;

            if (root == null)
                return true;

            Transform lodTransform = lodGroup.transform;
            Transform current = renderer.transform;
            while (current != null)
            {
                if (current == lodTransform)
                    return true;

                if (current == root)
                    break;

                current = current.parent;
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static bool LooksLikeLodName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        string normalized = name
            .Replace(" ", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("_", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("-", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(".", string.Empty, StringComparison.OrdinalIgnoreCase);

        int index = normalized.IndexOf("lod", StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            int after = index + 3;
            if (after >= normalized.Length || char.IsDigit(normalized[after]))
                return true;

            index = normalized.IndexOf("lod", after, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static string TransformPath(Transform transform, Transform root)
    {
        if (transform == null)
            return "<null>";

        List<string> names = new();
        Transform current = transform;
        while (current != null)
        {
            names.Add(current.name ?? "<unnamed>");
            if (current == root)
                break;

            current = current.parent;
        }

        names.Reverse();
        return string.Join("/", names);
    }

    private static string RendererMaterialNames(Renderer renderer)
    {
        try
        {
            Material[] materials = renderer.sharedMaterials;
            if (materials == null || materials.Length == 0)
                return "<none>";

            return string.Join(", ", materials.Select(material => material != null ? material.name : "<null>"));
        }
        catch
        {
            return "<unavailable>";
        }
    }

    private static void RememberOriginalMaterials(Renderer renderer, Material[] materials)
    {
        int rendererId = renderer.GetInstanceID();
        OriginalMaterialsByPaintedRenderer[rendererId] = new RendererOriginalMaterialSet(
            renderer,
            OriginalMaterialArray(materials));
    }

    private static Material[] OriginalMaterialArray(Material[] materials)
    {
        Material[] originalMaterials = new Material[materials.Length];
        for (int i = 0; i < materials.Length; i++)
        {
            Material material = materials[i];
            originalMaterials[i] = material == null ? null! : OriginalMaterial(material);
        }

        return originalMaterials;
    }

    private static int RestoreTrackedRenderers(string context)
    {
        if (OriginalMaterialsByPaintedRenderer.Count == 0)
            return 0;

        int restoredRenderers = 0;
        foreach (RendererOriginalMaterialSet originalSet in OriginalMaterialsByPaintedRenderer.Values.ToArray())
        {
            try
            {
                Renderer renderer = originalSet.Renderer;
                if (renderer == null)
                    continue;

                ClearRendererPropertyBlocks(renderer, originalSet.Materials.Length);
                renderer.sharedMaterials = originalSet.Materials;
                restoredRenderers++;
            }
            catch (Exception ex)
            {
                if (RestoredBrokenMaterialLogCount++ < 8)
                {
                    Melon<UADVanillaPlusMod>.Logger.Warning(
                        $"UADVP ship paint proof failed to restore a tracked renderer during {context}. {ex.GetType().Name}: {ex.Message}");
                }
            }
        }

        OriginalMaterialsByPaintedRenderer.Clear();
        return restoredRenderers;
    }

    private static void ClearRendererPropertyBlocks(Renderer renderer, int materialCount)
    {
        for (int i = 0; i < materialCount; i++)
            ClearPropertyBlock(renderer, i);
    }

    private static PaintedRendererResult ApplyPaintPropertyBlocks(Renderer renderer, Material[] materials, PaintArea paintArea, PaintProfile profile)
    {
        bool changedRenderer = false;
        int paintedMaterialCount = 0;
        int skippedMaterialCount = 0;

        for (int i = 0; i < materials.Length; i++)
        {
            Material material = materials[i];
            if (material == null)
            {
                skippedMaterialCount++;
                ClearPropertyBlock(renderer, i);
                continue;
            }

            Material source = OriginalMaterial(material);
            if (!IsUsableSourceMaterial(source) || !ShouldPaintMaterial(source, paintArea))
            {
                skippedMaterialCount++;
                ClearPropertyBlock(renderer, i);
                continue;
            }

            MaterialPropertyBlock? block = CreatePaintPropertyBlock(source, paintArea, profile);
            if (block == null)
            {
                skippedMaterialCount++;
                ClearPropertyBlock(renderer, i);
                continue;
            }

            try
            {
                renderer.SetPropertyBlock(block, i);
                changedRenderer = true;
                paintedMaterialCount++;
            }
            catch (Exception ex)
            {
                skippedMaterialCount++;
                LogPropertyBlockFailure(source, paintArea, ex);
            }
        }

        return new PaintedRendererResult(changedRenderer, paintedMaterialCount, skippedMaterialCount);
    }

    private static MaterialPropertyBlock? CreatePaintPropertyBlock(Material source, PaintArea paintArea, PaintProfile profile)
    {
        MaterialPropertyBlock? block = null;

        foreach (string property in TextureNameProperties)
        {
            if (!source.HasProperty(property))
                continue;

            Texture texture;
            try
            {
                texture = source.GetTexture(property);
            }
            catch
            {
                continue;
            }

            Texture? generatedTexture = GetOrCreatePaintTexture(texture, paintArea, profile);
            if (generatedTexture == null || generatedTexture == texture)
                continue;

            block ??= new MaterialPropertyBlock();
            block.SetTexture(Shader.PropertyToID(property), generatedTexture);
        }

        return block;
    }

    private static void ClearPropertyBlock(Renderer renderer, int materialIndex)
    {
        try
        {
            renderer.SetPropertyBlock(null, materialIndex);
        }
        catch
        {
            // Clearing is best-effort; the next successful paint pass overwrites the slot.
        }
    }

    private static void LogPropertyBlockFailure(Material source, PaintArea paintArea, Exception ex)
    {
        if (PropertyBlockFailureLogCount++ >= 8)
            return;

        Melon<UADVanillaPlusMod>.Logger.Warning(
            $"UADVP ship paint proof failed to apply property block for '{source.name ?? "<material>"}' ({AreaLabel(paintArea)}). {ex.GetType().Name}: {ex.Message}");
    }

    private static PaintedMaterialSet GetOrCreatePaintedMaterialSet(Material[] materials, PaintArea paintArea, ShipPaintScheme scheme, PaintProfile profile)
    {
        string key = PaintedMaterialSetCacheKey(materials, paintArea, scheme, profile);
        if (PaintedMaterialSets.TryGetValue(key, out PaintedMaterialSet? cachedSet))
        {
            if (IsUsablePaintedMaterialSet(cachedSet))
                return cachedSet;

            PaintedMaterialSets.Remove(key);
        }

        Material[] paintedMaterials = new Material[materials.Length];
        bool changedRenderer = false;
        int paintedMaterialCount = 0;
        int skippedMaterialCount = 0;

        for (int i = 0; i < materials.Length; i++)
        {
            Material material = materials[i];
            if (material == null)
            {
                continue;
            }

            Material originalMaterial = OriginalMaterial(material);
            if (IsGeneratedPaintMaterial(material)
                && !IsUsablePaintedMaterial(material)
                && !ReferenceEquals(originalMaterial, material)
                && IsUsableSourceMaterial(originalMaterial))
            {
                LogRestoredBrokenMaterial(material, originalMaterial);
                material = originalMaterial;
            }

            Material? paintedMaterial = GetOrCreatePaintMaterial(material, paintArea, profile);
            if (paintedMaterial == null)
            {
                skippedMaterialCount++;
                paintedMaterials[i] = material;
                continue;
            }

            paintedMaterialCount++;
            paintedMaterials[i] = paintedMaterial;
            if (!ReferenceEquals(paintedMaterial, material))
                changedRenderer = true;
        }

        PaintedMaterialSet set = new(paintedMaterials, changedRenderer, paintedMaterialCount, skippedMaterialCount);
        PaintedMaterialSets[key] = set;
        return set;
    }

    private static Material? GetOrCreatePaintMaterial(Material material, PaintArea paintArea, PaintProfile profile)
    {
        Material source = OriginalMaterial(material);
        int materialId = material.GetInstanceID();
        if (ProfileSuffixByGeneratedMaterial.TryGetValue(materialId, out string? existingSuffix)
            && string.Equals(existingSuffix, profile.Suffix, StringComparison.OrdinalIgnoreCase))
        {
            if (IsUsablePaintedMaterial(material))
                return material;

            GeneratedMaterials.Remove(MaterialCacheKey(source, profile));
            DestroyUnityObject(material);
        }

        if (ReferenceEquals(source, material)
            && !string.IsNullOrWhiteSpace(material.name)
            && material.name.Contains(GeneratedMarker, StringComparison.OrdinalIgnoreCase))
        {
            return material.name.Contains(profile.Suffix, StringComparison.OrdinalIgnoreCase) ? material : null;
        }

        if (!IsUsableSourceMaterial(source))
            return null;

        if (!ShouldPaintMaterial(source, paintArea))
            return null;

        string key = MaterialCacheKey(source, profile);
        if (GeneratedMaterials.TryGetValue(key, out Material? cachedMaterial))
        {
            if (IsUsablePaintedMaterial(cachedMaterial))
                return cachedMaterial;

            GeneratedMaterials.Remove(key);
            DestroyUnityObject(cachedMaterial);
        }

        if (FailedMaterialCopies.Contains(key))
            return null;

        try
        {
            Material clone = new(source)
            {
                name = $"{source.name}{profile.Suffix}_mat"
            };

            if (!ApplyPaintToMaterialClone(clone, source, paintArea, profile))
            {
                FailedMaterialCopies.Add(key);
                DestroyUnityObject(clone);
                return null;
            }

            GeneratedMaterials[key] = clone;
            OriginalMaterialByGeneratedMaterial[clone.GetInstanceID()] = source;
            ProfileSuffixByGeneratedMaterial[clone.GetInstanceID()] = profile.Suffix;
            return clone;
        }
        catch (Exception ex)
        {
            FailedMaterialCopies.Add(key);
            Melon<UADVanillaPlusMod>.Logger.Warning(
                $"UADVP ship paint proof failed to clone material '{source.name}' ({AreaLabel(paintArea)}). {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private static Material OriginalMaterial(Material material)
    {
        int materialId = material.GetInstanceID();
        return OriginalMaterialByGeneratedMaterial.TryGetValue(materialId, out Material? originalMaterial) && originalMaterial != null
            ? originalMaterial
            : material;
    }

    private static int RestoreOriginalMaterials(Part part, string context, bool logDetails = true)
    {
        try
        {
            int rendererCount = 0;
            int materialCount = 0;
            foreach (Renderer renderer in HullRenderers(part))
            {
                if (renderer == null)
                    continue;

                Material[] materials = renderer.sharedMaterials;
                if (materials == null || materials.Length == 0)
                    continue;

                bool changed = false;
                Material[] restoredMaterials = OriginalMaterialArray(materials);
                for (int i = 0; i < materials.Length; i++)
                {
                    Material material = materials[i];
                    if (material == null)
                        continue;

                    if (!ReferenceEquals(restoredMaterials[i], material))
                    {
                        changed = true;
                        materialCount++;
                    }
                }

                if (!changed)
                    continue;

                ClearRendererPropertyBlocks(renderer, restoredMaterials.Length);
                renderer.sharedMaterials = restoredMaterials;
                OriginalMaterialsByPaintedRenderer.Remove(renderer.GetInstanceID());
                rendererCount++;
            }

            if (rendererCount > 0)
            {
                RemoveAppliedPartSignatures(part);

                if (logDetails && BattleLoadLogCount++ < 8)
                {
                    Melon<UADVanillaPlusMod>.Logger.Msg(
                        $"UADVP ship paint proof: restored {materialCount} generated material(s) on {SafePartName(part.data)} before {context}; renderers={rendererCount}.");
                }
            }

            return rendererCount;
        }
        catch (Exception ex)
        {
            Melon<UADVanillaPlusMod>.Logger.Warning(
                $"UADVP ship paint proof failed to restore generated materials before {context}. {ex.GetType().Name}: {ex.Message}");
            return 0;
        }
    }

    private static bool IsUsablePaintedMaterialSet(PaintedMaterialSet set)
    {
        if (!set.ChangedRenderer)
            return true;

        foreach (Material material in set.Materials)
        {
            if (material == null)
                continue;

            if (material.name != null
                && material.name.Contains(GeneratedMarker, StringComparison.OrdinalIgnoreCase)
                && !IsUsablePaintedMaterial(material))
            {
                return false;
            }
        }

        return true;
    }

    private static bool RendererMaterialsUsable(Part part)
    {
        foreach (Renderer renderer in HullRenderers(part))
        {
            if (renderer == null)
                continue;

            Material[] materials = renderer.sharedMaterials;
            if (materials == null)
                continue;

            foreach (Material material in materials)
            {
                if (material != null && !IsUsablePaintedMaterial(material))
                    return false;
            }
        }

        return true;
    }

    private static bool IsGeneratedPaintMaterial(Material? material)
    {
        if (material == null)
            return false;

        if (ProfileSuffixByGeneratedMaterial.ContainsKey(material.GetInstanceID()))
            return true;

        return material.name != null
               && material.name.Contains(GeneratedMarker, StringComparison.OrdinalIgnoreCase);
    }

    private static void LogRestoredBrokenMaterial(Material brokenMaterial, Material originalMaterial)
    {
        if (RestoredBrokenMaterialLogCount++ >= 8)
            return;

        Melon<UADVanillaPlusMod>.Logger.Msg(
            $"UADVP ship paint proof: restored broken generated material '{brokenMaterial.name ?? "<material>"}' to '{originalMaterial.name ?? "<material>"}'.");
    }

    private static bool IsUsablePaintedMaterial(Material? material)
        => material != null
           && material.shader != null
           && !string.Equals(material.shader.name, "Hidden/InternalErrorShader", StringComparison.OrdinalIgnoreCase);

    private static bool IsUsableSourceMaterial(Material? material)
        => material != null
           && material.shader != null
           && !string.Equals(material.shader.name, "Hidden/InternalErrorShader", StringComparison.OrdinalIgnoreCase);

    private static bool ApplyPaintToMaterialClone(Material material, Material source, PaintArea paintArea, PaintProfile profile)
    {
        bool changed = false;

        foreach (string property in ColorProperties)
        {
            if (!source.HasProperty(property) || !material.HasProperty(property))
                continue;

            Color tint = profile.MaterialColor;
            try
            {
                tint.a = source.GetColor(property).a;
            }
            catch
            {
                // Some shaders expose the property but do not like GetColor.
            }

            material.SetColor(property, tint);
            changed = true;
        }

        if (!EnableTextureTintCopies)
            return changed;

        foreach (string property in TextureNameProperties)
        {
            if (!source.HasProperty(property) || !material.HasProperty(property))
                continue;

            Texture texture;
            try
            {
                texture = source.GetTexture(property);
            }
            catch
            {
                continue;
            }

            Texture? generatedTexture = GetOrCreatePaintTexture(texture, paintArea, profile);
            if (generatedTexture == null || generatedTexture == texture)
                continue;

            material.SetTexture(property, generatedTexture);
            changed = true;
        }

        return changed;
    }

    private static Texture? GetOrCreatePaintTexture(Texture? source, PaintArea paintArea, PaintProfile profile)
    {
        if (source == null)
            return null;

        Texture originalSource = OriginalTexture(source);
        if (!ReferenceEquals(originalSource, source))
        {
            if (!string.IsNullOrWhiteSpace(source.name) && source.name.Contains(profile.Suffix, StringComparison.OrdinalIgnoreCase))
                return source;

            source = originalSource;
        }
        else if (!string.IsNullOrWhiteSpace(source.name) && source.name.Contains(GeneratedMarker, StringComparison.OrdinalIgnoreCase))
        {
            return source.name.Contains(profile.Suffix, StringComparison.OrdinalIgnoreCase) ? source : null;
        }

        string suffix = profile.Suffix;
        if (!string.IsNullOrWhiteSpace(source.name) && source.name.Contains(suffix, StringComparison.OrdinalIgnoreCase))
            return source;

        string key = TextureCacheKey(source, suffix);
        if (GeneratedTextures.TryGetValue(key, out Texture? cachedTexture) && cachedTexture != null)
            return cachedTexture;

        if (FailedTextureCopies.Contains(key))
            return null;

        if (source.width <= 0 || source.height <= 0)
            return null;

        RenderTexture? previousActive = RenderTexture.active;
        RenderTexture renderTexture = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);

        try
        {
            Graphics.Blit(source, renderTexture);
            RenderTexture.active = renderTexture;

            Texture2D copy = new(source.width, source.height, TextureFormat.RGBA32, false)
            {
                name = $"{source.name}{suffix}",
                filterMode = source.filterMode,
                wrapMode = source.wrapMode,
                anisoLevel = source.anisoLevel
            };
            copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0, false);
            copy.Apply(false, false);

            Color32[] pixels = copy.GetPixels32();
            for (int i = 0; i < pixels.Length; i++)
            {
                Color32 pixel = pixels[i];
                pixel.r = BlendTowardTarget(pixel.r, profile.TextureTarget.r, profile.TextureBlend);
                pixel.g = BlendTowardTarget(pixel.g, profile.TextureTarget.g, profile.TextureBlend);
                pixel.b = BlendTowardTarget(pixel.b, profile.TextureTarget.b, profile.TextureBlend);
                pixels[i] = pixel;
            }

            copy.SetPixels32(pixels);
            copy.Apply(false, false);

            GeneratedTextures[key] = copy;
            OriginalTextureByGeneratedTexture[copy.GetInstanceID()] = source;
            if (GeneratedTextureLogCount++ < 8)
            {
                Melon<UADVanillaPlusMod>.Logger.Msg(
                    $"UADVP ship paint proof: generated {AreaLabel(paintArea)} texture '{copy.name}' from '{source.name}' ({source.width}x{source.height}).");
            }

            return copy;
        }
        catch (Exception ex)
        {
            FailedTextureCopies.Add(key);
            Melon<UADVanillaPlusMod>.Logger.Warning(
                $"UADVP ship paint proof failed for '{source.name}' ({AreaLabel(paintArea)}). {ex.GetType().Name}: {ex.Message}");
            return null;
        }
        finally
        {
            RenderTexture.active = previousActive;
            RenderTexture.ReleaseTemporary(renderTexture);
        }
    }

    private static Texture OriginalTexture(Texture texture)
    {
        int textureId = texture.GetInstanceID();
        return OriginalTextureByGeneratedTexture.TryGetValue(textureId, out Texture? originalTexture) && originalTexture != null
            ? originalTexture
            : texture;
    }

    private static string PaintPartKey(Part part, PaintArea paintArea, PaintProfile profile)
        => $"{part.Pointer}:{paintArea}:{profile.Suffix}";

    private static string RendererMaterialSignature(Part part)
    {
        List<string> rendererKeys = new();
        foreach (Renderer renderer in HullRenderers(part))
        {
            if (renderer == null)
                continue;

            Material[] materials = renderer.sharedMaterials;
            if (materials == null || materials.Length == 0)
            {
                rendererKeys.Add($"{RendererStableName(renderer)}:<none>");
                continue;
            }

            List<string> materialNames = new();
            foreach (Material material in materials)
            {
                materialNames.Add(material == null
                    ? "<null>"
                    : StableName(material.name, "<material>"));
            }

            rendererKeys.Add($"{RendererStableName(renderer)}:{string.Join(",", materialNames)}");
        }

        return string.Join("|", rendererKeys);
    }

    private static string PaintedMaterialSetCacheKey(Material[] materials, PaintArea paintArea, ShipPaintScheme scheme, PaintProfile profile)
    {
        List<string> materialKeys = new(materials.Length);
        foreach (Material material in materials)
        {
            if (material == null)
            {
                materialKeys.Add("<null>");
                continue;
            }

            materialKeys.Add(MaterialSourceKey(OriginalMaterial(material)));
        }

        return $"{scheme.Id}|{paintArea}|{profile.Suffix}|{string.Join(",", materialKeys)}";
    }

    private static string MaterialCacheKey(Material material, PaintProfile profile)
    {
        return $"{MaterialSourceKey(OriginalMaterial(material))}|profile={profile.Suffix}";
    }

    private static string MaterialSourceKey(Material source)
    {
        List<string> textureKeys = new();
        foreach (string property in TextureNameProperties)
        {
            if (!source.HasProperty(property))
                continue;

            try
            {
                Texture texture = source.GetTexture(property);
                if (texture != null)
                    textureKeys.Add($"{property}:{SourceTextureKey(texture)}");
            }
            catch
            {
                // Ignore shader/texture slots that cannot be read on this material.
            }
        }

        return $"{source.GetInstanceID()}|{StableName(source.name, "<material>")}|shader={ShaderStableName(source)}|textures={string.Join(",", textureKeys)}";
    }

    private static string TextureCacheKey(Texture texture, string suffix)
        => $"{SourceTextureKey(texture)}|profile={suffix}";

    private static string SourceTextureKey(Texture texture)
        => $"{StableName(texture.name, "<texture>")}|{texture.width}x{texture.height}";

    private static string RendererStableName(Renderer renderer)
        => $"{SafeObjectName(renderer.gameObject)}#{renderer.GetType().Name}";

    private static string ShaderStableName(Material material)
    {
        try
        {
            return StableName(material.shader?.name, "<shader>");
        }
        catch
        {
            return "<shader>";
        }
    }

    private static string StableName(string? name, string fallback)
    {
        if (string.IsNullOrWhiteSpace(name))
            return fallback;

        return name
            .Replace("(Instance)", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("(Clone)", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim()
            .ToLowerInvariant();
    }

    private static byte BlendTowardTarget(byte value, byte target, float blend)
    {
        float blended = Mathf.Lerp(value, target, blend);
        return (byte)Mathf.Clamp(Mathf.RoundToInt(blended), 0, byte.MaxValue);
    }

    private static bool ShouldPaintMaterial(Material material, PaintArea paintArea)
    {
        Material source = OriginalMaterial(material);
        string key = $"{paintArea}:{MaterialSourceKey(source)}";
        if (PaintMaterialCandidateCache.TryGetValue(key, out bool cachedDecision))
            return cachedDecision;

        string materialText = MaterialSearchText(source);

        bool decision = paintArea switch
        {
            PaintArea.Superstructure => LooksLikeSuperstructureMaterial(materialText),
            PaintArea.Gun => LooksLikeGunMaterial(materialText),
            PaintArea.Barbette => LooksLikeBarbetteMaterial(materialText),
            _ => LooksLikePaintedSideMaterial(materialText)
        };
        PaintMaterialCandidateCache[key] = decision;
        return decision;
    }

    private static bool LooksLikePaintedSideMaterial(string materialText)
    {
        if (ContainsAny(materialText, HullSkipTokens))
            return false;

        return ContainsAny(materialText, SideTokens);
    }

    private static bool LooksLikeSuperstructureMaterial(string materialText)
    {
        if (ContainsAny(materialText, SuperstructureSkipTokens))
            return false;

        return ContainsAny(materialText, SuperstructureTokens);
    }

    private static bool LooksLikeGunMaterial(string materialText)
    {
        if (ContainsAny(materialText, GunSkipTokens))
            return false;

        return ContainsAny(materialText, GunTokens);
    }

    private static bool LooksLikeBarbetteMaterial(string materialText)
    {
        if (ContainsAny(materialText, BarbetteSkipTokens))
            return false;

        return ContainsAny(materialText, BarbetteTokens);
    }

    private static string MaterialSearchText(Material material)
    {
        string text = material.name ?? string.Empty;

        foreach (string property in TextureNameProperties)
        {
            if (!material.HasProperty(property))
                continue;

            try
            {
                Texture texture = material.GetTexture(property);
                if (texture != null && !string.IsNullOrWhiteSpace(texture.name))
                    text += " " + texture.name;
            }
            catch
            {
                // Ignore shader/texture slots that cannot be read on this material.
            }
        }

        return text.ToLowerInvariant();
    }

    private static bool ContainsAny(string text, string[] tokens)
    {
        foreach (string token in tokens)
        {
            if (text.Contains(token, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static void LogFirstApplication(Part part, PaintArea paintArea, ShipPaintScheme scheme, string context, int rendererCount, int changedMaterialCount, int skippedMaterialCount)
    {
        string key = $"{scheme.Id}:{paintArea}:{part.Pointer}";
        if (!LoggedPaintParts.Add(key))
            return;

        ApplicationLogCountByArea.TryGetValue(paintArea, out int areaLogCount);
        if (areaLogCount >= MaxApplicationLogsPerArea)
        {
            if (SuppressedApplicationLogAreas.Add(paintArea))
            {
                Melon<UADVanillaPlusMod>.Logger.Msg(
                    $"UADVP ship paint proof: further {AreaLabel(paintArea)} application logs suppressed.");
            }

            return;
        }

        ApplicationLogCountByArea[paintArea] = areaLogCount + 1;

        string shipName = SafeShipName(part.ship);
        string partName = SafePartName(part.data);
        Melon<UADVanillaPlusMod>.Logger.Msg(
            $"UADVP ship paint proof: tinted {AreaLabel(paintArea)} using {scheme.Id} for {shipName} / {partName} during {context}; renderers={rendererCount}, paintedMaterials={changedMaterialCount}, skippedMaterials={skippedMaterialCount}.");

        if (ShouldLogMaterialSamples(paintArea))
            LogMaterialSamples(part, paintArea);
    }

    private static bool ShouldLogMaterialSamples(PaintArea paintArea)
    {
        if (paintArea == PaintArea.Gun)
            return GunDetailedLogCount++ < 3;

        if (paintArea == PaintArea.Barbette)
            return BarbetteDetailedLogCount++ < 3;

        if (paintArea == PaintArea.Superstructure)
            return SuperstructureDetailedLogCount++ < 3;

        return HullDetailedLogCount++ < 3;
    }

    private static void LogMaterialSamples(Part part, PaintArea paintArea)
    {
        int sampleCount = 0;
        foreach (Renderer renderer in HullRenderers(part))
        {
            if (renderer == null)
                continue;

            Material[] materials = renderer.sharedMaterials;
            if (materials == null)
                continue;

            foreach (Material material in materials)
            {
                if (material == null)
                    continue;

                string verdict = ShouldPaintMaterial(material, paintArea) ? "paint" : "skip";
                Melon<UADVanillaPlusMod>.Logger.Msg(
                    $"UADVP ship paint sample ({AreaLabel(paintArea)}): {verdict}; renderer='{SafeObjectName(renderer.gameObject)}'; material='{material.name ?? "<material>"}'; textures='{MaterialTextureNames(material)}'.");

                sampleCount++;
                if (sampleCount >= 8)
                    return;
            }
        }
    }

    private static string AreaLabel(PaintArea paintArea)
        => paintArea switch
        {
            PaintArea.Superstructure => "superstructure",
            PaintArea.Gun => "gun",
            PaintArea.Barbette => "barbette",
            _ => "hull side"
        };

    private static string MaterialTextureNames(Material material)
    {
        List<string> names = new();

        foreach (string property in TextureNameProperties)
        {
            if (!material.HasProperty(property))
                continue;

            try
            {
                Texture texture = material.GetTexture(property);
                if (texture != null && !string.IsNullOrWhiteSpace(texture.name))
                    names.Add($"{property}:{texture.name}");
            }
            catch
            {
                // Ignore shader/texture slots that cannot be read on this material.
            }
        }

        return names.Count == 0 ? "<none>" : string.Join(", ", names);
    }

    private static string SafeObjectName(GameObject? gameObject)
    {
        if (gameObject == null)
            return "<renderer>";

        return string.IsNullOrWhiteSpace(gameObject.name) ? "<renderer>" : gameObject.name;
    }

    private static string SafeShipName(Ship? ship)
    {
        if (ship == null)
            return "<ship>";

        try
        {
            return ship.Name(false, false, false, false, true);
        }
        catch
        {
            return "<ship>";
        }
    }

    private static string SafePartName(PartData? part)
    {
        if (part == null)
            return "<hull>";

        if (!string.IsNullOrWhiteSpace(part.nameUi))
            return part.nameUi;

        return string.IsNullOrWhiteSpace(part.name) ? "<hull>" : part.name;
    }
}

[HarmonyPatch(typeof(GameManager), nameof(GameManager.OnLeaveState))]
internal static class DesignHullColorProofLeaveStatePatch
{
    [HarmonyPostfix]
    private static void Postfix(GameManager.GameState state)
    {
        if (DesignHullColorProofPatch.IsEnabled
            && state is GameManager.GameState.Constructor
                or GameManager.GameState.Battle
                or GameManager.GameState.CustomBattleSetup
                or GameManager.GameState.World)
        {
            DesignHullColorProofPatch.ResetScenePaintCache($"leaving {state}");
        }
    }
}

[HarmonyPatch(typeof(GameManager), nameof(GameManager.ToCustomBattleFromSave))]
internal static class DesignHullColorProofCustomBattleSavePatch
{
    [HarmonyPrefix]
    private static void Prefix(GameManager.RealBattleSave save)
    {
        if (DesignHullColorProofPatch.IsEnabled)
            DesignHullColorProofPatch.RememberRealBattleCountries(save);
    }
}

[HarmonyPatch(typeof(Ship), "ShowDamagedVisuals")]
internal static class DesignHullColorProofDamageVisualPatch
{
    [HarmonyPrefix]
    private static void Prefix(Part partHint, Part.Damage damageState)
    {
        if (DesignHullColorProofPatch.IsEnabled)
            DesignHullColorProofPatch.PrepareForDamagedVisuals(partHint, damageState);
    }

    [HarmonyPostfix]
    private static void Postfix(Part partHint, Part.Damage damageState)
    {
        if (DesignHullColorProofPatch.IsEnabled)
            DesignHullColorProofPatch.RememberDamageVisualPolicy(partHint, damageState);
    }
}

[HarmonyPatch(typeof(GameManager), nameof(GameManager.OnEnterState))]
internal static class DesignHullColorProofEnterStatePatch
{
    [HarmonyPostfix]
    private static void Postfix(GameManager.GameState state)
    {
        if (!DesignHullColorProofPatch.IsEnabled)
            return;

        if (state == GameManager.GameState.Battle)
        {
            DesignHullColorProofPatch.ScheduleBattleRepaintRetries("entering Battle", repaintImmediately: true);
            return;
        }

        if (state is not GameManager.GameState.LoadingCustom and not GameManager.GameState.Battle)
            DesignHullColorProofPatch.ResetScenePaintCache($"entering {state}");
    }
}
