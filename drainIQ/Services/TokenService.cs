using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using drainIQ.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace drainIQ.Services;

/// <summary>Issues signed JWT access tokens for the React frontend to authenticate against this API.</summary>
public class TokenService(IOptions<JwtOptions> jwtOptions, UserManager<ApplicationUser> userManager)
{
    private readonly JwtOptions _options = jwtOptions.Value;

    public async Task<string> GenerateTokenAsync(ApplicationUser user)
    {
        var roles = await userManager.GetRolesAsync(user);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty)
        };

        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Key));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_options.ExpiryMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
