using System.Collections.Generic;

namespace GVK.Koth.Core.Config
{
    /// <summary>
    /// Plugin settings, read from GVK_KOTH.cfg (XML). Edit the file and run !koth reload; the plugin never writes
    /// over it. Lists start empty because XmlSerializer adds file items to a list that already has items.
    /// </summary>
    public class KothConfig
    {
        public bool Enabled { get; set; } = true;

        /// <summary>Game ticks between zone checks (60 = about once a second).</summary>
        public int UpdateIntervalTicks { get; set; } = 60;

        public string DiscordWebhookUrl { get; set; } = "";
        public string DiscordAdminWebhookUrl { get; set; } = "";

        /// <summary>Optional role mention for slot warnings, e.g. &lt;@&amp;123456789&gt;.</summary>
        public string DiscordSlotWarningPing { get; set; } = "";

        /// <summary>Add GPS markers to players' GPS lists for open zones, upcoming zones and loot drops.</summary>
        public bool AddGpsMarkers { get; set; } = true;

        /// <summary>Minutes before a slot opens to warn players. Empty = 30 and 10; a single 0 = no warnings.</summary>
        public List<int> SlotWarningMinutes { get; set; } = new List<int>();

        /// <summary>Weekly Discord roll-up time (UTC).</summary>
        public WeeklyTime WeeklyRollup { get; set; }

        /// <summary>Optional shared settings inherited by zones where the setting was omitted.</summary>
        public ZoneProfile Defaults { get; set; }

        public List<ZoneProfile> Zones { get; set; } = new List<ZoneProfile>();
        public List<SlotConfig> Slots { get; set; } = new List<SlotConfig>();

        /// <summary>Fills in anything the file left out.</summary>
        public static void ApplyDefaults(KothConfig config)
        {
            if (config.Defaults != null && config.Zones != null)
            {
                foreach (var zone in config.Zones)
                    zone.ApplyDefaultsFrom(config.Defaults);
            }

            if (config.SlotWarningMinutes == null || config.SlotWarningMinutes.Count == 0)
                config.SlotWarningMinutes = new List<int> { 30, 10 };
            if (config.WeeklyRollup == null)
                config.WeeklyRollup = new WeeklyTime { Day = System.DayOfWeek.Monday, Hour = 0, Minute = 0 };
            if (config.Zones == null)
                config.Zones = new List<ZoneProfile>();
            if (config.Slots == null)
                config.Slots = new List<SlotConfig>();
            foreach (var zone in config.Zones)
            {
                if (zone.Loot == null)
                    zone.Loot = new List<LootItem>();
            }
            foreach (var slot in config.Slots)
            {
                if (slot.Pool == null)
                    slot.Pool = new List<string>();
                if (slot.Start == null)
                    slot.Start = new WeeklyTime();
            }
        }
    }
}
