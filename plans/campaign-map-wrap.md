# Campaign Map Wrap Plan

## Current State

- Feature name in the UAD:VP options menu: `Map Geometry`.
- Option values: `Disc World` enables the experimental wrap illusion; `Flat Earth` keeps vanilla map geometry.
- Menu location: UAD:VP options -> `Experimental`.
- Default state: off.
- Current version after adding generic mesh/material diagnostics for the missing country-line layer: `0.2.39`.
- Current source files:
  - `UADVanillaPlus/Harmony/CampaignMapWrapVisualPatch.cs`
  - `UADVanillaPlus/GameData/ModSettings.cs`
  - `UADVanillaPlus/Harmony/InGameOptionsMenuPatch.cs`
  - `README.md`

## What Works

- Horizontal camera bounds can expand by one map width on either side when the option is enabled.
- The Pacific edge can be shown as a wrap illusion by rendering neighbor copies of the campaign map surface at `+/- CampaignMap.mapWidth`.
- The wrapped side maps now render with the correct source map material layers, native grid, area/province labels, and political country overlays.
- Port, task-force, and event/mission UI marker copies are synced to the side maps from vanilla `CampaignMapElement.UpdatePositionScale` calls.
- Wrapped port, task-force, and event/mission marker copies can proxy clicks and hover handling to the original marker/button.
- Wrapped-map movement clicks now keep the visual side-map coordinate instead of forcing the destination back into the original map bounds. This lets vanilla pathfinding, the move dialog, and the final Move command share the same seam-aware destination value.
- Wrapped side-map terrain copies now have map-layer raycast colliders so vanilla can detect right-click destination clicks on the copied map surface.
- Task-force route and destination marker visuals are mirrored to the side maps from vanilla `MapUI.GetRoute(...)` results.
- An attempted `CampaignShipsMovementManager.RequestPath(...)` route-bias hook in `0.1.97` broke the movement dialog by producing invalid `PathResult` data and was removed in `0.1.98`.
- `0.1.99` adds temporary diagnostics only:
  - `UADVP map wrap diag click ...` logs raw vs normalized map click coordinates.
  - `UADVP map wrap diag route ...` logs displayed route path count, approximate length, first/last points, largest segment, and seam-like segment count.
- `0.1.100` adds more temporary diagnostics only:
  - `UADVP map wrap diag move-window show ...` logs `MoveShipsWindow.ShowMapToMap(...)` from/to coordinates and `PathResult` summary.
  - `UADVP map wrap diag move-window text ...` logs the `PathResult` summary received by `MoveShipsWindow.UpdateTextData(...)`.
- `0.1.101` removes the `move-window text` hook because the Il2Cpp `ref PathResult` prefix crashed as soon as the move dialog opened. Keep using the safer `move-window show` diagnostic for now.
- `0.1.102` changes wrapped movement clicks from normalized coordinates to effective visual side-map coordinates:
  - `UADVP map wrap diag click ...` now logs raw, normalized, and effective positions.
  - `UADVP map wrap diag move-window show ...` now logs the same effective click so path endpoints can be compared with the actual dialog destination.
- `0.1.103` keeps track of whether the selected task-force marker came from a wrapped side-map copy:
  - Wrapped-copy selection stores a `+/- CampaignMap.mapWidth` offset so the next wrapped destination click is translated back into the selected marker's local map space before vanilla validates the click.
  - Vanilla/original `ShipUI.OnPointerClick` selection resets the stored offset to `0`.
  - If vanilla pathfinding stops a wrapped destination route at the original map edge, the move-dialog path list gets a final destination point appended for the visible route line.
- `0.1.104` separates player intent from vanilla validation:
  - `desired` is the visual destination implied by the clicked map copy and the selected task-force copy offset.
  - `effective` is now a near-edge routing proxy when `desired` is outside the original map bounds, so vanilla pathfinding sees a valid edge-adjacent target instead of a deep off-map point.
  - `MoveShipsWindow.ShowMapToMap(...)` swaps the dialog destination back to `desired` and extends the path endpoint after vanilla pathfinding succeeds.
