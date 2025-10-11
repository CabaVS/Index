using System.Globalization;
using CabaVS.Workerly.Shared.Constants;
using CabaVS.Workerly.Shared.Models;
using CabaVS.Workerly.Web.Configuration;
using HtmlAgilityPack;
using Microsoft.Extensions.Options;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Microsoft.VisualStudio.Services.Common;

namespace CabaVS.Workerly.Web.Endpoints;

internal static class ReportingInfoEndpoint
{
    public static void MapReportingInfoEndpoint(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet(
            "api/work-items/{workItemId:int}/reporting-info",
            async (
                int workItemId,
                WorkItemTrackingHttpClient workItemClient,
                IOptions<TeamsDefinitionOptions> teamsDefinitionOptions,
                ILoggerFactory loggerFactory,
                CancellationToken cancellationToken) =>
            {
                // With top-level statements, ILogger<Program> uses the compiler-generated Program type in the global namespace, so the category is literally just "Program".
                ILogger logger = loggerFactory.CreateLogger(typeof(ReportingInfoEndpoint).FullName!);
                
                logger.LogInformation("Request received for reporting info. WorkItemId: {WorkItemId}", workItemId);

                WorkItem? workItem = await workItemClient.GetWorkItemAsync(
                    workItemId,
                    fields: [FieldNames.ReportingInfo],
                    cancellationToken: cancellationToken);

                if (workItem is null)
                {
                    logger.LogWarning("Work item not found. WorkItemId: {WorkItemId}", workItemId);
                    return Results.NotFound();
                }

                var reportingInfo = workItem.Fields.GetCastedValueOrDefault(FieldNames.ReportingInfo, string.Empty);
                if (string.IsNullOrWhiteSpace(reportingInfo))
                {
                    logger.LogInformation("ReportingInfo field is empty for WorkItemId: {WorkItemId}", workItemId);
                    return Results.Ok(Array.Empty<object>());
                }

                logger.LogInformation("Processing reporting info for WorkItemId: {WorkItemId}", workItemId);

                var sanitizedHtml = reportingInfo.Replace("&nbsp;", " ", StringComparison.InvariantCulture);

                var htmlDoc = new HtmlDocument();
                htmlDoc.LoadHtml(sanitizedHtml);

                var parsed = htmlDoc.DocumentNode.SelectNodes("//tr")
                    .Select(tr => tr
                        .SelectNodes("./td")
                        .Select(td => td.InnerText.Replace('\u00A0', ' ').Trim())
                        .ToArray())
                    .Where(row => row is { Length: 4 })
                    .Where(row => row.Any(x => !string.IsNullOrWhiteSpace(x)))
                    .Select(row => new
                    {
                        Date = DateOnly.ParseExact(row[0], "dd.MM.yyyy", CultureInfo.InvariantCulture),
                        Reporter = row[1],
                        Amount = double.Parse(
                            row[2].Replace(",", ".", StringComparison.InvariantCulture),
                            CultureInfo.InvariantCulture),
                        Comment = row[3]
                    });

                var groupedByReporter = parsed.GroupBy(x => x.Reporter)
                    .Select(g => new { Reporter = g.Key, Total = g.Sum(x => x.Amount) })
                    .OrderByDescending(x => x.Total)
                    .ThenBy(x => x.Reporter);
                IOrderedEnumerable<ReportingInfoResponseItem> groupedByTeam = groupedByReporter
                    .Select(x =>
                    {
                        var team = teamsDefinitionOptions.Value
                            .Teams
                            .SingleOrDefault(y => y.Value.Contains(x.Reporter),
                                new KeyValuePair<string, HashSet<string>>($"UNKNOWN TEAM on {x.Reporter}", []))
                            .Key;
                        return new { Team = team, Amount = x.Total };
                    })
                    .GroupBy(x => x.Team)
                    .Select(g => new ReportingInfoResponseItem(g.Key, g.Sum(x => x.Amount)))
                    .OrderByDescending(x => x.Total)
                    .ThenBy(x => x.Team);

                logger.LogInformation("Reporting info processing completed for WorkItemId: {WorkItemId}", workItemId);

                return Results.Ok(
                    new ReportingInfoResponse(groupedByTeam.ToArray()));
            });
}
