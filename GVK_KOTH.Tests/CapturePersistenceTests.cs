using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using GVK.Koth.Core;
using Xunit;

namespace GVK.Koth.Tests
{
    public class CapturePersistenceTests
    {
        private static CaptureRules Rules() => new CaptureRules
        {
            CaptureSeconds = 100,
            MinPresenceShare = 0.25,
            MinPlayers = 1,
            IdleDrainPlayers = 1,
            PointsPerCompletion = 5,
            PointsPerLoot = 10,
            FlatSpeed = true
        };

        private static string Serialize(KothState state)
        {
            var serializer = new XmlSerializer(typeof(KothState));
            using (var writer = new StringWriter())
            {
                serializer.Serialize(writer, state);
                return writer.ToString();
            }
        }

        private static KothState Deserialize(string xml)
        {
            var serializer = new XmlSerializer(typeof(KothState));
            using (var reader = new StringReader(xml))
                return (KothState)serializer.Deserialize(reader);
        }

        // Use the public production state and capture APIs at the persistence boundary.
        private static ZoneSave Save(ZoneCapture capture, int pendingLoot) => new ZoneSave
        {
            Name = "zone",
            Progress = capture.Progress,
            LootCounter = capture.LootCounter,
            PendingLootDrops = pendingLoot,
            FactionProgressSpecified = true,
            FactionProgress = capture.SnapshotFactionProgress()
                .Select(p => new FactionProgressSave { FactionId = p.Key, Progress = p.Value }).ToList()
        };

        private static ZoneCapture Restore(ZoneSave save)
        {
            var capture = new ZoneCapture(Rules()) { Progress = save.Progress, LootCounter = save.LootCounter };
            capture.RestoreFactionProgress(save.FactionProgress.Select(p =>
                new KeyValuePair<long, double>(p.FactionId, p.Progress)));
            return capture;
        }

        private static ZoneSave RoundTrip(ZoneSave save) =>
            Deserialize(Serialize(new KothState { Zones = new List<ZoneSave> { save } })).Zones[0];

        [Fact]
        public void Restart_after_ninety_preserves_completion_reward_and_loot()
        {
            var uninterrupted = new ZoneCapture(Rules()) { LootCounter = 5 };
            var beforeRestart = new ZoneCapture(Rules()) { LootCounter = 5 };
            Assert.False(uninterrupted.Tick(90, new long[] { 1 }).Completed);
            Assert.False(beforeRestart.Tick(90, new long[] { 1 }).Completed);
            var saved = RoundTrip(Save(beforeRestart, 3));
            Assert.True(saved.FactionProgressSpecified);
            Assert.Single(saved.FactionProgress);
            Assert.Equal(1L, saved.FactionProgress[0].FactionId);
            Assert.Equal(90, saved.FactionProgress[0].Progress);
            Assert.Equal(90, saved.Progress);
            Assert.Equal(5, saved.LootCounter);
            Assert.Equal(3, saved.PendingLootDrops);
            var restarted = Restore(saved);

            var expected = uninterrupted.Tick(10, new long[] { 1 });
            var actual = restarted.Tick(10, new long[] { 1 });
            Assert.True(expected.Completed);
            Assert.True(actual.Completed);
            Assert.Equal(new long[] { 1 }, actual.ScoringFactions);
            Assert.Equal(expected.ScoringFactions, actual.ScoringFactions);
            Assert.Equal(5, actual.PointsEach);
            Assert.Equal(expected.PointsEach, actual.PointsEach);
            Assert.Equal(1, actual.LootDrops);
            Assert.Equal(expected.LootDrops, actual.LootDrops);
            Assert.Equal(uninterrupted.LootCounter, restarted.LootCounter);
            Assert.Equal(0, restarted.Progress);
            Assert.Empty(restarted.SnapshotFactionProgress());
        }

        [Fact]
        public void Restart_keeps_multiple_credits_but_late_and_departed_factions_do_not_score()
        {
            var original = new ZoneCapture(Rules());
            original.Tick(60, new long[] { 1, 2, 3 });
            original.Tick(30, new long[] { 1, 2 });
            var saved = RoundTrip(Save(original, 2));
            Assert.True(saved.FactionProgressSpecified);
            Assert.Equal(3, saved.FactionProgress.Count);
            Assert.Equal(90, saved.FactionProgress.Single(p => p.FactionId == 1).Progress);
            Assert.Equal(90, saved.FactionProgress.Single(p => p.FactionId == 2).Progress);
            Assert.Equal(60, saved.FactionProgress.Single(p => p.FactionId == 3).Progress);
            var restarted = Restore(saved);
            var expected = original.Tick(10, new long[] { 1, 2, 4 });
            var actual = restarted.Tick(10, new long[] { 1, 2, 4 });

            Assert.True(actual.Completed);
            Assert.Equal(new long[] { 1, 2 }, actual.ScoringFactions);
            Assert.Equal(expected.ScoringFactions, actual.ScoringFactions);
            Assert.DoesNotContain(3L, actual.ScoringFactions); // departed despite sufficient credit
            Assert.DoesNotContain(4L, actual.ScoringFactions); // genuinely arrived for the last 10
            Assert.Equal(1, actual.LootDrops);
            Assert.Equal(expected.LootDrops, actual.LootDrops);
            Assert.Equal(2, saved.PendingLootDrops);
        }

