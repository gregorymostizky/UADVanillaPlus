# Vanilla Refit Superstructure Baseline

## Scope

This note reviews how vanilla Ultimate Admiral: Dreadnoughts appears to manage refits, part availability, towers, funnels, and related superstructure replacements. It is intended as a baseline before any UAD Vanilla Plus behavior changes for the requested feature:

> Allow refit designs to use newer towers, funnels, and similar superstructure parts, so older ships can receive historically plausible reconstructions instead of being locked to early dreadnought-era fittings.

No implementation is proposed as final here. The goal is to map the vanilla mechanics, identify the real blockers, and outline safer mod directions.

## Short Take

The request is historically sensible and technically plausible, but the most important vanilla detail is this:

Vanilla refit application already replaces the live ship with the selected refit design's parts. If the refit design contains a newer tower or funnel, the finished refit should physically receive that tower or funnel.

The main blockers are earlier in the pipeline:

- Whether the refit constructor exposes the newer tower or funnel as available for the old hull.
- Whether the newer part's hull tags, country gates, ship type gates, tech unlocks, mount rules, and part count rules pass.
- Whether refit-mode placement accepts the new part near an old tower or funnel position.
- Whether the game treats the old hull as still belonging to an older hull family whose allowed tower and funnel set does not include the newer reconstruction-era parts.

That means the cleanest implementation probably is not a post-refit part swap. The better first target is the refit constructor eligibility layer: allow player-only, option-gated, historically compatible tower/funnel candidates to appear and place during refit design editing.

## Evidence Sources

Code inspection used the locally decompiled vanilla sources:

- `E:\Codex\cpp2il_uad_diffable\DiffableCs\Assembly-CSharp\Ship.cs`
- `E:\Codex\cpp2il_uad_isil\IsilDump\Assembly-CSharp\Ship.txt`
- `E:\Codex\cpp2il_uad_isil\IsilDump\Assembly-CSharp\Ui.txt`
- `E:\Codex\cpp2il_uad_isil\IsilDump\Assembly-CSharp\Part.txt`
- `E:\Codex\cpp2il_uad_isil\IsilDump\Assembly-CSharp\PlayerController.txt`
- `E:\Codex\cpp2il_uad_isil\IsilDump\Assembly-CSharp\CampaignFleetWindow.txt`

Data inspection used the local captured vanilla reference data in:

- `E:\Codex\UADRealismDIP\TweaksAndFixes\Default_Files\UAD_Files\parts.csv`
- `E:\Codex\UADRealismDIP\TweaksAndFixes\Default_Files\UAD_Files\randParts.csv`
- `E:\Codex\UADRealismDIP\TweaksAndFixes\Default_Files\UAD_Files\randPartsRefit.csv`
- `E:\Codex\UADRealismDIP\TweaksAndFixes\Default_Files\UAD_Files\components.csv`
- `E:\Codex\UADRealismDIP\TweaksAndFixes\Default_Files\UAD_Files\technologies.csv`
- `E:\Codex\UADRealismDIP\TweaksAndFixes\Default_Files\UAD_Files\params.csv`

Related current VP code worth keeping in mind:

- `UADVanillaPlus\Harmony\DesignObsoleteRetentionPatch.cs`
- `UADVanillaPlus\Harmony\DesignBritishTowerAvailabilityPatch.cs`
- `UADVanillaPlus\Harmony\DesignRefitNamePatch.cs`
- `UADVanillaPlus\Harmony\CampaignFleetWindowDesignViewerPatch.cs`
- `UADVanillaPlus\Harmony\InGameOptionsMenuPatch.cs`

## Core Vanilla Concepts

### Ship Versus Design

Vanilla uses `Ship` objects both for live vessels and design/constructor vessels. During refit, the game creates an editable design copy from an existing ship. That design is later applied back onto one or more live ships.

Important refit-related `Ship` fields observed in vanilla:

