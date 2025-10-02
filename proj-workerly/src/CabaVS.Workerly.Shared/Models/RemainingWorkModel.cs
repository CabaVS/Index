namespace CabaVS.Workerly.Shared.Models;

public sealed record RemainingWorkModel(
    double Functionality,
    double Requirements,
    double ReleaseFinalization,
    double Technical,
    double Other) : IComparable<RemainingWorkModel>
{
    public double Total => Functionality + Requirements + ReleaseFinalization + Technical + Other;

    public int CompareTo(RemainingWorkModel? other) =>
        other is null ? 1 : Total.CompareTo(other.Total);
    
    public static RemainingWorkModel operator +(RemainingWorkModel a, RemainingWorkModel b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        
        return new RemainingWorkModel(
            Functionality: a.Functionality + b.Functionality,
            Requirements: a.Requirements + b.Requirements,
            ReleaseFinalization: a.ReleaseFinalization + b.ReleaseFinalization,
            Technical: a.Technical + b.Technical,
            Other: a.Other + b.Other);
    }
}

public static class RemainingWorkModelExtensions
{
    public static RemainingWorkModel Sum(this IEnumerable<RemainingWorkModel> source) =>
        source.Aggregate(
            new RemainingWorkModel(0, 0, 0, 0, 0),
            (current, model) => current + model);
}
