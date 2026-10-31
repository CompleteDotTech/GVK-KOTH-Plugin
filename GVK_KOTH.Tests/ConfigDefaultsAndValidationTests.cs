using System;
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using GVK.Koth.Core.Config;
using Xunit;

namespace GVK.Koth.Tests
{
    public class ConfigDefaultsAndValidationTests
    {
        private static KothConfig Read(string xml)
        {
            using (var reader = new StringReader(xml))
                return (KothConfig)new XmlSerializer(typeof(KothConfig)).Deserialize(reader);
        }

        private static KothConfig RoundTrip(KothConfig config)
        {
            var serializer = new XmlSerializer(typeof(KothConfig));
            using (var writer = new StringWriter())
            {
                serializer.Serialize(writer, config);
                return Read(writer.ToString());
            }
        }

        private static KothConfig Load(string xml)
        {
            var config = Read(xml);
            KothConfig.ApplyDefaults(config);
            return config;
        }

        [Fact]
        public void Legacy_xml_without_defaults_keeps_builtin_values_through_roundtrip()
        {
            var config = Read("<KothConfig><Zones><ZoneProfile><Name>Legacy</Name><X>42</X><Activation>AlwaysOn</Activation><PointsPerLoot>0</PointsPerLoot></ZoneProfile></Zones></KothConfig>");
            Assert.Null(config.Defaults);
            config = RoundTrip(config);
            KothConfig.ApplyDefaults(config);
            var zone = Assert.Single(config.Zones);
            Assert.Null(config.Defaults);
            Assert.Equal("Legacy", zone.Name);
            Assert.True(zone.Enabled);
            Assert.Equal(ZoneMode.Capture, zone.Mode);
            Assert.Equal(1500, zone.Radius);
            Assert.Equal(300, zone.CaptureSeconds);
            Assert.Equal(42, zone.X);
            Assert.Empty(ConfigCheck.Run(config));
        }

        private static string SharedXml(int radius) => $@"<KothConfig>
  <Defaults><Name>NotAnIdentity</Name><Radius>{radius}</Radius><Enabled>false</Enabled>
    <Mode>Encampment</Mode><Activation>AlwaysOn</Activation><X>12</X><PointsPerLoot>0</PointsPerLoot>
    <SpawnGroup>shared</SpawnGroup><LootContainerName>shared container</LootContainerName>
    <Loot><LootItem><TypeId>Ingot</TypeId><SubtypeId>Iron</SubtypeId><Amount>4</Amount></LootItem></Loot>
  </Defaults>
  <Zones>
    <ZoneProfile><Name>Inherited</Name></ZoneProfile>
    <ZoneProfile><Name>Override</Name><Radius>0</Radius><Enabled>true</Enabled><Mode>Capture</Mode>
      <X>0</X><SpawnGroup></SpawnGroup><LootContainerName></LootContainerName><Loot /></ZoneProfile>
    <ZoneProfile><Name>Second</Name></ZoneProfile>
  </Zones></KothConfig>";

        [Fact]
        public void Xml_defaults_inherit_only_omissions_and_keep_explicit_zero_empty_and_enum_zero()
        {
            var config = Load(SharedXml(700));
            var inherited = config.Zones[0];
            var overridden = config.Zones[1];
            Assert.Equal("Inherited", inherited.Name);
            Assert.Equal(700, inherited.Radius);
            Assert.False(inherited.Enabled);
            Assert.Equal(ZoneMode.Encampment, inherited.Mode);
            Assert.Equal(12, inherited.X);
            Assert.Equal("shared", inherited.SpawnGroup);
            Assert.Equal("Iron", Assert.Single(inherited.Loot).SubtypeId);
            Assert.Equal("Override", overridden.Name);
            Assert.Equal(0, overridden.Radius);
            Assert.True(overridden.Enabled);
            Assert.Equal(ZoneMode.Capture, overridden.Mode);
            Assert.Equal(0, overridden.X);
            Assert.Equal("", overridden.SpawnGroup);
            Assert.Equal("", overridden.LootContainerName);
            Assert.Empty(overridden.Loot);
        }

