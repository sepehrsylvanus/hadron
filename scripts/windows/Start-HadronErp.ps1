# Interactive, loopback-only development launcher. This is not the LAN host supervisor.
# Supports Host mode (local .NET SDK + Node.js) and automatic Docker fallback when host
# SDKs are unavailable or incompatible with global.json.
[CmdletBinding()]
param(
    [ValidateSet('Auto', 'Host', 'Docker')]
    [string]$Mode = 'Auto',

    [switch]$Rebuild,

    [switch]$Detach,

    [switch]$NoBrowser
)

$ErrorActionPreference = 'Stop'
$script:repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
Set-Location -LiteralPath $script:repoRoot

function Invoke-Checked {
    param(
        [Parameter(Mandatory)][string]$Program,
        [string[]]$Arguments = @()
    )
    & $Program @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Program failed (exit code $LASTEXITCODE)."
    }
}

function Invoke-Quiet {
    param(
        [Parameter(Mandatory)][string]$Program,
        [string[]]$Arguments = @()
    )
    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & $Program @Arguments 2>$null
        $code = $LASTEXITCODE
    } catch {
        $output = @()
        $code = 1
    } finally {
        $ErrorActionPreference = $previousPreference
    }
    return [pscustomobject]@{
        ExitCode = $code
        Output   = @($output)
    }
}

function New-RandomHexSecret {
    param([int]$ByteLength = 24)
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $bytes = New-Object byte[] $ByteLength
        $rng.GetBytes($bytes)
        return ([BitConverter]::ToString($bytes).Replace('-', '').ToLowerInvariant())
    } finally {
        $rng.Dispose()
    }
}

function Ensure-EnvFile {
    $envPath = Join-Path $script:repoRoot '.env'
    if (Test-Path -LiteralPath $envPath) { return $envPath }

    $examplePath = Join-Path $script:repoRoot '.env.example'
    if (-not (Test-Path -LiteralPath $examplePath)) {
        throw 'Create .env from .env.example and configure the required secrets first.'
    }

    $generatedSecrets = @{
        'POSTGRES_PASSWORD'        = New-RandomHexSecret 16
        'MINIO_ACCESS_KEY'         = 'hadron' + (New-RandomHexSecret 8)
        'MINIO_SECRET_KEY'         = New-RandomHexSecret 20
        'LIVEKIT_API_KEY'          = 'hadronlk' + (New-RandomHexSecret 8)
        'LIVEKIT_API_SECRET'       = New-RandomHexSecret 24
        'IDENTITY_BOOTSTRAP_TOKEN' = New-RandomHexSecret 24
    }

    $lines = [Collections.Generic.List[string]]::new()
    foreach ($line in [IO.File]::ReadAllLines($examplePath, [Text.Encoding]::UTF8)) {
        if ($line -match '^([A-Za-z_][A-Za-z0-9_]*)=(.*)$' -and $generatedSecrets.ContainsKey($Matches[1])) {
            $lines.Add("$($Matches[1])=$($generatedSecrets[$Matches[1]])")
        } else {
            $lines.Add($line)
        }
    }

    $utf8NoBom = [Text.UTF8Encoding]::new($false)
    [IO.File]::WriteAllText($envPath, ($lines -join "`r`n") + "`r`n", $utf8NoBom)
    Write-Host '[INFO] Created local .env from .env.example with generated local-only secrets.' -ForegroundColor Yellow
    Write-Host '[INFO] For initial admin setup, read IDENTITY_BOOTSTRAP_TOKEN from .env.' -ForegroundColor Yellow
    return $envPath
}

