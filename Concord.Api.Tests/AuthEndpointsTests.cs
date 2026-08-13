using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Concord.Api.Data;
using Concord.Api.DTOs.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Concord.Api.Tests;

public sealed class AuthEndpointsTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Register_creates_user_and_returns_tokens()
    {
        await using var factory = new AuthWebApplicationFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/register", NewRegistration());
        var payload = await response.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(payload);
        Assert.NotEqual(Guid.Empty, payload.User.Id);
        Assert.Equal("aurora", payload.User.Username);
        Assert.NotEmpty(payload.AccessToken);
        Assert.NotEmpty(payload.RefreshToken);
    }

    [Fact]
    public async Task Login_with_valid_credentials_returns_tokens()
    {
        await using var factory = new AuthWebApplicationFactory();
        var client = factory.CreateClient();
        await RegisterAsync(client);

        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Login = "aurora",
            Password = "Concord1"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(await response.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions));
    }

    [Fact]
    public async Task Login_with_invalid_credentials_returns_unauthorized()
    {
        await using var factory = new AuthWebApplicationFactory();
        var client = factory.CreateClient();
        await RegisterAsync(client);

        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest
        {
            Login = "aurora",
            Password = "WrongPassword1"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_with_access_token_returns_authenticated_user()
    {
        await using var factory = new AuthWebApplicationFactory();
        var client = factory.CreateClient();
        var auth = await RegisterAsync(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var response = await client.GetAsync("/api/auth/me");
        var rawUser = await response.Content.ReadAsStringAsync();
        var user = JsonSerializer.Deserialize<UserResponse>(rawUser, JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(user);
        Assert.Equal("aurora", user.Username);
    }

    [Fact]
    public async Task Me_without_access_token_returns_unauthorized()
    {
        await using var factory = new AuthWebApplicationFactory();
        var response = await factory.CreateClient().GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static RegisterRequest NewRegistration() => new()
    {
        Username = "aurora",
        Email = "aurora@concord.test",
        Password = "Concord1"
    };

    private static async Task<AuthResponse> RegisterAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register", NewRegistration());
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions))!;
    }

    private sealed class AuthWebApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly InMemoryDatabaseRoot _databaseRoot = new();
        private readonly string _databaseName = $"concord-auth-tests-{Guid.NewGuid()}";

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