- `designShipForRefit`: baseline/original design used for comparison while calculating refit cost/time.
- `refitDesignListID`: saved design list identifier for the refit design.
- `savedSpeedForRefit`: remembered speed value during refit editing.
- `isRefitSimple`: whether vanilla considers the refit a simple refit.
- `refitProgress`: progress toward completing the live refit.
- `refitDesignName`: display name for the target refit design.
- `designRefitTime`: design-side refit time calculation.
- `dateCreatedRefit`: campaign date used for refit naming and dating.
- `refitEdgeMainGuns`: stored old longitudinal positions for main guns.
- `refitEdgeBarbettes`: stored old barbette position/caliber constraints.
- `refitEdgeFunnel`: stored old longitudinal positions for funnels.
- `refitEdgeMainTower`: stored old longitudinal positions for towers.

The existence of `refitEdgeFunnel` and `refitEdgeMainTower` matters. Vanilla has explicit refit-mode concepts for funnels and towers, not only guns.

### PartData

Parts are represented by `PartData`. Relevant observed fields and concepts include:

- `type`: part category such as `tower_main`, `tower_sec`, `funnel`, `barbette`, `gun`, etc.
- `nameUi`: localized/display-facing name reference.
- `param`: behavior flags and modifiers.
- `isHull`, `isTowerMain`, `isTowerAny`, `isFunnel`, `isBarbette`, `isGun`, `isWeapon`: cached type booleans.
- `cost`, `weight`, `crew`, `caliber`, `barrels`: stats used by design and construction logic.
- `NeedUnlock`: explicit unlock requirements.
- `needTags`: hull/tag compatibility requirements derived from data.
- `excludeTags`: hull/tag exclusions.
- `countriesx`: country restrictions.
- `mountPoints` and `mounts`: model/mount compatibility requirements.
- tonnage, beam, draught, and ship-type constraints.
- `Generation`, `constructorShip`, `isCampaignShipyardGood`, and `Count`: additional constructor and campaign eligibility fields.

The key issue for superstructure refits is that newer towers and funnels are usually not merely "researched components." They are `PartData` records with compatibility gates tied to hull tags, country, model mounts, and unlock data.

### Mounts

Mounts define where a part can attach and what can fit there. Relevant mount concepts include:

- centerline/side placement.
- barbette and casemate placement.
- main tower, secondary tower, and funnel mount categories.
- caliber minimum/maximum.
- barrel-count restrictions.
- collision and fitting restrictions.

Even if a newer tower/funnel is made visible in the constructor, it still may not place unless the hull exposes a compatible mount and the model collision rules accept it.

### RandPart and Refit Rules

`randParts.csv` and `randPartsRefit.csv` define automatic design generation and automatic refit generation patterns.

Relevant observed `RandPart` fields include:

- `shipTypes`
- `chance`
- `min` / `max`
- `type`
- `paired`
- `group`
- `effect`
- `center` / `side`
- `rangeZFrom` / `rangeZTo`
- `condition`
- `param`
- `demandMounts`
- `excludeMounts`

`randPartsRefit.csv` is especially important because it shows vanilla's AI/generator layer already understands refit replacement of towers and funnels. That is separate from whether the player can manually select every desirable historical reconstruction part.

## Vanilla Refit Flow

### 1. Entering Refit Constructor

The main entry point observed is `Ui.RefitShip(Ship ship = null)`.

The vanilla flow, simplified:

1. Set constructor refit mode on the UI.
2. Determine the source ship. If no ship argument is passed, vanilla uses the current `PlayerController` ship.
3. Clone the selected ship into an editable design-like ship through `PlayerController.CloneShipRaw(...)`.
4. Clone the current constructor ship/design as `designShipForRefit`; this becomes the baseline used for comparisons.
5. Collect old part material/weight information into `designShipForRefit.oldPartsWeightChangeHull`.
6. Call `Ship.GrabTechs(...)` so the refit design reflects current tech state.
7. Set refit creation date from the campaign date.
8. Generate a refit name suffix via `Ship.GetRefitYearNameEnd(...)`.
9. Store speed/refit-name context.
10. Enter constructor mode with the cloned refit design.
11. Call `Ship.CalculateRefitZones()`.

