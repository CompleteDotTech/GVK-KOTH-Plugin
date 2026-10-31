using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using GVK.Koth.Core;
using GVK.Koth.Core.Config;
using GVK.Koth.Services;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRageMath;

namespace GVK.Koth.Zones
{
    /// <summary>
    /// Runs every zone once per check: which zones are active (always on, slots, admin overrides), the shared
    /// capture bars, rewards, the loot queue, slot warnings and the weekly roll-up. Game thread only.
    /// </summary>
    public sealed class ZoneManager
    {
        private const double LootRetrySeconds = 10;
        private const double StateSaveSeconds = 60;
        private const double LootGpsRange = 15000;
        private static readonly TimeSpan LootGpsLifetime = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan DefaultOpenGpsLifetime = TimeSpan.FromHours(3);

        private readonly Plugin _plugin;
        private readonly KothData _data;
        private readonly Announcer _announce;
        private readonly Random _random = new Random();

        // Fixed-coordinate zones by name; NPC zones live in the tracker, one per tagged grid.
        private readonly Dictionary<string, ZoneRuntime> _zones = new Dictionary<string, ZoneRuntime>(StringComparer.OrdinalIgnoreCase);
        private readonly NpcZoneTracker _npc;
        private readonly RuleEnforcer _rules;
        private readonly HashSet<string> _forcedOn = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _forcedOff = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Slot name -> start of the occurrence that is running now.
        private readonly Dictionary<string, DateTime> _runningSlots = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _warningsSent = new HashSet<string>();
        private readonly HashSet<string> _spawnFailuresReported = new HashSet<string>();

        private sealed class LootFailure
        {
            public DateTime FirstUtc;
            public long RetryCount;
        }

        private readonly Dictionary<string, LootFailure> _lootFailures = new Dictionary<string, LootFailure>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, DateTime> _lootNextAttempt = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<long> _checkedNpcContainers = new HashSet<long>();
        private double _secondsSinceSave;

        // Profiles open at the last check, and profiles whose sandbox boolean has been written at least once.
        private readonly HashSet<string> _activeProfiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _publishedProfiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public ZoneManager(Plugin plugin, KothData data, Announcer announce)
        {
            _plugin = plugin;
            _data = data;
            _announce = announce;
            _npc = new NpcZoneTracker(plugin, data);
            _rules = new RuleEnforcer(announce);

            // Admin overrides survive restarts until !koth auto
            _forcedOn.UnionWith(data.State.ForcedOn);
            _forcedOff.UnionWith(data.State.ForcedOff);
        }

        private KothConfig Config => _plugin.Config;

        /// <summary>Fixed zones and NPC zones.</summary>
        public IEnumerable<ZoneRuntime> Zones => _zones.Values.Concat(_npc.Zones);

        /// <summary>A fixed zone by name.</summary>
        public ZoneRuntime Find(string name) => name != null && _zones.TryGetValue(name, out var zone) ? zone : null;

        /// <summary>True if the name is a configured zone profile (fixed or NPC).</summary>
        public bool IsProfile(string name) =>
            Config.Zones.Any(z => string.Equals(z.Name, name, StringComparison.OrdinalIgnoreCase));

        public void Start() => _npc.Start();

        public void Stop()
        {
            SaveState();
            if (MyAPIGateway.Session != null && MyAPIGateway.Utilities != null)
                ClosePublishedProfiles();
            _npc.Stop();
        }

        /// <summary>Builds the zones from the config, keeping saved bars and loot queues.</summary>
        public void Rebuild()
        {
            foreach (var zone in _zones.Values)
                zone.StoreState();
            _zones.Clear();

            foreach (var profile in Config.Zones)
            {
                if (profile.Attach != ZoneAttach.Coordinates || string.IsNullOrWhiteSpace(profile.Name) || _zones.ContainsKey(profile.Name))
                    continue;
                _zones[profile.Name] = new ZoneRuntime(profile, _data.State.GetZone(profile.Name));
            }

            _npc.Rebuild();
            _checkedNpcContainers.Clear();
        }

