using System.ComponentModel.DataAnnotations;
using CabaVS.Workerly.Web.Entities;
using CabaVS.Workerly.Web.Models;
using CabaVS.Workerly.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace CabaVS.Workerly.Web.Pages;

internal sealed class Index(
    ILogger<Index> logger,
    CurrentUserProvider currentUserProvider,
    IWorkspaceService workspaceService,
    IWorkspaceConfigService configService) : PageModel
{
    public IReadOnlyList<WorkspaceListItem> Workspaces { get; private set; } = [];
    
    public bool IsSelectedWorkspaceAdmin { get; private set; }
    
    public bool ConnPatIsSet { get; private set; }
    
    [BindProperty]
    public Guid? SelectedWorkspaceId { get; set; }
    
    [BindProperty]
    [EmailAddress]
    public string? InviteEmail { get; set; }
    
    [BindProperty]
    [Required]
    [StringLength(80, MinimumLength = 2)]
    public string? NewWorkspaceName { get; set; }
    
    [BindProperty]
    public string? ConnOrganization { get; set; }
    
    [BindProperty]
    public string? ConnPersonalAccessToken { get; set; }
    
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
            IsSelectedWorkspaceAdmin = selectedWorkspace.IsAdmin;
            
            logger.LogInformation("Selected workspace for user {UserId}: {WorkspaceId} ({WorkspaceName}).",
                user.Id, selectedWorkspace.Id, selectedWorkspace.Name);
            
            if (IsSelectedWorkspaceAdmin)
            {
                WorkspaceConnection? cfg = await configService.GetAsync(SelectedWorkspaceId.Value, ct);
                if (cfg is not null)
                {
                    ConnOrganization = cfg.Organization;
                    ConnPatIsSet = !string.IsNullOrEmpty(cfg.PersonalAccessToken);
                    
                    logger.LogInformation("Loaded connection settings for workspace {WorkspaceId}. PAT set: {HasPat}",
                        SelectedWorkspaceId.Value, ConnPatIsSet);
                }
                else
                {
                    ConnOrganization = null;
                    ConnPatIsSet = false;
                    
                    logger.LogInformation("No connection settings found for workspace {WorkspaceId}.", SelectedWorkspaceId.Value);
                }
            }
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
        
        ModelState.Remove(nameof(InviteEmail));
        
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
    
    public async Task<IActionResult> OnPostInviteAsync(CancellationToken ct)
    {
        if (User.Identity is not { IsAuthenticated: true })
        {
            logger.LogInformation("Unauthenticated POST request to Invite.");
            return RedirectToPage();
        }
        
        ModelState.Remove(nameof(NewWorkspaceName));
        
        if (string.IsNullOrWhiteSpace(InviteEmail))
        {
            logger.LogWarning("Invite request missing email input.");
            ModelState.AddModelError(nameof(InviteEmail), "Email is required.");
            
            ViewData["ShowInviteModal"] = true;
            return Page();
        }

        if (!SelectedWorkspaceId.HasValue)
        {
            logger.LogWarning("Invite request missing selected workspace.");
            ModelState.AddModelError(string.Empty, "No workspace selected.");
            
            ViewData["ShowInviteModal"] = true;
            return Page();
        }
        
        User inviter = currentUserProvider.GetCurrentUser();
        logger.LogInformation("Processing invite request by user {UserId} ({UserEmail}).", inviter.Id, inviter.Email);
        
        Guid workspaceId = SelectedWorkspaceId!.Value;
        logger.LogInformation("User {UserId} inviting {InviteEmail} to workspace {WorkspaceId}.",
            inviter.Id, InviteEmail, workspaceId);
        
        InviteUserResult result = await workspaceService.InviteUserByEmailAsync(
            inviter.Id, SelectedWorkspaceId!.Value, InviteEmail!, ct);
        switch (result)
        {
            case InviteUserResult.Success:
                logger.LogInformation("User {UserId} successfully invited {InviteEmail} to workspace {WorkspaceId}.",
                    inviter.Id, InviteEmail, workspaceId);
                
                return RedirectToPage();

            case InviteUserResult.UserNotFound:
                logger.LogWarning("Invite failed: target user {InviteEmail} not found. Inviter: {UserId}.",
                    InviteEmail, inviter.Id);
                
                ModelState.AddModelError(nameof(InviteEmail), "User with this email was not found.");
                ViewData["ShowInviteModal"] = true;
                return Page();

            case InviteUserResult.AlreadyMember:
                logger.LogInformation("Invite skipped: user {InviteEmail} is already a member of workspace {WorkspaceId}. Inviter: {UserId}.",
                    InviteEmail, workspaceId, inviter.Id);
                
                ModelState.AddModelError(nameof(InviteEmail), "User is already a member of this workspace.");
                ViewData["ShowInviteModal"] = true;
                return Page();

            case InviteUserResult.Forbidden:
                logger.LogWarning("Invite forbidden: user {UserId} attempted to invite {InviteEmail} to workspace {WorkspaceId} but is not an admin.",
                    inviter.Id, InviteEmail, workspaceId);
                
                ModelState.AddModelError(string.Empty, "You are not an admin of the selected workspace.");
                ViewData["ShowInviteModal"] = true;
                return Page();

            default:
                logger.LogError("Invite failed with unexpected result {Result} for inviter {UserId} and email {InviteEmail}.",
                    result, inviter.Id, InviteEmail);
                
                ModelState.AddModelError(string.Empty, "Could not invite user due to an unexpected error.");
                ViewData["ShowInviteModal"] = true;
                return Page();
        }
    }

    public async Task<IActionResult> OnPostSaveConnectionAsync(
        [FromServices] IWorkspaceConfigService cfgService,
        CancellationToken ct)
    {
        if (User.Identity is not { IsAuthenticated: true })
        {
            logger.LogInformation("Unauthenticated POST SaveConnection.");
            return RedirectToPage();
        }
        
        ModelState.Remove(nameof(NewWorkspaceName));
        ModelState.Remove(nameof(InviteEmail));

        if (!SelectedWorkspaceId.HasValue)
        {
            logger.LogWarning("SaveConnection missing SelectedWorkspaceId.");
            ModelState.AddModelError(string.Empty, "No workspace selected.");
        }

        if (string.IsNullOrWhiteSpace(ConnOrganization))
        {
            ModelState.AddModelError(nameof(ConnOrganization), "Organization is required.");
        }

        if (string.IsNullOrWhiteSpace(ConnPersonalAccessToken))
        {
            ModelState.AddModelError(nameof(ConnPersonalAccessToken), "Personal Access Token is required.");
        }

        User me = currentUserProvider.GetCurrentUser();
        Workspaces = await workspaceService.GetForUserAsync(me.Id, ct);

        if (!ModelState.IsValid)
        {
            ViewData["ShowConfigureModal"] = true;
            return Page();
        }

        Guid wsId = SelectedWorkspaceId!.Value;
        logger.LogInformation("User {UserId} saving connection for workspace {WorkspaceId}.",
            me.Id, wsId);

        SaveConnectionResult result = await cfgService.UpsertAsync(
            me.Id, wsId, ConnOrganization!, ConnPersonalAccessToken!, ct);

        switch (result)
        {
            case SaveConnectionResult.Success:
                logger.LogInformation("Connection saved for workspace {WorkspaceId} by {UserId}.", wsId, me.Id);
                return RedirectToPage();

            case SaveConnectionResult.Forbidden:
                logger.LogWarning("SaveConnection forbidden for user {UserId} on workspace {WorkspaceId}.", me.Id,
                    wsId);
                ModelState.AddModelError(string.Empty, "You are not an admin of this workspace.");
                break;

            case SaveConnectionResult.Invalid:
                logger.LogWarning("SaveConnection invalid input for workspace {WorkspaceId} by {UserId}.", wsId, me.Id);
                ModelState.AddModelError(string.Empty, "Invalid connection data.");
                break;

            default:
                logger.LogError("SaveConnection unexpected error for workspace {WorkspaceId} by {UserId}.", wsId,
                    me.Id);
                ModelState.AddModelError(string.Empty, "Could not save connection due to an unexpected error.");
                break;
        }

        ViewData["ShowConfigureModal"] = true;
        return Page();
    }
}
