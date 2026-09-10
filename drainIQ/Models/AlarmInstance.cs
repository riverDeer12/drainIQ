namespace drainIQ.Models;

public class AlarmInstance
{
    public long AlarmId { get; set; }
    public int RuleId { get; set; }
    public AlarmRule Rule { get; set; } = null!;
    public long MeasurementId { get; set; }
    public Measurement Measurement { get; set; } = null!;
    public DateTimeOffset TriggeredAt { get; set; }
    public string Status { get; set; } = AlarmStatus.Triggered;
    public DateTimeOffset? AcknowledgedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
}

public static class AlarmStatus
{
    public const string Triggered = "triggered";
    public const string Acknowledged = "acknowledged";
    public const string Resolved = "resolved";

    public static readonly string[] All = [Triggered, Acknowledged, Resolved];
}
