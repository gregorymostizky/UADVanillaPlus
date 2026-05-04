# UAD Vanilla Plus Agent Notes

## Working Rules

- Before continuing an existing multi-session investigation, check `plans/` for handoff notes. In particular, campaign map wrap-around follow-up work should start with `plans/campaign-map-wrap.md`.
- Bump `UADVanillaPlus/ModInfo.cs` `MelonVersion` for every user-visible code or behavior change.
- Keep the in-game overlay version and MelonLoader metadata consistent through `ModInfo.DisplayText`.
- After a successful build, always try the built-DLL copy immediately. Copy directly to the game `Mods` folder without first checking whether the game is running; if the DLL is locked, let the copy fail and report that.
- When the user asks to merge, push, update GitHub, or otherwise publish completed work, commit locally, fast-forward/merge the work to `master`, and push `master` unless they explicitly limit it to local-only or ask for a different branch.
- The GitHub CLI is installed at `E:\Codex\tools\gh\gh_2.92.0_windows_amd64\bin\gh.exe`; use that full path when `gh` is not on `PATH`.
- When creating or updating GitHub release notes, summarize the major player-facing highlights since the previous public release/tag, not only the final commit. Use the tag range, such as `previous_tag..new_tag`, and group the notes by user-facing area when helpful.
- Do not stop at a feature branch or PR branch for normal completed work. Use feature branches only as temporary work branches or when the user explicitly asks for a PR-style flow.
- Keep feature ports modular. Each QoL port or gameplay change should ideally live in its own source file under a clear folder, with only small shared helpers in `GameData` or similar common areas.
- Do not add loose config files for player-facing balance options. Balance-affecting features should be controlled through the in-game VP options menu, with shared option state living behind a typed helper in `GameData`.
- Keep QoL changes always enabled, while balance changes default to improved/on and can be toggled individually back to vanilla in-game.
- Port only the requested behavior from TAF/DIP. Avoid pulling unrelated config systems, UI rewrites, fleet tab changes, data edits, or gameplay logic as hidden dependencies.
- Prefer VP names for new UI objects and logs, such as `UADVP_...`, rather than carrying over `TAF_...` names.
- Update `README.md` when adding major player-facing features, installation changes, or source-build workflow changes. Keep README consumer-friendly: describe the main feature value, not every implementation detail or internal versioning rule.
- Order README feature bullets by user value/impact within each subsection, not by implementation chronology. Use judgment: frequently checked, high-friction, or high-consequence gameplay improvements should appear before smaller conveniences.
- In README feature lists, bold the feature name before the colon, such as `**Campaign maintenance indicators**: ...`.
- Never commit or push to `master` unless the user asks for a commit, push, merge, or GitHub update. Once they do, `master` is the default target in this repo.
- For development work, truth-seek against the available game disassembly before guessing how UAD works. The workspace has both skeleton/diffable and fuller IL views available at `E:\Codex\cpp2il_uad_diffable` and `E:\Codex\cpp2il_uad_isil`; inspect the relevant game classes/methods there when behavior or signatures are uncertain.
- Before adding Harmony diagnostics to Il2Cpp methods with unusual signatures, verify the exact decompiled signature and likely marshaling behavior up front. Be especially careful around `ref`/`out` parameters, value-type structs, Il2Cpp collection fields, nested generic types, and UI hot paths; prefer safer surrounding hooks or read-only postfixes when the direct hook shape is uncertain.
- Be performance-conscious by default. One of VP's goals is to avoid TAF/DIP-style overhead, so watch for hot paths, broad polling, expensive UI rebuilds, repeated reflection, allocations in frequent hooks, and large data scans. Push back when a requested idea is likely to hurt performance, and prefer designs that cache, narrow scope, or hook less frequently.
- Be liberal with temporary logs and timings when debugging. UAD behavior is often unclear from source alone and reruns are expensive, so optimize for enough upfront evidence to diagnose from `Latest.log`. Keep debug output clearly prefixed/scoped so it can be removed or gated later.
- When a player-visible rough edge is accepted as "good enough for now", document it in `README.md` under Known Issues so future sessions do not rediscover it from scratch.
- For campaign UI text patches, assume vanilla may rewrite labels through multiple paths after the obvious `CampaignCountryInfoUI.Refresh` call. Prefer native getter postfixes or final-pass repair hooks over one-shot text writes, and keep any watchdog narrow, cached, and visible-instance scoped. For campaign maintenance indicators specifically, render inside vanilla's existing multi-line `ShipbuildingCapacity` label and force the country-info layout rebuild; do not append a second line to `ShipyardSize` or position a floating TMP clone, because those approaches can push or overlap neighboring rows.

## Build

Use the workspace-local .NET home so builds do not write to user-profile locations:

```powershell
$env:DOTNET_CLI_HOME='E:\Codex\.dotnet_home'
$env:NUGET_PACKAGES='E:\Codex\.nuget\packages'
E:\Codex\dotnet\dotnet.exe build E:\Codex\UADVanillaPlus\UADVanillaPlus.sln -c Release /p:RestoreConfigFile=E:\Codex\UADVanillaPlus\NuGet.Config
```

Copy the built DLL directly after a successful build. Do not run a process check first; if the game has the DLL locked, let the copy fail and report that:

```powershell
Copy-Item -LiteralPath 'E:\Codex\UADVanillaPlus\UADVanillaPlus\bin\Release\net6.0\UADVanillaPlus.dll' -Destination 'E:\SteamLibrary\steamapps\common\Ultimate Admiral Dreadnoughts\Mods\UADVanillaPlus.dll' -Force
```

## Current Feature Layout

- `plans/`: cross-session implementation notes and next-step plans. Read the relevant file before resuming a planned feature.
- `Harmony/UiVersionTextPatch.cs`: version text overlay only.
- `Harmony/CampaignFleetWindowDesignViewerPatch.cs`: Designs tab country viewer and design ship-count display only.
- `Harmony/CampaignConstructionStatusPatch.cs`: campaign construction summary/count display plus campaign maintenance indicators only.
- `Harmony/CampaignCountryInfoFinalRefreshPatch.cs`: final-pass campaign country-info decoration after broad vanilla UI refreshes only.
- `Harmony/CampaignCountryInfoWatchdogPatch.cs`: narrow campaign country-info repair pass for vanilla tab/popup rewrites only; keep checks cheap and scoped to visible country-info instances.
- `Harmony/CampaignPortShipCountPatch.cs`: campaign world-map port-name active vessel counts only.
- `Harmony/CampaignActiveFleetStatusPatch.cs`: campaign Active Fleet in-port count display only.
- `Harmony/CampaignTechnologyStatusPatch.cs`: campaign country-info technology timing indicator only.
- `Harmony/InGameOptionsMenuPatch.cs`: top-right UAD:VP in-game options menu only.
- `Harmony/CampaignPoliticsDeclareWarPatch.cs`: politics row Declare War and Force Peace buttons only.
- `Harmony/CampaignMapWrapVisualPatch.cs`: experimental campaign map visual wrap-around only; off by default from the UAD:VP Experimental options section.
- `Harmony/BattleTimeSpeedLimitPatch.cs`: battle simulation speed limit QoL only.
- `Harmony/BattleWeatherBalancePatch.cs`: battle weather/daytime balance option only.
- `Harmony/BattleAccuracyPenaltyBalancePatch.cs` and `GameData/AccuracyPenaltyBalance.cs`: battle design-side accuracy penalty balance option only; rewrites selected `StatData.effect` strings before vanilla `PostProcess` parses them, avoiding combat-time overhead and loaded-dictionary mutation.
- `Harmony/BattleStartAccuracyBreakdownPatch.cs`: battle-accept design accuracy diagnostic logging only.
- `Harmony/Il2CppInteropExceptionPatch.cs`: compatibility/debug logging for Il2Cpp trampoline exceptions only.
- `Harmony/PortStrikeBalancePatch.cs`: port strike transport-loss balance option only.
- `Harmony/DesignTorpedoRestrictionPatch.cs`: CA+ torpedo availability balance option only.
- `GameData/CampaignDiplomacyActions.cs`: small diplomacy validation/action helpers for campaign politics patches.
- `GameData/ExtraGameData.cs`: small campaign/player lookup helpers.
- `GameData/ModSettings.cs`: typed in-game option state only; feature patches should read options from here instead of parsing files.
- `GameData/PlayerExtensions.cs`: small player fleet enumeration helpers.

## High-Level Design

- `UADVanillaPlusMod.cs` is the MelonLoader entrypoint. It should stay small: patch registration, startup logging, and lifecycle hooks only.
- `ModInfo.cs` is the single source of truth for mod identity, SemVer, and displayed version text. Do not duplicate version strings in patches.
- `Harmony/` owns behavior changes implemented through Harmony patches. Each feature should get its own patch file named after the game surface it changes, such as `CampaignFleetWindowDesignViewerPatch.cs`.
- `GameData/` owns small read/query helpers around UAD campaign objects. Keep these helpers generic and side-effect free so multiple feature patches can share them safely.
- Campaign country-info additions are currently split between native text producers and final repair hooks because some vanilla tab/popup paths repaint labels after normal refresh. Campaign maintenance indicators use vanilla's `ShipbuildingCapacity` multi-line text field (`maintenance`, `Shipbuilding Capacity:`, then used/capacity) instead of modifying `ShipyardSize` layout height or using a positioned overlay.
- Future folders should follow responsibility, not chronology. For example, put reusable UI construction helpers under `Ui/`, data import/export helpers under `Data/`, and ship/designer calculations under `ShipDesign/` if those areas become real modules.
- Feature patches should explain their intent in comments near the class or non-obvious methods: what behavior changes, why VP wants it, and what vanilla behavior is being protected.
