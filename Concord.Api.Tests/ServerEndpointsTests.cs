using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Concord.Api.Data;
using Concord.Api.DTOs.Auth;
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

public sealed class ServerEndpointsTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Creation_adds_authenticated_user_as_owner()
    {
        await using var factory = new ServerWebApplicationFactory();
        var client = factory.CreateClient();
        var auth = await RegisterAndAuthenticateAsync(client, "owner");

        var server = await CreateServerAsync(client);

        Assert.Equal(auth.User.Id, server.OwnerId);
        var owner = Assert.Single(server.Members);
        Assert.Equal(auth.User.Id, owner.UserId);
        Assert.Equal("OWNER", owner.Role);
    }

    [Fact]
    public async Task Member_can_access_server_and_list_it()
    {
        await using var factory = new ServerWebApplicationFactory();
        var client = factory.CreateClient();
        await RegisterAndAuthenticateAsync(client, "owner");
        var created = await CreateServerAsync(client);

        var getResponse = await client.GetAsync($"/api/servers/{created.Id}");
        var list = await client.GetFromJsonAsync<ServerResponse[]>("/api/servers", JsonOptions);

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        Assert.Contains(list!, server => server.Id == created.Id);
    }

    [Fact]
    public async Task Non_member_cannot_access_internal_server_data()
    {
        await using var factory = new ServerWebApplicationFactory();
        var ownerClient = factory.CreateClient();
        await RegisterAndAuthenticateAsync(ownerClient, "owner");
        var server = await CreateServerAsync(ownerClient);
        var outsiderClient = factory.CreateClient();
        await RegisterAndAuthenticateAsync(outsiderClient, "outsider");

        var response = await outsiderClient.GetAsync($"/api/servers/{server.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Authenticated_user_can_join_server()
    {
        await using var factory = new ServerWebApplicationFactory();
        var ownerClient = factory.CreateClient();
        await RegisterAndAuthenticateAsync(ownerClient, "owner");
        var server = await CreateServerAsync(ownerClient);
        var memberClient = factory.CreateClient();
        var memberAuth = await RegisterAndAuthenticateAsync(memberClient, "member");

        var joinResponse = await memberClient.PostAsync($"/api/servers/{server.Id}/members", null);
        var accessResponse = await memberClient.GetAsync($"/api/servers/{server.Id}");
        var member = await joinResponse.Content.ReadFromJsonAsync<ServerMemberResponse>(JsonOptions);

        Assert.Equal(HttpStatusCode.Created, joinResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, accessResponse.StatusCode);
        Assert.Equal(memberAuth.User.Id, member!.UserId);
        Assert.Equal("MEMBER", member.Role);
    }

    [Fact]
    public async Task Member_can_leave_server()
    {
        await using var factory = new ServerWebApplicationFactory();
        var ownerClient = factory.CreateClient();
        await RegisterAndAuthenticateAsync(ownerClient, "owner");
        var server = await CreateServerAsync(ownerClient);
        var memberClient = factory.CreateClient();
        var memberAuth = await RegisterAndAuthenticateAsync(memberClient, "member");
        await memberClient.PostAsync($"/api/servers/{server.Id}/members", null);

        var leaveResponse = await memberClient.DeleteAsync(
            $"/api/servers/{server.Id}/members/{memberAuth.User.Id}");
        var accessResponse = await memberClient.GetAsync($"/api/servers/{server.Id}");

        Assert.Equal(HttpStatusCode.NoContent, leaveResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, accessResponse.StatusCode);
    }

    [Fact]
    public async Task Only_owner_can_delete_server()
    {
        await using var factory = new ServerWebApplicationFactory();
        var ownerClient = factory.CreateClient();
        await RegisterAndAuthenticateAsync(ownerClient, "owner");
        var server = await CreateServerAsync(ownerClient);
        var memberClient = factory.CreateClient();
        await RegisterAndAuthenticateAsync(memberClient, "member");
        await memberClient.PostAsync($"/api/servers/{server.Id}/members", null);

        var forbidden = await memberClient.DeleteAsync($"/api/servers/{server.Id}");
        var deleted = await ownerClient.DeleteAsync($"/api/servers/{server.Id}");
        var afterDelete = await ownerClient.GetAsync($"/api/servers/{server.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, afterDelete.StatusCode);
    }

    private static async Task<ServerResponse> CreateServerAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/servers", new CreateServerRequest
        {
            Name = "Concord Test Server",
            Icon = "https://cdn.concord.test/server.png"
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ServerResponse>(JsonOptions))!;
    }

    private static async Task<AuthResponse> RegisterAndAuthenticateAsync(HttpClient client, string username)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register", new RegisterRequest
        {
            Username = username,
            Email = $"{username}@concord.test",
            Password = "Concord1"
        });
        response.EnsureSuccessStatusCode();
        var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return auth;
    }

    private sealed class ServerWebApplicationFactory : WebApplicationFactory<Program>
    {
        private readonly InMemoryDatabaseRoot _databaseRoot = new();
        private readonly string _databaseName = $"concord-server-tests-{Guid.NewGuid()}";

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
