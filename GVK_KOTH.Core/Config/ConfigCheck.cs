using System;
using System.Collections.Generic;
using System.Linq;

namespace GVK.Koth.Core.Config
{
    /// <summary>
    /// Lists every problem in the config so !koth reload and startup can report them instead of skipping zones.
    /// </summary>
    public static class ConfigCheck
    {
        public static List<string> Run(KothConfig config)
        {
            var problems = new List<string>();
            if (config == null)
            {
                problems.Add("Config is missing.");
                return problems;
            }

            if (config.UpdateIntervalTicks < 1)
                problems.Add($"UpdateIntervalTicks is {config.UpdateIntervalTicks}; it must be 1 or more.");

            if (config.Zones == null || config.Zones.Count == 0)
                problems.Add("No zones are configured, so KOTH does nothing.");

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var zone in config.Zones ?? new List<ZoneProfile>())
            {
                string label = string.IsNullOrWhiteSpace(zone.Name) ? "(zone with no name)" : $"Zone '{zone.Name}'";

                if (string.IsNullOrWhiteSpace(zone.Name))
                    problems.Add($"{label}: needs a Name.");
                else if (!names.Add(zone.Name))
                    problems.Add($"{label}: the name is used by more than one zone.");

                CheckFinite(problems, label, nameof(zone.X), zone.X);
                CheckFinite(problems, label, nameof(zone.Y), zone.Y);
                CheckFinite(problems, label, nameof(zone.Z), zone.Z);
                CheckFinite(problems, label, nameof(zone.Radius), zone.Radius);
                CheckFinite(problems, label, nameof(zone.CaptureSeconds), zone.CaptureSeconds);
                CheckFinite(problems, label, nameof(zone.MinPresenceShare), zone.MinPresenceShare);
                CheckFinite(problems, label, nameof(zone.NoThrusterRange), zone.NoThrusterRange);
                CheckFinite(problems, label, nameof(zone.NoLargeGridPowerRange), zone.NoLargeGridPowerRange);
                CheckFinite(problems, label, nameof(zone.NoSafeZoneRange), zone.NoSafeZoneRange);
                CheckFinite(problems, label, nameof(zone.NoProjectorRange), zone.NoProjectorRange);
                CheckFinite(problems, label, nameof(zone.LinkRange), zone.LinkRange);

                if (zone.PointsPerCompletion > 0 && zone.CreditsPerPoint >= 0 &&
                    zone.CreditsPerPoint > long.MaxValue / zone.PointsPerCompletion)
                    problems.Add($"{label}: PointsPerCompletion multiplied by CreditsPerPoint exceeds the supported credits range.");

                if (zone.IdleDrainPlayers < 0)
                    problems.Add($"{label}: IdleDrainPlayers can't be negative.");

                if (zone.Mode != ZoneMode.RulesOnly && zone.Radius <= 0)
                    problems.Add($"{label}: Radius must be above 0.");

                if (zone.Attach == ZoneAttach.Coordinates && zone.X == 0 && zone.Y == 0 && zone.Z == 0)
                    problems.Add($"{label}: Attach is Coordinates but X, Y and Z are all 0.");

                if (!string.IsNullOrWhiteSpace(zone.SpawnGroup) && zone.X == 0 && zone.Y == 0 && zone.Z == 0)
                    problems.Add($"{label}: SpawnGroup is set, so X, Y and Z must be the spawn point, but they are all 0.");

                if (!string.IsNullOrWhiteSpace(zone.SpawnGroup) && zone.Attach != ZoneAttach.NpcTag)
                    problems.Add($"{label}: SpawnGroup only makes sense with Attach NpcTag (the spawned grid carries the zone).");

                if (zone.Attach == ZoneAttach.Coordinates && zone.Activation == ZoneActivation.Lifetime)
                    problems.Add($"{label}: Activation Lifetime only works with Attach NpcTag.");

                if (zone.Mode == ZoneMode.Capture)
                {
                    if (!zone.OnFoot && !zone.SmallGrids && !zone.LargeGrids)
                        problems.Add($"{label}: OnFoot, SmallGrids and LargeGrids are all off, so nobody can capture.");
                    if (zone.MinPlayers < 1)
                        problems.Add($"{label}: MinPlayers must be 1 or more.");
                }

                if (zone.Mode != ZoneMode.RulesOnly)
                {
                    if (zone.CaptureSeconds <= 0)
                        problems.Add($"{label}: CaptureSeconds must be above 0.");
                    if (zone.MinPresenceShare < 0 || zone.MinPresenceShare > 1)
                        problems.Add($"{label}: MinPresenceShare must be between 0 and 1.");
                    if (zone.PointsPerCompletion < 0 || zone.CreditsPerPoint < 0 || zone.PointsPerLoot < 0)
                        problems.Add($"{label}: points, credits and PointsPerLoot can't be negative.");

                    if (zone.PointsPerLoot > 0)
                    {
                        if (zone.Loot == null || zone.Loot.Count == 0)
                            problems.Add($"{label}: PointsPerLoot is set but Loot is empty.");
                        if (string.IsNullOrWhiteSpace(zone.LootContainerName))
                            problems.Add($"{label}: PointsPerLoot is set but LootContainerName is empty.");
                    }

                    foreach (var item in zone.Loot ?? new List<LootItem>())
                    {
                        if (string.IsNullOrWhiteSpace(item.TypeId) || string.IsNullOrWhiteSpace(item.SubtypeId))
                            problems.Add($"{label}: a Loot item needs TypeId and SubtypeId.");
                        if (item.Amount < 1)
                            problems.Add($"{label}: Loot item {item.TypeId}/{item.SubtypeId} has Amount {item.Amount}.");
                    }
                }

                if (zone.NoThrusterRange < 0 || zone.NoLargeGridPowerRange < 0 || zone.NoSafeZoneRange < 0 || zone.NoProjectorRange < 0 || zone.LinkRange < 0)
                    problems.Add($"{label}: rule and link ranges can't be negative.");
            }

