using System.Globalization;
using System.Linq;
using System.Text;
using Sandbox.ModAPI;
using Torch.Commands;
using Torch.Commands.Permissions;
using VRage.Game.ModAPI;

namespace GVK.Koth.Commands
{
    [Category("koth")]
    public class KothAdminCommands : CommandModule
    {
        private static Plugin Plugin => Plugin.Instance;

        [Command("reload", "Reloads the GVK KOTH config and lists any problems in it.")]
        [Permission(MyPromoteLevel.Admin)]
        public void Reload()
        {
            if (Plugin == null)
            {
                Context.Respond("GVK KOTH is not loaded.");
                return;
            }

            Plugin.LoadConfig();
            var problems = Plugin.ConfigProblems;
            if (Plugin.LastConfigLoadAccepted && problems.Count == 0)
            {
                Context.Respond($"GVK KOTH config reloaded: {Plugin.Config.Zones.Count} zones, {Plugin.Config.Slots.Count} slots, no problems.");
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine(Plugin.LastConfigLoadAccepted
                ? $"GVK KOTH config reloaded with {problems.Count} problem(s):"
                : $"GVK KOTH config rejected; previous config retained. {problems.Count} problem(s):");
            foreach (string problem in problems)
                sb.AppendLine("- " + problem);
            Context.Respond(sb.ToString());
        }

        [Command("status", "Shows every GVK KOTH zone: open or not, players counted, bar, queued loot.")]
        [Permission(MyPromoteLevel.Admin)]
        public void Status()
        {
            if (Plugin?.Config == null)
            {
                Context.Respond("GVK KOTH is not loaded.");
                return;
            }

            var cfg = Plugin.Config;
            var zones = Plugin.Zones;
            var sb = new StringBuilder();
            sb.AppendLine($"GVK KOTH: {(cfg.Enabled ? "enabled" : "DISABLED")}, {(zones != null ? "running" : "session not loaded")}, config problems: {Plugin.ConfigProblems.Count}");
            if (zones != null)
            {
                foreach (var zone in zones.Zones)
                {
                    string force = zones.ForceState(zone.Name);
                    sb.AppendLine($"- {zone.Name} ({zone.Profile.Mode}, {zone.Profile.Activation}{(zone.Profile.Enabled ? "" : ", disabled")}{(force == "" ? "" : ", " + force)}): "
                        + $"{(zone.Active ? "OPEN" : "closed")}, counted {zone.LastParticipantCount}, "
                        + $"bar {zone.Capture.Fraction:P0}, loot counter {zone.Capture.LootCounter}, queued drops {zone.Save.PendingLootDrops}");
                    if (zone.Save.LootReconciliation != null)
                        AppendReconciliation(sb, zone.Save.LootReconciliation);
                }
            }

            var savedZones = Plugin.Data?.State?.Zones;
            if (savedZones != null)
            {
                var shownSaves = new System.Collections.Generic.List<GVK.Koth.Core.ZoneSave>();
                foreach (var save in savedZones.OrderByDescending(z => z?.LootReconciliation != null))
                {
                    if (save == null || (save.LootReconciliation == null && save.PendingLootDrops <= 0))
                        continue;
                    bool shown = (zones != null && zones.Zones.Any(zone =>
                            (save.LootReconciliation == null || zone.Save.LootReconciliation != null)
                            && SameSave(zone.Save, save)))
                        || shownSaves.Any(previous => SameSave(previous, save));
                    if (shown)
                        continue;
                    shownSaves.Add(save);
                    sb.AppendLine($"- saved zone {save.Name} (SaveKey/name), queued drops {save.PendingLootDrops.ToString(CultureInfo.InvariantCulture)}");
                    AppendSavedPosition(sb, save.LootPosition);
                    if (save.LootReconciliation != null)
                        AppendReconciliation(sb, save.LootReconciliation);
                    else
                        sb.AppendLine("  OWED: pending delivery.");
                }
            }
            Context.Respond(sb.ToString());
        }

        private static bool SameSave(GVK.Koth.Core.ZoneSave left, GVK.Koth.Core.ZoneSave right)
        {
            return object.ReferenceEquals(left, right)
                || (!string.IsNullOrEmpty(right.Name) && string.Equals(left.Name, right.Name, System.StringComparison.OrdinalIgnoreCase));
        }

        private static void AppendSavedPosition(StringBuilder sb, GVK.Koth.Core.LootPositionSave position)
        {
            if (position == null || double.IsNaN(position.X) || double.IsInfinity(position.X)
                || double.IsNaN(position.Y) || double.IsInfinity(position.Y)
                || double.IsNaN(position.Z) || double.IsInfinity(position.Z))
            {
                sb.AppendLine("  Saved location unknown; delivery blocked pending operator assessment.");
                return;
            }
            sb.AppendLine($"  Saved location: {position.X.ToString("R", CultureInfo.InvariantCulture)}, {position.Y.ToString("R", CultureInfo.InvariantCulture)}, {position.Z.ToString("R", CultureInfo.InvariantCulture)}");
        }

        private static void AppendReconciliation(StringBuilder sb, GVK.Koth.Core.LootReconciliationSave reconciliation)
        {
            sb.AppendLine($"  SUSPENDED: loot reconciliation required; attempt {reconciliation.AttemptId}, container {reconciliation.ContainerId.ToString(CultureInfo.InvariantCulture)}, reason: {reconciliation.Reason}");
            if (reconciliation.Items == null || reconciliation.Items.Count == 0)
                sb.AppendLine("  Reconciliation items unavailable.");
            else
            {
                foreach (var item in reconciliation.Items)
                {
                    if (item == null)
                    {
                        sb.AppendLine("  Reconciliation item unavailable.");
                        continue;
                    }
                    string observed = item.ObservedKnown ? FormatRaw(item.ObservedRaw) : "unknown";
                    sb.AppendLine($"  {item.TypeId}/{item.SubtypeId}: requested {FormatRaw(item.RequestedRaw)}, before {FormatRaw(item.BeforeRaw)}, observed {observed}");
                }
            }
        }

        private static string FormatRaw(long raw)
        {
            return (raw / 1000000m).ToString("0.######", CultureInfo.InvariantCulture);
        }

        [Command("start", "Forces a zone open until !koth auto <zone>. Usage: !koth start <zone>")]
        [Permission(MyPromoteLevel.Admin)]
        public void Start(string zoneName)
        {
            if (!TryZone(zoneName, out string name))
                return;
            Plugin.Zones.ForceOn(name);
            Context.Respond($"{name} forced open. !koth auto {name} returns it to its schedule.");
        }

        [Command("stop", "Forces a zone closed until !koth auto <zone>. Usage: !koth stop <zone>")]
        [Permission(MyPromoteLevel.Admin)]
        public void Stop(string zoneName)
        {
            if (!TryZone(zoneName, out string name))
                return;
            Plugin.Zones.ForceOff(name);
            Context.Respond($"{name} forced closed. !koth auto {name} returns it to its schedule.");
        }

        [Command("auto", "Returns a zone to its normal schedule. Usage: !koth auto <zone>")]
        [Permission(MyPromoteLevel.Admin)]
        public void Auto(string zoneName)
        {
            if (!TryZone(zoneName, out string name))
                return;
            Plugin.Zones.ClearForce(name);
            Context.Respond($"{name} is back on its schedule.");
        }

        [Command("points", "Adjusts a faction's season points. Usage: !koth points <faction tag> <+/-amount> <reason>")]
        [Permission(MyPromoteLevel.Admin)]
        public void Points(string factionTag, string amount, string reason = "")
        {
            if (Plugin?.Data == null)
            {
                Context.Respond("GVK KOTH is not running.");
                return;
            }

            if (!int.TryParse(amount, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int change) || change == 0)
            {
                Context.Respond($"'{amount}' isn't a whole number like 5 or -5.");
                return;
            }

            var faction = MyAPIGateway.Session.Factions.TryGetFactionByTag(factionTag);
            if (faction == null)
            {
                Context.Respond($"No faction with tag '{factionTag}'.");
                return;
            }

            // Torch splits on spaces; join any extra words back into the reason.
            string fullReason = string.Join(" ", new[] { reason }.Concat(Context.Args.Skip(3)).Where(s => !string.IsNullOrWhiteSpace(s)));
            if (string.IsNullOrWhiteSpace(fullReason))
                fullReason = "Admin adjustment";

            string admin = Context.Player?.DisplayName ?? "Server";
            var entry = Plugin.Data.AddPoints(faction.FactionId, faction.Tag, faction.Name, change, "Admin",
                $"{fullReason} (by {admin})");
            Plugin.Announce.Both($"[{faction.Tag}] {(entry.Change >= 0 ? "gained" : "lost")} {System.Math.Abs(entry.Change)} KOTH points: {fullReason}. Total {entry.NewTotal}.");
            Context.Respond($"[{faction.Tag}] now has {entry.NewTotal} points.");
        }

        private bool TryZone(string zoneName, out string name)
        {
            name = null;
            var profile = Plugin?.Config?.Zones.FirstOrDefault(z => string.Equals(z.Name, zoneName, System.StringComparison.OrdinalIgnoreCase));
            if (profile == null || Plugin.Zones == null)
            {
                var known = Plugin?.Config?.Zones.Select(z => z.Name) ?? Enumerable.Empty<string>();
                Context.Respond($"No zone '{zoneName}'. Zones: {string.Join(", ", known)}");
                return false;
            }
            name = profile.Name;
            return true;
        }
    }
}
