# UAD UI Layout Guide

This note is a reusable field guide for adding UI to Ultimate Admiral:
Dreadnoughts from UAD:VP without spending five iterations nudging a button by
hand.

The short version: first decide whether the new thing belongs inside an
existing vanilla layout, inside an existing text field, as a child overlay, or
as a truly new floating surface. Most placement pain comes from choosing the
wrong category.

## Sources To Check First

- Vanilla skeleton classes: `E:\Codex\cpp2il_uad_diffable\DiffableCs\Assembly-CSharp`
- Fuller IL flow: `E:\Codex\cpp2il_uad_isil\IsilDump\Assembly-CSharp`
- Current VP examples:
  - `UADVanillaPlus/Harmony/InGameOptionsMenuPatch.cs`
  - `UADVanillaPlus/Harmony/CampaignPoliticsDeclareWarPatch.cs`
  - `UADVanillaPlus/Harmony/CampaignFleetWindowDesignViewerPatch.cs`
  - `UADVanillaPlus/Harmony/CampaignTaskForceTonnageIndicatorPatch.cs`
  - `UADVanillaPlus/Harmony/CampaignConstructionStatusPatch.cs`
  - `UADVanillaPlus/Harmony/CampaignCountryInfoFinalRefreshPatch.cs`
  - `UADVanillaPlus/Harmony/CampaignCountryInfoWatchdogPatch.cs`

## Mental Model

UAD uses Unity uGUI. The relevant object usually has:

- a `GameObject` with a `Transform` or `RectTransform`;
- a visual component such as `Image`, `Text`, or `TMP_Text`;
- an input component such as `Button`, `Toggle`, `OnClickH`, `OnEnter`, or
  `OnLeave`;
- sometimes a parent `HorizontalLayoutGroup`, `VerticalLayoutGroup`,
  `DynamicScrollView`, or vanilla refresh method that overwrites state later.

For layout, the parent usually wins. If a parent has a layout group, manual
`localPosition` or `anchoredPosition` edits on a child are only temporary, and
may be overwritten on the next refresh. In that case placement should be done
with `SetParent(..., false)`, sibling order, `LayoutElement` sizes, and parent
layout settings.

If the object is outside a layout group, placement is controlled by
`RectTransform` anchors, pivot, offsets, and size. Use this only when there is
no suitable native slot.

Vanilla often rebuilds or rewrites UI from refresh methods. Good hooks are
usually the method that creates/refreshes the native row, plus a postfix/final
pass after broad refreshes if vanilla repaints the same labels again.

## Decision Tree

### 1. Can this be text inside an existing label?

Prefer this for compact campaign country-info and summary data.

Use when:

- the screen already has a label in the exact semantic area;
- the extra information can be one compact line or suffix;
- adding another object risks pushing neighboring rows.

Current example: `CampaignConstructionStatusPatch` writes maintenance status
into the existing `ShipbuildingCapacity` multi-line label, strips stale VP
lines before reapplying, and calls `LayoutRebuilder.ForceRebuildLayoutImmediate`
on the country-info rect. This avoided the earlier floating-line overlap trap.

Checklist:

- Find the native `TMP_Text` field in the decompile.
- Strip old VP text before appending new text so refreshes are idempotent.
- Prefer a vanilla multi-line label over a new sibling if the panel is tight.
- Force rebuild the owning rect when line count changes.
- If vanilla rewrites it later, add a narrow final-pass hook or watchdog rather
  than broad polling.

Related examples:

- `CampaignActiveFleetStatusPatch` and `CampaignTechnologyStatusPatch` decorate
  existing country-info labels with compact suffixes and strip old suffixes
  before reapplying.
- `CampaignPortShipCountPatch` decorates existing map port labels rather than
  adding another map marker.
- `CampaignBattleAutoResolveOddsPatch` appends one compact line to the existing
  battle popup description and removes it again when the popup changes to result
  state.

Pattern:

```csharp
string clean = StripVpLine(ui.ShipbuildingCapacity.text ?? string.Empty);
ui.ShipbuildingCapacity.text = string.IsNullOrWhiteSpace(clean)
    ? vpLine
    : $"{vpLine}\n{clean}";

RectTransform? rect = ui.RTransform ?? ui.GetComponent<RectTransform>();
if (rect != null)
    LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
```

### 2. Is there already a same-kind native button in the row?

Clone the native button. This is the highest-confidence path for row actions.

Use when:

- adding a button beside other buttons in the same row;
- vanilla already has the right visual style, layout components, hover state,
  and text hierarchy;
