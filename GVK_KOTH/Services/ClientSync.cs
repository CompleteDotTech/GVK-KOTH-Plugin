using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using GVK.Koth.Core;
using GVK.Koth.Core.Config;
using GVK.Koth.Zones;
using Sandbox.ModAPI;
using VRageMath;

namespace GVK.Koth.Services
{
    /// <summary>
    /// Sends zone state to players for the GVK_Settings KOTH HUD and /gvk map icons: open zones and the next
    /// slot's zone, once a second while any zone is open (every 10 seconds otherwise), plus capture events for
    /// fireworks. Plain text, one line per item; see docs/CLIENT_PROTOCOL.md. Game thread only.
    /// </summary>
    public sealed class ClientSync
    {
        /// <summary>Network channel shared with the GVK_Settings client script.</summary>
        public const ushort Channel = 47410;

        private const double OpenIntervalSeconds = 1;
        private const double IdleIntervalSeconds = 10;

        private readonly List<string> _events = new List<string>();
        private double _sinceSend = double.MaxValue;

        /// <summary>Queues a "bar completed" event (fireworks) for the next send.</summary>
        public void Completed(ZoneRuntime zone, IEnumerable<string> factionTags)
        {
            _events.Add("C|" + Clean(zone.Name) + "|" + Position(zone.Center) + "|" + Clean(string.Join(" ", factionTags)));
        }

        public void Tick(ZoneManager zones, double dt, DateTime now)
        {
            _sinceSend += dt;
            var open = zones.Zones.Where(z => z.Active && z.Profile.Mode != ZoneMode.RulesOnly).ToList();
            double interval = open.Count > 0 ? OpenIntervalSeconds : IdleIntervalSeconds;
            if (_events.Count == 0 && _sinceSend < interval)
                return;
            _sinceSend = 0;

            var c = CultureInfo.InvariantCulture;
            var sb = new StringBuilder("GVKKOTH|1\n");
            foreach (var zone in open)
            {
                sb.Append(ClientStateProtocol.BuildOpenZoneRecord(
                    zone.Name, zone.Center.X, zone.Center.Y, zone.Center.Z, zone.Profile.Radius,
                    zone.Capture.Fraction, zone.LastParticipantCount, zone.Grid?.EntityId ?? 0L,
                    zone.LastFactionTags)).Append('\n');
            }

            var next = zones.NextSlotZone(now);
            if (next.HasValue && open.All(z => !string.Equals(z.Name, next.Value.Profile.Name, StringComparison.OrdinalIgnoreCase)))
            {
                var profile = next.Value.Profile;
                sb.Append("N|").Append(Clean(profile.Name)).Append('|').Append(Position(zones.ProfilePosition(profile))).Append('|')
                  .Append(profile.Radius.ToString("0", c)).Append('|')
                  .Append(next.Value.StartUtc.Ticks.ToString(c)).Append('\n');
            }

            foreach (string e in _events)
                sb.Append(e).Append('\n');
            _events.Clear();

            try
            {
                MyAPIGateway.Multiplayer.SendMessageToOthers(Channel, Encoding.UTF8.GetBytes(sb.ToString()), true);
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn(ex, "GVK KOTH: failed to send zone state to players.");
            }
        }

        private static string Position(Vector3D p)
        {
            var c = CultureInfo.InvariantCulture;
            return p.X.ToString("0.#", c) + "|" + p.Y.ToString("0.#", c) + "|" + p.Z.ToString("0.#", c);
        }

        private static string Clean(string text) => (text ?? "").Replace('|', '/').Replace('\n', ' ').Replace('\r', ' ');
    }
}
