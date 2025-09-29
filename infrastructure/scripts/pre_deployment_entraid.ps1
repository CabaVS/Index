[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $TenantId,

    [Parameter(Mandatory = $true)]
    [string] $SubscriptionId,

    [Parameter(Mandatory = $true)]
    [string] $RgName,
    
    # Format: owner/repo (e.g., myorg/myinfra)
    [Parameter(Mandatory = $true)]
    [string] $GitHubRepo,
    
    # GitHub environment to allow OIDC from (e.g., production)
    [Parameter(Mandatory = $true)]
    [string] $GitHubEnv,
    
    [Parameter(Mandatory = $true)]
    [string] $TerraformStateContainerName
)

function Ensure-RoleAssignment {
    param(
        [Parameter(Mandatory)] [string] $RoleName,
        [Parameter(Mandatory)] [string] $Scope,
        [Parameter(Mandatory)] [string] $AssigneeObjectId
    )
    Write-Host "Ensuring role '$RoleName' at scope '$Scope' ..." -ForegroundColor Cyan

    $existing = $null
    try {
        $existing = az role assignment list `
            --assignee-object-id $AssigneeObjectId `
            --role "$RoleName" `
            --scope "$Scope" `
            -o json 2>$null | ConvertFrom-Json
    } catch { $existing = $null }

    if ($existing -and $existing.Count -gt 0) {
        Write-Host "Role '$RoleName' already assigned at scope '$Scope'." -ForegroundColor Yellow
        return
    }

    az role assignment create `
        --assignee-object-id $AssigneeObjectId `
        --role "$RoleName" `
        --scope "$Scope" `
        -o none

    if ($LASTEXITCODE -ne 0) { throw "Failed to assign role '$RoleName' at scope '$Scope'." }
    Write-Host "Role '$RoleName' assigned at scope '$Scope'." -ForegroundColor Green
}

$ErrorActionPreference = 'Stop'

$az = Get-Command az -ErrorAction SilentlyContinue
if (-not $az) { throw "Azure CLI ('az') is not installed or not in PATH." }

try {
    $azVerObj = az version -o json 2>$null | ConvertFrom-Json
    $azVersion = $azVerObj.'azure-cli'
} catch {
    $azVersion = "(unknown)"
}
Write-Host "Azure CLI version: $azVersion" -ForegroundColor Cyan

$account = $null
try { $account = az account show -o json 2>$null | ConvertFrom-Json } catch { }

if (-not $account -or $account.tenantId -ne $TenantId) {
    Write-Host "Signing in to tenant $TenantId ..." -ForegroundColor Cyan
    az login --tenant $TenantId --only-show-errors 1>$null 2>$null
    if ($LASTEXITCODE -ne 0) { Write-Error "Sign-in was canceled or failed. Exiting."; return }
    try { $account = az account show -o json 2>$null | ConvertFrom-Json } catch { }
    if (-not $account) { Write-Error "No active Azure CLI session detected after login. Exiting."; return }
}

if ($account.id -ne $SubscriptionId) {
    $subExists = az account list --query "[?id=='$SubscriptionId'].id | [0]" -o tsv 2>$null
    if (-not $subExists) { Write-Error "The subscription '$SubscriptionId' is not available for the signed-in account in tenant '$TenantId'."; return }
    Write-Host "Setting subscription $SubscriptionId ..." -ForegroundColor Cyan
    az account set --subscription $SubscriptionId --only-show-errors
    if ($LASTEXITCODE -ne 0) { Write-Error "Failed to set subscription '$SubscriptionId'. Exiting."; return }
}

$rg = $null
try { $rg = az group show --name $RgName -o json 2>$null | ConvertFrom-Json } catch { }

if (-not $rg) {
    Write-Error "Resource group '$RgName' was not found in subscription '$SubscriptionId' (tenant '$TenantId'). Exiting."
    return
}

Write-Host "Resource group found: $($rg.name) ($($rg.location))" -ForegroundColor Cyan

$owner,$repo = $GitHubRepo.Split('/', 2)
$appName = ("gh-{0}-{1}-{2}" -f $owner, $repo, $GitHubEnv).ToLower()

Write-Host "Ensuring App Registration '$appName' ..." -ForegroundColor Cyan

$app = $null
try {
    $apps = az ad app list --display-name $appName -o json 2>$null | ConvertFrom-Json
    if ($apps) { $app = $apps | Select-Object -First 1 }
} catch { }