        public void Tick(double dt)
        {
            var now = DateTime.UtcNow;
            var slotZones = UpdateSlots(now);
            _npc.Refresh(now);

            var liveZones = Zones.ToList();
            var liveSaves = new HashSet<ZoneSave>();
            var liveKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var zone in liveZones)
            {
                liveSaves.Add(zone.Save);
                liveKeys.Add(zone.Save.Name ?? "");
                if (zone.IsNpc && _checkedNpcContainers.Add(zone.Npc.GridId))
                    _plugin.ReportRuntimeConfigProblems(CheckLootContainer(zone));
                var profile = zone.Profile;
                bool active = zone.IsNpc ? NpcActive(zone, slotZones, now) : FixedActive(zone, slotZones);

                if (active != zone.Active)
                {
                    zone.Active = active;
                    if (!active)
                        zone.Capture.Reset();
                    if (profile.Activation != ZoneActivation.AlwaysOn && profile.Mode != ZoneMode.RulesOnly)
                        AnnounceStartOrEnd(zone);
                }

                if (zone.Active && profile.Mode != ZoneMode.RulesOnly)
                    RunCapture(zone, dt);
                else
                    zone.LastFactionTags = new string[0];

                if (!zone.GridGone)
                    DeliverQueuedLoot(zone.Save, zone.Profile, zone.Center, now);
            }

            // Recover only the saved grid's own debt; never instantiate a capture runtime.
            foreach (var npc in _data.State.NpcZones.ToList())
            {
                if (npc == null || liveKeys.Contains(npc.SaveKey))
                    continue;
                foreach (var save in _data.State.Zones.Where(z => z != null &&
                    string.Equals(z.Name, npc.SaveKey, StringComparison.OrdinalIgnoreCase)).ToList())
                {
                    if (!liveSaves.Add(save))
                        continue;
                    var profile = Profile(npc.Profile);
                    var pos = save.LootPosition;
                    if (save.LootReconciliation != null || save.PendingLootDrops == 0)
                        continue;
                    if (save.PendingLootDrops < 0)
                    {
                        ReportLootFailure(save, "Negative saved loot queue; operator correction required.", now);
                        continue;
                    }
                    if (profile == null || profile.Attach != ZoneAttach.NpcTag || pos == null ||
                        !Finite(pos.X) || !Finite(pos.Y) || !Finite(pos.Z))
                    {
                        ReportLootFailure(save, "Saved NPC profile or finite loot position is unavailable; operator action required.", now);
                        continue;
                    }
                    DeliverQueuedLoot(save, profile, new Vector3D(pos.X, pos.Y, pos.Z), now);
                }
            }

            var zones = Zones.ToList();
            _npc.Links.Apply(zones.Where(z => z.Active && z.Profile.Mode != ZoneMode.RulesOnly).ToList());
            _rules.Apply(zones, now);
            PublishActiveProfiles(zones);
            _plugin.Sync?.Tick(this, dt, now);

            CheckWeeklyRollup(now);

            _secondsSinceSave += dt;
            if (_secondsSinceSave >= StateSaveSeconds)
                SaveState();
        }

        /// <summary>True while any zone with this profile name is open.</summary>
        public bool IsProfileActive(string name) => name != null && _activeProfiles.Contains(name);

        /// <summary>
        /// On every change, sets the MES sandbox boolean GVK_KOTH_&lt;Name&gt;_Active and tells API listeners.
        /// At start every profile is written once, so a value left true by a crash is corrected.
        /// </summary>
        private void PublishActiveProfiles(List<ZoneRuntime> zones)
        {
            var nowActive = new HashSet<string>(
                zones.Where(z => z.Active && z.Profile.Mode != ZoneMode.RulesOnly).Select(z => z.Name),
                StringComparer.OrdinalIgnoreCase);

            var eligible = new HashSet<string>(Config.Zones.Where(p => p != null && p.Mode != ZoneMode.RulesOnly &&
                !string.IsNullOrWhiteSpace(p.Name)).Select(p => p.Name), StringComparer.OrdinalIgnoreCase);
            foreach (var name in _publishedProfiles.Where(name => !eligible.Contains(name)).ToList())
            {
                MyAPIGateway.Utilities.SetVariable(SandboxBoolName(name), false);
                if (_activeProfiles.Remove(name))
                    _plugin.Api?.ZoneChanged(name, false);
                _publishedProfiles.Remove(name);
            }

            foreach (var profile in Config.Zones)
            {
                if (profile.Mode == ZoneMode.RulesOnly || string.IsNullOrWhiteSpace(profile.Name))
                    continue;

                bool active = nowActive.Contains(profile.Name);
                bool known = _publishedProfiles.Contains(profile.Name);
                if (known && active == _activeProfiles.Contains(profile.Name))
                    continue;

                MyAPIGateway.Utilities.SetVariable(SandboxBoolName(profile.Name), active);
                if (known)
                    _plugin.Api?.ZoneChanged(profile.Name, active);
                _publishedProfiles.Add(profile.Name);
            }

            _activeProfiles.Clear();
            _activeProfiles.UnionWith(nowActive);
        }

