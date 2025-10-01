namespace CabaVS.Workerly.Shared.Models;

public sealed record ReportingInfoResponse(ReportingInfoResponseItem[] Items);

public sealed record ReportingInfoResponseItem(string Team, double Total);