This means the player is editing a cloned ship in a special constructor mode. The refit baseline is preserved for cost, time, and placement comparisons.

### 2. Calculating Refit Zones

`Ship.CalculateRefitZones()` scans the current ship's parts and records the previous longitudinal positions of important refit-limited parts.

Observed lists:

- `refitEdgeMainGuns`: old main-gun Z positions.
- `refitEdgeBarbettes`: old barbette Z positions plus caliber-related constraints.
- `refitEdgeFunnel`: old funnel Z positions.
- `refitEdgeMainTower`: old tower Z positions.

Observed behavior:

- Main guns are recorded when `PartData.isGun` and `Ship.IsMainCal(...)` match.
- Barbettes are recorded when `PartData.isBarbette` matches, with additional caliber/mount context.
- Funnels are recorded when `PartData.isFunnel` matches.
- Towers are recorded when tower-main / tower-any style flags match.

This strongly suggests vanilla intended tower and funnel replacement to be allowed in refits, but constrained near the old layout.

### 3. Part Placement In Refit Mode

`Part.CanPlace(...)` contains special logic for constructor refit mode.

Before refit-specific logic, vanilla still performs generic placement checks through `Part.CanPlaceGeneric(...)`. Observed generic denial reasons include:

- `"available"`: part is not available for the ship/context.
- `"amount"`: part count limit is exceeded.
- `"mount"`: required mount is unavailable or incompatible.

In refit mode, vanilla adds old-position checks for several major part classes:

- Main guns use old main-gun positions.
- Barbettes use old barbette positions and caliber constraints.
- Funnels use old funnel positions.
- Towers use old tower positions.

The relevant vanilla params are:

- `refit_configurable_z_gun,30`
- `refit_configurable_z_barbettes,50`
- `refit_configurable_z_funnel,35`
- `refit_configurable_z_tower,30`

If the replacement is too far away from the previous location, vanilla sets the denial reason to:

- `"Too far from previous place"`

If caliber limits fail around barbettes/guns, vanilla can also use:

- `"exceededPermissibleCaliberSize"`

Implication: vanilla does not appear to forbid tower/funnel replacement categorically. It allows it within old-location windows, assuming the part is available and mount-compatible.

### 4. Simple Versus Major Refit

`Ship.IsSimpleRefitShip(refitDesign)` checks whether a refit is simple.

Observed behavior:

- Vanilla compares the original/current ship's part names against the refit design's part names.
- If the part count differs, or if an original part name is missing from the target design, the refit is not simple.
- When false, `isRefitSimple` is set false.

Implication for this feature:

- Replacing towers/funnels with newer part names should normally classify as a major refit.
- That is good. A superstructure reconstruction should not receive the cheap simple-refit path.

### 5. Refit Time And Cost

Observed params:

- `refit_cost_mult,1`
- `refit_simple_mult,0.05`
- `refit_time_simple_mult,0.05`
- `refit_time_mult,0.3`
- `refit_time_modifier,0.3`

`Ship.RefitTime(...)` uses the simple-refit multiplier when `isRefitSimple` is true. Otherwise it uses the major-refit multipliers. It also compares design changes against `designShipForRefit`, including old material/part weight data.

If the original baseline cannot be resolved, vanilla can fall back to a build-time-derived calculation.

Implication:

- Letting the player swap a tower/funnel through the normal refit design should flow into vanilla's refit-time machinery.
- We should still test whether the cost/time delta is reasonable, because newly exposed parts might not interact with material-weight comparison exactly as expected.

### 6. Applying The Refit To Live Ships

The main observed flow is `PlayerController.RefitShipsStart(List<Ship> shipList, Ship refitDesign, bool isPlayer = true)`.

For each selected live ship, vanilla roughly:

1. Computes simple/major refit status via `Ship.IsSimpleRefitShip(refitDesign)`.
2. Resolves the live ship object.
3. Calls `Ship.RemoveAllParts(true)`.
4. Calls `Ship.RemoveAllComponents()`.
5. Calls `Ship.ChangeRefitShipTech(refitDesign)`.
6. Iterates through `refitDesign.hullAndParts`.
7. Creates each design part on the live ship with `Part.Create(...)`.
8. Copies transform position and rotation from the refit design part.
9. Adds the part to the live ship.
10. Calls `Ship.ChangeRefitShip(refitDesign)`.
11. Sets vessel status to refit.
12. Sets `isRefitSimple`, resets refit progress, clears refit paused state.
13. Copies refit design ID/name metadata.
14. Stores the updated ship.

This is a full application of the new refit design, not a cosmetic overlay. Therefore, if the constructor lets the player build a valid design with newer towers/funnels, the live refit application path should naturally carry those parts forward.

### 7. Save/Store Behavior

`Ship.Store` persists refit metadata including:

- `refitProgress`
- `isRefitPaused`
- `isRefitSimple`
- `refitDesignName`
- `designRefitTime`
- `refitDesignListID`
- `dateCreatedRefit`

It also persists core ship/design identity and physical contents such as:

- `parts`
- `components`
- `techs`
- `hullName`
- `tonnage`
- `beam`
- `draught`

Implication:

- If a refit design is successfully saved with a newer tower/funnel part, that part should persist by normal ship storage.
- A compatibility-expansion patch must consider future edits. If the patch is disabled later, saved ships may still contain the parts, but the constructor may no longer allow editing or reselecting those parts.

## Vanilla Availability Gates

The relevant part availability path includes `Ship.IsPartAvailable(...)` and lower-level/basic checks.

Observed/expected gates include:

- Current game mode: campaign, custom battle, mission, or constructor context.
- Player/country context.
- Ship type.
- Part enabled flag.
- Tech and unlock requirements.
- Hull tags through `need(...)`.
- Exclusion tags through `exclude(...)`.
- Country restrictions.
- Tonnage, beam, draught, and hull constraints.
- Part count limits.
- Mount compatibility.

This matters because the phrase "newer towers and funnels" can mean several different vanilla filters:

- The part exists, but is not unlocked by current tech/year.
- The part is unlocked, but only for a newer hull's tags.
- The part is unlocked and tag-compatible, but needs a different mount.
- The part can be placed, but only within a tight refit Z window.
- The part is shown in generated refits but hidden or denied in manual constructor UI.

Any implementation needs to distinguish these cases.

## How Vanilla Governs Towers And Funnels

### Part Counts In Local Vanilla Data

From `parts.csv`, tower/funnel part counts in the local vanilla reference are:

- `tower_main`: 923
- `tower_sec`: 545
- `funnel`: 665

For tower/funnel parts, observed gate grouping was:

- Explicit `need(...)` tags: 1347
- Explicit `needunlock(...)` entries: 786

There were no obvious tower/funnel parts with no explicit need-style gating in the local data scan. In practice, tower and funnel availability is heavily hull/tag/unlock driven.

### Refit Generator Knows About Towers And Funnels

From `randPartsRefit.csv`, refit generator entries by type include:

- `gun`: 1006
- `barbette`: 263
- `funnel`: 204
- `tower_main`: 183
- `tower_sec`: 151
- `torpedo`: 117
- `special`: 1

Early representative `bb/bc` refit rows include:

- `tower_main` with `auto_refit, delete_refit`
- `tower_sec` with `auto_refit, delete_refit`
- `funnel` with `auto_refit, delete_refit`

Many more refit rows use hull-family conditions such as:

- `barbette_need`
- `Command_Forward`
- `hullbarbette`
- `newyork_style`
- `danton_style`
- `Russian_Central_Turrets`
- `old_predread`
- `battlecruiser_forward`
- `Yamato_Style`
- `BB_Fuso`
- `g3`

