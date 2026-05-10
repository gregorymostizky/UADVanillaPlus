# Vanilla campaign ship and task-force icon painting

This note is a vanilla-only deep dive into how Ultimate Admiral: Dreadnoughts paints campaign-map ship and task-force icons. It is written for VP mod research, but it intentionally ignores VP-specific patches and treats the decompiled vanilla game as the source of truth.

The inspected vanilla sources are:

- `E:\Codex\cpp2il_uad_diffable\DiffableCs\Assembly-CSharp\ShipUI.cs`
- `E:\Codex\cpp2il_uad_diffable\DiffableCs\Assembly-CSharp\MapUI.cs`
- `E:\Codex\cpp2il_uad_diffable\DiffableCs\Assembly-CSharp\CampaignMapElement.cs`
- `E:\Codex\cpp2il_uad_diffable\DiffableCs\Assembly-CSharp\Route.cs`
- `E:\Codex\cpp2il_uad_diffable\DiffableCs\Assembly-CSharp\CampaignController.cs`
- `E:\Codex\cpp2il_uad_isil\IsilDump\Assembly-CSharp\ShipUI.txt`
- `E:\Codex\cpp2il_uad_isil\IsilDump\Assembly-CSharp\MapUI.txt`
- `E:\Codex\cpp2il_uad_isil\IsilDump\Assembly-CSharp\MapUI_NestedType___c.txt`
- `E:\Codex\cpp2il_uad_isil\IsilDump\Assembly-CSharp\MapUI_NestedType___c__DisplayClass75_0.txt`
- `E:\Codex\cpp2il_uad_isil\IsilDump\Assembly-CSharp\MapUI_NestedType___c__DisplayClass75_1.txt`
- `E:\Codex\cpp2il_uad_isil\IsilDump\Assembly-CSharp\CampaignController_NestedType_TaskForce.txt`

Decompiler caveat: the diffable C# files preserve class shape, fields, method names, and signatures, but most bodies are empty. The ISIL dumps preserve call flow and field offsets, but some calls are only visible as native addresses or virtual dispatch. Where a conclusion depends on an inferred Unity API call, this note says so.

Runtime caveat for VP/MelonLoader: live Il2Cpp wrapper reflection is not shaped exactly like the diffable C# view. A private vanilla field such as `MapUI.shipGroups` may appear in runtime field reflection as metadata storage like `NativeFieldInfoPtr_shipGroups : System.IntPtr`, while the usable member is exposed through generated accessors such as `get_shipGroups` / `set_shipGroups`. Treat the decompiled private field names as semantic evidence, not as proof that `AccessTools.Field(typeof(MapUI), "shipGroups")` or a normal managed `FieldInfo.GetValue(...)` will return the live dictionary.

## Short version

Campaign task-force markers are not rendered from ship models. They are Unity UI prefabs managed by `MapUI`.

The main visual object is `ShipUI`, a `CampaignMapElement` with these important fields:

- `Flag`: a UI `Image` for the owning player's flag.
- `Icon`: a UI `Image` for either the ship sprite or submarine sprite.
- `ShipSprite` and `SubmarineSprite`: the two built-in icon sprite choices.
- `FriendlyColor`, `EnemyColor`, `DefaultColor`: prefab-authored colors used to tint `Icon`.
- `Route`: a child route object with line renderers and a destination marker.
- `Zone`, `MineSweepingRadius`, `MinelayingRadius`, and `Obstacles`: extra child visuals attached around the marker.

The central paint method is:

```csharp
MapUI.RefreshMovingGroups(bool updateNodeOwners = false)
```

The per-frame positioning/scaling method is:

```csharp
MapUI.LateUpdate()
```

Vanilla's icon paint contract is roughly:

```csharp
// New-marker path only.
ship.SetType(taskForce.GetVesselType());

// Common refresh path for new and reused markers.
ship.Flag.sprite = taskForce.Controller.Flag(...);

if (taskForce.Controller == PlayerController.Instance.Player)
    ship.Icon.color = ship.FriendlyColor;
else if (mainPlayerRelationToController.isWar)
    ship.Icon.color = ship.EnemyColor;
else
    ship.Icon.color = ship.DefaultColor;
```

