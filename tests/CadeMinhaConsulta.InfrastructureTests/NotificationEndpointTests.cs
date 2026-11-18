using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Driver;
using CadeMinhaConsulta.Api.Features.Notifications;
using CadeMinhaConsulta.Api.Infrastructure;
using Xunit;
using static CadeMinhaConsulta.InfrastructureTests.NotificationProjectionTests;

namespace CadeMinhaConsulta.InfrastructureTests;

public sealed class NotificationEndpointTests : IDisposable
{
    private const string Issuer = "https://issuer.example.test";
    private const string Audience = "cade-minha-consulta-notifications-test";
    private const string ListPath = "/api/notifications/me";
    private readonly RSA signingKey = RSA.Create(2048);
    private readonly TestAppointments oracle = new();
    private readonly TestNotifications mongo = new();

    [Fact]
    public async Task ListUsesOwnerClaimAndKeepsOneNotificationPerObservedState()
    {
        oracle.Snapshots =
        [
            Row(101, 11, "AGENDADA"),
            Row(101, 12, "FALTA"),
            Row(102, 13, "CANCELADA")
        ];
        using var factory = CreateFactory();
        using var patient = CreateClient(factory, "101");
        using var otherPatient = CreateClient(factory, "102");

        var first = Assert.Single(await ReadListAsync(patient));
        var repeated = Assert.Single(await ReadListAsync(patient));
        var other = Assert.Single(await ReadListAsync(otherPatient));
        Assert.Equal("11", first.AppointmentId);
        Assert.Equal("AGENDADA", first.Status);
        Assert.Equal(first, repeated);
        Assert.Equal("13", other.AppointmentId);

        oracle.Snapshots = [Row(101, 11, "CONFIRMADA"), Row(102, 13, "CANCELADA")];
        var afterChange = await ReadListAsync(patient);

        Assert.Equal(2, afterChange.Length);
        Assert.Contains(first, afterChange);
        Assert.Contains(afterChange, notification => notification.Status == "CONFIRMADA");
        Assert.Equal(new decimal[] { 101, 101, 102, 101 }, oracle.RequestedOwners.ToArray());
    }

    [Fact]
    public async Task NoConsultationsReturnsEmptyArray()
    {
        using var factory = CreateFactory();
        using var patient = CreateClient(factory, "101");

        Assert.Empty(await ReadListAsync(patient));
        Assert.Empty(mongo.Items);
    }

    [Fact]
    public async Task ReadIsIdempotentAndForeignOrMissingIdsReturn404()
    {
        oracle.Snapshots = [Row(101, 11, "AGENDADA")];
        using var factory = CreateFactory();
        using var patient = CreateClient(factory, "101");
        using var otherPatient = CreateClient(factory, "102");
        var item = Assert.Single(await ReadListAsync(patient));
        Assert.False(item.IsRead);
        var path = $"/api/notifications/{item.Id}/read";

        using var foreign = await otherPatient.PatchAsync(path, null);
        using var missing = await patient.PatchAsync($"/api/notifications/{new string('a', 64)}/read", null);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.False(Assert.Single(mongo.Items.Values).IsRead);

        using var read = await patient.PatchAsync(path, null);
        using var repeated = await patient.PatchAsync(path, null);
        Assert.Equal(HttpStatusCode.NoContent, read.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, repeated.StatusCode);
        Assert.Equal(item with { IsRead = true }, Assert.Single(await ReadListAsync(patient)));
        Assert.Single(mongo.Items);
    }

