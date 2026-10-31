using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using GVK.Koth.Core;
using Xunit;

namespace GVK.Koth.Tests
{
    public class LootReconciliationPersistenceTests
    {
        private static string Serialize(KothState state)
        {
            using (var writer = new StringWriter())
            {
                new XmlSerializer(typeof(KothState)).Serialize(writer, state);
                return writer.ToString();
            }
        }

        private static KothState Deserialize(string xml)
        {
            using (var reader = new StringReader(xml))
                return (KothState)new XmlSerializer(typeof(KothState)).Deserialize(reader);
        }

        private static KothState RoundTrip(KothState state) => Deserialize(Serialize(state));

        [Fact]
        public void Legacy_int_queue_xml_preserves_debt_and_leaves_position_and_marker_unknown()
        {
            const string xml = "<KothState><Zones><ZoneSave><Name>npc#42</Name>" +
                "<Progress>37.5</Progress><LootCounter>6</LootCounter>" +
                "<PendingLootDrops>2147483647</PendingLootDrops></ZoneSave></Zones></KothState>";

            var zone = Assert.Single(Deserialize(xml).Zones);
            Assert.Equal(2147483647L, zone.PendingLootDrops);
            Assert.Null(zone.LootPosition);
            Assert.Null(zone.LootReconciliation);
            var restored = Assert.Single(RoundTrip(new KothState { Zones = new List<ZoneSave> { zone } }).Zones);
            Assert.Equal("npc#42", restored.Name);
            Assert.Equal(37.5, restored.Progress);
            Assert.Equal(6, restored.LootCounter);
            Assert.Equal(2147483647L, restored.PendingLootDrops);
            Assert.Null(restored.LootPosition);
            Assert.Null(restored.LootReconciliation);
        }

        [Fact]
        public void Long_queues_and_known_or_unknown_positions_roundtrip_per_zone()
        {
            var state = new KothState { Zones = new List<ZoneSave>
            {
                new ZoneSave { Name = "large", PendingLootDrops = 9007199254740993L,
                    LootPosition = new LootPositionSave { X = 12345.125, Y = -9876.5, Z = 0.25 } },
                new ZoneSave { Name = "origin", PendingLootDrops = long.MaxValue,
                    LootPosition = new LootPositionSave { X = 0, Y = 0, Z = 0 } },
                new ZoneSave { Name = "unknown", PendingLootDrops = 2147483648L }
            } };

            var restored = RoundTrip(state);
            Assert.Equal(3, restored.Zones.Count);
            var large = restored.Zones[0];
            var origin = restored.Zones[1];
            var unknown = restored.Zones[2];
            Assert.Equal("large", large.Name);
            Assert.Equal(9007199254740993L, large.PendingLootDrops);
            Assert.NotNull(large.LootPosition);
            Assert.Equal(12345.125, large.LootPosition.X);
            Assert.Equal(-9876.5, large.LootPosition.Y);
            Assert.Equal(0.25, large.LootPosition.Z);
            Assert.Equal("origin", origin.Name);
            Assert.Equal(long.MaxValue, origin.PendingLootDrops);
            Assert.NotNull(origin.LootPosition);
            Assert.Equal(0d, origin.LootPosition.X);
            Assert.Equal(0d, origin.LootPosition.Y);
            Assert.Equal(0d, origin.LootPosition.Z);
            Assert.Equal("unknown", unknown.Name);
            Assert.Equal(2147483648L, unknown.PendingLootDrops);
            Assert.Null(unknown.LootPosition);
            Assert.Null(large.LootReconciliation);
            Assert.Null(origin.LootReconciliation);
            Assert.Null(unknown.LootReconciliation);
            Assert.NotSame(large, origin);
            Assert.NotSame(large.LootPosition, origin.LootPosition);

            large.LootPosition.X = -1;
            large.PendingLootDrops = 1;
            Assert.Equal(0d, origin.LootPosition.X);
            Assert.Equal(long.MaxValue, origin.PendingLootDrops);
            var again = RoundTrip(restored);
            Assert.Equal(-1d, again.Zones[0].LootPosition.X);
            Assert.Equal(1L, again.Zones[0].PendingLootDrops);
            Assert.Equal(0d, again.Zones[1].LootPosition.X);
            Assert.Equal(long.MaxValue, again.Zones[1].PendingLootDrops);
            Assert.Null(again.Zones[2].LootPosition);
            Assert.Null(again.Zones[2].LootReconciliation);
        }

        [Fact]
        public void Legacy_xml_without_marker_keeps_null_and_existing_state()
        {
            const string xml = "<KothState><Zones><ZoneSave><Name>old</Name><Progress>42.5</Progress>" +
                "<LootCounter>7</LootCounter><PendingLootDrops>3</PendingLootDrops></ZoneSave></Zones>" +
                "<ForcedOn><string>old</string></ForcedOn><SlotPicks><SlotPick><Slot>morning</Slot>" +
                "<Zone>old</Zone><Spawned>true</Spawned></SlotPick></SlotPicks></KothState>";

            var legacy = Deserialize(xml);
            Assert.Null(new ZoneSave().LootReconciliation);
            Assert.Null(legacy.Zones[0].LootReconciliation);
            var restored = RoundTrip(legacy);
            var zone = Assert.Single(restored.Zones);
            Assert.Null(zone.LootReconciliation);
            Assert.Equal("old", zone.Name);
            Assert.Equal(42.5, zone.Progress);
            Assert.Equal(7, zone.LootCounter);
            Assert.Equal(3, zone.PendingLootDrops);
            Assert.Equal("old", Assert.Single(restored.ForcedOn));
            var slot = Assert.Single(restored.SlotPicks);
            Assert.Equal("morning", slot.Slot);
            Assert.Equal("old", slot.Zone);
            Assert.True(slot.Spawned);
        }

