using System.Security.Cryptography;
using System.Text.Json;
using MongoDB.Bson.Serialization.Attributes;

namespace CadeMinhaConsulta.Api.Features.ReminderRules;

internal sealed record ReminderRuleInput(string? Name, int? DaysBefore, string? Message, bool? IsActive)
{
    public (ReminderRuleValues? Values, Dictionary<string, string[]> Errors) Validate()
    {
        var errors = new Dictionary<string, string[]>();
        var name = Name?.Trim();
        var message = Message?.Trim();

        if (name is not { Length: >= 1 and <= 100 })
            errors["name"] = ["Informe um nome com 1 a 100 caracteres."];
        if (DaysBefore is not (>= 0 and <= 365))
            errors["daysBefore"] = ["Informe um número inteiro de 0 a 365."];
        if (message is not { Length: >= 1 and <= 500 })
            errors["message"] = ["Informe uma mensagem com 1 a 500 caracteres."];
        if (IsActive is null)
            errors["isActive"] = ["Informe se a regra está ativa."];

        return errors.Count == 0
            ? (new ReminderRuleValues(name!, DaysBefore!.Value, message!, IsActive!.Value), errors)
            : (null, errors);
    }
}

internal sealed record ReminderRuleValues(
    [property: BsonElement("name")] string Name,
    [property: BsonElement("daysBefore")] int DaysBefore,
    [property: BsonElement("message")] string Message,
    [property: BsonElement("isActive")] bool IsActive);

internal sealed record ReminderRule(
    [property: BsonId] string Id,
    [property: BsonElement("original")] ReminderRuleValues Original,
    [property: BsonElement("current")] ReminderRuleValues Current,
    [property: BsonElement("createdAtUtc")] DateTime CreatedAtUtc,
    [property: BsonElement("updatedAtUtc")] DateTime UpdatedAtUtc,
    [property: BsonElement("deletedAtUtc")] DateTime? DeletedAtUtc)
{
    public static string IdForCreate(string issuer, string subject, string key)
    {
        var scope = JsonSerializer.SerializeToUtf8Bytes(new[] { issuer, subject, "POST /api/reminder-rules", key });
        return Convert.ToHexStringLower(SHA256.HashData(scope));
    }

    public ReminderRuleResponse ToResponse() => ToResponse(Current, UpdatedAtUtc);

    public ReminderRuleResponse OriginalResponse() => ToResponse(Original, CreatedAtUtc);

    private ReminderRuleResponse ToResponse(ReminderRuleValues values, DateTime updatedAtUtc) =>
        new(Id, values.Name, values.DaysBefore, values.Message, values.IsActive, CreatedAtUtc, updatedAtUtc);
}

internal sealed record ReminderRuleResponse(
    string Id, string Name, int DaysBefore, string Message, bool IsActive,
    DateTime CreatedAtUtc, DateTime UpdatedAtUtc);

internal interface IReminderRuleStore
{
    Task<ReminderRule> CreateOrGetAsync(ReminderRule candidate, CancellationToken cancellationToken);
    Task<List<ReminderRule>> ListAsync(CancellationToken cancellationToken);
    Task<ReminderRule?> FindAsync(string id, CancellationToken cancellationToken);
    Task<ReminderRule?> UpdateAsync(
        string id, ReminderRuleValues values, DateTime nowUtc, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(string id, DateTime nowUtc, CancellationToken cancellationToken);
}

internal sealed class ReminderRuleStoreUnavailableException : Exception;