    [Fact]
    public async Task ConcurrentListsForSameAppointmentShareOneStableId()
    {
        const int requests = 8;
        oracle.Snapshots = [Row(101, 11, "AGENDADA")];
        mongo.DelayUpsertUntilAllArrive(requests);
        using var factory = CreateFactory();
        using var patient = CreateClient(factory, "101");

        var results = await Task.WhenAll(Enumerable.Range(0, requests).Select(_ => ReadListAsync(patient)));

        var item = Assert.Single(mongo.Items.Values);
        Assert.All(results, list => Assert.Equal(item.Id, Assert.Single(list).Id));
        Assert.Equal(requests, oracle.RequestedOwners.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("opaque-user")]
    [InlineData("0")]
    public async Task MissingOrInvalidOwnerNeverQueriesDatabases(string? owner)
    {
        using var factory = CreateFactory();
        using var patient = CreateClient(factory, owner);
        using var list = await patient.GetAsync(ListPath);
        using var read = await patient.PatchAsync($"/api/notifications/{new string('a', 64)}/read", null);

        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
        Assert.Empty(oracle.RequestedOwners);
        Assert.Empty(mongo.Items);
    }

    [Fact]
    public async Task BothEndpointsRequireAuthenticationAndDiagnosticIsUnavailableInProduction()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var list = await client.GetAsync(ListPath);
        using var read = await client.PatchAsync($"/api/notifications/{new string('a', 64)}/read", null);
        using var diagnostics = await client.GetAsync("/api/diagnostics/auth");

        Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, read.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, diagnostics.StatusCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DependencyFailureReturns503WithoutLeakingDetails(bool oracleFails)
    {
        oracle.Snapshots = [Row(101, 11, "AGENDADA")];
        oracle.Fails = oracleFails;
        mongo.Fails = !oracleFails;
        using var factory = CreateFactory();
        using var patient = CreateClient(factory, "101");
        using var list = await patient.GetAsync(ListPath);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, list.StatusCode);
        Assert.DoesNotContain("test-private-detail", await list.Content.ReadAsStringAsync());
        Assert.Empty(mongo.Items);

        if (!oracleFails)
        {
            using var read = await patient.PatchAsync($"/api/notifications/{new string('a', 64)}/read", null);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, read.StatusCode);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ActualAdapterReturns503WhenItsConnectionIsMissing(bool useActualOracle)
    {
        oracle.Snapshots = [Row(101, 11, "AGENDADA")];
        using var factory = CreateFactory(useActualOracle: useActualOracle, useActualMongo: !useActualOracle);
        using var patient = CreateClient(factory, "101");
        using var response = await patient.GetAsync(ListPath);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [RealMongoFact]
    [Trait("Dependency", "MongoDB")]
    public async Task RealMongoPersistsOneNotificationAndReadStateAcrossHostRestart()
    {
        var uri = Environment.GetEnvironmentVariable(RealMongoFactAttribute.UriVariable)!;
        var databaseName = Environment.GetEnvironmentVariable(RealMongoFactAttribute.DatabaseVariable)!;
        Assert.StartsWith("vagacerta_test_", databaseName);
        var owner = RandomNumberGenerator.GetInt32(10_000, int.MaxValue - 1);
        var snapshot = Row(owner, owner, "CONFIRMADA");
        oracle.Snapshots = [snapshot];
        var expected = Notification.FromAppointment(snapshot, DateTime.UtcNow)!;
        var settings = MongoClientSettings.FromConnectionString(uri);
        settings.ServerSelectionTimeout = TimeSpan.FromSeconds(5);
        using var mongoClient = new MongoClient(settings);
        var collection = mongoClient.GetDatabase(databaseName).GetCollection<Notification>("notifications");
        var byId = Builders<Notification>.Filter.Eq(item => item.Id, expected.Id);

        try
        {
            using (var firstHost = CreateFactory(useActualMongo: true, mongoUri: uri, mongoDatabase: databaseName))
            using (var patient = CreateClient(firstHost, expected.OwnerId))
            using (var other = CreateClient(firstHost, (owner + 1).ToString()))
            {
                var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => ReadListAsync(patient)));
                Assert.All(results, list => Assert.Equal(expected.Id, Assert.Single(list).Id));
                Assert.Equal(1, await collection.CountDocumentsAsync(byId));
                using var indexes = await collection.Indexes.ListAsync();
                Assert.Contains(await indexes.ToListAsync(), index =>
                    index["name"].AsString == "_id_" && index["key"].AsBsonDocument["_id"].AsInt32 == 1);

                var readPath = $"/api/notifications/{expected.Id}/read";
                using var foreignRead = await other.PatchAsync(readPath, null);
                Assert.Equal(HttpStatusCode.NotFound, foreignRead.StatusCode);
                Assert.Empty(await ReadListAsync(other));
                for (var attempt = 0; attempt < 2; attempt++)
                {
                    using var read = await patient.PatchAsync(readPath, null);
                    Assert.Equal(HttpStatusCode.NoContent, read.StatusCode);
                }
            }

            using var restartedHost = CreateFactory(useActualMongo: true, mongoUri: uri, mongoDatabase: databaseName);
            using var restartedPatient = CreateClient(restartedHost, expected.OwnerId);
            var afterRestart = Assert.Single(await ReadListAsync(restartedPatient));
            Assert.Equal(expected.Id, afterRestart.Id);
            Assert.True(afterRestart.IsRead);
            Assert.True((await collection.Find(byId).SingleAsync()).IsRead);
        }
        finally
        {
            await collection.DeleteOneAsync(byId);
        }
    }

