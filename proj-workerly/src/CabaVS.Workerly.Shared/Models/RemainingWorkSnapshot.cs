namespace CabaVS.Workerly.Shared.Models;

public sealed record RemainingWorkSnapshot(Root Root, RemainingWorkResponseItem[] Report)
{
    public string Id => $"{Root.WorkspaceId}|{Root.WorkItemId}|{Root.ExecutionDateUtc.Ticks}";
    public string WorkspaceId => Root.WorkspaceId.ToString();
}

public sealed record Root(Guid WorkspaceId, int WorkItemId, string WorkItemTitle, DateTime ExecutionDateUtc);
