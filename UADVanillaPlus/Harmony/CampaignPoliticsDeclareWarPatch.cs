using HarmonyLib;
using Il2Cpp;
using Il2CppTMPro;
using MelonLoader;
using UADVanillaPlus.GameData;
using UnityEngine;
using UnityEngine.UI;

namespace UADVanillaPlus.Harmony;

[HarmonyPatch(typeof(CampaignPolitics_ElementUI))]
internal static class CampaignPoliticsDeclareWarPatch
{
    private const string DeclareWarButtonName = "UADVP_DeclareWar";
    private static readonly Dictionary<IntPtr, Player> RowPlayers = new();
    private static readonly Dictionary<IntPtr, string> LastLoggedState = new();

    [HarmonyPatch(nameof(CampaignPolitics_ElementUI.Init))]
    [HarmonyPostfix]
    private static void PostfixInit(CampaignPolitics_ElementUI __instance, Player p)
    {
        EnsureDeclareWarButton(__instance);
        CacheRowPlayer(__instance, p);
        RefreshDeclareWarButton(__instance);
    }

    [HarmonyPatch(nameof(CampaignPolitics_ElementUI.RefreshActions))]
    [HarmonyPostfix]
    private static void PostfixRefreshActions(CampaignPolitics_ElementUI __instance, Player p)
    {
        EnsureDeclareWarButton(__instance);
        CacheRowPlayer(__instance, p);
        RefreshDeclareWarButton(__instance);
    }

    private static void EnsureDeclareWarButton(CampaignPolitics_ElementUI row)
    {
        if (row == null || row.IncreseTension == null)
            return;

        if (FindDeclareWarButton(row) != null)
            return;

        GameObject buttonObject = UnityEngine.Object.Instantiate(row.IncreseTension.gameObject, row.IncreseTension.transform.parent);
        buttonObject.name = DeclareWarButtonName;

        try { buttonObject.transform.SetSiblingIndex(row.IncreseTension.transform.GetSiblingIndex() + 1); }
        catch { }

        Button button = buttonObject.GetComponent<Button>();
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(new System.Action(() => ConfirmDeclareWar(row)));

        TMP_Text? text = ButtonText(button);
        if (text != null)
        {
            text.text = "Declare War";
            text.color = new Color(1f, 0.22f, 0.18f, 1f);
        }

        Melon<UADVanillaPlusMod>.Logger.Msg("UADVP diplomacy: added Declare War button to politics row.");
    }

    private static void RefreshDeclareWarButton(CampaignPolitics_ElementUI row)
    {
        Button? button = FindDeclareWarButton(row);
        if (button == null)
            return;

        Player? mainPlayer = ExtraGameData.MainPlayer();
        Player? target = PlayerForRow(row);
        bool canDeclare = CampaignDiplomacyActions.CanTriggerWar(mainPlayer, target, out string reason);

        // Keep the button clickable when the row target is valid so the popup
        // can explain blocked states instead of making the UI look broken.
        button.interactable = target != null && mainPlayer != null && mainPlayer != target;
        LogDeclareWarState(row, mainPlayer, target, canDeclare, reason);

        TMP_Text? text = ButtonText(button);
        if (text != null)
        {
            text.text = "Declare War";
            text.color = canDeclare ? new Color(1f, 0.22f, 0.18f, 1f) : new Color(0.7f, 0.35f, 0.35f, 1f);
        }
    }

    private static Button? FindDeclareWarButton(CampaignPolitics_ElementUI row)
    {
        if (row?.IncreseTension?.transform?.parent == null)
            return null;

        Transform existing = row.IncreseTension.transform.parent.Find(DeclareWarButtonName);
        return existing == null ? null : existing.GetComponent<Button>();
    }

    private static TMP_Text? ButtonText(Button button)
    {
        if (button == null)
            return null;

        return button.GetComponentInChildren<TMP_Text>();
    }

    private static Player? PlayerForRow(CampaignPolitics_ElementUI row)
    {
        if (row != null && RowPlayers.TryGetValue(row.Pointer, out Player? cached))
            return cached;

        return null;
    }

    private static void CacheRowPlayer(CampaignPolitics_ElementUI row, Player player)
    {
        if (row == null || row.Pointer == IntPtr.Zero || player == null)
            return;

        RowPlayers[row.Pointer] = player;
    }

    private static void LogDeclareWarState(CampaignPolitics_ElementUI row, Player? mainPlayer, Player? target, bool canDeclare, string reason)
    {
        if (row == null || row.Pointer == IntPtr.Zero)
            return;

        string state = canDeclare
            ? $"enabled {mainPlayer?.Name(false) ?? "<none>"} -> {target?.Name(false) ?? "<none>"}"
            : $"blocked {mainPlayer?.Name(false) ?? "<none>"} -> {target?.Name(false) ?? "<none>"}: {reason}";

        if (LastLoggedState.TryGetValue(row.Pointer, out string? last) && last == state)
            return;

        LastLoggedState[row.Pointer] = state;
        Melon<UADVanillaPlusMod>.Logger.Msg($"UADVP diplomacy declare-war button {state}");
    }

    private static void ConfirmDeclareWar(CampaignPolitics_ElementUI row)
    {
        Player? mainPlayer = ExtraGameData.MainPlayer();
        Player? target = PlayerForRow(row);
        if (!CampaignDiplomacyActions.CanTriggerWar(mainPlayer, target, out string reason))
        {
            MessageBoxUI.Show("Declare War", reason);
            RefreshDeclareWarButton(row);
            return;
        }

        string title = "Declare War";
        string message = string.Format(LocalizeManager.Localize("$Ui_World_DoYouWantToDeclareWarAgainst0"), target!.Name(false));
        string yes = LocalizeManager.Localize("$Ui_Popup_Generic_Yes");
        string no = LocalizeManager.Localize("$Ui_Popup_Generic_No");

        MessageBoxUI.Show(
            title,
            message,
            null,
            true,
            yes,
            no,
            new System.Action(() =>
            {
                CampaignDiplomacyActions.TriggerWar(mainPlayer, target);
                RefreshDeclareWarButton(row);
            }),
            new System.Action(() => RefreshDeclareWarButton(row)));
    }
}
