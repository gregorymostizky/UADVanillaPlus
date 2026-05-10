# Vanilla Battle Flow Reference

This note maps how vanilla Ultimate Admiral: Dreadnoughts wires custom battles
and normal campaign surface battles into the playable battle scene. The goal is
to give UAD:VP a concrete reference before we generate our own battles.

The source is a mix of diffable C# skeletons, ISIL call flow, and the current
VP test-battle experiment. Treat the exact inner order of large compiler
iterator methods as something to re-check before patching them directly, but the
data contracts and top-level handoffs below are stable enough to design from.

## Source Map

- `E:\Codex\cpp2il_uad_diffable\DiffableCs\Assembly-CSharp\GameManager.cs`
- `E:\Codex\cpp2il_uad_diffable\DiffableCs\Assembly-CSharp\BattleManager.cs`
- `E:\Codex\cpp2il_uad_diffable\DiffableCs\Assembly-CSharp\CampaignBattleBase.cs`
- `E:\Codex\cpp2il_uad_diffable\DiffableCs\Assembly-CSharp\CampaignBattle.cs`
- `E:\Codex\cpp2il_uad_diffable\DiffableCs\Assembly-CSharp\Division.cs`
- `E:\Codex\cpp2il_uad_diffable\DiffableCs\Assembly-CSharp\Ui.cs`
- `E:\Codex\cpp2il_uad_isil\IsilDump\Assembly-CSharp\BattleManager.txt`
- `E:\Codex\cpp2il_uad_isil\IsilDump\Assembly-CSharp\BattleManager_NestedType__PrepareBattle_d__67.txt`
- `E:\Codex\cpp2il_uad_isil\IsilDump\Assembly-CSharp\BattleManager_NestedType__PrepareBattleFromSave_d__65.txt`
- `E:\Codex\cpp2il_uad_isil\IsilDump\Assembly-CSharp\GameManager_NestedType__UpdateLoadingBattle_d__117.txt`
- `E:\Codex\UADVanillaPlus\UADVanillaPlus\Harmony\CampaignDesignTestBattlePatch.cs`
- `E:\Codex\UADVanillaPlus\UADVanillaPlus\Harmony\DesignHullColorProofPatch.cs`

## Mental Model

Vanilla has three related but different battle paths:

1. Custom battle setup from the UI: the setup screen creates a
   `Ui.SkirmishSetup`, then `BattleManager.StartCustomBattle` builds temporary
   players and ships before entering battle.
2. Saved custom battle or academy battle: a `GameManager.RealBattleSave`
   already contains the designs, battle ship stores, divisions, projectiles,
   statistics, and weather needed to reconstruct a battle.
3. Campaign battle: campaign logic creates a `CampaignBattle` that references
   real campaign `Ship` objects and task-force/group state. Playing the battle
   loads those existing ships into the scene; finishing it writes results back
   into the campaign.

For generating our own battle, the saved-custom-battle path is the cleanest
contract because it can be built as a self-contained payload. The campaign path
is more dangerous because vanilla assumes task forces, campaign ship ownership,
map groups, battle removal, victory points, crew losses, and damage persistence
all exist and need to be mutated when the battle ends.

## Game State Hand-Offs

Relevant `GameManager.GameState` values:

- `CustomBattleSetup` is the setup UI.
- `LoadingCustom` is the load phase for custom/mission battles.
- `Loading` is the regular load phase.
- `World` is the campaign map.
- `Constructor` is the ship designer.
- `Battle` is the playable battle scene.

Relevant `GameManager.UIState` values:

- `BattleStart` is the pre-combat battle UI state.
- `BattleCombat` is the active combat UI state.

The top-level transition methods are:

- `GameManager.ToCustomBattleSetup()`
- `GameManager.ToCustomBattle(bool doBuild = true, bool isRestart = false)`
- `GameManager.ToCustomBattleFromSave(GameManager.RealBattleSave save)`
- `GameManager.ToBattle(CampaignBattle battle, float? duration = null,
  float? distance = null, float? spread = null, RealBattleSave save = null)`
- `GameManager.LoadCustom(IEnumerator routine)`
- `GameManager.UpdateLoadingBattle(RealBattleSave save = null)`

`GameManager` owns scene/state changes. `BattleManager` owns battle data,
custom-battle preinit, weather, division initialization, battle preparation, and
completion.

## Core Data Contracts

### Custom Battle Setup

`Ui.SkirmishSetup` is the custom battle setup screen payload.

It contains:

- `player1` and `player2`, each with:
  - `country: PlayerData`
  - `year`
  - `shipAmounts: Dictionary<ShipType, int>`
  - `isHullAvailable: Dictionary<ShipType, bool>`
