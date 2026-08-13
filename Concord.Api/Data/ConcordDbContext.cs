using Concord.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Concord.Api.Data;

/// <summary>
/// Contexto inicial do Concord. As entidades serão acrescentadas em migrations futuras.
/// </summary>
public sealed class ConcordDbContext(DbContextOptions<ConcordDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Server> Servers => Set<Server>();
    public DbSet<ServerMember> ServerMembers => Set<ServerMember>();
    public DbSet<ServerInvite> ServerInvites => Set<ServerInvite>();
    public DbSet<Channel> Channels => Set<Channel>();
    public DbSet<Message> Messages => Set<Message>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(user => user.Username).HasMaxLength(32).IsRequired();
            entity.Property(user => user.Avatar).HasMaxLength(2048);
            entity.Property(user => user.DisplayName).HasMaxLength(100);
            entity.Property(user => user.Bio).HasMaxLength(500);
            entity.Property(user => user.Status).HasConversion<string>().HasMaxLength(20);
            entity.HasIndex(user => user.Username).IsUnique();
        });

        builder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(token => token.Id);
            entity.Property(token => token.TokenHash).HasMaxLength(128).IsRequired();
            entity.HasIndex(token => token.TokenHash).IsUnique();
            entity.HasOne(token => token.User)
                .WithMany(user => user.RefreshTokens)
                .HasForeignKey(token => token.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Server>(entity =>
        {
            entity.Property(server => server.Name).HasMaxLength(100).IsRequired();
            entity.Property(server => server.Icon).HasMaxLength(2048);
            entity.HasOne(server => server.Owner)
                .WithMany(user => user.OwnedServers)
                .HasForeignKey(server => server.OwnerId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<ServerMember>(entity =>
        {
            entity.Property(member => member.Role).HasConversion<string>().HasMaxLength(20);
            entity.HasIndex(member => new { member.ServerId, member.UserId }).IsUnique();
            entity.HasOne(member => member.Server)
                .WithMany(server => server.Members)
                .HasForeignKey(member => member.ServerId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(member => member.User)
                .WithMany(user => user.ServerMemberships)
                .HasForeignKey(member => member.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ServerInvite>(entity =>
        {
            entity.Property(invite => invite.Code).HasMaxLength(64).IsRequired();
            entity.Property(invite => invite.Uses).IsConcurrencyToken();
            entity.HasIndex(invite => invite.Code).IsUnique();
            entity.HasOne(invite => invite.Server)
                .WithMany(server => server.Invites)
                .HasForeignKey(invite => invite.ServerId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(invite => invite.CreatedByUser)
                .WithMany(user => user.CreatedServerInvites)
                .HasForeignKey(invite => invite.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<Channel>(entity =>
        {
            entity.Property(channel => channel.Name).HasMaxLength(100).IsRequired();
            entity.Property(channel => channel.Type).HasConversion<string>().HasMaxLength(20);
            entity.HasIndex(channel => new { channel.ServerId, channel.Position });
            entity.HasOne(channel => channel.Server)
                .WithMany(server => server.Channels)
                .HasForeignKey(channel => channel.ServerId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Message>(entity =>
        {
            entity.Property(message => message.Content).HasMaxLength(2000).IsRequired();
            entity.HasIndex(message => new { message.ChannelId, message.CreatedAt });
            entity.HasIndex(message => message.CreatedAt);
            entity.HasOne(message => message.Channel)
                .WithMany(channel => channel.Messages)
                .HasForeignKey(message => message.ChannelId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(message => message.Author)
                .WithMany(user => user.Messages)
                .HasForeignKey(message => message.AuthorId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
