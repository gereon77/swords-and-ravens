namespace agot_bg_website.Domain;

public class UsageMetric
{
    public DateTime Hour { get; set; }
    public string Category { get; set; } = "";
    public string Outcome { get; set; } = "";
    public long Requests { get; set; }
    public double DurationSeconds { get; set; }
    public long ProcessSamples { get; set; }
    public double ProcessCpuPercentSum { get; set; }
    public long ProcessMemoryBytesMax { get; set; }
}

public class UsageMetricBatch
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }
}