- the parent already orders buttons correctly.

Current example: `CampaignPoliticsDeclareWarPatch` clones
`row.IncreseTension.gameObject` for `Declare War`, clones `PeaceTreaty` or
`IncreseTension` for `Force Peace`, uses the same parent, sets sibling index
next to the native button, removes old listeners, then updates text/color/state
from `RefreshActions`.

Checklist:

- Patch the row `Init(...)` and the row refresh method.
- Use the method arguments that vanilla already passes. For politics rows,
  `CampaignPolitics_ElementUI.Init(Player p, ...)` and `RefreshActions(Player p)`
  already provide the row player.
- Instantiate the closest native button, not a generic prefab.
- Keep the same parent and set a sibling index relative to a known neighbor.
- Remove old listeners and install exactly one VP listener.
- Remove inherited localization and hover handlers if VP owns the text or
  tooltip. `LocalizeText`, `OnEnter`, and `OnLeave` from the cloned button can
  silently restore vanilla text or stale tooltip behavior.
- Update `interactable`, text, color, and tooltip in the refresh hook.
- Choose blocked-action behavior deliberately. Keep the button clickable when
  the click path opens a popup that explains the block, as diplomacy actions do.
  If the button is disabled, make the reason clear through tooltip, text, or
  color, as `CampaignTaskForceReturnToPortPatch` does.

Pattern:

```csharp
GameObject buttonObject = UnityEngine.Object.Instantiate(source.gameObject, source.transform.parent);
buttonObject.name = "UADVP_NewAction";
buttonObject.transform.SetSiblingIndex(source.transform.GetSiblingIndex() + 1);

Button button = buttonObject.GetComponent<Button>() ?? buttonObject.AddComponent<Button>();
button.onClick.RemoveAllListeners();
RemoveComponent<OnEnter>(buttonObject);
RemoveComponent<OnLeave>(buttonObject);
button.onClick.AddListener(new System.Action(() => ConfirmAction(row)));

TMP_Text? text = button.GetComponentInChildren<TMP_Text>();
if (text != null)
{
    RemoveComponent<LocalizeText>(text.gameObject);
    text.text = "New Action";
}
```

### 3. Is it a new panel we fully own?

Build it with layout groups and fixed row contracts.

Use when:

- VP owns the whole popup/panel content;
- controls need to remain stable as options are added;
- manual coordinates would be fragile across resolutions.

Current example: `InGameOptionsMenuPatch` clones the vanilla `PopupMenu`
surface, clears the window, then builds a controlled layout:

- root content is anchored full size with margin offsets;
- body uses `HorizontalLayoutGroup`;
- section list and option pane use `VerticalLayoutGroup`;
- rows use `HorizontalLayoutGroup`;
- buttons get `LayoutElement` min/preferred widths and heights;
- cloned popup buttons have their inherited tall geometry clamped to `26f`.

Checklist:

- Anchor the content root to full stretch and reserve margins with offsets.
- Put every row under a `HorizontalLayoutGroup` or `VerticalLayoutGroup`.
- Give fixed-height rows `minHeight` and `preferredHeight`.
- Give labels flexible width and buttons fixed preferred width.
- Clamp cloned template button rects if the source prefab is taller than the row.
- Rebuild the content tree on option changes if the menu is small and static.

Pattern:

```csharp
GameObject row = new("UADVP_Option_Row");
row.transform.SetParent(parent, false);

HorizontalLayoutGroup rowLayout = row.AddComponent<HorizontalLayoutGroup>();
rowLayout.padding = new RectOffset { left = 8, right = 8, top = 4, bottom = 4 };
rowLayout.spacing = 8f;
rowLayout.childControlHeight = true;
rowLayout.childControlWidth = true;
rowLayout.childForceExpandHeight = false;
rowLayout.childForceExpandWidth = false;

LayoutElement rowSize = row.AddComponent<LayoutElement>();
rowSize.minHeight = 34f;
rowSize.preferredHeight = 34f;
rowSize.flexibleWidth = 1f;
```

### 4. Is it an overlay on an existing icon or row?

Attach it as a child and anchor it to the parent.

Use when:

- the new UI should visually modify an existing icon without adding text;
- the new UI is a small badge that should follow an existing row without
  affecting row layout;
- the parent already moves, scales, hides, or clones itself;
- the overlay should not receive clicks.