The exact code is decompiled, so that is not literal vanilla source, but it matches the field offsets and calls observed in `ShipUI.SetType` and `MapUI.RefreshMovingGroups`.

Important correction from the ISIL: `ShipUI.SetType(...)` is only called in the branch that instantiates a new marker from `ShipTemplate`. Existing markers found in `shipGroups` keep their current `Icon.sprite`; the common refresh path repaints `Icon.color`, refreshes `Flag.sprite`, updates position, route paths, radius overlays, and visibility. A mod that changes a live task force's ship-vs-submarine display category should call `SetType(...)` itself or force marker recreation instead of assuming `RefreshMovingGroups(...)` will reselect the sprite.

## Object model

### CampaignMapElement

`CampaignMapElement` is the base class for map UI markers. Its relevant fields are:

- `Id`
- `WorldPos`
- `NeedOffset`
- `CachedTransform`
- `ElementPositionHint`
- `UpdateElement`
- cached `RectTransform`

`CampaignMapElement.UpdatePositionScale(Vector3 newPosition, float scale)` positions the cached transform at the UI-space map position and scales it uniformly. `ShipUI` overrides it to apply the task-force marker's extra `offset` before setting transform position.

This means map elements do not own projection from campaign world space into UI space. They receive an already projected `newPosition`, then apply element-local positioning and scale rules.

### ShipUI

`ShipUI` inherits `CampaignMapElement`.

Important fields from the vanilla class:

```csharp
public Vector3 offset;
public Route Route;
public GameObject Zone;
public GameObject MineSweepingRadius;
public GameObject MinelayingRadius;
public Button Btn;
public Image Flag;
public Image Icon;
public Sprite ShipSprite;
public Sprite SubmarineSprite;
public Color FriendlyColor;
public Color EnemyColor;
public Color DefaultColor;
public NavmeshObstacles Obstacles;
```

The actual icon body is only `Icon`; the flag is separate. Route and radius visuals are separate objects tied to the same task force.

### Route

`Route` is another prefab-driven helper object:

```csharp
public GameObject Destination;
public MeshRenderer DestinationRenderer;
public LineRenderer RouteLine;
public LineRenderer AdditionalLine;
public Material Friendly;
public Material Ally;
public Material Enemy;
```

`MapUI` also has route-level material fields:

- `RouteFriendly`
- `RouteEnemy`
- `RouteDefault`

So a route uses two material systems:

- line renderer materials from `MapUI`
- destination marker materials from the `Route` prefab

### MapUI

`MapUI` owns the ship/task-force marker lifecycle.

Important fields:

```csharp
public Route RouteTemplate;
public Transform RouteLineRoot;
public Material RouteFriendly;
public Material RouteEnemy;
public Material RouteDefault;

public ShipUI ShipTemplate;
public Transform ShipsRoot;
public Vector2 ShipMinMaxScale;

private List<CampaignMapElement> movingShipsElements;
private List<Route> routes;
private Dictionary<TaskForce, CampaignMapElement> shipGroups;
```

`ShipTemplate` is the source prefab for map task-force markers. `ShipsRoot` is the parent transform. `shipGroups` is the lookup from vanilla `TaskForce` objects to their live UI marker.

In the live VP 0.3.54 log from 2026-05-09, a runtime `MapUI` member dump did not expose `shipGroups` as a managed instance `Dictionary<TaskForce, CampaignMapElement>` field. It exposed `NativeFieldInfoPtr_shipGroups : System.IntPtr`, and a binary string scan of the live Il2Cpp assembly also found `get_shipGroups` and `set_shipGroups`. That means code that needs the live dictionary should resolve and invoke the generated property/accessor path, or otherwise use Il2Cpp field-pointer APIs, instead of assuming standard .NET field reflection can read the dictionary directly.

## Data source

The data source is `CampaignController.Data.TaskForces`.

