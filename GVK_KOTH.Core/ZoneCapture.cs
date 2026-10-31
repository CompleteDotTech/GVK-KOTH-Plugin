using System;
using System.Collections.Generic;
using System.Linq;

namespace GVK.Koth.Core
{
    /// <summary>
    /// Capture settings one zone needs; filled from its ZoneProfile.
    /// </summary>
    public sealed class CaptureRules
    {
        public double CaptureSeconds = 300;
        public int MinPlayers = 1;
        public int IdleDrainPlayers = 3;
        public double MinPresenceShare;
        public int PointsPerCompletion = 5;
        public int PointsPerLoot = 10;

        /// <summary>Encampment: the bar fills at 1x no matter how many factions are present.</summary>
        public bool FlatSpeed;
    }

    public sealed class CaptureResult
    {
        public static readonly CaptureResult None = new CaptureResult();

        public bool Completed;

        /// <summary>Factions that score this completion, each PointsEach points.</summary>
        public List<long> ScoringFactions = new List<long>();
        public int PointsEach;

        /// <summary>Loot drops earned this completion.</summary>
        public int LootDrops;
    }

    /// <summary>
    /// The shared capture bar of one zone. Every eligible player, from any faction, fills the same bar;
    /// when it fills, every faction present (and present long enough) scores, and the bar starts again.
    /// </summary>
    public sealed class ZoneCapture
    {
        private readonly CaptureRules _rules;

        // Bar progress each faction was present for, for MinPresenceShare.
        private readonly Dictionary<long, double> _factionProgress = new Dictionary<long, double>();

        public ZoneCapture(CaptureRules rules)
        {
            _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        }

        /// <summary>Bar progress in seconds of one-player capture, 0 to CaptureSeconds.</summary>
        public double Progress { get; set; }

        public double Fraction => _rules.CaptureSeconds > 0 ? Progress / _rules.CaptureSeconds : 0;

        /// <summary>Zone points earned toward the next loot drop.</summary>
        public int LootCounter { get; set; }

        /// <summary>Returns a detached copy of faction contributions to the current bar.</summary>
        public Dictionary<long, double> SnapshotFactionProgress()
        {
            return new Dictionary<long, double>(_factionProgress);
        }

        /// <summary>Restores valid faction contributions for the already restored bar.</summary>
        public void RestoreFactionProgress(IEnumerable<KeyValuePair<long, double>> restored)
        {
            _factionProgress.Clear();
            if (restored == null || double.IsNaN(Progress) || double.IsInfinity(Progress) || Progress < 0)
                return;

            foreach (var entry in restored)
            {
                double contribution = entry.Value;
                if (double.IsNaN(contribution) || double.IsInfinity(contribution) || contribution < 0)
                    continue;

                _factionProgress[entry.Key] = Math.Min(contribution, Progress);
            }
        }

        /// <summary>
        /// Advances the bar by dtSeconds.
        /// </summary>
        /// <param name="participants">The faction of each eligible participant (a player, or a station in Encampment mode).</param>
        public CaptureResult Tick(double dtSeconds, IReadOnlyCollection<long> participants)
        {
            if (dtSeconds <= 0 || _rules.CaptureSeconds <= 0)
                return CaptureResult.None;

            int count = participants?.Count ?? 0;
            if (count == 0 || count < _rules.MinPlayers)
            {
                Drain(dtSeconds);
                return CaptureResult.None;
            }

            double gain = dtSeconds * (_rules.FlatSpeed ? 1.0 : CaptureCurve.Speed(count));
            Progress += gain;

            var present = participants.Distinct().ToList();
            foreach (long faction in present)
            {
                _factionProgress.TryGetValue(faction, out double had);
                _factionProgress[faction] = had + gain;
            }

            if (Progress < _rules.CaptureSeconds)
                return CaptureResult.None;

            return Complete(present);
        }

        public void Reset()
        {
            Progress = 0;
            _factionProgress.Clear();
        }

        private void Drain(double dtSeconds)
        {
            if (Progress <= 0)
                return;

            Progress -= dtSeconds * CaptureCurve.Speed(_rules.IdleDrainPlayers);
            if (Progress <= 0)
            {
                Reset();
                return;
            }

            // A faction can't have been present for more of the bar than is left.
            foreach (long faction in _factionProgress.Keys.ToList())
                _factionProgress[faction] = Math.Min(_factionProgress[faction], Progress);
        }

        private CaptureResult Complete(List<long> present)
        {
            double needed = _rules.MinPresenceShare * _rules.CaptureSeconds;
            var result = new CaptureResult
            {
                Completed = true,
                PointsEach = _rules.PointsPerCompletion,
                ScoringFactions = present
                    .Where(f => _factionProgress.TryGetValue(f, out double p) && p + 1e-6 >= needed)
                    .ToList()
            };

            if (_rules.PointsPerLoot > 0)
            {
                LootCounter += result.PointsEach * result.ScoringFactions.Count;
                result.LootDrops = LootCounter / _rules.PointsPerLoot;
                LootCounter %= _rules.PointsPerLoot;
            }

            Reset();
            return result;
        }
    }
}