Current example: `CampaignTaskForceTonnageIndicatorPatch` adds
`UADVP_TaskForceTonnageFill` under `ShipUI.Icon`, anchors it from `(0, 0)` to
`(1, 1)`, sets zero offsets, disables raycast, and copies the icon sprite,
material, active state, and fill settings. Wrapped map clones then sync this
child from the source icon.

Adjacent example: `CampaignResearchStandingPatch` adds a
`UADVP_ResearchStandingBadge` child under each technology row, anchors it to the
top-right of the row, disables raycast, and updates the badge text without
reordering the native research list.

Checklist:

- Parent to the exact graphic or row that already tracks position and scale.
- Add a `RectTransform`; use full-stretch anchors and zero offsets for fills,
  or corner anchors and explicit size for badges.
- Keep identity rotation and scale one unless matching a native child template.
- Set `raycastTarget = false` unless the overlay is the click target.
- Copy sprite/material/preserveAspect from the source every refresh.
- If the parent is cloned elsewhere, add an explicit sync path for the overlay.

Pattern:

```csharp
GameObject fillObject = new("UADVP_IconFill");
fillObject.AddComponent<RectTransform>();
fillObject.transform.SetParent(icon.transform, false);

RectTransform rect = fillObject.GetComponent<RectTransform>();
rect.anchorMin = Vector2.zero;
rect.anchorMax = Vector2.one;
rect.offsetMin = Vector2.zero;
rect.offsetMax = Vector2.zero;
rect.anchoredPosition = Vector2.zero;
rect.localRotation = Quaternion.identity;
rect.localScale = Vector3.one;

Image fill = fillObject.AddComponent<Image>();
fill.raycastTarget = false;
```

### 5. Does it need to float in a gap above or beside native content?

Reserve space explicitly and restore it when hidden.

Use when:

- there is no native row/button slot;
- the new control is conceptually a toolbar or overlay for a whole tab;
- it must sit near existing content but not inside its native list.

Current example: `CampaignFleetWindowDesignViewerPatch` adds a flag toolbar
above the Designs list. It is not simply positioned over the list. The patch:

- parents the toolbar to the window root;
- computes a safe width from the parent and the design list rect;
- estimates the top gap using `GetWorldCorners` and `InverseTransformPoint`;
- stores original `offsetMin`/`offsetMax` for the Designs list and header;
- applies a `ContentTopGap`;
- restores original offsets when leaving the tab.

Checklist:

- Avoid this path unless text injection, button cloning, or child overlay will
  not fit the feature.
- Store original rect offsets before modifying them.
- Apply a named content gap, not magic repeated position nudges.
- Restore offsets when hiding, changing tabs, or falling back.
- Recalculate width/height on refresh from live rects.
- Keep all state idempotent because refresh can run many times.
- Choose the panel parent before doing any bounds math. The parent defines the
  coordinate space for `anchoredPosition`, so measuring the correct target rect
  still fails if the panel is parented under an unrelated button row or layout
  container.
- Prefer a stable screen/root parent for manually positioned floating UI. In the
  fleet window, `window.Root.GetChild("Root") ?? window.Root` is a safer parent
  than deriving the parent from `DesignButtonsRoot` when the new control spans
  native UI regions.
- Do not assume a named semantic container is the visible area you want. In UAD
  layouts, objects such as `"Design Ships"` can be broad shell/content roots,
  not the actual table, header, or list bounds visible on screen.
- Separate the anchor rect from the reserve rect when the control sits between
  two native regions:
  - use the smallest visible rect for X/width placement, such as
    `CampaignFleetWindow.DesignHeader` when aligning to the designs table;
  - use the actual content/list rect for space reservation, such as
    `DesignScroll` or `DesignRoot`;
  - fall back to broad path-based objects only when the specific field is
    missing.
- If responsive metrics hide labels or collapse spacing unexpectedly, first log
  the measured panel width and rect names. A tiny width usually means the code
  measured the wrong rect, not that the compact layout thresholds are wrong.
- When a floating panel lands in the wrong region, log parent, anchor, reserve,
  and standard-button rect names plus local bounds before changing constants.
- Treat zero or near-zero bounds from `GetWorldCorners` as invalid geometry, not
  as a valid placement. It can mean layout is not ready yet, but it can also
  mean the chosen object is only a zero-size structural container.
- Do not require the coordinate parent itself to have usable width/height. A
  zero-size root can still be a valid parent for local coordinates when its
  visible children are manually positioned.
- If a named parent stays zero-size after a deferred retry, measure visible
  descendants instead of waiting forever. Combine non-zero active child rects
  under the header/list/scroll root, or measure the actual visible buttons rather
  than their zero-size row holder.
