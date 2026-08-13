using Concord.Api.DTOs.Users;

namespace Concord.Api.Services;

public interface IUserProfileService
{
    Task<UserProfileResponse?> GetAsync(Guid userId, CancellationToken cancellationToken);
    Task<UserProfileResponse?> UpdateAsync(Guid userId, UpdateUserProfileRequest request, CancellationToken cancellationToken);
    Task<UserProfileResponse?> UpdateAvatarAsync(Guid userId, UpdateAvatarRequest request, CancellationToken cancellationToken);
}
