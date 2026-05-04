using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using UnityEngine;
using UADVanillaPlus.GameData;

[assembly: MelonGame("Game Labs", "Ultimate Admiral Dreadnoughts")]
[assembly: MelonInfo(typeof(UADVanillaPlus.UADVanillaPlusMod), UADVanillaPlus.ModInfo.ShortName, UADVanillaPlus.ModInfo.MelonVersion, "GG")]
[assembly: MelonColor(255, 80, 180, 255)]
[assembly: HarmonyDontPatchAll]

namespace UADVanillaPlus;

public sealed class UADVanillaPlusMod : MelonMod
{
    private bool patchingFailed;

    public override void OnInitializeMelon()
    {
        try
        {
            HarmonyInstance.PatchAll(MelonAssembly.Assembly);
        }
        catch (Exception ex)
        {
            patchingFailed = true;
            LoggerInstance.BigError($"UAD:VP Harmony patching failed. This usually means the game version no longer matches the mod patch signatures.\n{ex}");
        }

        LoggerInstance.Msg($"{ModInfo.DisplayText} initialized.");
        ModSettings.LogCurrentSettings("startup");
    }

    public override void OnLateInitializeMelon()
    {
        try
        {
            if (!HasUnityExplorer())
                Application.add_logMessageReceived(new Action<string, string, LogType>(ApplicationLogMessageReceived));
        }
        catch (Exception ex)
        {
            LoggerInstance.Warning($"UADVP compatibility logging setup failed. {ex.GetType().Name}: {ex.Message}");
        }

        if (patchingFailed)
            TryShowPatchFailureMessage();
    }

    public override void OnDeinitializeMelon()
    {
        try
        {
            Application.remove_logMessageReceived(new Action<string, string, LogType>(ApplicationLogMessageReceived));
        }
        catch
        {
            // Unity may already be tearing down; nothing useful to do here.
        }
    }

    private static bool HasUnityExplorer()
    {
        foreach (MelonBase melon in MelonBase.RegisteredMelons)
        {
            if (melon.Info.Name == "UnityExplorer")
                return true;
        }

        return false;
    }

    private void ApplicationLogMessageReceived(string condition, string stackTrace, LogType type)
    {
        string message = $"[Unity] {condition ?? string.Empty}";
        switch (type)
        {
            case LogType.Log:
                LoggerInstance.Msg(message);
                break;
            case LogType.Warning:
                if (!IsIgnoredUnityWarning(condition))
                    LoggerInstance.Warning(message);
                break;
            case LogType.Error:
            case LogType.Exception:
                LoggerInstance.Error($"{message}\n{stackTrace}");
                break;
        }
    }

    private static bool IsIgnoredUnityWarning(string? condition)
    {
        if (string.IsNullOrEmpty(condition))
            return false;

        // Keep TAF's useful battle-log hygiene: these vanilla warnings can
        // flood logs during combat and hide the real diagnostic signal.
        return condition.StartsWith("BoxColliders does not support negative scale or size.", StringComparison.Ordinal) ||
               condition.StartsWith("Parent of RectTransform is being set with parent property.", StringComparison.Ordinal) ||
               condition.StartsWith("self-col ", StringComparison.Ordinal) ||
               condition.StartsWith("unable to make non-interlapping shot ", StringComparison.Ordinal);
    }

    private void TryShowPatchFailureMessage()
    {
        try
        {
            MessageBoxUI.Show(
                "UAD:VP Version Mismatch",
                "UAD:VP failed to apply one or more Harmony patches. The installed game version may not match this mod build. Check MelonLoader/Latest.log for details.",
                null,
                false,
                null,
                null,
                new System.Action(() => { GameManager.Quit(); }));
        }
        catch (Exception ex)
        {
            LoggerInstance.Warning($"UADVP patch failure popup could not be shown. {ex.GetType().Name}: {ex.Message}");
        }
    }
}
