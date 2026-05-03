using HarmonyLib;
using Il2Cpp;
using MelonLoader;
using UADVanillaPlus.GameData;

namespace UADVanillaPlus.Harmony;

// Patch intent: vanilla applies one global over-capacity time penalty to every
// shipyard job. VP instead lets the monthly advance run only the work that fits
// current capacity, preferring near-finished repairs before builds/refits, and
// temporarily pauses the overflow jobs so vanilla's own filters skip progress.
[HarmonyPatch(typeof(CampaignController))]
internal static class CampaignShipyardCapacityBalancePatch
{
    private enum WorkKind
    {
        Repair = 0,
        Build = 1,
        Refit = 2,
    }

    private sealed class WorkItem
    {
        internal Ship Ship = null!;
        internal WorkKind Kind;
        internal float Tonnage;
        internal float MonthsRemaining;
    }

    private sealed class AdvancePlan
    {
        internal readonly List<PausedState> PausedShips = new();
    }

    private readonly struct PausedState
    {
        internal readonly Ship Ship;
        internal readonly bool IsBuildingPaused;
        internal readonly bool IsRepairingPaused;
        internal readonly bool IsRefitPaused;

        internal PausedState(Ship ship)
        {
            Ship = ship;
            IsBuildingPaused = ship.isBuildingPaused;
            IsRepairingPaused = ship.isRepairingPaused;
            IsRefitPaused = ship.isRefitPaused;
        }
    }

