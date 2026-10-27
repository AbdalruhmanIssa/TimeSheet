using FluentAssertions;
using MockQueryable;
using System.ComponentModel.DataAnnotations;
using TimeSheet.BLL.DTOs.Request;
using TimeSheet.BLL.Extensions;

namespace TimeSheet.Tests.Extensions
{
    public class PagingExtensionsTests
    {
        private sealed record Row(int N);

        private static readonly List<Row> TwentyFive = Enumerable.Range(1, 25).Select(n => new Row(n)).ToList();

        [Fact]
        public async Task ToPagedAsync_FirstPage()
        {
            var page = await TwentyFive.BuildMock().ToPagedAsync(new PagedRequest { Page = 1, PageSize = 10 });

            page.Items.Select(r => r.N).Should().Equal(Enumerable.Range(1, 10));
            page.TotalCount.Should().Be(25);
            page.TotalPages.Should().Be(3);
            page.HasPreviousPage.Should().BeFalse();
            page.HasNextPage.Should().BeTrue();
        }

        [Fact]
        public async Task ToPagedAsync_LastPartialPage()
        {
            var page = await TwentyFive.BuildMock().ToPagedAsync(new PagedRequest { Page = 3, PageSize = 10 });

            page.Items.Select(r => r.N).Should().Equal(21, 22, 23, 24, 25);
            page.HasPreviousPage.Should().BeTrue();
            page.HasNextPage.Should().BeFalse();
        }

        [Fact]
        public async Task ToPagedAsync_PastTheEnd_IsEmpty_NotAnError()
        {
            var page = await TwentyFive.BuildMock().ToPagedAsync(new PagedRequest { Page = 9, PageSize = 10 });

            page.Items.Should().BeEmpty();
            page.TotalCount.Should().Be(25);
            page.HasNextPage.Should().BeFalse();
        }

        [Fact]
        public async Task ToPagedAsync_NoRows()
        {
            var page = await new List<Row>().BuildMock().ToPagedAsync(new PagedRequest());

            page.Items.Should().BeEmpty();
            page.TotalCount.Should().Be(0);
            page.TotalPages.Should().Be(0);
            page.HasNextPage.Should().BeFalse();
            page.HasPreviousPage.Should().BeFalse();
        }

        [Fact]
        public void ToPaged_InMemory_MatchesTheQueryableVersion()
        {
            var page = TwentyFive.ToPaged(new PagedRequest { Page = 2, PageSize = 10 });

            page.Items.Select(r => r.N).Should().Equal(Enumerable.Range(11, 10));
            page.TotalPages.Should().Be(3);
        }

        [Fact]
        public void PagedRequest_Defaults_Are_Page1_Size20()
        {
            var request = new PagedRequest();

            request.Page.Should().Be(1);
            request.PageSize.Should().Be(20);
        }

        [Theory]
        [InlineData(0, 20, false)]
        [InlineData(1, 0, false)]
        [InlineData(1, 101, false)]  // max page size is 100
        [InlineData(1, 100, true)]
        [InlineData(5, 1, true)]
        public void PagedRequest_Validation(int page, int pageSize, bool valid)
        {
            var request = new PagedRequest { Page = page, PageSize = pageSize };

            Validator.TryValidateObject(request, new ValidationContext(request), null, validateAllProperties: true)
                .Should().Be(valid);
        }
    }
}
