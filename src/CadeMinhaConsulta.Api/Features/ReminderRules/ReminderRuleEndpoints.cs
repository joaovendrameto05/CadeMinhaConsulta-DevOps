using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using CadeMinhaConsulta.Api.Infrastructure;

namespace CadeMinhaConsulta.Api.Features.ReminderRules;

internal static class ReminderRuleEndpoints
{
    private const string BasePath = "/api/reminder-rules";
    private const string IdempotencyHeader = "Idempotency-Key";

    public static void MapReminderRuleEndpoints(this WebApplication app)
    {
        var group = app.MapGroup(BasePath)
            .WithTags("Regras de lembrete")
            .RequireAuthorization(JwtAuthentication.ReminderRuleReader)
            .RequireRateLimiting("per-subject")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .AddEndpointFilter(async (context, next) =>
            {
                try
                {
                    return await next(context);
                }
                catch (Exception exception) when (
                    exception is MongoException or TimeoutException or ReminderRuleStoreUnavailableException)
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status503ServiceUnavailable,
                        title: "Regras de lembrete temporariamente indisponíveis.");
                }
            });

        group.MapGet("", ListAsync)
            .WithSummary("Lista as regras de lembrete.")
            .Produces<ReminderRuleResponse[]>();
        group.MapGet("/{id}", GetAsync)
            .WithSummary("Consulta uma regra de lembrete.")
            .Produces<ReminderRuleResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPost("", CreateAsync)
            .RequireAuthorization(JwtAuthentication.Manager)
            .WithSummary("Cria uma regra de lembrete.")
            .WithDescription("Exige Idempotency-Key com 1 a 128 caracteres ASCII (letras, dígitos, ponto, hífen ou sublinhado). " +
                "A mesma chave, usuário e conteúdo retornam o 201, o Location e o corpo originais. " +
                "A mesma chave com conteúdo diferente retorna 409. Em caso de 503, repita com a mesma chave e conteúdo.")
            .Produces<ReminderRuleResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);
        group.MapPut("/{id}", UpdateAsync)
            .RequireAuthorization(JwtAuthentication.Manager)
            .WithSummary("Substitui os campos editáveis de uma regra.")
            .Produces<ReminderRuleResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);
        group.MapDelete("/{id}", DeleteAsync)
            .RequireAuthorization(JwtAuthentication.Manager)
            .WithSummary("Exclui uma regra de lembrete.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> ListAsync(IReminderRuleStore store, CancellationToken cancellationToken)
    {
        var rules = await store.ListAsync(cancellationToken);
        return Results.Ok(rules.Select(rule => rule.ToResponse()).ToArray());
    }

    private static async Task<IResult> GetAsync(string id, IReminderRuleStore store, CancellationToken cancellationToken)
    {
        var rule = IsRuleId(id) ? await store.FindAsync(id, cancellationToken) : null;
        return rule is null ? RuleNotFound() : Results.Ok(rule.ToResponse());
    }

    private static async Task<IResult> CreateAsync(
        [FromHeader(Name = IdempotencyHeader)] string key, ReminderRuleInput input, HttpRequest request,
        IReminderRuleStore store, TimeProvider clock, CancellationToken cancellationToken)
    {
        var (values, errors) = input.Validate();
        if (request.Headers[IdempotencyHeader].Count != 1 || key.Length is < 1 or > 128 ||
            !key.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_'))
            errors[IdempotencyHeader] = ["Informe uma chave única com 1 a 128 caracteres ASCII válidos."];
        if (errors.Count > 0)
            return Results.ValidationProblem(errors);

        var user = request.HttpContext.User;
        var id = ReminderRule.IdForCreate(user.FindFirst("iss")!.Value, user.FindFirst("sub")!.Value, key);
        var now = UtcWithMongoPrecision(clock);
        var candidate = new ReminderRule(id, values!, values!, now, now, null);
        var persisted = await store.CreateOrGetAsync(candidate, cancellationToken);

        if (persisted.Original != candidate.Original)
            return Results.Problem(statusCode: StatusCodes.Status409Conflict,
                title: "Idempotency-Key já utilizada com outro conteúdo.");

        return Results.Created($"{BasePath}/{persisted.Id}", persisted.OriginalResponse());
    }

    private static async Task<IResult> UpdateAsync(
        string id, ReminderRuleInput input, IReminderRuleStore store, TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var (values, errors) = input.Validate();
        if (errors.Count > 0)
            return Results.ValidationProblem(errors);
        var rule = IsRuleId(id)
            ? await store.UpdateAsync(id, values!, UtcWithMongoPrecision(clock), cancellationToken)
            : null;
        return rule is null ? RuleNotFound() : Results.Ok(rule.ToResponse());
    }

    private static async Task<IResult> DeleteAsync(
        string id, IReminderRuleStore store, TimeProvider clock, CancellationToken cancellationToken) =>
        IsRuleId(id) && await store.DeleteAsync(id, UtcWithMongoPrecision(clock), cancellationToken)
            ? Results.NoContent()
            : RuleNotFound();

    private static bool IsRuleId(string id) => id.Length == 64 && id.All(Uri.IsHexDigit);

    private static DateTime UtcWithMongoPrecision(TimeProvider clock) =>
        DateTimeOffset.FromUnixTimeMilliseconds(clock.GetUtcNow().ToUnixTimeMilliseconds()).UtcDateTime;

    private static IResult RuleNotFound() =>
        Results.Problem(title: "Regra de lembrete não encontrada.", statusCode: StatusCodes.Status404NotFound);
}
