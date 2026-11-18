using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MongoDB.Bson;
using MongoDB.Driver;

namespace CadeMinhaConsulta.Api.Infrastructure;

internal sealed class OracleHealthCheck(
    OracleReadDbContext database, IConfiguration configuration) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(configuration.GetConnectionString("Oracle")) ||
            string.IsNullOrWhiteSpace(configuration["Oracle:Schema"]))
            return HealthCheckResult.Unhealthy("Configuração do Oracle incompleta.");

        await database.Consultas.Take(1).ToListAsync(cancellationToken);
        return HealthCheckResult.Healthy();
    }
}

internal sealed class MongoHealthCheck(
    IServiceProvider services, IConfiguration configuration) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(configuration.GetConnectionString("Mongo")) ||
            string.IsNullOrWhiteSpace(configuration["Mongo:Database"]))
            return HealthCheckResult.Unhealthy("Configuração do MongoDB incompleta.");

        var database = services.GetRequiredService<IMongoDatabase>();
        await database.RunCommandAsync<BsonDocument>(
            new BsonDocument("ping", 1), cancellationToken: cancellationToken);
        return HealthCheckResult.Healthy();
    }
}
