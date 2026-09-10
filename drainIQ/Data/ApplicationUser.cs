using drainIQ.Models;
using Microsoft.AspNetCore.Identity;

namespace drainIQ.Data;

public class ApplicationUser : IdentityUser<Guid>
{
    public ICollection<UserDevice> UserDevices { get; set; } = new List<UserDevice>();
}