        [Fact]
        public void Explicit_false_and_programmatic_assignments_override_true_shared_values()
        {
            var config = Load("<KothConfig><Defaults><Enabled>true</Enabled><OnFoot>true</OnFoot><Radius>700</Radius></Defaults><Zones><ZoneProfile><Name>XML</Name><Enabled>false</Enabled><OnFoot>false</OnFoot><Radius>0</Radius></ZoneProfile></Zones></KothConfig>");
            Assert.False(config.Zones[0].Enabled);
            Assert.False(config.Zones[0].OnFoot);
            Assert.Equal(0, config.Zones[0].Radius);
            var programmatic = new ZoneProfile { Name = "Code", Enabled = false, OnFoot = false, Radius = 0, Loot = new System.Collections.Generic.List<LootItem>() };
            config.Zones.Add(programmatic);
            config.Defaults.Loot.Add(new LootItem { SubtypeId = "Iron" });
            KothConfig.ApplyDefaults(config);
            Assert.False(programmatic.Enabled);
            Assert.False(programmatic.OnFoot);
            Assert.Equal(0, programmatic.Radius);
            Assert.Empty(programmatic.Loot);
        }

        [Fact]
        public void Inherited_loot_is_deeply_independent_and_reapplication_is_idempotent()
        {
            var config = Load(SharedXml(700));
            var first = config.Zones[0];
            var second = config.Zones[2];
            Assert.NotSame(config.Defaults.Loot, first.Loot);
            Assert.NotSame(first.Loot, second.Loot);
            Assert.NotSame(config.Defaults.Loot[0], first.Loot[0]);
            Assert.NotSame(first.Loot[0], second.Loot[0]);
            KothConfig.ApplyDefaults(config);
            Assert.Single(first.Loot);
            Assert.Single(second.Loot);
            first.Loot[0].Amount = 99;
            first.Loot.Add(new LootItem { SubtypeId = "Gold" });
            Assert.Equal(4, config.Defaults.Loot[0].Amount);
            Assert.Equal(4, second.Loot[0].Amount);
            Assert.Single(second.Loot);
        }

        [Fact]
        public void Reloaded_xml_updates_inheritors_but_preserves_overrides()
        {
            var first = Load(SharedXml(700));
            var second = Load(SharedXml(900));
            Assert.Equal(700, first.Zones[0].Radius);
            Assert.Equal(900, second.Zones[0].Radius);
            Assert.Equal(0, first.Zones[1].Radius);
            Assert.Equal(0, second.Zones[1].Radius);
            var roundtrip = RoundTrip(Read(SharedXml(700)));
            KothConfig.ApplyDefaults(roundtrip);
            Assert.Equal(700, roundtrip.Zones[0].Radius);
            Assert.Empty(roundtrip.Zones[1].Loot);
        }

        [Fact]
        public void Invalid_inherited_setting_is_reported_for_the_effective_zone()
        {
            var config = Load("<KothConfig><Defaults><Radius>-1</Radius><Activation>AlwaysOn</Activation><X>1</X><PointsPerLoot>0</PointsPerLoot></Defaults><Zones><ZoneProfile><Name>Bad</Name></ZoneProfile><ZoneProfile><Name>Good</Name><Radius>10</Radius></ZoneProfile></Zones></KothConfig>");
            var problems = ConfigCheck.Run(config);
            Assert.Contains(problems, p => p.Contains("Bad") && p.Contains("Radius"));
            Assert.DoesNotContain(problems, p => p.Contains("Good") && p.Contains("Radius"));
        }

