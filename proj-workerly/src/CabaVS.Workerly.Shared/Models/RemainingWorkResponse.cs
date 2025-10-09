namespace CabaVS.Workerly.Shared.Models;

public sealed record RemainingWorkResponse(int Id, string Title, RemainingWorkResponseItem[] Report);

public sealed record RemainingWorkResponseItem(string Team, RemainingWorkModel RemainingWork);