    private WebApplicationFactory<Program> CreateFactory(bool useActualOracle = false, bool useActualMongo = false,
        string? mongoUri = null, string? mongoDatabase = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Oracle"] = null,
                    ["ConnectionStrings:Mongo"] = mongoUri,
                    ["Mongo:Database"] = mongoDatabase,
                    ["Jwt:Issuer"] = Issuer,
                    ["Jwt:Audience"] = Audience,
                    ["Jwt:PublicKeyPem"] = signingKey.ExportSubjectPublicKeyInfoPem(),
                    ["Jwt:OracleUserIdClaim"] = "patient_id",
                    ["RateLimiting:PermitLimit"] = "60"
                }));
            builder.ConfigureServices(services =>
            {
                if (!useActualOracle)
                    services.RemoveAll<IPatientAppointments>().AddSingleton<IPatientAppointments>(oracle);
                if (!useActualMongo)
                    services.RemoveAll<INotificationStore>().AddSingleton<INotificationStore>(mongo);
            });
        });

    private HttpClient CreateClient(WebApplicationFactory<Program> factory, string? owner)
    {
        var client = factory.CreateClient();
        var claims = new List<Claim> { new("sub", $"test-{owner}") };
        if (owner is not null)
            claims.Add(new Claim("patient_id", owner));
        var token = new JwtSecurityToken(Issuer, Audience, claims,
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(new RsaSecurityKey(signingKey), SecurityAlgorithms.RsaSha256));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        return client;
    }

    private static async Task<NotificationResponse[]> ReadListAsync(HttpClient client)
    {
        using var response = await client.GetAsync(ListPath);
        response.EnsureSuccessStatusCode();
        Assert.True(response.Headers.CacheControl?.NoStore);
        return (await response.Content.ReadFromJsonAsync<NotificationResponse[]>())!;
    }

    public void Dispose() => signingKey.Dispose();

    private sealed class TestAppointments : IPatientAppointments
    {
        public List<ConsultaOracle> Snapshots { get; set; } = [];
        public ConcurrentQueue<decimal> RequestedOwners { get; } = new();
        public bool Fails { get; set; }

        public Task<List<ConsultaOracle>> ListAsync(decimal ownerId, CancellationToken cancellationToken)
        {
            RequestedOwners.Enqueue(ownerId);
            if (Fails)
                throw new TimeoutException("test-private-detail");
            return Task.FromResult(Snapshots.Where(row => row.IdUsuario == ownerId).ToList());
        }
    }

    private sealed class TestNotifications : INotificationStore
    {
        private TaskCompletionSource? concurrentUpserts;
        private int pendingUpserts;
        public ConcurrentDictionary<string, Notification> Items { get; } = new();
        public bool Fails { get; set; }

        public void DelayUpsertUntilAllArrive(int count)
        {
            pendingUpserts = count;
            concurrentUpserts = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public async Task UpsertAsync(Notification notification, CancellationToken cancellationToken)
        {
            if (Fails)
                throw new TimeoutException("test-private-detail");

            if (concurrentUpserts is not null)
            {
                if (Interlocked.Decrement(ref pendingUpserts) == 0)
                    concurrentUpserts.SetResult();
                await concurrentUpserts.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            }

            Items.TryAdd(notification.Id, notification);
        }

        public Task<List<Notification>> ListAsync(string ownerId, CancellationToken cancellationToken) =>
            Task.FromResult(Items.Values.Where(item => item.OwnerId == ownerId).ToList());

        public Task<bool> MarkReadAsync(string ownerId, string id, CancellationToken cancellationToken)
        {
            if (Fails)
                throw new TimeoutException("test-private-detail");
            if (!Items.TryGetValue(id, out var item) || item.OwnerId != ownerId)
                return Task.FromResult(false);
            Items[id] = item with { IsRead = true };
            return Task.FromResult(true);
        }
    }
}
