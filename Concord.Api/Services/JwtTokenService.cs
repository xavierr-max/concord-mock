using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Concord.Api.Configurations;
using Concord.Api.Models;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Concord.Api.Services;

public sealed class JwtTokenService(IOptions<JwtSettings> settings) : IJwtTokenService
{
    private readonly JwtSettings _settings = settings.Value;

    public (string Token, DateTimeOffset ExpiresAt) CreateAccessToken(ApplicationUser user)
    {
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(_settings.AccessTokenMinutes);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.UniqueName, user.Username),
            new Claim(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty)
        };
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.Key)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(_settings.Issuer, _settings.Audience, claims,
            expires: expiresAt.UtcDateTime, signingCredentials: credentials);
        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }

    public (string Token, string Hash, DateTimeOffset ExpiresAt) CreateRefreshToken()
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        return (token, HashRefreshToken(token), DateTimeOffset.UtcNow.AddDays(_settings.RefreshTokenDays));
    }

    public string HashRefreshToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
