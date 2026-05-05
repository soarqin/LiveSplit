using Xunit;

using LiveSplit.UI.Components;

namespace LiveSplit.PinnedSplits.Tests;

public class PinnedSegmentParserMust
{
    [Theory]
    [InlineData(null, false, null)]
    [InlineData("", false, "")]
    [InlineData("Boss", false, "Boss")]
    [InlineData("^Boss", true, "Boss")]
    [InlineData("^^Boss", false, "^Boss")]
    [InlineData("^", false, "")]
    [InlineData("^^", false, "^")]
    [InlineData("^^^Boss", true, "^^Boss")]
    [InlineData("-^Boss", false, "-^Boss")]
    [InlineData("^-Boss", true, "-Boss")]
    [InlineData(" ^Boss", false, " ^Boss")]
    public void Parse_ReturnsExpected(string input, bool expectedIsPinned, string expectedDisplayName)
    {
        var result = PinnedSegmentParser.Parse(input);

        Assert.Equal(expectedIsPinned, result.IsPinned);
        Assert.Equal(expectedDisplayName, result.DisplayName);
    }
}
