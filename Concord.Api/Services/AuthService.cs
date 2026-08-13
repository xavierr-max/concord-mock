using Concord.Api.DTOs.Auth;
using Concord.Api.Models;
using Concord.Api.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Concord.Api.Services;

public sealed class AuthService(
    UserManager<ApplicationUser> userManager,
    IRefreshTokenRepository refreshTokenRepository,
    IJwtTokenService jwtTokenService) : IAuthService
{
    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = request.Username,
            Username = request.Username,
            Email = request.Email,
            Avatar = request.Avatar,
            Status = UserStatus.Offline
        };
        var result = await userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            throw new IdentityValidationException(result.Errors.Select(error => error.Description).ToArray());
        }
        return await CreateAuthResponseAsync(user, cancellationToken);
    }

    public async Task<AuthResponse?> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var normalizedLogin = request.Login.ToUpperInvariant();
        var user = await userManager.Users.SingleOrDefaultAsync(candidate =>
            candidate.NormalizedUserName == normalizedLogin || candidate.NormalizedEmail == normalizedLogin,
            cancellationToken);
        if (user is null || !await userManager.CheckPasswordAsync(user, request.Password)) return null;
        return await CreateAuthResponseAsync(user, cancellationToken);
    }

    public async Task<AuthResponse?> RefreshAsync(RefreshRequest request, CancellationToken cancellationToken)
    {
        var storedToken = await refreshTokenRepository.GetByHashAsync(
            jwtTokenService.HashRefreshToken(request.RefreshToken), cancellationToken);
        if (storedToken is null || !storedToken.IsActive) return null;

        storedToken.RevokedAt = DateTimeOffset.UtcNow;
        await refreshTokenRepository.SaveChangesAsync(cancellationToken);
        return await CreateAuthResponseAsync(storedToken.User, cancellationToken);
    }

    public async Task<bool> LogoutAsync(Guid userId, RefreshRequest request, CancellationToken cancellationToken)
    {
        var storedToken = await refreshTokenRepository.GetByHashAsync(
            jwtTokenService.HashRefreshToken(request.RefreshToken), cancellationToken);
        if (storedToken is null || storedToken.UserId != userId || !storedToken.IsActive) return false;
        storedToken.RevokedAt = DateTimeOffset.UtcNow;
        await refreshTokenRepository.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<UserResponse?> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        return user is null ? null : ToUserResponse(user);
    }

    private async Task<AuthResponse> CreateAuthResponseAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var access = jwtTokenService.CreateAccessToken(user);
        var refresh = jwtTokenService.CreateRefreshToken();
        await refreshTokenRepository.AddAsync(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = refresh.Hash,
            ExpiresAt = refresh.ExpiresAt
        }, cancellationToken);
        await refreshTokenRepository.SaveChangesAsync(cancellationToken);
        return new AuthResponse(access.Token, access.ExpiresAt, refresh.Token, refresh.ExpiresAt, ToUserResponse(user));
    }

    private static UserResponse ToUserResponse(ApplicationUser user) =>
        new(user.Id, user.Username, user.Email ?? string.Empty, user.Avatar, user.CreatedAt, user.UpdatedAt, user.Status.ToString());
}

public sealed class IdentityValidationException(IReadOnlyCollection<string> errors) : Exception("Dados de registro inválidos.")
{
    public IReadOnlyCollection<string> Errors { get; } = errors;
}
