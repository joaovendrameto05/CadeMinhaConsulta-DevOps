using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using CadeMinhaConsulta.Api.Features.ReminderRules;
using CadeMinhaConsulta.Api.Infrastructure;
using Xunit;

namespace CadeMinhaConsulta.InfrastructureTests;

public sealed class ReminderRuleTests : IDisposable
{
    private const string Path = "/api/reminder-rules";
    private const string Issuer = "https://issuer.example.test";
    private const string Audience = "cade-minha-consulta-reminders-test";
    private static readonly ReminderRuleInput Valid = new("Consulta em dois dias", 2, "Confira o horário.", true);
    private readonly RSA signingKey = RSA.Create(2048);
    private readonly TestStore store = new();
    private readonly TestClock clock = new();

    [Fact]
    public async Task ManagerCanCreateReadUpdateAndDeleteAndCitizenCanRead()
    {
        using var factory = CreateFactory();
        using var manager = CreateClient(factory, "GESTOR");
        using var citizen = CreateClient(factory, "CIDADAO");

        using var created = await PostAsync(manager, "crud", Valid);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var first = (await created.Content.ReadFromJsonAsync<ReminderRuleResponse>())!;
        Assert.Equal(clock.UtcNow.UtcDateTime, first.CreatedAtUtc);
        Assert.Equal(first.CreatedAtUtc, first.UpdatedAtUtc);
        var location = created.Headers.Location!.ToString();
        Assert.Equal(first, Assert.Single((await citizen.GetFromJsonAsync<ReminderRuleResponse[]>(Path))!));
        Assert.Equal(first, await citizen.GetFromJsonAsync<ReminderRuleResponse>(location));

        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        using var edited = await manager.PutAsJsonAsync(location, Valid with { DaysBefore = 5, IsActive = false });
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        var changed = (await edited.Content.ReadFromJsonAsync<ReminderRuleResponse>())!;
        Assert.Equal(first.Id, changed.Id);
        Assert.Equal(first.CreatedAtUtc, changed.CreatedAtUtc);
        Assert.Equal(clock.UtcNow.UtcDateTime, changed.UpdatedAtUtc);
        Assert.Equal(5, changed.DaysBefore);
        Assert.False(changed.IsActive);

        using var deleted = await manager.DeleteAsync(location);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        using var absent = await citizen.GetAsync(location);
        Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);
        Assert.Empty((await citizen.GetFromJsonAsync<ReminderRuleResponse[]>(Path))!);
        using var repeatedDelete = await manager.DeleteAsync(location);
        using var editDeleted = await manager.PutAsJsonAsync(location, Valid);
        Assert.Equal(HttpStatusCode.NotFound, repeatedDelete.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, editDeleted.StatusCode);