function Import-EnvFile {
    param([Parameter(Mandatory)][string]$Path)
    $rawLines = [IO.File]::ReadAllLines($Path, [Text.Encoding]::UTF8)
    $lineNumber = 0
    foreach ($rawLine in $rawLines) {
        $lineNumber++
        $line = $rawLine
        if ($lineNumber -eq 1 -and $line.Length -gt 0 -and [int][char]$line[0] -eq 0xFEFF) {
            $line = $line.Substring(1)
        }
        $line = $line.TrimEnd("`r")
        if ($line -match '^\s*(?:#|$)') { continue }
        if ($line -notmatch '^\s*(?:export\s+)?([A-Za-z_][A-Za-z0-9_]*)=(.*)$') {
            throw "Invalid entry in .env at line $lineNumber."
        }
        $name = $Matches[1]
        $value = $Matches[2]
        if (($value.Length -ge 2) -and (
            ($value.StartsWith('"') -and $value.EndsWith('"')) -or
            ($value.StartsWith("'") -and $value.EndsWith("'"))
        )) {
            $value = $value.Substring(1, $value.Length - 2)
        }
        [Environment]::SetEnvironmentVariable($name, $value, 'Process')
    }

    foreach ($required in @('POSTGRES_DB', 'POSTGRES_USER', 'POSTGRES_PASSWORD', 'MINIO_ACCESS_KEY', 'MINIO_SECRET_KEY')) {
        if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($required, 'Process'))) {
            throw "Required setting is missing or empty in .env: $required"
        }
    }
}

