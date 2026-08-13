using Concord.Api.Models;

namespace Concord.Api.Services;

public interface IJwtTokenService
{
    (string Token, DateTimeOffset ExpiresAt) CreateAccessToken(ApplicationUser user);
    (string Token, string Hash, DateTimeOffset ExpiresAt) CreateRefreshToken();
    string HashRefreshToken(string token);
}
