using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using User = CabaVS.Workerly.Shared.Entities.User;

namespace CabaVS.Workerly.Shared.Persistence;

internal sealed class CosmosUserService(CosmosContext ctx, ILogger<CosmosUserService> logger) : IUserService
{
    public async Task<User?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        try
        {
            ItemResponse<User> resp = await ctx.Users.ReadItemAsync<User>(
                id: id.ToString(),
                partitionKey: new PartitionKey(id.ToString()),
                cancellationToken: ct);

            logger.LogInformation("Found user {UserId}. RU: {RU}", id, resp.RequestCharge);
            return resp.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            logger.LogInformation("User {UserId} not found.", id);
            return null;
        }
    }

    public async Task EnsureExistsAsync(User user, CancellationToken ct)
    {
        if (user.Id == Guid.Empty)
        {
            logger.LogWarning("EnsureExistsAsync called with empty Id. Skipping creation.");
            return;
        }
        
        if (!string.IsNullOrWhiteSpace(user.Email))
        {
            user.Email = user.Email.Trim().ToLowerInvariant();
        }
        
        User? existing = await GetByIdAsync(user.Id, ct);
        if (existing is not null)
        {
            logger.LogInformation("User {UserId} already exists. No action taken.", user.Id);
            return;
        }

        try
        {
            ItemResponse<User> resp = await ctx.Users.CreateItemAsync(
                item: user,
                partitionKey: new PartitionKey(user.Id.ToString()),
                cancellationToken: ct);

            logger.LogInformation("Created user {UserId}. RU: {RU}", user.Id, resp.RequestCharge);
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            logger.LogWarning(ex, "Conflict creating user {UserId} (possible duplicate email '{Email}').", user.Id, user.Email);
        }
    }
}