- `distance`
- `sharedDesignsUsage`
- `weather`
- `daytime`

The setup UI mutates this object through `SkirmishSetupInit`,
`SkirmishSetupManual`, `SkirmishSetupRandomize`, `SkirmishSetupClear`, and
`SkirmishSetupUI`. `BattleManager.CanStartCustomBattle` and
`CanStartCustomBattleBasic` validate whether the selection can launch.

### Saved Real Battle

`GameManager.RealBattleSave` is the serialized/reloadable battle contract.

It contains:

- battle metadata: `Version`, `IsAcademyMission`, `Mission`, `DateTicks`,
  `FriendlyName`, `StartYear`, `MissionChoiceIndex`, and `Difficulty`;
- two sides: `Player` and `Enemy`, both `GameManager.RealBattlePlayer`;
- `Divisions: List<Division.Store>`;
- `BattleTimer`;
- `MainShip`, which is a battle ship id;
- `Weather: BattleManager.WeatherStore`;
- saved weapon statistics;
- saved `Torpedos` and `Shells`.

`GameManager.RealBattlePlayer` contains:

- `Country`
- `ShipDesigns: List<Ship.Store>`
- `Ships: List<Ship.BattleStore>`
- `ShipAmounts: Dictionary<string, int>`
- `IsHullAvailable: Dictionary<string, bool>`

This is the format VP currently uses for the design-test battle button:
`CampaignDesignTestBattlePatch.BuildSave` creates a `RealBattleSave`, fills one
`RealBattlePlayer` for each side, creates one `Division.Store` per side, then
launches through `GameManager.ToCustomBattleFromSave(save)`.

`RealBattleSave.IsValid()` is only used by the saved-slot wrapper path. It
rejects saves with an empty `FriendlyName` and requires
`Version >= GameManager.CustomBattleSaveVersion`. The direct
`ToCustomBattleFromSave(save)` handoff bypasses that validation, so generated
payloads still need to fill the same fields defensively.

### Campaign Battle

`CampaignBattleBase` carries campaign-level battle identity and map context:

- battle and group ids: `Id`, `AttackerGroupId`, `DefenderGroupId`;
- sides: `Attacker`, `Defender`, `ParticipatePlayersAttacker`,
  `ParticipatePlayersDefender`;
- battle kind and state: `Type`, `CurrentState`, `Delay`, `Withdraw`,
  `DelayedCount`, `Escalated`;
- map and timing: `BattleWorldPos`, `LocationId`, `LocationName`, `Date`,
  `RouteStartIndex`;
- outcome fields: `Victor`, `Avoider`, `VictoryPointsAttacker`,
  `VictoryPointsDefender`, `ResultsWasViewed`;
- seed and positioning: `Seed`, `bNeedOverridePositionDirection`,
  `AttackerDirection`, `DefenderDirection`;
- campaign marker: `IsCampaignBattle`.

The serialized `CampaignBattleBase.BaseStore` uses similar but not identical
names, such as `ParticipatePlayersA/B`, `CurrentBattleState`, `BattleDelay`,
`WorldPos`, `Location`, `vpA/vpB`, and `ResultsViewed`. Check which layer a
patch is reading before copying names between store and runtime code.

`CampaignBattle` adds surface-ship lists and battle damage:

- runtime ship lists: `AttackerShips`, `DefenderShips`,
  `AttackerShipsSink`, `DefenderShipsSink`;
- possible reinforcement/additional lists: `ShipsAdditionalAttacker`,
  `ShipsAdditionalDefender`;
- `ActualShips`, `SunkShips`, and cached variants;
- `BattleDamage`, `StartBattleDamage`, `StartShipsAmmoSpent`,
  `StartCrewLosses`;
- serialized/store lists of attacker and defender ship ids.

The important distinction is that a campaign battle references real campaign
`Ship` instances. A saved custom battle references `Ship.Store` and
`Ship.BattleStore` payloads that can reconstruct temporary ships.

### Ships

There are two ship snapshots to keep straight:

- `Ship.Store` is the design/constructor snapshot. It describes what the ship
  is supposed to be: hull, parts, design id, year-ish data, ship type, and
  design settings.
- `Ship.BattleStore` is the battle runtime snapshot. It describes the ship as
  it enters or resumes battle: battle id, design id link, position, rotation,
  speed/order state, firing modes, crew, ammo, damage, sections, module damage,
  targeting/aim state, gun groups, flashfire state, and combat statistics.