Interpretation:

- Vanilla's automatic refit system already includes tower/funnel replacement rules.
- Those rules are not broad free-form modernization rules. They are constrained by hull tags and conditions.
- `delete_refit` likely marks old refit parts for removal/replacement in the generated refit flow. This should be verified before relying on it for any manual feature.

### Refit Placement Windows

Vanilla params:

- Towers: 30 units forward/aft from previous tower positions.
- Funnels: 35 units forward/aft from previous funnel positions.
- Main guns: 30 units.
- Barbettes: 50 units.

Practical result:

- A newer tower/funnel that fits the old hull and mount may still be denied if its valid mount position is too far from the old tower/funnel position.
- Large historical reconstructions may require broader movement than vanilla permits.
- A mod should avoid globally changing these params if possible, because they affect all refits and likely the AI too.

## Why Players Get Stuck With Old Towers/Funnels

The likely vanilla reasons are layered:

1. Existing refit design editing starts from the old hull and does not normally transform that hull into a newer hull definition.
2. Older hulls retain old hull tags.
3. Newer tower/funnel parts are usually gated to newer or modernized hull tags.
4. Modernized historical hulls often exist as separate hull definitions rather than as allowed part sets on earlier hull definitions.
5. Even if a part is researched, it can still fail `need(...)`, `needunlock(...)`, country, ship type, mount, amount, or refit-Z checks.
6. VP's existing obsolete tech/hull retention work helps with obsolete filtering, but does not automatically grant cross-hull tower/funnel compatibility.

The bottleneck is therefore mostly constructor/refit-design eligibility, not the final live-ship application step.

## Japanese Modernization Example

The user example mentioned Japan's 1930s reconstructions. The local vanilla reference data supports the idea that vanilla represents at least some of this through separate modernized hulls and tags.

Representative Japanese hull records:

- `bc_3_kongo`: `Battlecruiser IV`
  - Tags include `BB_Fuso_Old`, `bc`, `g3`, `battlecruiser_forward`, `barbette_need`, `RussianCenterline`.
- `bc_4_kongo`: `Battlecruiser V`
  - Tags include `BB_Fuso_Old`, `BB_Fuso_sec`, `bc`, `g3`, `battlecruiser_forward`, `barbette_need`, `RussianCenterline`.
- `bc_5_kongo`: `Modernized Battlecruiser`
  - Tags include `BC_Modernized_Japan`, `bc`, `g3`, `battlecruiser_forward`, `barbette_need`, `RussianCenterline`.
- `bb_3_japan`: `Dreadnought IV`
  - Tags include `BB_Fuso_Old`, `barbette_need`, `RussianCenterline`, `bb`, `g3`.
- `bb_4_japan_3`: `Modernized Dreadnought III`
  - Tags include `BB_Fuso`, `barbette_need`, `bb`, `g3`.
- `bb_4_japan_wide`: `Modernized Dreadnought IV`
  - Tags include `BC_Modernized_Japan`, `bb`, `g3`, `battlecruiser_forward`, `barbette_need`, `RussianCenterline`.

This suggests a likely vanilla pattern:

- Some modernized superstructure options are tied to modernized hull tags like `BC_Modernized_Japan` or `BB_Fuso`.
- Earlier hulls may share broad tags like `g3`, `bb`, or `bc`, but still lack the specific modernized tags that unlock later superstructures.
- Therefore a ship built on the older hull may not see the later tower/funnel family even if a historically related modernized hull exists in the data.

That is exactly the sort of place where a carefully curated compatibility bridge could help.

## Candidate Implementation Directions

### Direction A: Refit-Only Part Availability Expansion

Expose selected newer tower/funnel parts only while editing a refit design.

Likely condition set:

- Player-only.
- Option-gated.
- Constructor refit mode only.
- Part type limited to:
  - `tower_main`
  - `tower_sec`
  - `funnel`
