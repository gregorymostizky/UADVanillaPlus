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
    private const string ForcePeaceButtonName = "UADVP_ForcePeace";
    private static readonly Color DeclareWarColor = new(1f, 0.22f, 0.18f, 1f);
    private static readonly Color DeclareWarBlockedColor = new(0.7f, 0.35f, 0.35f, 1f);
    private static readonly Color ForcePeaceColor = new(0.2f, 0.95f, 0.38f, 1f);
    private static readonly Color ForcePeaceBlockedColor = new(0.35f, 0.7f, 0.42f, 1f);
    private static readonly Dictionary<IntPtr, Player> RowPlayers = new();
    private static readonly Dictionary<IntPtr, string> LastLoggedDeclareWarState = new();
    private static readonly Dictionary<IntPtr, string> LastLoggedForcePeaceState = new();

    [HarmonyPatch(nameof(CampaignPolitics_ElementUI.Init))]
    [HarmonyPostfix]
    private static void PostfixInit(CampaignPolitics_ElementUI __instance, Player p)
    {
        EnsureDeclareWarButton(__instance);
        EnsureForcePeaceButton(__instance);
        CacheRowPlayer(__instance, p);
        RefreshDiplomacyButtons(__instance);
    }

    [HarmonyPatch(nameof(CampaignPolitics_ElementUI.RefreshActions))]
    [HarmonyPostfix]
    private static void PostfixRefreshActions(CampaignPolitics_ElementUI __instance, Player p)
    {
        EnsureDeclareWarButton(__instance);
        EnsureForcePeaceButton(__instance);
        CacheRowPlayer(__instance, p);
        RefreshDiplomacyButtons(__instance);
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
            text.color = DeclareWarColor;
        }

        Melon<UADVanillaPlusMod>.Logger.Msg("UADVP diplomacy: added Declare War button to politics row.");
    }

    private static void EnsureForcePeaceButton(CampaignPolitics_ElementUI row)
    {
        if (row == null)
            return;

        Button? source = row.PeaceTreaty != null ? row.PeaceTreaty : row.IncreseTension;
        if (source == null)
            return;

        if (FindForcePeaceButton(row) != null)
            return;

        GameObject buttonObject = UnityEngine.Object.Instantiate(source.gameObject, source.transform.parent);
        buttonObject.name = ForcePeaceButtonName;

        try { buttonObject.transform.SetSiblingIndex(source.transform.GetSiblingIndex() + 1); }
        catch { }

        Button button = buttonObject.GetComponent<Button>();
        button.onClick.RemoveAllListeners();
        button.onClick.AddListener(new System.Action(() => ConfirmForcePeace(row)));

        TMP_Text? text = ButtonText(button);
        if (text != null)
        {
            text.text = "Force Peace";
            text.color = ForcePeaceColor;
        }

        Melon<UADVanillaPlusMod>.Logger.Msg("UADVP diplomacy: added Force Peace button to politics row.");
    }

    private static void RefreshDiplomacyButtons(CampaignPolitics_ElementUI row)
    {
        RefreshDeclareWarButton(row);
        RefreshForcePeaceButton(row);
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
        LogButtonState(LastLoggedDeclareWarState, row, "declare-war", mainPlayer, target, canDeclare, reason);

        TMP_Text? text = ButtonText(button);
        if (text != null)
        {
            text.text = "Declare War";
            text.color = canDeclare ? DeclareWarColor : DeclareWarBlockedColor;
        }
    }

    private static void RefreshForcePeaceButton(CampaignPolitics_ElementUI row)
    {
        Button? button = FindForcePeaceButton(row);
        if (button == null)
            return;

        Player? mainPlayer = ExtraGameData.MainPlayer();
        Player? target = PlayerForRow(row);
        bool canForcePeace = CampaignDiplomacyActions.CanForcePeace(mainPlayer, target, out string reason);

        button.interactable = target != null && mainPlayer != null && mainPlayer != target;
        LogButtonState(LastLoggedForcePeaceState, row, "force-peace", mainPlayer, target, canForcePeace, reason);

        TMP_Text? text = ButtonText(button);
        if (text != null)
        {
            text.text = "Force Peace";
            text.color = canForcePeace ? ForcePeaceColor : ForcePeaceBlockedColor;
        }
    }

    private static Button? FindDeclareWarButton(CampaignPolitics_ElementUI row)
    {
        if (row?.IncreseTension?.transform?.parent == null)
            return null;

        Transform existing = row.IncreseTension.transform.parent.Find(DeclareWarButtonName);
        return existing == null ? null : existing.GetComponent<Button>();
    }

    private static Button? FindForcePeaceButton(CampaignPolitics_ElementUI row)
    {
        Transform? parent = row?.PeaceTreaty?.transform?.parent ?? row?.IncreseTension?.transform?.parent;
        if (parent == null)
            return null;

        Transform existing = parent.Find(ForcePeaceButtonName);
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

    private static void LogButtonState(
        Dictionary<IntPtr, string> lastLoggedStates,
        CampaignPolitics_ElementUI row,
        string actionName,
        Player? mainPlayer,
        Player? target,
        bool canUse,
        string reason)
    {
        if (row == null || row.Pointer == IntPtr.Zero)
            return;

        string state = canUse
            ? $"enabled {mainPlayer?.Name(false) ?? "<none>"} -> {target?.Name(false) ?? "<none>"}"
            : $"blocked {mainPlayer?.Name(false) ?? "<none>"} -> {target?.Name(false) ?? "<none>"}: {reason}";

        if (lastLoggedStates.TryGetValue(row.Pointer, out string? last) && last == state)
            return;

        lastLoggedStates[row.Pointer] = state;
        Melon<UADVanillaPlusMod>.Logger.Msg($"UADVP diplomacy {actionName} button {state}");
    }

    private static void ConfirmDeclareWar(CampaignPolitics_ElementUI row)
    {
        Player? mainPlayer = ExtraGameData.MainPlayer();
        Player? target = PlayerForRow(row);
        if (!CampaignDiplomacyActions.CanTriggerWar(mainPlayer, target, out string reason))
        {
            MessageBoxUI.Show("Declare War", reason);
            RefreshDiplomacyButtons(row);
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
                RefreshDiplomacyButtons(row);
            }),
            new System.Action(() => RefreshDiplomacyButtons(row)));
    }

    private static void ConfirmForcePeace(CampaignPolitics_ElementUI row)
    {
        Player? mainPlayer = ExtraGameData.MainPlayer();
        Player? target = PlayerForRow(row);
        if (!CampaignDiplomacyActions.CanForcePeace(mainPlayer, target, out string reason))
        {
            MessageBoxUI.Show("Force Peace", reason);
            RefreshDiplomacyButtons(row);
            return;
        }

        string title = "Force Peace";
        string message = $"Force peace with {target!.Name(false)}? The treaty will be accepted automatically and the normal peace resolution will continue afterward.";
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
                CampaignDiplomacyActions.ForcePeace(mainPlayer, target);
                RefreshDiplomacyButtons(row);
            }),
            new System.Action(() => RefreshDiplomacyButtons(row)));
    }
}