`GameManager.LoadShips(savedShips, shipDesigns, owner)` joins battle stores back
to design stores and creates/loads ships for a saved battle. VP's current
test-battle helper should treat `ship.ToBattleStore()` as required. Filling
missing collections can protect against null-list issues after a real battle
snapshot exists, but it cannot turn a skeletal `Ship.BattleStore` into a valid
ship.

The ids must line up:

- `Ship.BattleStore.Id` is the battle-instance id.
- `Ship.BattleStore.DesignId` must match the design store id it should load.
- `Division.Store.Ships` contains battle-instance ids.
- `RealBattleSave.MainShip` is a battle-instance id.
- VP paint/country mapping also keys by battle-instance id for custom battle
  loads.

### Divisions

`Division.Store` is the persisted division contract. It includes:

- `Id`
- `Ships: List<Guid>`
- `Formation`
- `Spread`
- movement state like `IsMovingTo`, `MovingTo`, `IsMovingDir`, `MovingDir`,
  and reverse-speed state;
- `ScoutIdx`;
- relationship ids such as `ScreenDivisionId`, `FollowDivisionId`,
  `ScoutDivisionId`, `FollowingDivisionId`, and `FollowTargetId`.

Runtime `Division` owns the actual `ships` list, the leader, screen/scout/follow
relationships, formation, spread, collision/torpedo behavior, smoke timers, and
order state. `DivisionsManager.Cleanup()` and `DivisionsManager.Init()` run
when a battle is initialized. Preparation then creates divisions and attaches
ships. Reinforcement placement uses the division leader, following ship, and
screen division relationships to place ships and restore formation behavior.

For a generated one-on-one battle, one `Division.Store` per side with one ship,
`Formation.Column`, `Spread.Normal`, and an empty `ScoutIdx` is enough to get a
basic division. More complex generated battles need coherent division ids and
relationship ids, not just ship lists.

### Battlefield And Weather

`BattleManager.Init(CampaignBattle battle, float? duration, float? distance,
float? spread, WeatherStore weather)` establishes the battle shell:

- sets `CurrentBattle`;
- chooses `StartDistance` and `StartSpread`;
- creates the combat timer;
- calls `DivisionsManager.Cleanup()`;
- calls `DivisionsManager.Init()`;
- applies weather through `BattleManager.SetWeather(updateAll: false, store)`.

Campaign battles can derive distance from campaign parameters such as
`campaign_battle_distance_min` and `campaign_battle_distance_max`, plus
`BattleTypeEx` data. Custom battles can pass setup distance/weather more
directly.

`BattleManager.WeatherStore` contains:

- `WavesPower`
- `PresetIndex`
- `Time`
- `RelativeTime`
- `CloudsDensity`
- `StormIntensity`
- `DesiredStormIntensity`
- `Wind`

Saved battle preparation restores weather through the saved store and
`DayCycleAndWeather.InitFromSave`. VP's current test-battle patch also reapplies
clear weather after battle entry because vanilla scene entry can still override
some visual weather state.

## Vanilla Custom Battle Setup Flow

The normal UI-driven custom battle path is:

1. The user enters `CustomBattleSetup`.
2. `Ui.SkirmishSetup` is created or refreshed by the skirmish setup UI.
3. The setup object records each side's country, year, selected ship counts,
   hull availability, shared-design preference, distance, weather, and daytime.
4. `BattleManager.CanStartCustomBattle` validates that the setup can launch.
5. The launch button calls
   `BattleManager.StartCustomBattle(skirmishSetup, doBuild)`.
6. `StartCustomBattle` logs the full setup name and calls
   `PreInitCustomBattle(skirmishSetup)`.
7. `PreInitCustomBattle` creates/configures temporary custom-battle players,
   available hulls, selected years, countries, and the custom-battle environment.
8. `StartCustomBattle` loads shared/manual designs for the selected countries
   and ship counts.
9. If a design must be edited or created, vanilla enters the constructor through
   `GameManager.ToConstructor(...)`.
10. If no constructor step is needed, vanilla continues through
    `GameManager.ToCustomBattle(doBuild, isRestart)`.
11. `ToCustomBattle` routes into `LoadCustom` with
    `BattleManager.UpdateLoadingCustomBattle(...)`.
12. `UpdateLoadingCustomBattle` creates the temporary battle ships from the
    selected designs and ship counts, registers them as custom-battle ships, and
    then hands off to the normal battle loading path.
13. The battle scene load uses `GameManager.UpdateLoadingBattle(...)` and
    `BattleManager.PrepareBattle()` to spawn ships, create divisions, apply
    start damage/ammo, set weather, and enter battle UI.

