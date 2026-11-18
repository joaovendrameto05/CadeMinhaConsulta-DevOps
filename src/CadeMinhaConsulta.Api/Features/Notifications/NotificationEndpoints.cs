using System.Data.Common;
using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using MongoDB.Driver;

namespace CadeMinhaConsulta.Api.Features.Notifications;

internal static class NotificationEndpoints
{
    public static void MapNotificationEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/notifications")
            .WithTags("Notificações")
            .RequireAuthorization()
            .RequireRateLimiting("per-subject")
            .AddEndpointFilter(async (context, next) =>
            {
                try
                {
                    return await next(context);
                }
                catch (Exception exception) when (
                    exception is NotificationDependencyUnavailableException or DbException or MongoException or TimeoutException)
                {
                    return Results.Problem(
                        title: "Serviço de notificações temporariamente indisponível.",
                        statusCode: StatusCodes.Status503ServiceUnavailable);
                }
            });

        group.MapGet("/me", ListAsync)
            .WithSummary("Atualiza e lista as notificações do paciente autenticado.")
            .WithDescription("Consulta os estados atuais no Oracle e recupera as notificações do titular no MongoDB.")
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPatch("/{id}/read", MarkReadAsync)
            .WithSummary("Marca uma notificação própria como lida.")
            .WithDescription("Repetir a operação retorna 204. Uma notificação ausente ou de outro paciente retorna 404.")
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
    }

    private static async Task<Results<Ok<NotificationResponse[]>, ForbidHttpResult>> ListAsync(
        HttpContext context, IConfiguration configuration, IPatientAppointments appointments,
        INotificationStore store, TimeProvider clock, CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!TryGetOwner(context.User, configuration, out var ownerId))
            return TypedResults.Forbid();

        var consultations = await appointments.ListAsync(ownerId, cancellationToken);
        var observedAtUtc = clock.GetUtcNow().UtcDateTime;
        foreach (var consultation in consultations)
        {
            var notification = Notification.FromAppointment(consultation, observedAtUtc);
            if (notification is not null)
                await store.UpsertAsync(notification, cancellationToken);
        }

        var notifications = await store.ListAsync(Notification.FormatId(ownerId), cancellationToken);
        return TypedResults.Ok(notifications.Select(item => new NotificationResponse(
            item.Id, item.AppointmentId, item.Status, item.AppointmentAt, item.ObservedAtUtc, item.IsRead)).ToArray());
    }

    private static async Task<Results<NoContent, NotFound, ForbidHttpResult>> MarkReadAsync(
        string id, ClaimsPrincipal principal, IConfiguration configuration,
        INotificationStore store, CancellationToken cancellationToken)
    {
        if (!TryGetOwner(principal, configuration, out var ownerId))
            return TypedResults.Forbid();

        if (id.Length != 64 || !id.All(Uri.IsHexDigit))
            return TypedResults.NotFound();

        return await store.MarkReadAsync(Notification.FormatId(ownerId), id, cancellationToken)
            ? TypedResults.NoContent()
            : TypedResults.NotFound();
    }

    private static bool TryGetOwner(ClaimsPrincipal principal, IConfiguration configuration, out decimal ownerId)
    {
        ownerId = default;
        var claimType = configuration["Jwt:OracleUserIdClaim"];
        return !string.IsNullOrWhiteSpace(claimType)
            && decimal.TryParse(principal.FindFirst(claimType)?.Value, NumberStyles.None,
                CultureInfo.InvariantCulture, out ownerId)
            && ownerId > 0;
    }
}