    private readonly record struct WorkSummary(int Count, int Repairs, int Builds, int Refits, float Tonnage)
    {
        internal string LogText => $"{Count} ships/{Tonnage:0}t (repair={Repairs}, build={Builds}, refit={Refits})";
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(CampaignController.AdvanceShips))]
    private static void AdvanceShipsPrefix(Player player, bool prewarm, out AdvancePlan? __state)
    {
        __state = BeginAdvance(player, prewarm);
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(CampaignController.AdvanceShips))]
    private static void AdvanceShipsPostfix(AdvancePlan? __state)
    {
        EndAdvance(__state);
    }

    private static AdvancePlan? BeginAdvance(Player player, bool prewarm)
    {
        if (!ModSettings.ShipyardCapacityBalanced || prewarm || player == null)
            return null;

        try
        {
            return CreateAdvancePlan(player);
        }
        catch (Exception ex)
        {
            Melon<UADVanillaPlusMod>.Logger.Warning(
                $"UADVP shipyard capacity balance failed before monthly ship advance; keeping vanilla behavior. {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    private static AdvancePlan? CreateAdvancePlan(Player player)
    {
        List<WorkItem> work = BuildWorkList(player);
        if (work.Count == 0)
            return null;

        float capacity = SafeCapacity(player);
        if (capacity <= 0f)
            return null;

        float activeTonnage = work.Sum(static item => item.Tonnage);
        if (activeTonnage <= capacity + 0.5f)
            return null;

        HashSet<string> allowed = new(StringComparer.Ordinal);
        float remainingCapacity = capacity;

        foreach (WorkItem item in work
                     .OrderBy(static item => item.Kind == WorkKind.Repair ? 0 : 1)
                     .ThenBy(static item => item.MonthsRemaining)
                     .ThenByDescending(static item => item.Tonnage))
        {
            if (item.Tonnage > remainingCapacity + 0.5f)
                continue;

            allowed.Add(ShipKey(item.Ship));
            remainingCapacity -= item.Tonnage;
        }

        AdvancePlan plan = new();
        List<WorkItem> delayed = new();
        List<WorkItem> running = new();
        foreach (WorkItem item in work)
        {
            if (allowed.Contains(ShipKey(item.Ship)))
            {
                running.Add(item);
                continue;
            }

            delayed.Add(item);
            plan.PausedShips.Add(new PausedState(item.Ship));
            PauseWorkItem(item);
        }

        if (plan.PausedShips.Count == 0)
            return null;

        if (!player.isAi)
        {
            WorkSummary runningSummary = Summarize(running);
            WorkSummary delayedSummary = Summarize(delayed);
            Melon<UADVanillaPlusMod>.Logger.Msg(
                $"UADVP shipyard capacity balance: {player.Name(false)}, capacity={capacity:0}t, queued={work.Count} ships/{activeTonnage:0}t, running={runningSummary.LogText}, delayed={delayedSummary.LogText}.");
        }

        return plan;
    }

    private static void EndAdvance(AdvancePlan? plan)
    {
        if (plan == null)
            return;

        foreach (PausedState state in plan.PausedShips)
        {
            if (state.Ship == null)
                continue;

            state.Ship.isBuildingPaused = state.IsBuildingPaused;
            state.Ship.isRepairingPaused = state.IsRepairingPaused;
            state.Ship.isRefitPaused = state.IsRefitPaused;
        }
    }

    private static List<WorkItem> BuildWorkList(Player player)
    {
        List<WorkItem> work = new();
        foreach (Ship ship in player.GetFleetAll())
        {
            if (ship == null || ship.isDesign || ship.isSunk || ship.isScrapped || ship.isErased)
                continue;

            WorkItem? item = TryCreateWorkItem(ship);
            if (item != null)
                work.Add(item);
        }

        return work;
    }

    private static WorkItem? TryCreateWorkItem(Ship ship)
    {
        try
        {
            if (ship.isRepairing && !ship.isRepairingPaused)
            {
                return new WorkItem
                {
                    Ship = ship,
                    Kind = WorkKind.Repair,
                    Tonnage = SafeTonnage(ship),
                    MonthsRemaining = RemainingMonths(ship.repairingProgress, ship.RepairingTime(false)),
                };
            }

            if (ship.isBuilding && !ship.isBuildingPaused)
            {
                return new WorkItem
                {
                    Ship = ship,
                    Kind = WorkKind.Build,
                    Tonnage = SafeTonnage(ship),
                    MonthsRemaining = RemainingMonths(ship.buildingProgress, ship.BuildingTime(false)),
                };
            }

            if (ship.isRefit && !ship.isRefitPaused)
            {
                return new WorkItem
                {
                    Ship = ship,
                    Kind = WorkKind.Refit,
                    Tonnage = SafeTonnage(ship),
                    MonthsRemaining = RemainingMonths(ship.refitProgress, ship.DesignRefitTime(false)),
                };
            }
        }
        catch (Exception ex)
        {
            Melon<UADVanillaPlusMod>.Logger.Warning($"UADVP shipyard capacity balance skipped one work item. {ex.GetType().Name}: {ex.Message}");
        }

        return null;
    }

    private static void PauseWorkItem(WorkItem item)
    {
        switch (item.Kind)
        {
            case WorkKind.Repair:
                item.Ship.isRepairingPaused = true;
                break;
            case WorkKind.Refit:
                item.Ship.isRefitPaused = true;
                break;
            default:
                item.Ship.isBuildingPaused = true;
                break;
        }
    }

    private static WorkSummary Summarize(IEnumerable<WorkItem> items)
    {
        int count = 0;
        int repairs = 0;
        int builds = 0;
        int refits = 0;
        float tonnage = 0f;

        foreach (WorkItem item in items)
        {
            count++;
            tonnage += item.Tonnage;

            switch (item.Kind)
            {
                case WorkKind.Repair:
                    repairs++;
                    break;
                case WorkKind.Refit:
                    refits++;
                    break;
                default:
                    builds++;
                    break;
            }
        }

        return new WorkSummary(count, repairs, builds, refits, tonnage);
    }

    private static float RemainingMonths(float progress, float totalMonths)
    {
        if (totalMonths <= 0f)
            return float.MaxValue;

        float remainingProgress = Math.Max(0f, 100f - progress);
        return remainingProgress * totalMonths / 100f;
    }

    private static float SafeCapacity(Player player)
    {
        try { return player.ShipbuildingCapacityLimit(); }
        catch { return 0f; }
    }

    private static float SafeTonnage(Ship ship)
    {
        try { return Math.Max(0f, ship.Tonnage()); }
        catch { return 0f; }
    }

    private static string ShipKey(Ship ship)
    {
        try { return ship.id.ToString(); }
        catch { return ship.GetHashCode().ToString(); }
    }
}

[HarmonyPatch(typeof(Player))]
internal static class CampaignShipyardCapacityTimePenaltyPatch
{
    [HarmonyPrefix]
    [HarmonyPatch(nameof(Player.TimePenalty))]
    private static bool TimePenaltyPrefix(ref float __result)
    {
        if (!ModSettings.ShipyardCapacityBalanced)
            return true;

        __result = 1f;
        return false;
    }
}
