using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace GVK.Koth.Core
{
    public sealed class FactionScore
    {
        public long FactionId { get; set; }
        public string Tag { get; set; } = "";
        public string Name { get; set; } = "";
        public int Points { get; set; }

        /// <summary>Points at the last weekly roll-up, for "gain this week".</summary>
        public int PointsAtLastRollup { get; set; }
    }

    /// <summary>Season totals file. Delete it (server stopped) to start a new season.</summary>
    public sealed class ScoreData
    {
        public List<FactionScore> Factions { get; set; } = new List<FactionScore>();
        public DateTime LastRollupUtc { get; set; }
    }

    public sealed class ScoreLogEntry
    {
        public const string CsvHeader = "TimeUtc,FactionId,Tag,Change,Source,Reason,NewTotal";

        public DateTime TimeUtc;
        public long FactionId;
        public string Tag;
        public int Change;
        public string Source;
        public string Reason;
        public int NewTotal;

        public string ToCsv()
        {
            return string.Join(",",
                TimeUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                FactionId.ToString(CultureInfo.InvariantCulture),
                Csv(Tag),
                Change.ToString(CultureInfo.InvariantCulture),
                Csv(Source),
                Csv(Reason),
                NewTotal.ToString(CultureInfo.InvariantCulture));
        }

        private static string Csv(string value)
        {
            value = value ?? "";
            if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0)
                return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }

    /// <summary>
    /// Season points per faction. Totals never go below 0; every change returns a log entry.
    /// </summary>
    public sealed class ScoreBook
    {
        public ScoreBook(ScoreData data = null)
        {
            Data = data ?? new ScoreData();
        }

        public ScoreData Data { get; }

        public FactionScore Get(long factionId) => Data.Factions.FirstOrDefault(f => f.FactionId == factionId);

        /// <summary>
        /// Adds (or subtracts) points, saturating valid totals between 0 and int.MaxValue.
        /// Rejects a negative stored total; the returned change is the actual total delta.
        /// </summary>
        public ScoreLogEntry Add(long factionId, string tag, string name, int change, string source, string reason, DateTime utcNow)
        {
            var score = Get(factionId);
            if (score == null)
            {
                score = new FactionScore { FactionId = factionId };
                Data.Factions.Add(score);
            }

            if (score.Points < 0)
                throw new InvalidOperationException("Cannot adjust a negative stored season total.");

            if (!string.IsNullOrEmpty(tag)) score.Tag = tag;
            if (!string.IsNullOrEmpty(name)) score.Name = name;

            long desired = (long)score.Points + change;
            int total = (int)Math.Max(0L, Math.Min((long)int.MaxValue, desired));
            int applied = (int)((long)total - score.Points);
            score.Points = total;

            return new ScoreLogEntry
            {
                TimeUtc = utcNow,
                FactionId = factionId,
                Tag = score.Tag,
                Change = applied,
                Source = source,
                Reason = reason,
                NewTotal = score.Points
            };
        }

        /// <summary>Factions by points, highest first, with points gained since the last roll-up.</summary>
        public List<(FactionScore Score, int GainSinceRollup)> Standings()
        {
            return Data.Factions
                .Where(f => f.Points > 0 || f.PointsAtLastRollup > 0)
                .OrderByDescending(f => f.Points)
                .ThenBy(f => f.Tag)
                .Select(f => (f, f.Points - f.PointsAtLastRollup))
                .ToList();
        }

        /// <summary>Records the current totals as the start of the next week.</summary>
        public void MarkRollup(DateTime utcNow)
        {
            foreach (var f in Data.Factions)
                f.PointsAtLastRollup = f.Points;
            Data.LastRollupUtc = utcNow;
        }
    }
}
