using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using GVK.Koth.Core;
using GVK.Koth.Core.Config;
using GVK.Koth.Services;
using Sandbox.Game.Entities;
using Sandbox.Game.World;
using Sandbox.ModAPI;
using VRage.Game.Entity;

namespace GVK.Koth.Zones
{
    /// <summary>
    /// Finds NPC grids whose remote control carries "GVK-KOTH=&lt;profile&gt;;" (from the MES behavior profile)
    /// and keeps one zone per grid. Grids are checked when they enter the world (spawn or reveal from
    /// concealment) and again a few seconds later, in case MES writes the behavior after the grid appears.
    /// </summary>
    public sealed class NpcZoneTracker
    {
        private const double RecheckSeconds = 5;
        private static readonly TimeSpan ForgetAfter = TimeSpan.FromDays(7);

        private readonly Plugin _plugin;
        private readonly KothData _data;

        // Grid ids to check, with the time they are due. Filled from the entity-added event.
        private readonly ConcurrentQueue<(long GridId, DateTime DueUtc)> _toCheck = new ConcurrentQueue<(long, DateTime)>();
        private readonly List<(long GridId, DateTime DueUtc)> _notYetDue = new List<(long, DateTime)>();
        private readonly HashSet<long> _unknownProfileReported = new HashSet<long>();

        private readonly Dictionary<long, ZoneRuntime> _zones = new Dictionary<long, ZoneRuntime>();

        public NpcZoneTracker(Plugin plugin, KothData data)
        {
            _plugin = plugin;
            _data = data;
        }

        public IEnumerable<ZoneRuntime> Zones => _zones.Values;

        /// <summary>Tagged NPC blocks found by the same grid scan.</summary>
        public LinkedBlocks Links { get; } = new LinkedBlocks();

        public void Start()
        {
            MyEntities.OnEntityAdd += OnEntityAdd;
            var now = DateTime.UtcNow;
            foreach (var entity in MyEntities.GetEntities())
            {
                if (entity is MyCubeGrid grid)
                    _toCheck.Enqueue((grid.EntityId, now));
            }
        }

        public void Stop()
        {
            MyEntities.OnEntityAdd -= OnEntityAdd;
            foreach (var zone in _zones.Values)
                zone.StoreState();
            _zones.Clear();
        }

        /// <summary>Drops every zone so the next checks rebuild them from the new config.</summary>
        public void Rebuild()
        {
            foreach (var zone in _zones.Values)
                zone.StoreState();
            _zones.Clear();
            _unknownProfileReported.Clear();
            Links.Clear();

            var now = DateTime.UtcNow;
            foreach (var entity in MyEntities.GetEntities())
            {
                if (entity is MyCubeGrid grid)
                    _toCheck.Enqueue((grid.EntityId, now));
            }
        }

        private void OnEntityAdd(MyEntity entity)
        {
            if (!(entity is MyCubeGrid grid))
                return;

            var now = DateTime.UtcNow;
            _toCheck.Enqueue((grid.EntityId, now));
            _toCheck.Enqueue((grid.EntityId, now.AddSeconds(RecheckSeconds)));
        }

        /// <summary>Checks queued grids and drops zones whose grid is gone. Game thread only.</summary>
        public void Refresh(DateTime now)
        {
            while (_toCheck.TryDequeue(out var item))
            {
                if (item.DueUtc > now)
                    _notYetDue.Add(item);
                else
                    Check(item.GridId, now);
            }
            foreach (var item in _notYetDue)
                _toCheck.Enqueue(item);
            _notYetDue.Clear();

            foreach (var gridId in _zones.Keys.ToList())
            {
                var zone = _zones[gridId];
                if (zone.GridGone || zone.Npc.Ended)
                {
                    // Despawned or concealed: drop it quietly. A revealed grid is found again with its lifetime.
                    zone.StoreState();
                    _zones.Remove(gridId);
                }
                else
                {
                    zone.Npc.LastSeenUtc = now;
                }
            }

            foreach (var old in _data.State.NpcZones.ToList())
            {
                var associatedZoneSave = _data.State.Zones
                    .Where(z => string.Equals(z.Name, old.SaveKey, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(z => z.LootReconciliation != null || z.PendingLootDrops != 0)
                    .FirstOrDefault();
                if (!CanForgetSavedZone(old, associatedZoneSave, now, _zones.ContainsKey(old.GridId)))
                    continue;

                _data.State.NpcZones.Remove(old);
                _data.State.Zones.RemoveAll(z => z.Name == old.SaveKey);
            }
        }

        internal static bool CanForgetSavedZone(NpcZoneSave npc, ZoneSave save, DateTime now, bool active)
        {
            return !active && now - npc.LastSeenUtc > ForgetAfter
                && (save == null || (save.LootReconciliation == null && save.PendingLootDrops == 0));
        }

        private void Check(long gridId, DateTime now)
        {
            if (!MyEntities.TryGetEntityById(gridId, out var entity) || !(entity is MyCubeGrid grid) || grid.Closed || grid.MarkedForClose)
                return;

            Links.Scan(grid);
            if (_zones.ContainsKey(gridId))
                return;

            foreach (var block in grid.GetFatBlocks())
            {
                if (!(block is IMyRemoteControl remote) || remote.Closed)
                    continue;

                string profileName = NpcTag.Read(remote.CustomData);
                if (profileName == null || !IsNpc(remote.OwnerId))
                    continue;

                var profile = _plugin.Config.Zones.FirstOrDefault(z =>
                    z.Attach == ZoneAttach.NpcTag && string.Equals(z.Name, profileName, StringComparison.OrdinalIgnoreCase));
                if (profile == null)
                {
                    if (_unknownProfileReported.Add(gridId))
                        _plugin.Alerts.Send($"NPC grid '{grid.DisplayName}' has GVK-KOTH={profileName}, but no zone with that Name and Attach NpcTag exists.");
                    return;
                }

                var npc = _data.State.GetNpcZone(gridId);
                if (npc == null)
                {
                    npc = new NpcZoneSave { GridId = gridId, Profile = profile.Name, FirstSeenUtc = now, LastSeenUtc = now };
                    _data.State.NpcZones.Add(npc);
                }
                if (npc.Ended)
                    return;

                _zones[gridId] = new ZoneRuntime(profile, _data.State.GetZone(npc.SaveKey), npc, grid, remote);
                Plugin.Log.Info($"GVK KOTH: NPC zone '{profile.Name}' found on grid '{grid.DisplayName}' ({gridId}).");
                return;
            }
        }

        private static bool IsNpc(long identityId)
        {
            return identityId != 0 && MySession.Static?.Players?.IdentityIsNpc(identityId) == true;
        }
    }
}