- Preserve normal research/tech unlock gates.
- Preserve country gates.
- Preserve ship type gates.
- Preserve mount and count checks.
- Soften only the hull/tag compatibility layer through curated hull-family mappings.

This is the safest first implementation shape because it keeps the rest of vanilla's refit machinery intact.

Potential hooks:

- Postfix `Ship.IsPartAvailable(PartData)` or a lower-level availability method.
- Patch the in-memory part compatibility data after data post-processing.
- Add a dedicated helper used by a postfix to decide whether the current old hull can borrow a modernized family part.

Pros:

- Minimal change to live ship application.
- Keeps manual player control.
- Lets vanilla cost/time/save paths work normally.
- Easier to gate by option and player.

Cons:

- Need to handle UI refresh and part list visibility.
- Need to map hull families carefully.
- May still hit mount or placement denial after availability is expanded.

### Direction B: Refit Placement Relaxation For Towers/Funnels

If Direction A exposes parts but placement fails with `"Too far from previous place"`, add option-gated refit placement relaxation.

Preferred shape:

- Patch only in constructor refit mode.
- Patch only for player designs.
- Patch only for `tower_main`, `tower_sec`, and `funnel`.
- Avoid changing global `params.csv` values.
- Prefer a local override in `Part.CanPlace(...)` logic or a narrow postfix/transpiler around the refit-Z denial.

Possible behavior:

- Towers: allow a larger Z window if the part fits a valid tower mount.
- Funnels: allow a larger Z window if the part fits a valid funnel mount.
- Keep broad movement as major refit, never simple refit.

Pros:

- Allows historically bigger reconstructions.
- Avoids broad global changes.

Cons:

- `Part.CanPlace(...)` may be a harder patch target.
- Too much relaxation could permit absurd layouts.
- Needs hands-on constructor testing.

### Direction C: Curated Hull-Family Compatibility Map

Create explicit modernization bridges between old and newer hull tag families.

Example concept:

- Japanese older Kongo/Fuso-style hull tags can borrow selected later Japanese modernized tower/funnel tags once the required tech is unlocked.
- The mapping should be from old hull family to allowed modernized family, not "all Japanese towers fit all Japanese hulls."

Pros:

- Best historical fit.
- Reduces silly cross-era/cross-model combinations.
- Easier to explain in release notes.

Cons:

- Requires data work.
- Needs many hull-family mappings to feel complete.
- Vanilla tags are not always semantic or cleanly named.

### Direction D: Automatic Reconstruction Helper

Add a button or option that automatically selects better compatible towers/funnels during refit, possibly using `randPartsRefit.csv` logic as a guide.

Pros:

- Helps players who do not want to hunt through part lists.
- Could mimic vanilla auto-refit behavior.

Cons:

- Higher risk and more UI work.
- Easy to make bad choices.
- Needs careful control over what gets deleted and replaced.
- Not necessary for a first version if manual selection works.

### Direction E: Full Hull Modernization / Hull Swap

Allow a refit to transform an old hull into a newer modernized hull definition.

Pros:

- Most historically faithful for cases where vanilla models the modernization as a separate hull.
- Could unlock correct mounts, tower positions, and funnel arrangements naturally.

Cons:

- Much higher risk.
- Hull swap affects sections, tonnage, beam, draught, armor, mount geometry, saved parts, and refit time.
- Existing parts may not transfer cleanly.
- Needs deep validation in campaign saves, battles, constructor UI, and design list storage.

This should be treated as a later project, not the first approach.

## Recommended First Mod Shape

The best first implementation appears to be:

Feature name candidate:

- `Superstructure Refits`

Option shape:

- Default: off.
- Player-only.
- Campaign constructor/refit only.
- Manual refit design editing only at first.
- No AI behavior changes.

Initial scope:

- Allow historically related newer towers and funnels in refit mode:
  - `tower_main`
  - `tower_sec`
  - `funnel`
