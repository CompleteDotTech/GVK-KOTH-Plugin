using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace GVK.Koth.Core
{
    /// <summary>Formats open-zone state for the client HUD protocol.</summary>
    public static class ClientStateProtocol
    {
        public static string BuildOpenZoneRecord(string name, double x, double y, double z, double radius,
            double fraction, int participants, long gridEntityId, IEnumerable<string> factionTags)
        {
            var culture = CultureInfo.InvariantCulture;
            return "O|" + (name ?? "").Replace('|', '/').Replace('\n', ' ').Replace('\r', ' ') + "|"
                + x.ToString("0.#", culture) + "|" + y.ToString("0.#", culture) + "|"
                + z.ToString("0.#", culture) + "|" + radius.ToString("0", culture) + "|"
                + fraction.ToString("0.000", culture) + "|" + participants.ToString(culture) + "|"
                + gridEntityId.ToString(culture) + "|" + FormatFactionTags(factionTags);
        }

        public static string FormatFactionTags(IEnumerable<string> factionTags)
        {
            if (factionTags == null)
                return "";

            var tags = new SortedSet<string>(StringComparer.Ordinal);
            foreach (string tag in factionTags)
            {
                if (string.IsNullOrWhiteSpace(tag))
                    continue;

                string cleaned = tag.Replace('|', '/').Replace('\r', ' ').Replace('\n', ' ')
                    .Replace("<", "").Replace(">", "").Trim();
                if (cleaned.Length != 0)
                    tags.Add(cleaned);
            }

            var result = new StringBuilder();
            foreach (string tag in tags)
            {
                if (result.Length != 0)
                    result.Append(' ');
                result.Append('[').Append(tag).Append(']');
            }
            return result.ToString();
        }
    }
}
