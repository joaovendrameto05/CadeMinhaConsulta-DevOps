using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using CadeMinhaConsulta.Api.Features.Notifications;
using CadeMinhaConsulta.Api.Infrastructure;
using Xunit;

namespace CadeMinhaConsulta.InfrastructureTests;

public sealed class NotificationProjectionTests
{
    [Theory]
    [InlineData("AGENDADA")]
    [InlineData("CONFIRMADA")]
    [InlineData("CANCELADA")]
    public void SupportedSnapshotProducesStableUnreadNotification(string state)
    {
        var appointment = Row(101, 88, state);
        var observedAt = new DateTime(2026, 11, 17, 12, 0, 0, DateTimeKind.Utc);

        var first = Notification.FromAppointment(appointment, observedAt);
        var repeated = Notification.FromAppointment(appointment, observedAt.AddHours(1));

        Assert.NotNull(first);
        Assert.NotNull(repeated);
        Assert.Equal(first.Id, repeated.Id);
        Assert.Equal(64, first.Id.Length);
        Assert.Equal("101", first.OwnerId);
        Assert.Equal("88", first.AppointmentId);
        Assert.Equal(state, first.Status);
        Assert.Equal("2026-12-01T09:30:00", first.AppointmentAt);
        Assert.Equal(observedAt, first.ObservedAtUtc);
        Assert.False(first.IsRead);
    }

    [Theory]
    [InlineData("PRESENTE")]
    [InlineData("FALTA")]
    [InlineData("DESCONHECIDO")]
    public void OtherStatesDoNotGenerateNotifications(string state) =>
        Assert.Null(Notification.FromAppointment(Row(101, 88, state), DateTime.UtcNow));

    [Fact]
    public void IdDistinguishesOwnerAppointmentAndState()
    {
        var now = DateTime.UtcNow;
        var ids = new[]
        {
            Notification.FromAppointment(Row(101, 88, "AGENDADA"), now)!.Id,
            Notification.FromAppointment(Row(102, 88, "AGENDADA"), now)!.Id,
            Notification.FromAppointment(Row(101, 89, "AGENDADA"), now)!.Id,
            Notification.FromAppointment(Row(101, 88, "CONFIRMADA"), now)!.Id
        };

        Assert.Equal(ids.Length, ids.Distinct().Count());
    }

    [Fact]
    public void MongoDocumentUsesStableIdAndPreservesSourceTimeWithoutAnOffset()
    {
        var observedAt = new DateTime(2026, 11, 17, 12, 0, 0, DateTimeKind.Utc);
        var notification = Notification.FromAppointment(Row(101, 88, "AGENDADA"), observedAt)!;
        var document = notification.ToBsonDocument();

        Assert.Equal(notification.Id, document["_id"].AsString);
        Assert.Equal("101", document["ownerId"].AsString);
        Assert.Equal("2026-12-01T09:30:00", document["appointmentAt"].AsString);
        Assert.True(document["observedAtUtc"].IsBsonDateTime);
        Assert.Equal(notification, BsonSerializer.Deserialize<Notification>(document));
    }

    internal static ConsultaOracle Row(decimal ownerId, decimal appointmentId, string state) => new()
    {
        IdUsuario = ownerId,
        IdConsulta = appointmentId,
        Status = state,
        DataHora = new DateTime(2026, 12, 1, 9, 30, 0)
    };
}