        private void ClosePublishedProfiles()
        {
            foreach (var name in _publishedProfiles.ToList())
            {
                MyAPIGateway.Utilities.SetVariable(SandboxBoolName(name), false);
                if (_activeProfiles.Contains(name))
                    _plugin.Api?.ZoneChanged(name, false);
            }
            _activeProfiles.Clear();
            _publishedProfiles.Clear();
        }

        public static string SandboxBoolName(string profileName) => $"GVK_KOTH_{profileName}_Active";

        private bool FixedActive(ZoneRuntime zone, HashSet<string> slotZones)
        {
            var profile = zone.Profile;
            return profile.Enabled
                && !_forcedOff.Contains(zone.Name)
                && (profile.Activation == ZoneActivation.AlwaysOn || slotZones.Contains(zone.Name) || _forcedOn.Contains(zone.Name));
        }

        /// <summary>
        /// An NPC zone runs while its grid is here, its remote control works and (by Activation) its slot is
        /// running or its lifetime hasn't run out. A destroyed remote control or an expired lifetime ends it for good.
        /// </summary>
        private bool NpcActive(ZoneRuntime zone, HashSet<string> slotZones, DateTime now)
        {
            var profile = zone.Profile;
            var npc = zone.Npc;
            if (npc.Ended || zone.GridGone)
                return false;

            if (zone.RemoteKilled)
            {
                npc.Ended = true;
                zone.EndedEarly = true;
                if (zone.Active && profile.Mode != ZoneMode.RulesOnly)
                    _announce.Both($"KOTH {zone.Name} has ended: its control was destroyed.");
                GpsMarkers.RemoveForAll(GpsMarkers.ZoneName(zone.Name));
                return false;
            }

            if (!profile.Enabled || _forcedOff.Contains(zone.Name))
                return false;

            if (profile.Activation == ZoneActivation.Slot)
                return slotZones.Contains(zone.Name) || _forcedOn.Contains(zone.Name);

            if (!npc.WithinLifetime(profile.LifetimeMinutes, now))
            {
                npc.Ended = true;
                return false;
            }
            return true;
        }

        public void SaveState()
        {
            _secondsSinceSave = 0;
            foreach (var zone in Zones.ToList())
                zone.StoreState();
            _data.SaveState();
        }

        // --- Capture ------------------------------------------------------------------------------------------

        private void RunCapture(ZoneRuntime zone, double dt)
        {
            var participants = zone.Profile.Mode == ZoneMode.Encampment
                ? ParticipantScanner.Encampment(zone.Profile, zone.Center)
                : ParticipantScanner.Capture(zone.Profile, zone.Center);

            zone.LastParticipantCount = participants.Count;
            var tags = new List<string>();
            var factionIds = new HashSet<long>();
            foreach (long factionId in participants)
            {
                if (!factionIds.Add(factionId))
                    continue;

                var faction = MyAPIGateway.Session.Factions.TryGetFactionById(factionId);
                if (faction != null)
                    tags.Add(faction.Tag);
            }
            zone.LastFactionTags = tags.ToArray();
            if (zone.Save.PendingLootDrops < 0)
            {
                ReportLootFailure(zone.Save, "Negative saved loot queue; operator correction required.", DateTime.UtcNow);
                return;
            }
            long credits;
            try
            {
                if (zone.Profile.PointsPerCompletion < 0 || zone.Profile.CreditsPerPoint < 0)
                    throw new OverflowException();
                credits = checked((long)zone.Profile.PointsPerCompletion * zone.Profile.CreditsPerPoint);
            }
            catch (OverflowException)
            {
                ReportLootFailure(zone.Save, "Invalid or overflowing credit reward; operator correction required.", DateTime.UtcNow);
                return;
            }
            var result = zone.Capture.Tick(dt, participants, long.MaxValue - zone.Save.PendingLootDrops);
            if (result.Completed)
                Reward(zone, result, credits);
        }

