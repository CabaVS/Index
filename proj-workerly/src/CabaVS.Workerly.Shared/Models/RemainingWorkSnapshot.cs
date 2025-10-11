namespace CabaVS.Workerly.Shared.Models;

public sealed record RemainingWorkSnapshot(Root? Root, RemainingWorkResponseItem[] Report);

public sealed record Root(int Id, string Title, DateTime ExecutionDateUtc);
