# Vanilla ship material and color flow

This note is a vanilla-focused deep dive into how Ultimate Admiral: Dreadnoughts appears to build, color, render, and reuse ship visuals. It intentionally ignores VP-specific patches and treats the decompiled game as the source of truth.

The inspected vanilla sources are:

- `E:\Codex\cpp2il_uad_diffable\DiffableCs\Assembly-CSharp\*.cs` for class shapes, fields, method names, and signatures.
- `E:\Codex\cpp2il_uad_isil\IsilDump\Assembly-CSharp\*.txt` for method bodies and call flow.

Decompiler caveat: the C# skeletons omit method bodies, while the ISIL dumps preserve control flow and calls but lose some semantic names. The exact Unity material assets, shader graphs, prefab defaults, and texture contents are not fully represented in these files.

## High-level model

Vanilla does not appear to have one clean `shipColor` or `shipPaint` property that drives every ship surface.

The ship's visible appearance is composed from several separate systems:

1. The prefab/model materials that come with loaded hulls, parts, guns, barbettes, funnels, and other ship objects.
2. Runtime renderer discovery and refresh on `Part` and `Ship`.
3. Runtime material changes for special states such as damage, flags, and schematic/damage-plan rendering.
4. Offscreen camera captures that turn the live 3D ship into cached `Texture2D` previews.
5. UI-only colors for campaign map icons, battle bars, side indicators, text, and tactical overlays.

That split matters. A vanilla "ship is colored" report can refer to:

- the actual Unity materials on live 3D renderers,
- a cached raster preview generated from the 3D model,
- a battle/campaign UI overlay color,
- a flag texture,
- a damage-plan or section-plan replacement shader image,
- or country/player map materials unrelated to the ship hull mesh.

## Important terminology trap

`Ship.Mat` and `Ship.MatInfo` are not Unity visual materials.

In `Ship.cs`, `Mat` contains entries such as `Steel`, `Hull`, `Armor`, `Turret`, `Barrel`, `Engine`, `AntiTorp`, `Fuel`, `Ammo`, and `Torpedo`. `MatInfo` contains weight/cost fields. This is the ship design economy and weight model, not the renderer material assignment path.

When investigating visual materials, the useful types and fields are instead Unity-side concepts:

- `UnityEngine.Material`
- `UnityEngine.Renderer`
- `UnityEngine.Texture`
- `UnityEngine.Texture2D`
- `UnityEngine.Camera`
- `UnityEngine.RenderTexture`
- `RawImage`
- `Image`

## Data that feeds color

### Player/country data

`PlayerData` has these relevant fields:

- `color`
- `highlightColor`
- `PlayerMaterial`

`PlayerData.PostProcess()` loads a campaign UI material from a path shaped like `Campaign UI\Materials\PlayerMaterials\player-...`, stores it in `PlayerMaterial`, and sets the material color using the player color with an adjusted alpha.

This looks like campaign/UI color identity, not a universal hull paint system. It can color campaign map or player-owned UI materials, but it is not the same thing as recoloring every live ship mesh renderer.

`Player.BattleColor()` returns a side color for battle context. Vanilla uses it for UI elements such as battle bars and colorized ship names. It should be treated as battle UI team color, not physical ship material color.

`Player.Flag(...)` resolves country/government flag textures from folders such as:

- `Flags`
- `FlagsNaval`
- `FlagsFascists`
- `FlagsDemocracy`
- `FlagsMonarchy`
- `FlagsCommunists`

Flags are their own texture/material path. They can change visible ship identity without changing the hull material.

### Part/model data

`PartData` and `PartModelData` include country availability and country-specific model selection fields such as `countries` and `countriesx`.

Those fields affect which parts or model variants are available/selected. In the inspected vanilla C#/ISIL, they do not look like a direct data table for hull paint colors.

This suggests that country-specific visual identity is split across:

- model and part availability,
- prefab/material defaults,
- flag textures,
- campaign/player UI colors,
- battle UI colors,
- and any shader/material data embedded in Unity assets.

## Live 3D ship model pipeline

### Part model load and refresh

The central vanilla model path is:

```text
Part.LoadModel(...)
  -> Part.Refresh(...)
       -> Part.GetVisualRenderers(...)
       -> stores Part.visualRenderers
       -> refreshes borders, colliders, mounting helpers, hull links, and effects
       -> may call Ship.RefreshHull(...)
```

Relevant `Part` fields:

- `model`
- `barrelModels`
- `visualRenderers`
- `borderVisuals`
- `bow`
- `middlesBase`
- `middles`
- `stern`
- `hullInfo`
- `damageState`
- `hullSectionsDamageStates`
- static loaded model/shared mesh caches such as `loadedModels`, `loadedModelsCont`, and `loadedSharedMeshes`

`Part.GetVisualRenderers(GameObject obj)` gathers the renderers that vanilla considers visually relevant for the part. `Part.RefreshOnlyRenderers()` is the lighter helper that only updates `visualRenderers` from the current object.

`Part.Refresh(...)` is much broader. It rebuilds or reconnects the visual model state around the part. It is also a hot path: model loading, hull refresh, constructor transitions, and battle transitions can all cause parts to refresh. Any visual-material logic attached here will run often.

### Hull refresh

`Ship.RefreshHull(bool updateSections = true)` is the main hull refresh surface. It walks the ship's hull/parts, updates hull section state, refreshes relevant parts, and updates hull stats.

The important observation from the inspected vanilla ISIL is that `RefreshHull` is mostly about the ship model structure, sections, bounds, and part refresh. It is not obviously a dedicated "apply country paint" method.

So the base hull appearance seems to come primarily from the loaded hull/part model materials unless another state-specific method mutates those materials later.

### Battle model transition

Battle setup uses these methods:

```text
Part.LoadBattle(...)
  -> Part.LoadUnloadBattle(true)

Part.LoadBattle2(...)
  -> Part.LoadUnloadBattle2(true)

Ship.LoadUnloadBattle(bool battle, BattleStore save = null)
```

`Part.LoadUnloadBattle(...)` and `Part.LoadUnloadBattle2(...)` switch part objects into or out of battle state and call into `Ship.RefreshPartEffects(...)`.

`Ship.LoadUnloadBattle(...)` builds a lot of battle-only visual/UI state:

- ship overhead UI,
- range/aiming UI,
- battle bar UI,
- selection and scheme UI objects,
- renderer and shadow fade caches,
- battle-side colors through `Player.BattleColor()`.

This method uses renderer collections from hull and parts, but the observed color calls here are mostly UI/overlay related rather than direct hull paint.

## Where vanilla actively changes materials

### Damage visuals

`Ship.ShowDamagedVisuals(...)` is one of the clearest vanilla methods that directly mutates materials.

The ISIL shows calls equivalent to:

- `Material.get_color`
- `Material.set_color`
- `Material.SetTexture("_MainTex", ...)`
- `Material.SetTexture("_BumpMap", ...)`
- `Material.SetTextureScale(...)`
- `Material.SetFloat("_BumpScale", ...)`
- `Material.SetFloat("_Glossiness", ...)`
- `Material.SetFloat("_GlossMapScale", ...)`

This path applies a damaged visual state by changing color, texture, normal map, scale, bump, gloss, and gloss-map settings.

Practical implication: any custom material state that assumes the original material remains untouched can be invalidated by damage visuals. Damage is not just an overlay; vanilla can write to material properties.

### Flag materials

`Ship` has a `FlagMaterials: List<Material>` field. The ISIL shows paths that assign flag textures, including neutral/surrender-style flag changes through `Material.set_mainTexture`.

Flags should be treated as a separate material system from hull paint. A ship can have unchanged hull materials but changed visible identity through flag material textures.

### Campaign/player materials

`PlayerData.PostProcess()` loads and colors `PlayerMaterial` for campaign UI/player display use. This is a material color mutation, but it is part of player/campaign presentation rather than the ship renderer pipeline.

### Damage plans and section plans

`Ui.RefreshDamagePlan(...)`, `Ui.RefreshDamagePlanBasic(...)`, `Ui.RefreshDamagePlanManualSideTop(...)`, and `Ui.RefreshSectionsPlan(...)` render special diagnostic/schematic images.

