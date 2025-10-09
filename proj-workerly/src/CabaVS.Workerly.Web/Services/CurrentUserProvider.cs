using System.Security.Claims;
using CabaVS.Workerly.Web.Entities;

namespace CabaVS.Workerly.Web.Services;

internal sealed class CurrentUserProvider(IHttpContextAccessor httpContextAccessor, ILogger<CurrentUserProvider> logger)
{
    public User GetCurrentUser() => GetCurrentUser(httpContextAccessor.HttpContext?.User);
    
    public User GetCurrentUser(ClaimsPrincipal? claimsPrincipal)
    {
        if (claimsPrincipal?.Identity is not { IsAuthenticated: true })
        {
            throw new InvalidOperationException("No authenticated user found in the current HttpContext.");
        }

        var userId = claimsPrincipal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new InvalidOperationException("Authenticated user does not contain a valid NameIdentifier claim.");
        }

        if (!Guid.TryParse(userId, out Guid userIdValue))
        {
            throw new InvalidOperationException("Authenticated user has an invalid NameIdentifier claim format (not a GUID).");
        }

        var user = new User
        {
            Id = userIdValue,
            Email = claimsPrincipal.FindFirstValue(ClaimTypes.Email)
                    ?? string.Empty
        };
        
        logger.LogInformation("Successfully retrieved current user with Id: {UserId}", user.Id);
        
        return user; 
    }
}
