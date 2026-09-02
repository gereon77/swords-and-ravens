using agot_bg_website.Services;
using Xunit;

namespace agot_bg_website.Tests.Services;

public class PbemResponseTimeCalculatorTests
{
    [Fact]
    public void NoSamples_ReturnsNull()
    {
        var result = PbemResponseTimeCalculator.CalculateAverage([]);

        Assert.Null(result);
    }

    [Fact]
    public void FewSamples_AveragesAllOfThem()
    {
        var result = PbemResponseTimeCalculator.CalculateAverage([10, 20, 30]);

        Assert.Equal(TimeSpan.FromSeconds(20), result);
    }

    [Fact]
    public void ManySamples_TrimsTenFastestAndTenSlowestBeforeAveraging()
    {
        // 30 samples: ten 1s (fastest), ten 100s (middle, average), ten 10000s (slowest, outliers).
        var values = Enumerable
            .Repeat(1, 10)
            .Concat(Enumerable.Repeat(100, 10))
            .Concat(Enumerable.Repeat(10000, 10))
            .ToList();

        var result = PbemResponseTimeCalculator.CalculateAverage(values);

        // Only the middle 10 samples (all 100s) remain after trimming 10 fastest + 10 slowest.
        Assert.Equal(TimeSpan.FromSeconds(100), result);
    }

    [Fact]
    public void ExactlyTwentySamples_DoesNotTrim()
    {
        var values = Enumerable.Repeat(10, 10).Concat(Enumerable.Repeat(20, 10)).ToList();

        var result = PbemResponseTimeCalculator.CalculateAverage(values);

        Assert.Equal(TimeSpan.FromSeconds(15), result);
    }
}
