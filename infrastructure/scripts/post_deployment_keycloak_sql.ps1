[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $ServerInstance,              # e.g. "myserver.database.windows.net"

    [Parameter(Mandatory = $true)]
    [string] $Database,                    # target DB

    [Parameter(Mandatory = $true)]
    [string] $AdminLogin,                  # SQL/contained admin for this DB

    [Parameter(Mandatory = $true)]
    [securestring] $AdminPassword,

    [Parameter(Mandatory = $false)]
    [string] $SchemaName = "dbo"           # app schema to fully control
)

$ErrorActionPreference = 'Stop'

function ConvertTo-PlainText([securestring]$s) {
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($s)
    try { [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) } finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
}

function New-RandomPassword {
    param([int]$Length = 32)
    $upper = 'ABCDEFGHJKLMNPQRSTUVWXYZ'
    $lower = 'abcdefghijkmnopqrstuvwxyz'
    $digit = '23456789'
    $symb  = '!#$%&()*+,-./:;<=>?@[]^_{|}~'
    $all   = ($upper + $lower + $digit + $symb).ToCharArray()

    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    $bytes = New-Object byte[] ($Length)
    $rng.GetBytes($bytes)

    $chars = @()
    $chars += $upper.ToCharArray() | Get-Random
    $chars += $lower.ToCharArray() | Get-Random
    $chars += $digit.ToCharArray() | Get-Random
    $chars += $symb.ToCharArray()  | Get-Random

    for ($i = $chars.Count; $i -lt $Length; $i++) {
        $chars += $all[ [int]($bytes[$i] % $all.Length) ]
    }

    -join ($chars | Sort-Object { Get-Random })
}

function WarmUp-AzureSql {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string] $ServerInstance,   # e.g. "sql-cvs-idx.database.windows.net"
        [Parameter(Mandatory)][string] $Database,         # e.g. "sqldb-cvs-idx-keycloak"
        [Parameter(Mandatory)][string] $Username,         # SQL/contained admin
        [Parameter(Mandatory)][string] $Password,         # string OR SecureString
        [int] $MaxSeconds = 120                           # total warm-up budget
    )

    $deadline = (Get-Date).AddSeconds($MaxSeconds)
    $delay    = 3   # start small, back off up to 30s
    $attempt  = 0

    # Common transient/Azure SQL resume error codes/messages
    $retryableSqlNumbers = @(40613, 40197, 40501, 4060, 4221, 10928, 10929)
    $retryableRegex = 'timeout|pre-?login|post-?login|is not currently available|service is busy|The client was unable to establish|transport-level error|semaphore|handshake'

    while ($true) {
        $attempt++
        try {
            Write-Host "Warm-up attempt $attempt..." -ForegroundColor Cyan
            # Short timeouts during warm-up;
            Invoke-Sqlcmd -ServerInstance $ServerInstance -Database $Database `
                -Username $Username -Password $Password `
                -Encrypt Optional -TrustServerCertificate `
                -ConnectionTimeout 30 -QueryTimeout 30 `
                -Query "SELECT 1 AS ok;" | Out-Null

            Write-Host "Database is online." -ForegroundColor Green
            return
        }
        catch {
            $now = Get-Date
            if ($now -ge $deadline) {
                Write-Host "Warm-up timed out after $MaxSeconds seconds." -ForegroundColor Red
                throw
            }

            # Decide whether to retry
            $msg = $_.Exception.Message
            $retry = $false
            
            if ($_.Exception -is [System.Data.SqlClient.SqlException]) {
                $num = $_.Exception.Number
                if ($retryableSqlNumbers -contains $num) { $retry = $true }
            }
            
            if ($msg -match $retryableRegex) { $retry = $true }

            if (-not $retry) { throw }  # not a resume-ish/transient error

            $sleep = [Math]::Min($delay, [int]($deadline - $now).TotalSeconds)
            Write-Host "Database not ready yet (sleep ${sleep}s). Reason: $msg" -ForegroundColor Yellow
            Start-Sleep -Seconds $sleep
            $delay = [Math]::Min($delay * 2, 30)   # backoff
        }
    }
}

# Ensure SqlServer module
if (-not (Get-Module -ListAvailable -Name SqlServer)) {
    try { Import-Module SqlServer -ErrorAction Stop } catch { throw "PowerShell module 'SqlServer' is required. Install-Module SqlServer" }
} else {
    Import-Module SqlServer -ErrorAction Stop
}

$adminPwd = ConvertTo-PlainText $AdminPassword
$username = "keycloak_dbuser"
$generatedPassword = "[UNKNOWN]"

WarmUp-AzureSql -ServerInstance $ServerInstance `
    -Database $Database `
    -Username $AdminLogin `
    -Password $adminPwd `
    -MaxSeconds 120

Write-Host "Ensuring database user '$username' in [$Database] on [$ServerInstance] ..." -ForegroundColor Cyan

# Check existence
$existsQuery = "SELECT CASE WHEN EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'$username') THEN 1 ELSE 0 END AS ExistsFlag;"
$exists = (Invoke-Sqlcmd -ServerInstance $ServerInstance -Database $Database -Username $AdminLogin -Password $adminPwd -Encrypt Optional -TrustServerCertificate -Query $existsQuery).ExistsFlag

if ($exists -eq 1) {
    Write-Host "User '$username' already exists. Skipping changes." -ForegroundColor Yellow
} else {
    # Create contained user with generated password and default schema
    $pwd = New-RandomPassword -Length 32
    $generatedPassword = $pwd

    $createUser = @"
CREATE USER [$username] WITH PASSWORD = N'$pwd', DEFAULT_SCHEMA = [$SchemaName];
"@
    Invoke-Sqlcmd -ServerInstance $ServerInstance -Database $Database -Username $AdminLogin -Password $adminPwd -Encrypt Optional -TrustServerCertificate -Query $createUser
    Write-Host "Created user '$username' with default schema [$SchemaName]." -ForegroundColor Green

    # Role memberships for data and DDL
    $roleGrants = @"
EXEC sp_addrolemember N'db_ddladmin',   N'$username';
EXEC sp_addrolemember N'db_datareader', N'$username';
EXEC sp_addrolemember N'db_datawriter', N'$username';
"@
    Invoke-Sqlcmd -ServerInstance $ServerInstance -Database $Database -Username $AdminLogin -Password $adminPwd -Encrypt Optional -TrustServerCertificate -Query $roleGrants
    Write-Host "Added '$username' to db_ddladmin, db_datareader, db_datawriter." -ForegroundColor Green

    # Full control within the target schema (create/alter/drop/execute/truncate/etc.)
    $schemaGrant = "GRANT CONTROL ON SCHEMA::[$SchemaName] TO [$username];"
    Invoke-Sqlcmd -ServerInstance $ServerInstance -Database $Database -Username $AdminLogin -Password $adminPwd -Encrypt Optional -TrustServerCertificate -Query $schemaGrant
    Write-Host "Granted CONTROL on schema [$SchemaName] to '$username'." -ForegroundColor Green
}

Write-Host ""
Write-Host ("sql_user_name     = ""{0}""" -f $username)
Write-Host ("sql_user_password = ""{0}""" -f $generatedPassword)