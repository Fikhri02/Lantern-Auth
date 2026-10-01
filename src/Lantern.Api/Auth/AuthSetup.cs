using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Lantern.Api.Auth;

public static class AuthSetup
{
    public static IServiceCollection AddLanternJwt(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<KeycloakOptions>(config.GetSection(KeycloakOptions.Section));
        services.AddMemoryCache();
        services.AddHttpClient<TokenIntrospector>(c => c.Timeout = TimeSpan.FromSeconds(5));

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
                o.Events = new JwtBearerEvents
                {
                    OnTokenValidated = async ctx =>
                    {
                        var introspector = ctx.HttpContext.RequestServices.GetRequiredService<TokenIntrospector>();
                        var result = await introspector.CheckAsync((JsonWebToken)ctx.SecurityToken, ctx.HttpContext.RequestAborted);
                        if (result != IntrospectionResult.Active)
                            ctx.Fail($"Token rejected by introspection: {result}");
                    }
                };
            });

        return services;
    }
}