if ($app) {
    Write-Host "App Registration already exists: displayName='$($app.displayName)' appId=$($app.appId)" -ForegroundColor Yellow
} else {
    $app = az ad app create `
        --display-name $appName `
        --sign-in-audience AzureADMyOrg `
        -o json 2>$null | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or -not $app) { throw "Failed to create App Registration '$appName'." }
    Write-Host "App Registration created: displayName='$($app.displayName)' appId=$($app.appId)" -ForegroundColor Green
}

Write-Host "Ensuring Service Principal for appId $($app.appId) ..." -ForegroundColor Cyan
$sp = $null
try {
    $spList = az ad sp list --filter "appId eq '$($app.appId)'" -o json 2>$null | ConvertFrom-Json
    if ($spList) { $sp = $spList | Select-Object -First 1 }
} catch { }

if ($sp) {
    Write-Host "Service Principal already exists: objectId=$($sp.id)" -ForegroundColor Yellow
} else {
    $sp = az ad sp create --id $app.appId -o json 2>$null | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or -not $sp) { throw "Failed to create Service Principal for appId '$($app.appId)'." }
    Write-Host "Service Principal created: objectId=$($sp.id)" -ForegroundColor Green
}

$AppObjectId = $app.id
$Subject = "repo:$($GitHubRepo):environment:$($GitHubEnv)"
$FicName = "${appName}-federated"

Write-Host "Ensuring GitHub OIDC federated identity '$FicName' for subject '$Subject' ..." -ForegroundColor Cyan

$existingFids = @()
try {
    $existingFids = az ad app federated-credential list --id $AppObjectId -o json 2>$null | ConvertFrom-Json
    if (-not $existingFids) { $existingFids = @() }
} catch {
    $existingFids = @()
}

$already = $existingFids | Where-Object { $_.name -eq $FicName -or $_.subject -eq $Subject }
if ($already) {
    Write-Host "Federated credential already exists (name='$($already[0].name)', subject='$($already[0].subject)'). Skipping creation." -ForegroundColor Yellow
} else {
    $TempFile = New-TemporaryFile
    try {
@"
{
  "name": "$FicName",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "$Subject",
  "description": "GitHub Actions OIDC for $GitHubRepo on environment $GitHubEnv",
  "audiences": ["api://AzureADTokenExchange"]
}
"@ | Out-File -Encoding utf8 -FilePath $TempFile.FullName

        az ad app federated-credential create `
            --id $AppObjectId `
            --parameters "@$($TempFile.FullName)" `
            -o none

        if ($LASTEXITCODE -ne 0) {
            throw "Failed to create federated credential '$FicName'."
        }

        Write-Host "Federated credential created: name='$FicName', subject='$Subject'" -ForegroundColor Green
    } finally {
        if ($TempFile -and (Test-Path $TempFile.FullName)) { Remove-Item $TempFile.FullName -ErrorAction SilentlyContinue }
    }
}

$rgScope = $rg.id
$SpObjectId = $sp.id
Ensure-RoleAssignment -RoleName "Contributor" -Scope $rgScope -AssigneeObjectId $SpObjectId
Ensure-RoleAssignment -RoleName "User Access Administrator" -Scope $rgScope -AssigneeObjectId $SpObjectId

$saList = @()
try {
    $saList = az storage account list -o json 2>$null | ConvertFrom-Json
    if (-not $saList) { $saList = @() }
} catch { $saList = @() }

if ($saList.Count -eq 0) {
    Write-Error "No storage accounts found in the subscription. Cannot assign container-level role."
    return
}

$selectedSa = $null
if ($saList.Count -eq 1) {
    $selectedSa = $saList[0]
} else {
    $inRg = $saList | Where-Object { $_.resourceGroup -eq $RgName }
    if ($inRg.Count -eq 1) {
        $selectedSa = $inRg[0]
    } elseif ($inRg.Count -gt 1) {
        Write-Error "Multiple storage accounts found in resource group '$RgName'. Please disambiguate or adjust the script."
        return
    } else {
        Write-Error "Multiple storage accounts found in subscription and none in RG '$RgName'. Please disambiguate."
        return
    }
}

$saName = $selectedSa.name
$saRg   = $selectedSa.resourceGroup

$containers = @()
try {
    $containers = az storage container list `
        --account-name $saName `
        --auth-mode login `
        -o json 2>$null | ConvertFrom-Json
    if (-not $containers) { $containers = @() }
} catch { $containers = @() }

$targetContainer = $containers | Where-Object { $_.name -eq $TerraformStateContainerName }
if (-not $targetContainer) {
    Write-Error "Container '$TerraformStateContainerName' was not found in storage account '$saName'."
    return
}

$subId   = $account.id
$ctScope = "/subscriptions/$subId/resourceGroups/$saRg/providers/Microsoft.Storage/storageAccounts/$saName/blobServices/default/containers/$TerraformStateContainerName"

Ensure-RoleAssignment -RoleName "Storage Blob Data Contributor" -Scope $ctScope -AssigneeObjectId $SpObjectId

Write-Host ""
$tfOut = @"
tenant_id            = "$($account.tenantId)"
subscription_id      = "$($account.id)"
resource_group_name  = "$($rg.name)"
app_display_name     = "$($app.displayName)"
client_id            = "$($app.appId)"
sp_object_id         = "$($sp.id)"
"@
Write-Host $tfOut