- Do not treat `GetWorldCorners` as the only source of truth. If a diagnostic
  tree dump shows useful `RectTransform.rect.width` / `rect.height` values while
  `GetWorldCorners` collapses to one point, pivot to local `RectTransform` data:
  `rect`, `anchoredPosition`, `offsetMin`, `offsetMax`, and sibling-local
  placement.
- For button-row references, prefer combining the visible button rects
  (`DesignView`, `Delete`, `NewDesign`, `BuildShip`, `DesignRefit`) over
  measuring `DesignButtonsRoot` if the root logs as width `0`.
- For list/table references, prefer the visible scroll/header children when
  `DesignScroll`, `DesignHeader`, `DesignRoot`, or `"Design Ships"` logs as
  width `0`.
- Do not hide invalid geometry behind fallback sizes. A `Mathf.Max(minWidth,
  measuredWidth)` guard is useful only after the measured rect is plausible; if
  the measured width is `0`, the right fix is to defer or choose a better rect.
- If a `CampaignFleetWindow.Refresh` postfix measures invalid rects, schedule a
  one-frame deferred refresh or use a late UI pass. Call `Canvas.ForceUpdateCanvases`
  before remeasuring if the screen just rebuilt rows or changed active tabs.
- If deferred retries still report zero bounds, stop retry spam and switch to a
  diagnostic tree dump: object name, `activeInHierarchy`, rect size, child count,
  and the first few non-zero descendant rects.
- When adding a floating row between native regions, prefer parenting it under
  the same local branch as the target list/buttons if that branch exposes useful
  local rect data. A broad root can be valid for coordinates, but sibling-local
  placement is usually easier to reason about when world corners are unreliable.
- Reserve native content space only after the geometry needed for final
  placement is valid, or restore the reservation before returning false. Do not
  shrink/move the list and then hide the VP panel because measurement failed.
- When bounds are already converted into the panel parent's local space, be
  careful with extra origin correction such as subtracting `parentMinX`. That can
  double-apply an offset, especially when the parent rect itself is zero-size.
- Match the panel anchors to the coordinate system used for the measured bounds.
  If the bounds are parent-local coordinates around the parent's center, use
  center anchors such as `(0.5, 0.5)` before assigning `anchoredPosition`. If the
  panel uses bottom-left anchors `(0, 0)`, convert parent-local coordinates by
  subtracting the parent rect's `xMin` / `yMin`.
- For a tab-wide action strip below a list/table, make the strip a semantic row
  with grouped slots instead of a flat pile of buttons and labels. The useful
  contract is usually:
  - row X/width follows the visible table/header, not the whole screen;
  - row Y sits between the table/list and the native standard buttons;
  - the table/list gets an explicit bottom reserve equal to the strip height plus
    a small gap;
  - each slot is a fixed button plus a flexible state label;
  - the terminal action, such as `Start`, lives in its own fixed-width group.
- Keep slot state labels short and outcome-focused. Prefer `No ship selected`
  or the selected design label over explanatory prose. Long helper text makes
  the strip feel crowded and can hide that the row geometry is finally correct.
- Clamp cloned button geometry to the new row's contract after cloning. A native
  button's original rect may be correct for the standard button row but too tall
  or wide for a compact strip.
- Log the parent rect min/max alongside the final `anchoredPosition` when a
  panel has valid geometry but is invisible. Valid bounds plus a missing panel
  usually means the last anchor/pivot conversion, not the measured rect, is now
  wrong.
- If a prior attempt created the panel under the wrong parent, clean it up or
  reparent it. Otherwise stale UI can survive and make the current code look
  broken even after the anchor logic is fixed.

Pattern:

```csharp
originalOffsetMin ??= targetRect.offsetMin;
originalOffsetMax ??= targetRect.offsetMax;

targetRect.offsetMax = new Vector2(originalOffsetMax.Value.x, originalOffsetMax.Value.y - contentGap);

toolbarRect.anchorMin = new Vector2(0.5f, 1f);
toolbarRect.anchorMax = new Vector2(0.5f, 1f);
toolbarRect.pivot = new Vector2(0.5f, 0.5f);
toolbarRect.sizeDelta = new Vector2(width, height);
toolbarRect.anchoredPosition = new Vector2(0f, -yFromTop);
```

### 6. Is it a modal popup shell?

Clone the vanilla popup shell, stretch the root/backdrop, then own the centered
window interior.