These paths use cameras, render textures, material colors, shader parameters, and replacement shaders. They should not be mistaken for normal hull paint:

- the output is a generated UI texture,
- the view is side/top/schematic,
- renderer colors are for plan visualization,
- and the result is consumed by UI images, not by the live battle model.

## Preview image pipeline

Vanilla ship preview images are not separate artwork. They are raster captures of the live 3D ship model.

Core methods in `Ui`:

```text
Ui.GetShipPreviewTex(Ship ship, bool isForce = true)
Ui.GetShipProfileTex(Ship ship)
Ui.GetShipProfileTopTex(Ship ship)
  -> Ui.GetShipPreviewTexGeneric(...)
```

`Ui.GetShipPreviewTexGeneric(...)`:

1. Chooses a cache key from `VesselEntity.id`. The inspected ISIL appears to prefer the design ship's `id` when the current ship has a design link, otherwise it uses the current ship's `id`.
2. Checks the selected dictionary and returns an existing non-destroyed `Texture2D` immediately.
3. Ensures the ship has a model loaded, entering constructor state if needed.
4. Temporarily makes the ship visible for capture.
5. Moves/rotates/scales the ship under an offscreen preview camera.
6. Collects renderers from `ship.hullAndParts`.
7. Disables or normalizes LOD/trail/render state so the capture is stable.
8. Computes renderer bounds and sets camera orthographic size.
9. Enables the appropriate preview/profile lights.
10. Renders the camera into its target `RenderTexture`.
11. Reads pixels into a new `Texture2D`.
12. Applies the texture and stores it in the cache under the selected `id`.
13. Restores the ship position, rotation, scale, visibility, LOD, trail, and constructor state.

That means preview textures inherit whatever the live 3D materials look like at the moment of capture.

If the ship model is grey, tinted, damaged, missing textures, or carrying stale material state when the preview is captured, the cached preview can preserve that state until the cache entry is removed, the cached Unity object is destroyed, or the preview dictionaries are cleaned up. In the inspected vanilla ISIL, the `isForce` parameter should not be treated as a guaranteed cache-bypass or recapture flag because a non-null cached texture can be returned before the capture path runs.

### Ship preview caches

Relevant `Ui` fields:

- `partsPreview: Dictionary<Key, Texture2D>`
- `shipsPreview: Dictionary<Guid, Texture2D>`
- `shipsProfile: Dictionary<Guid, Texture2D>`
- `shipsProfileTop: Dictionary<Guid, Texture2D>`

Related helper:

- `Ui.CleanupRawTextures()`

`Ui.CleanupRawTextures()` destroys the cached `Texture2D` objects from these preview/profile dictionaries and clears the dictionaries. It is a broad cleanup helper, not a narrow single-ship invalidation API.

For any visual work, the preview caches are a separate concern from live material correctness. A live ship can be fixed while an old cached UI texture still looks wrong.

### Part previews

Part previews use a similar offscreen capture path:

```text
Ui.GetPartPreviewTex(PartData part, Ship ship)
  -> Ui.PreviewPart(...)
  -> PartCamera/Camera target texture
  -> Texture2D.ReadPixels(...)
  -> Texture2D.Apply()
  -> Ui.StopPreviewPart(...)
```

This is used for designer part info and part lists. The preview is generated from a temporary/fake preview part, not directly from the player's existing installed part.

## Where preview textures are used

The following vanilla call sites reuse the preview/profile texture helpers:

- `CampaignFleetWindow` calls `Ui.GetShipPreviewTex(...)` and assigns it to a `RawImage` ship icon.
- `ShipGroupPopupUI` calls `Ui.GetShipPreviewTex(...)` for group popup ship images.
- `MoveShipsWindow` calls `Ui.GetShipPreviewTex(...)` for transfer/move ship rows.
- `BattleResultWindow` calls `Ui.GetShipProfileTex(...)` and assigns it to result `RawImage` entries.
- `DivisionTooltip` calls `Ui.GetShipProfileTex(...)`.
- `UIDivision` calls `Ui.GetShipProfileTex(...)`.
- Several `Ui` methods call `GetShipPreviewTex(...)`, `GetShipProfileTex(...)`, and `GetPartPreviewTex(...)` directly for fleet, designer, selected ship, and part panels.

