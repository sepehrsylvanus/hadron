# Interactive, loopback-only development launcher. This is not the LAN host supervisor.
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..\..')

function Invoke-Checked {
    param([string]$Program, [string[]]$Arguments)
    & $Program @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Program failed (exit code $LASTEXITCODE)." }
}
function Find-FreePort {
    param([int]$Preferred)
    if ($Preferred -lt 1 -or $Preferred -gt 65535) { throw "Invalid port: $Preferred" }
    for ($port = $Preferred; $port -le 65535; $port++) {
        if ($script:chosenPorts.Contains($port)) { continue }
        $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, $port)
        try {
            $listener.ExclusiveAddressUse = $true
            $listener.Start()
            $script:chosenPorts.Add($port) | Out-Null
            return $port
        } catch { } finally { $listener.Stop() }
    }
    throw "No available TCP port at or above $Preferred."
}
function Get-ComposePort {
    param([string]$Service, [int]$ContainerPort)
    # Only reuse the published port of our running Compose container, not an unrelated listener.
    $id = (& docker compose ps -q --status running $Service | Select-Object -First 1)
    if ($LASTEXITCODE -ne 0 -or -not $id) { return 0 }
    $binding = (& docker compose port $Service $ContainerPort | Select-Object -First 1)
    if ($LASTEXITCODE -eq 0 -and $binding -match '^127\.0\.0\.1:(\d+)$') { return [int]$Matches[1] }
    return 0
}
function Select-ServicePort {
    param([string]$Service, [int]$ContainerPort, [int]$Preferred)
    $existing = Get-ComposePort $Service $ContainerPort
    if ($existing -gt 0) { $script:chosenPorts.Add($existing) | Out-Null; return $existing }
    return Find-FreePort $Preferred
}

$api = $null
$frontend = $null
try {
    if (-not (Test-Path '.env')) { throw 'Create .env from .env.example and configure the required secrets first.' }
    foreach ($line in Get-Content '.env') {
        if ($line -match '^\s*(?:#|$)') { continue }
        if ($line -notmatch '^\s*([A-Za-z_][A-Za-z0-9_]*)=(.*)$') { throw 'Invalid entry in .env.' }
        [Environment]::SetEnvironmentVariable($Matches[1], $Matches[2], 'Process')
    }
    Invoke-Checked docker @('info', '--format', '{{.ServerVersion}}')
    $script:chosenPorts = [System.Collections.Generic.HashSet[int]]::new()
    $postgresPreferred = if ($env:POSTGRES_PORT) { [int]$env:POSTGRES_PORT } else { 5432 }
    $minioPreferred = if ($env:MINIO_PORT) { [int]$env:MINIO_PORT } else { 9000 }
    $consolePreferred = if ($env:MINIO_CONSOLE_PORT) { [int]$env:MINIO_CONSOLE_PORT } else { 9001 }
    $env:POSTGRES_PORT = Select-ServicePort 'postgres' 5432 $postgresPreferred
    $env:MINIO_PORT = Select-ServicePort 'minio' 9000 $minioPreferred
    $env:MINIO_CONSOLE_PORT = Select-ServicePort 'minio' 9001 $consolePreferred
    $apiPort = Find-FreePort 5080
    $frontendPort = Find-FreePort 5173
    $env:POSTGRES_HOST = '127.0.0.1'
    $env:MINIO_ENDPOINT = "http://127.0.0.1:$($env:MINIO_PORT)"
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:DOTNET_ENVIRONMENT = 'Development'
    $env:ASPNETCORE_URLS = "http://127.0.0.1:$apiPort"
    $env:FRONTEND_ORIGINS = "http://localhost:$frontendPort,http://127.0.0.1:$frontendPort"
    $env:VITE_API_BASE_URL = '/api'
    $env:VITE_API_PROXY_TARGET = "http://127.0.0.1:$apiPort"
    Write-Host "PostgreSQL: $env:POSTGRES_PORT | MinIO: $env:MINIO_PORT | MinIO console: $env:MINIO_CONSOLE_PORT | API: $apiPort | Frontend: $frontendPort"
    Invoke-Checked docker @('compose', 'up', '-d', '--wait', 'postgres', 'minio')
    Invoke-Checked dotnet @('tool', 'restore')
    Invoke-Checked dotnet @('tool', 'run', 'dotnet-ef', 'database', 'update', '--project', 'src/backend/Hadron.Erp.Api', '--startup-project', 'src/backend/Hadron.Erp.Api')
    if (-not (Test-Path 'src/frontend/node_modules/vite')) {
        Push-Location 'src/frontend'
        try { Invoke-Checked npm.cmd @('ci') } finally { Pop-Location }
    }
    Invoke-Checked dotnet @('build', 'src/backend/Hadron.Erp.Api')
    $apiDirectory = Join-Path (Get-Location) 'src/backend/Hadron.Erp.Api'
    $apiDll = Join-Path $apiDirectory 'bin/Debug/net10.0/Hadron.Erp.Api.dll'
    $api = Start-Process -FilePath 'dotnet' -ArgumentList @(('"{0}"' -f $apiDll)) -WorkingDirectory $apiDirectory -PassThru -NoNewWindow
    $frontend = Start-Process -FilePath 'node' -ArgumentList @('node_modules/vite/bin/vite.js', '--host', '127.0.0.1', '--port', "$frontendPort", '--strictPort') -WorkingDirectory (Join-Path (Get-Location) 'src/frontend') -PassThru -NoNewWindow
    Write-Host "Open http://localhost:$frontendPort (Ctrl+C to stop API and frontend). Docker containers remain running."
    while (-not $api.HasExited -and -not $frontend.HasExited) { Start-Sleep -Seconds 2; $api.Refresh(); $frontend.Refresh() }
    throw 'API or frontend exited; see output above.'
} catch {
    Write-Host "[ERROR] $($_.Exception.Message)" -ForegroundColor Red
    exit 1
} finally {
    foreach ($process in @($api, $frontend)) {
        if ($null -ne $process -and -not $process.HasExited) { Stop-Process -Id $process.Id -ErrorAction SilentlyContinue }
    }
}
