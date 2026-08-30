namespace agot_bg_website.Services;

/// <summary>
/// Mirrors Django's average-PBEM-response-time calculation in
/// agotboardgame_main.views.user_profile (MIGRATION_PLAN.md §10 / user profile page): average the
/// most recent 100 <see cref="Domain.PbemResponseTime"/> rows for a user, trimming the 10 fastest
/// and 10 slowest of those samples first if there are more than 20, to reduce the influence of
/// outliers (e.g. a single multi-day away-from-keyboard response skewing the average).
/// </summary>
public static class PbemResponseTimeCalculator
{
    /// <param name="responseTimesInSeconds">
    /// Response times (in seconds) of a user's most recent up-to-100 PBEM moves, in any order.
    /// </param>
    public static TimeSpan? CalculateAverage(IReadOnlyCollection<int> responseTimesInSeconds)
    {
        if (responseTimesInSeconds.Count == 0)
        {
            return null;
        }

        var values = responseTimesInSeconds.ToList();
        if (values.Count > 20)
        {
            values.Sort();
            values = values.Skip(10).Take(values.Count - 20).ToList();
        }

        var averageSeconds = values.Average();
        return TimeSpan.FromSeconds(Math.Round(averageSeconds));
    }
}
