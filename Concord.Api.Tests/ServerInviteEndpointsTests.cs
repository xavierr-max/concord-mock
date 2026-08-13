using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Concord.Api.Data;
using Concord.Api.DTOs.Auth;
using Concord.Api.DTOs.Invites;
using Concord.Api.DTOs.Servers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Concord.Api.Tests;

public sealed class ServerInviteEndpointsTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Member_can_create_and_get_secure_invite()
    {
        await using var factory = new InviteWebApplicationFactory();
        var client = factory.CreateClient();
        await RegisterAsync(client, "owner");
        var server = await CreateServerAsync(client);

        var invite = await CreateInviteAsync(client, server.Id, 5);
        var response = await client.GetAsync($"/api/invites/{invite.Code}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(32, invite.Code.Length);
        Assert.DoesNotContain('+', invite.Code);
        Assert.DoesNotContain('/', invite.Code);
        Assert.Equal(5, invite.MaxUses);
    }

    [Fact]
    public async Task Non_member_cannot_create_invite()
    {
        await using var factory = new InviteWebApplicationFactory();
        var owner = factory.CreateClient();
        await RegisterAsync(owner, "owner");
        var server = await CreateServerAsync(owner);
        var outsider = factory.CreateClient();
        await RegisterAsync(outsider, "outsider");

        var response = await outsider.PostAsJsonAsync($"/api/servers/{server.Id}/invites", NewInviteRequest());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task User_can_accept_invite_and_becomes_member()
    {
        await using var factory = new InviteWebApplicationFactory();
        var owner = factory.CreateClient();
        await RegisterAsync(owner, "owner");
        var server = await CreateServerAsync(owner);
        var invite = await CreateInviteAsync(owner, server.Id);
        var guest = factory.CreateClient();
        await RegisterAsync(guest, "guest");

        var accept = await guest.PostAsync($"/api/invites/{invite.Code}/accept", null);
        var access = await guest.GetAsync($"/api/servers/{server.Id}");

        Assert.Equal(HttpStatusCode.OK, accept.StatusCode);
        Assert.Equal(HttpStatusCode.OK, access.StatusCode);
    }

    [Fact]
    public async Task Expired_invite_is_rejected()
    {
        await using var factory = new InviteWebApplicationFactory();
        var owner = factory.CreateClient();
        await RegisterAsync(owner, "owner");
        var server = await CreateServerAsync(owner);
        var invite = await CreateInviteAsync(owner, server.Id);
        await factory.ExpireAsync(invite.Code);
        var guest = factory.CreateClient();
        await RegisterAsync(guest, "guest");

        Assert.Equal(HttpStatusCode.Gone, (await guest.GetAsync($"/api/invites/{invite.Code}")).StatusCode);
        Assert.Equal(HttpStatusCode.Gone,
            (await guest.PostAsync($"/api/invites/{invite.Code}/accept", null)).StatusCode);
    }

    [Fact]
    public async Task Invite_use_limit_is_enforced()
    {
        await using var factory = new InviteWebApplicationFactory();
        var owner = factory.CreateClient();
        await RegisterAsync(owner, "owner");
        var server = await CreateServerAsync(owner);
        var invite = await CreateInviteAsync(owner, server.Id, 1);
        var first = factory.CreateClient();
        await RegisterAsync(first, "first");
        var second = factory.CreateClient();
        await RegisterAsync(second, "second");

        Assert.Equal(HttpStatusCode.OK,
            (await first.PostAsync($"/api/invites/{invite.Code}/accept", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await second.PostAsync($"/api/invites/{invite.Code}/accept", null)).StatusCode);
    }

    [Fact]
    public async Task Missing_invite_and_existing_member_are_rejected()
    {
        await using var factory = new InviteWebApplicationFactory();
        var owner = factory.CreateClient();
        await RegisterAsync(owner, "owner");
        var server = await CreateServerAsync(owner);
        var invite = await CreateInviteAsync(owner, server.Id);

        Assert.Equal(HttpStatusCode.NotFound,
            (await owner.GetAsync("/api/invites/does-not-exist")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,
            (await owner.PostAsync($"/api/invites/{invite.Code}/accept", null)).StatusCode);
    }

    [Fact]
    public async Task Creator_can_delete_invite()
    {
        await using var factory = new InviteWebApplicationFactory();
        var owner = factory.CreateClient();
        await RegisterAsync(owner, "owner");
        var server = await CreateServerAsync(owner);
        var invite = await CreateInviteAsync(owner, server.Id);

        Assert.Equal(HttpStatusCode.NoContent,
            (await owner.DeleteAsync($"/api/invites/{invite.Code}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await owner.GetAsync($"/api/invites/{invite.Code}")).StatusCode);
    }

    private static CreateServerInviteRequest NewInviteRequest(int? maxUses = null) => new()
    {
        ExpiresAt = DateTimeOffset.UtcNow.AddDays(1), MaxUses = maxUses
    };

    private static async Task<ServerInviteResponse> CreateInviteAsync(
        HttpClient client, Guid serverId, int? maxUses = null)
    {
        var response = await client.PostAsJsonAsync(
            $"/api/servers/{serverId}/invites", NewInviteRequest(maxUses));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ServerInviteResponse>(JsonOptions))!;
    }

    private static async Task<ServerResponse> CreateServerAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/servers", new CreateServerRequest { Name = "Invite Test" });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ServerResponse>(JsonOptions))!;
    }

    private static async Task RegisterAsync(HttpClient client, string username)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            Username = username, Email = $"{username}@concord.test", Password = "Concord1"
        });
        response.EnsureSuccessStatusCode();
        var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
    }

    private sealed class InviteWebApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly InMemoryDatabaseRoot _databaseRoot = new();
        private readonly string _databaseName = $"concord-invite-tests-{Guid.NewGuid()}";

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

        public async Task ExpireAsync(string code)
        {
            using var scope = Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ConcordDbContext>();
            var invite = await context.ServerInvites.SingleAsync(item => item.Code == code);
            invite.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            await context.SaveChangesAsync();
        }
    }
}
