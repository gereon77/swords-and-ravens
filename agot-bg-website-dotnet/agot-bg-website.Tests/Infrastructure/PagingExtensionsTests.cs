using agot_bg_website.Infrastructure.Paging;
using Xunit;

namespace agot_bg_website.Tests.Infrastructure;

/// <summary>
/// Covers the "Items per page" control on Pages/Shared/_Pager.cshtml, which round-trips a
/// user/localStorage-supplied page size through the querystring - PagingExtensions.NormalizePageSize
/// is the only thing standing between that and either a useless zero/negative page size or a
/// tampered value large enough to force an expensive unbounded query.
/// </summary>
public class PagingExtensionsTests
{
    [Theory]
    [InlineData(25, 25)]
    [InlineData(50, 50)]
    [InlineData(PagingExtensions.MinPageSize, PagingExtensions.MinPageSize)]
    [InlineData(PagingExtensions.MaxPageSize, PagingExtensions.MaxPageSize)]
    public void NormalizePageSize_ValueWithinRange_ReturnsItUnchanged(int requested, int expected)
    {
        Assert.Equal(expected, PagingExtensions.NormalizePageSize(requested, defaultPageSize: 25));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void NormalizePageSize_NonPositiveValue_FallsBackToDefault(int requested)
    {
        Assert.Equal(25, PagingExtensions.NormalizePageSize(requested, defaultPageSize: 25));
    }

    [Fact]
    public void NormalizePageSize_BelowMinimum_ClampsToMinimum()
    {
        Assert.Equal(
            PagingExtensions.MinPageSize,
            PagingExtensions.NormalizePageSize(1, defaultPageSize: 25)
        );
    }

    [Fact]
    public void NormalizePageSize_AboveMaximum_ClampsToMaximum()
    {
        Assert.Equal(
            PagingExtensions.MaxPageSize,
            PagingExtensions.NormalizePageSize(PagingExtensions.MaxPageSize + 1000, defaultPageSize: 25)
        );
    }
}
