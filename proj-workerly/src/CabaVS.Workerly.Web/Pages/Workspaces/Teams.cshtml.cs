using System.ComponentModel.DataAnnotations;
using System.Globalization;
using CabaVS.Workerly.Web.Entities;
using CabaVS.Workerly.Web.Models;
using CabaVS.Workerly.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CabaVS.Workerly.Web.Pages.Workspaces;

internal sealed class Teams(
    ILogger<Teams> logger,
    CurrentUserProvider currentUserProvider,
    IWorkspaceService workspaceService,
    IWorkspaceConfigService configService) : PageModel
{
    [BindProperty(SupportsGet = true)]
    [FromRoute]
    public Guid WorkspaceId { get; set; }

    // Rendered rows
    public List<Row> Rows { get; private set; } = new();

    // Bound on POST
    [BindProperty]
    public List<Row> EditRows { get; set; } = new();

    internal sealed class Row
    {
        [Required, StringLength(80)]
        public string Team { get; set; } = string.Empty;

        [Display(Name = "Members (comma-separated)")]
        public string? MembersCsv { get; set; }
    }

    public async Task<IActionResult> OnGet(CancellationToken ct)
    {
        if (User.Identity is not { IsAuthenticated: true })
        {
            logger.LogInformation("Unauthenticated GET /workspaces/{WorkspaceId}/teams.", WorkspaceId);
            return RedirectToPage("/Index");
        }

        User me = currentUserProvider.GetCurrentUser();
        logger.LogInformation("GET /workspaces/{WorkspaceId}/teams by {UserId}.", WorkspaceId, me.Id);

        // Ensure membership (any member allowed)
        IReadOnlyList<WorkspaceListItem> myWorkspaces = await workspaceService.GetForUserAsync(me.Id, ct);
        if (myWorkspaces.All(w => w.Id != WorkspaceId))
        {
            logger.LogWarning("Forbidden GET Teams: user {UserId} is not a member of {WorkspaceId}.", me.Id, WorkspaceId);
            return Forbid();
        }

        TeamsDefinition teams = await configService.GetTeamsAsync(WorkspaceId, ct);
        Rows = [];

        foreach (KeyValuePair<string, HashSet<string>> kv in teams.Teams.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            Rows.Add(new Row
            {
                Team = kv.Key.ToUpper(CultureInfo.InvariantCulture),
                MembersCsv = string.Join(", ", kv.Value.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            });
        }

        logger.LogInformation("Loaded {RowCount} team rows for workspace {WorkspaceId}.", Rows.Count, WorkspaceId);
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken ct)
    {
        if (User.Identity is not { IsAuthenticated: true })
        {
            logger.LogInformation("Unauthenticated POST Save /workspaces/{WorkspaceId}/teams.", WorkspaceId);
            return RedirectToPage("/Index");
        }

        User me = currentUserProvider.GetCurrentUser();
        logger.LogInformation("POST Save Teams for workspace {WorkspaceId} by {UserId}.", WorkspaceId, me.Id);

        // Re-display rows if validation fails
        Rows = EditRows;

        if (!ModelState.IsValid)
        {
            logger.LogWarning("Save Teams validation failed for workspace {WorkspaceId}. Errors: {Errors}",
                WorkspaceId,
                string.Join("; ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)));
            return Page();
        }

        // Map posted rows -> TeamsDefinition
        var def = new TeamsDefinition([]);
        foreach (Row r in Rows)
        {
            var key = r.Team.Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(r.MembersCsv))
            {
                IEnumerable<string> tokens = r.MembersCsv.Split(',',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(t => !string.IsNullOrWhiteSpace(t));
                foreach (var token in tokens)
                {
                    set.Add(token);
                }
            }

            def.Teams[key] = set;
        }

        SaveTeamsResult result = await configService.SaveTeamsAsync(me.Id, WorkspaceId, def, ct);
        switch (result)
        {
            case SaveTeamsResult.Success:
                logger.LogInformation("Teams saved for workspace {WorkspaceId} by {UserId}.", WorkspaceId, me.Id);
                return RedirectToPage("/Index");

            case SaveTeamsResult.Forbidden:
                logger.LogWarning("SaveTeams forbidden for user {UserId} on workspace {WorkspaceId}.", me.Id, WorkspaceId);
                ModelState.AddModelError(string.Empty, "You are not a member of this workspace.");
                return Page();

            case SaveTeamsResult.Invalid:
                logger.LogWarning("SaveTeams invalid payload for workspace {WorkspaceId} by {UserId}.", WorkspaceId, me.Id);
                ModelState.AddModelError(string.Empty, "Invalid team data.");
                return Page();

            default:
                logger.LogError("SaveTeams error for workspace {WorkspaceId} by {UserId}.", WorkspaceId, me.Id);
                ModelState.AddModelError(string.Empty, "Could not save teams due to an unexpected error.");
                return Page();
        }
    }
}