        private void Reward(ZoneRuntime zone, CaptureResult result, long credits)
        {
            // Verify queue headroom before any payment, scoring, or other completion side effect.
            long pending = checked(zone.Save.PendingLootDrops + result.LootDrops);
            var profile = zone.Profile;
            var tags = new List<string>();

            foreach (long factionId in result.ScoringFactions)
            {
                var faction = MyAPIGateway.Session.Factions.TryGetFactionById(factionId);
                if (faction == null)
                    continue;

                if (credits > 0)
                    faction.RequestChangeBalance(credits);

                if (result.PointsEach > 0)
                    _data.AddPoints(faction.FactionId, faction.Tag, faction.Name, result.PointsEach,
                        profile.Mode == ZoneMode.Encampment ? "Encampment" : "Zone", profile.Name);

                tags.Add($"[{faction.Tag}]");
            }

            if (tags.Count > 0)
            {
                string creditText = credits > 0
                    ? $" and {credits.ToString("N0", CultureInfo.InvariantCulture)} SC"
                    : "";
                _announce.Both($"{profile.Name}: {string.Join(" ", tags)} scored {result.PointsEach} points{creditText} each.");
                _plugin.Sync?.Completed(zone, tags);
            }

            if (result.LootDrops > 0)
            {
                zone.Save.PendingLootDrops = pending;
                zone.StoreState();
                _lootNextAttempt.Remove(zone.Save.Name ?? "");
            }
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        public List<string> CheckLootContainers()
        {
            var problems = new List<string>();
            if (MyAPIGateway.Session == null)
                return problems;
            foreach (var zone in Zones.ToList())
                problems.AddRange(CheckLootContainer(zone));
            return problems;
        }

        private IEnumerable<string> CheckLootContainer(ZoneRuntime zone)
        {
            if (MyAPIGateway.Session == null || zone.GridGone || zone.Profile.Loot == null ||
                zone.Profile.Loot.Count == 0)
                yield break;
            if (!LootDelivery.HasContainer(zone.Profile, zone.Center))
                yield return $"Zone '{zone.Name}' ({zone.Save.Name}): no eligible named NPC-owned loot container found at the known position.";
        }

        private void ReportLootFailure(ZoneSave save, string reason, DateTime now)
        {
            string key = save.Name ?? "";
            if (!_lootFailures.TryGetValue(key, out var failure))
            {
                failure = new LootFailure { FirstUtc = now, RetryCount = 1 };
                _lootFailures[key] = failure;
                _plugin.Alerts.Send($"Loot queue '{key}' blocked: {save.PendingLootDrops} owed; first failure {now:O}, age 0s, retries 1; {reason}");
            }
            else if (failure.RetryCount < long.MaxValue)
                failure.RetryCount++;
        }

        private void DeliverQueuedLoot(ZoneSave save, ZoneProfile profile, Vector3D center, DateTime now)
        {
            if (save.LootReconciliation != null)
                return;
            if (save.PendingLootDrops < 0)
            {
                ReportLootFailure(save, "Negative saved loot queue; operator correction required.", now);
                return;
            }
            if (save.PendingLootDrops == 0)
                return;
            string key = save.Name ?? "";
            if (_lootNextAttempt.TryGetValue(key, out var next) && now < next)
                return;
            _lootNextAttempt[key] = now.AddSeconds(LootRetrySeconds);

            long delivered = 0;
            long containerId = 0;
            IMyCargoContainer container = null;
            while (save.PendingLootDrops > 0)
            {
                var result = LootDelivery.TryDeliver(profile, center, ref containerId, out var attemptedContainer);
                if (result.Status == LootDeliveryStatus.ReconciliationRequired)
                {
                    var marker = result.Reconciliation ?? new LootReconciliationSave
                    {
                        ContainerId = containerId, CreatedUtc = now,
                        Reason = "Delivery required reconciliation but supplied no details."
                    };
                    save.LootReconciliation = marker;
                    SaveState();
                    _plugin.Alerts.Send($"Zone {key}: loot delivery suspended; container {marker.ContainerId}, attempt {marker.AttemptId}, reason: {marker.Reason}");
                    break;
                }
                if (result.Status != LootDeliveryStatus.Delivered)
                {
                    ReportLootFailure(save, result.Reason, now);
                    break;
                }
                save.PendingLootDrops--;
                delivered++;
                container = attemptedContainer;
            }

            if (delivered > 0)
            {
                string drops = delivered == 1 ? "Loot has" : $"{delivered} loot drops have";
                _announce.Chat($"{drops} dropped at {profile.Name}! Loot GPS added for players nearby.");
                if (container != null)
                    GpsMarkers.AddForAll(GpsMarkers.LootName(profile.Name), "KOTH loot container", container.GetPosition(),
                        GpsMarkers.LootColor, LootGpsLifetime, LootGpsRange);
                SaveState();
            }
            if (delivered > 0 && save.PendingLootDrops == 0 && _lootFailures.TryGetValue(key, out var failure))
            {
                _plugin.Alerts.Send($"Loot queue '{key}' recovered after {Math.Max(0, (now - failure.FirstUtc).TotalSeconds):0}s; first failure {failure.FirstUtc:O}, retries {failure.RetryCount}.");
                _lootFailures.Remove(key);
                _lootNextAttempt.Remove(key);
            }
        }

        // --- Slots ----------------------------------------------------------------------------------------------

        /// <summary>Returns the zones picked by running slots, and handles slot start, end and warnings.</summary>
        private HashSet<string> UpdateSlots(DateTime now)
        {
            var picked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var slot in Config.Slots)
            {
                if (!slot.Enabled || slot.Pool == null || slot.Pool.Count == 0 || slot.DurationMinutes <= 0)
                    continue;

                if (WeeklySchedule.IsActive(slot.Start, slot.DurationMinutes, now, out var start))
                {
                    string zone = GetOrPick(slot, start);
                    if (zone != null)
                    {
                        picked.Add(zone);
                        SpawnIfNeeded(slot, start, zone);
                    }
                    _runningSlots[slot.Name] = start;
                }
                else
                {
                    _runningSlots.Remove(slot.Name);
                    SendWarnings(slot, WeeklySchedule.NextStart(slot.Start, now), now);
                }
            }

            // Forget picks older than a week.
            _data.State.SlotPicks.RemoveAll(p => p.StartUtc < now.AddDays(-8));
            return picked;
        }

