using System;
using System.Collections.Generic;
using System.Linq;

namespace GVK.Koth.Core
{
    /// <summary>
    /// Saved runtime state, so a restart keeps capture progress, loot owed and slot picks.
    /// </summary>
    public sealed class KothState
    {
        public List<ZoneSave> Zones { get; set; } = new List<ZoneSave>();
        public List<SlotPick> SlotPicks { get; set; } = new List<SlotPick>();
        public List<NpcZoneSave> NpcZones { get; set; } = new List<NpcZoneSave>();

        /// <summary>Zones forced open or closed with !koth start / !koth stop; kept until !koth auto.</summary>
        public List<string> ForcedOn { get; set; } = new List<string>();
        public List<string> ForcedOff { get; set; } = new List<string>();

        public NpcZoneSave GetNpcZone(long gridId) => NpcZones.FirstOrDefault(z => z.GridId == gridId);

        public ZoneSave GetZone(string name)
        {
            var zone = Zones.FirstOrDefault(z => string.Equals(z.Name, name, StringComparison.OrdinalIgnoreCase));
            if (zone == null)
            {
                zone = new ZoneSave { Name = name };
                Zones.Add(zone);
            }
            return zone;
        }
    }

    public sealed class ZoneSave
    {
        public string Name { get; set; } = "";
        public double Progress { get; set; }
        public int LootCounter { get; set; }

        /// <summary>
        /// Per-faction contribution to the current capture bar. A specified, non-null list
        /// is a known snapshot, including when empty; an absent element or null list is unknown.
        /// </summary>
        public List<FactionProgressSave> FactionProgress { get; set; }

        /// <summary>Whether the faction progress XML element was present or should be written.</summary>
        [System.Xml.Serialization.XmlIgnore]
        public bool FactionProgressSpecified { get; set; }

        /// <summary>Loot drops earned but not yet delivered (container missing, concealed or full).</summary>
        public long PendingLootDrops { get; set; }

        /// <summary>Last known loot delivery position; null means the location is unknown.</summary>
        public LootPositionSave LootPosition { get; set; }

        /// <summary>Unresolved delivery attempt requiring operator reconciliation; null for legacy saves.</summary>
        public LootReconciliationSave LootReconciliation { get; set; }
    }

    /// <summary>One faction's contribution to the saved capture bar.</summary>
    public sealed class FactionProgressSave
    {
        public long FactionId { get; set; }
        public double Progress { get; set; }
    }

    /// <summary>Saved world position for recovering an owed loot delivery.</summary>
    public sealed class LootPositionSave
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
    }

    /// <summary>Details of a loot delivery whose residual could not be verified.</summary>
    public sealed class LootReconciliationSave
    {
        public string AttemptId { get; set; } = "";
        public long ContainerId { get; set; }
        public DateTime CreatedUtc { get; set; }
        public string Reason { get; set; } = "";
        public List<LootReconciliationItemSave> Items { get; set; } = new List<LootReconciliationItemSave>();
    }

    /// <summary>Item quantities in raw fixed-point units (one million units per item).</summary>
    public sealed class LootReconciliationItemSave
    {
        public string TypeId { get; set; } = "";
        public string SubtypeId { get; set; } = "";
        public long RequestedRaw { get; set; }
        public long BeforeRaw { get; set; }
        public long ObservedRaw { get; set; }
        public bool ObservedKnown { get; set; }
    }

    /// <summary>
    /// An NPC grid carrying a KOTH tag. Kept so a concealed (or restarted) grid resumes with its remaining lifetime,
    /// and a zone that ended stays ended.
    /// </summary>
    public sealed class NpcZoneSave
    {
        public long GridId { get; set; }
        public string Profile { get; set; } = "";
        public DateTime FirstSeenUtc { get; set; }
        public DateTime LastSeenUtc { get; set; }

        /// <summary>Ended by its lifetime or by its remote control being destroyed; never restarts.</summary>
        public bool Ended { get; set; }

        /// <summary>"Open" was announced once; a concealed grid coming back doesn't announce again.</summary>
        public bool AnnouncedOpen { get; set; }

        /// <summary>Key for this grid's ZoneSave (bar and loot), separate from other grids with the same profile.</summary>
        public string SaveKey => Profile + "#" + GridId;

        /// <summary>True while the zone is within its lifetime (0 minutes = no limit).</summary>
        public bool WithinLifetime(int lifetimeMinutes, DateTime utcNow)
        {
            return !Ended && (lifetimeMinutes <= 0 || utcNow < FirstSeenUtc.AddMinutes(lifetimeMinutes));
        }
    }

    /// <summary>The zone a slot picked for one occurrence, so a restart mid-slot keeps the same zone.</summary>
    public sealed class SlotPick
    {
        public string Slot { get; set; } = "";
        public DateTime StartUtc { get; set; }
        public string Zone { get; set; } = "";

        /// <summary>The zone's SpawnGroup (Flying KOTH) was spawned for this occurrence.</summary>
        public bool Spawned { get; set; }
    }
}
