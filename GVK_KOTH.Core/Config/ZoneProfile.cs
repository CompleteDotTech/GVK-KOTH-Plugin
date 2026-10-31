using System.Collections.Generic;
using System.Xml.Serialization;

namespace GVK.Koth.Core.Config
{
    public enum ZoneMode
    {
        /// <summary>Shared capture: every faction in the zone fills one bar and scores when it fills.</summary>
        Capture,

        /// <summary>Factions with a powered station inside fill the bar at a flat speed.</summary>
        Encampment,

        /// <summary>No capture; only the rules below apply.</summary>
        RulesOnly
    }

    public enum ZoneAttach
    {
        /// <summary>Fixed coordinates (X, Y, Z).</summary>
        Coordinates,

        /// <summary>An NPC grid whose remote control CustomData has "GVK-KOTH=&lt;Name&gt;;" (from its MES behavior profile).</summary>
        NpcTag
    }

    public enum ZoneActivation
    {
        /// <summary>Runs all the time.</summary>
        AlwaysOn,

        /// <summary>Runs only while a slot has picked it.</summary>
        Slot,

        /// <summary>NPC zones: runs from the grid's spawn for LifetimeMinutes (0 = while the grid exists).</summary>
        Lifetime
    }

    /// <summary>
    /// One KOTH zone. Settings are copied from the King of the Hill mod where they existed.
    /// </summary>
    public class ZoneProfile
    {
        private readonly HashSet<string> _specified = new HashSet<string>();
        private bool _inheritedLoot;

        public string Name { get; set; } = "";
        private bool _enabled = true;
        public bool Enabled
        {
            get { return _enabled; }
            set { _enabled = value; _specified.Add(nameof(Enabled)); }
        }
        private ZoneMode _mode = ZoneMode.Capture;
        public ZoneMode Mode
        {
            get { return _mode; }
            set { _mode = value; _specified.Add(nameof(Mode)); }
        }
        private ZoneAttach _attach = ZoneAttach.Coordinates;
        public ZoneAttach Attach
        {
            get { return _attach; }
            set { _attach = value; _specified.Add(nameof(Attach)); }
        }
        private ZoneActivation _activation = ZoneActivation.Slot;
        public ZoneActivation Activation
        {
            get { return _activation; }
            set { _activation = value; _specified.Add(nameof(Activation)); }
        }

        private double _x;
        public double X
        {
            get { return _x; }
            set { _x = value; _specified.Add(nameof(X)); }
        }
        private double _y;
        public double Y
        {
            get { return _y; }
            set { _y = value; _specified.Add(nameof(Y)); }
        }
        private double _z;
        public double Z
        {
            get { return _z; }
            set { _z = value; _specified.Add(nameof(Z)); }
        }
        private double _radius = 1500;
        public double Radius
        {
            get { return _radius; }
            set { _radius = value; _specified.Add(nameof(Radius)); }
        }

        /// <summary>NPC zones: minutes the zone runs after the grid spawns (0 = while the grid exists).</summary>
        private int _lifetimeMinutes;
        public int LifetimeMinutes
        {
            get { return _lifetimeMinutes; }
            set { _lifetimeMinutes = value; _specified.Add(nameof(LifetimeMinutes)); }
        }

        /// <summary>Optional MES spawn group spawned when a slot picks this zone (Flying KOTH).</summary>
        private string _spawnGroup = "";
        public string SpawnGroup
        {
            get { return _spawnGroup; }
            set { _spawnGroup = value; _specified.Add(nameof(SpawnGroup)); }
        }

        // --- Who counts ---
        private bool _onFoot;
        public bool OnFoot
        {
            get { return _onFoot; }
            set { _onFoot = value; _specified.Add(nameof(OnFoot)); }
        }
        private bool _smallGrids = true;
        public bool SmallGrids
        {
            get { return _smallGrids; }
            set { _smallGrids = value; _specified.Add(nameof(SmallGrids)); }
        }
        private bool _largeGrids = true;
        public bool LargeGrids
        {
            get { return _largeGrids; }
            set { _largeGrids = value; _specified.Add(nameof(LargeGrids)); }
        }
        private int _minSmallGridBlocks = 50;
        public int MinSmallGridBlocks
        {
            get { return _minSmallGridBlocks; }
            set { _minSmallGridBlocks = value; _specified.Add(nameof(MinSmallGridBlocks)); }
        }
        private int _minLargeGridBlocks = 100;
        public int MinLargeGridBlocks
        {
            get { return _minLargeGridBlocks; }
            set { _minLargeGridBlocks = value; _specified.Add(nameof(MinLargeGridBlocks)); }
        }

