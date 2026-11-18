using Microsoft.EntityFrameworkCore;

namespace CadeMinhaConsulta.Api.Infrastructure;

public sealed class OracleReadDbContext(
    DbContextOptions<OracleReadDbContext> options, IConfiguration configuration) : DbContext(options)
{
    public IQueryable<ConsultaOracle> Consultas => Set<ConsultaOracle>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ConsultaOracle>(entity =>
        {
            entity.HasNoKey();
            entity.ToView("TB_GS_CONSULTA", configuration["Oracle:Schema"]);
            entity.Property(row => row.IdConsulta).HasColumnName("ID_CONSULTA").HasColumnType("NUMBER(38,0)");
            entity.Property(row => row.IdUsuario).HasColumnName("ID_USUARIO").HasColumnType("NUMBER(38,0)");
            entity.Property(row => row.Status).HasColumnName("STATUS_CONSULTA").HasColumnType("VARCHAR2(10 CHAR)");
            entity.Property(row => row.DataHora).HasColumnName("DATA_HORA_CONSULTA").HasColumnType("DATE");
        });
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess) =>
        throw new NotSupportedException("O DBv3 é somente leitura nesta API.");

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("O DBv3 é somente leitura nesta API.");
}

public sealed class ConsultaOracle
{
    public decimal IdConsulta { get; init; }
    public decimal IdUsuario { get; init; }
    public required string Status { get; init; }
    public DateTime DataHora { get; init; }
}
