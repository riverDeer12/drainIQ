using drainIQ.Data;

namespace drainIQ.Models;

public class UserDevice
{
    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;
    public int DeviceId { get; set; }
    public Device Device { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
}
