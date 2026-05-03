# UAD Vanilla Plus

UAD Vanilla Plus (`UAD:VP`) is a lightweight mod for Ultimate Admiral: Dreadnoughts that keeps the base game feel while adding small quality-of-life improvements.

Current version: `0.1.56`

## Philosophy

- No performance degradation compared to vanilla.
- No config files: installation should stay as simple as one DLL in the `Mods` folder.
- Quality-of-life changes are always enabled.
- Balance changes default to improved behavior and can be turned off individually from the in-game UAD:VP options menu.
- Balance changes are intended to make the game feel more fair: fewer extreme edge cases, fewer unrealistic exploits, and fewer outcomes where the player or AI gets punished by hidden or overly swingy mechanics.

## Installation

1. Install [MelonLoader](https://github.com/LavaGang/MelonLoader) for Ultimate Admiral: Dreadnoughts.
2. Download the latest `UADVanillaPlus.dll` from the repository releases page.
3. Copy `UADVanillaPlus.dll` into your game `Mods` folder.

Typical Steam install path:

```text
...\Steam\steamapps\common\Ultimate Admiral Dreadnoughts\Mods
```

Start the game normally after copying the DLL. If the mod loads, `UAD:VP` and the version number will appear in the game's version text.

## Features

### Campaign

**QoL:**

- Campaign maintenance indicators: show dock expansion status and transport capacity directly in the campaign country info panel.
- Campaign active fleet port count: show how many active vessels are currently in port.
- Campaign construction summaries: split own builds, foreign contracts, and commissioning ships in the existing build counts.
- Campaign technology indicator: show a compact estimate for the next expected research discovery.
- Declare War politics action: add a direct Declare War button with confirmation to campaign politics rows.
- In-game options menu: control UAD:VP balance options from the top-right game UI.

**Balance:**

- Port Strike balance: scales transport losses from undefended port strikes by attacker tonnage instead of allowing small raiders to destroy large transport groups.
- Suspend Dock Overcapacity: automatically delays lower-priority repairs, builds, and refits when monthly dock work exceeds shipyard capacity; manual mode keeps vanilla overcapacity handling.

### Design

**QoL:**

- Design ship counts: show active, building, and unavailable ships for each design.
- Designs tab country viewer: browse major AI nations' ship designs from the campaign Designs tab.

**Balance:**

- CA+ torpedo restriction: optionally disallow torpedo launchers on heavy cruisers, battlecruisers, and battleships.

### Battle

**QoL:**

- Battle speed quality-of-life: keep the player's selected battle speed available when the game tries to slow simulation speed near enemies.

**Balance:**

- Accuracy penalty balance: lets players reduce extreme smoke, stability, and instability accuracy penalties from ship design.
- Battle weather balance: optionally force daytime fair-weather battles instead of random time and bad-weather rolls.

## Known Issues

- Campaign maintenance indicators may not appear immediately on initial campaign load. They are restored after switching campaign tabs.

## Building And Running From Source

Clone the repository and build the solution with .NET 6:

```powershell
dotnet build .\UADVanillaPlus.sln -c Release
```

If the project cannot find your game install automatically, set `UAD_PATH` to the Ultimate Admiral: Dreadnoughts install folder:

```powershell
$env:UAD_PATH='E:\SteamLibrary\steamapps\common\Ultimate Admiral Dreadnoughts\'
dotnet build .\UADVanillaPlus.sln -c Release
```

The built DLL will be here:

```text
UADVanillaPlus\bin\Release\net6.0\UADVanillaPlus.dll
```

To build and copy directly into the game's `Mods` folder:

```powershell
dotnet build .\UADVanillaPlus.sln -c Release /p:DeployOnBuild=true
```

## Thanks

UAD Vanilla Plus is inspired by the work of the [Tweaks and Fixes / UAD Realism DIP team](https://github.com/DukeDagor/UADRealismDIP), especially as a reference for how Ultimate Admiral: Dreadnoughts modding works.
