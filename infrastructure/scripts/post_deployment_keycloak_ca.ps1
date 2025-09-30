[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $ServerInstance,              # e.g. "myserver.database.windows.net"
    
    [Parameter(Mandatory = $true)]
    [string] $Database,                    # target DB

    [Parameter(Mandatory = $true)]
    [string] $TenantId,
    
    [Parameter(Mandatory = $true)]
    [string] $SubscriptionId,
    
    [Parameter(Mandatory = $true)]
    [string] $RgName,
    
    [Parameter(Mandatory = $true)]
    [string] $KeycloakDatabaseAdmin,
    
    [Parameter(Mandatory = $true)]
    [securestring] $KeycloakDatabaseAdminPassword,
    
    [Parameter(Mandatory = $true)]
    [string] $KeycloakConsoleAdmin,
    
    [Parameter(Mandatory = $true)]
    [securestring] $KeycloakConsoleAdminPassword
)

$ErrorActionPreference = 'Stop'

function ConvertTo-PlainText([securestring]$s) {
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($s)
    try { [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) } finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
}

function Get-JdbcSqlUrl {
    param(
        [Parameter(Mandatory)][string] $ServerFqdn,
        [Parameter(Mandatory)][string] $DbName
    )
    
    "jdbc:sqlserver://$($ServerFqdn):1433;databaseName=$DbName;encrypt=true;trustServerCertificate=false;loginTimeout=120"
}

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

# --- Locate the Container App that contains "keycloak" in its name ---
Write-Host "Searching for Container App with 'keycloak' in the name in RG '$RgName'..." -ForegroundColor Cyan
$apps = az containerapp list -g $RgName -o json 2>$null | ConvertFrom-Json
if (-not $apps) {
    Write-Error "No Container Apps found in resource group '$RgName'."
    return
}

$keycloakApps = $apps | Where-Object { $_.name -match 'keycloak' }
if (-not $keycloakApps -or $keycloakApps.Count -eq 0) {
    Write-Error "No Container App with 'keycloak' in its name was found in RG '$RgName'."
    return
}
elseif ($keycloakApps.Count -gt 1) {
    # If there are multiple matches, pick the shortest name as a heuristic
    $selected = $keycloakApps | Sort-Object { $_.name.Length } | Select-Object -First 1
    Write-Host "Multiple matches found. Selecting '$($selected.name)' by shortest-name heuristic." -ForegroundColor Yellow
} else {
    $selected = $keycloakApps[0]
}
$caName = $selected.name
Write-Host "Using Container App: $caName" -ForegroundColor Green

# --- Derive values for env vars ---
# KC_DB_URL is based on the inputs we already have:
$jdbc = Get-JdbcSqlUrl -ServerFqdn $ServerInstance -DbName $Database

# KC_HOSTNAME should reflect the public FQDN of the Container App (if ingress is enabled)
$ca = az containerapp show -g $RgName -n $caName -o json 2>$null | ConvertFrom-Json
$kcHostname = $ca.properties.configuration.ingress.fqdn

# Convert mandatory passwords to plain text for env var
$kcDbAdminPwdPlain = ConvertTo-PlainText $KeycloakDatabaseAdminPassword
$kcConsoleAdminPwdPlain = ConvertTo-PlainText $KeycloakConsoleAdminPassword

# Prepare env var list (only include values that are known; skip unknown password to avoid overwriting an existing secret)
$envPairs = @(
    "KC_DB=mssql",
    "KC_DB_URL=$jdbc",
    "KC_DB_USERNAME=$KeycloakDatabaseAdmin",
    "KC_DB_PASSWORD=$kcDbAdminPwdPlain",
    "KC_HTTP_ENABLED=true",
    "KC_HTTP_PORT=8080",
    "KC_PROXY=edge",
    "KC_BOOTSTRAP_ADMIN_USERNAME=$KeycloakConsoleAdmin",
    "KC_BOOTSTRAP_ADMIN_PASSWORD=$kcConsoleAdminPwdPlain",
    "KC_TRACING_ENABLED=true",
    "KC_TRACING_PROTOCOL=grpc",
    "KC_TRACING_ENDPOINT=http://localhost:4317",
    "KC_TRACING_SERVICE_NAME=$caName",
    "KC_TRACING_SAMPLER_RATIO=1.0",
    "KC_METRICS_ENABLED=true",
    "KC_LOG=file",
    "KC_LOG_FILE=/var/log/keycloak/keycloak.log",
    "KC_LOG_FILE_OUTPUT=json",
    "KC_LOG_LEVEL=info"
)

if ($kcHostname) {
    $envPairs += "KC_HOSTNAME=https://$($kcHostname)"
} else {
    Write-Host "Container App '$caName' has no public FQDN (ingress may be disabled). Skipping KC_HOSTNAME." -ForegroundColor Yellow
}

# --- Upsert env vars idempotently ---
# 'az containerapp update --set-env-vars' performs upsert (existing values are overwritten, missing are added).
# This does not modify other settings of the app.
Write-Host "Updating env vars on Container App '$caName'..." -ForegroundColor Cyan

# The CLI prefers the pairs as separate args; We'll pass them in one go, space-separated.
$envPairsQuoted = $envPairs | ForEach-Object {
    '"' + ($_.Replace('"','\"')) + '"'
}

$updateArgs = @(
    'containerapp','update',
    '-g', $RgName,
    '-n', $caName,
    '--container-name', 'keycloak',
    '--set-env-vars'
) + $envPairsQuoted + @('--only-show-errors')

$null = az @updateArgs

if ($LASTEXITCODE -ne 0) {
    Write-Error "Failed to update environment variables on Container App '$caName'."
    return
}

Write-Host "Environment variables have been upserted on '$caName'." -ForegroundColor Green
$envPairsQuoted | ForEach-Object { Write-Host "arg: $_" -ForegroundColor DarkGray }