        public ZoneProfile Profile(string name) =>
            name == null ? null : Config.Zones.FirstOrDefault(z => string.Equals(z.Name, name, StringComparison.OrdinalIgnoreCase));

        /// <summary>Where a profile is now: its fixed zone, an open NPC zone of that name, or its configured X/Y/Z.</summary>
        public Vector3D ProfilePosition(ZoneProfile profile)
        {
            var zone = Find(profile.Name) ?? _npc.Zones.FirstOrDefault(z => z.Active && string.Equals(z.Name, profile.Name, StringComparison.OrdinalIgnoreCase));
            return zone?.Center ?? new Vector3D(profile.X, profile.Y, profile.Z);
        }

        /// <summary>
        /// The zone the next slot will run and when it starts (picked early so players know which KOTH is next).
        /// </summary>
        public (ZoneProfile Profile, DateTime StartUtc)? NextSlotZone(DateTime now)
        {
            var next = Config.Slots
                .Where(s => s.Enabled && s.Pool != null && s.Pool.Count > 0 && s.DurationMinutes > 0 && !_runningSlots.ContainsKey(s.Name))
                .Select(s => (Slot: s, Start: WeeklySchedule.NextStart(s.Start, now)))
                .OrderBy(x => x.Start)
                .FirstOrDefault();
            if (next.Slot == null)
                return null;

            var profile = Profile(GetOrPick(next.Slot, next.Start));
            return profile == null ? ((ZoneProfile, DateTime)?)null : (profile, next.Start);
        }

        /// <summary>Spawns a picked zone's SpawnGroup (Flying KOTH) once per slot occurrence.</summary>
        private void SpawnIfNeeded(SlotConfig slot, DateTime start, string zoneName)
        {
            var profile = Profile(zoneName);
            if (profile == null || string.IsNullOrWhiteSpace(profile.SpawnGroup))
                return;

            var pick = _data.State.SlotPicks.FirstOrDefault(p =>
                string.Equals(p.Slot, slot.Name, StringComparison.OrdinalIgnoreCase) && p.StartUtc == start);
            if (pick == null || pick.Spawned)
                return;

            var position = new Vector3D(profile.X, profile.Y, profile.Z);
            if (_plugin.Mes.Spawn(profile.SpawnGroup, position))
            {
                pick.Spawned = true;
                _data.SaveState();
                Plugin.Log.Info($"GVK KOTH: spawned '{profile.SpawnGroup}' for slot '{slot.Name}' ({zoneName}).");
            }
            else if (_spawnFailuresReported.Add($"{slot.Name}|{start:o}"))
            {
                _plugin.Alerts.Send($"Slot '{slot.Name}': MES didn't spawn '{profile.SpawnGroup}' for {zoneName}; retrying every check.");
            }
        }