So a ship material problem can surface far away from the live 3D model. The same underlying material state can show up in:

- designer preview cards,
- campaign fleet windows,
- campaign group popups,
- move/transfer windows,
- battle results,
- division tooltips,
- battle or fleet ship lists,
- and ship profile/top/profile preview views.

## Designer and constructor

The designer/constructor uses the live ship model, not just preview images.

The relevant flow is approximately:

```text
Ship enters constructor/designer state
  -> parts and hull models are loaded/refreshed
  -> Part.Refresh(...) collects renderers and rebuilds part visual state
  -> Ship.RefreshHull(...) updates hull sections and related model state
  -> constructor cameras render the actual ship
  -> UI panels may separately request preview/profile/damage-plan textures
```

Designer visuals therefore have two different surfaces:

1. The actual 3D ship shown in the constructor.
2. Cached or freshly captured `Texture2D` previews shown in UI panels.

Those can disagree if the live material was changed after a preview was cached, or if a preview capture temporarily loaded/refreshed objects differently from the constructor view.

Part info previews are their own fake-preview path through `Ui.PreviewPart(...)`, so they are another possible place where temporary preview objects can carry different material state from installed live parts.

## Battle UI and battle scene

The battle scene has several visual layers:

### Live ship materials

The actual 3D ship still comes from loaded `Part` and `Ship` models and their renderers. Battle transition methods can refresh parts and effects, but the inspected vanilla calls do not show a single battle method that universally recolors hull materials by country.

### Battle overlays and UI colors

`Ship.LoadUnloadBattle(...)` creates and configures battle UI state. It uses `Player.BattleColor()` for side/team coloring.

Examples of battle-color usage include:

- battle bars,
- overhead or ingame ship UI,
- ship names when the battle-color option is requested,
- division/tooltip display,
- range/selection/scheme UI fade dictionaries.

These colors are important visually, but they are not the same as hull material color.

### Scheme and range views

`Ship` has fields such as `schemeUi`, `ingameUi`, `uiGunRanges`, `uiGunRangesToFade`, and `uiGunRangesToFade3`. These are battle UI/tactical surfaces. They are renderer-related but should be treated as overlays or helper visuals, not base ship paint.

### Damage state

Battle is where damage visual mutations become especially relevant. `Ship.ShowDamagedVisuals(...)` can write material color, main textures, bump maps, and gloss/bump parameters.

So a material investigation in battle has to distinguish:

- original prefab material,
- model-load material state,
- battle transition state,
- battle overlay color,
- damage visual material mutation,
- cached preview/image state,
- and flag material state.

## Campaign map and fleet UI

`ShipUI` is the campaign map element. Its fields include:

- `Flag: Image`
- `Icon: Image`
- `ShipSprite`
- `SubmarineSprite`
- `FriendlyColor`
- `EnemyColor`
- `DefaultColor`

This is a 2D campaign map icon system, not the live 3D ship material system.

Campaign/fleet panels that show actual ship silhouettes generally go through `Ui.GetShipPreviewTex(...)` or related profile helpers and display the resulting `Texture2D` in a `RawImage`.

So campaign visuals split into:

- 2D strategic map icon/flag/color state through `ShipUI`,
- preview/profile captures through `Ui`,
- and the actual constructor/battle 3D model when a real scene is active.

## Practical vanilla lifecycle

A simplified lifecycle for a ship visual looks like this:

```text
Data/model selection
  -> PartData / PartModelData / hull / part definitions
  -> model prefab/material defaults

Model load
  -> Part.LoadModel(...)
  -> Part.Refresh(...)
  -> visualRenderers collected
  -> Ship.RefreshHull(...)

Designer
  -> live constructor model
  -> part previews through fake preview parts
  -> ship/profile previews through offscreen cameras
  -> damage/section plans through schematic cameras and replacement shaders

Campaign UI
  -> ship preview/profile textures reused in RawImages
  -> campaign map ShipUI icon/flag/team colors

Battle load
  -> Part.LoadBattle(...)
  -> Ship.LoadUnloadBattle(...)
  -> battle UI and overlay objects
  -> Player.BattleColor() used for battle UI side color

Battle runtime
  -> live ship renderers
  -> flags
  -> overlays/ranges/scheme UI
  -> damage visuals can mutate material properties

Battle/result UI
  -> profile/preview captures reused again
```