`CampaignController.Data` has:

```csharp
public List<TaskForce> TaskForces;
public Dictionary<Guid, TaskForce> TaskForceById;
public Dictionary<PlayerData, List<TaskForce>> TaskForceByPlayer;
```

The `TaskForce` class carries both state and display inputs:

```csharp
public List<VesselEntity> Vessels;
public Guid Id;
public Vector3[] Path;
public Player Controller;
public int CurrentPositionIndex;
public bool Hide;
public bool GroupMovingToBattle;
public bool GroupMovingFromBattle;
public Vector3 WorldPos;
public NavmeshObstacles NavmeshObstacles;
private Nullable<VesselType> vesselsType;
```

The key display helpers are:

- `GetVesselType()`
- `CurrentPosition()`
- `IsMoving()`
- `GetZoneRadius(bool force = false)`
- `GetTaskForceMinefieldRadius()`
- `GetMinesweepingRadius(bool unityUnits = false)`
- `BattleTonnage()`
- `GetFleetCount()`

For icon painting, `GetVesselType()` is the important one. It lazily caches a nullable vessel type from the task force's vessels. If there is no vessel source, it returns the default enum value.

`RefreshMovingGroups` also has a task-force validity pass before the common repaint work. The generated predicate `MapUI.<>c.<RefreshMovingGroups>b__75_3(VesselEntity s)` returns true for null, sunk, or scrapped vessels; the surrounding branch deactivates the marker and queues the task force for `CampaignController.Data.RemoveTaskForce(...)` when the collection-level invalid-vessel check succeeds. In practice, the normal marker paint path is for task forces with at least one usable vessel, not for empty or fully dead groups.

## RefreshMovingGroups lifecycle

`MapUI.RefreshMovingGroups(bool updateNodeOwners = false)` is the main task-force marker pass. It does a lot more than icon painting, but the lifecycle is understandable in phases.

### 1. It prepares task-force data

At the top of the method, vanilla touches campaign task-force collections, sorts task forces with a comparer based on `TaskForce.GetZoneRadius(true)`, and rebuilds task-force lookup state.

The generated lambdas show this clearly:

- `MapUI.<>c.<RefreshMovingGroups>b__75_0(TaskForce g1, TaskForce g2)` compares `GetZoneRadius(true)`.
- `MapUI.<>c.<RefreshMovingGroups>b__75_1(TaskForce x)` returns `x.Id`.
- `MapUI.<>c.<RefreshMovingGroups>b__75_2(TaskForce x)` returns the task-force object itself.

That implies this pass is not a tiny repaint pass. It reconciles campaign model state, UI marker state, and some auxiliary dictionaries/lists.

### 2. It reuses existing markers by TaskForce object

`MapUI` stores markers in:

```csharp
private Dictionary<TaskForce, CampaignMapElement> shipGroups;
```

During refresh, vanilla checks whether the current `TaskForce` already has a marker in `shipGroups`.

If a marker exists, the method casts it back to `ShipUI` and updates it through the common refresh path.

If no marker exists, vanilla instantiates a new `ShipUI` from:

```csharp
ShipTemplate
ShipsRoot
```

The ISIL path around `MapUI.RefreshMovingGroups` shows `MapUI.ShipTemplate` at field offset `0x158`, `ShipsRoot` at `0x160`, an instantiate call, then `ShipUI.SetType(...)`. That `SetType` call is not present in the reused-marker branch.

### 3. It sets the icon type for new markers

The method calls:

```csharp
ship.SetType(taskForce.GetVesselType());
```

`ShipUI.SetType(VesselType type)` is very small. It writes one of two sprites to `Icon`:

- if the type value compares equal to zero, it uses `ShipSprite`
- otherwise, it uses `SubmarineSprite`

The important part is that vanilla does not compose a different icon for different ship classes, tonnage, fleet count, or mission roles here. At this layer the icon is binary: surface ship sprite or submarine sprite.

