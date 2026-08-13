using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Concord.Api.Data;
using Concord.Api.DTOs.Auth;
using Concord.Api.DTOs.Channels;
using Concord.Api.DTOs.Invites;
using Concord.Api.DTOs.Servers;
using Concord.Api.Models;
using Concord.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Concord.Api.Tests;

public sealed class ServerPermissionTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    [Fact]
    public async Task Owner_has_every_permission_and_exclusive_server_management()
    {
        await using var factory = new PermissionWebApplicationFactory();
        var owner = factory.CreateClient();
        var ownerAuth = await RegisterAsync(owner, "owner");
        var server = await CreateServerAsync(owner);

        Assert.True(await factory.IsOwnerAsync(server.Id, ownerAuth.User.Id));
        Assert.False(await factory.IsAdminAsync(server.Id, ownerAuth.User.Id));
        foreach (var permission in Enum.GetValues<ServerPermission>())
            Assert.True(await factory.HasPermissionAsync(server.Id, ownerAuth.User.Id, permission));

        var update = await owner.PutAsJsonAsync($"/api/servers/{server.Id}",
            new UpdateServerRequest { Name = "Updated" });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
    }

    [Fact]
    public async Task Admin_manages_channels_invites_and_members_but_not_server()
    {
        await using var factory = new PermissionWebApplicationFactory();
        var owner = factory.CreateClient();
        await RegisterAsync(owner, "owner");
        var server = await CreateServerAsync(owner);
        var admin = factory.CreateClient();
        var adminAuth = await RegisterAsync(admin, "admin");
        await admin.PostAsync($"/api/servers/{server.Id}/members", null);
        await factory.SetRoleAsync(server.Id, adminAuth.User.Id, ServerRole.ADMIN);
        var member = factory.CreateClient();
        var memberAuth = await RegisterAsync(member, "member");
        await member.PostAsync($"/api/servers/{server.Id}/members", null);

        Assert.True(await factory.IsAdminAsync(server.Id, adminAuth.User.Id));
        Assert.True(await factory.HasPermissionAsync(server.Id, adminAuth.User.Id, ServerPermission.ManageChannels));
        Assert.True(await factory.HasPermissionAsync(server.Id, adminAuth.User.Id, ServerPermission.ManageInvites));
        Assert.True(await factory.HasPermissionAsync(server.Id, adminAuth.User.Id, ServerPermission.ModerateMembers));

        var channel = await admin.PostAsJsonAsync($"/api/servers/{server.Id}/channels",
            new SaveChannelRequest { Name = "admin-channel", Type = ChannelType.Text, Position = 0 }, JsonOptions);
        var invite = await admin.PostAsJsonAsync($"/api/servers/{server.Id}/invites",
            new CreateServerInviteRequest { ExpiresAt = DateTimeOffset.UtcNow.AddDays(1) });
        var moderation = await admin.DeleteAsync($"/api/servers/{server.Id}/members/{memberAuth.User.Id}");
        var deleteServer = await admin.DeleteAsync($"/api/servers/{server.Id}");

        Assert.Equal(HttpStatusCode.Created, channel.StatusCode);
        Assert.Equal(HttpStatusCode.Created, invite.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, moderation.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, deleteServer.StatusCode);
    }

    [Fact]
    public async Task Member_has_basic_permissions_only()
    {
        await using var factory = new PermissionWebApplicationFactory();
        var owner = factory.CreateClient();
        await RegisterAsync(owner, "owner");
        var server = await CreateServerAsync(owner);
        await owner.PostAsJsonAsync($"/api/servers/{server.Id}/channels",
            new SaveChannelRequest { Name = "general", Type = ChannelType.Text, Position = 0 }, JsonOptions);
        var member = factory.CreateClient();
        var memberAuth = await RegisterAsync(member, "member");
        await member.PostAsync($"/api/servers/{server.Id}/members", null);

        Assert.True(await factory.IsMemberAsync(server.Id, memberAuth.User.Id));
        Assert.False(await factory.IsOwnerAsync(server.Id, memberAuth.User.Id));
        Assert.False(await factory.IsAdminAsync(server.Id, memberAuth.User.Id));
        Assert.True(await factory.HasPermissionAsync(server.Id, memberAuth.User.Id, ServerPermission.ViewChannels));
        Assert.True(await factory.HasPermissionAsync(server.Id, memberAuth.User.Id, ServerPermission.SendMessages));
        Assert.True(await factory.HasPermissionAsync(server.Id, memberAuth.User.Id, ServerPermission.JoinVoiceChannels));
        Assert.False(await factory.HasPermissionAsync(server.Id, memberAuth.User.Id, ServerPermission.ManageChannels));

        Assert.Equal(HttpStatusCode.OK,
            (await member.GetAsync($"/api/servers/{server.Id}/channels")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await member.PostAsJsonAsync($"/api/servers/{server.Id}/channels",
                new SaveChannelRequest { Name = "blocked", Type = ChannelType.Text, Position = 1 }, JsonOptions)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await member.PostAsJsonAsync($"/api/servers/{server.Id}/invites",
                new CreateServerInviteRequest { ExpiresAt = DateTimeOffset.UtcNow.AddDays(1) })).StatusCode);
    }

    private static async Task<ServerResponse> CreateServerAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/servers", new CreateServerRequest { Name = "Permissions" });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ServerResponse>(JsonOptions))!;
    }

    private static async Task<AuthResponse> RegisterAsync(HttpClient client, string username)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            Username = username, Email = $"{username}@concord.test", Password = "Concord1"
        });
        response.EnsureSuccessStatusCode();
        var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return auth;
    }

    private sealed class PermissionWebApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly InMemoryDatabaseRoot _databaseRoot = new();
        private readonly string _databaseName = $"concord-permission-tests-{Guid.NewGuid()}";

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

        public Task<bool> IsMemberAsync(Guid serverId, Guid userId) =>
            CheckAsync(service => service.IsMemberAsync(serverId, userId, default));

        public Task<bool> IsOwnerAsync(Guid serverId, Guid userId) =>
            CheckAsync(service => service.IsOwnerAsync(serverId, userId, default));

        public Task<bool> IsAdminAsync(Guid serverId, Guid userId) =>
            CheckAsync(service => service.IsAdminAsync(serverId, userId, default));

        public Task<bool> HasPermissionAsync(Guid serverId, Guid userId, ServerPermission permission) =>
            CheckAsync(service => service.HasPermissionAsync(serverId, userId, permission, default));

        public async Task SetRoleAsync(Guid serverId, Guid userId, ServerRole role)
        {
            using var scope = Services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ConcordDbContext>();
            var membership = await context.ServerMembers.SingleAsync(member =>
                member.ServerId == serverId && member.UserId == userId);
            membership.Role = role;
            await context.SaveChangesAsync();
        }

        private async Task<bool> CheckAsync(Func<IServerAuthorizationService, Task<bool>> check)
        {
            using var scope = Services.CreateScope();
            return await check(scope.ServiceProvider.GetRequiredService<IServerAuthorizationService>());
        }
    }
}
