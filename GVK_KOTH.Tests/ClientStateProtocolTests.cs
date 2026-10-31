using System;
using System.Globalization;
using GVK.Koth.Core;
using Xunit;

namespace GVK.Koth.Tests
{
    public class ClientStateProtocolTests
    {
        [Fact]
        public void Open_record_preserves_prefix_and_appends_sorted_factions()
        {
            string record = ClientStateProtocol.BuildOpenZoneRecord(
                "Hill", 12.34, -56.78, 0, 150.4, 0.125, 3, 9876543210L,
                new[] { "BBB", "AAA" });

            Assert.Equal("O|Hill|12.3|-56.8|0|150|0.125|3|9876543210|[AAA] [BBB]", record);
        }

        [Fact]
        public void Empty_factions_still_produce_an_empty_tenth_field()
        {
            const string expected = "O|Hill|1|2|3|4|0.500|0|42|";
            Assert.Equal(expected, ClientStateProtocol.BuildOpenZoneRecord("Hill", 1, 2, 3, 4, 0.5, 0, 42, null));
            Assert.Equal(expected, ClientStateProtocol.BuildOpenZoneRecord("Hill", 1, 2, 3, 4, 0.5, 0, 42, new string[0]));
            Assert.Equal("", ClientStateProtocol.FormatFactionTags(null));
            Assert.Equal("", ClientStateProtocol.FormatFactionTags(new string[0]));
        }

        [Fact]
        public void Formatting_filters_blank_values_deduplicates_and_sorts_ordinally()
        {
            var tags = new[] { "b", null, " ", "AAA", "B", "AAA", "\t", "a" };
            Assert.Equal("[AAA] [B] [a] [b]", ClientStateProtocol.FormatFactionTags(tags));
            Assert.Equal("", ClientStateProtocol.FormatFactionTags(new[] { null, "", "  ", "\t" }));
        }

        [Fact]
        public void Faction_changes_are_visible_when_count_and_fraction_are_unchanged()
        {
            string first = ClientStateProtocol.BuildOpenZoneRecord("Hill", 1, 2, 3, 4, 0.5, 2, 42, new[] { "AAA" });
            string second = ClientStateProtocol.BuildOpenZoneRecord("Hill", 1, 2, 3, 4, 0.5, 2, 42, new[] { "BBB" });

            Assert.Equal("O|Hill|1|2|3|4|0.500|2|42|[AAA]", first);
            Assert.Equal("O|Hill|1|2|3|4|0.500|2|42|[BBB]", second);
            Assert.NotEqual(first, second);
        }

        [Fact]
        public void Numbers_use_invariant_format_under_non_invariant_current_culture()
        {
            CultureInfo original = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                Assert.Equal("O|Hill|1.5|-2.5|3|4|0.125|2|42|[AAA]",
                    ClientStateProtocol.BuildOpenZoneRecord("Hill", 1.5, -2.5, 3, 4, 0.125, 2, 42, new[] { "AAA" }));
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
            }
        }

        [Fact]
        public void Hostile_inputs_cannot_add_fields_records_or_faction_markup()
        {
            string record = ClientStateProtocol.BuildOpenZoneRecord(
                "Hill|other\r\nnext", 1, 2, 3, 4, 0.5, 2, 42,
                new[] { "A|B\r\n<color=red>C</color>" });
            string[] fields = record.Split('|');

            Assert.Equal(10, fields.Length);
            Assert.Equal("Hill/other  next", fields[1]);
            Assert.DoesNotContain("\r", record);
            Assert.DoesNotContain("\n", record);
            Assert.DoesNotContain("<", fields[9]);
            Assert.DoesNotContain(">", fields[9]);
            Assert.StartsWith("[", fields[9]);
            Assert.EndsWith("]", fields[9]);
        }
    }
}
