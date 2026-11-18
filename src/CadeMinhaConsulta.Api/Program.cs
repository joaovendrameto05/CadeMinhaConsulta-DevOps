using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.OpenApi;
using MongoDB.Driver;
using CadeMinhaConsulta.Api.Features.Notifications;
using CadeMinhaConsulta.Api.Features.ReminderRules;
using CadeMinhaConsulta.Api.Features.OracleCrud;
using CadeMinhaConsulta.Api.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddProblemDetails();
builder.Services.AddCadeMinhaConsultaAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IPatientAppointments, OraclePatientAppointments>();
builder.Services.AddScoped<INotificationStore, MongoNotificationStore>();
builder.Services.AddScoped<IReminderRuleStore, MongoReminderRuleStore>();

builder.Services.AddDbContext<OracleReadDbContext>((services, options) =>
{
    var oracleConnection = services.GetRequiredService<IConfiguration>().GetConnectionString("Oracle");
    if (!string.IsNullOrWhiteSpace(oracleConnection))
        options.UseOracle(oracleConnection);
});

builder.Services.AddSingleton<IMongoClient>(services =>
{
    var connection = services.GetRequiredService<IConfiguration>().GetConnectionString("Mongo");
    var settings = MongoClientSettings.FromConnectionString(
        connection ?? throw new InvalidOperationException("Configure ConnectionStrings:Mongo."));
    settings.ServerSelectionTimeout = TimeSpan.FromSeconds(5);
    return new MongoClient(settings);
});
builder.Services.AddSingleton(services =>
{
    var database = services.GetRequiredService<IConfiguration>()["Mongo:Database"];
    return services.GetRequiredService<IMongoClient>()
        .GetDatabase(database ?? throw new InvalidOperationException("Configure Mongo:Database."));
});

builder.Services.AddHealthChecks()
    .AddCheck<OracleHealthCheck>("oracle", tags: ["ready"], timeout: TimeSpan.FromSeconds(3))
    .AddCheck<MongoHealthCheck>("mongo", tags: ["ready"], timeout: TimeSpan.FromSeconds(3));

builder.Services.AddRateLimiter();
builder.Services.AddOptions<RateLimiterOptions>().Configure<IConfiguration>((options, configuration) =>
{
    var permitLimit = configuration.GetValue("RateLimiting:PermitLimit", 60);
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("per-subject", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirst("sub")!.Value,
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        }));
    options.OnRejected = (context, _) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            var seconds = (int)Math.Ceiling(retryAfter.TotalSeconds);
            context.HttpContext.Response.Headers.RetryAfter =
                seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return ValueTask.CompletedTask;
    };
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "Cadê Minha Consulta API", Version = "v1" });
    options.AddSecurityDefinition("bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });
    options.OperationFilter<SwaggerAuthorizationFilter>();
});

var app = builder.Build();
app.UseExceptionHandler(new ExceptionHandlerOptions
{
    StatusCodeSelector = exception => exception is BadHttpRequestException badRequest
        ? badRequest.StatusCode
        : StatusCodes.Status500InternalServerError
});
app.UseWhen(context => context.Request.Path.StartsWithSegments("/api/reminder-rules"),
    branch => branch.UseStatusCodePages());

app.UseSwagger();
app.UseSwaggerUI();

if (app.Environment.IsDevelopment())
{
    app.MapGet("/api/diagnostics/auth", () => TypedResults.NoContent())
        .RequireAuthorization()
        .RequireRateLimiting("per-subject")
        .WithSummary("Valida um JWT sem consultar dados do usuário.")
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status429TooManyRequests);
}

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapGet("/health/live", (HttpResponse response) =>
    {
        response.Headers.CacheControl = "no-store";
        return TypedResults.Text("Healthy");
    })
    .WithTags("Saúde")
    .WithSummary("Verifica se o processo da API responde.")
    .AllowAnonymous();

app.MapGet("/health/ready", async (
        HealthCheckService healthChecks, HttpResponse response, CancellationToken cancellationToken) =>
    {
        response.Headers.CacheControl = "no-store";
        var report = await healthChecks.CheckHealthAsync(
            registration => registration.Tags.Contains("ready"), cancellationToken);
        var status = report.Status == HealthStatus.Healthy
            ? StatusCodes.Status200OK
            : StatusCodes.Status503ServiceUnavailable;
        var body = new ReadinessResponse(report.Status.ToString(),
            report.Entries.ToDictionary(entry => entry.Key, entry => entry.Value.Status.ToString()));
        return TypedResults.Json(body, statusCode: status);
    })
    .WithTags("Saúde")
    .WithSummary("Verifica a prontidão do Oracle e do MongoDB.")
    .Produces<ReadinessResponse>(StatusCodes.Status200OK)
    .Produces<ReadinessResponse>(StatusCodes.Status503ServiceUnavailable)
    .AllowAnonymous();

app.MapNotificationEndpoints();
app.MapReminderRuleEndpoints();
app.MapOracleCrudEndpoints();

app.Run();

public partial class Program { }

internal sealed record ReadinessResponse(string Status, Dictionary<string, string> Checks);