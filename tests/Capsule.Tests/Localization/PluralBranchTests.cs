using System.Text;
using Capsule.Localization;

namespace Capsule.Tests.Localization;

public sealed class PluralBranchTests
{
    // An exact branch matches a non-negative count of its whole magnitude, however large, and never a negative one.
    [Theory]
    [InlineData(9223372036854775807UL, false, "max")]
    [InlineData(ulong.MaxValue, false, "other")]
    [InlineData(9223372036854775807UL, true, "other")]
    public void AnExactBranch_MatchesOnlyItsOwnNonNegativeCount(ulong magnitude, bool negative, string shown)
    {
        byte[] block =
        [
            StringTable.ExactBranch, .. "9223372036854775807"u8, StringTable.ExactEnd, .. "max"u8,
            StringTable.CategoryBranch, (byte)('0' + (int)PluralCategory.Other), .. "other"u8,
        ];

        Assert.Equal(shown, Encoding.UTF8.GetString(StringTable.Branch(block, magnitude, negative, PluralCategory.Other)));
    }
}
