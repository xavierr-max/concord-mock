using Concord.Api.DTOs.Users;
using Concord.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Concord.Api.Services;

public sealed class UserProfileService(UserManager<ApplicationUser> userManager) : IUserProfileService
{
    public async Task<UserProfileResponse?> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await userManager.Users.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken);
        return user is null ? null : ToResponse(user);
    }

    public async Task<UserProfileResponse?> UpdateAsync(
        Guid userId, UpdateUserProfileRequest request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null) return null;

        var username = request.Username.Trim();
        var normalizedUsername = userManager.NormalizeName(username);
        var duplicate = await userManager.Users.AsNoTracking().AnyAsync(candidate =>
            candidate.Id != userId && candidate.NormalizedUserName == normalizedUsername, cancellationToken);
        if (duplicate) throw new DuplicateUsernameException(username);

        user.Username = username;
        user.UserName = username;
        user.DisplayName = NormalizeOptional(request.DisplayName);
        user.Bio = NormalizeOptional(request.Bio);
        user.UpdatedAt = DateTimeOffset.UtcNow;

        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
            throw new IdentityValidationException(result.Errors.Select(error => error.Description).ToArray());

        return ToResponse(user);
    }

    public async Task<UserProfileResponse?> UpdateAvatarAsync(
        Guid userId, UpdateAvatarRequest request, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null) return null;

        user.Avatar = request.Avatar.Trim();
        user.UpdatedAt = DateTimeOffset.UtcNow;
        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
            throw new IdentityValidationException(result.Errors.Select(error => error.Description).ToArray());

        return ToResponse(user);
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static UserProfileResponse ToResponse(ApplicationUser user) => new(
        user.Id, user.Username, user.DisplayName, user.Bio, user.Avatar,
        user.CreatedAt, user.UpdatedAt, user.Status.ToString());
}

public sealed class DuplicateUsernameException(string username)
    : Exception($"O username '{username}' já está em uso.");
