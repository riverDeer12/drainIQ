namespace drainIQ.Models;

public class Device
{
    public int DeviceId { get; set; }
    public string DeviceName { get; set; } = null!;
    public decimal Lat { get; set; }
    public decimal Long { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public ICollection<Measurement> Measurements { get; set; } = new List<Measurement>();
    public ICollection<AlarmRule> AlarmRules { get; set; } = new List<AlarmRule>();
    public ICollection<UserDevice> UserDevices { get; set; } = new List<UserDevice>();
}
