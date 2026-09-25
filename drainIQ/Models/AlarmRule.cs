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
}

public static class AlarmTypes
{
    public const string WaterLevel = "water_level";
    public const string LowBattery = "low_battery";

    public static readonly string[] All = [WaterLevel, LowBattery];
}