This path is good for the vanilla custom-battle screen, but it is awkward for
external generation because it expects setup UI state, temporary player setup,
shared-design lookup, and optional constructor behavior.

## Saved Custom Battle Flow

The saved custom battle path is more directly useful for generation, but vanilla
has two related entry points that should stay separate:

Full saved-slot wrapper:

1. Caller passes a `GameManager.RealBattleSave` to
   `GameManager.LoadCustomBattle(save)`.
2. `LoadCustomBattle(save)` calls `save.IsValid()` and returns early unless the
   save has a non-empty `FriendlyName` and a compatible
   `GameManager.CustomBattleSaveVersion`, then sets up the custom-battle
   players.
3. `BattleManager.InitCustomBattleFromSave(startYear, mainEnemy)` initializes
   custom-battle context without going through the setup UI.
4. `GameManager.LoadShips(...)` loads the player/enemy saved ships and designs.
5. The wrapper then starts `BattleManager.UpdateLoadingCustomBattleFromSave(save)`
   and enters the custom battle loading state.

Direct handoff:

1. Caller builds or loads a `GameManager.RealBattleSave`.
2. Caller must already have usable player/enemy context and matching `Ship`
   instances visible through the fleets vanilla will inspect during load.
3. Caller invokes `GameManager.ToCustomBattleFromSave(save)`.
4. `ToCustomBattleFromSave` performs the same `LoadingCustom` state handoff
   pattern as `GameManager.LoadCustom(...)`, then runs
   `BattleManager.UpdateLoadingCustomBattleFromSave(save)`. It does not call
   `save.IsValid()`.
5. `UpdateLoadingCustomBattleFromSave(save)` builds a transient
   `CampaignBattle` from the current custom-battle players/fleets and passes
   the save into `GameManager.ToBattle(..., save)`.
6. `GameManager.UpdateLoadingBattle(save)` loads the battle scene.
7. `BattleManager.PrepareBattleFromSave(save)` reconstructs battle ships,
   divisions, projectile/torpedo state, statistics, battle timer, main ship,
   weather, and battle UI state.

`PrepareBattleFromSave` drives saved-ship restoration through
`Ship.EnterBattle(BattleStore)` and the ship battle setup path, including
per-part `Part.LoadBattle` / `Part.LoadBattle2` hooks, then restores projectiles
and weather. This is why `RealBattleSave` is the most promising payload for
generated battles: it lets us supply the completed battle snapshot instead of
asking vanilla to derive one from UI setup.

The catch is that this contract is exacting. Null collections, missing ids,
missing ammo/module/section state, or mismatched design ids can fail late during
scene load. VP's test-battle patch already carries defensive helpers for this.

## Campaign Battle Execution Flow

The normal campaign surface-battle path is:

1. Campaign logic creates a `CampaignBattle` with attacker/defender players,
   task-force group ids, participating player bits, world position, battle type,
   date, route/location data, and attacker/defender ship lists.
2. Campaign UI presents the battle. `BattlePopup.Init(CampaignBattle)` is the
   visible decision point for the player.
3. When the battle is accepted, `BattleManager.AcceptBattle(battle, isAi,
   autoResolve, fromUi)` is called.
4. `AcceptBattle` logs attacker/defender names, type, and ship lists.
5. If `autoResolve` is true, it calls `BattleManager.AutoResolveBattle(...)`.
   That path calculates outcome/damage without entering the playable scene.
6. If the battle is played, it calls `GameManager.ToBattle(battle, duration,
   distance, spread)`.
7. `ToBattle` marks the selected campaign ships for battle and enters the
   loading state.
8. `BattleManager.Init(...)` stores `CurrentBattle`, chooses distance/spread,
   starts the combat timer, initializes divisions, and applies weather.
9. `GameManager.UpdateLoadingBattle(null)` loads the battle scene.
10. `BattleManager.PrepareBattle()` uses `CurrentBattle.AttackerShips`,
    `CurrentBattle.DefenderShips`, additional/reinforcement lists, battle type,
    start positions, directions, and side bits to enter real campaign ships into
    the battle scene.
11. Preparation creates divisions, positions ships around attacker/defender
    starts, applies existing battle damage, records start damage/ammo/crew state,
    and transitions into battle UI.
12. When the battle ends, the finish path calls
    `BattleManager.CompleteBattle(CampaignBattle, ...)`. In campaign state this
    refreshes ship status and runs relation, damage, crew-training, and salvage
    bookkeeping. Battle removal, task-force cleanup, result screens, and return
    flow sit in the broader campaign/battle-finish path rather than in
    `CompleteBattle` alone.