        [Fact]
        public void Negative_idle_drain_is_rejected_for_inherited_and_explicit_values_while_zero_is_valid()
        {
            var config = Load("<KothConfig><Defaults><IdleDrainPlayers>-1</IdleDrainPlayers><Activation>AlwaysOn</Activation><X>1</X><PointsPerLoot>0</PointsPerLoot></Defaults><Zones><ZoneProfile><Name>Inherited</Name></ZoneProfile><ZoneProfile><Name>Explicit</Name><IdleDrainPlayers>-2</IdleDrainPlayers></ZoneProfile><ZoneProfile><Name>Zero</Name><IdleDrainPlayers>0</IdleDrainPlayers></ZoneProfile></Zones></KothConfig>");
            Assert.Equal(-1, config.Zones[0].IdleDrainPlayers);
            Assert.Equal(-2, config.Zones[1].IdleDrainPlayers);
            Assert.Equal(0, config.Zones[2].IdleDrainPlayers);
            var problems = ConfigCheck.Run(config);
            Assert.Contains(problems, p => p.Contains("Inherited") && p.Contains("IdleDrainPlayers"));
            Assert.Contains(problems, p => p.Contains("Explicit") && p.Contains("IdleDrainPlayers"));
            Assert.DoesNotContain(problems, p => p.Contains("Zero"));

            var zeroInherited = Load("<KothConfig><Defaults><IdleDrainPlayers>0</IdleDrainPlayers><Activation>AlwaysOn</Activation><X>1</X><PointsPerLoot>0</PointsPerLoot></Defaults><Zones><ZoneProfile><Name>Valid</Name></ZoneProfile></Zones></KothConfig>");
            Assert.Equal(0, Assert.Single(zeroInherited.Zones).IdleDrainPlayers);
            Assert.Empty(ConfigCheck.Run(zeroInherited));
        }

        [Theory]
        [InlineData("X")]
        [InlineData("Y")]
        [InlineData("Z")]
        [InlineData("Radius")]
        [InlineData("CaptureSeconds")]
        [InlineData("MinPresenceShare")]
        [InlineData("NoThrusterRange")]
        [InlineData("NoLargeGridPowerRange")]
        [InlineData("NoSafeZoneRange")]
        [InlineData("NoProjectorRange")]
        [InlineData("LinkRange")]
        public void Every_floating_setting_rejects_each_nonfinite_xml_value(string field)
        {
            foreach (var value in new[] { "NaN", "INF", "-INF" })
            {
                var config = Load($"<KothConfig><Defaults><{field}>{value}</{field}></Defaults><Zones><ZoneProfile><Name>Bad</Name><Activation>AlwaysOn</Activation><PointsPerLoot>0</PointsPerLoot></ZoneProfile></Zones></KothConfig>");
                Assert.Contains(ConfigCheck.Run(config), p => p.Contains("Bad") && p.Contains(field) && p.Contains("finite"));
            }
        }

        [Theory]
        [InlineData(1, 9223372036854775807L, false)]
        [InlineData(2, 4611686018427387903L, false)]
        [InlineData(2, 4611686018427387904L, true)]
        [InlineData(2147483647, 0L, false)]
        [InlineData(0, 9223372036854775807L, false)]
        [InlineData(5, 200000L, false)]
        public void Credit_product_boundary_is_validated_after_xml_inheritance(int points, long credits, bool overflow)
        {
            var config = Load($"<KothConfig><Defaults><PointsPerCompletion>{points}</PointsPerCompletion><CreditsPerPoint>{credits}</CreditsPerPoint><X>1</X><Activation>AlwaysOn</Activation><PointsPerLoot>0</PointsPerLoot></Defaults><Zones><ZoneProfile><Name>Reward</Name></ZoneProfile></Zones></KothConfig>");
            var problems = ConfigCheck.Run(config);
            Assert.Equal(overflow, problems.Any(p => p.Contains("PointsPerCompletion") && p.Contains("CreditsPerPoint")));
            if (!overflow) Assert.Empty(problems);
        }
    }
}
