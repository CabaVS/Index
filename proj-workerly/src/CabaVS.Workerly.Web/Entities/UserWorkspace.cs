namespace CabaVS.Workerly.Web.Entities;

internal sealed class UserWorkspace
{
    public Guid UserId { get; set; }
    public User? User { get; set; }

    public Guid WorkspaceId { get; set; }
    public Workspace? Workspace { get; set; }

    public bool IsAdmin { get; set; }
}