The campaign path is therefore not just "spawn these ships and fight." It is
the campaign simulation's live battle transaction. Any generated battle that
uses this path must either be a real campaign battle with real campaign
bookkeeping, or must intercept/neutralize the completion assumptions.

## Ship Placement And Division Creation

The battle-preparation code computes an attacker start position and defender
start position based on battle distance, spread, type, and direction. It keeps a
list of in-battle ship positions to avoid collisions and then enters ships into
the scene.

The reinforcement helper shows the same assumptions clearly:

- choose side bits with `CampaignBattleBase.AttackerBits` or `DefenderBits`;
- find the flagman with `DivisionsManager.Flagman`;
- use division leader/following-ship information when available;
- add randomized offset and angle;
- call `Ship.EnterBattle`;
- apply existing overall damage;
- create/attach divisions through `DivisionsManager.Create`;
- calculate following positions and screen division relationships;
- move the division forward into formation.

That is the runtime version of what a generated saved battle needs to provide in
serialized form: coherent ship ids, positions, rotations, formation/spread, and
division relationships.

## Current VP Test-Battle Path

`CampaignDesignTestBattlePatch` is already a small generated-battle prototype.
It does not use the full campaign battle completion path. Instead it:

1. lets the player pick two campaign designs;
2. creates temporary campaign `Ship` objects for each side;
3. refreshes each temporary ship's `Ship.BattleStore`;
4. builds a `GameManager.RealBattleSave`;
5. creates one division per side;
6. fills `RealBattlePlayer` data for player and enemy;
7. records real battle countries for paint lookup;
8. calls `GameManager.ToCustomBattleFromSave(save)`;
9. scopes `Player.get_fleet` during load so vanilla only sees the temporary
   test-battle ships;
10. suppresses premature finish briefly and bypasses campaign completion
    bookkeeping for the synthetic battle.

This confirms the likely direction for custom generated battles: construct a
`RealBattleSave` and enter through `ToCustomBattleFromSave`, while patching only
the specific places where vanilla assumes the battle came from a normal custom
save slot or real campaign state. The direct handoff is not a self-contained
`RealBattleSave` loader; VP makes it viable by creating temporary campaign
ships, refreshing their `Ship.BattleStore` snapshots, and scoping
`Player.get_fleet` so vanilla can see those ships while it builds the transient
`CampaignBattle`.

## Implications For Our Own Battle Generator

Prefer a saved-custom-battle payload first.

Minimum generated payload:

- `RealBattleSave.Version = GameManager.CustomBattleSaveVersion`
- metadata fields filled with safe non-null values, including a non-empty
  `FriendlyName` if the save may go through `LoadCustomBattle(save)`;
- `Player` and `Enemy` `RealBattlePlayer` objects;
- one or more `Ship.Store` design snapshots per side;
- matching `Ship.BattleStore` battle snapshots per side;
- matching live or temporary `Ship` instances visible through scoped fleets when
  using the direct `ToCustomBattleFromSave(save)` handoff;
- `ShipAmounts` and `IsHullAvailable` populated for each side;
- one or more `Division.Store` entries whose ship ids are battle-store ids;
- `MainShip` set to a valid player-side battle-store id;
- `WeatherStore` filled;
- empty, non-null `Shells`, `Torpedos`, and statistics lists unless resuming an
  in-progress battle.

Be strict about identity:

- battle ship ids, design ids, division ship ids, and main ship id must agree;
- side/country identity should be derived from the save payload, not just
  `ship.player`, during first battle load;
- if using real campaign designs, keep temporary ships isolated from the
  player's real campaign fleet during custom load.

## Lessons From The VP 1v1 Test Battle

The current VP experiment is intentionally scoped to a generated 1v1 battle.
Do not expand this checklist into multi-ship, multi-division, or campaign-result
application work unless that becomes an explicit feature.

`ToCustomBattleFromSave(save)` is not a fully self-contained loader. The save
payload matters, but vanilla still expects usable temporary `Ship` instances to
be visible through the custom-battle players and fleets while it builds the
transient battle.

Failures can happen before the custom-battle loader starts. If
`Ship.FromStore(...)` fails during temporary ship creation, the flow never
reaches `ToCustomBattleFromSave`, `UpdateLoadingCustomBattleFromSave`, fleet
scoping, or `PrepareBattleFromSave`.

Normalize and validate the cloned `Ship.Store` before calling
`Ship.FromStore(...)`. At minimum, log and verify `shipType`, `hullName`, `id`,
`designId`, selected design name, and owner. A missing or unresolved `shipType`
produces vanilla's `null ShipType in save` error and prevents launch.

