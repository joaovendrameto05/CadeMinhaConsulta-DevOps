using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MongoDB.Bson.Serialization.Attributes;
using CadeMinhaConsulta.Api.Infrastructure;

namespace CadeMinhaConsulta.Api.Features.Notifications;

internal sealed record Notification(
    [property: BsonId] string Id,
    [property: BsonElement("ownerId")] string OwnerId,
    [property: BsonElement("appointmentId")] string AppointmentId,
    [property: BsonElement("status")] string Status,
    [property: BsonElement("appointmentAt")] string AppointmentAt,
    [property: BsonElement("observedAtUtc")] DateTime ObservedAtUtc,
    [property: BsonElement("isRead")] bool IsRead)
{
    public static Notification? FromAppointment(ConsultaOracle appointment, DateTime observedAtUtc)
    {
        if (appointment.Status is not ("AGENDADA" or "CONFIRMADA" or "CANCELADA"))
            return null;

        var ownerId = FormatId(appointment.IdUsuario);
        var appointmentId = FormatId(appointment.IdConsulta);
        var key = Encoding.UTF8.GetBytes($"{ownerId}:{appointmentId}:{appointment.Status}");
        return new Notification(
            Convert.ToHexStringLower(SHA256.HashData(key)), ownerId, appointmentId, appointment.Status,
            appointment.DataHora.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
            observedAtUtc, false);
    }

    public static string FormatId(decimal id) => id.ToString("0", CultureInfo.InvariantCulture);
}

internal sealed record NotificationResponse(
    string Id, string AppointmentId, string Status, string AppointmentAt, DateTime ObservedAtUtc, bool IsRead);

internal interface IPatientAppointments
{
    Task<List<ConsultaOracle>> ListAsync(decimal ownerId, CancellationToken cancellationToken);
}

internal interface INotificationStore
{
    Task UpsertAsync(Notification notification, CancellationToken cancellationToken);
    Task<List<Notification>> ListAsync(string ownerId, CancellationToken cancellationToken);
    Task<bool> MarkReadAsync(string ownerId, string id, CancellationToken cancellationToken);
}

internal sealed class NotificationDependencyUnavailableException : Exception;
