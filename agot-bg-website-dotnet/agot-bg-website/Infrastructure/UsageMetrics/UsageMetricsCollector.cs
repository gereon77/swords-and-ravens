using System.Diagnostics;
using Prometheus;

namespace agot_bg_website.Infrastructure.UsageMetrics;

internal sealed record UsageMetricValue(
    string Category,
    string Outcome,
    long Count,
    double Seconds
);

internal sealed record UsageBatch(
    Guid Id,
    DateTime Hour,
    IReadOnlyList<UsageMetricValue> Values,
    double CpuPercent,
    long MemoryBytes
);

internal sealed class UsageMetricsCollector
{
    private static readonly string[] Categories =
    [
        "statistics",
        "profiles",
        "public-api",
        "other-api",
        "other",
    ];
    private static readonly string[] Outcomes = ["2xx", "3xx", "4xx", "429", "5xx", "other"];
    private readonly object gate = new();
    private readonly Histogram histogram = Metrics
        .WithCustomRegistry(Metrics.NewCustomRegistry())
        .CreateHistogram(
            "snr_request_duration_seconds",
            "Request durations grouped by fixed category and response status.",
            ["category", "outcome"]
        );
    private readonly Dictionary<(string, string), (long Count, double Sum)> last = new();
    private readonly Process process = Process.GetCurrentProcess();
    private TimeSpan previousCpu;
    private long previousTimestamp = Stopwatch.GetTimestamp();

    public UsageMetricsCollector()
    {
        previousCpu = process.TotalProcessorTime;
    }

    public void Record(string category, string outcome, double seconds)
    {
        if (!Categories.Contains(category) || !Outcomes.Contains(outcome))
        {
            throw new ArgumentOutOfRangeException(nameof(category), "Unknown metric label.");
        }

        lock (gate)
        {
            histogram.WithLabels(category, outcome).Observe(seconds);
        }
    }

    public UsageBatch Snapshot(DateTime now)
    {
        lock (gate)
        {
            var values = new List<UsageMetricValue>();
            foreach (var category in Categories)
            {
                foreach (var outcome in Outcomes)
                {
                    var metric = histogram.WithLabels(category, outcome);
                    var previous = last.GetValueOrDefault((category, outcome));
                    var count = metric.Count - previous.Count;
                    var seconds = metric.Sum - previous.Sum;
                    last[(category, outcome)] = (metric.Count, metric.Sum);
                    if (count > 0)
                    {
                        values.Add(new(category, outcome, count, seconds));
                    }
                }
            }

            process.Refresh();
            var cpu = process.TotalProcessorTime;
            var timestamp = Stopwatch.GetTimestamp();
            var elapsed = Stopwatch.GetElapsedTime(previousTimestamp, timestamp).TotalSeconds;
            var cpuPercent = elapsed > 0 ? (cpu - previousCpu).TotalSeconds / elapsed * 100 : 0;
            previousCpu = cpu;
            previousTimestamp = timestamp;
            var hour = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc);
            return new(Guid.NewGuid(), hour, values, Math.Max(0, cpuPercent), process.WorkingSet64);
        }
    }
}