- Preserve:
  - tech unlocks
  - country restrictions
  - ship type restrictions
  - mount checks
  - part count checks
  - refit cost/time path
- Add placement relaxation only if evidence shows availability alone is insufficient.

Implementation strategy:

1. Add a read-only debug/dump path first, not a behavior change.
2. Dump why candidate tower/funnel parts are hidden or denied for representative old hulls.
3. Build a curated compatibility map from old hull tags to modernized hull tags.
4. Patch part availability only in player refit constructor mode.
5. Test whether vanilla placement windows are sufficient.
6. Add narrow placement relaxation only if necessary.

Why this shape:

- It targets the true vanilla bottleneck.
- It lets vanilla apply, store, price, and complete the refit.
- It avoids broad AI and global-data disruption.
- It aligns with VP's preference for balance-sensitive features to be option-gated and player-only when AI cannot reasonably use them.

## Investigation Checklist Before Coding

### Constructor Availability

For representative old hulls, log tower/funnel candidates and denial causes:

- candidate part ID
- candidate type
- candidate country restrictions
- candidate `need(...)` tags
- candidate `needunlock(...)` requirements
- candidate `exclude(...)` tags
- current hull name
- current hull tags
- current country
- current year/date
- current researched tech state
- `Ship.IsPartAvailable(...)` result
- generic placement result
- refit placement result
- denial reason if available

Recommended initial Japanese test hulls:

- `bc_3_kongo`
- `bc_4_kongo`
- `bc_5_kongo`
- `bb_3_japan`
- `bb_4_japan_3`
- `bb_4_japan_wide`

### Manual UI Behavior

Verify:

- Are newer towers/funnels hidden entirely, or shown but disabled?
- Does the constructor category list filter them before `Ship.IsPartAvailable(...)`?
- Does removing an old tower/funnel in refit mode work normally?
- Does placement fail because of availability, amount, mount, or refit-Z distance?
- Does the UI refresh immediately after toggling any new option?

### Refit Application

After a test design can be created:

- Start refit on one live ship.
- Confirm status becomes refit.
- Confirm `refitDesignName`, `isRefitSimple`, `refitProgress`, and stored refit metadata look sane.
- Advance until completion if practical.
- Confirm the completed ship retains the new tower/funnel.
- Load a battle and confirm models/materials load correctly.
- Reopen the design/ship in UI and confirm the parts are still there.

### Cost And Time

Check:

- Does replacing a tower/funnel make `IsSimpleRefitShip(...)` return false?
- Does refit time use major-refit multipliers?
- Does cost/time feel plausible compared to replacing guns/barbettes?
- Does removing/replacing multiple superstructure parts create extreme or zero-time results?

### Save Compatibility

Check:

- Save after creating the refit design.
- Reload and inspect the refit design.
- Save during live refit.
- Reload and inspect the live refit.
- Save after completion.
- Reload and inspect final ship.

### Logs

Check `Latest.log` after each focused test for:

- constructor exceptions
- missing mount errors
- missing model/material errors
- part availability exceptions
- refit start/complete exceptions
- battle-load model errors

## Risks

### Over-Broad Compatibility

If we simply allow all newer towers/funnels for all older hulls of the same country, the constructor may expose nonsense combinations. Some may place visually but be historically or mechanically absurd.

Mitigation:

- Use curated hull-family mappings.
- Start with a narrow set of verified families.
- Log every compatibility bridge that is applied.

### Mount And Model Mismatch

A part can be theoretically desirable but practically incompatible with an old hull's mounts or model geometry.

Mitigation:

- Preserve mount checks.
- Only relax hull/tag availability first.
- Add placement relaxation only after concrete evidence.

### Refit-Z Constraints

Historical reconstructions could move superstructure significantly. Vanilla restricts towers to 30 and funnels to 35 Z units from previous positions.

Mitigation:

- Observe real placement failures first.
- If necessary, patch refit-Z checks narrowly instead of editing global params.

### AI Design Quality

