using Microsoft.EntityFrameworkCore;
using CadeMinhaConsulta.Api.Features.Notifications;

namespace CadeMinhaConsulta.Api.Infrastructure;

internal sealed class OraclePatientAppointments(OracleReadDbContext database, IConfiguration configuration)
    : IPatientAppointments
{
    public Task<List<ConsultaOracle>> ListAsync(decimal ownerId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(configuration.GetConnectionString("Oracle")) ||
            string.IsNullOrWhiteSpace(configuration["Oracle:Schema"]))
            throw new NotificationDependencyUnavailableException();

        return ForOwner(database, ownerId).ToListAsync(cancellationToken);
    }

    internal static IQueryable<ConsultaOracle> ForOwner(OracleReadDbContext database, decimal ownerId) =>
        database.Consultas.Where(consulta => consulta.IdUsuario == ownerId);
}