        [Fact]
        public void Snapshots_and_restored_input_are_detached_and_invalid_credit_is_not_invented()
        {
            var capture = new ZoneCapture(Rules());
            capture.Tick(40, new long[] { 1 });
            var snapshot = capture.SnapshotFactionProgress();
            snapshot[1] = 5;
            snapshot[2] = 100;
            Assert.Equal(40, capture.SnapshotFactionProgress()[1]);
            Assert.False(capture.SnapshotFactionProgress().ContainsKey(2));

            var input = new Dictionary<long, double>
            {
                [1] = 25, [2] = 100, [3] = -1,
                [4] = double.NaN, [5] = double.PositiveInfinity
            };
            capture.RestoreFactionProgress(input);
            input[1] = 0;
            input[6] = 20;
            var restored = capture.SnapshotFactionProgress();
            Assert.Equal(25, restored[1]);
            Assert.Equal(40, restored[2]);
            Assert.False(restored.ContainsKey(3));
            Assert.False(restored.ContainsKey(4));
            Assert.False(restored.ContainsKey(5));
            Assert.False(restored.ContainsKey(6));
            capture.RestoreFactionProgress(null);
            Assert.Empty(capture.SnapshotFactionProgress());
        }

        [Fact]
        public void Drain_and_reset_clear_or_bound_restored_credit()
        {
            var initial = new ZoneCapture(Rules());
            initial.Tick(60, new long[] { 1, 2 });
            var capture = Restore(RoundTrip(Save(initial, 0)));
            capture.Tick(20, new long[0]);
            Assert.Equal(40, capture.Progress);
            Assert.Equal(40, capture.SnapshotFactionProgress()[1]);
            Assert.Equal(40, capture.SnapshotFactionProgress()[2]);
            capture.Reset();
            Assert.Equal(0, capture.Progress);
            Assert.Empty(capture.SnapshotFactionProgress());
            capture.Tick(20, new long[] { 1 });
            capture.Tick(25, new long[0]);
            Assert.Equal(0, capture.Progress);
            Assert.Empty(capture.SnapshotFactionProgress());
        }

        [Fact]
        public void Legacy_absent_and_explicit_empty_xml_remain_distinct_and_keep_other_fields()
        {
            const string legacy = "<KothState><Zones><ZoneSave><Name>zone</Name><Progress>90</Progress>" +
                "<LootCounter>7</LootCounter><PendingLootDrops>3</PendingLootDrops></ZoneSave></Zones></KothState>";
            const string knownEmpty = "<KothState><Zones><ZoneSave><Name>zone</Name><Progress>90</Progress>" +
                "<LootCounter>7</LootCounter><FactionProgress /><PendingLootDrops>3</PendingLootDrops></ZoneSave></Zones></KothState>";
            var oldSave = Deserialize(legacy).Zones[0];
            var emptySave = Deserialize(knownEmpty).Zones[0];
            Assert.False(oldSave.FactionProgressSpecified);
            Assert.True(emptySave.FactionProgressSpecified);
            Assert.NotNull(emptySave.FactionProgress);
            Assert.Empty(emptySave.FactionProgress);
            // A reader may initialize the collection even when the XML element is absent.
            oldSave.FactionProgress = new List<FactionProgressSave>();
            var legacyXml = Serialize(new KothState { Zones = new List<ZoneSave> { oldSave } });
            Assert.DoesNotContain("<FactionProgress", legacyXml);
            var roundTrippedLegacy = Deserialize(legacyXml).Zones[0];
            Assert.False(roundTrippedLegacy.FactionProgressSpecified);
            var roundTrippedEmpty = RoundTrip(emptySave);
            Assert.True(roundTrippedEmpty.FactionProgressSpecified);
            Assert.NotNull(roundTrippedEmpty.FactionProgress);
            Assert.Empty(roundTrippedEmpty.FactionProgress);
            foreach (var save in new[] { oldSave, roundTrippedLegacy, emptySave, roundTrippedEmpty })
            {
                Assert.Equal("zone", save.Name);
                Assert.Equal(90, save.Progress);
                Assert.Equal(7, save.LootCounter);
                Assert.Equal(3, save.PendingLootDrops);
            }
        }
    }
}
