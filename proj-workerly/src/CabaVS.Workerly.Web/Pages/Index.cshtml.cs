using System.ComponentModel.DataAnnotations;
using CabaVS.Workerly.Web.Entities;
using CabaVS.Workerly.Web.Models;
using CabaVS.Workerly.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CabaVS.Workerly.Web.Pages;

internal sealed class Index(ILogger<Index> logger, CurrentUserProvider currentUserProvider, IWorkspaceService workspaceService) : PageModel
{
    public IReadOnlyList<WorkspaceListItem> Workspaces { get; private set; } = [];
    
    [BindProperty]
    public Guid? SelectedWorkspaceId { get; set; }
    
    [BindProperty]
    [Required]
    [StringLength(80, MinimumLength = 2)]
    public string? NewWorkspaceName { get; set; }
    
    public async Task<IActionResult> OnGet(CancellationToken ct)
    {
        if (User.Identity is not { IsAuthenticated: true })
        {
            logger.LogInformation("Unauthenticated user accessed Index page.");
            return Page();
        }

        User user = currentUserProvider.GetCurrentUser();
        logger.LogInformation("Fetching workspaces for user {UserId} ({UserEmail})", user.Id, user.Email);
        
        Workspaces = await workspaceService.GetForUserAsync(user.Id, ct);
        logger.LogInformation("Fetched {WorkspaceCount} workspaces for user {UserId}.",
            Workspaces.Count, user.Id);
        
        WorkspaceListItem? selectedWorkspace = Workspaces.SingleOrDefault(w => w.IsSelected);
        if (selectedWorkspace is not null)
        {
            SelectedWorkspaceId = selectedWorkspace.Id;
            logger.LogInformation("Selected workspace for user {UserId}: {WorkspaceId} ({WorkspaceName}).",
                user.Id, selectedWorkspace.Id, selectedWorkspace.Name);
        }
        else
        {
            logger.LogInformation("No selected workspace found for user {UserId}.", user.Id);
        }
        
        return Page();
    }
    
    public async Task<IActionResult> OnPostSelectWorkspaceAsync(CancellationToken ct)
    {
        if (User.Identity is not { IsAuthenticated: true })
        {
            logger.LogInformation("Unauthenticated POST request to SelectWorkspace.");
            return RedirectToPage();
        }

        if (SelectedWorkspaceId is not { } selectedWorkspaceIdValue)
        {
            logger.LogWarning("POST SelectWorkspace called without SelectedWorkspaceId.");
            return RedirectToPage();
        }
        
        User user = currentUserProvider.GetCurrentUser();
        logger.LogInformation("User {UserId} selecting workspace {WorkspaceId}.", user.Id, selectedWorkspaceIdValue);
            
        await workspaceService.SetSelectedAsync(user.Id, selectedWorkspaceIdValue, ct);
        logger.LogInformation("User {UserId} set workspace {WorkspaceId} as selected.",
            user.Id, selectedWorkspaceIdValue);

        return RedirectToPage();
    }
    
    public async Task<IActionResult> OnPostCreateWorkspaceAsync(CancellationToken ct)
    {
        if (User.Identity is not { IsAuthenticated: true })
        {
            logger.LogInformation("Unauthenticated POST request to CreateWorkspace.");
            return RedirectToPage();
        }
        
        if (!ModelState.IsValid)
        {
            logger.LogWarning("CreateWorkspace validation failed. Errors: {Errors}",
                string.Join("; ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)));
            
            User userForErrors = currentUserProvider.GetCurrentUser();
            Workspaces = await workspaceService.GetForUserAsync(userForErrors.Id, ct);
            ViewData["ShowCreateWorkspaceModal"] = true;
            return Page();
        }

        User user = currentUserProvider.GetCurrentUser();
        var name = NewWorkspaceName!.Trim();

        logger.LogInformation("Creating workspace '{WorkspaceName}' for user {UserId}.", name, user.Id);

        try
        {
            Guid id = await workspaceService.CreateAsync(name, user.Id, ct);
            logger.LogInformation("Workspace '{WorkspaceName}' created with Id {WorkspaceId} for user {UserId}.",
                name, id, user.Id);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create workspace '{WorkspaceName}' for user {UserId}.", name, user.Id);
            
            Workspaces = await workspaceService.GetForUserAsync(user.Id, ct);
            ViewData["ShowCreateWorkspaceModal"] = true;
            return Page();
        }
        
        return RedirectToPage();
    }
}
