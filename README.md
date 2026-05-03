# UAD Vanilla Plus

UAD Vanilla Plus (`UAD:VP`) is a lightweight mod for Ultimate Admiral: Dreadnoughts that keeps the base game feel while adding small quality-of-life improvements.

Current version: `0.2.20`

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

- <u>Campaign maintenance indicators</u>: show dock expansion status and transport capacity directly in the campaign country info panel.
- <u>Task force return shortcut</u>: add a `Return to <origin port>` button to task-force popups for one-click orders back to port.
- <u>Campaign battle auto-resolve odds</u>: show the player's vanilla auto-resolve win chance in the battle popup.
- <u>Campaign map port ship counts</u>: darken and bold ports with player or AI vessels, lightly mute empty ports, and show counts beside occupied port names on the world map.
- <u>Campaign active fleet port count</u>: show how many active vessels are currently in port.
- <u>Campaign construction summaries</u>: split own builds, foreign contracts, and commissioning ships in the existing build counts.
- <u>Campaign technology indicator</u>: show a compact estimate for the next expected research discovery.
- <u>Direct diplomacy politics actions</u>: add Declare War and Force Peace buttons with confirmation to campaign politics rows, with Force Peace using the vanilla reparation flow when war victory points produce a clear winner.
- <u>In-game options menu</u>: control UAD:VP balance options from the top-right game UI.

**Balance:**

- <u>Port Strike balance</u>: scales transport losses from undefended port strikes by attacker tonnage instead of allowing small raiders to destroy large transport groups.
- <u>Suspend Dock Overcapacity</u>: automatically delays lower-priority repairs, builds, and refits when monthly dock work exceeds shipyard capacity; manual mode keeps vanilla overcapacity handling.

### Design

**QoL:**

- <u>Design ship counts</u>: show active, building, and unavailable ships for each design.
- <u>Designs tab country viewer</u>: browse major AI nations' ship designs from the campaign Designs tab.

**Balance:**

- <u>CA+ torpedo restriction</u>: optionally disallow torpedo launchers on heavy cruisers, battlecruisers, and battleships.

### Battle

**QoL:**

- <u>Battle speed quality-of-life</u>: keep the player's selected battle speed available when the game tries to slow simulation speed near enemies.

**Balance:**

- <u>Accuracy penalty balance</u>: lets players reduce extreme smoke, stability, and instability accuracy penalties from ship design.
- <u>Battle weather balance</u>: optionally force daytime fair-weather battles instead of random time and bad-weather rolls.

### Experimental

- <u>Map Geometry</u>: optional `Disc World` seamless visual wrap-around for the campaign map at the Pacific edge, including clickable port/task-force/mission marker copies, wrapped task-force route visuals, and wrapped-map movement clicks/destinations. The `Flat Earth` setting keeps vanilla map geometry and remains the default.

## Known Issues

- Map Geometry's `Disc World` mode is still experimental. Map surface, labels, political overlays, grid visuals, wrapped port/task-force/mission marker clicks, task-force route visuals, and wrapped-map movement destination clicks wrap, but some less-common map interactions and marker types may still use vanilla map behavior.

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