        /// <summary>Eligible players needed before the bar moves.</summary>
        private int _minPlayers = 1;
        public int MinPlayers
        {
            get { return _minPlayers; }
            set { _minPlayers = value; _specified.Add(nameof(MinPlayers)); }
        }

        // --- Capture ---
        /// <summary>Seconds to fill the bar with one player (faster with more, see CaptureCurve).</summary>
        private double _captureSeconds = 300;
        public double CaptureSeconds
        {
            get { return _captureSeconds; }
            set { _captureSeconds = value; _specified.Add(nameof(CaptureSeconds)); }
        }

        /// <summary>The empty zone drains at this many players' speed (old IdleDrainRate). 0 = no drain.</summary>
        private int _idleDrainPlayers = 3;
        public int IdleDrainPlayers
        {
            get { return _idleDrainPlayers; }
            set { _idleDrainPlayers = value; _specified.Add(nameof(IdleDrainPlayers)); }
        }

        /// <summary>Share of the bar (0-1) a faction must have been present for to score when it fills. 0 = off.</summary>
        private double _minPresenceShare;
        public double MinPresenceShare
        {
            get { return _minPresenceShare; }
            set { _minPresenceShare = value; _specified.Add(nameof(MinPresenceShare)); }
        }

        // --- Rewards ---
        private int _pointsPerCompletion = 5;
        public int PointsPerCompletion
        {
            get { return _pointsPerCompletion; }
            set { _pointsPerCompletion = value; _specified.Add(nameof(PointsPerCompletion)); }
        }
        private long _creditsPerPoint = 200000;
        public long CreditsPerPoint
        {
            get { return _creditsPerPoint; }
            set { _creditsPerPoint = value; _specified.Add(nameof(CreditsPerPoint)); }
        }

        /// <summary>Zone points (all factions together) per loot drop. 0 = no loot.</summary>
        private int _pointsPerLoot = 10;
        public int PointsPerLoot
        {
            get { return _pointsPerLoot; }
            set { _pointsPerLoot = value; _specified.Add(nameof(PointsPerLoot)); }
        }
        private List<LootItem> _loot = new List<LootItem>();
        public List<LootItem> Loot
        {
            get { return _loot; }
            set { _loot = value; _inheritedLoot = false; _specified.Add(nameof(Loot)); }
        }

        /// <summary>Name of the public container inside the zone that receives the loot.</summary>
        private string _lootContainerName = "KOTH Loot";
        public string LootContainerName
        {
            get { return _lootContainerName; }
            set { _lootContainerName = value; _specified.Add(nameof(LootContainerName)); }
        }

        // --- Rules (0 = off), enforced around the zone all the time ---
        private double _noThrusterRange;
        public double NoThrusterRange
        {
            get { return _noThrusterRange; }
            set { _noThrusterRange = value; _specified.Add(nameof(NoThrusterRange)); }
        }
        private double _noLargeGridPowerRange;
        public double NoLargeGridPowerRange
        {
            get { return _noLargeGridPowerRange; }
            set { _noLargeGridPowerRange = value; _specified.Add(nameof(NoLargeGridPowerRange)); }
        }
        private double _noSafeZoneRange;
        public double NoSafeZoneRange
        {
            get { return _noSafeZoneRange; }
            set { _noSafeZoneRange = value; _specified.Add(nameof(NoSafeZoneRange)); }
        }
        private double _noProjectorRange;
        public double NoProjectorRange
        {
            get { return _noProjectorRange; }
            set { _noProjectorRange = value; _specified.Add(nameof(NoProjectorRange)); }
        }