        /// <summary>The zone a slot occurrence uses, picked at random once and saved.</summary>
        private string GetOrPick(SlotConfig slot, DateTime start)
        {
            var pick = _data.State.SlotPicks.FirstOrDefault(p =>
                string.Equals(p.Slot, slot.Name, StringComparison.OrdinalIgnoreCase) && p.StartUtc == start);
            if (pick != null)
                return pick.Zone;

            var pool = slot.Pool.Where(name =>
            {
                var profile = Profile(name);
                return profile != null && profile.Enabled && profile.Activation == ZoneActivation.Slot;
            }).ToList();
            if (pool.Count == 0)
            {
                _plugin.Alerts.Send($"Slot '{slot.Name}' has no enabled zones to pick from.");
                return null;
            }

            string zone = pool[_random.Next(pool.Count)];
            _data.State.SlotPicks.Add(new SlotPick { Slot = slot.Name, StartUtc = start, Zone = zone });
            _data.SaveState();
            return zone;
        }

        private void SendWarnings(SlotConfig slot, DateTime nextStart, DateTime now)
        {
            double minutesUntil = (nextStart - now).TotalMinutes;
            foreach (int warning in Config.SlotWarningMinutes ?? new List<int>())
            {
                if (warning <= 0 || minutesUntil > warning || minutesUntil <= warning - 2)
                    continue;

                string key = $"{slot.Name}|{nextStart:o}|{warning}";
                if (!_warningsSent.Add(key))
                    continue;

                var profile = Profile(GetOrPick(slot, nextStart));
                if (profile == null)
                    continue;

                _announce.Chat($"KOTH {profile.Name} opens in {warning} minutes ({nextStart:HH:mm} UTC). GPS added.");
                var closes = nextStart.AddMinutes(slot.DurationMinutes);
                GpsMarkers.AddForAll(GpsMarkers.ZoneName(profile.Name), $"KOTH opens {nextStart:HH:mm} UTC", ProfilePosition(profile),
                    GpsMarkers.UpcomingColor, closes - DateTime.UtcNow);
                string ping = string.IsNullOrWhiteSpace(Config.DiscordSlotWarningPing) ? "" : Config.DiscordSlotWarningPing + " ";
                _announce.Discord(ping + $"KOTH {profile.Name} opens in {warning} minutes ({nextStart:HH:mm} UTC).");
            }
        }

        private void AnnounceStartOrEnd(ZoneRuntime zone)
        {
            if (zone.IsNpc)
            {
                AnnounceNpcStartOrEnd(zone);
                return;
            }

            if (zone.Active)
            {
                var slot = Config.Slots.FirstOrDefault(s => _runningSlots.ContainsKey(s.Name)
                    && string.Equals(GetOrPick(s, _runningSlots[s.Name]), zone.Name, StringComparison.OrdinalIgnoreCase));
                string until = slot != null ? $" until {_runningSlots[slot.Name].AddMinutes(slot.DurationMinutes):HH:mm} UTC" : "";
                _announce.Chat($"KOTH {zone.Name} is open{until}! GPS added.");
                _announce.Discord($"KOTH {zone.Name} is open{until}.");
                var lifetime = slot != null
                    ? _runningSlots[slot.Name].AddMinutes(slot.DurationMinutes) - DateTime.UtcNow
                    : DefaultOpenGpsLifetime;
                GpsMarkers.AddForAll(GpsMarkers.ZoneName(zone.Name), "King of the Hill: open", zone.Center, GpsMarkers.OpenColor, lifetime);
            }
            else
            {
                _announce.Both($"KOTH {zone.Name} has closed.");
                GpsMarkers.RemoveForAll(GpsMarkers.ZoneName(zone.Name));
            }
        }