function Update-ProcessPath {
    $currentParts = @($env:PATH -split ';' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    $machineParts = @([Environment]::GetEnvironmentVariable('Path', 'Machine') -split ';' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    $userParts = @([Environment]::GetEnvironmentVariable('Path', 'User') -split ';' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })

    $standardCandidates = @()
    if ($env:ProgramFiles) {
        $standardCandidates += (Join-Path $env:ProgramFiles 'dotnet')
        $standardCandidates += (Join-Path $env:ProgramFiles 'nodejs')
    }
    if (${env:ProgramFiles(x86)}) {
        $standardCandidates += (Join-Path ${env:ProgramFiles(x86)} 'dotnet')
    }
    if ($env:USERPROFILE) {
        $standardCandidates += (Join-Path $env:USERPROFILE '.dotnet')
    }
    if ($env:LOCALAPPDATA) {
        $standardCandidates += (Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet')
        $standardCandidates += (Join-Path $env:LOCALAPPDATA 'Programs\nodejs')
    }

    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $merged = [Collections.Generic.List[string]]::new()
    foreach ($entry in ($currentParts + $machineParts + $userParts)) {
        $trimmed = $entry.Trim().TrimEnd('\')
        if (-not [string]::IsNullOrWhiteSpace($trimmed) -and $seen.Add($trimmed)) {
            $merged.Add($entry.Trim())
        }
    }
    foreach ($candidate in $standardCandidates) {
        $trimmed = $candidate.Trim().TrimEnd('\')
        if ((Test-Path -LiteralPath $candidate) -and $seen.Add($trimmed)) {
            $merged.Add($candidate)
        }
    }
    $env:PATH = ($merged -join ';')
}

function Get-HostToolchain {
    Update-ProcessPath

    $dotnetCandidates = @(Get-Command 'dotnet' -All -ErrorAction SilentlyContinue | Where-Object { $_.CommandType -eq 'Application' })
    $dotnetPath = $null
    $dotnetVersion = $null
    $dotnetReason = $null
    if ($dotnetCandidates.Count -eq 0) {
        $dotnetReason = 'dotnet CLI was not found on this machine.'
    } else {
        foreach ($candidate in $dotnetCandidates) {
            $probe = Invoke-Quiet $candidate.Source @('--version')
            if ($probe.ExitCode -eq 0 -and $probe.Output.Count -gt 0) {
                $dotnetPath = $candidate.Source
                $dotnetVersion = ($probe.Output[0]).Trim()
                break
            }
        }
        if ($null -eq $dotnetPath) {
            $dotnetReason = 'Installed .NET SDK does not satisfy global.json (requires .NET SDK 10.0.302 patch band).'
        }
    }

    $nodeCmd = Get-Command 'node' -ErrorAction SilentlyContinue
    $npmCmd = Get-Command 'npm.cmd' -ErrorAction SilentlyContinue
    if ($null -eq $npmCmd) { $npmCmd = Get-Command 'npm' -ErrorAction SilentlyContinue }
    $nodePath = $null
    $npmPath = $null
    $nodeVersion = $null
    $nodeReason = $null
    if ($null -eq $nodeCmd -or $null -eq $npmCmd) {
        $nodeReason = 'Node.js / npm was not found on this machine.'
    } else {
        $nodeProbe = Invoke-Quiet $nodeCmd.Source @('-p', 'process.versions.node')
        if ($nodeProbe.ExitCode -eq 0 -and $nodeProbe.Output.Count -gt 0) {
            $nodeVersion = ($nodeProbe.Output[0]).Trim()
            $major = 0
            if ([int]::TryParse(($nodeVersion -split '\.')[0], [ref]$major) -and $major -ge 22) {
                $nodePath = $nodeCmd.Source
                $npmPath = $npmCmd.Source
            } else {
                $nodeReason = "Node.js $nodeVersion is too old for the frontend toolchain (requires Node.js 22+ / 26.x)."
            }
        } else {
            $nodeReason = 'Node.js failed to report its version.'
        }
    }

    return [pscustomobject]@{
        Ready         = ($null -ne $dotnetPath -and $null -ne $nodePath -and $null -ne $npmPath)
        DotnetPath    = $dotnetPath
        DotnetVersion = $dotnetVersion
        DotnetReason  = $dotnetReason
        NodePath      = $nodePath
        NpmPath       = $npmPath
        NodeVersion   = $nodeVersion
        NodeReason    = $nodeReason
    }
}

function Test-LoopbackPortFree {
    param([int]$Port)
    $v4 = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, $Port)
    try {
        $v4.ExclusiveAddressUse = $true
        $v4.Start()
    } catch {
        return $false
    } finally {
        $v4.Stop()
    }

    if ([Net.Sockets.Socket]::OSSupportsIPv6) {
        $v6 = [Net.Sockets.TcpListener]::new([Net.IPAddress]::IPv6Loopback, $Port)
        try {
            $v6.ExclusiveAddressUse = $true
            $v6.Start()
        } catch [Net.Sockets.SocketException] {
            if ($_.Exception.SocketErrorCode -in @([Net.Sockets.SocketError]::AddressAlreadyInUse, [Net.Sockets.SocketError]::AccessDenied)) {
                return $false
            }
        } catch {
            return $false
        } finally {
            $v6.Stop()
        }
    }
    return $true
}

function Find-FreePort {
    param([int]$Preferred)
    if ($Preferred -lt 1 -or $Preferred -gt 65535) { throw "Invalid port: $Preferred" }
    for ($port = $Preferred; $port -le 65535; $port++) {
        if ($script:chosenPorts.Contains($port)) { continue }
        if (Test-LoopbackPortFree $port) {
            [void]$script:chosenPorts.Add($port)
            return $port
        }
    }
    throw "No available TCP port at or above $Preferred."
}

function Get-ComposePort {
    param(
        [string]$Service,
        [int]$ContainerPort,
        [string[]]$Profiles = @()
    )
    $prefix = @('compose')
    foreach ($profile in $Profiles) {
        $prefix += @('--profile', $profile)
    }
    $psResult = Invoke-Quiet 'docker' ($prefix + @('ps', '-q', '--status', 'running', $Service))
    if ($psResult.ExitCode -ne 0 -or $psResult.Output.Count -eq 0 -or [string]::IsNullOrWhiteSpace($psResult.Output[0])) {
        return 0
    }
    $portResult = Invoke-Quiet 'docker' ($prefix + @('port', $Service, "$ContainerPort"))
    if ($portResult.ExitCode -eq 0 -and $portResult.Output.Count -gt 0 -and $portResult.Output[0].Trim() -match '^127\.0\.0\.1:(\d+)$') {
        return [int]$Matches[1]
    }
    return 0
}

function Select-ServicePort {
    param(
        [string]$Service,
        [int]$ContainerPort,
        [int]$Preferred,
        [string[]]$Profiles = @()
    )
    $existing = Get-ComposePort -Service $Service -ContainerPort $ContainerPort -Profiles $Profiles
    if ($existing -gt 0) {
        [void]$script:chosenPorts.Add($existing)
        return $existing
    }
    return Find-FreePort $Preferred
}

function Test-ComposeServiceHealthy {
    param(
        [string]$Service,
        [string[]]$Profiles = @()
    )
    $prefix = @('compose')
    foreach ($profile in $Profiles) {
        $prefix += @('--profile', $profile)
    }
    $psResult = Invoke-Quiet 'docker' ($prefix + @('ps', '-q', '--status', 'running', $Service))
    if ($psResult.ExitCode -ne 0 -or $psResult.Output.Count -eq 0 -or [string]::IsNullOrWhiteSpace($psResult.Output[0])) {
        return $false
    }
    $containerId = ($psResult.Output[0]).Trim()
    $inspect = Invoke-Quiet 'docker' @('inspect', '--format', '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}', $containerId)
    if ($inspect.ExitCode -ne 0 -or $inspect.Output.Count -eq 0) { return $false }
    $status = ($inspect.Output[0]).Trim()
    return ($status -eq 'healthy' -or $status -eq 'running')
}

function Test-DockerImageExists {
    param([Parameter(Mandatory)][string]$ImageName)
    $result = Invoke-Quiet 'docker' @('image', 'inspect', $ImageName)
    return ($result.ExitCode -eq 0)
}

function Get-InputFingerprint {
    param(
        [Parameter(Mandatory)][string[]]$Paths,
        [string[]]$ExtraValues = @()
    )
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $sb = [Text.StringBuilder]::new()
        $excludedDirs = @('bin', 'obj', 'node_modules', 'dist', 'coverage', 'TestResults', '.vite')
        $rootPrefix = $script:repoRoot.TrimEnd('\', '/')
        $files = [Collections.Generic.List[IO.FileInfo]]::new()
        foreach ($relPath in $Paths) {
            $full = Join-Path $script:repoRoot $relPath
            if (-not (Test-Path -LiteralPath $full)) { continue }
            $item = Get-Item -LiteralPath $full
            if ($item.PSIsContainer) {
                Get-ChildItem -LiteralPath $full -Recurse -File | ForEach-Object {
                    $rel = $_.FullName.Substring($rootPrefix.Length).TrimStart('\', '/')
                    $segments = $rel -split '[\\/]'
                    $skip = $false
                    for ($i = 0; $i -lt ($segments.Count - 1); $i++) {
                        if ($excludedDirs -contains $segments[$i]) { $skip = $true; break }
                    }
                    if (-not $skip) { $files.Add($_) }
                }
            } else {
                $files.Add($item)
            }
        }
        $sorted = @($files | Sort-Object FullName)
        foreach ($file in $sorted) {
            $rel = $file.FullName.Substring($rootPrefix.Length).TrimStart('\', '/').Replace('\', '/')
            $stream = [IO.File]::OpenRead($file.FullName)
            try {
                $hashBytes = $sha.ComputeHash($stream)
            } finally {
                $stream.Dispose()
            }
            $fileHash = [BitConverter]::ToString($hashBytes).Replace('-', '').ToLowerInvariant()
            [void]$sb.AppendLine("$rel|$fileHash")
        }
        foreach ($extra in $ExtraValues) {
            [void]$sb.AppendLine("extra|$extra")
        }
        $finalBytes = [Text.Encoding]::UTF8.GetBytes($sb.ToString())
        return ([BitConverter]::ToString($sha.ComputeHash($finalBytes)).Replace('-', '').ToLowerInvariant())
    } finally {
        $sha.Dispose()
    }
}

function Read-BuildState {
    $statePath = Join-Path $script:repoRoot '.docker\local-build-state.json'
    if (-not (Test-Path -LiteralPath $statePath)) { return @{} }
    try {
        $parsed = Get-Content -LiteralPath $statePath -Raw -Encoding UTF8 | ConvertFrom-Json
        $table = @{}
        if ($null -ne $parsed) {
            foreach ($prop in $parsed.PSObject.Properties) {
                $table[$prop.Name] = [string]$prop.Value
            }
        }
        return $table
    } catch {
        return @{}
    }
}

function Write-BuildState {
    param([hashtable]$State)
    $dir = Join-Path $script:repoRoot '.docker'
    if (-not (Test-Path -LiteralPath $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }
    $statePath = Join-Path $dir 'local-build-state.json'
    $json = $State | ConvertTo-Json -Depth 2
    $utf8NoBom = [Text.UTF8Encoding]::new($false)
    [IO.File]::WriteAllText($statePath, $json + "`r`n", $utf8NoBom)
}

function Test-HttpOk {
    param([Parameter(Mandatory)][string]$Url)
    try {
        $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 3
        return ([int]$response.StatusCode -eq 200)
    } catch {
        return $false
    }
}

function Wait-HostServicesReady {
    param(
        [Parameter(Mandatory)][Diagnostics.Process]$ApiProcess,
        [Parameter(Mandatory)][Diagnostics.Process]$FrontendProcess,
        [Parameter(Mandatory)][int]$ApiPort,
        [Parameter(Mandatory)][int]$FrontendPort,
        [int]$TimeoutSeconds = 90
    )
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        $ApiProcess.Refresh()
        $FrontendProcess.Refresh()
        if ($ApiProcess.HasExited) { throw "API exited during startup (exit code $($ApiProcess.ExitCode))." }
        if ($FrontendProcess.HasExited) { throw "Frontend exited during startup (exit code $($FrontendProcess.ExitCode))." }
        if ((Test-HttpOk "http://127.0.0.1:$ApiPort/health/ready") -and (Test-HttpOk "http://127.0.0.1:$FrontendPort/")) {
            return
        }
        Start-Sleep -Seconds 1
    }
    throw "API or frontend did not become ready within $TimeoutSeconds seconds."
}

function Open-AppInBrowser {
    param([Parameter(Mandatory)][string]$Url)
    if ($NoBrowser) { return }
    try {
        Start-Process $Url | Out-Null
    } catch {
        # Ignore when running in a headless shell without a browser handler.
    }
}

$api = $null
$frontend = $null
$effectiveMode = $Mode
$dockerAppStarted = $false
try {
    $envPath = Ensure-EnvFile
    Import-EnvFile -Path $envPath

    $dockerCheck = Invoke-Quiet 'docker' @('info', '--format', '{{.ServerVersion}}')
    if ($dockerCheck.ExitCode -ne 0) {
        throw 'Docker Desktop is not running or docker CLI is unavailable. Start Docker Desktop and rerun run-hadron.cmd.'
    }

    $toolchain = $null
    if ($Mode -ne 'Docker') {
        $toolchain = Get-HostToolchain
        if ($toolchain.Ready) {
            $effectiveMode = 'Host'
        } elseif ($Mode -eq 'Host') {
            $reasons = @($toolchain.DotnetReason, $toolchain.NodeReason) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
            throw ('Host mode prerequisites are missing: ' + ($reasons -join ' ') + ' Install .NET SDK 10.0.302 and Node.js 26.x, or run without -Mode Host to launch automatically in Docker.')
        } else {
            $reasons = @($toolchain.DotnetReason, $toolchain.NodeReason) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
            Write-Host ('[INFO] ' + ($reasons -join ' ')) -ForegroundColor Yellow
            Write-Host '[INFO] Switching automatically to Docker mode (no host .NET SDK or Node.js required).' -ForegroundColor Cyan
            $effectiveMode = 'Docker'
        }
    }

    if ($effectiveMode -eq 'Host') {
        if ((Test-ComposeServiceHealthy 'api' @('app')) -or (Test-ComposeServiceHealthy 'web' @('app'))) {
            Write-Host '[INFO] Stopping existing Docker app containers before starting in Host mode...' -ForegroundColor Cyan
            Invoke-Quiet 'docker' @('compose', '--profile', 'app', 'stop', 'api', 'web') | Out-Null
        }
    }

    $script:chosenPorts = [System.Collections.Generic.HashSet[int]]::new()
    $postgresPreferred = if ($env:POSTGRES_PORT) { [int]$env:POSTGRES_PORT } else { 5432 }
    $minioPreferred = if ($env:MINIO_PORT) { [int]$env:MINIO_PORT } else { 9000 }
    $consolePreferred = if ($env:MINIO_CONSOLE_PORT) { [int]$env:MINIO_CONSOLE_PORT } else { 9001 }
    $env:POSTGRES_PORT = "$(Select-ServicePort -Service 'postgres' -ContainerPort 5432 -Preferred $postgresPreferred)"
    $env:MINIO_PORT = "$(Select-ServicePort -Service 'minio' -ContainerPort 9000 -Preferred $minioPreferred)"
    $env:MINIO_CONSOLE_PORT = "$(Select-ServicePort -Service 'minio' -ContainerPort 9001 -Preferred $consolePreferred)"

    if ($effectiveMode -eq 'Docker') {
        $apiPort = Select-ServicePort -Service 'api' -ContainerPort 8080 -Preferred 5080 -Profiles @('app')
        $frontendPort = Select-ServicePort -Service 'web' -ContainerPort 8080 -Preferred 5173 -Profiles @('app')
        $env:API_PORT = "$apiPort"
        $env:FRONTEND_PORT = "$frontendPort"
    } else {
        $apiPort = Find-FreePort 5080
        $frontendPort = Find-FreePort 5173
    }

    Write-Host "Mode: $effectiveMode | PostgreSQL: $env:POSTGRES_PORT | MinIO: $env:MINIO_PORT | MinIO console: $env:MINIO_CONSOLE_PORT | API: $apiPort | Frontend: $frontendPort" -ForegroundColor Cyan

    $buildState = Read-BuildState
    $minioHash = Get-InputFingerprint -Paths @('deploy/minio')
    $postgresHealthy = Test-ComposeServiceHealthy 'postgres'
    $minioHealthy = Test-ComposeServiceHealthy 'minio'
    $minioImagePresent = Test-DockerImageExists 'hadron-minio:RELEASE.2025-09-07T16-13-09Z'
    $minioHashChanged = $buildState.ContainsKey('minioHash') -and ($buildState['minioHash'] -ne $minioHash)
    $needMinioBuild = $Rebuild -or (-not $minioImagePresent) -or $minioHashChanged

    if (-not ($postgresHealthy -and $minioHealthy) -or $needMinioBuild) {
        $storageArgs = @('compose', 'up', '-d', '--wait')
        if (-not $needMinioBuild) {
            $storageArgs += '--no-build'
        }
        $storageArgs += @('postgres', 'minio')
        Invoke-Checked 'docker' $storageArgs
        $buildState['minioHash'] = $minioHash
        Write-BuildState $buildState
    } else {
        if (-not $buildState.ContainsKey('minioHash')) {
            $buildState['minioHash'] = $minioHash
            Write-BuildState $buildState
        }
        Write-Host '[INFO] PostgreSQL and MinIO are already running and healthy.' -ForegroundColor Green
    }

    if ($effectiveMode -eq 'Docker') {
        $backendHash = Get-InputFingerprint -Paths @(
            'Directory.Build.props',
            'Hadron.Erp.slnx',
            '.config/dotnet-tools.json',
            'src/backend/Hadron.Erp.Api'
        )
        $frontendHash = Get-InputFingerprint -Paths @(
            'src/frontend/Dockerfile',
            'src/frontend/package.json',
            'src/frontend/package-lock.json',
            'src/frontend/index.html',
            'src/frontend/tsconfig.json',
            'src/frontend/tsconfig.app.json',
            'src/frontend/tsconfig.node.json',
            'src/frontend/vite.config.ts',
            'src/frontend/src',
            'deploy/nginx/frontend.conf'
        ) -ExtraValues @("VITE_PUBLIC_APP_ORIGIN=$($env:VITE_PUBLIC_APP_ORIGIN)")

        $servicesToBuild = [Collections.Generic.List[string]]::new()
        $needMigrateBuild = $Rebuild -or ($buildState['backendHash'] -ne $backendHash) -or (-not (Test-DockerImageExists 'hadron-erp-api-migrations:local'))
        $needApiBuild = $Rebuild -or ($buildState['backendHash'] -ne $backendHash) -or (-not (Test-DockerImageExists 'hadron-erp-api:local'))
        $needWebBuild = $Rebuild -or ($buildState['frontendHash'] -ne $frontendHash) -or (-not (Test-DockerImageExists 'hadron-erp-web:local'))

        if ($needMigrateBuild) { $servicesToBuild.Add('migrate') }
        if ($needApiBuild) { $servicesToBuild.Add('api') }
        if ($needWebBuild) { $servicesToBuild.Add('web') }

        if ($servicesToBuild.Count -gt 0) {
            Write-Host ('[INFO] Building Docker image(s): ' + ($servicesToBuild -join ', ') + ' (first build may take a few minutes; subsequent runs reuse cache)...') -ForegroundColor Cyan
            $buildArgs = @('compose', '--profile', 'migrate', '--profile', 'app', 'build') + $servicesToBuild.ToArray()
            Invoke-Checked 'docker' $buildArgs
            $buildState['backendHash'] = $backendHash
            $buildState['frontendHash'] = $frontendHash
            Write-BuildState $buildState
        } else {
            Write-Host '[INFO] Application Docker images are up to date (skipping rebuild).' -ForegroundColor Green
        }

        Write-Host '[INFO] Applying database migrations...' -ForegroundColor Cyan
        Invoke-Checked 'docker' @('compose', '--profile', 'migrate', 'run', '--rm', '-T', '--no-deps', 'migrate')

        $dockerAppStarted = $true
        Write-Host '[INFO] Starting API container...' -ForegroundColor Cyan
        Invoke-Checked 'docker' @('compose', '--profile', 'app', 'up', '-d', '--wait', '--no-deps', '--no-build', 'api')

        Write-Host '[INFO] Starting frontend container...' -ForegroundColor Cyan
        Invoke-Checked 'docker' @('compose', '--profile', 'app', 'up', '-d', '--wait', '--no-deps', '--no-build', 'web')

        $appUrl = "http://localhost:$frontendPort"
        Write-Host ''
        Write-Host 'Hadron ERP is ready!' -ForegroundColor Green
        Write-Host "  App:           $appUrl" -ForegroundColor Green
        Write-Host "  API:           http://127.0.0.1:$apiPort" -ForegroundColor Green
        Write-Host "  MinIO console: http://127.0.0.1:$($env:MINIO_CONSOLE_PORT)" -ForegroundColor Green
        Open-AppInBrowser -Url $appUrl

        if ($Detach) {
            Write-Host 'Detached mode: containers remain running. Stop app containers with: docker compose --profile app stop api web' -ForegroundColor Cyan
            return
        }

        Write-Host 'Press Ctrl+C to stop API and frontend (PostgreSQL and MinIO remain running).' -ForegroundColor Cyan
        while ($true) {
            Start-Sleep -Seconds 3
            if (-not (Test-ComposeServiceHealthy 'api' @('app'))) {
                throw 'API container stopped or became unhealthy.'
            }
            if (-not (Test-ComposeServiceHealthy 'web' @('app'))) {
                throw 'Frontend container stopped or became unhealthy.'
            }
        }
    } else {
        $env:POSTGRES_HOST = '127.0.0.1'
        $env:MINIO_ENDPOINT = "http://127.0.0.1:$($env:MINIO_PORT)"
        $env:ASPNETCORE_ENVIRONMENT = 'Development'
        $env:DOTNET_ENVIRONMENT = 'Development'
        $env:ASPNETCORE_URLS = "http://127.0.0.1:$apiPort"
        $env:FRONTEND_ORIGINS = "http://localhost:$frontendPort,http://127.0.0.1:$frontendPort"
        $env:VITE_API_BASE_URL = '/api'
        $env:VITE_API_PROXY_TARGET = "http://127.0.0.1:$apiPort"

        Write-Host "[INFO] Using host .NET SDK $($toolchain.DotnetVersion) and Node.js $($toolchain.NodeVersion)." -ForegroundColor Cyan
        Invoke-Checked $toolchain.DotnetPath @('build', 'src/backend/Hadron.Erp.Api', '--nologo')

        $efProbe = Invoke-Quiet $toolchain.DotnetPath @('tool', 'run', 'dotnet-ef', '--version')
        if ($efProbe.ExitCode -ne 0) {
            Invoke-Checked $toolchain.DotnetPath @('tool', 'restore')
        }
        Invoke-Checked $toolchain.DotnetPath @('tool', 'run', 'dotnet-ef', 'database', 'update', '--no-build', '--project', 'src/backend/Hadron.Erp.Api', '--startup-project', 'src/backend/Hadron.Erp.Api')

        $frontendDirectory = Join-Path $script:repoRoot 'src\frontend'
        $viteBin = Join-Path $frontendDirectory 'node_modules\vite\bin\vite.js'
        $lockFile = Join-Path $frontendDirectory 'package-lock.json'
        $installedLockFile = Join-Path $frontendDirectory 'node_modules\.package-lock.json'
        $needNpmCi = (-not (Test-Path -LiteralPath $viteBin)) -or
            (-not (Test-Path -LiteralPath $installedLockFile)) -or
            ((Get-Item -LiteralPath $lockFile).LastWriteTimeUtc -gt (Get-Item -LiteralPath $installedLockFile).LastWriteTimeUtc)
        if ($needNpmCi) {
            Push-Location -LiteralPath $frontendDirectory
            try { Invoke-Checked $toolchain.NpmPath @('ci') } finally { Pop-Location }
        }

        $apiDirectory = Join-Path $script:repoRoot 'src\backend\Hadron.Erp.Api'
        $apiDll = Join-Path $apiDirectory 'bin\Debug\net10.0\Hadron.Erp.Api.dll'
        $api = Start-Process -FilePath $toolchain.DotnetPath -ArgumentList @(('"{0}"' -f $apiDll)) -WorkingDirectory $apiDirectory -PassThru -NoNewWindow
        $frontend = Start-Process -FilePath $toolchain.NodePath -ArgumentList @('node_modules/vite/bin/vite.js', '--host', '127.0.0.1', '--port', "$frontendPort", '--strictPort') -WorkingDirectory $frontendDirectory -PassThru -NoNewWindow

        Write-Host '[INFO] Waiting for API and frontend readiness...' -ForegroundColor Cyan
        Wait-HostServicesReady -ApiProcess $api -FrontendProcess $frontend -ApiPort $apiPort -FrontendPort $frontendPort

        $appUrl = "http://localhost:$frontendPort"
        Write-Host ''
        Write-Host 'Hadron ERP is ready!' -ForegroundColor Green
        Write-Host "  App:           $appUrl" -ForegroundColor Green
        Write-Host "  API:           http://127.0.0.1:$apiPort" -ForegroundColor Green
        Write-Host "  MinIO console: http://127.0.0.1:$($env:MINIO_CONSOLE_PORT)" -ForegroundColor Green
        Open-AppInBrowser -Url $appUrl
        Write-Host 'Press Ctrl+C to stop API and frontend (PostgreSQL and MinIO remain running).' -ForegroundColor Cyan

        while (-not $api.HasExited -and -not $frontend.HasExited) {
            Start-Sleep -Seconds 2
            $api.Refresh()
            $frontend.Refresh()
        }
        throw 'API or frontend exited; see output above.'
    }
} catch {
    if ($effectiveMode -eq 'Docker' -and $dockerAppStarted) {
        Write-Host '[INFO] Recent container logs:' -ForegroundColor Yellow
        $previousPref = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        try {
            & docker compose --profile app logs --tail=40 api web
        } finally {
            $ErrorActionPreference = $previousPref
        }
    }
    Write-Host "[ERROR] $($_.Exception.Message)" -ForegroundColor Red
    exit 1
} finally {
    if ($effectiveMode -eq 'Docker' -and $dockerAppStarted -and -not $Detach) {
        Invoke-Quiet 'docker' @('compose', '--profile', 'app', 'stop', 'api', 'web') | Out-Null
    }
    foreach ($process in @($api, $frontend)) {
        if ($null -ne $process -and -not $process.HasExited) {
            Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        }
    }
}