## What this means for future material work

### The best vanilla hook surfaces are lifecycle-aware

Because `Part.Refresh(...)` is broad and frequent, it is a risky place to do heavy material work. It is useful for understanding when renderers become available, but expensive texture generation or material cloning there can run many times.

Safer surfaces depend on the goal:

- For live designer visuals: after model load/renderer collection, but with cache guards.
- For battle visuals: after battle models are real and no longer in the loading/outline/preview phase.
- For preview correctness: before `GetShipPreviewTexGeneric(...)` captures, or by removing/cleaning the affected preview cache entries after material changes.
- For damage compatibility: after vanilla damage visuals, or by preserving enough original material state to restore/reapply safely.
- For campaign map colors: through `ShipUI`/`PlayerData`/UI image paths, not ship mesh materials.

### Preview cache invalidation is part of visual correctness

If a material changes after `Ui.GetShipPreviewTex(...)` captured a ship, the cached `Texture2D` can stay stale. Passing `isForce` is not enough evidence that vanilla will recapture an already-cached preview; the inspected cache check can return the old texture first.

Any system that modifies ship materials should think about:

- `shipsPreview`,
- `shipsProfile`,
- `shipsProfileTop`,
- `partsPreview`,
- and `CleanupRawTextures()`.

It should also check whether the current ship and its design ship share the same preview key. A stale design-level preview can make multiple live ships look wrong even after the live renderers are correct.

### Do not conflate UI color and hull color

`Player.BattleColor()`, `PlayerData.color`, `ShipUI.FriendlyColor`, `ShipUI.EnemyColor`, and `PlayerMaterial` are real visual color systems, but they mostly describe UI identity. They are not evidence that the physical ship hull mesh has been recolored.

### Damage visuals can overwrite assumptions

`Ship.ShowDamagedVisuals(...)` is a direct material writer. It touches color, main texture, bump map, texture scale, bump scale, glossiness, and gloss-map scale.

Any material restoration/cache system needs to treat damaged visuals as a first-class vanilla mutation path.

### Some color is probably asset-driven

The inspected decompiled code does not expose a simple vanilla table that says "country X hull uses color Y". The base visual state appears to come heavily from Unity prefab/material assets and loaded model variants.

That means some questions cannot be answered from C#/ISIL alone. To identify exact base hull material names, shader names, texture names, or country-material mappings, the next step would be Unity asset inspection rather than more C# call tracing.

## Quick checklist for future investigations

When a ship looks wrong in vanilla or a vanilla-like path, check these separately:

1. Is the live 3D model wrong, or only a cached preview/profile image?
2. Is the issue in designer, campaign UI, battle, battle result UI, or all of them?
3. Does the part's `visualRenderers` list contain the expected renderers after `Part.Refresh(...)`?
4. Did `Ship.RefreshHull(...)` or battle load rebuild/replace the part model after materials were changed?
5. Did `Ship.ShowDamagedVisuals(...)` change material properties later?
6. Is the visible color actually a battle/campaign UI color rather than a hull material?
7. Is the visible identity coming from a flag material texture?
8. Was the preview texture captured before the live material was corrected?
9. Did an offscreen preview/fake part object create or pollute material state?
10. Is the preview cache keyed through the ship's design `id` rather than only the live ship instance?
11. Are model/prefab material defaults or asset-level shader properties the real source?

## Bottom line

Vanilla ship appearance is not a single pipeline. The live ship mesh starts with prefab/model materials, then vanilla refreshes renderers and hull structure through `Part` and `Ship`, mutates materials for states such as damage and flags, and repeatedly converts the live model into cached UI textures through offscreen cameras.

Designer, battle, campaign fleet UI, campaign map UI, battle results, tooltips, and part previews all touch different slices of that system. A correct material analysis has to follow the specific surface where the bad color appears rather than assuming there is one global ship paint pass.
