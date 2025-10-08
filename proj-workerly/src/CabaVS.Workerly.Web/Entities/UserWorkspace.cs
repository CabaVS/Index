namespace CabaVS.Workerly.Web.Entities;

internal sealed class UserWorkspace
{
    public string Id => $"{UserId}:{WorkspaceId}";
    
    public Guid UserId { get; set; }
    public Guid WorkspaceId { get; set; }

    public bool IsAdmin { get; set; }
    public bool IsSelected { get; set; }
}
