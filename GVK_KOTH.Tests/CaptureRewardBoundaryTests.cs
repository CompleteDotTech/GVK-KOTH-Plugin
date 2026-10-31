using System;
using System.Collections.Generic;
using GVK.Koth.Core;
using Xunit;

namespace GVK.Koth.Tests
{
    public class CaptureRewardBoundaryTests
    {
        private static readonly long[] TwoFactions = { 11, 22 };

        private static CaptureRules Rules() => new CaptureRules
        {
            CaptureSeconds = 10,
            FlatSpeed = true,
            MinPlayers = 1,
            MinPresenceShare = 0.5,
            PointsPerCompletion = 1500000000,
            PointsPerLoot = 1
        };

        [Fact]
        public void Two_large_faction_rewards_produce_exact_long_drop_count()
        {
            var zone = new ZoneCapture(Rules());
            var result = zone.Tick(10, TwoFactions, long.MaxValue);

            Assert.True(result.Completed);
            Assert.Equal(new long[] { 11, 22 }, result.ScoringFactions);
            Assert.Equal(1500000000, result.PointsEach);
            Assert.Equal(3000000000L, result.LootDrops);
            Assert.Equal(0, zone.LootCounter);
            Assert.Equal(0, zone.Progress);
            Assert.Empty(zone.SnapshotFactionProgress());

            var next = zone.Tick(1, TwoFactions, long.MaxValue);
            Assert.False(next.Completed);
            Assert.Equal(0L, next.LootDrops);
            Assert.Equal(1, zone.Progress);
        }

        [Fact]
        public void Large_product_and_existing_counter_conserve_remainder_across_completions()
        {
            var rules = Rules();
            rules.PointsPerLoot = 7;
            var zone = new ZoneCapture(rules) { LootCounter = 5 };

            var first = zone.Tick(10, TwoFactions);
            Assert.True(first.Completed);
            Assert.Equal(428571429L, first.LootDrops);
            Assert.Equal(2, zone.LootCounter);

            var second = zone.Tick(10, TwoFactions);
            Assert.True(second.Completed);
            Assert.Equal(428571428L, second.LootDrops);
            Assert.Equal(6, zone.LootCounter);
            Assert.Equal(6000000005L, (first.LootDrops + second.LootDrops) * 7 + zone.LootCounter);
        }

        [Fact]
        public void Insufficient_and_zero_headroom_hold_one_completion_until_it_fits()
        {
            var rules = Rules();
            rules.PointsPerLoot = 7;
            var zone = new ZoneCapture(rules) { LootCounter = 5 };
            Assert.False(zone.Tick(3, TwoFactions, 0).Completed);

            var blocked = zone.Tick(7, TwoFactions, 428571428L);
            AssertHeld(zone, blocked);
            AssertHeld(zone, zone.Tick(100, TwoFactions, 0));
            AssertHeld(zone, zone.Tick(100, TwoFactions, 428571428L));

            var completed = zone.Tick(1, TwoFactions, 428571429L);
            Assert.True(completed.Completed);
            Assert.Equal(new long[] { 11, 22 }, completed.ScoringFactions);
            Assert.Equal(1500000000, completed.PointsEach);
            Assert.Equal(428571429L, completed.LootDrops);
            Assert.Equal(2, zone.LootCounter);
            Assert.Equal(0, zone.Progress);
            Assert.Empty(zone.SnapshotFactionProgress());

            var next = zone.Tick(1, TwoFactions, long.MaxValue);
            Assert.False(next.Completed);
            Assert.Equal(0L, next.LootDrops);
            Assert.Equal(2, zone.LootCounter);
            Assert.Equal(1, zone.Progress);
        }

        private static void AssertHeld(ZoneCapture zone, CaptureResult result)
        {
            Assert.False(result.Completed);
            Assert.Empty(result.ScoringFactions);
            Assert.Equal(0, result.PointsEach);
            Assert.Equal(0L, result.LootDrops);
            Assert.Equal(10, zone.Progress);
            Assert.Equal(5, zone.LootCounter);
            var credit = zone.SnapshotFactionProgress();
            Assert.Equal(2, credit.Count);
            Assert.Equal(10, credit[11]);
            Assert.Equal(10, credit[22]);
        }

        public static IEnumerable<object[]> InvalidDoubles()
        {
            yield return new object[] { -1.0 };
            yield return new object[] { double.NaN };
            yield return new object[] { double.PositiveInfinity };
            yield return new object[] { double.NegativeInfinity };
        }

        [Theory]
        [MemberData(nameof(InvalidDoubles))]
        public void Invalid_elapsed_time_does_not_mutate_or_complete(double invalid)
        {
            var zone = PartlyFilled();
            AssertUnchanged(zone, zone.Tick(invalid, TwoFactions));
        }

        [Theory]
        [MemberData(nameof(InvalidDoubles))]
        public void Invalid_capture_time_does_not_mutate_or_complete(double invalid)
        {
            var rules = Rules();
            var zone = PartlyFilled(rules);
            rules.CaptureSeconds = invalid;
            AssertUnchanged(zone, zone.Tick(10, TwoFactions));
        }

        [Theory]
        [MemberData(nameof(InvalidDoubles))]
        public void Invalid_presence_share_does_not_mutate_or_complete(double invalid)
        {
            var rules = Rules();
            var zone = PartlyFilled(rules);
            rules.MinPresenceShare = invalid;
            AssertUnchanged(zone, zone.Tick(10, TwoFactions));
        }

        [Fact]
        public void Presence_share_above_one_does_not_mutate_or_complete()
        {
            var rules = Rules();
            var zone = PartlyFilled(rules);
            rules.MinPresenceShare = 1.01;
            AssertUnchanged(zone, zone.Tick(10, TwoFactions));
        }

        [Theory]
        [MemberData(nameof(InvalidDoubles))]
        public void Invalid_saved_progress_does_not_mutate_or_complete(double invalid)
        {
            var zone = PartlyFilled();
            zone.Progress = invalid;
            var result = zone.Tick(10, TwoFactions);
            Assert.False(result.Completed);
            Assert.Empty(result.ScoringFactions);
            Assert.Equal(0L, result.LootDrops);
            Assert.Equal(invalid, zone.Progress);
            Assert.Equal(5, zone.LootCounter);
            var credit = zone.SnapshotFactionProgress();
            Assert.Equal(2, credit.Count);
            Assert.Equal(3, credit[11]);
            Assert.Equal(3, credit[22]);
        }

        private static ZoneCapture PartlyFilled(CaptureRules rules = null)
        {
            rules = rules ?? Rules();
            rules.PointsPerLoot = 7;
            var zone = new ZoneCapture(rules) { LootCounter = 5 };
            Assert.False(zone.Tick(3, TwoFactions).Completed);
            return zone;
        }

        private static void AssertUnchanged(ZoneCapture zone, CaptureResult result)
        {
            Assert.False(result.Completed);
            Assert.Empty(result.ScoringFactions);
            Assert.Equal(0, result.PointsEach);
            Assert.Equal(0L, result.LootDrops);
            Assert.Equal(3, zone.Progress);
            Assert.Equal(5, zone.LootCounter);
            var credit = zone.SnapshotFactionProgress();
            Assert.Equal(2, credit.Count);
            Assert.Equal(3, credit[11]);
            Assert.Equal(3, credit[22]);
        }
    }
}