Do not depend on the live design reference still existing at launch time. Design
viewer rows can be rebuilt or erased between "set slot" and "start battle", so
capture the needed `Ship.Store` payloads while the slot is set and the live
design is known-good. Prefer calling the live selected design's `ToStore(false)`
at slot-capture time, and capture separate payloads if the temporary load store
and final saved-battle store need to diverge.

Do not mutate a stored selection snapshot in place. If a launch-time copy is
needed, use a proven structured clone or another fresh `ToStore(false)` from a
still-valid live design; avoid manual field-by-field copies between Il2Cpp
`Ship.Store` objects unless they have been separately proven safe. A manual copy
can fail before `Ship.FromStore(...)`, while relying only on the live design at
launch can fail with a stale or erased design reference.

Keep the final saved-battle design store separate from the temporary load store.
The temporary load store should be normalized only for `Ship.FromStore(...)`;
the saved-battle store can carry the generated final `id` and `designId` used by
`RealBattleSave`.

After `FromStore(...)` succeeds, the temporary shell should become the authority
for the battle snapshot. Snapshot the shell into `Ship.BattleStore`, then make
sure `BattleStore.Id`, `BattleStore.DesignId`, `Division.Store.Ships`, and
`RealBattleSave.MainShip` all agree.

A successful preview that only shows country and ship-type counts is not enough.
Those counts can come from `RealBattlePlayer.ShipAmounts` even when the actual
`Ship.BattleStore` is skeletal. If `Ship.ToBattleStore()` fails for the
temporary shell, treat it as a launch-blocking validation failure until there is
a proven complete battle-store builder. Otherwise the preview can look "empty":
the sides and ship classes are known, but gun groups, ammo, part damage, and
other battle-state payloads may never have been captured.

The 2026-05-09 hard-fail log shows that brute-force prep calls are not enough
when `Ship.FromStore(...)` produces a not-fully-initialized shell. The player
shell had `parts=37`, `hullAndParts=38`, `ammo=3`, `ammoTotal=3`, and
`gunGroups=3`, but also `hullAndPartsInited=0`, `partDamage=-1`, `modules=-1`,
`gunGroupsBySide=0`, `shipIsInited=False`, and `crewPartsValid=False`.
`CreateSectionsForShip`, `CWeap`, `RecalcAmmo`, `RecalculateWeaponRanges`, and
`RefreshGunsStats` ran, while `RefreshHull` and `RefreshMountedParts` threw
`NullReferenceException`; `Ship.ToBattleStore()` still threw
`KeyNotFoundException`. The next fix should reproduce or call vanilla's actual
custom-battle ship initialization path instead of adding more isolated refresh
calls.

A later 2026-05-09 run preserved a live manual design
(`manualDesign=True`, `manualDesignErased=False`) and still failed the same way:
the temp shell had `hullAndPartsInited=0`, `partDamage=-1`, `modules=-1`,
`gunGroupsBySide=0`, `shipIsInited=False`, and `crewPartsValid=False`, then
`Ship.ToBattleStore()` threw `KeyNotFoundException`. Keeping the selected
design object alive is therefore not sufficient by itself. The scoped
`Player.get_fleet` entries used by `UpdateLoadingCustomBattleFromSave` need to
look like vanilla's initialized custom-battle fleet ships, not merely like
`Ship.Create(...)` plus `Ship.FromStore(...)` design shells.

The v0.3.62 attempt tried `Ship.ToBattleStore()` directly on the selected design
before falling back to the temporary shell. The selected design still had
`hullAndPartsInited=0`, `partDamage=-1`, `modules=-1`, `shipIsInited=False`,
and `crewPartsValid=False`; only `gunGroupsBySide` differed from the temporary
shell (`2` versus `0`). It also threw `KeyNotFoundException`. So the next step
is not just choosing between selected design and temp shell; it is finding where
vanilla turns a constructor/design ship into a battle-initialized ship with
damage/module dictionaries and initialized hull-part state.

The v0.3.63 custom-battle-path attempt moved away from pre-launch
`Ship.ToBattleStore()` and called vanilla `ToCustomBattle(true, false)` with a
synthetic `Ui.SkirmishSetup` plus `BattleManager.customBattleSharedDesigns`.
That reached `BattleManager.PreInitCustomBattle`, which immediately triggered
`CampaignController.CleanupShips`; the temporary ships created before pre-init
were then unusable and the launch failed with "could not prepare the shared
custom-battle design". Do not create the shared-design temp ships before
`PreInitCustomBattle` if that pre-init cleanup can erase them. Build the skirmish
setup first, run pre-init, then create/inject the temporary shared designs after
the cleanup boundary, or use a vanilla hook after cleanup where shared designs
are normally available.

