namespace drainIQ.Models;

public class AlarmRule
{
    public int RuleId { get; set; }
    public int DeviceId { get; set; }
    public Device Device { get; set; } = null!;
    public string AlarmName { get; set; } = null!;
    public string AlarmType { get; set; } = null!;
    public decimal ThresholdValue { get; set; }
    public string Comparator { get; set; } = null!;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<AlarmInstance> AlarmInstances { get; set; } = new List<AlarmInstance>();
}

public static class AlarmComparators
{
    public static readonly string[] Allowed = ["<", "<=", ">", ">=", "="];

    public static bool IsSatisfied(string comparator, decimal value, decimal threshold) => comparator switch
    {
        "<" => value < threshold,
        "<=" => value <= threshold,
        ">" => value > threshold,
        ">=" => value >= threshold,
        "=" => value == threshold,
        _ => false
    };
}

public static class AlarmTypes
{
    public const string WaterLevel = "water_level";
    public const string LowBattery = "low_battery";

    /// <summary>Threshold_value is minutes since the device's last measurement; evaluated
    /// periodically by DeviceOfflineCheckService, not per-measurement like the others.</summary>
    public const string DeviceOffline = "device_offline";

    public static readonly string[] All = [WaterLevel, LowBattery, DeviceOffline];
}
