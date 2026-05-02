using HarmonyLib;
using MelonLoader;

[assembly: MelonGame("Game Labs", "Ultimate Admiral Dreadnoughts")]
[assembly: MelonInfo(typeof(UADVanillaPlus.UADVanillaPlusMod), UADVanillaPlus.ModInfo.ShortName, UADVanillaPlus.ModInfo.MelonVersion, "GG")]
[assembly: MelonColor(255, 80, 180, 255)]
[assembly: HarmonyDontPatchAll]

namespace UADVanillaPlus;

public sealed class UADVanillaPlusMod : MelonMod
{
    public override void OnInitializeMelon()
    {
        HarmonyInstance.PatchAll(MelonAssembly.Assembly);
        LoggerInstance.Msg($"{ModInfo.DisplayText} initialized.");
    }
}
