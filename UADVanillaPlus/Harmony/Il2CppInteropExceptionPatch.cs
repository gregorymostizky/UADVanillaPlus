using HarmonyLib;
using MelonLoader;

namespace UADVanillaPlus.Harmony;

// Patch intent: some Il2Cpp native-to-managed trampoline failures can otherwise
// vanish into a silent crash or sparse log. TAF uses this hook to surface those
// boundary exceptions; VP keeps the same diagnostic behavior while we test
// fragile combat hooks.
[HarmonyPatch("Il2CppInterop.HarmonySupport.Il2CppDetourMethodPatcher", "ReportException")]
internal static class Il2CppInteropExceptionPatch
{
    private static bool Prefix(Exception ex)
    {
        Melon<UADVanillaPlusMod>.Logger.Error("UADVP Il2Cpp trampoline exception", ex);
        return false;
    }
}
