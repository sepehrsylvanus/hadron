[CmdletBinding()]
param(
    [switch]$ValidateOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$hadronRepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$hadronEnvironmentFile = Join-Path $hadronRepositoryRoot '.env'
$hadronLocalRoot = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'HadronErp\LocalHost'
$hadronApiExecutable = Join-Path $hadronRepositoryRoot '.local-host\runtime\api\Hadron.Erp.Api.exe'
$hadronKeyDirectory = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'HadronErp\DataProtectionKeys'
$hadronLogDirectory = Join-Path $hadronLocalRoot 'logs'
$hadronLogFile = Join-Path $hadronLogDirectory 'api.log'
$hadronStandardOutputLog = Join-Path $hadronLogDirectory 'api.stdout.log'
$hadronStandardErrorLog = Join-Path $hadronLogDirectory 'api.stderr.log'
$hadronStateDirectory = Join-Path $hadronLocalRoot 'state'
$hadronPidFile = Join-Path $hadronStateDirectory 'api.pid'
$hadronApiDirectory = Split-Path -Parent $hadronApiExecutable

function Import-HadronEnvironment {
    param([Parameter(Mandatory)][string]$Path)

    foreach ($hadronLine in Get-Content -LiteralPath $Path) {
        if ([string]::IsNullOrWhiteSpace($hadronLine) -or $hadronLine.TrimStart().StartsWith('#')) {
            continue
        }

        $hadronMatch = [regex]::Match($hadronLine, '^\s*([A-Za-z_][A-Za-z0-9_]*)=(.*)$')
        if (-not $hadronMatch.Success) {
            throw "Invalid environment entry in $Path."
        }

        Set-Item -Path ('Env:' + $hadronMatch.Groups[1].Value) -Value $hadronMatch.Groups[2].Value
    }
}

function Test-HadronTcpPort {
    param(
        [Parameter(Mandatory)][string]$Address,
        [Parameter(Mandatory)][int]$Port
    )

    $hadronClient = [Net.Sockets.TcpClient]::new()
    try {
        $hadronConnect = $hadronClient.ConnectAsync($Address, $Port)
        return $hadronConnect.Wait(750) -and $hadronClient.Connected
    }
    catch {
        return $false
    }
    finally {
        $hadronClient.Dispose()
    }
}

function Rotate-HadronLog {
    param([Parameter(Mandatory)][string]$Path)

    if ((Test-Path -LiteralPath $Path) -and (Get-Item -LiteralPath $Path).Length -ge 10MB) {
        Move-Item -LiteralPath $Path -Destination ($Path + '.previous') -Force
    }
}

function Write-HadronRunnerLog {
    param([Parameter(Mandatory)][string]$Message)

    Rotate-HadronLog -Path $hadronLogFile
    Add-Content -LiteralPath $hadronLogFile -Value $Message
}

function Wait-HadronDependencies {
    $hadronWaitingWasLogged = $false
    while ($true) {
        if ((Test-HadronTcpPort -Address $env:POSTGRES_HOST -Port $hadronPostgresPort) -and
            (Test-HadronTcpPort -Address $hadronMinioUri.Host -Port $hadronMinioPort)) {
            if ($hadronWaitingWasLogged) {
                Write-HadronRunnerLog "[$([DateTimeOffset]::Now.ToString('O'))] PostgreSQL and MinIO are reachable; API startup is resuming."
            }
            return
        }

        if (-not $hadronWaitingWasLogged) {
            Write-HadronRunnerLog "[$([DateTimeOffset]::Now.ToString('O'))] PostgreSQL or MinIO is unavailable; the supervisor will wait and retry without exiting."
            $hadronWaitingWasLogged = $true
        }
        Start-Sleep -Seconds 5
    }
}

New-Item -ItemType Directory -Path $hadronKeyDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $hadronLogDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $hadronStateDirectory -Force | Out-Null

trap {
    Write-HadronRunnerLog "[$([DateTimeOffset]::Now.ToString('O'))] API runner failed: $($_.Exception.Message)"
    exit 1
}

if (-not (Test-Path -LiteralPath $hadronEnvironmentFile)) {
    throw "Hadron ERP local environment file is missing: $hadronEnvironmentFile"
}
if (-not (Test-Path -LiteralPath $hadronApiExecutable)) {
    $hadronIdentity = [Security.Principal.WindowsIdentity]::GetCurrent().Name
    $hadronDirectoryExists = Test-Path -LiteralPath $hadronApiDirectory
    Write-HadronRunnerLog "[$([DateTimeOffset]::Now.ToString('O'))] API release diagnostic: identity=$hadronIdentity; directoryExists=$hadronDirectoryExists; executable=$hadronApiExecutable"
    throw "Hadron ERP local API release is missing: $hadronApiExecutable"
}

Import-HadronEnvironment -Path $hadronEnvironmentFile
$hadronRequiredVariables = @(
    'POSTGRES_HOST', 'POSTGRES_PORT', 'POSTGRES_DB', 'POSTGRES_USER', 'POSTGRES_PASSWORD',
    'MINIO_ENDPOINT', 'MINIO_ACCESS_KEY', 'MINIO_SECRET_KEY', 'MINIO_PROFILE_BUCKET'
)
foreach ($hadronVariable in $hadronRequiredVariables) {
    if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($hadronVariable))) {
        throw "Required local configuration is missing: $hadronVariable"
    }
}