            var slotNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var slot in config.Slots ?? new List<SlotConfig>())
            {
                string label = string.IsNullOrWhiteSpace(slot.Name) ? "(slot with no name)" : $"Slot '{slot.Name}'";

                if (string.IsNullOrWhiteSpace(slot.Name))
                    problems.Add($"{label}: needs a Name.");
                else if (!slotNames.Add(slot.Name))
                    problems.Add($"{label}: the name is used by more than one slot.");

                if (slot.DurationMinutes <= 0)
                    problems.Add($"{label}: DurationMinutes must be above 0.");
                if (slot.Start == null || slot.Start.Hour < 0 || slot.Start.Hour > 23 || slot.Start.Minute < 0 || slot.Start.Minute > 59)
                    problems.Add($"{label}: Start time is not a valid hour and minute.");

                if (slot.Pool == null || slot.Pool.Count == 0)
                {
                    problems.Add($"{label}: Pool is empty.");
                    continue;
                }

                foreach (string zoneName in slot.Pool)
                {
                    var zone = config.Zones?.FirstOrDefault(z => string.Equals(z.Name, zoneName, StringComparison.OrdinalIgnoreCase));
                    if (zone == null)
                        problems.Add($"{label}: Pool names zone '{zoneName}', which doesn't exist.");
                    else if (zone.Activation != ZoneActivation.Slot)
                        problems.Add($"{label}: zone '{zoneName}' is in the Pool but its Activation is {zone.Activation}, not Slot.");
                }
            }

            foreach (var zone in config.Zones ?? new List<ZoneProfile>())
            {
                bool inPool = config.Slots?.Any(s => s.Pool?.Any(p => string.Equals(p, zone.Name, StringComparison.OrdinalIgnoreCase)) == true) == true;
                if (zone.Activation == ZoneActivation.Slot && zone.Mode != ZoneMode.RulesOnly && !inPool)
                    problems.Add($"Zone '{zone.Name}': Activation is Slot but no slot's Pool includes it, so it never runs.");
            }

            return problems;
        }

        private static void CheckFinite(List<string> problems, string label, string name, double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                problems.Add($"{label}: {name} must be finite.");
        }
    }
}
