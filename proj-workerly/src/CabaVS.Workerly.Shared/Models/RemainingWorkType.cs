namespace CabaVS.Workerly.Shared.Models;

public enum RemainingWorkType
{
    Functionality,
    Requirements,
    ReleaseFinalization,
    Technical,
    Other
}

public static class RemainingWorkTypeExtensions
{
    private static readonly HashSet<string> FunctionalTags = ["Functionality"];
    private static readonly HashSet<string> RequirementsTags = ["Requirements"];
    private static readonly HashSet<string> ReleaseFinalizationTags = [];
    private static readonly HashSet<string> TechnicalTags = ["Technical", "Non-functional requirements", "Refactoring"];
    
    public static RemainingWorkType DetermineFromTags(this IEnumerable<string> tags)
    {
        var tagSet = new HashSet<string>(tags, StringComparer.OrdinalIgnoreCase);

        return tagSet.Intersect(FunctionalTags, StringComparer.OrdinalIgnoreCase).Any() ? RemainingWorkType.Functionality
            : tagSet.Intersect(RequirementsTags, StringComparer.OrdinalIgnoreCase).Any() ? RemainingWorkType.Requirements
            : tagSet.Intersect(ReleaseFinalizationTags, StringComparer.OrdinalIgnoreCase).Any() ? RemainingWorkType.ReleaseFinalization
            : tagSet.Intersect(TechnicalTags, StringComparer.OrdinalIgnoreCase).Any() ? RemainingWorkType.Technical 
            : RemainingWorkType.Other;
    }
}