        using var replay = await PostAsync(manager, "crud", Valid);
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.Equal(created.Headers.Location, replay.Headers.Location);
        Assert.Equal(first, await replay.Content.ReadFromJsonAsync<ReminderRuleResponse>());
        Assert.Empty((await manager.GetFromJsonAsync<ReminderRuleResponse[]>(Path))!);
    }

    [Fact]
    public async Task ValidationReachesSwaggerClientsAsProblemDetailsAndDoesNotReserveKey()
    {
        using var factory = CreateFactory();
        using var manager = CreateClient(factory, "GESTOR");
        using var invalid = await PostAsync(manager, "bad", new ReminderRuleInput(" ", -1, "", null));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal("application/problem+json", invalid.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await invalid.Content.ReadAsStringAsync());
        var errors = body.RootElement.GetProperty("errors");
        Assert.True(errors.TryGetProperty("name", out _));
        Assert.True(errors.TryGetProperty("daysBefore", out _));
        Assert.True(errors.TryGetProperty("message", out _));
        Assert.True(errors.TryGetProperty("isActive", out _));

        using var missingKey = await PostAsync(manager, null, Valid);
        using var invalidKey = await PostAsync(manager, "bad,key", Valid);
        Assert.Equal(HttpStatusCode.BadRequest, missingKey.StatusCode);
        Assert.Equal("application/problem+json", missingKey.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.BadRequest, invalidKey.StatusCode);
        Assert.Empty(store.Rules);
        using var valid = await PostAsync(manager, "bad", Valid);
        Assert.Equal(HttpStatusCode.Created, valid.StatusCode);

        using var invalidEdit = await manager.PutAsJsonAsync(valid.Headers.Location, Valid with { DaysBefore = 366 });
        Assert.Equal(HttpStatusCode.BadRequest, invalidEdit.StatusCode);
        Assert.Equal(2, Assert.Single(store.Rules.Values).Current.DaysBefore);
    }

    [Fact]
    public async Task ReplayUsesOriginalBodyAndLocationAndDifferentContentConflictsWithinSubject()
    {
        using var factory = CreateFactory();
        using var manager = CreateClient(factory, "GESTOR");
        using var secondManager = CreateClient(factory, "GESTOR", "second");

        using var created = await PostAsync(manager, "same", Valid);
        using var replay = await PostAsync(manager, "same", Valid with { Name = $" {Valid.Name} " });
        using var conflict = await PostAsync(manager, "same", Valid with { Message = "Outro." });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.Equal(created.Headers.Location, replay.Headers.Location);
        Assert.Equal(await created.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal("application/problem+json", conflict.Content.Headers.ContentType?.MediaType);

        using var differentUser = await PostAsync(secondManager, "same", Valid);
        Assert.Equal(HttpStatusCode.Created, differentUser.StatusCode);
        Assert.NotEqual(created.Headers.Location, differentUser.Headers.Location);
        Assert.Equal(2, store.Rules.Count);
    }

    [Fact]
    public async Task ConcurrentRequestsUseOneRuleAndConflictingContentWinsOnlyOnce()
    {
        using var factory = CreateFactory();
        using var manager = CreateClient(factory, "GESTOR");

        var responses = await Task.WhenAll(Enumerable.Range(0, 12)
            .Select(i => PostAsync(manager, "concurrent", Valid with { DaysBefore = i % 2 })));
        try
        {
            var successes = responses.Where(response => response.StatusCode == HttpStatusCode.Created).ToArray();
            Assert.Equal(6, successes.Length);
            Assert.Single(successes.Select(response => response.Headers.Location).Distinct());
            Assert.Equal(6, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
            Assert.Single(store.Rules);
        }
        finally
        {
            foreach (var response in responses)
                response.Dispose();
        }
    }

    [Fact]
    public async Task RetryingAfterSimulatedLostAcknowledgementReplaysTheAlreadyStoredRule()
    {
        store.FailAfterNextInsert = 1;
        using var factory = CreateFactory();
        using var manager = CreateClient(factory, "GESTOR");
        using var failed = await PostAsync(manager, "uncertain", Valid);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        Assert.DoesNotContain("private-driver-details", await failed.Content.ReadAsStringAsync());
        var committed = Assert.Single(store.Rules.Values);

        using var retried = await PostAsync(manager, "uncertain", Valid);
        Assert.Equal(HttpStatusCode.Created, retried.StatusCode);
        Assert.Equal(committed.OriginalResponse(), await retried.Content.ReadFromJsonAsync<ReminderRuleResponse>());
        Assert.Single(store.Rules);
    }

    [Fact]
    public async Task AnonymousReceives401AndCitizenCannotWrite()
    {
        using var factory = CreateFactory();
        using var anonymous = factory.CreateClient();
        using var citizen = CreateClient(factory, "CIDADAO");
        using var unknown = CreateClient(factory, "OUTRO");
        using var readAnonymous = await anonymous.GetAsync(Path);
        using var createAnonymous = await PostAsync(anonymous, "auth", Valid);
        using var createCitizen = await PostAsync(citizen, "auth", Valid);
        using var updateCitizen = await citizen.PutAsJsonAsync($"/api/reminder-rules/{new string('a', 64)}", Valid);
        using var deleteCitizen = await citizen.DeleteAsync($"/api/reminder-rules/{new string('a', 64)}");
        using var readUnknown = await unknown.GetAsync(Path);

        Assert.Equal(HttpStatusCode.Unauthorized, readAnonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, createAnonymous.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, createCitizen.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, updateCitizen.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, deleteCitizen.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, readUnknown.StatusCode);
        Assert.Empty(store.Rules);
    }

    [Fact]
    public async Task RateLimitIsPerSubjectAndAnonymousRequestsStillReturn401()
    {
        using var factory = CreateFactory(permitLimit: 1);
        using var anonymous = factory.CreateClient();
        using var manager = CreateClient(factory, "GESTOR");
        using var secondManager = CreateClient(factory, "GESTOR", "second");
        using var anonymousFirst = await anonymous.GetAsync(Path);
        using var first = await manager.GetAsync(Path);
        using var limited = await manager.GetAsync(Path);
        using var other = await secondManager.GetAsync(Path);
        using var anonymousAgain = await anonymous.GetAsync(Path);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymousFirst.StatusCode);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.NotNull(limited.Headers.RetryAfter?.Delta);
        Assert.Equal("application/problem+json", limited.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousAgain.StatusCode);
    }

    [Fact]
    public async Task SwaggerDocumentsAllOperationsAndRequiredIdempotencyHeader()
    {
        using var factory = CreateFactory();
        using var anonymous = factory.CreateClient();
        using var response = await anonymous.GetAsync("/swagger/v1/swagger.json");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");
        var listAndCreate = paths.GetProperty(Path);
        Assert.True(listAndCreate.GetProperty("get").GetProperty("responses").TryGetProperty("200", out _));
        var post = listAndCreate.GetProperty("post");
        var header = Assert.Single(post.GetProperty("parameters").EnumerateArray());
        Assert.Equal("header", header.GetProperty("in").GetString());
        Assert.Equal("Idempotency-Key", header.GetProperty("name").GetString());
        Assert.True(header.GetProperty("required").GetBoolean());
        Assert.True(post.GetProperty("responses").TryGetProperty("409", out _));
        Assert.True(post.GetProperty("responses").TryGetProperty("429", out _));
        var detail = paths.GetProperty($"{Path}/{{id}}");
        Assert.True(detail.TryGetProperty("get", out _));
        Assert.True(detail.TryGetProperty("put", out _));
        Assert.True(detail.TryGetProperty("delete", out _));
    }

    [Theory]
    [InlineData(true, HttpStatusCode.OK)]
    [InlineData(false, HttpStatusCode.Unauthorized)]
    public async Task DevelopmentIssuerOnlyWorksInDevelopment(bool development, HttpStatusCode expected)
    {
        using var factory = CreateFactory(development: development, issuer: JwtAuthentication.DevelopmentIssuer);
        using var manager = CreateClient(factory, "GESTOR", issuer: JwtAuthentication.DevelopmentIssuer);
        using var response = await manager.GetAsync(Path);
        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task RealAdapterReportsMissingMongoConfigurationAs503()
    {
        using var factory = CreateFactory(useMongo: true);
        using var manager = CreateClient(factory, "GESTOR");
        using var read = await manager.GetAsync(Path);
        using var create = await PostAsync(manager, "unconfigured", Valid);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, read.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, create.StatusCode);
    }

    [Fact]
    public void MongoDocumentPreservesOriginalResponseAndUsesIdempotencyIdAsPrimaryKey()
    {
        var (values, errors) = Valid.Validate();
        Assert.Empty(errors);
        var now = clock.UtcNow.UtcDateTime;
        var original = new ReminderRule(ReminderRule.IdForCreate(Issuer, "subject", "key"), values!,
            values!, now, now, null);
        var edited = original with { Current = values! with { DaysBefore = 6 }, UpdatedAtUtc = now.AddMinutes(1) };
        var document = edited.ToBsonDocument();
        var roundTrip = BsonSerializer.Deserialize<ReminderRule>(document);

        Assert.Equal(original.Id, document["_id"].AsString);
        Assert.Equal(2, roundTrip.OriginalResponse().DaysBefore);
        Assert.Equal(6, roundTrip.ToResponse().DaysBefore);
        Assert.Equal(original.OriginalResponse(), roundTrip.OriginalResponse());
        Assert.NotEqual(original.Id, ReminderRule.IdForCreate(Issuer, "another-subject", "key"));
    }

    [RealMongoFact]
    [Trait("Dependency", "MongoDB")]
    public async Task RealMongoSerializesConcurrentCreationAndReplaysAfterHostRestart()
    {
        var mongoUri = Environment.GetEnvironmentVariable(RealMongoFactAttribute.UriVariable)!;
        var databaseName = Environment.GetEnvironmentVariable(RealMongoFactAttribute.DatabaseVariable)!;
        Assert.StartsWith("vagacerta_test_", databaseName);
        var subject = Guid.NewGuid().ToString("N");
        const string key = "restart";
        var expectedId = ReminderRule.IdForCreate(Issuer, subject, key);
        var settings = MongoClientSettings.FromConnectionString(mongoUri);
        settings.ServerSelectionTimeout = TimeSpan.FromSeconds(5);
        using var mongoClient = new MongoClient(settings);
        var collection = mongoClient.GetDatabase(databaseName)
            .GetCollection<ReminderRule>(MongoReminderRuleStore.CollectionName);
        var byId = Builders<ReminderRule>.Filter.Eq(rule => rule.Id, expectedId);
        try
        {
            string originalBody;
            using (var firstHost = CreateFactory(useMongo: true, mongoUri: mongoUri, mongoDatabase: databaseName))
            using (var manager = CreateClient(firstHost, "GESTOR", subject))
            {
                var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => PostAsync(manager, key, Valid)));
                try
                {
                    Assert.All(responses, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
                    originalBody = await responses[0].Content.ReadAsStringAsync();
                    foreach (var response in responses)
                        Assert.Equal(originalBody, await response.Content.ReadAsStringAsync());
                    Assert.Equal(1, await collection.CountDocumentsAsync(byId));
                    using var indexes = await collection.Indexes.ListAsync();
                    Assert.Contains(await indexes.ToListAsync(), index =>
                        index["name"].AsString == "_id_" && index["key"].AsBsonDocument["_id"].AsInt32 == 1);
                }
                finally
                {
                    foreach (var response in responses)
                        response.Dispose();
                }
            }

            using var restartedHost = CreateFactory(useMongo: true, mongoUri: mongoUri, mongoDatabase: databaseName);
            using var restartedManager = CreateClient(restartedHost, "GESTOR", subject);
            using var replay = await PostAsync(restartedManager, key, Valid);
            Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
            Assert.Equal($"{Path}/{expectedId}", replay.Headers.Location?.ToString());
            Assert.Equal(originalBody, await replay.Content.ReadAsStringAsync());
            using var conflict = await PostAsync(restartedManager, key, Valid with { DaysBefore = 3 });
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);

            using var edited = await restartedManager.PutAsJsonAsync(replay.Headers.Location, Valid with { DaysBefore = 6 });
            Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
            Assert.Equal(6, (await edited.Content.ReadFromJsonAsync<ReminderRuleResponse>())!.DaysBefore);
            using var deleted = await restartedManager.DeleteAsync(replay.Headers.Location);
            Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
            using var absent = await restartedManager.GetAsync(replay.Headers.Location);
            Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);
            using var replayAfterDelete = await PostAsync(restartedManager, key, Valid);
            Assert.Equal(HttpStatusCode.Created, replayAfterDelete.StatusCode);
            Assert.Equal(originalBody, await replayAfterDelete.Content.ReadAsStringAsync());
            Assert.Equal(1, await collection.CountDocumentsAsync(byId));
        }
        finally
        {
            await collection.DeleteOneAsync(byId);
        }
    }

    private WebApplicationFactory<Program> CreateFactory(int permitLimit = 60, bool development = true,
        string issuer = Issuer, bool useMongo = false, string? mongoUri = null, string? mongoDatabase = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(development ? "Development" : "Production");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Oracle"] = null,
                    ["ConnectionStrings:Mongo"] = mongoUri,
                    ["Mongo:Database"] = mongoDatabase,
                    ["Jwt:Issuer"] = issuer,
                    ["Jwt:Audience"] = Audience,
                    ["Jwt:RoleClaim"] = "roles",
                    ["Jwt:PublicKeyPem"] = signingKey.ExportSubjectPublicKeyInfoPem(),
                    ["RateLimiting:PermitLimit"] = permitLimit.ToString()
                }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<TimeProvider>().AddSingleton<TimeProvider>(clock);
                if (!useMongo)
                    services.RemoveAll<IReminderRuleStore>().AddSingleton<IReminderRuleStore>(store);
            });
        });

    private HttpClient CreateClient(WebApplicationFactory<Program> factory, string role,
        string subject = "first", string issuer = Issuer)
    {
        var client = factory.CreateClient();
        var token = new JwtSecurityToken(issuer, Audience, [new Claim("sub", subject), new Claim("roles", role)],
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(new RsaSecurityKey(signingKey), SecurityAlgorithms.RsaSha256));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        return client;
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string? key, ReminderRuleInput values)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Path) { Content = JsonContent.Create(values) };
        if (key is not null)
            request.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(request);
    }

    public void Dispose() => signingKey.Dispose();

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class TestStore : IReminderRuleStore
    {
        public ConcurrentDictionary<string, ReminderRule> Rules { get; } = new();
        public int FailAfterNextInsert;

        public async Task<ReminderRule> CreateOrGetAsync(ReminderRule candidate, CancellationToken cancellationToken)
        {
            await Task.Yield();
            var result = Rules.GetOrAdd(candidate.Id, candidate);
            if (Interlocked.Exchange(ref FailAfterNextInsert, 0) == 1)
                throw new TimeoutException("private-driver-details");
            return result;
        }

        public Task<List<ReminderRule>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Rules.Values.Where(rule => rule.DeletedAtUtc is null).ToList());

        public Task<ReminderRule?> FindAsync(string id, CancellationToken cancellationToken) =>
            Task.FromResult(Rules.TryGetValue(id, out var rule) && rule.DeletedAtUtc is null ? rule : null);

        public async Task<ReminderRule?> UpdateAsync(
            string id, ReminderRuleValues values, DateTime nowUtc, CancellationToken cancellationToken)
        {
            var rule = await FindAsync(id, cancellationToken);
            if (rule is null)
                return null;
            var edited = rule with { Current = values, UpdatedAtUtc = nowUtc };
            return Rules.TryUpdate(id, edited, rule) ? edited : null;
        }

        public async Task<bool> DeleteAsync(string id, DateTime nowUtc, CancellationToken cancellationToken)
        {
            var rule = await FindAsync(id, cancellationToken);
            return rule is not null && Rules.TryUpdate(id, rule with { DeletedAtUtc = nowUtc }, rule);
        }
    }
}

internal sealed class RealMongoFactAttribute : FactAttribute
{
    public const string UriVariable = "VAGACERTA_TEST_MONGO_URI";
    public const string DatabaseVariable = "VAGACERTA_TEST_MONGO_DATABASE";

    public RealMongoFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(UriVariable)) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(DatabaseVariable)))
            Skip = "Configure as variáveis do MongoDB de testes para verificar a persistência real.";
    }
}
