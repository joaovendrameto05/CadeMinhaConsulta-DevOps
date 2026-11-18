using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace CadeMinhaConsulta.InfrastructureTests;

public sealed class ApiInfrastructureTests : IDisposable
{
    private const string Issuer = "https://issuer.example.test";
    private const string Audience = "cade-minha-consulta-dotnet-test";
    private const string AuthPath = "/api/diagnostics/auth";
    private readonly RSA signingKey = RSA.Create(2048);

    [Fact]
    public async Task LivenessIsHealthyAndReadinessFailsWithoutDatabases()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        using var live = await client.GetAsync("/health/live");
        using var ready = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
        using var body = JsonDocument.Parse(await ready.Content.ReadAsStringAsync());
        var checks = body.RootElement.GetProperty("checks");
        Assert.Equal("Unhealthy", checks.GetProperty("oracle").GetString());
        Assert.Equal("Unhealthy", checks.GetProperty("mongo").GetString());
    }

    [Theory]
    [InlineData(HealthStatus.Healthy, HttpStatusCode.OK)]
    [InlineData(HealthStatus.Degraded, HttpStatusCode.ServiceUnavailable)]
    public async Task ReadinessReturns200OnlyWhenAllRequiredChecksAreHealthy(
        HealthStatus oracle, HttpStatusCode expected)
    {
        using var factory = CreateFactory().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.PostConfigure<HealthCheckServiceOptions>(options =>
            {
                options.Registrations.Clear();
                options.Registrations.Add(new HealthCheckRegistration("oracle",
                    new StubHealthCheck(oracle), null, ["ready"]));
                options.Registrations.Add(new HealthCheckRegistration("mongo",
                    new StubHealthCheck(HealthStatus.Healthy), null, ["ready"]));
            });
        }));
        using var client = factory.CreateClient();

        using var ready = await client.GetAsync("/health/ready");
        Assert.Equal(expected, ready.StatusCode);
        using var body = JsonDocument.Parse(await ready.Content.ReadAsStringAsync());
        Assert.Equal(oracle.ToString(), body.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task SwaggerDescribesBearerAuthenticationAndRateLimit()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/swagger/v1/swagger.json");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        var root = document.RootElement;
        Assert.Equal("Cadê Minha Consulta API", root.GetProperty("info").GetProperty("title").GetString());
        var bearer = root.GetProperty("components").GetProperty("securitySchemes").GetProperty("bearer");
        Assert.Equal("bearer", bearer.GetProperty("scheme").GetString());
        var paths = root.GetProperty("paths");
        Assert.False(root.TryGetProperty("security", out _));
        var ready = paths.GetProperty("/health/ready").GetProperty("get");
        Assert.False(ready.TryGetProperty("security", out _));
        Assert.True(ready.GetProperty("responses").TryGetProperty("503", out _));
        var live = paths.GetProperty("/health/live").GetProperty("get");
        Assert.False(live.TryGetProperty("security", out _));
        Assert.True(live.GetProperty("responses").TryGetProperty("200", out _));
        var protectedOperation = paths.GetProperty("/api/reminder-rules").GetProperty("post");
        Assert.True(Assert.Single(protectedOperation.GetProperty("security").EnumerateArray())
            .TryGetProperty("bearer", out _));
        var responses = paths.GetProperty(AuthPath)
            .GetProperty("get").GetProperty("responses");
        Assert.True(responses.TryGetProperty("429", out _));

        var list = paths.GetProperty("/api/notifications/me").GetProperty("get").GetProperty("responses");
        Assert.True(list.TryGetProperty("200", out _));
        Assert.True(list.TryGetProperty("403", out _));
        Assert.True(list.TryGetProperty("503", out _));

        var read = paths.GetProperty("/api/notifications/{id}/read").GetProperty("patch").GetProperty("responses");
        Assert.True(read.TryGetProperty("204", out _));
        Assert.True(read.TryGetProperty("404", out _));
    }

    [Fact]
    public async Task ProtectedEndpointRejectsMissingToken()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(AuthPath);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("expiration")]
    [InlineData("signature")]
    public async Task ProtectedEndpointRejectsInvalidToken(string invalidField)
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var otherSigningKey = RSA.Create(2048);
        var token = CreateToken(
            "101",
            invalidField == "issuer" ? "https://other.example.test" : Issuer,
            invalidField == "audience" ? "other-api" : Audience,
            invalidField == "expiration" ? DateTime.UtcNow.AddMinutes(-2) : null,
            invalidField == "signature" ? otherSigningKey : null);

        using var response = await SendTokenAsync(client, token);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ProtectedEndpointRequiresSubject()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var response = await SendTokenAsync(client, CreateToken(null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RateLimitReturns429AndIsIsolatedBySubject()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var token = CreateToken("101");

        using var first = await SendTokenAsync(client, token);
        using var repeated = await SendTokenAsync(client, token);
        using var otherSubject = await SendTokenAsync(client, CreateToken("102"));

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, repeated.StatusCode);
        Assert.NotNull(repeated.Headers.RetryAfter);
        Assert.Equal(HttpStatusCode.NoContent, otherSubject.StatusCode);
    }

    private WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Oracle"] = null,
                    ["Oracle:Schema"] = null,
                    ["ConnectionStrings:Mongo"] = null,
                    ["Mongo:Database"] = null,
                    ["Jwt:Issuer"] = Issuer,
                    ["Jwt:Audience"] = Audience,
                    ["Jwt:PublicKeyPem"] = signingKey.ExportSubjectPublicKeyInfoPem(),
                    ["RateLimiting:PermitLimit"] = "1"
                }));
        });

    private string CreateToken(
        string? subject, string issuer = Issuer, string audience = Audience,
        DateTime? expiration = null, RSA? key = null)
    {
        var token = new JwtSecurityToken(
            issuer, audience, subject is null ? [] : [new Claim("sub", subject)],
            expires: expiration ?? DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(
                new RsaSecurityKey(key ?? signingKey), SecurityAlgorithms.RsaSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static async Task<HttpResponseMessage> SendTokenAsync(HttpClient client, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, AuthPath);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    public void Dispose() => signingKey.Dispose();

    private sealed class StubHealthCheck(HealthStatus status) : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(new HealthCheckResult(status));
    }
}
