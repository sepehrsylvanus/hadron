[CmdletBinding()]
param(
    [ValidateSet('Install', 'Start', 'Stop', 'Status', 'Uninstall')]
    [string]$Action = 'Status'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$hadronRepositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$hadronApiRunner = Join-Path $PSScriptRoot 'Run-HadronErpLocalApi.ps1'
$hadronFrontendRunner = Join-Path $PSScriptRoot 'Run-HadronErpLocalFrontend.ps1'
$hadronApiExecutable = Join-Path $hadronRepositoryRoot '.local-host\runtime\api\Hadron.Erp.Api.exe'
$hadronNodeExecutable = Join-Path (Join-Path $env:ProgramFiles 'nodejs') 'node.exe'
$hadronViteEntry = Join-Path $hadronRepositoryRoot 'src\frontend\node_modules\vite\bin\vite.js'
$hadronStateDirectory = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'HadronErp\LocalHost\state'
$hadronApiPidFile = Join-Path $hadronStateDirectory 'api.pid'
$hadronFrontendPidFile = Join-Path $hadronStateDirectory 'frontend.pid'
$hadronApiTaskName = 'Hadron ERP Local API'
$hadronFrontendTaskName = 'Hadron ERP Local Frontend'
$hadronTaskNames = @($hadronApiTaskName, $hadronFrontendTaskName)

function Get-HadronTask {
    param([Parameter(Mandatory)][string]$TaskName)
    Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
}

function Test-HadronTcpPort {
    param([Parameter(Mandatory)][int]$Port)

    $hadronClient = [Net.Sockets.TcpClient]::new()
    try {
        $hadronConnect = $hadronClient.ConnectAsync('127.0.0.1', $Port)
        return $hadronConnect.Wait(750) -and $hadronClient.Connected
    }
    catch {
        return $false
    }
    finally {
        $hadronClient.Dispose()
    }
}

function Test-HadronOwnedProcess {
    param(
        [Parameter(Mandatory)][string]$PidFile,
        [Parameter(Mandatory)][string]$ExpectedExecutable,
        [string]$RequiredCommandFragment
    )

    if (-not (Test-Path -LiteralPath $PidFile)) {
        return $false
    }

    $hadronPid = 0
    if (-not [int]::TryParse([IO.File]::ReadAllText($PidFile).Trim(), [ref]$hadronPid) -or $hadronPid -le 0) {
        return $false
    }
    $hadronProcess = Get-CimInstance Win32_Process -Filter "ProcessId = $hadronPid" -ErrorAction SilentlyContinue
    if ($null -eq $hadronProcess -or [string]::IsNullOrWhiteSpace($hadronProcess.ExecutablePath)) {
        return $false
    }
    if (-not ([IO.Path]::GetFullPath($hadronProcess.ExecutablePath)).Equals(
            [IO.Path]::GetFullPath($ExpectedExecutable), [StringComparison]::OrdinalIgnoreCase)) {
        return $false
    }
    if (-not [string]::IsNullOrWhiteSpace($RequiredCommandFragment) -and
        ([string]$hadronProcess.CommandLine).IndexOf($RequiredCommandFragment, [StringComparison]::OrdinalIgnoreCase) -lt 0) {
        return $false
    }
    return $true
}

function Stop-HadronOwnedProcess {
    param(
        [Parameter(Mandatory)][string]$Component,
        [Parameter(Mandatory)][string]$PidFile,
        [Parameter(Mandatory)][string]$ExpectedExecutable,
        [string]$RequiredCommandFragment
    )

    if (-not (Test-Path -LiteralPath $PidFile)) {
        return
    }

    $hadronPid = 0
    $hadronPidText = [IO.File]::ReadAllText($PidFile).Trim()
    if (-not [int]::TryParse($hadronPidText, [ref]$hadronPid) -or $hadronPid -le 0) {
        throw "$Component PID file is invalid; no process was terminated."
    }

    $hadronProcess = Get-CimInstance Win32_Process -Filter "ProcessId = $hadronPid" -ErrorAction SilentlyContinue
    if ($null -eq $hadronProcess) {
        [IO.File]::Delete($PidFile)
        return
    }

    $hadronActualExecutable = [IO.Path]::GetFullPath($hadronProcess.ExecutablePath)
    $hadronExpectedExecutable = [IO.Path]::GetFullPath($ExpectedExecutable)
    if (-not $hadronActualExecutable.Equals($hadronExpectedExecutable, [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Component PID $hadronPid belongs to a different executable; it was not terminated."
    }
    if (-not [string]::IsNullOrWhiteSpace($RequiredCommandFragment) -and
        ([string]$hadronProcess.CommandLine).IndexOf($RequiredCommandFragment, [StringComparison]::OrdinalIgnoreCase) -lt 0) {
        throw "$Component PID $hadronPid has an unexpected command line; it was not terminated."
    }

    Stop-Process -Id $hadronPid -Force
    $hadronProcessDeadline = [DateTimeOffset]::UtcNow.AddSeconds(10)
    while ($null -ne (Get-Process -Id $hadronPid -ErrorAction SilentlyContinue) -and
        [DateTimeOffset]::UtcNow -lt $hadronProcessDeadline) {
        Start-Sleep -Milliseconds 250
    }
    if ($null -ne (Get-Process -Id $hadronPid -ErrorAction SilentlyContinue)) {
        throw "$Component PID $hadronPid did not stop."
    }
    [IO.File]::Delete($PidFile)
}

function Stop-HadronSupervisorProcesses {
    param([Parameter(Mandatory)][string[]]$RunnerPaths)

    $hadronNormalizedRunnerPaths = @($RunnerPaths | ForEach-Object { [IO.Path]::GetFullPath($_) })
    $hadronSupervisorProcesses = @(Get-CimInstance Win32_Process -ErrorAction SilentlyContinue | Where-Object {
        if ($_.Name -notin @('powershell.exe', 'pwsh.exe') -or [string]::IsNullOrWhiteSpace($_.CommandLine)) {
            return $false
        }
        foreach ($hadronRunnerPath in $hadronNormalizedRunnerPaths) {
            if ([string]$_.CommandLine -like "*$hadronRunnerPath*") {
                return $true
            }
        }
        return $false
    })

    foreach ($hadronSupervisorProcess in $hadronSupervisorProcesses) {
        Stop-Process -Id $hadronSupervisorProcess.ProcessId -Force -ErrorAction Stop
    }

    $hadronSupervisorDeadline = [DateTimeOffset]::UtcNow.AddSeconds(10)
    foreach ($hadronSupervisorProcess in $hadronSupervisorProcesses) {
        while ($null -ne (Get-Process -Id $hadronSupervisorProcess.ProcessId -ErrorAction SilentlyContinue) -and
            [DateTimeOffset]::UtcNow -lt $hadronSupervisorDeadline) {
            Start-Sleep -Milliseconds 250
        }
        if ($null -ne (Get-Process -Id $hadronSupervisorProcess.ProcessId -ErrorAction SilentlyContinue)) {
            throw "Hadron supervisor PID $($hadronSupervisorProcess.ProcessId) did not stop."
        }
    }
}

function Stop-HadronTasks {
    foreach ($hadronTaskName in $hadronTaskNames) {
        if (Get-HadronTask -TaskName $hadronTaskName) {
            Stop-ScheduledTask -TaskName $hadronTaskName -ErrorAction SilentlyContinue
        }
    }

    $hadronStopDeadline = [DateTimeOffset]::UtcNow.AddSeconds(30)
    do {
        $hadronRunningTasks = @($hadronTaskNames | Where-Object {
            $hadronTask = Get-HadronTask -TaskName $_
            $null -ne $hadronTask -and $hadronTask.State -in @('Running', 'Queued')
        })
        if ($hadronRunningTasks.Count -eq 0) {
            break
        }
        Start-Sleep -Milliseconds 500
    } while ([DateTimeOffset]::UtcNow -lt $hadronStopDeadline)

    if ($hadronRunningTasks.Count -gt 0) {
        throw "Hadron ERP tasks did not stop cleanly: $($hadronRunningTasks -join ', ')"
    }

    # A previously interrupted or re-registered Scheduled Task can leave its
    # PowerShell supervisor detached while the task itself reports Ready. Stop
    # only supervisors whose command line contains one of this repository's
    # exact runner paths before terminating their owned children.
    Stop-HadronSupervisorProcesses -RunnerPaths @($hadronApiRunner, $hadronFrontendRunner)

    Stop-HadronOwnedProcess -Component 'API' -PidFile $hadronApiPidFile -ExpectedExecutable $hadronApiExecutable
    Stop-HadronOwnedProcess -Component 'Frontend' -PidFile $hadronFrontendPidFile `
        -ExpectedExecutable $hadronNodeExecutable -RequiredCommandFragment $hadronViteEntry

    $hadronConflictingPorts = @(@(5080, 5173) | Where-Object { Test-HadronTcpPort -Port $_ })
    if ($hadronConflictingPorts.Count -gt 0) {
        Write-Warning "Hadron ERP managed processes stopped, but unrelated listeners remain on TCP port(s) $($hadronConflictingPorts -join ', '); they were not terminated."
    }
}

function Start-HadronTasks {
    foreach ($hadronTaskName in $hadronTaskNames) {
        $hadronTask = Get-HadronTask -TaskName $hadronTaskName
        if (-not $hadronTask) {
            throw "Scheduled task is not installed: $hadronTaskName"
        }
        if ($hadronTask.State -notin @('Running', 'Queued')) {
            Start-ScheduledTask -TaskName $hadronTaskName
        }
    }
}

function Test-HadronEndpoint {
    param([Parameter(Mandatory)][string]$Url)
    try {
        $hadronResponse = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 4
        return [int]$hadronResponse.StatusCode
    }
    catch {
        $hadronResponseProperty = $_.Exception.PSObject.Properties['Response']
        if ($null -ne $hadronResponseProperty -and $null -ne $hadronResponseProperty.Value) {
            return [int]$hadronResponseProperty.Value.StatusCode
        }
        return 0
    }
}

function Wait-HadronHostReady {
    $hadronReadyDeadline = [DateTimeOffset]::UtcNow.AddSeconds(90)
    do {
        $hadronApiTask = Get-HadronTask -TaskName $hadronApiTaskName
        $hadronFrontendTask = Get-HadronTask -TaskName $hadronFrontendTaskName
        if ($null -ne $hadronApiTask -and $null -ne $hadronFrontendTask -and
            $hadronApiTask.State -eq 'Running' -and $hadronFrontendTask.State -eq 'Running' -and
            (Test-HadronOwnedProcess -PidFile $hadronApiPidFile -ExpectedExecutable $hadronApiExecutable) -and
            (Test-HadronOwnedProcess -PidFile $hadronFrontendPidFile -ExpectedExecutable $hadronNodeExecutable -RequiredCommandFragment $hadronViteEntry) -and
            (Test-HadronEndpoint -Url 'http://127.0.0.1:5173/') -eq 200 -and
            (Test-HadronEndpoint -Url 'http://127.0.0.1:5080/health/live') -eq 200 -and
            (Test-HadronEndpoint -Url 'http://127.0.0.1:5080/health/ready') -eq 200) {
            return
        }
        Start-Sleep -Seconds 2
    } while ([DateTimeOffset]::UtcNow -lt $hadronReadyDeadline)

    Show-HadronStatus
    throw 'Hadron ERP local host did not reach a healthy state within 90 seconds.'
}

function Show-HadronStatus {
    $hadronRows = foreach ($hadronTaskName in $hadronTaskNames) {
        $hadronTask = Get-HadronTask -TaskName $hadronTaskName
        if (-not $hadronTask) {
            [pscustomobject]@{ Component = $hadronTaskName; State = 'NotInstalled'; LastRun = $null; LastResult = $null }
            continue
        }
        $hadronInfo = Get-ScheduledTaskInfo -TaskName $hadronTaskName
        [pscustomobject]@{
            Component = $hadronTaskName
            State = $hadronTask.State
            LastRun = $hadronInfo.LastRunTime
            LastResult = $hadronInfo.LastTaskResult
        }
    }
    $hadronRows | Format-Table -AutoSize
    [pscustomobject]@{
        FrontendOwned = Test-HadronOwnedProcess -PidFile $hadronFrontendPidFile -ExpectedExecutable $hadronNodeExecutable -RequiredCommandFragment $hadronViteEntry
        ApiOwned = Test-HadronOwnedProcess -PidFile $hadronApiPidFile -ExpectedExecutable $hadronApiExecutable
        Frontend = Test-HadronEndpoint -Url 'http://127.0.0.1:5173/'
        ApiLive = Test-HadronEndpoint -Url 'http://127.0.0.1:5080/health/live'
        ApiReady = Test-HadronEndpoint -Url 'http://127.0.0.1:5080/health/ready'
    } | Format-List
}

switch ($Action) {
    'Install' {
        foreach ($hadronRequiredPath in @($hadronApiRunner, $hadronFrontendRunner, $hadronApiExecutable, (Join-Path $hadronRepositoryRoot '.env'))) {
            if (-not (Test-Path -LiteralPath $hadronRequiredPath)) {
                throw "Local host prerequisite is missing: $hadronRequiredPath"
            }
        }

        & $hadronApiRunner -ValidateOnly | Out-Null
        & $hadronFrontendRunner -ValidateOnly | Out-Null

        $hadronPreviousTaskXml = @{}
        $hadronPreviouslyRunning = @{}
        foreach ($hadronTaskName in $hadronTaskNames) {
            $hadronExistingTask = Get-HadronTask -TaskName $hadronTaskName
            if ($null -ne $hadronExistingTask) {
                $hadronPreviousTaskXml[$hadronTaskName] = Export-ScheduledTask -TaskName $hadronTaskName
                $hadronPreviouslyRunning[$hadronTaskName] = $hadronExistingTask.State -in @('Running', 'Queued')
            }
        }

        try {
            Stop-HadronTasks
            $hadronPowerShell = Join-Path ([Environment]::GetFolderPath('System')) 'WindowsPowerShell\v1.0\powershell.exe'
            $hadronUser = [Security.Principal.WindowsIdentity]::GetCurrent().Name
            $hadronPrincipal = New-ScheduledTaskPrincipal -UserId $hadronUser -LogonType Interactive -RunLevel Limited
            $hadronTrigger = New-ScheduledTaskTrigger -AtLogOn -User $hadronUser
            $hadronTrigger.Delay = 'PT45S'
            $hadronSettings = New-ScheduledTaskSettingsSet `
                -AllowStartIfOnBatteries `
                -DontStopIfGoingOnBatteries `
                -StartWhenAvailable `
                -MultipleInstances IgnoreNew `
                -RestartCount 10 `
                -RestartInterval (New-TimeSpan -Minutes 1) `
                -ExecutionTimeLimit ([TimeSpan]::Zero)

            $hadronApiAction = New-ScheduledTaskAction -Execute $hadronPowerShell `
                -Argument "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$hadronApiRunner`"" `
                -WorkingDirectory $hadronRepositoryRoot
            $hadronFrontendAction = New-ScheduledTaskAction -Execute $hadronPowerShell `
                -Argument "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$hadronFrontendRunner`"" `
                -WorkingDirectory $hadronRepositoryRoot

            Register-ScheduledTask -TaskName $hadronApiTaskName -Action $hadronApiAction -Trigger $hadronTrigger `
                -Settings $hadronSettings -Principal $hadronPrincipal `
                -Description 'Runs the local Hadron ERP API under the signed-in Windows user with restart-on-failure.' -Force | Out-Null
            Register-ScheduledTask -TaskName $hadronFrontendTaskName -Action $hadronFrontendAction -Trigger $hadronTrigger `
                -Settings $hadronSettings -Principal $hadronPrincipal `
                -Description 'Runs the local Hadron ERP frontend under the signed-in Windows user with restart-on-failure.' -Force | Out-Null

            Start-HadronTasks
            Wait-HadronHostReady
        }
        catch {
            $hadronInstallError = $_
            $hadronRollbackErrors = @()

            foreach ($hadronTaskName in $hadronTaskNames) {
                try {
                    if (Get-HadronTask -TaskName $hadronTaskName) {
                        Stop-ScheduledTask -TaskName $hadronTaskName -ErrorAction Stop
                    }
                }
                catch {
                    $hadronRollbackErrors += "Could not stop $hadronTaskName during rollback: $($_.Exception.Message)"
                }
            }

            $hadronRollbackStopDeadline = [DateTimeOffset]::UtcNow.AddSeconds(30)
            do {
                $hadronRollbackRunningTasks = @($hadronTaskNames | Where-Object {
                    $hadronRollbackTask = Get-HadronTask -TaskName $_
                    $null -ne $hadronRollbackTask -and $hadronRollbackTask.State -in @('Running', 'Queued')
                })
                if ($hadronRollbackRunningTasks.Count -eq 0) {
                    break
                }
                Start-Sleep -Milliseconds 500
            } while ([DateTimeOffset]::UtcNow -lt $hadronRollbackStopDeadline)
            if ($hadronRollbackRunningTasks.Count -gt 0) {
                $hadronRollbackErrors += "Tasks still running during rollback: $($hadronRollbackRunningTasks -join ', ')"
            }

            try {
                Stop-HadronOwnedProcess -Component 'API' -PidFile $hadronApiPidFile -ExpectedExecutable $hadronApiExecutable
            }
            catch {
                $hadronRollbackErrors += "Could not stop the owned API child during rollback: $($_.Exception.Message)"
            }
            try {
                Stop-HadronOwnedProcess -Component 'Frontend' -PidFile $hadronFrontendPidFile `
                    -ExpectedExecutable $hadronNodeExecutable -RequiredCommandFragment $hadronViteEntry
            }
            catch {
                $hadronRollbackErrors += "Could not stop the owned frontend child during rollback: $($_.Exception.Message)"
            }

            $hadronRestoredTaskNames = @{}
            foreach ($hadronTaskName in $hadronTaskNames) {
                try {
                    if ($hadronPreviousTaskXml.ContainsKey($hadronTaskName)) {
                        Register-ScheduledTask -TaskName $hadronTaskName -Xml $hadronPreviousTaskXml[$hadronTaskName] -Force | Out-Null
                        $hadronRestoredTaskNames[$hadronTaskName] = $true
                    }
                    elseif (Get-HadronTask -TaskName $hadronTaskName) {
                        Unregister-ScheduledTask -TaskName $hadronTaskName -Confirm:$false
                    }
                }
                catch {
                    $hadronRollbackErrors += "Could not restore $hadronTaskName during rollback: $($_.Exception.Message)"
                }
            }

            foreach ($hadronTaskName in $hadronTaskNames) {
                if ($hadronRestoredTaskNames.ContainsKey($hadronTaskName) -and $hadronPreviouslyRunning[$hadronTaskName]) {
                    try {
                        Start-ScheduledTask -TaskName $hadronTaskName
                    }
                    catch {
                        $hadronRollbackErrors += "Could not restart $hadronTaskName after rollback: $($_.Exception.Message)"
                    }
                }
            }

            foreach ($hadronRollbackError in $hadronRollbackErrors) {
                Write-Warning $hadronRollbackError
            }
            $PSCmdlet.ThrowTerminatingError($hadronInstallError)
        }

        Show-HadronStatus
    }
    'Start' {
        Start-HadronTasks
        Wait-HadronHostReady
        Show-HadronStatus
    }
    'Stop' {
        Stop-HadronTasks
        Start-Sleep -Seconds 2
        Show-HadronStatus
    }
    'Status' {
        Show-HadronStatus
    }
    'Uninstall' {
        Stop-HadronTasks
        foreach ($hadronTaskName in $hadronTaskNames) {
            if (Get-HadronTask -TaskName $hadronTaskName) {
                Unregister-ScheduledTask -TaskName $hadronTaskName -Confirm:$false
            }
        }
        Show-HadronStatus
    }
}