Because this happens only when the marker is created, `RefreshMovingGroups` is not a guaranteed sprite repair pass for an already-live `ShipUI`. It is a reliable color/flag/route/overlay refresh, but not a reliable icon-type refresh.

### 4. It names and wires the marker

For a newly created marker, vanilla also:

- stores the owning player's display name on the marker's inherited `Id` field,
- builds/assigns a `Route` object,
- wires tooltip text through `Ui` helper calls,
- removes existing button listeners and adds a click handler,
- adds the marker to the relevant map-element lists,
- stores the marker in `shipGroups`.

Those initialization steps are also creation-only. Reused markers keep their existing route object, tooltip/click wiring, and icon sprite while the shared path below refreshes their dynamic visual state.

The click handler in `MapUI.<>c__DisplayClass75_0.<RefreshMovingGroups>b__7(PointerEventData e)` opens `ShipGroupPopupUI.Init(...)` for eligible left-clicks on the main player's task force. This is interaction wiring, not painting, but it explains why the marker prefab includes a `Button`.

`ShipUI.OnPointerClick(PointerEventData eventData)` has its own right-click-ish behavior: it checks `eventData.button == 1` and sends the marker transform to sibling index zero. That affects draw order, not color.

### 5. It paints the icon color

This is the core of "painting" for the visible ship/task-force icon.

After route setup, `RefreshMovingGroups` decides `Icon.color` from the task-force owner relative to the main player:

1. If the task force belongs to the main player, the icon receives `ShipUI.FriendlyColor`.
2. If it does not belong to the main player and the main player's relation to that owner is war, the icon receives `ShipUI.EnemyColor`.
3. Otherwise, the icon receives `ShipUI.DefaultColor`.

In the ISIL this is visible as writes to `[ship + 0x90]`, the `Icon` field, using color values from:

- `[ship + 0xA8]` -> `FriendlyColor`
- `[ship + 0xB8]` -> `EnemyColor`
- `[ship + 0xC8]` -> `DefaultColor`

The setter is a virtual call on the `Image`/`Graphic` object. Based on the target object and argument shape, this is the Unity UI graphic color setter.

Important implication: vanilla does not derive task-force icon color directly from `PlayerData.color`, `PlayerData.highlightColor`, `PlayerMaterial`, flag texture, or nation. Those may affect other campaign visuals, but the task-force icon tint itself comes from the `ShipUI` prefab's three color fields.

Unlike `ShipUI.SetType(...)`, this color assignment is in the common branch and runs for both new and reused markers.

### 6. It paints the flag

The flag is separate from the icon.

`RefreshMovingGroups` reads the task-force controller and calls:

```csharp
Player.Flag(...)
```

Then it assigns the result to:

```csharp
ship.Flag.sprite
```

In ISIL this is the call sequence around `Player.Flag` followed by `Image.set_sprite` on `[ship + 0x88]`, the `Flag` field.

Important implication: flag identity and icon tint are independent. A neutral foreign task force can have its own national flag while still using `DefaultColor` for the hull/submarine icon.

Like icon color, flag sprite assignment is in the common branch and is refreshed on reused markers.

### 7. It updates marker world position

`RefreshMovingGroups` keeps the marker's inherited `WorldPos` aligned with task-force path/current position state.

If the task force has a path and current position index inside that path, the marker world position can be copied from the path element. Otherwise, the task-force's stored `WorldPos` / `CurrentPosition()` path is the fallback.

That `WorldPos` is still campaign/world-map space. UI projection happens later.

### 8. It handles moving routes

`TaskForce.IsMoving()` is checked during refresh.

If the task force is not moving, vanilla disables the route game object:

```csharp
ship.Route.gameObject.SetActive(false);
```

If the task force is moving, `MapUI.GetRoute(...)` and `MapUI.SetRoutePath(...)` build and update route line renderers.

Routes are tied to the task-force marker, but they are not part of the `Icon` image. They are separate line/destination visuals.

### 9. It creates and scales radius visuals

The marker may also lazily spawn and update:

- `Zone`
- `MineSweepingRadius`
- `MinelayingRadius`
- `Obstacles`

