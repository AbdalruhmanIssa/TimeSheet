using FluentAssertions;
using TimeSheet.BLL.Extensions;

namespace TimeSheet.Tests.Extensions
{
    public class SearchPatternTests
    {
        [Fact]
        public void Contains_WrapsPlainTextInWildcards()
        {
            SearchPattern.Contains("acme").Should().Be("%acme%");
        }

        [Theory]
        [InlineData("50%", @"%50\%%")]
        [InlineData("a_b", @"%a\_b%")]
        [InlineData(@"c:\temp", @"%c:\\temp%")]
        [InlineData(@"\%_", @"%\\\%\_%")]
        public void Contains_EscapesLikeWildcardsAndTheEscapeCharacter(string search, string expected)
        {
            SearchPattern.Contains(search).Should().Be(expected);
        }

        [Fact]
        public void Escape_IsBackslash()
        {
            SearchPattern.Escape.Should().Be(@"\");
        }
    }
}
