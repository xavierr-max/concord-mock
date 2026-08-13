using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Concord.Api.Data;
using Concord.Api.DTOs.Auth;
using Concord.Api.DTOs.Users;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Concord.Api.Tests;

public sealed class UserProfileEndpointsTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Profile_endpoints_require_authentication()
    {
        await using var factory = new ProfileWebApplicationFactory();
        var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/users/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.GetAsync($"/api/users/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Get_me_returns_profile_without_sensitive_fields()
    {
        await using var factory = new ProfileWebApplicationFactory();
        var client = factory.CreateClient();
        var auth = await RegisterAsync(client, "aurora", "aurora@concord.test");
        Authenticate(client, auth);

        var response = await client.GetAsync("/api/users/me");
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("email", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("phone", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Update_me_changes_only_editable_profile_fields()
    {
        await using var factory = new ProfileWebApplicationFactory();
        var client = factory.CreateClient();
        var auth = await RegisterAsync(client, "aurora", "aurora@concord.test");
        Authenticate(client, auth);

        var response = await client.PutAsJsonAsync("/api/users/me", new UpdateUserProfileRequest
        {
            Username = "nova-aurora",
            DisplayName = "Aurora Silva",
            Bio = "Olá, Concord!"
        });
        var profile = await response.Content.ReadFromJsonAsync<UserProfileResponse>(JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(profile);
        Assert.Equal(auth.User.Id, profile.Id);
        Assert.Equal(auth.User.CreatedAt, profile.CreatedAt);
        Assert.Equal("nova-aurora", profile.Username);
        Assert.Equal("Aurora Silva", profile.DisplayName);
        Assert.Equal("Olá, Concord!", profile.Bio);
    }

    [Fact]
    public async Task Update_avatar_changes_avatar()
    {
        await using var factory = new ProfileWebApplicationFactory();
        var client = factory.CreateClient();
        var auth = await RegisterAsync(client, "aurora", "aurora@concord.test");
        Authenticate(client, auth);

        var response = await client.PutAsJsonAsync("/api/users/me/avatar", new UpdateAvatarRequest
        {
            Avatar = "https://cdn.concord.test/avatars/aurora.png"
        });
        var profile = await response.Content.ReadFromJsonAsync<UserProfileResponse>(JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("https://cdn.concord.test/avatars/aurora.png", profile!.Avatar);
    }

    [Fact]
    public async Task Update_me_rejects_duplicate_username()
    {
        await using var factory = new ProfileWebApplicationFactory();
        var firstClient = factory.CreateClient();
        await RegisterAsync(firstClient, "aurora", "aurora@concord.test");
        var secondClient = factory.CreateClient();
        var secondAuth = await RegisterAsync(secondClient, "orion", "orion@concord.test");
        Authenticate(secondClient, secondAuth);

        var response = await secondClient.PutAsJsonAsync("/api/users/me", new UpdateUserProfileRequest
        {
            Username = "AURORA"
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Get_by_id_returns_another_users_public_profile()
    {
        await using var factory = new ProfileWebApplicationFactory();
        var client = factory.CreateClient();
        var first = await RegisterAsync(client, "aurora", "aurora@concord.test");
        var second = await RegisterAsync(client, "orion", "orion@concord.test");
        Authenticate(client, second);

        var response = await client.GetAsync($"/api/users/{first.User.Id}");
        var profile = await response.Content.ReadFromJsonAsync<UserProfileResponse>(JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("aurora", profile!.Username);
    }

    private static void Authenticate(HttpClient client, AuthResponse auth) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

    private static async Task<AuthResponse> RegisterAsync(HttpClient client, string username, string email)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            Username = username,
            Email = email,
            Password = "Concord1"
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions))!;
    }

    private sealed class ProfileWebApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly InMemoryDatabaseRoot _databaseRoot = new();
        private readonly string _databaseName = $"concord-profile-tests-{Guid.NewGuid()}";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<ConcordDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<ConcordDbContext>>();
                services.RemoveAll<ConcordDbContext>();
                services.AddDbContext<ConcordDbContext>(options =>
                    options.UseInMemoryDatabase(_databaseName, _databaseRoot));
            });
        }
    }
}
