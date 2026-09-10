using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using drainIQ.Data;
using drainIQ.Services;
using Microsoft.AspNetCore.Identity;

namespace drainIQ.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/register", async (RegisterRequest request, UserManager<ApplicationUser> userManager, TokenService tokenService) =>
        {
            if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            {
                return Results.BadRequest("Email and password are required.");
            }

            if (await userManager.FindByEmailAsync(request.Email) is not null)
            {
                return Results.Conflict("A user with this email already exists.");
            }

            var user = new ApplicationUser { UserName = request.Email, Email = request.Email };

            var result = await userManager.CreateAsync(user, request.Password);
            if (!result.Succeeded)
            {
                return Results.BadRequest(result.Errors.Select(e => e.Description));
            }

            var token = await tokenService.GenerateTokenAsync(user);
            return Results.Created($"/api/auth/me", new AuthResponse(token, user.Id, user.Email!));
        });

        group.MapPost("/login", async (LoginRequest request, UserManager<ApplicationUser> userManager, TokenService tokenService) =>
        {
            var user = await userManager.FindByEmailAsync(request.Email);
            if (user is null || !await userManager.CheckPasswordAsync(user, request.Password))
            {
                return Results.Unauthorized();
            }

            var token = await tokenService.GenerateTokenAsync(user);
            return Results.Ok(new AuthResponse(token, user.Id, user.Email!));
        });

        group.MapGet("/me", (ClaimsPrincipal principal) =>
        {
            var userId = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
            var email = principal.FindFirstValue(JwtRegisteredClaimNames.Email);
            var roles = principal.FindAll(ClaimTypes.Role).Select(c => c.Value);

            return Results.Ok(new { userId, email, roles });
        }).RequireAuthorization();
    }
}

public record RegisterRequest(string Email, string Password);
public record LoginRequest(string Email, string Password);
public record AuthResponse(string Token, Guid UserId, string Email);
