using MongoDB.Driver;
using CadeMinhaConsulta.Api.Features.Notifications;

namespace CadeMinhaConsulta.Api.Infrastructure;

internal sealed class MongoNotificationStore(IServiceProvider services, IConfiguration configuration)
    : INotificationStore
{
    private static readonly FilterDefinitionBuilder<Notification> Filter = Builders<Notification>.Filter;
    private static readonly UpdateDefinitionBuilder<Notification> Update = Builders<Notification>.Update;

    public async Task UpsertAsync(Notification notification, CancellationToken cancellationToken)
    {
        var collection = GetCollection();
        // MongoDB takes _id from the equality filter when it inserts the document.
        var filter = Filter.Eq(item => item.Id, notification.Id);
        var insert = Update
            .SetOnInsert(item => item.OwnerId, notification.OwnerId)
            .SetOnInsert(item => item.AppointmentId, notification.AppointmentId)
            .SetOnInsert(item => item.Status, notification.Status)
            .SetOnInsert(item => item.AppointmentAt, notification.AppointmentAt)
            .SetOnInsert(item => item.ObservedAtUtc, notification.ObservedAtUtc)
            .SetOnInsert(item => item.IsRead, false);

        try
        {
            await collection.UpdateOneAsync(filter, insert, new UpdateOptions { IsUpsert = true },
                cancellationToken);
        }
        catch (MongoWriteException exception) when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // A concurrent insert for this ID is equivalent only if the expected owner owns it.
            var existing = await collection.Find(filter & Filter.Eq(item => item.OwnerId, notification.OwnerId))
                .Limit(1).FirstOrDefaultAsync(cancellationToken);
            if (existing is null)
                throw;
        }
    }

    public Task<List<Notification>> ListAsync(string ownerId, CancellationToken cancellationToken) =>
        GetCollection()
            .Find(Filter.Eq(item => item.OwnerId, ownerId))
            .SortByDescending(item => item.ObservedAtUtc).ThenBy(item => item.Id)
            .ToListAsync(cancellationToken);

    public async Task<bool> MarkReadAsync(string ownerId, string id, CancellationToken cancellationToken)
    {
        var filter = Filter.Eq(item => item.Id, id) & Filter.Eq(item => item.OwnerId, ownerId);
        var result = await GetCollection().UpdateOneAsync(
            filter, Update.Set(item => item.IsRead, true), cancellationToken: cancellationToken);
        return result.MatchedCount == 1;
    }

    private IMongoCollection<Notification> GetCollection()
    {
        if (string.IsNullOrWhiteSpace(configuration.GetConnectionString("Mongo")) ||
            string.IsNullOrWhiteSpace(configuration["Mongo:Database"]))
            throw new NotificationDependencyUnavailableException();

        return services.GetRequiredService<IMongoDatabase>().GetCollection<Notification>("notifications")
            .WithWriteConcern(WriteConcern.Acknowledged);
    }
}