The AI already struggles with designs. Giving the AI broader superstructure refit freedom could produce broken ships or bad layouts.

Mitigation:

- Player-only first.
- Do not alter auto-refit generation until manual player behavior is validated.

### Cost/Time Balance

If vanilla underprices exposed tower/funnel swaps, refits may become too cheap.

Mitigation:

- Confirm major-refit classification.
- Add an option-specific modifier only if vanilla pricing proves too generous.

### Future Edits After Option Disabled

If a player creates a ship with expanded compatibility and later disables the option, the ship may still load, but editing/recreating that design may become constrained again.

Mitigation:

- Document the option behavior.
- Keep saved existing parts loadable; only control new selection/placement.

## Open Questions

- Should this be a purely manual constructor option, or should there eventually be an automatic "modernize superstructure" helper?
- Should the first implementation focus on Japan/Kongo/Fuso-style cases, or should it launch only after a broader hull-family map exists?
- Should secondary towers be included immediately? The data has `tower_sec` refit rows, so the answer is probably yes, but it needs testing.
- Should barbettes be included later? They are part of historical reconstructions, but they carry more gun/caliber balance risk.
- Should refit placement allow adding additional towers/funnels, or only replacing existing ones?
- Should the option apply only to active campaign refits, or also to custom-battle/design editing?
- How visible should this be in UI: one setting, multiple settings, or a small note in design/refit tooltips?

## Proposed Work Plan

### Phase 0: Baseline Logging

Add temporary/refined diagnostics to dump tower/funnel candidate availability in refit constructor mode.

Output should answer:

- Which newer tower/funnel parts exist for the country and ship type?
- Which are hidden because of tech?
- Which are hidden because of hull tags?
- Which are visible but cannot place?
- Which fail specifically because of refit-Z limits?

No behavior changes in this phase.

### Phase 1: Refit-Only Manual Availability

Implement the option-gated, player-only availability bridge for `tower_main`, `tower_sec`, and `funnel`.

Keep:

- tech gates
- country gates
- ship type gates
- mount gates
- count gates

Relax:

- selected hull-family `need(...)` tag restrictions, only through curated mappings.

### Phase 2: Placement Validation And Narrow Relaxation

If tests show availability is not enough, patch refit placement for towers/funnels.

Preferred behavior:

- Refits still need valid mounts.
- Movement beyond vanilla Z windows is allowed only for the expanded superstructure option.
- No global `params.csv` changes.

### Phase 3: Curated Family Expansion

Build a small compatibility map and grow it with evidence.

Suggested first family:

- Japanese BB/BC modernization cases, because the user-facing motivation and vanilla data both point there.

Then expand to other countries/hull families only after representative testing.

### Phase 4: Refit Cost/Time Review

Validate the balance:

- Simple versus major refit classification.
- Time/cost against comparable vanilla major refits.
- Multiple superstructure part changes.
- Campaign affordability and dockyard duration.

### Phase 5: Campaign/Battle Validation

Run an end-to-end validation loop:

- Create refit design.
- Start refit on live campaign ship.
- Save and reload.
- Complete refit or force progression in a controlled test.
- Load battle.
- Inspect `Latest.log`.

### Phase 6: Optional UX Polish

Only after the core behavior is stable:

- Add clearer option text.
- Add debug logging behind a setting or conditional.
- Consider auto-selection/helper behavior.
- Consider barbettes as a separate option or later extension.

## Baseline Conclusion

Vanilla's refit system is closer to supporting this than it first appears. It already has tower and funnel refit zones, refit-generator tower/funnel replacement rows, and a full live-ship replacement path that applies the refit design's physical parts.

The restrictive part is the manual design phase: newer superstructure parts are heavily gated by hull tags, unlocks, country, ship type, mounts, and refit placement windows. The safest VP feature would therefore be a default-off, player-only `Superstructure Refits` option that expands manual refit-constructor eligibility for historically compatible towers and funnels while leaving vanilla's application, storage, and cost/time systems mostly intact.