- `0.1.105` changes wrapped task-force destination selection from blind offset subtraction to nearest-copy selection:
  - Wrapped marker clicks store both the marker copy offset and the marker's visual world position.
  - A destination click is first normalized to the original-map geography, then the patch chooses the equivalent visual copy nearest to the selected marker copy.
  - This avoids forcing west/east edge routes when a ship selected on a side-map copy is sent to a destination visible on the main map.
- `0.1.106` removes the noisier route/path-extension debug stream and keeps consolidated movement diagnostics:
  - `UADVP map wrap diag click ...` records the click hit, selected marker visual position, desired destination, routing proxy, and map bounds.
  - `UADVP map wrap diag move ...` records the move-window destination, original routing proxy, whether the path endpoint was extended, and the final path summary.
- `0.2.0` expands `Disc World` marker coverage:
  - Wrapped dynamic marker copies now include `EventUI` mission/event markers under `MapUI.EventsRoot`.
  - Wrapped marker copies mirror graphic, text, and line-renderer state from the authoritative marker so enemy task forces, mission icons, and other marker state changes remain visually current.
  - Wrapped event/mission marker copies proxy click, hover, and leave handling to the original marker button.
- `0.2.1` changes the camera model from a fixed three-map strip to seamless rotation:
  - Horizontal camera bounds are canonical again; when the camera crosses a Pacific edge it is shifted by one map width to the opposite edge.
  - Left/right map, grid, label, country-overlay, marker, and route copies stay pooled but only activate when that side of the map is visible.
  - The original map remains the authoritative vanilla map; side copies are visual/raycast proxies for whichever seam is currently in view.
- Native `MapVisualGrid` cloning is the correct grid path. Earlier procedural grid attempts rendered on top of the map and changed ocean color.
- Country overlays must be created after the game swaps neutral materials for live `player-*` materials. Creating them immediately after `CampaignMap.PostInit` produces grey masks.
- `0.2.36` adds a one-shot `UADVP map wrap border diag ...` summary after country overlays are ready. It counts likely `CampaignBordersManager` sources and samples renderer paths/materials without cloning anything.
- `0.2.37` prioritizes named border-candidate samples and separates VP-generated overlay children from original country highlight meshes so the useful renderer paths are visible in the log.
- `0.2.38` adds a one-shot active-scene border diagnostic that excludes the known `WorldEx/2DMap` root and VP-generated wrap objects, then reports line-renderer and border-keyword candidates elsewhere in the scene.
- `0.2.39` adds generic mesh/material sampling for the `WorldEx/2DMap` root and visible non-text scene meshes, including material, shader, render queue, and common texture names.
- The toggle is intended to work live:
  - Enabling creates current campaign map wrap visuals if the map already exists.
  - Disabling clears wrap visuals, restores side borders, and lets vanilla camera bounds run again.

## Important Implementation Notes