The v0.3.64 follow-up moved temp-ship creation after pre-init, but exposed two
campaign-UI hazards. First, the Designs tab refresh still needs vanilla's
`CampaignFleetWindow.SetDesignImageAndInfoForFirstShip(designs, null, true)`
call. In vanilla and the older TAF/DIP viewer, `nextShip == null` is not a no-op;
it lets vanilla choose the first design or cleanly disable the preview when the
list is empty. Short-circuiting that call can leave the design screen blank.
Second, scoping `CampaignData.PlayersMajor` down to a 1v1 pair is dangerous if
the scope escapes back to campaign UI. A broken run showed the Designs tab with
only two nation flags and zero visible campaign/design counts. Any player-list
scope must be load-only, restored on every abort/state transition, and preferably
implemented without mutating the live campaign `PlayersMajor` list at all.

The v0.3.66 diagnostics showed the early-return issue had been corrected, but
the campaign Designs tab was still empty because the data entering the refresh
was already empty: `CampaignData.Players.Count = 224`,
`CampaignData.PlayersMajor.Count = 2`, `player.designs.Count = 0`,
`GetViewedDesigns(player).Count = 0`, and `designUiByShip.Count = 0` for both
the US and France. That means the next investigation should not keep tuning the
row/preview refresh; it should prove whether the loaded campaign data has been
corrupted/scoped before the Designs tab opens, or whether VP is selecting the
wrong `Player` instances/containers for campaign designs and vessels.

The v0.3.68 integrity pass showed the empty state already exists at
`CampaignController.InitPlayersMajor(isLoadingSave: true)`: `Players = 224`,
`PlayersMajor = 2`, `playersWithMajorFlag = 2`, `Vessels = 0`, and
`VesselsByPlayer = 0`. That makes the blank Designs tab a loaded-data problem,
not a row-rendering problem. The same run also produced thousands of vanilla
`Player.get_designsAll()` errors ("Can't find player for designsAll, ...")
because VP diagnostics called `player.designs` / `player.fleetAll` while walking
all `CampaignData.Players`. `Player.get_designsAll()` looks up a campaign design
dictionary by `Player.data` and logs an error when the key is absent, so broad
diagnostics must not call those accessors on every country/province player. Log
raw campaign dictionaries first, or guard per-player access by checking the
underlying dictionary/key exists.

After the final test of this approach, the campaign-data-shaping route should be
considered abandoned. The feature should keep the vanilla custom-battle-loader
direction, but not by mutating or repairing live campaign structures. Treat the
campaign Designs tab as a launcher only: capture the selected 1v1 design payload
in VP-owned static state, enter vanilla custom battle with a minimal
`Ui.SkirmishSetup`, and inject the two selected designs only at the vanilla point
where shared custom-battle designs are consumed. The next investigation target is
therefore `PreInitCustomBattle -> LoadSharedDesigns -> GetShipFromCustomBattle`,
especially the exact contract of `BattleManager.customBattleSharedDesigns` at
the moment vanilla reads it.

The new pivot rule is: do not assign to `CampaignData.PlayersMajor`, do not
scope or rebuild live campaign vessel/design stores, and do not make campaign
data look like a temporary 1v1 campaign. If vanilla needs a temporary 1v1 custom
battle, let the custom-battle loader create its own temporary players and ships;
VP should only provide the selected shared-design payload and clear it after
success or failure.

The v0.3.71 rollback diagnostic is an important branch point: the live log
confirmed `CampaignDesignTestBattlePatch.PatchEnabled = false` via
`UADVP test battle: campaign design test battle patch is disabled for this
diagnostic build`, but the Designs tab was still empty. The same run still had
`Vessels = 0`, `VesselsByPlayer` keys for only `usa` and `france` with zero
ships, `playerVessels = 0`, `viewed = 0`, `uiBacked = 0`, and `designUi = 0`.
So an empty Designs tab in that build should not be attributed to active
test-battle scoping. The next baseline needs a truly clean DLL/source state, or
at minimum the design-viewer diagnostics/patches must be disabled separately
from the test-battle launcher to prove whether the remaining blank screen is a
save-state issue or another VP design-viewer/WIP interaction.

Scope cleanup must cover more than `_UpdateLoadingCustomBattleFromSave`. The
risky path continues through `GameManager.UpdateLoadingBattle(save)` and
`PrepareBattleFromSave(save)`, so failed scene preparation needs a watchdog or
finalizer that restores scoped player and fleet state.

