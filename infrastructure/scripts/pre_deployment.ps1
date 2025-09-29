[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $TenantId,

    [Parameter(Mandatory = $true)]
    [string] $SubscriptionId,

    [Parameter(Mandatory = $false)]
    [string] $Location = "westeurope",

    [Parameter(Mandatory = $false)]
    [string] $ResourcePostfix = ""
)

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

$rgName = "rg-cvs-idx$ResourcePostfix"
Write-Host "Ensuring resource group '$rgName' in '$Location' ..." -ForegroundColor Cyan

$rg = $null
try { $rg = az group show --name $rgName -o json 2>$null | ConvertFrom-Json } catch { }

if ($rg) {
    Write-Host "Resource group already exists: $($rg.name) ($($rg.location))" -ForegroundColor Yellow
} else {
    $rg = az group create --name $rgName --location $Location --only-show-errors -o json 2>$null | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or -not $rg) { throw "Failed to create resource group '$rgName'." }
    Write-Host "Resource group created: $($rg.name) ($($rg.location))" -ForegroundColor Green
}

$saName = ("stcvsidx" + ($ResourcePostfix -replace "-", "")).ToLower()
Write-Host "Ensuring storage account '$saName' in resource group '$($rg.name)' ..." -ForegroundColor Cyan

$sa = $null
try { $sa = az storage account show --name $saName --resource-group $rg.name -o json 2>$null | ConvertFrom-Json } catch { }

if ($sa) {
    Write-Host "Storage account already exists: $($sa.name) ($($sa.location))" -ForegroundColor Yellow
} else {
    $sa = az storage account create `
        --name $saName `
        --resource-group $rg.name `
        --location $rg.location `
        --sku Standard_LRS `
        --kind StorageV2 `
        --only-show-errors `
        -o json 2>$null | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or -not $sa) { throw "Failed to create storage account '$saName'." }
    Write-Host "Storage account created: $($sa.name) ($($sa.location))" -ForegroundColor Green
}

$containerName = "tfstate"
Write-Host "Ensuring blob container '$containerName' in '$($sa.name)' ..." -ForegroundColor Cyan

$exists = ""
try { $exists = az storage container exists --name $containerName --account-name $sa.name --auth-mode login -o tsv --query exists 2>$null } catch { }

if ($exists -eq "true") {
    Write-Host "Blob container already exists: $containerName" -ForegroundColor Yellow
} else {
    az storage container create --name $containerName --account-name $sa.name --auth-mode login --public-access off -o json 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Failed to create blob container '$containerName'." }
    Write-Host "Blob container created: $containerName" -ForegroundColor Green
}

Write-Host ""
$tfOut = @"
tenant_id            = "$($account.tenantId)"
subscription_id      = "$($account.id)"
resource_group_name  = "$($rg.name)"
postfix              = "$ResourcePostfix"
storage_account_name = "$($sa.name)"
container_name       = "$containerName"
"@
Write-Host $tfOut
