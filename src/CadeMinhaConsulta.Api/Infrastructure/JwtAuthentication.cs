using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

namespace CadeMinhaConsulta.Api.Infrastructure;

internal static class JwtAuthentication
{
    public const string Citizen = "CIDADAO";
    public const string Manager = "GESTOR";
    public const string ReminderRuleReader = "ReminderRuleReader";
    public const string DevelopmentIssuer = "urn:vagacerta:development";

    public static void AddCadeMinhaConsultaAuthentication(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                var jwt = configuration.GetSection("Jwt");
                var issuer = jwt["Issuer"];
                var publicKey = jwt["PublicKeyPem"];
                RsaSecurityKey? signingKey = null;
                if (!string.IsNullOrWhiteSpace(publicKey))
                {
                    using var rsa = RSA.Create();
                    rsa.ImportFromPem(publicKey);
                    signingKey = new RsaSecurityKey(rsa.ExportParameters(false));
                }

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    RequireSignedTokens = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = signingKey,
                    ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                    ValidateIssuer = true,
                    ValidIssuer = issuer == DevelopmentIssuer && !environment.IsDevelopment() ? null : issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt["Audience"],
                    RequireExpirationTime = true,
                    ValidateLifetime = true,
                    RoleClaimType = jwt["RoleClaim"] ?? "role",
                    ClockSkew = TimeSpan.FromSeconds(30)
                };
            });

        var signedIn = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireAssertion(context => !string.IsNullOrWhiteSpace(context.User.FindFirst("sub")?.Value))
            .Build();

        services.AddAuthorizationBuilder()
            .SetDefaultPolicy(signedIn)
            .AddPolicy(Citizen, policy => policy.Combine(signedIn).RequireRole(Citizen))
            .AddPolicy(Manager, policy => policy.Combine(signedIn).RequireRole(Manager))
            .AddPolicy(ReminderRuleReader, policy => policy.Combine(signedIn).RequireRole(Citizen, Manager));
    }
}
