using System.Collections.Generic;
using GVK.Koth.Core;
using GVK.Koth.Core.Config;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRageMath;

namespace GVK.Koth.Zones
{
    /// <summary>
    /// A zone while the session runs: its profile, capture bar and loot queue. Fixed zones sit at their
    /// coordinates; NPC zones follow the grid that carries their tag.
    /// </summary>
    public sealed class ZoneRuntime
    {
        public ZoneRuntime(ZoneProfile profile, ZoneSave save)
        {
            Profile = profile;
            Save = save;
            Capture = new ZoneCapture(new CaptureRules
            {
                CaptureSeconds = profile.CaptureSeconds,
                MinPlayers = profile.Mode == ZoneMode.Encampment ? 1 : profile.MinPlayers,
                IdleDrainPlayers = profile.IdleDrainPlayers,
                MinPresenceShare = profile.MinPresenceShare,
                PointsPerCompletion = profile.PointsPerCompletion,
                PointsPerLoot = profile.PointsPerLoot,
                FlatSpeed = profile.Mode == ZoneMode.Encampment
            })
            {
                Progress = save.Progress,
                LootCounter = save.LootCounter
            };

            bool knownFactionProgress = save.FactionProgressSpecified && save.FactionProgress != null;
            if (!knownFactionProgress && save.Progress > 0 && profile.MinPresenceShare > 0)
            {
                // One-time migration: legacy saves have no historical faction credit for this bar.
                Capture.Progress = 0;
                Plugin.Log.Warn($"GVK KOTH: reset the incomplete capture bar for zone '{profile.Name}' because its legacy save has no faction presence data. Loot progress and pending loot are preserved.");
            }
            else if (knownFactionProgress)
            {
                var restored = new List<KeyValuePair<long, double>>();
                foreach (var entry in save.FactionProgress)
                    if (entry != null)
                        restored.Add(new KeyValuePair<long, double>(entry.FactionId, entry.Progress));
                Capture.RestoreFactionProgress(restored);
            }
        }

        /// <summary>An NPC zone on a tagged grid.</summary>
        public ZoneRuntime(ZoneProfile profile, ZoneSave save, NpcZoneSave npc, MyCubeGrid grid, IMyRemoteControl remote)
            : this(profile, save)
        {
            Npc = npc;
            Grid = grid;
            Remote = remote;
        }

        public ZoneProfile Profile { get; }
        public ZoneSave Save { get; }
        public ZoneCapture Capture { get; }

        public NpcZoneSave Npc { get; }
        public MyCubeGrid Grid { get; }
        public IMyRemoteControl Remote { get; }
        public bool IsNpc => Npc != null;

        public string Name => Profile.Name;
        public bool Active { get; set; }

        /// <summary>The NPC zone ended because its remote control was destroyed (already announced).</summary>
        public bool EndedEarly { get; set; }

        /// <summary>Where the zone is now: its coordinates, or the centre of its NPC grid.</summary>
        public Vector3D Center => Grid != null ? Grid.PositionComp.WorldAABB.Center : new Vector3D(Profile.X, Profile.Y, Profile.Z);

        /// <summary>The NPC grid is gone (despawned, concealed or deleted).</summary>
        public bool GridGone => Grid != null && (Grid.Closed || Grid.MarkedForClose);

        /// <summary>The tagged remote control was destroyed or is no longer working.</summary>
        public bool RemoteKilled => Remote != null && (Remote.Closed || !Remote.IsFunctional);

        /// <summary>Participants counted at the last check, for status and the HUD.</summary>
        public int LastParticipantCount { get; set; }

        /// <summary>Faction tags observed at the last check, for the HUD.</summary>
        public string[] LastFactionTags { get; set; } = new string[0];

        public long LootContainerId;
        public double SecondsToNextLootAttempt;

        /// <summary>Copies the bar, faction credit and loot counter into the saved state.</summary>
        public void StoreState()
        {
            Save.Progress = Capture.Progress;
            Save.LootCounter = Capture.LootCounter;
            var factionProgress = new List<FactionProgressSave>();
            foreach (var entry in Capture.SnapshotFactionProgress())
                factionProgress.Add(new FactionProgressSave { FactionId = entry.Key, Progress = entry.Value });
            Save.FactionProgress = factionProgress;
            Save.FactionProgressSpecified = true;

            // A closed or unavailable NPC grid cannot provide a reliable recovery location.
            if (Grid == null || (!Grid.Closed && !Grid.MarkedForClose && Grid.PositionComp != null))
            {
                Vector3D center = Center;
                if (!double.IsNaN(center.X) && !double.IsInfinity(center.X) &&
                    !double.IsNaN(center.Y) && !double.IsInfinity(center.Y) &&
                    !double.IsNaN(center.Z) && !double.IsInfinity(center.Z))
                    Save.LootPosition = new LootPositionSave { X = center.X, Y = center.Y, Z = center.Z };
            }
        }
    }
}