        /// <summary>NPC blocks tagged [GVK-KOTH-Link] within this range switch on while the zone runs. 0 = off.</summary>
        private double _linkRange;
        public double LinkRange
        {
            get { return _linkRange; }
            set { _linkRange = value; _specified.Add(nameof(LinkRange)); }
        }
        // Omitted and inherited values must stay omitted across XML round trips.
        public bool ShouldSerializeEnabled() { return _specified.Contains(nameof(Enabled)); }
        public bool ShouldSerializeMode() { return _specified.Contains(nameof(Mode)); }
        public bool ShouldSerializeAttach() { return _specified.Contains(nameof(Attach)); }
        public bool ShouldSerializeActivation() { return _specified.Contains(nameof(Activation)); }
        public bool ShouldSerializeX() { return _specified.Contains(nameof(X)); }
        public bool ShouldSerializeY() { return _specified.Contains(nameof(Y)); }
        public bool ShouldSerializeZ() { return _specified.Contains(nameof(Z)); }
        public bool ShouldSerializeRadius() { return _specified.Contains(nameof(Radius)); }
        public bool ShouldSerializeLifetimeMinutes() { return _specified.Contains(nameof(LifetimeMinutes)); }
        public bool ShouldSerializeSpawnGroup() { return _specified.Contains(nameof(SpawnGroup)); }
        public bool ShouldSerializeOnFoot() { return _specified.Contains(nameof(OnFoot)); }
        public bool ShouldSerializeSmallGrids() { return _specified.Contains(nameof(SmallGrids)); }
        public bool ShouldSerializeLargeGrids() { return _specified.Contains(nameof(LargeGrids)); }
        public bool ShouldSerializeMinSmallGridBlocks() { return _specified.Contains(nameof(MinSmallGridBlocks)); }
        public bool ShouldSerializeMinLargeGridBlocks() { return _specified.Contains(nameof(MinLargeGridBlocks)); }
        public bool ShouldSerializeMinPlayers() { return _specified.Contains(nameof(MinPlayers)); }
        public bool ShouldSerializeCaptureSeconds() { return _specified.Contains(nameof(CaptureSeconds)); }
        public bool ShouldSerializeIdleDrainPlayers() { return _specified.Contains(nameof(IdleDrainPlayers)); }
        public bool ShouldSerializeMinPresenceShare() { return _specified.Contains(nameof(MinPresenceShare)); }
        public bool ShouldSerializePointsPerCompletion() { return _specified.Contains(nameof(PointsPerCompletion)); }
        public bool ShouldSerializeCreditsPerPoint() { return _specified.Contains(nameof(CreditsPerPoint)); }
        public bool ShouldSerializePointsPerLoot() { return _specified.Contains(nameof(PointsPerLoot)); }
        public bool ShouldSerializeLootContainerName() { return _specified.Contains(nameof(LootContainerName)); }
        public bool ShouldSerializeNoThrusterRange() { return _specified.Contains(nameof(NoThrusterRange)); }
        public bool ShouldSerializeNoLargeGridPowerRange() { return _specified.Contains(nameof(NoLargeGridPowerRange)); }
        public bool ShouldSerializeNoSafeZoneRange() { return _specified.Contains(nameof(NoSafeZoneRange)); }
        public bool ShouldSerializeNoProjectorRange() { return _specified.Contains(nameof(NoProjectorRange)); }
        public bool ShouldSerializeLinkRange() { return _specified.Contains(nameof(LinkRange)); }

        // XmlSerializer may populate an existing list without invoking its setter.
        // The Specified convention records even an explicitly empty <Loot/>.
        [XmlIgnore]
        public bool LootSpecified
        {
            get { return _specified.Contains(nameof(Loot)) || (!_inheritedLoot && _loot != null && _loot.Count != 0); }
            set
            {
                if (value) _specified.Add(nameof(Loot));
                else _specified.Remove(nameof(Loot));
            }
        }