Use when:

- the feature needs more than one row;
- vanilla already has a full-screen popup layer, backdrop, button base, and
  navigation behavior;
- the screen should not fight an existing panel's cramped layout.

Current example: `InGameOptionsMenuPatch` clones
`Global/Ui/UiMain/Popup/PopupMenu`, parents it under
`Global/Ui/UiMain/Popup`, stretches the root/backdrop, configures the `Window`
rect to a centered fixed size, and builds its own content under that window.

Checklist:

- Find the popup root and popup template path at runtime.
- Parent under the same popup root with `SetParent(..., false)`.
- Stretch only the root and backdrop to the full popup area.
- Keep the `Window` rect on a centered size contract unless the feature truly
  needs full-screen content.
- Clear only the cloned window children, not the template.
- Own the interior with layout groups.
- Disable or hide the launcher button while the modal is active.

## First-Pass Investigation Checklist

Before writing code, answer these in notes or comments:

1. What vanilla class owns the surface?
2. Which method creates or refreshes the row/panel?
3. Is the object inside a layout group, scroll view, or manually anchored rect?
4. Is there a native template sibling to clone?
5. Does vanilla rewrite this UI later through a broader refresh?
6. What object should own clicks and tooltips?
7. What state proves the feature applied in `Latest.log`?

Useful searches:

```powershell
rg -n "ClassName|FieldName|Refresh|Init|Button|Toggle|LayoutRebuilder|SetSiblingIndex" E:\Codex\cpp2il_uad_diffable\DiffableCs\Assembly-CSharp
rg -n "ClassName|MethodName|SetActive|set_interactable|SetSiblingIndex|ForceRebuildLayoutImmediate" E:\Codex\cpp2il_uad_isil\IsilDump\Assembly-CSharp
```

Avoid wildcard file arguments in PowerShell `rg` calls. Search a directory or
specific file paths instead.

## Runtime Diagnostics That Save Iterations

Add one-time or change-only logs during development, then condense them before
leaving the feature.

High-value diagnostics:

- object path and active state;
- parent name and sibling index;
- `RectTransform` anchors, pivot, size, offsets, anchored position;
- layout group and `LayoutElement` values;
- source template name when cloning;
- whether the refresh hook ran before or after vanilla state changed.

Temporary helper:

```csharp
private static string RectSummary(GameObject? obj)
{
    if (obj == null)
        return "<null>";

    RectTransform? rect = obj.GetComponent<RectTransform>();
    LayoutElement? layout = obj.GetComponent<LayoutElement>();
    Transform? parent = obj.transform.parent;

    return rect == null
        ? $"{obj.name} parent={parent?.name ?? "<none>"} sibling={obj.transform.GetSiblingIndex()} no-rect"
        : $"{obj.name} parent={parent?.name ?? "<none>"} sibling={obj.transform.GetSiblingIndex()} " +
          $"anchor={rect.anchorMin}->{rect.anchorMax} pivot={rect.pivot} " +
          $"size={rect.sizeDelta} offset={rect.offsetMin}->{rect.offsetMax} pos={rect.anchoredPosition} " +
          $"layout=min({layout?.minWidth},{layout?.minHeight}) pref({layout?.preferredWidth},{layout?.preferredHeight}) flex({layout?.flexibleWidth},{layout?.flexibleHeight})";
}
```

Keep logs event-scoped. Do not log every frame from `Update` or `LateUpdate`
unless throttled and temporary.

## Common Traps

- Setting `localPosition` under a layout-group parent. The parent will likely
  overwrite it.
- Creating a new sibling in a tight native panel when an existing text field
  could carry the information.
- Appending lines without stripping prior VP lines first.
- Forgetting `LayoutRebuilder.ForceRebuildLayoutImmediate` after changing
  line counts or row heights.
- Cloning a button but leaving vanilla listeners, localization, or old tooltip
  handlers attached.
- Adding an overlay `Image` with `raycastTarget = true`, causing clicks to land
  on the overlay instead of the original button.
- Floating a toolbar without reserving content space below it.
- Failing to restore original offsets after hiding a tab-specific toolbar.
- Using one broad container both to reserve space and to compute panel X/width.
  This can produce a technically valid row in the wrong screen region, such as
  a control strip under a preview pane instead of under the target table.
- Measuring the right rect in the wrong parent coordinate space. The bounds may
  be valid, but `anchoredPosition` will still place the control in the wrong
  region if the panel lives under a different UI branch.
