using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using CadeMinhaConsulta.Api.Infrastructure;
using Xunit;

namespace CadeMinhaConsulta.InfrastructureTests;

public sealed class OracleMappingTests
{
    [Fact]
    public void ConsultaMappingProducesSelectFromExistingSchema()
    {
        using var database = CreateContext();
        var entity = database.Model.FindEntityType(typeof(ConsultaOracle));
        Assert.NotNull(entity);
        Assert.Null(entity.FindPrimaryKey());

        var sql = OraclePatientAppointments.ForOwner(database, 101).ToQueryString();

        Assert.Contains("\"DBV3_TEST\".\"TB_GS_CONSULTA\"", sql);
        Assert.Contains("\"ID_CONSULTA\"", sql);
        Assert.Contains("\"ID_USUARIO\"", sql);
        Assert.Contains("\"STATUS_CONSULTA\"", sql);
        Assert.Contains("\"DATA_HORA_CONSULTA\"", sql);
        Assert.Matches("""WHERE\s+\S+\."ID_USUARIO"\s*=\s*:""", sql);
    }

    [Fact]
    public async Task ContextRejectsSaveChanges()
    {
        using var database = CreateContext();

        Assert.Throws<NotSupportedException>(() => database.SaveChanges());
        await Assert.ThrowsAsync<NotSupportedException>(() => database.SaveChangesAsync());
    }

    private static OracleReadDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<OracleReadDbContext>()
            .UseOracle("Data Source=oracle.example.test")
            .Options;
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Oracle:Schema"] = "DBV3_TEST" })
            .Build();
        return new OracleReadDbContext(options, configuration);
    }
}