These are driven by:

- `TaskForce.GetZoneRadius(true)`
- `TaskForce.GetTaskForceMinefieldRadius()`
- `TaskForce.GetMinesweepingRadius(...)`

The radius objects are positioned at the marker's map coordinate and scaled from the computed radius. They are better understood as task-force overlays than as icon paint.

### 10. It cleans up stale markers

When a task force is removed, `MapUI.OnShipsGroupRemoved(TaskForce fleetGroup)` removes associated route and marker objects:

- destroys the route game object,
- destroys `Zone`, `MineSweepingRadius`, `MinelayingRadius`,
- destroys `Obstacles`,
- destroys the marker game object,
- removes the task force from `shipGroups`,
- refreshes moving groups afterward.

This cleanup is broad because `ShipUI` owns several child/linked objects beyond the icon image itself.

## Per-frame projection and scaling

`RefreshMovingGroups` creates and repaints markers. It is not the only thing that moves them.

`MapUI.LateUpdate()` handles ongoing visual placement and scale for map elements.

For moving ship/task-force markers, the relevant call is:

```csharp
UpdateMapElementPosition(movingShipsElements, ShipMinMaxScale, zoomLevel, ratio)
```

`UpdateMapElementPosition(...)` does this for each element:

1. reads `CampaignMapElement.WorldPos`,
2. calls `MapUI.WorldToUISpace(UICanvas, WorldPos)`,
3. calls the element's virtual `UpdatePositionScale(...)`.

For `ShipUI`, the override:

1. lazily caches the transform,
2. applies `ShipUI.offset * scale` to the projected position,
3. sets transform position,
4. sets transform local scale to `Vector3.one * scale`.

This means zoom/camera changes do not require repainting `Icon.color` or `Flag.sprite`; they run through the positioning/scaling path.

It also means zoom/camera changes are unrelated to `Icon.sprite`; if a marker is already using the wrong ship/submarine sprite, `LateUpdate` will not repair it.

## Route painting

`MapUI.GetRoute(List<Vector3> path, bool isEnemy, bool isMainPlayer)` creates a `Route` prefab and paints its line/destination materials.

It instantiates from:

```csharp
RouteTemplate
RouteLineRoot
```

Then it enables world-space line rendering on both:

- `Route.RouteLine`
- `Route.AdditionalLine`

It also checks whether the path crosses the map mesh split with `GetSlitPathIndex(...)`. When the route crosses that split, vanilla divides the path between `RouteLine` and `AdditionalLine`. Otherwise, `RouteLine` gets the path and `AdditionalLine` is cleared/hidden.

The route material choice follows the same high-level relation categories as the icon color, but through route material fields:

- main-player route: `MapUI.RouteFriendly` on line renderers, `Route.Friendly` on destination renderer
- enemy route: `MapUI.RouteEnemy` on line renderers, `Route.Enemy` on destination renderer
- other route: `MapUI.RouteDefault` on line renderers, `Route.Ally` on destination renderer

In `RefreshMovingGroups`, vanilla only shows the route game object for the main player's task forces. Non-main markers can still have route objects configured internally, but the route object is not kept visible in the same way.

`MapUI.LateUpdate()` later adjusts route line widths and destination marker scale as zoom changes.

## Hover behavior

`MapUI` wires hover callbacks through generated display class `<>c__DisplayClass75_1`.

On hover enter:

- `Zone` is activated if present.
- `MineSweepingRadius` is activated if present and has nonzero scale.
- `MinelayingRadius` is activated if present and has nonzero scale.
- The route may be shown depending on task-force movement/path state.

On hover leave:

- `Zone` is hidden.
- `MineSweepingRadius` is hidden.
- `MinelayingRadius` is hidden.
- The route is hidden again for cases where vanilla only wanted it as a hover cue.

That behavior reinforces the split between icon paint and overlay paint:

- icon sprite/color/flag identify the group,
- hover controls transient overlays.

## What vanilla does not paint into the icon

