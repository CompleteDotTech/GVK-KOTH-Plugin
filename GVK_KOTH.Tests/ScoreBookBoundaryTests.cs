using System;
using GVK.Koth.Core;
using Xunit;

namespace GVK.Koth.Tests
{
    public class ScoreBookBoundaryTests
    {
        private static readonly DateTime Now = new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);

        private static ScoreLogEntry Add(ScoreBook book, int change)
        {
            return book.Add(1, "AAA", "Alpha", change, "API", "boundary", Now);
        }

        private static void AssertResult(ScoreBook book, ScoreLogEntry entry, int change, int total, string csv)
        {
            Assert.Equal(change, entry.Change);
            Assert.Equal(total, entry.NewTotal);
            Assert.Equal(total, book.Get(1).Points);
            Assert.Equal(csv, entry.ToCsv());
        }

        [Fact]
        public void Maximum_then_repeated_positive_changes_stay_at_cap_and_subtraction_works()
        {
            var book = new ScoreBook();

            AssertResult(book, Add(book, int.MaxValue), 2147483647, 2147483647,
                "2026-10-28 12:00:00,1,AAA,2147483647,API,boundary,2147483647");
            AssertResult(book, Add(book, 1), 0, 2147483647,
                "2026-10-28 12:00:00,1,AAA,0,API,boundary,2147483647");
            AssertResult(book, Add(book, int.MaxValue), 0, 2147483647,
                "2026-10-28 12:00:00,1,AAA,0,API,boundary,2147483647");
            AssertResult(book, Add(book, -10), -10, 2147483637,
                "2026-10-28 12:00:00,1,AAA,-10,API,boundary,2147483637");
        }

        [Fact]
        public void Large_positive_change_near_maximum_applies_only_remaining_headroom()
        {
            var book = new ScoreBook();

            AssertResult(book, Add(book, 2147483640), 2147483640, 2147483640,
                "2026-10-28 12:00:00,1,AAA,2147483640,API,boundary,2147483640");
            AssertResult(book, Add(book, 1000), 7, 2147483647,
                "2026-10-28 12:00:00,1,AAA,7,API,boundary,2147483647");
        }

        [Fact]
        public void Minimum_change_from_zero_applies_zero()
        {
            var book = new ScoreBook();

            AssertResult(book, Add(book, int.MinValue), 0, 0,
                "2026-10-28 12:00:00,1,AAA,0,API,boundary,0");
        }

        [Fact]
        public void Minimum_change_from_maximum_applies_negative_maximum()
        {
            var book = new ScoreBook();

            AssertResult(book, Add(book, int.MaxValue), 2147483647, 2147483647,
                "2026-10-28 12:00:00,1,AAA,2147483647,API,boundary,2147483647");
            AssertResult(book, Add(book, int.MinValue), -2147483647, 0,
                "2026-10-28 12:00:00,1,AAA,-2147483647,API,boundary,0");
        }
    }
}
