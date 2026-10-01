using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;

namespace Lantern.Api.Auth;

public static class AuthSetup
{
    public static IServiceCollection AddLanternJwt(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<KeycloakOptions>(config.GetSection(KeycloakOptions.Section));

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<KeycloakOptions>>((o, kcOptions) =>
            {
                var kc = kcOptions.Value;
                o.MetadataAddress = $"{kc.RealmUrl}/.well-known/openid-configuration";
                o.RequireHttpsMetadata = false;
                o.MapInboundClaims = false;
                o.TokenValidationParameters.ValidIssuer = kc.Issuer;
                o.TokenValidationParameters.ValidAudience = kc.Audience;
                o.TokenValidationParameters.NameClaimType = "preferred_username";
                o.TokenValidationParameters.RoleClaimType = "roles";
            });

        return services;
    }
}