        /// <summary>
        /// NPC zones announce "open" once per grid (not again when revealed from concealment), and "closed" only
        /// when their lifetime runs out. A grid that despawns or is concealed closes quietly.
        /// </summary>
        private void AnnounceNpcStartOrEnd(ZoneRuntime zone)
        {
            var npc = zone.Npc;
            if (zone.Active)
            {
                if (npc.AnnouncedOpen)
                    return;
                npc.AnnouncedOpen = true;

                string until = "";
                if (zone.Profile.Activation == ZoneActivation.Lifetime && zone.Profile.LifetimeMinutes > 0)
                {
                    int left = (int)Math.Ceiling((npc.FirstSeenUtc.AddMinutes(zone.Profile.LifetimeMinutes) - DateTime.UtcNow).TotalMinutes);
                    until = $" for {left} minutes";
                }
                _announce.Chat($"KOTH {zone.Name} is open{until}! GPS added.");
                _announce.Discord($"KOTH {zone.Name} is open{until}.");
                var lifetime = zone.Profile.Activation == ZoneActivation.Lifetime && zone.Profile.LifetimeMinutes > 0
                    ? npc.FirstSeenUtc.AddMinutes(zone.Profile.LifetimeMinutes) - DateTime.UtcNow
                    : DefaultOpenGpsLifetime;
                GpsMarkers.AddForAll(GpsMarkers.ZoneName(zone.Name), "King of the Hill: open", zone.Center, GpsMarkers.OpenColor, lifetime);
            }
            else if (npc.Ended && !zone.EndedEarly)
            {
                _announce.Both($"KOTH {zone.Name} has closed.");
                GpsMarkers.RemoveForAll(GpsMarkers.ZoneName(zone.Name));
            }
        }

        // --- Weekly roll-up --------------------------------------------------------------------------------------

        private void CheckWeeklyRollup(DateTime now)
        {
            var scores = _data.Scores;
            var due = WeeklySchedule.LastStart(Config.WeeklyRollup, now);
            if (scores.Data.LastRollupUtc >= due)
                return;

            // First run: start counting from now instead of posting an empty roll-up.
            if (scores.Data.LastRollupUtc != default)
                _announce.Discord(StandingsText("Weekly KOTH roll-up", includeGain: true));

            scores.MarkRollup(now);
            _data.SaveScores();
        }

        public string StandingsText(string title, bool includeGain)
        {
            var standings = _data.Scores.Standings();
            var sb = new StringBuilder();
            sb.AppendLine($"**{title}**");
            if (standings.Count == 0)
            {
                sb.AppendLine("No KOTH points yet this season.");
                return sb.ToString();
            }

            int rank = 1;
            foreach (var (score, gain) in standings)
            {
                string gainText = includeGain ? $" (+{gain} this week)" : "";
                sb.AppendLine($"{rank++}. [{score.Tag}] {score.Name}: {score.Points}{gainText}");
            }
            return sb.ToString();
        }

        // --- Admin overrides -------------------------------------------------------------------------------------

        public void ForceOn(string name)
        {
            _forcedOff.Remove(name);
            _forcedOn.Add(name);
            SaveForced();
        }

        public void ForceOff(string name)
        {
            _forcedOn.Remove(name);
            _forcedOff.Add(name);
            SaveForced();
        }

        public void ClearForce(string name)
        {
            _forcedOn.Remove(name);
            _forcedOff.Remove(name);
            SaveForced();
        }

        private void SaveForced()
        {
            _data.State.ForcedOn = _forcedOn.ToList();
            _data.State.ForcedOff = _forcedOff.ToList();
            SaveState();
        }

        public string ForceState(string name) =>
            _forcedOn.Contains(name) ? "forced on" : _forcedOff.Contains(name) ? "forced off" : "";

        /// <summary>When the zone next runs, for !koth.</summary>
        public string NextRunText(ZoneRuntime zone, DateTime now)
        {
            if (zone.Active)
                return "OPEN now";
            if (zone.Profile.Activation == ZoneActivation.AlwaysOn)
                return "always on";

            var times = Config.Slots
                .Where(s => s.Enabled && s.Pool.Any(p => string.Equals(p, zone.Name, StringComparison.OrdinalIgnoreCase)))
                .Select(s => (Slot: s, Next: WeeklySchedule.NextStart(s.Start, now)))
                .OrderBy(x => x.Next)
                .ToList();
            if (times.Count == 0)
                return "not scheduled";

            var first = times[0];
            string chance = first.Slot.Pool.Count > 1 ? $" (1 of {first.Slot.Pool.Count} picked at random)" : "";
            return $"next possible {first.Next:ddd HH:mm} UTC{chance}";
        }
    }
}
