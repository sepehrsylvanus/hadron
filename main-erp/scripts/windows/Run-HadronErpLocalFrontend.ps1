[CmdletBinding()]
param(
    [switch]$ValidateOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$hadronRepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$hadronFrontendDirectory = Join-Path $hadronRepositoryRoot 'src\frontend'
$hadronPackageFile = Join-Path $hadronFrontendDirectory 'package.json'
$hadronNodeDirectory = Join-Path $env:ProgramFiles 'nodejs'
$hadronNodeExecutable = Join-Path $hadronNodeDirectory 'node.exe'
$hadronViteEntry = Join-Path $hadronFrontendDirectory 'node_modules\vite\bin\vite.js'
$hadronLocalRoot = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'HadronErp\LocalHost'
$hadronLogDirectory = Join-Path $hadronLocalRoot 'logs'
$hadronLogFile = Join-Path $hadronLogDirectory 'frontend.log'
$hadronStandardOutputLog = Join-Path $hadronLogDirectory 'frontend.stdout.log'
$hadronStandardErrorLog = Join-Path $hadronLogDirectory 'frontend.stderr.log'
$hadronStateDirectory = Join-Path $hadronLocalRoot 'state'
$hadronPidFile = Join-Path $hadronStateDirectory 'frontend.pid'

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

New-Item -ItemType Directory -Path $hadronLogDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $hadronStateDirectory -Force | Out-Null

trap {
    Write-HadronRunnerLog "[$([DateTimeOffset]::Now.ToString('O'))] Frontend runner failed: $($_.Exception.Message)"
    exit 1
}

if (-not (Test-Path -LiteralPath $hadronPackageFile)) {
    throw "Hadron ERP frontend package is missing: $hadronPackageFile"
}
if (-not (Test-Path -LiteralPath $hadronNodeExecutable)) {
    throw "The approved Node.js executable is missing: $hadronNodeExecutable"
}
if (-not (Test-Path -LiteralPath $hadronViteEntry)) {
    throw 'Frontend dependencies are missing. Run npm install before installing the local host.'
}

if ($ValidateOnly) {
    [pscustomobject]@{
        Component = 'Frontend'
        Executable = $hadronNodeExecutable
        ViteEntry = $hadronViteEntry
        WorkingDirectory = $hadronFrontendDirectory
        BindAddress = '0.0.0.0:5173 (firewall-scoped company LAN)'
        Valid = $true
    }
    return
}

$env:Path = $hadronNodeDirectory + ';' + $env:Path
foreach ($hadronOutputPath in @($hadronLogFile, $hadronStandardOutputLog, $hadronStandardErrorLog)) {
    Rotate-HadronLog -Path $hadronOutputPath
}
Write-HadronRunnerLog "`n[$([DateTimeOffset]::Now.ToString('O'))] Hadron ERP local frontend supervisor started."

$hadronPortConflictWasLogged = $false
while ($true) {
    if (Test-HadronTcpPort -Address '127.0.0.1' -Port 5173) {
        if (-not $hadronPortConflictWasLogged) {
            Write-HadronRunnerLog "[$([DateTimeOffset]::Now.ToString('O'))] Port 5173 is occupied without an owned frontend PID; waiting without terminating or trusting that process."
            $hadronPortConflictWasLogged = $true
        }
        Start-Sleep -Seconds 15
        continue
    }
    if ($hadronPortConflictWasLogged) {
        Write-HadronRunnerLog "[$([DateTimeOffset]::Now.ToString('O'))] Port 5173 is available; frontend startup is resuming."
        $hadronPortConflictWasLogged = $false
    }

    $hadronProcess = $null
    $hadronExitCode = 1
    try {
        foreach ($hadronOutputPath in @($hadronLogFile, $hadronStandardOutputLog, $hadronStandardErrorLog)) {
            Rotate-HadronLog -Path $hadronOutputPath
        }
        Write-HadronRunnerLog "[$([DateTimeOffset]::Now.ToString('O'))] Starting Hadron ERP local frontend child."
        $hadronProcess = Start-Process -FilePath $hadronNodeExecutable `
            -ArgumentList @(("`"{0}`"" -f $hadronViteEntry), '--host', '0.0.0.0', '--port', '5173') `
            -WorkingDirectory $hadronFrontendDirectory `
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
        Write-HadronRunnerLog "[$([DateTimeOffset]::Now.ToString('O'))] Frontend child failed: $($_.Exception.Message)"
    }
    finally {
        if ($null -ne $hadronProcess -and (Test-Path -LiteralPath $hadronPidFile)) {
            $hadronRecordedPid = [IO.File]::ReadAllText($hadronPidFile).Trim()
            if ($hadronRecordedPid -eq $hadronProcess.Id.ToString([Globalization.CultureInfo]::InvariantCulture)) {
                [IO.File]::Delete($hadronPidFile)
            }
        }
    }

    Write-HadronRunnerLog "[$([DateTimeOffset]::Now.ToString('O'))] Frontend child exited with code $hadronExitCode; restarting in five seconds."
    Start-Sleep -Seconds 5
}
