using MongoDB.Driver;
using CadeMinhaConsulta.Api.Features.ReminderRules;

namespace CadeMinhaConsulta.Api.Infrastructure;

internal sealed class MongoReminderRuleStore(IServiceProvider services, IConfiguration configuration)
    : IReminderRuleStore
{
    internal const string CollectionName = "reminder_rules";
    private static readonly FilterDefinitionBuilder<ReminderRule> Filter = Builders<ReminderRule>.Filter;
    private static readonly UpdateDefinitionBuilder<ReminderRule> Update = Builders<ReminderRule>.Update;
    private static readonly FilterDefinition<ReminderRule> Visible = Filter.Eq(rule => rule.DeletedAtUtc, null);

    public async Task<ReminderRule> CreateOrGetAsync(ReminderRule candidate, CancellationToken cancellationToken)
    {
        var collection = GetCollection();
        try
        {
            // The rule and the immutable creation response are committed in one insertion.
            await collection.InsertOneAsync(candidate, cancellationToken: cancellationToken);
            return candidate;
        }
        catch (MongoWriteException exception) when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // A concurrent write may still be awaiting majority acknowledgement.
            var byId = Filter.Eq(rule => rule.Id, candidate.Id);
            for (var attempt = 0; attempt < 3; attempt++)
            {
                if (attempt > 0)
                    await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
                var previous = await collection.Find(byId).FirstOrDefaultAsync(cancellationToken);
                if (previous is not null)
                    return previous;
            }

            throw new ReminderRuleStoreUnavailableException();
        }
    }

    public Task<List<ReminderRule>> ListAsync(CancellationToken cancellationToken) =>
        GetCollection().Find(Visible).SortBy(rule => rule.CreatedAtUtc).ThenBy(rule => rule.Id)
            .ToListAsync(cancellationToken);

    public async Task<ReminderRule?> FindAsync(string id, CancellationToken cancellationToken) =>
        await GetCollection().Find(Visible & Filter.Eq(rule => rule.Id, id)).FirstOrDefaultAsync(cancellationToken);

    public async Task<ReminderRule?> UpdateAsync(
        string id, ReminderRuleValues values, DateTime nowUtc, CancellationToken cancellationToken) =>
        await GetCollection().FindOneAndUpdateAsync(
            Visible & Filter.Eq(rule => rule.Id, id),
            Update.Set(rule => rule.Current, values).Max(rule => rule.UpdatedAtUtc, nowUtc),
            new FindOneAndUpdateOptions<ReminderRule> { ReturnDocument = ReturnDocument.After },
            cancellationToken);

    public async Task<bool> DeleteAsync(string id, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var result = await GetCollection().UpdateOneAsync(
            Visible & Filter.Eq(rule => rule.Id, id), Update.Set(rule => rule.DeletedAtUtc, nowUtc),
            cancellationToken: cancellationToken);
        return result.MatchedCount == 1;
    }

    private IMongoCollection<ReminderRule> GetCollection()
    {
        if (string.IsNullOrWhiteSpace(configuration.GetConnectionString("Mongo")) ||
            string.IsNullOrWhiteSpace(configuration["Mongo:Database"]))
            throw new ReminderRuleStoreUnavailableException();

        return services.GetRequiredService<IMongoDatabase>().GetCollection<ReminderRule>(CollectionName)
            .WithReadPreference(ReadPreference.Primary)
            .WithReadConcern(ReadConcern.Majority)
            .WithWriteConcern(WriteConcern.WMajority.With(journal: true, wTimeout: TimeSpan.FromSeconds(5)));
    }
}
