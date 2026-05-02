using Il2Cpp;

namespace UADVanillaPlus.GameData;

internal static class ExtraGameData
{
    internal static Player? MainPlayer()
    {
        // UAD exposes the campaign player list but not a convenient stable
        // "current human" accessor in the places VP patches need it.
        if (CampaignController.Instance?.CampaignData?.Players == null)
            return null;

        foreach (Player player in CampaignController.Instance.CampaignData.Players)
        {
            if (player != null && player.isMain)
                return player;
        }

        return null;
    }
}
