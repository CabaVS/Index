namespace CabaVS.Workerly.Jobs.BurndownSnapping.Configuration;

internal sealed class RemainingWorkTrackerOptions
{
    public Guid WorkspaceId { get; set; }
    public ToTrackItem[] ToTrackItems { get; set; } = [];
    
    internal sealed class ToTrackItem
    {
        public int WorkItemId { get; set; }
        public DateOnly From { get; set; }
        public DateOnly To { get; set; }
    }
}
