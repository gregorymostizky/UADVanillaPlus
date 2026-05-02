# UAD Vanilla Plus Agent Notes

## Working Rules

- Bump `UADVanillaPlus/ModInfo.cs` `MelonVersion` for every user-visible code or behavior change.
- Keep the in-game overlay version and MelonLoader metadata consistent through `ModInfo.DisplayText`.
- For copy requests, copy the built DLL directly to the game `Mods` folder. Do not check whether the game is running first; if the DLL is locked, let the copy fail and report that.
- Keep feature ports modular. Each QoL port or gameplay change should ideally live in its own source file under a clear folder, with only small shared helpers in `GameData` or similar common areas.
- Port only the requested behavior from TAF/DIP. Avoid pulling unrelated config systems, UI rewrites, fleet tab changes, data edits, or gameplay logic as hidden dependencies.
- Prefer VP names for new UI objects and logs, such as `UADVP_...`, rather than carrying over `TAF_...` names.
- Update `README.md` when adding major player-facing features, installation changes, or source-build workflow changes. Keep README consumer-friendly: describe the main feature value, not every implementation detail or internal versioning rule.
- Never commit or push to `master` unless the user explicitly asks for that action.
- For development work, truth-seek against the available game disassembly before guessing how UAD works. The workspace has both skeleton/diffable and fuller IL views available at `E:\Codex\cpp2il_uad_diffable` and `E:\Codex\cpp2il_uad_isil`; inspect the relevant game classes/methods there when behavior or signatures are uncertain.
- Be performance-conscious by default. One of VP's goals is to avoid TAF/DIP-style overhead, so watch for hot paths, broad polling, expensive UI rebuilds, repeated reflection, allocations in frequent hooks, and large data scans. Push back when a requested idea is likely to hurt performance, and prefer designs that cache, narrow scope, or hook less frequently.
- Be liberal with temporary logs and timings when debugging. UAD behavior is often unclear from source alone and reruns are expensive, so optimize for enough upfront evidence to diagnose from `Latest.log`. Keep debug output clearly prefixed/scoped so it can be removed or gated later.

## Build

Use the workspace-local .NET home so builds do not write to user-profile locations:

```powershell
$env:DOTNET_CLI_HOME='E:\Codex\.dotnet_home'
$env:NUGET_PACKAGES='E:\Codex\.nuget\packages'
E:\Codex\dotnet\dotnet.exe build E:\Codex\UADVanillaPlus\UADVanillaPlus.sln -c Release /p:RestoreConfigFile=E:\Codex\UADVanillaPlus\NuGet.Config
```

Copy the built DLL directly when requested:

```powershell
Copy-Item -LiteralPath 'E:\Codex\UADVanillaPlus\UADVanillaPlus\bin\Release\net6.0\UADVanillaPlus.dll' -Destination 'E:\SteamLibrary\steamapps\common\Ultimate Admiral Dreadnoughts\Mods\UADVanillaPlus.dll' -Force
```

## Current Feature Layout

- `Harmony/UiVersionTextPatch.cs`: version text overlay only.
- `Harmony/CampaignFleetWindowDesignViewerPatch.cs`: Designs tab country viewer only.
- `Harmony/CampaignPoliticsDeclareWarPatch.cs`: politics row Declare War button only.
- `GameData/CampaignDiplomacyActions.cs`: small diplomacy validation/action helpers for campaign politics patches.
- `GameData/ExtraGameData.cs`: small campaign/player lookup helpers.
- `GameData/PlayerExtensions.cs`: small player fleet enumeration helpers.

## High-Level Design

- `UADVanillaPlusMod.cs` is the MelonLoader entrypoint. It should stay small: patch registration, startup logging, and lifecycle hooks only.
- `ModInfo.cs` is the single source of truth for mod identity, SemVer, and displayed version text. Do not duplicate version strings in patches.
- `Harmony/` owns behavior changes implemented through Harmony patches. Each feature should get its own patch file named after the game surface it changes, such as `CampaignFleetWindowDesignViewerPatch.cs`.
- `GameData/` owns small read/query helpers around UAD campaign objects. Keep these helpers generic and side-effect free so multiple feature patches can share them safely.
- Future folders should follow responsibility, not chronology. For example, put reusable UI construction helpers under `Ui/`, data import/export helpers under `Data/`, and ship/designer calculations under `ShipDesign/` if those areas become real modules.
- Feature patches should explain their intent in comments near the class or non-obvious methods: what behavior changes, why VP wants it, and what vanilla behavior is being protected.
