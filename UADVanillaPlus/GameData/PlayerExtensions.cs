using Il2Cpp;

namespace UADVanillaPlus.GameData;

internal static class PlayerExtensions
{
    internal static IEnumerable<Ship> GetFleetAll(this Player player)
    {
        // Designs can be owned by inactive or foreign players. Read from the
        // campaign vessel index so the design viewer counts real ships for any nation.
        if (player == null ||
            CampaignController.Instance?.CampaignData?.VesselsByPlayer == null ||
            !CampaignController.Instance.CampaignData.VesselsByPlayer.TryGetValue(player.data, out var vessels))
        {
            yield break;
        }

        foreach (Ship ship in vessels)
        {
            if (ship != null)
                yield return ship;
        }
    }
}