        [Fact]
        public void Marker_roundtrip_preserves_exact_raw_values_and_unknown_observation()
        {
            var created = new DateTime(2026, 10, 31, 16, 53, 36, DateTimeKind.Utc).AddTicks(1234567);
            var state = new KothState { Zones = new List<ZoneSave> { new ZoneSave
            {
                Name = "alpha", Progress = 23.5, LootCounter = 8, PendingLootDrops = 4,
                LootReconciliation = new LootReconciliationSave
                {
                    AttemptId = "attempt<&-01", ContainerId = 9223372036854775000L,
                    CreatedUtc = created, Reason = "rollback <uncertain> & inspect",
                    Items = new List<LootReconciliationItemSave>
                    {
                        new LootReconciliationItemSave { TypeId = "Ore", SubtypeId = "Iron",
                            RequestedRaw = 9007199254740993L, BeforeRaw = 9223372036854775001L,
                            ObservedRaw = 9223372036854775002L, ObservedKnown = false },
                        new LootReconciliationItemSave { TypeId = "Ingot", SubtypeId = "Gold",
                            RequestedRaw = 1000001L, BeforeRaw = 0L, ObservedRaw = 0L, ObservedKnown = true }
                    }
                }
            } } };

            var zone = Assert.Single(RoundTrip(state).Zones);
            Assert.Equal(4, zone.PendingLootDrops);
            Assert.Equal(23.5, zone.Progress);
            Assert.Equal(8, zone.LootCounter);
            var marker = Assert.IsType<LootReconciliationSave>(zone.LootReconciliation);
            Assert.Equal("attempt<&-01", marker.AttemptId);
            Assert.Equal(9223372036854775000L, marker.ContainerId);
            Assert.Equal(created, marker.CreatedUtc);
            Assert.Equal(DateTimeKind.Utc, marker.CreatedUtc.Kind);
            Assert.Equal("rollback <uncertain> & inspect", marker.Reason);
            Assert.Equal(2, marker.Items.Count);
            var unknown = marker.Items[0];
            Assert.Equal("Ore", unknown.TypeId);
            Assert.Equal("Iron", unknown.SubtypeId);
            Assert.Equal(9007199254740993L, unknown.RequestedRaw);
            Assert.Equal(9223372036854775001L, unknown.BeforeRaw);
            Assert.Equal(9223372036854775002L, unknown.ObservedRaw);
            Assert.False(unknown.ObservedKnown);
            var zero = marker.Items[1];
            Assert.Equal("Ingot", zero.TypeId);
            Assert.Equal("Gold", zero.SubtypeId);
            Assert.Equal(1000001L, zero.RequestedRaw);
            Assert.Equal(0L, zero.BeforeRaw);
            Assert.Equal(0L, zero.ObservedRaw);
            Assert.True(zero.ObservedKnown);
        }

        [Fact]
        public void Zones_roundtrip_independently_and_empty_nonnull_marker_is_not_cleared()
        {
            var state = new KothState { Zones = new List<ZoneSave>
            {
                new ZoneSave { Name = "one", PendingLootDrops = 2,
                    LootReconciliation = new LootReconciliationSave { AttemptId = "first", ContainerId = 11 } },
                new ZoneSave { Name = "two", PendingLootDrops = 5,
                    LootReconciliation = new LootReconciliationSave { AttemptId = "second", ContainerId = 22 } },
                new ZoneSave { Name = "empty", PendingLootDrops = 7, LootReconciliation = new LootReconciliationSave() },
                new ZoneSave { Name = "legacy", PendingLootDrops = 9 }
            } };

            var restored = RoundTrip(state);
            Assert.Equal(4, restored.Zones.Count);
            Assert.Equal("first", restored.Zones[0].LootReconciliation.AttemptId);
            Assert.Equal(11L, restored.Zones[0].LootReconciliation.ContainerId);
            Assert.Equal(2, restored.Zones[0].PendingLootDrops);
            Assert.Equal("second", restored.Zones[1].LootReconciliation.AttemptId);
            Assert.Equal(22L, restored.Zones[1].LootReconciliation.ContainerId);
            Assert.Equal(5, restored.Zones[1].PendingLootDrops);
            Assert.NotNull(restored.Zones[2].LootReconciliation);
            Assert.Equal(7, restored.Zones[2].PendingLootDrops);
            Assert.Null(restored.Zones[3].LootReconciliation);
            Assert.Equal(9, restored.Zones[3].PendingLootDrops);

            restored.Zones[0].LootReconciliation.AttemptId = "changed";
            Assert.Equal("second", restored.Zones[1].LootReconciliation.AttemptId);
            var again = RoundTrip(restored);
            Assert.Equal("changed", again.Zones[0].LootReconciliation.AttemptId);
            Assert.Equal("second", again.Zones[1].LootReconciliation.AttemptId);
            Assert.NotNull(again.Zones[2].LootReconciliation);
            Assert.Null(again.Zones[3].LootReconciliation);
        }
    }
}
