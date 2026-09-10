using drainIQ.Data;
using drainIQ.Models;
using Microsoft.EntityFrameworkCore;

namespace drainIQ.Endpoints;

public static class DeviceEndpoints
{
    public static void MapDeviceEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/devices").WithTags("Devices").RequireAuthorization();

        group.MapGet("/", async (ApplicationDbContext db) =>
            await db.Devices.Where(d => d.IsActive).OrderBy(d => d.DeviceName).ToListAsync());

        group.MapGet("/{id:int}", async (int id, ApplicationDbContext db) =>
            await db.Devices.FindAsync(id) is { } device ? Results.Ok(device) : Results.NotFound());

        group.MapPost("/", async (CreateDeviceRequest request, ApplicationDbContext db) =>
        {
            var device = new Device
            {
                DeviceName = request.DeviceName,
                Lat = request.Lat,
                Long = request.Long,
                Description = request.Description
            };

            db.Devices.Add(device);
            await db.SaveChangesAsync();

            return Results.Created($"/api/devices/{device.DeviceId}", device);
        });

        group.MapGet("/{id:int}/measurements", async (int id, DateTimeOffset? from, DateTimeOffset? to, ApplicationDbContext db) =>
        {
            var since = from ?? DateTimeOffset.UtcNow.AddHours(-24);
            var until = to ?? DateTimeOffset.UtcNow;

            var measurements = await db.Measurements
                .Where(m => m.DeviceId == id && m.SentAt >= since && m.SentAt <= until)
                .OrderByDescending(m => m.SentAt)
                .ToListAsync();

            return Results.Ok(measurements);
        });
    }
}

public record CreateDeviceRequest(string DeviceName, decimal Lat, decimal Long, string? Description);