        internal void ApplyDefaultsFrom(ZoneProfile defaults)
        {
            if (defaults == null) return;
            if (!_specified.Contains(nameof(Enabled)) && defaults._specified.Contains(nameof(Enabled)))
                _enabled = defaults._enabled;
            if (!_specified.Contains(nameof(Mode)) && defaults._specified.Contains(nameof(Mode)))
                _mode = defaults._mode;
            if (!_specified.Contains(nameof(Attach)) && defaults._specified.Contains(nameof(Attach)))
                _attach = defaults._attach;
            if (!_specified.Contains(nameof(Activation)) && defaults._specified.Contains(nameof(Activation)))
                _activation = defaults._activation;
            if (!_specified.Contains(nameof(X)) && defaults._specified.Contains(nameof(X)))
                _x = defaults._x;
            if (!_specified.Contains(nameof(Y)) && defaults._specified.Contains(nameof(Y)))
                _y = defaults._y;
            if (!_specified.Contains(nameof(Z)) && defaults._specified.Contains(nameof(Z)))
                _z = defaults._z;
            if (!_specified.Contains(nameof(Radius)) && defaults._specified.Contains(nameof(Radius)))
                _radius = defaults._radius;
            if (!_specified.Contains(nameof(LifetimeMinutes)) && defaults._specified.Contains(nameof(LifetimeMinutes)))
                _lifetimeMinutes = defaults._lifetimeMinutes;
            if (!_specified.Contains(nameof(SpawnGroup)) && defaults._specified.Contains(nameof(SpawnGroup)))
                _spawnGroup = defaults._spawnGroup;
            if (!_specified.Contains(nameof(OnFoot)) && defaults._specified.Contains(nameof(OnFoot)))
                _onFoot = defaults._onFoot;
            if (!_specified.Contains(nameof(SmallGrids)) && defaults._specified.Contains(nameof(SmallGrids)))
                _smallGrids = defaults._smallGrids;
            if (!_specified.Contains(nameof(LargeGrids)) && defaults._specified.Contains(nameof(LargeGrids)))
                _largeGrids = defaults._largeGrids;
            if (!_specified.Contains(nameof(MinSmallGridBlocks)) && defaults._specified.Contains(nameof(MinSmallGridBlocks)))
                _minSmallGridBlocks = defaults._minSmallGridBlocks;
            if (!_specified.Contains(nameof(MinLargeGridBlocks)) && defaults._specified.Contains(nameof(MinLargeGridBlocks)))
                _minLargeGridBlocks = defaults._minLargeGridBlocks;
            if (!_specified.Contains(nameof(MinPlayers)) && defaults._specified.Contains(nameof(MinPlayers)))
                _minPlayers = defaults._minPlayers;
            if (!_specified.Contains(nameof(CaptureSeconds)) && defaults._specified.Contains(nameof(CaptureSeconds)))
                _captureSeconds = defaults._captureSeconds;
            if (!_specified.Contains(nameof(IdleDrainPlayers)) && defaults._specified.Contains(nameof(IdleDrainPlayers)))
                _idleDrainPlayers = defaults._idleDrainPlayers;
            if (!_specified.Contains(nameof(MinPresenceShare)) && defaults._specified.Contains(nameof(MinPresenceShare)))
                _minPresenceShare = defaults._minPresenceShare;
            if (!_specified.Contains(nameof(PointsPerCompletion)) && defaults._specified.Contains(nameof(PointsPerCompletion)))
                _pointsPerCompletion = defaults._pointsPerCompletion;
            if (!_specified.Contains(nameof(CreditsPerPoint)) && defaults._specified.Contains(nameof(CreditsPerPoint)))
                _creditsPerPoint = defaults._creditsPerPoint;
            if (!_specified.Contains(nameof(PointsPerLoot)) && defaults._specified.Contains(nameof(PointsPerLoot)))
                _pointsPerLoot = defaults._pointsPerLoot;
            // A caller can also populate the built-in list directly before applying defaults.
            if (!_inheritedLoot && _loot != null && _loot.Count != 0)
                _specified.Add(nameof(Loot));
            if (!_specified.Contains(nameof(Loot)) && defaults.LootSpecified)
            {
                _loot = defaults._loot == null ? null : defaults._loot.ConvertAll(item =>
                    item == null ? null : new LootItem
                    {
                        TypeId = item.TypeId,
                        SubtypeId = item.SubtypeId,
                        Amount = item.Amount
                    });
                _inheritedLoot = true;
            }
            if (!_specified.Contains(nameof(LootContainerName)) && defaults._specified.Contains(nameof(LootContainerName)))
                _lootContainerName = defaults._lootContainerName;
            if (!_specified.Contains(nameof(NoThrusterRange)) && defaults._specified.Contains(nameof(NoThrusterRange)))
                _noThrusterRange = defaults._noThrusterRange;
            if (!_specified.Contains(nameof(NoLargeGridPowerRange)) && defaults._specified.Contains(nameof(NoLargeGridPowerRange)))
                _noLargeGridPowerRange = defaults._noLargeGridPowerRange;
            if (!_specified.Contains(nameof(NoSafeZoneRange)) && defaults._specified.Contains(nameof(NoSafeZoneRange)))
                _noSafeZoneRange = defaults._noSafeZoneRange;
            if (!_specified.Contains(nameof(NoProjectorRange)) && defaults._specified.Contains(nameof(NoProjectorRange)))
                _noProjectorRange = defaults._noProjectorRange;
            if (!_specified.Contains(nameof(LinkRange)) && defaults._specified.Contains(nameof(LinkRange)))
                _linkRange = defaults._linkRange;
        }
    }

    public class LootItem
    {
        /// <summary>Object builder type without the prefix, e.g. Component, Ingot, Ore.</summary>
        public string TypeId { get; set; } = "Component";
        public string SubtypeId { get; set; } = "";
        public int Amount { get; set; } = 1;
    }
}