Finish and leave hooks should be token-gated. `activeTestBattle` alone is too
broad; only suppress finish, complete, salvage, abandon, or leave behavior when
the current battle matches the generated 1v1 battle token.

Avoid the full campaign completion path until we intentionally want campaign
side effects. A synthetic `CampaignBattle` with fake task-force ids can enter
parts of the flow, but completion code expects campaign task-force groups,
campaign collections, damage persistence, sunk-ship handling, victory points,
and result viewing to be real.

## Things To Verify Before Implementation

- Exact `GameManager.UpdateLoadingBattle` order for scene load, init, battle
  preparation, start damage/ammo capture, and UI-state transition.
- Exact vanilla custom-battle ship initialization between `Ship.FromStore(...)`
  and `Ship.ToBattleStore()`, especially what sets `shipIsInited`,
  `hullAndPartsInited`, `partsDamage`, `modules`, `gunGroupsBySideAndData`, and
  `gunsAmountInGroupCanRotateAndShoot`.
- The shell state of a normal vanilla custom-battle fleet ship just before
  `SaveBattle` / `Ship.ToBattleStore()`, so VP can compare it directly to the
  generated campaign-design test shell.
- The exact order inside `BattleManager.PreInitCustomBattle`, especially its
  `CampaignController.CleanupShips` call relative to
  `customBattleSharedDesigns`; temp ships created before that cleanup may be
  erased before vanilla can consume them.
- Exact vanilla `CampaignFleetWindow.Refresh` / `RefreshAllShipsUi` /
  `SetDesignImageAndInfoForFirstShip` behavior for the Designs tab. Passing
  `nextShip = null` is part of the refresh contract, not a harmless call to skip.
- A safe way to expose only the generated 1v1 custom-battle players to the
  battle loader without leaving `CampaignData.PlayersMajor` scoped in campaign
  UI or saved campaign data.
- Loaded-campaign data integrity before the Designs tab patch runs:
  `PlayersMajor` names/count, each candidate player's `designs` count,
  `fleetAll` count, `VesselsByPlayer` key/count shape, and whether the same save
  is already empty with the test-battle patch disabled.
- The actual campaign design-store dictionary used by `Player.get_designsAll()`
  before touching `player.designs`. If that dictionary has no `Player.data` key,
  the accessor logs a Unity error; avoid using it as a broad probe across all
  `CampaignData.Players`.
- The exact shared-design consumption contract in a normal vanilla custom
  battle: when `customBattleSharedDesigns` is read, what `Ship` state those
  shared designs have, how `LoadSharedDesigns` filters them, and what
  `GetShipFromCustomBattle` receives and returns.
- Whether VP can inject `customBattleSharedDesigns` through a transient,
  VP-owned payload without mutating `CampaignData.PlayersMajor`,
  `VesselsByPlayer`, player `fleet`, or player design lists.
- A clean rollback matrix for the blank Designs tab: same save with the
  test-battle patch disabled, same save with the VP design-viewer patch disabled,
  and a known-good/pre-test-battle DLL. If only the dirty DLL is empty, the cause
  is a remaining VP patch. If all DLLs are empty for that save, stop debugging
  the test-battle path and treat the save/campaign state as the suspect.
- Exact `BattleManager.PrepareBattle` grouping rules for multi-ship generated
  fleets, especially ship-type ordering, screen/scout assignment, mission-range
  checks, and reinforcement lists.
- Whether generated battles need custom battle save-slot metadata or can stay
  purely in-memory.
- How far VP's current `Player.get_fleet` load scope needs to expand for
  multi-ship or multi-division generated battles.
- Whether campaign-generated battles should use `RealBattleSave` anyway, then
  apply results manually, instead of entering through `CampaignBattle`.
- How country/side mapping should work for campaign battles. VP currently has a
  robust saved-custom-battle country map, while campaign parity would likely
  need a separate map from `CampaignBattle.AttackerShips` and `DefenderShips`.

## Practical Debug Checklist

When testing generated battles, check `Latest.log` for:

- empty attacker or defender ship lists;
- null collection exceptions inside ship load, ammo, modules, sections, or
  statistics;
- `Ship.ToBattleStore()` failures after temporary shell creation, especially
  `KeyNotFoundException` with `shipIsInited=False`, `hullAndPartsInited=0`, or
  missing `partsDamage` / `modules`;
- divisions with missing ship ids;
- premature battle-finish triggers before ships finish entering battle;
- weather being reset after scene entry;
- country/paint identity falling back to the wrong side;
- campaign completion calls firing for a synthetic non-campaign battle.
