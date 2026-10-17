using LibraryApp.Application.Common;

namespace LibraryApp.Application.Tests.Common;

public class PagedResultTests
{
    [Fact]
    public void Defaults_are_page_1_and_20_items()
    {
        var request = new PageRequest();

        Assert.Equal(1, request.Page);
        Assert.Equal(20, request.PageSize);
        Assert.Equal(0, request.Skip);
    }

    [Theory]
    [InlineData(0, 20, 1, 20)]
    [InlineData(-5, 20, 1, 20)]
    [InlineData(3, 0, 3, 20)]
    [InlineData(3, 500, 3, 100)]
    public void Out_of_range_values_are_corrected(int page, int size, int expectedPage, int expectedSize)
    {
        var request = new PageRequest(page, size);

        Assert.Equal(expectedPage, request.Page);
        Assert.Equal(expectedSize, request.PageSize);
    }

    [Fact]
    public void Skip_and_total_pages_follow_the_page_size()
    {
        var request = new PageRequest(3, 20);
        var result = new PagedResult<string>(["a"], 41, request.Page, request.PageSize);

        Assert.Equal(40, request.Skip);
        Assert.Equal(3, result.TotalPages);
    }

    [Theory]
    [InlineData(int.MaxValue, 20)]
    [InlineData(int.MaxValue, 100)]
    [InlineData(107374184, 20)]   // the first page whose offset no longer fits in 32 bits
    public void A_page_far_past_the_end_never_gives_a_negative_skip(int page, int size)
    {
        var request = new PageRequest(page, size);

        Assert.Equal(int.MaxValue, request.Skip);
    }
}
