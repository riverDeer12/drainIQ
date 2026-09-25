namespace drainIQ.Models;

public class Measurement
{
    public long MeasurementId { get; set; }
    public int DeviceId { get; set; }
    public Device Device { get; set; } = null!;

    /// <summary>Distance from sensor top to water surface, in centimeters (cm).</summary>
    public decimal WaterLevelFromTopCm { get; set; }

    /// <summary>Device battery charge at the time of this reading, 0-100. Null if the device doesn't report it (e.g. mains-powered).</summary>
    public int? BatteryLevelPct { get; set; }

    public DateTimeOffset SentAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public ICollection<AlarmInstance> AlarmInstances { get; set; } = new List<AlarmInstance>();
}
