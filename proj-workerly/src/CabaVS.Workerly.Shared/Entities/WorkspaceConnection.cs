using CabaVS.Workerly.Shared.Models;

namespace CabaVS.Workerly.Shared.Entities;

public sealed class WorkspaceConnection
{
    public string Id => WorkspaceId.ToString();
    public Guid WorkspaceId { get; set; }

    public string Organization { get; set; } = string.Empty;
    public string PersonalAccessToken { get; set; } = string.Empty;

    public TeamsDefinition TeamsDefinition { get; set; } = new([]);
}