Based on the inspected surfaces, vanilla does not paint these things into the `ShipUI.Icon` image:

- task-force tonnage,
- ship count,
- design class,
- battle power,
- fuel/supply status,
- mine radius,
- route length,
- mission type,
- country color,
- ship hull/material color.

Some of those values appear in tooltips, routes, zones, popups, or separate campaign mechanics. They are not encoded in the icon sprite or icon tint.

## Live VP runtime finding: `shipGroups` access

The current task-force tonnage-fill experiment depends on mapping each `TaskForce` to its live `ShipUI` marker. The vanilla semantic source for that is still `MapUI.shipGroups`, but the 2026-05-09 VP 0.3.54 game log showed the first runtime resolver still failed:

```text
UADVP task-force tonnage indicators MapUI field dump (could not find TaskForce to CampaignMapElement dictionary).
  NativeFieldInfoPtr_shipGroups : System.IntPtr
UADVP task-force tonnage indicators unavailable because MapUI.shipGroups dictionary field was not found.
```

This is stronger evidence than the earlier missing-field log. It shows that the member name survives in the Il2Cpp wrapper metadata, but the reflected field is the native field-info pointer, not the actual dictionary value. A normal scan of `FieldInfo.FieldType` will only see `System.IntPtr` for that member, so it cannot discover the dictionary by generic type.

A follow-up string scan of the live `Assembly-CSharp.dll` found `NativeFieldInfoPtr_shipGroups`, `get_shipGroups`, and `set_shipGroups` together. The next implementation pass should therefore:

- try `AccessTools.PropertyGetter(typeof(MapUI), "shipGroups")` / `AccessTools.Method(typeof(MapUI), "get_shipGroups")` and cast the returned value to `Il2CppSystem.Collections.Generic.Dictionary<CampaignController.TaskForce, CampaignMapElement>`;
- log the resolved accessor name, return type, and dictionary count before applying icon fill;
- only fall back to lower-level Il2Cpp field-pointer reads if the generated accessor cannot be invoked;
- keep the existing `RefreshMovingGroups` postfix and `BattleTonnage()` fill logic once the dictionary accessor works.

Until this accessor issue is fixed, the tonnage indicator exits before it can create or repaint `UADVP_TaskForceTonnageFill`.

## Practical hook points for research

This section names vanilla surfaces only. It does not describe any VP implementation.

### To observe icon creation/repaint

Best surface:

```csharp
MapUI.RefreshMovingGroups(bool updateNodeOwners = false)
```

This is where vanilla:

- instantiates/reuses `ShipUI`,
- calls `ShipUI.SetType(...)` for newly instantiated markers,
- sets `Icon.color`,
- sets `Flag.sprite`,
- creates/reuses routes,
- creates/reuses radius overlays,
- updates `shipGroups`.

When specifically checking the sprite branch, distinguish new markers from reused markers. The only observed `ShipUI.SetType(...)` call in `MapUI.RefreshMovingGroups` is in the new-marker path.

### To observe the icon sprite branch

Best surface:

```csharp
ShipUI.SetType(VesselType type)
```

This is the minimal vanilla method for ship-vs-submarine sprite assignment.

If a marker is already present in `shipGroups`, this method is not automatically called by the common refresh path.

### To observe color identity

Best surface:

```csharp
MapUI.RefreshMovingGroups(...)
```

The relevant fields on `ShipUI` are:

```csharp
FriendlyColor
EnemyColor
DefaultColor
Icon
```

### To observe flag identity

Best surface:

```csharp
MapUI.RefreshMovingGroups(...)
Player.Flag(...)
```

The relevant field on `ShipUI` is:

```csharp
Flag
```

### To observe position and scaling

Best surfaces:

```csharp
MapUI.LateUpdate()
MapUI.UpdateMapElementPosition(...)
MapUI.WorldToUISpace(...)
ShipUI.UpdatePositionScale(...)
```

`RefreshMovingGroups` decides what the marker is. `LateUpdate` decides where and how large it appears for the current camera/zoom state.

