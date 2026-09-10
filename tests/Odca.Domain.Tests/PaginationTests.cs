using Odca.Application.Tenancy;

namespace Odca.Domain.Tests;

public sealed class PaginationTests
{
    [Theory]
    [InlineData(null, null, 1, 20)]
    [InlineData(0, 0, 1, 20)]
    [InlineData(-3, -1, 1, 20)]
    [InlineData(2, 50, 2, 50)]
    [InlineData(1, 500, 1, 100)]
    public void NormalizeClampsPageAndPageSize(int? page, int? pageSize, int expectedPage, int expectedSize)
    {
        var (normalizedPage, normalizedSize) = Pagination.Normalize(page, pageSize);

        Assert.Equal(expectedPage, normalizedPage);
        Assert.Equal(expectedSize, normalizedSize);
    }

    [Fact]
    public void OffsetUsesZeroBasedPages()
    {
        Assert.Equal(0, Pagination.Offset(1, 20));
        Assert.Equal(40, Pagination.Offset(3, 20));
    }
}
