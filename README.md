# UAD Vanilla Plus

UAD Vanilla Plus (`UAD:VP`) is a lightweight mod for Ultimate Admiral: Dreadnoughts that keeps the base game feel while adding small quality-of-life improvements.

Current version: `0.4.13`

## Philosophy

- No performance degradation compared to vanilla.
- No config files: installation should stay as simple as one DLL in the `Mods` folder.
- Quality-of-life changes are always enabled.
- Most balance changes default to improved behavior and can be turned off individually from the in-game UAD:VP options menu; sharper or experimental options may default to vanilla.
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

- **Campaign maintenance indicators**: show dock expansion status and transport capacity directly in the campaign country info panel.
- **Task force tonnage indicators**: fill campaign-map task-force icons by battle tonnage, with 100,000 tons and above shown as a full stack.
- **Task force return shortcut**: add a `Return to <origin port>` button to task-force popups for one-click orders back to port.
- **Campaign battle auto-resolve odds**: show the player's vanilla auto-resolve win chance in the battle popup.
- **Campaign map port ship counts**: darken and bold ports with player or AI vessels, lightly mute empty ports, and show counts beside occupied port names on the world map.
- **Campaign active fleet port count**: show how many active vessels are currently in port.
- **Campaign construction summaries**: split own builds, foreign contracts, and commissioning ships in the existing build counts.
- **Campaign technology indicator**: show a compact estimate for the next expected research discovery.
- **Research standing markers**: show colored badges spelling out whether each research category is ahead of, behind, or tied with the leading major nation.
- **Direct diplomacy politics actions**: add Declare War and Force Peace buttons with confirmation to campaign politics rows, with Force Peace using the vanilla reparation flow when war victory points produce a clear winner.
- **In-game options menu**: control UAD:VP balance options from the top-right game UI.

**Balance:**

- **Port Strike balance**: scales transport losses from undefended port strikes by attacker tonnage instead of allowing small raiders to destroy large transport groups.
- **Suspend Dock Overcapacity**: automatically delays lower-priority repairs, builds, and refits when monthly dock work exceeds shipyard capacity; manual mode keeps vanilla overcapacity handling.
- **Canal openings**: optional setting to open the Panama and Kiel canals from 1890 when a campaign map loads, matching early-campaign canals such as Suez; historical mode keeps vanilla's 1914 and 1895 opening years.
- **Technology Spread**: optional `Gradual`, `Swift`, and `Unrestricted` modes that help major nations catch up faster in research categories where they trail the current leader. This defaults to vanilla.
- **Campaign End Date**: optional setting to disable vanilla's forced 1965 retirement so campaigns can continue past the normal end date. This defaults to enabled.
- **Mine Warfare**: optional setting to disable minefield damage in existing campaigns and hide mine and minesweeping equipment from the ship designer. This defaults to enabled.
- **Submarine Warfare**: optional setting to disable submarine construction and submarine campaign battles while leaving existing submarines in saved campaigns untouched. This defaults to enabled.

### Design

**QoL:**

- **Design ship counts**: show active, building, and unavailable ships for each design.
- **Designs tab country viewer**: browse major AI nations' ship designs from the campaign Designs tab.
- **Refit design names**: use compact `Class (year)` names for player and AI refit designs, with same-year conflicts written as `Class (yearb)`, `Class (yearc)`, and so on.
- **British late-hull tower availability**: correct missing campaign compatibility between the Battlecruiser VI, G3, and N3 hull families and their matching late British main and secondary towers.

**Balance:**

- **CA+ torpedo restriction**: optionally disallow torpedo launchers on heavy cruisers, battlecruisers, and battleships.
- **Obsolete tech and hull retention**: optional player-only setting to keep already researched obsolete hulls and components available in ship design while AI design availability stays vanilla. This defaults to vanilla.
- **Superstructure Compatibility**: optional player-only `Unrestricted` mode that lets researched main towers, secondary towers, and funnels be used beyond their vanilla hull-family compatibility. Tech, country, ship class, mount, and placement checks still apply.

### Battle

**QoL:**

- **Battle speed quality-of-life**: keep the player's selected battle speed available when the game tries to slow simulation speed near enemies.
- **Battle division AI control**: add an `AI` division-order toggle with `6` hotkey support in battle so selected friendly divisions can be handed back to AI control, with manual right-click orders returning them to player control.

**Balance:**

- **Accuracy penalty balance**: lets players reduce extreme smoke, stability, and instability accuracy penalties from ship design.
- **Battle weather balance**: optionally force daytime, clear skies, calm wind, and calm seas instead of random bad-weather rolls.

### Experimental

- **Map Geometry**: optional `Disc World` seamless visual wrap-around for the campaign map at the Pacific edge, including source map material detail on side maps, clickable port/task-force/mission marker copies, wrapped task-force route visuals, and wrapped-map movement clicks/destinations. The `Flat Earth` setting keeps vanilla map geometry and remains the default.
- **Experimental Nation Ship Paints**: optional nation-themed ship paint schemes for designer previews and battles, with editable per-nation paint strings for hull, superstructure, and gun colors. This is disabled by default so vanilla ship materials remain unchanged unless players opt in.

## Known Issues

- Map Geometry's `Disc World` mode is still experimental. Map surface/material details, labels, political overlays, grid visuals, wrapped port/task-force/mission marker clicks, task-force route visuals, and wrapped-map movement destination clicks wrap. Country/state border-line rendering on side maps is still under diagnostic investigation, and some less-common map interactions and marker types may still use vanilla map behavior.
- Experimental Nation Ship Paints is still being tuned for visual consistency and battle-load performance. Runtime texture recoloring remains disabled; the option currently uses material color clones only when explicitly enabled.
- Superstructure Compatibility's `Unrestricted` mode is intentionally conservative, but newly exposed tower and funnel combinations may still need class-group tightening or a denylist after broader campaign testing.

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

## Borrowing Code

Other Ultimate Admiral: Dreadnoughts modders are welcome to borrow, adapt, or learn from this code for their own mods. Credit is appreciated when copying larger pieces, but the main goal is to make useful modding patterns easier to share.

## Modding Notes

Features are split into focused Harmony patches where possible so individual ideas can be traced without importing the whole mod. If a small helper or patch saves you time in another project, feel free to use it.

## Thanks

UAD Vanilla Plus is inspired by the work of the [Tweaks and Fixes / UAD Realism DIP team](https://github.com/DukeDagor/UADRealismDIP), especially as a reference for how Ultimate Admiral: Dreadnoughts modding works.