- Leaving stale children under an old parent after a placement attempt changes
  parent strategy.
- Trusting `GetWorldCorners` on a zero-size structural container. Identical
  corners or zero-width bounds mean the chosen object is not usable for that
  measurement yet, and it may never become usable if it is only a holder.
- Trusting `GetWorldCorners` after `RectTransform.rect` diagnostics already show
  useful local dimensions. In that case, switch measurement strategies instead
  of retrying the same world-corner path.
- Letting a minimum fallback width turn a zero-width measurement into a visible
  but misplaced compact panel.
- Waiting forever for zero-size roots to become non-zero instead of measuring
  their visible descendants.
- Reserving native content space before placement geometry is validated, then
  returning with the VP element hidden and the native layout still shifted.
- Validating the coordinate parent as though it must have visible bounds. Some
  UAD roots are useful coordinate spaces even when their own rect logs as `0x0`.
- Measuring a row holder such as `DesignButtonsRoot` when the real geometry is
  on the child buttons.
- Applying `anchoredPosition` corrections against `parentMinX`/`parentMinY`
  after already converting target bounds into the parent local coordinate space.
- Feeding parent-center local coordinates into a rect with bottom-left anchors.
  This can place a correctly sized and valid panel offscreen even though all
  measured geometry logs look good.
- Building a mixed button/label strip as flat siblings. Slot controls need their
  own small layout groups so labels can flex without pushing buttons into each
  other.
- Letting status labels carry full sentences in a cramped control strip. Once
  the panel is visually in the right place, text length and internal grouping are
  often the remaining layout problem.
- Aligning a list-adjacent action strip to the window or preview pane after the
  table itself has reliable bounds. A good strip should read as part of the list
  it controls.
- Changing `anchorMin` / `anchorMax` without rechecking the meaning of
  `anchoredPosition`. The same numeric position means different things under
  center anchors and bottom-left anchors.
- Treating disabled visuals as proof the action logic is wrong. First compare
  vanilla refresh and button state wiring.
- Hooking only the obvious refresh when a broader `Ui.Refresh`, tab switch, or
  popup path rewrites labels later.
- Searching the whole scene every frame. Cache roots/components and refresh the
  cache on a timer or lifecycle event.

## Preferred Patterns By Feature Type

| Feature shape | First choice | VP example |
| --- | --- | --- |
| Row action button | Clone nearest native sibling, same parent, sibling index | `CampaignPoliticsDeclareWarPatch` |
| Popup row action button | Clone nearest native popup button, then relayout the row if needed | `CampaignTaskForceReturnToPortPatch` |
| New setting row | Owned popup panel with layout groups and `LayoutElement` contracts | `InGameOptionsMenuPatch` |
| Compact status in country info | Existing `TMP_Text`, strip/reapply, force rebuild | `CampaignConstructionStatusPatch` |
| Compact status suffix | Existing label, regex strip/reapply, tooltip if useful | `CampaignActiveFleetStatusPatch`, `CampaignTechnologyStatusPatch` |
| Small row badge | Child badge anchored to existing row, `raycastTarget = false` | `CampaignResearchStandingPatch` |
| Icon progress/state | Child overlay anchored full-size to existing icon | `CampaignTaskForceTonnageIndicatorPatch` |
| Tab-wide selector with no slot | Floating toolbar plus explicit content gap and restore path | `CampaignFleetWindowDesignViewerPatch` |
| List/table action strip | Floating row aligned to visible table, grouped slots, explicit bottom reserve | `CampaignDesignTestBattlePatch` |
| Late vanilla label rewrite | Final-pass postfix or narrow visible-instance watchdog | `CampaignCountryInfoFinalRefreshPatch`, `CampaignCountryInfoWatchdogPatch` |

## Definition Of Done For UI Additions

- The element is attached to a native owner that refreshes in the same lifecycle
  as the screen.
- Placement uses layout/sibling/anchor contracts, not repeated coordinate
  nudges.
- Refresh is idempotent: no duplicate children, listeners, tooltips, or appended
  lines after repeated open/close/refresh.
- VP-owned labels and tooltips are not still controlled by inherited
  `LocalizeText`, `OnEnter`, or `OnLeave` handlers from a cloned template.
- Hidden or inactive states restore native layout.
- Dense rows use internal slot groups, fixed command widths, flexible state
  labels, and short text that still fits at the narrowest supported width.
- `Latest.log` can confirm the feature attached and refreshed without noisy
  per-frame spam.
- The implementation falls back cleanly if a template/path/field is missing.