- `CampaignMapWrapVisualPatch` is visual-only for now. It does not make the world truly cylindrical.
- The working approach is an illusion: keep the original map authoritative, show pooled side copies only near the visible seam, and rotate the camera by one map width when it crosses an edge.
- The base map copies use the original map mesh and cloned live source map materials at a high render queue.
- The base map copies also receive a `MeshCollider` using the same mesh, layer, and tag as the vanilla map surface, so `CampaignMap.DetectClick()` can hit them.
- Native side borders are hidden while wrap is enabled and restored when disabled.
- Static visual roots are cloned and non-rendering behaviours/colliders are disabled so they do not act like live UI/game objects.
- Country/state border-line rendering on side maps remains unresolved. A broad passive-root clone from `CampaignBordersManager` disrupted normal map label/country-name composition and was reverted; the current pass is diagnostic-only until the exact renderer path is known.
- Port/task-force/event marker copies keep the original markers authoritative and derive their side-map positions from vanilla UI positions plus `+/- CampaignMap.mapWidth`.
- Port marker copies selectively re-enable the cloned `PortButton` raycast path and forward vanilla `OnClickH`, `OnEnter`, and `OnLeave` handling to the original `PortButton`.
- Task-force marker copies selectively re-enable the cloned `ShipUI.Btn` raycast path and forward vanilla `OnClickH`, `OnEnter`, and `OnLeave` handling to the original `ShipUI.Btn`.
- Event/mission marker copies selectively re-enable the cloned `EventUI.Btn` raycast path and forward vanilla `OnClickH`, `OnEnter`, and `OnLeave` handling to the original `EventUI.Btn`.
- `CampaignMap.OnClickDetected` records raw, normalized, desired, effective, selected marker offset, and selected marker visual position. `desired` preserves the player-facing destination chosen by nearest-copy logic; `effective` is the value passed to vanilla pathfinding. For off-map desired destinations, effective is clamped to a small proxy just outside the nearest original map edge.
- `MoveShipsWindow.ShowMapToMap(...)` is currently used as the safer hook to inspect and lightly repair the move-dialog route path. When the click used a routing proxy, the patch changes the dialog `to` argument back to the desired destination and appends that destination to `FullPath` if the vanilla path stopped at the original map edge.
- Do not reintroduce a `RequestPath(...)` prefix that calls `RequestPath(...)` recursively without first proving the returned `PathResult` is safe for `MoveShipsWindow.UpdateTextData(...)` and `CampaignShipsMovementManager.BaseMoveShips(...)`.
- `MapUI.GetRoute(...)` remains authoritative for route generation. The wrap patch mirrors each returned `Route` object at `+/- CampaignMap.mapWidth` and keeps line renderers, destination markers, materials, and active states synced.
- Country overlays are retried from the camera-bounds patch at a throttled frame interval until live player materials are available.
- Logs are intentionally limited to lifecycle messages, warnings, and two consolidated map-wrap diagnostics while movement/route behavior is still being verified.

## Known Limitations

- Port, task-force, and `EventUI` mission/event icon markers are wrapped. Battles, minefields, zones, and other live map markers may still use vanilla map behavior.
- Port/task-force/mission marker clicks and hover are proxied from wrapped copies. Movement destination clicks use visual side-map coordinates on wrapped map surfaces, but non-movement map-surface hit testing may still use vanilla map behavior.
- Route visuals are mirrored, not regenerated as true cylindrical routes. If vanilla pathfinding chooses a long path, the visual copies will faithfully mirror that long path.
- Camera panning now visually rotates at the Pacific seam, but the underlying campaign state is still vanilla/canonical rather than a truly cylindrical world model.
- The feature is experimental and off by default because the player-facing illusion is promising but incomplete.

## Likely Next Steps

1. Verify movement and route behavior near the seam.
   - Test right-map-to-left-map and left-map-to-right-map movement orders.
   - Confirm wrapped side-map right-clicks now create nearby destinations instead of ignoring the click.
   - Watch whether vanilla pathfinding still chooses long routes even after destination coordinates normalize.
   - Compare `diag route` length and endpoints for the same destination clicked on the original map vs the wrapped side map.
   - Avoid mutating `RequestPath(...)` results until the exact `PathResult` contract is clear.

2. Handle route/path visuals.
   - Look for task-force path line renderers or UI route drawing.
   - Either clone visual routes to side maps or redraw seam-aware route segments.

3. Wrap the remaining passive dynamic visuals.
   - Check battles, minefields, sea-control zones, and any other `MapUI` roots not covered by ports, task forces, and `EventUI` mission/event markers.
   - Keep clone counts cached and avoid scene scans in hot paths.

4. Improve camera behavior.
   - Verify seam-crossing while dragging, keyboard scrolling, minimap jumps, zoomed-out views, and selected-task-force movement.
   - Consider smoothing or suppressing any visible snap if the camera shift is noticeable in play.

5. Verify performance.
   - Watch clone counts and retry logic.
   - Avoid per-frame scene scans. Cache roots/renderers where possible.

6. Update README Known Issues as the feature graduates.
   - Remove or narrow the visual-only warning only after dynamic markers and interactions are handled.

## Helpful Quote For Context

Do not try to make the world round. That is impossible. Instead, realize the truth: there is no round world. Then you will see it is not the map that wraps. It is one flat world, quietly moved under the camera.