$hadronPostgresPort = 0
if (-not [int]::TryParse($env:POSTGRES_PORT, [ref]$hadronPostgresPort) -or $hadronPostgresPort -lt 1 -or $hadronPostgresPort -gt 65535) {
    throw 'POSTGRES_PORT must be a valid TCP port.'
}
$hadronMinioUri = $null
if (-not [Uri]::TryCreate($env:MINIO_ENDPOINT, [UriKind]::Absolute, [ref]$hadronMinioUri)) {
    throw 'MINIO_ENDPOINT must be an absolute URI.'
}
$hadronMinioPort = $hadronMinioUri.Port

if ($ValidateOnly) {
    [pscustomobject]@{
        Component = 'API'
        Executable = $hadronApiExecutable
        EnvironmentFile = $hadronEnvironmentFile
        DataProtectionKeys = $hadronKeyDirectory
        PostgresEndpoint = "$($env:POSTGRES_HOST):$hadronPostgresPort"
        MinioEndpoint = "$($hadronMinioUri.Host):$hadronMinioPort"
        Valid = $true
    }
    return
}

$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:DOTNET_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = 'http://127.0.0.1:5080'
$env:DATA_PROTECTION_KEYS_PATH = $hadronKeyDirectory
$env:Logging__LogLevel__Default = 'Warning'
Set-Item -Path 'Env:Logging__LogLevel__Microsoft.AspNetCore' -Value 'Warning'
Set-Item -Path 'Env:Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command' -Value 'Warning'

foreach ($hadronOutputPath in @($hadronLogFile, $hadronStandardOutputLog, $hadronStandardErrorLog)) {
    Rotate-HadronLog -Path $hadronOutputPath
}
Write-HadronRunnerLog "`n[$([DateTimeOffset]::Now.ToString('O'))] Hadron ERP local API supervisor started."

$hadronPortConflictWasLogged = $false
while ($true) {
    Wait-HadronDependencies

    if (Test-HadronTcpPort -Address '127.0.0.1' -Port 5080) {
        if (-not $hadronPortConflictWasLogged) {
            Write-HadronRunnerLog "[$([DateTimeOffset]::Now.ToString('O'))] Port 5080 is occupied without an owned API PID; waiting without terminating or trusting that process."
            $hadronPortConflictWasLogged = $true
        }
        Start-Sleep -Seconds 15
        continue
    }
    if ($hadronPortConflictWasLogged) {
        Write-HadronRunnerLog "[$([DateTimeOffset]::Now.ToString('O'))] Port 5080 is available; API startup is resuming."
        $hadronPortConflictWasLogged = $false
    }

    $hadronProcess = $null
    $hadronExitCode = 1
    try {
        foreach ($hadronOutputPath in @($hadronLogFile, $hadronStandardOutputLog, $hadronStandardErrorLog)) {
            Rotate-HadronLog -Path $hadronOutputPath
        }
        Write-HadronRunnerLog "[$([DateTimeOffset]::Now.ToString('O'))] Starting Hadron ERP local API child."
        $hadronProcess = Start-Process -FilePath $hadronApiExecutable `
            -WorkingDirectory $hadronApiDirectory `
            -WindowStyle Hidden `
            -RedirectStandardOutput $hadronStandardOutputLog `
            -RedirectStandardError $hadronStandardErrorLog `
            -PassThru
        [IO.File]::WriteAllText($hadronPidFile, $hadronProcess.Id.ToString([Globalization.CultureInfo]::InvariantCulture))
        $hadronProcess.WaitForExit()
        $hadronProcess.Refresh()
        $hadronExitCode = $hadronProcess.ExitCode
    }
    catch {
        Write-HadronRunnerLog "[$([DateTimeOffset]::Now.ToString('O'))] API child failed: $($_.Exception.Message)"
    }
    finally {
        if ($null -ne $hadronProcess -and (Test-Path -LiteralPath $hadronPidFile)) {
            $hadronRecordedPid = [IO.File]::ReadAllText($hadronPidFile).Trim()
            if ($hadronRecordedPid -eq $hadronProcess.Id.ToString([Globalization.CultureInfo]::InvariantCulture)) {
                [IO.File]::Delete($hadronPidFile)
            }
        }
    }

    Write-HadronRunnerLog "[$([DateTimeOffset]::Now.ToString('O'))] API child exited with code $hadronExitCode; restarting in five seconds."
    Start-Sleep -Seconds 5
}
