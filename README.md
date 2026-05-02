# UAD Vanilla Plus

UAD Vanilla Plus (`UAD:VP`) is a clean, minimal Ultimate Admiral: Dreadnoughts MelonLoader mod scaffold.

Current version: `0.1.2`

This mod currently:

- registers the Melon mod as `UAD:VP`
- uses a SemVer version to avoid loader warnings
- replaces the in-game version overlay text with `UAD:VP 0.1.2`
- adds a Designs tab country viewer for browsing major AI nation designs

Build locally:

```powershell
E:\Codex\dotnet\dotnet.exe build E:\Codex\UADVanillaPlus\UADVanillaPlus.sln -c Release
```

Build and copy to the game `Mods` folder:

```powershell
E:\Codex\dotnet\dotnet.exe build E:\Codex\UADVanillaPlus\UADVanillaPlus.sln -c Release /p:DeployOnBuild=true
```
