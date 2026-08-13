using Concord.Api.Data;
using Concord.Api.Models;
using Concord.Api.Repositories;
using Concord.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace Concord.Api.Configurations;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddConcordConfiguration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("ConcordDatabase")
            ?? throw new InvalidOperationException(
                "A connection string 'ConcordDatabase' precisa ser configurada.");

        services.AddDbContext<ConcordDbContext>(options =>
            options.UseNpgsql(connectionString));

        var jwtSettings = configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>()
            ?? throw new InvalidOperationException("A seção Jwt precisa ser configurada.");
        if (jwtSettings.SigningKey.Length < 32)
        {
            throw new InvalidOperationException("Jwt:SigningKey precisa ter ao menos 32 caracteres.");
        }

        services.Configure<JwtSettings>(configuration.GetSection(JwtSettings.SectionName));
        services.AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 8;
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = false;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<ConcordDbContext>()
            .AddSignInManager();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.IncludeErrorDetails = true;
                options.Events = new JwtBearerEvents
                {
                    OnAuthenticationFailed = context =>
                    {
                        context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                            .CreateLogger("Concord.Authentication")
                            .LogWarning(context.Exception, "JWT authentication failed.");
                        return Task.CompletedTask;
                    }
                };
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwtSettings.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwtSettings.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.SigningKey)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                };
            });
        services.AddAuthorization();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IJwtTokenService, JwtTokenService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserProfileService, UserProfileService>();
        services.AddScoped<IServerService, ServerService>();
        services.AddScoped<IServerInviteService, ServerInviteService>();
        services.AddScoped<IChannelService, ChannelService>();
        services.AddScoped<IServerAuthorizationService, ServerAuthorizationService>();
        services.AddScoped<IMessageService, MessageService>();

        var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
            ?? [];

        services.AddCors(options => options.AddPolicy(CorsConfiguration.PolicyName, policy =>
        {
            if (allowedOrigins.Length == 0)
            {
                throw new InvalidOperationException("Configure ao menos uma origem em Cors:AllowedOrigins.");
            }

            policy.WithOrigins(allowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod();
        }));

        return services;
    }
}