### To observe removal

Best surface:

```csharp
MapUI.OnShipsGroupRemoved(TaskForce fleetGroup)
```

This method destroys the route, overlays, navmesh obstacle object, and marker object linked to a removed task force.

## Modding cautions from vanilla behavior

Vanilla treats the task-force marker as a compact prefab with several independent visual channels:

- `Icon.sprite` means surface/submarine.
- `Icon.color` means friendly/enemy/default relation class.
- `Flag.sprite` means owner identity.
- `Route` means movement path.
- `Zone` and radius objects mean operational overlay.
- Tooltip/click callbacks mean detailed inspection.

Because those channels are independent, changes to one channel should not assume ownership of the others.

Specific cautions:

- Repainting `Icon.color` can overwrite the vanilla friendly/enemy/default relation cue.
- Replacing `Icon.sprite` can erase the only vanilla distinction between surface ships and submarines.
- Relying on `RefreshMovingGroups` to repair `Icon.sprite` on an existing marker is unsafe; vanilla refreshes sprite choice on marker creation, then mostly treats the sprite as stable.
- Mutating the `Flag` image affects owner identification, not relation coloring.
- Per-frame position/scale changes run through `UpdatePositionScale`, so child objects added under `ShipUI` need to tolerate parent scale changes.
- Route visibility is not the same thing as task-force visibility.
- Radius overlays are lazily created and may be null until the relevant radius exists.
- `shipGroups` is keyed by `TaskForce`, not by GUID string, so object identity matters inside the live UI dictionary.
- In a MelonLoader/Il2Cpp build, the `shipGroups` semantic field may need generated accessor invocation rather than `AccessTools.Field(...)`; seeing `NativeFieldInfoPtr_shipGroups` in reflection is metadata, not the dictionary itself.

## Evidence map

Useful source locations from this inspection:

- `ShipUI.cs`: fields and method names for `ShipUI`.
- `ShipUI.txt`: `SetType` assigns `Image.sprite`; `UpdatePositionScale` applies offset and scale; `OnPointerClick` changes sibling index on right-click.
- `MapUI.cs`: fields for `ShipTemplate`, `ShipsRoot`, `ShipMinMaxScale`, `shipGroups`, `RouteTemplate`, and route materials.
- `MapUI.txt`: `RefreshMovingGroups` creates/reuses `ShipUI`, calls `SetType` for newly instantiated markers, assigns icon color, assigns flag sprite, creates routes/overlays, and updates dictionaries/lists.
- `MapUI.txt`: the only observed `ShipUI.SetType` call inside `RefreshMovingGroups` is in the new-marker branch; reused markers still receive color, flag, route, position, and overlay updates.
- `MapUI.txt`: `GetRoute` creates route prefabs and chooses route materials.
- `MapUI.txt`: `SetRoutePath` writes line-renderer positions and toggles line visibility.
- `MapUI.txt`: `LateUpdate` calls `UpdateMapElementPosition` for `movingShipsElements` using `ShipMinMaxScale`.
- `MapUI_NestedType___c.txt`: generated vessel predicates show the invalid-vessel cleanup around null/sunk/scrapped task-force vessels.
- `MapUI_NestedType___c__DisplayClass75_0.txt` and `MapUI_NestedType___c__DisplayClass75_1.txt`: generated tooltip, click, hover enter, and hover leave callbacks.
- `CampaignMapElement.cs` and `.txt`: base map element position/scale mechanics.
- `CampaignController.cs`: `TaskForce` fields and display helper method names.
- `CampaignController_NestedType_TaskForce.txt`: `GetVesselType`, `GetFleetCount`, `BattleTonnage`, `CurrentPosition`, and `IsMoving`.
- `E:\SteamLibrary\steamapps\common\Ultimate Admiral Dreadnoughts\MelonLoader\Latest.log`: VP 0.3.54 runtime field dump showed `NativeFieldInfoPtr_shipGroups : System.IntPtr`, followed by tonnage indicators being unavailable because the dictionary field was not found.
