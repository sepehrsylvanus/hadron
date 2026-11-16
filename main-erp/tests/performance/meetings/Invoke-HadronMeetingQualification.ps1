[CmdletBinding()]
param(
    [ValidateSet('ControlPlane', 'MediaSmoke', 'Local150', 'All')]
    [string]$Profile = 'ControlPlane',

    [ValidateRange(1, 10)]
    [int]$Loops = 3,

    [ValidatePattern('^[0-9]+(ms|s|m|h)$')]
    [string]$MediaDuration = '45s',

    [string]$LiveKitCliPath,

    [string]$ArtifactsRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $scriptDirectory '..\..\..'))
$frontendDirectory = Join-Path $repositoryRoot 'src\frontend'
$backendTestProject = Join-Path $repositoryRoot 'tests\backend\Hadron.Erp.IntegrationTests\Hadron.Erp.IntegrationTests.csproj'
$thresholdsPath = Join-Path $scriptDirectory 'thresholds.json'

if ([string]::IsNullOrWhiteSpace($ArtifactsRoot)) {
    $ArtifactsRoot = Join-Path $repositoryRoot 'artifacts\performance\meetings'
}

$runId = '{0}-{1}' -f ([DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ')),
    ([Guid]::NewGuid().ToString('N').Substring(0, 8))
$requestedRunDirectory = Join-Path $ArtifactsRoot $runId
$runDirectory = $requestedRunDirectory
$results = [System.Collections.Generic.List[object]]::new()
$harnessFailures = [System.Collections.Generic.List[object]]::new()
$resourceSamples = [System.Collections.Generic.List[object]]::new()
$mediaInvocations = [System.Collections.Generic.List[object]]::new()
$requiredQoeMetrics = [System.Collections.Generic.List[object]]::new()
$configurationEnvironment = @{}
$knownSecretValues = [System.Collections.Generic.List[string]]::new()
$evidenceRedactionCount = 0
$thresholds = $null
$thresholdSnapshotEvidence = $null
$hostSnapshot = $null
$gitEvidence = $null
$sourceEvidence = @()
$toolEvidence = [ordered]@{}
$dockerExecutable = $null
$mainCompleted = $false

try {
    [System.IO.Directory]::CreateDirectory($runDirectory) | Out-Null
}
catch {
    $bootstrapMessage = $_.Exception.Message
    $runDirectory = Join-Path ([System.IO.Path]::GetTempPath()) (Join-Path 'hadron-meeting-qualification' $runId)
    [System.IO.Directory]::CreateDirectory($runDirectory) | Out-Null
    $harnessFailures.Add([pscustomobject]@{
        stage = 'EvidenceDirectory'
        type = $_.Exception.GetType().FullName
        message = $bootstrapMessage
        requestedDirectory = $requestedRunDirectory
        fallbackDirectoryUsed = $true
    })
}

function Get-ExecutablePath {
    param([Parameter(Mandatory)][string]$Name)

    $command = Get-Command $Name -ErrorAction SilentlyContinue
    if ($null -eq $command) {
        throw "Required executable '$Name' was not found."
    }
    return $command.Source
}

function Get-HadronEnvironment {
    $result = @{}
    $allowedNames = @(
        'LIVEKIT_URL', 'LIVEKIT_API_KEY', 'LIVEKIT_API_SECRET',
        'POSTGRES_HOST', 'POSTGRES_PORT', 'POSTGRES_USER', 'POSTGRES_PASSWORD', 'POSTGRES_DB'
    )
    foreach ($name in $allowedNames) {
        $value = [Environment]::GetEnvironmentVariable($name)
        if (-not [string]::IsNullOrWhiteSpace($value)) { $result[$name] = $value }
    }

    $environmentPath = Join-Path $repositoryRoot '.env'
    if (Test-Path -LiteralPath $environmentPath) {
        foreach ($line in Get-Content -LiteralPath $environmentPath) {
            if ($line -notmatch '^\s*(LIVEKIT_URL|LIVEKIT_API_KEY|LIVEKIT_API_SECRET|POSTGRES_HOST|POSTGRES_PORT|POSTGRES_USER|POSTGRES_PASSWORD|POSTGRES_DB)\s*=\s*(.*)\s*$') {
                continue
            }
            $name = $Matches[1]
            if ($result.ContainsKey($name)) { continue }
            $value = $Matches[2].Trim()
            if (($value.StartsWith('"') -and $value.EndsWith('"')) -or
                ($value.StartsWith("'") -and $value.EndsWith("'"))) {
                $value = $value.Substring(1, $value.Length - 2)
            }
            if (-not [string]::IsNullOrWhiteSpace($value)) { $result[$name] = $value }
        }
    }
    return $result
}

function Select-HadronEnvironment {
    param([Parameter(Mandatory)][string[]]$Names)

    $selected = @{}
    foreach ($name in $Names) {
        if ($configurationEnvironment.ContainsKey($name)) {
            $selected[$name] = $configurationEnvironment[$name]
        }
    }
    return $selected
}

function Test-OpaqueTokenCandidate {
    param([AllowEmptyString()][string]$Candidate)

    if ($Candidate.Length -lt 43 -or $Candidate.Length -gt 512) { return $false }
    if ($Candidate -match '^[A-Fa-f0-9]{40,128}$') { return $false }
    if ($Candidate -notmatch '[a-z]' -or $Candidate -notmatch '[A-Z]' -or $Candidate -notmatch '[0-9]') {
        return $false
    }
    $entropy = 0.0
    foreach ($group in ($Candidate.ToCharArray() | Group-Object)) {
        $probability = [double]$group.Count / $Candidate.Length
        $entropy -= $probability * [Math]::Log($probability, 2)
    }
    # Long source identifiers and migration names can resemble base64url lexically;
    # high Shannon entropy keeps the generic fallback focused on opaque credentials.
    return $entropy -ge 4.8
}

function Protect-EvidenceText {
    param([AllowEmptyString()][string]$Value)

    $safe = if ($null -eq $Value) { '' } else { $Value }
    foreach ($secret in $knownSecretValues) {
        if (-not [string]::IsNullOrWhiteSpace($secret) -and $safe.Contains($secret)) {
            $safe = $safe.Replace($secret, '[REDACTED_SECRET]')
            $script:evidenceRedactionCount++
        }
    }

    $jwtPattern = 'eyJ[A-Za-z0-9_-]{12,}\.[A-Za-z0-9_-]{12,}\.[A-Za-z0-9_-]{12,}'
    if ([regex]::IsMatch($safe, $jwtPattern)) {
        $safe = [regex]::Replace($safe, $jwtPattern, '[REDACTED_JWT]')
        $script:evidenceRedactionCount++
    }

    $labelledTokenPattern = '(?i)(?<![A-Za-z0-9_])(inviteToken|accessTicket|joinToken|mediaToken|token)["'']?\s*[:=]\s*["'']?(?<token>[A-Za-z0-9_-]{24,512})'
    $matches = [regex]::Matches($safe, $labelledTokenPattern)
    for ($index = $matches.Count - 1; $index -ge 0; $index--) {
        $tokenGroup = $matches[$index].Groups['token']
        $safe = $safe.Remove($tokenGroup.Index, $tokenGroup.Length).Insert($tokenGroup.Index, '[REDACTED_TOKEN]')
        $script:evidenceRedactionCount++
    }

    $opaqueMatches = [regex]::Matches($safe, '(?<![A-Za-z0-9_-])[A-Za-z0-9_-]{43,512}(?![A-Za-z0-9_-])')
    for ($index = $opaqueMatches.Count - 1; $index -ge 0; $index--) {
        $candidate = $opaqueMatches[$index].Value
        if (-not (Test-OpaqueTokenCandidate $candidate)) { continue }
        $matchIndex = $opaqueMatches[$index].Index
        $safe = $safe.Remove($matchIndex, $opaqueMatches[$index].Length)
        $safe = $safe.Insert($matchIndex, '[REDACTED_OPAQUE_TOKEN]')
        $script:evidenceRedactionCount++
    }
    return $safe
}

function Write-SafeText {
    param([Parameter(Mandatory)][string]$Path, [AllowNull()][AllowEmptyString()][string]$Value)

    [System.IO.File]::WriteAllText($Path, (Protect-EvidenceText $Value),
        [System.Text.UTF8Encoding]::new($false))
}

function Add-ComponentResult {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][ValidateSet('Passed', 'Failed', 'Blocked')][string]$Status,
        [Nullable[int]]$ExitCode = $null,
        [double]$DurationMilliseconds = 0,
        [string]$Log,
        [ValidateSet('Complete', 'Partial')][string]$Coverage = 'Complete',
        [string]$Detail
    )

    $entry = [pscustomobject]@{
        name = $Name
        status = $Status
        coverage = $Coverage
        exitCode = $ExitCode
        durationMilliseconds = [Math]::Round($DurationMilliseconds, 1)
        log = $Log
        detail = if ([string]::IsNullOrWhiteSpace($Detail)) { $null } else { Protect-EvidenceText $Detail }
    }
    $results.Add($entry)
    return $entry
}

function New-HadronProcessStartInfo {
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [string[]]$ArgumentList = @(),
        [Parameter(Mandatory)][string]$WorkingDirectory,
        [hashtable]$Environment = @{}
    )

    $start = [System.Diagnostics.ProcessStartInfo]::new()
    $start.FileName = $FilePath
    $start.WorkingDirectory = $WorkingDirectory
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.CreateNoWindow = $true
    foreach ($argument in $ArgumentList) { $start.ArgumentList.Add($argument) }
    foreach ($name in @(
        'LIVEKIT_URL', 'LIVEKIT_API_KEY', 'LIVEKIT_API_SECRET',
        'POSTGRES_HOST', 'POSTGRES_PORT', 'POSTGRES_USER', 'POSTGRES_PASSWORD', 'POSTGRES_DB')) {
        $start.Environment.Remove($name) | Out-Null
    }
    foreach ($entry in $Environment.GetEnumerator()) {
        $start.Environment[[string]$entry.Key] = [string]$entry.Value
    }
    return $start
}

function Stop-HadronProcess {
    param([Parameter(Mandatory)][System.Diagnostics.Process]$Process)

    try {
        if (-not $Process.HasExited) { $Process.Kill($true) }
        $Process.WaitForExit()
    }
    catch { }
}

function Invoke-TextProcess {
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [string[]]$ArgumentList = @(),
        [Parameter(Mandatory)][string]$WorkingDirectory,
        [hashtable]$Environment = @{},
        [int]$TimeoutSeconds = 30
    )

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = New-HadronProcessStartInfo -FilePath $FilePath -ArgumentList $ArgumentList `
        -WorkingDirectory $WorkingDirectory -Environment $Environment
    if (-not $process.Start()) { throw "Unable to start '$FilePath'." }
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        Stop-HadronProcess $process
        throw "Process '$([System.IO.Path]::GetFileName($FilePath))' exceeded ${TimeoutSeconds}s."
    }
    $stdout = $stdoutTask.GetAwaiter().GetResult()
    $stderr = $stderrTask.GetAwaiter().GetResult()
    return [pscustomobject]@{
        exitCode = $process.ExitCode
        output = Protect-EvidenceText (($stdout, $stderr | Where-Object {
                    -not [string]::IsNullOrWhiteSpace($_) }) -join [Environment]::NewLine)
    }
}

function Invoke-RecordedProcess {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$FilePath,
        [string[]]$ArgumentList = @(),
        [Parameter(Mandatory)][string]$WorkingDirectory,
        [hashtable]$Environment = @{},
        [Parameter(Mandatory)][int]$TimeoutSeconds
    )

    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = New-HadronProcessStartInfo -FilePath $FilePath -ArgumentList $ArgumentList `
        -WorkingDirectory $WorkingDirectory -Environment $Environment
    if (-not $process.Start()) { throw "Unable to start '$Name'." }
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $timedOut = -not $process.WaitForExit($TimeoutSeconds * 1000)
    if ($timedOut) { Stop-HadronProcess $process }
    $stdout = $stdoutTask.GetAwaiter().GetResult()
    $stderr = $stderrTask.GetAwaiter().GetResult()
    $stopwatch.Stop()

    $combined = (($stdout, $stderr | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) `
            -join [Environment]::NewLine)
    if ($timedOut) {
        $combined += "`nHARNESS: process exceeded the hard timeout of ${TimeoutSeconds}s and was terminated."
    }
    $logPath = Join-Path $runDirectory ($Name + '.log')
    Write-SafeText -Path $logPath -Value $combined
    $status = if (-not $timedOut -and $process.ExitCode -eq 0) { 'Passed' } else { 'Failed' }
    return Add-ComponentResult -Name $Name -Status $status `
        -ExitCode $(if ($timedOut) { $null } else { $process.ExitCode }) `
        -DurationMilliseconds $stopwatch.Elapsed.TotalMilliseconds `
        -Log ([System.IO.Path]::GetRelativePath($repositoryRoot, $logPath)) `
        -Detail $(if ($timedOut) { "Hard timeout after ${TimeoutSeconds}s." } else { $null })
}

function Add-RecordedBlock {
    param([Parameter(Mandatory)][string]$Name, [Parameter(Mandatory)][string]$Reason)

    $logPath = Join-Path $runDirectory ($Name + '.log')
    Write-SafeText -Path $logPath -Value $Reason
    Add-ComponentResult -Name $Name -Status 'Blocked' `
        -Log ([System.IO.Path]::GetRelativePath($repositoryRoot, $logPath)) -Detail $Reason | Out-Null
}

function Get-MemorySnapshot {
    try {
        $operatingSystem = Get-CimInstance Win32_OperatingSystem -ErrorAction Stop
        $computerSystem = Get-CimInstance Win32_ComputerSystem -ErrorAction Stop
        return [pscustomobject]@{
            totalGigabytes = [Math]::Round(($computerSystem.TotalPhysicalMemory / 1GB), 2)
            freeGigabytes = [Math]::Round(($operatingSystem.FreePhysicalMemory * 1KB / 1GB), 2)
            source = 'WindowsCim'
        }
    }
    catch {
        try {
            Add-Type -AssemblyName Microsoft.VisualBasic
            $computerInfo = [Microsoft.VisualBasic.Devices.ComputerInfo]::new()
            return [pscustomobject]@{
                totalGigabytes = [Math]::Round(($computerInfo.TotalPhysicalMemory / 1GB), 2)
                freeGigabytes = [Math]::Round(($computerInfo.AvailablePhysicalMemory / 1GB), 2)
                source = 'MicrosoftVisualBasicComputerInfo'
            }
        }
        catch {
            return [pscustomobject]@{ totalGigabytes = $null; freeGigabytes = $null; source = 'Unavailable' }
        }
    }
}

function Get-HostSnapshot {
    $memory = Get-MemorySnapshot
    $drive = [System.IO.DriveInfo]::new([System.IO.Path]::GetPathRoot($repositoryRoot))
    return [pscustomobject]@{
        machineName = [Environment]::MachineName
        operatingSystem = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
        logicalProcessors = [Environment]::ProcessorCount
        totalMemoryGigabytes = $memory.totalGigabytes
        freeMemoryGigabytes = $memory.freeGigabytes
        memoryMeasurementSource = $memory.source
        freeDiskGigabytes = [Math]::Round(($drive.AvailableFreeSpace / 1GB), 2)
    }
}

function Get-SystemCpuPercent {
    try {
        $cpu = Get-CimInstance Win32_PerfFormattedData_PerfOS_Processor -Filter "Name='_Total'" `
            -ErrorAction Stop
        if ($null -eq $cpu) { return $null }
        return [double]$cpu.PercentProcessorTime
    }
    catch {
        try {
            $sample = Get-Counter '\Processor(_Total)\% Processor Time' -MaxSamples 1 -ErrorAction Stop
            return [Math]::Round([double]$sample.CounterSamples[0].CookedValue, 2)
        }
        catch { return $null }
    }
}

function Convert-MemoryTextToGigabytes {
    param([Parameter(Mandatory)][string]$Value)

    if ($Value -notmatch '(?<number>[0-9]+(?:\.[0-9]+)?)\s*(?<unit>KiB|MiB|GiB|TiB|KB|MB|GB|TB|B)') {
        return $null
    }
    $number = [double]$Matches['number']
    switch ($Matches['unit'].ToUpperInvariant()) {
        'B' { return $number / 1GB }
        'KIB' { return $number / 1MB }
        'KB' { return ($number * 1000) / 1GB }
        'MIB' { return $number / 1024 }
        'MB' { return ($number * 1000 * 1000) / 1GB }
        'GIB' { return $number }
        'GB' { return ($number * 1000 * 1000 * 1000) / 1GB }
        'TIB' { return $number * 1024 }
        'TB' { return ($number * 1000 * 1000 * 1000 * 1000) / 1GB }
    }
}

function Get-DockerMemoryGigabytes {
    if ([string]::IsNullOrWhiteSpace($dockerExecutable)) { return $null }
    try {
        $capture = Invoke-TextProcess -FilePath $dockerExecutable `
            -ArgumentList @('stats', '--no-stream', '--format', '{{.MemUsage}}') `
            -WorkingDirectory $repositoryRoot -TimeoutSeconds 15
        if ($capture.exitCode -ne 0) { return $null }
        $sum = 0.0
        $measured = $false
        foreach ($line in ($capture.output -split '\r?\n')) {
            if ([string]::IsNullOrWhiteSpace($line)) { continue }
            $used = ($line -split '/')[0].Trim()
            $value = Convert-MemoryTextToGigabytes $used
            if ($null -ne $value) { $sum += [double]$value; $measured = $true }
        }
        if ($measured) { return [Math]::Round($sum, 3) }
        return $null
    }
    catch { return $null }
}

function Get-ResourceSample {
    param([bool]$IncludeDocker)

    $memory = Get-MemorySnapshot
    return [pscustomobject]@{
        observedAtUtc = [DateTime]::UtcNow.ToString('O')
        cpuPercent = Get-SystemCpuPercent
        freeMemoryGigabytes = $memory.freeGigabytes
        memoryMeasurementSource = $memory.source
        dockerMemoryGigabytes = if ($IncludeDocker) { Get-DockerMemoryGigabytes } else { $null }
    }
}

function Test-MediaPreflight {
    param(
        [Parameter(Mandatory)][ValidateSet('MediaSmoke', 'Local150')][string]$MediaProfile,
        [Parameter(Mandatory)]$Snapshot
    )

    $reasons = [System.Collections.Generic.List[string]]::new()
    $minimumMemory = if ($MediaProfile -eq 'Local150') {
        [double]$thresholds.hostAbort.minimumFreeMemoryGigabytesLocal150
    } else { [double]$thresholds.hostAbort.minimumFreeMemoryGigabytesMediaSmoke }
    if ($null -eq $Snapshot.freeMemoryGigabytes) {
        $reasons.Add('Free-memory measurement is unavailable; media load is blocked fail-closed.')
    } elseif ($Snapshot.freeMemoryGigabytes -lt $minimumMemory) {
        $reasons.Add("Free memory is $($Snapshot.freeMemoryGigabytes) GB; $MediaProfile requires at least $minimumMemory GB.")
    }
    if ($Snapshot.freeDiskGigabytes -lt [double]$thresholds.hostAbort.minimumFreeDiskGigabytes) {
        $reasons.Add("Free disk is $($Snapshot.freeDiskGigabytes) GB; at least $($thresholds.hostAbort.minimumFreeDiskGigabytes) GB is required.")
    }
    $initialCpu = Get-SystemCpuPercent
    if ($null -eq $initialCpu) {
        $reasons.Add('CPU measurement is unavailable; media load is blocked fail-closed.')
    } elseif ($initialCpu -ge [double]$thresholds.hostAbort.maximumBurstCpuPercent) {
        $reasons.Add("System CPU is already at or above the $($thresholds.hostAbort.maximumBurstCpuPercent) percent burst limit.")
    }
    $initialDockerMemory = Get-DockerMemoryGigabytes
    if ($MediaProfile -eq 'Local150' -and $null -eq $initialDockerMemory) {
        $reasons.Add('Docker memory measurement is unavailable; Local150 is blocked fail-closed.')
    } elseif ($null -ne $initialDockerMemory -and
        $initialDockerMemory -gt [double]$thresholds.hostAbort.maximumDockerMemoryGigabytes) {
        $reasons.Add("Docker memory already exceeds $($thresholds.hostAbort.maximumDockerMemoryGigabytes) GB.")
    }
    foreach ($requiredName in @('LIVEKIT_URL', 'LIVEKIT_API_KEY', 'LIVEKIT_API_SECRET')) {
        if (-not $configurationEnvironment.ContainsKey($requiredName)) {
            $reasons.Add("Required process environment '$requiredName' is not available.")
        }
    }
    return $reasons
}

function Get-FileEvidence {
    param([Parameter(Mandatory)][string]$Path)

    $item = Get-Item -LiteralPath $Path
    return [pscustomobject]@{
        path = [System.IO.Path]::GetRelativePath($repositoryRoot, $item.FullName)
        length = $item.Length
        sha256 = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}

function Get-RelevantSourceEvidence {
    $paths = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($relative in @(
        'tests\performance\meetings\Invoke-HadronMeetingQualification.ps1',
        'tests\performance\meetings\README.md',
        'tests\performance\meetings\thresholds.json',
        'tests\backend\Hadron.Erp.IntegrationTests\MeetingTests.cs',
        'src\backend\Hadron.Erp.Api\Program.cs',
        'src\frontend\package.json')) {
        $candidate = Join-Path $repositoryRoot $relative
        if (Test-Path -LiteralPath $candidate) { $paths.Add((Resolve-Path $candidate).Path) | Out-Null }
    }
    foreach ($directory in @(
        (Join-Path $repositoryRoot 'src\backend\Hadron.Erp.Api\Modules\Collaboration'),
        (Join-Path $repositoryRoot 'src\frontend\src'))) {
        if (-not (Test-Path -LiteralPath $directory)) { continue }
        Get-ChildItem -LiteralPath $directory -File -Recurse | Where-Object {
            $_.Extension -in @('.cs', '.ts', '.tsx', '.css') -and
            ($_.FullName -match 'Meeting|meeting|CollaborationMeeting|collaborationMeeting')
        } | ForEach-Object { $paths.Add($_.FullName) | Out-Null }
    }
    return @($paths | Sort-Object | ForEach-Object { Get-FileEvidence $_ })
}

function Get-ToolEvidence {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$Path,
        [string[]]$VersionArguments = @('--version')
    )

    try {
        $capture = Invoke-TextProcess -FilePath $Path -ArgumentList $VersionArguments `
            -WorkingDirectory $repositoryRoot -TimeoutSeconds 30
        $version = (($capture.output -split '\r?\n') | Where-Object {
                -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -First 1)
        return [pscustomobject]@{
            name = $Name
            status = if ($capture.exitCode -eq 0) { 'Measured' } else { 'Unavailable' }
            version = $version
            executable = [System.IO.Path]::GetFileName($Path)
            sha256 = if (Test-Path -LiteralPath $Path -PathType Leaf) {
                (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
            } else { $null }
        }
    }
    catch {
        return [pscustomobject]@{
            name = $Name
            status = 'Unavailable'
            version = $null
            executable = [System.IO.Path]::GetFileName($Path)
            sha256 = $null
        }
    }
}

function Get-GitEvidence {
    param([Parameter(Mandatory)][string]$GitPath)

    $revision = Invoke-TextProcess -FilePath $GitPath -ArgumentList @('rev-parse', 'HEAD') `
        -WorkingDirectory $repositoryRoot
    if ($revision.exitCode -ne 0) { throw 'Unable to resolve repository revision.' }
    $status = Invoke-TextProcess -FilePath $GitPath `
        -ArgumentList @('status', '--porcelain=v1', '--untracked-files=all') `
        -WorkingDirectory $repositoryRoot
    if ($status.exitCode -ne 0) { throw 'Unable to inspect repository status.' }
    $counts = [ordered]@{}
    $lines = @($status.output -split '\r?\n' | Where-Object { $_.Length -ge 2 })
    foreach ($line in $lines) {
        $code = $line.Substring(0, 2)
        if (-not $counts.Contains($code)) { $counts[$code] = 0 }
        $counts[$code]++
    }
    return [pscustomobject]@{
        revision = $revision.output.Trim()
        workingTreeDirty = $lines.Count -gt 0
        changedPathCount = $lines.Count
        statusCounts = $counts
        diffContentsRecorded = $false
    }
}

function Test-QualificationTrx {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string[]]$ExpectedFullyQualifiedNames
    )

    [xml]$document = Get-Content -LiteralPath $Path -Raw
    $definitions = @($document.SelectNodes("//*[local-name()='UnitTest']"))
    $resultNodes = @($document.SelectNodes("//*[local-name()='UnitTestResult']"))
    $observed = [System.Collections.Generic.List[object]]::new()
    foreach ($definition in $definitions) {
        $method = $definition.SelectSingleNode(".//*[local-name()='TestMethod']")
        if ($null -eq $method) { continue }
        $fqn = "$($method.className).$($method.name)"
        foreach ($testResult in @($resultNodes | Where-Object { $_.testId -eq $definition.id })) {
            $observed.Add([pscustomobject]@{ fullyQualifiedName = $fqn; outcome = [string]$testResult.outcome })
        }
    }

    $problems = [System.Collections.Generic.List[string]]::new()
    foreach ($expected in $ExpectedFullyQualifiedNames) {
        $matches = @($observed | Where-Object { $_.fullyQualifiedName -eq $expected })
        if ($matches.Count -ne 1) {
            $problems.Add("Expected exactly one TRX result for '$expected'; found $($matches.Count).")
        } elseif ($matches[0].outcome -ne 'Passed') {
            $problems.Add("TRX outcome for '$expected' was '$($matches[0].outcome)'.")
        }
    }
    $failed = @($resultNodes | Where-Object { [string]$_.outcome -ne 'Passed' })
    if ($failed.Count -gt 0) { $problems.Add("TRX contains $($failed.Count) non-passing result(s).") }
    return [pscustomobject]@{
        passed = $problems.Count -eq 0
        expected = $ExpectedFullyQualifiedNames
        observed = @($observed)
        totalResults = $resultNodes.Count
        nonPassingResults = $failed.Count
        problems = @($problems)
    }
}

function Invoke-ControlPlane {
    param([Parameter(Mandatory)][hashtable]$DatabaseEnvironment)

    $dotnet = Get-ExecutablePath 'dotnet'
    $npm = Get-ExecutablePath 'npm.cmd'
    $processTimeout = [int]$thresholds.hostAbort.controlProcessHardTimeoutSeconds
    $expectedTests = @(
        'Hadron.Erp.IntegrationTests.MeetingTests.Fifty_internal_users_and_one_hundred_guests_complete_the_control_plane_lifecycle',
        'Hadron.Erp.IntegrationTests.MeetingTests.One_hundred_guest_status_pollers_behind_one_address_are_accepted_without_loading_roster'
    )
    if ([int]$thresholds.controlPlane.internalUsers -ne 50 -or
        [int]$thresholds.controlPlane.externalGuests -ne 100 -or
        [int]$thresholds.controlPlane.maximumHttpConcurrencyInIntegrationTest -ne 25) {
        Add-ComponentResult -Name 'control-plane-contract' -Status 'Failed' `
            -Detail 'The runner/test contract is fixed at 50 Testing identities, 100 guests and HTTP batches of 25; thresholds do not match it.' | Out-Null
        return
    }
    $filter = ($expectedTests | ForEach-Object { "FullyQualifiedName=$_" }) -join '|'

    for ($loop = 1; $loop -le $Loops; $loop++) {
        $backendDirectory = Join-Path $runDirectory ("backend-loop-{0:D2}" -f $loop)
        [System.IO.Directory]::CreateDirectory($backendDirectory) | Out-Null
        $backend = Invoke-RecordedProcess -Name ("control-backend-loop-{0:D2}" -f $loop) `
            -FilePath $dotnet `
            -ArgumentList @('test', $backendTestProject, '--no-restore', '--filter', $filter,
                '--logger', 'trx;LogFileName=meeting.trx', '--results-directory', $backendDirectory) `
            -WorkingDirectory $repositoryRoot -Environment $DatabaseEnvironment -TimeoutSeconds $processTimeout
        if ($backend.status -ne 'Passed') { return }

        $trxPath = Join-Path $backendDirectory 'meeting.trx'
        $trxEvidencePath = Join-Path $runDirectory ("control-backend-trx-loop-{0:D2}.json" -f $loop)
        try {
            $trx = Test-QualificationTrx -Path $trxPath -ExpectedFullyQualifiedNames $expectedTests
            Write-SafeText -Path $trxEvidencePath -Value ($trx | ConvertTo-Json -Depth 6)
            Add-ComponentResult -Name ("control-backend-trx-loop-{0:D2}" -f $loop) `
                -Status $(if ($trx.passed) { 'Passed' } else { 'Failed' }) `
                -Log ([System.IO.Path]::GetRelativePath($repositoryRoot, $trxEvidencePath)) `
                -Detail $(if ($trx.passed) { $null } else { $trx.problems -join ' ' }) | Out-Null
            if (-not $trx.passed) { return }
        }
        catch {
            Write-SafeText -Path $trxEvidencePath -Value $_.Exception.Message
            Add-ComponentResult -Name ("control-backend-trx-loop-{0:D2}" -f $loop) `
                -Status 'Failed' -Log ([System.IO.Path]::GetRelativePath($repositoryRoot, $trxEvidencePath)) `
                -Detail 'TRX qualification evidence could not be parsed.' | Out-Null
            return
        }

        $frontend = Invoke-RecordedProcess -Name ("control-frontend-loop-{0:D2}" -f $loop) `
            -FilePath $npm -ArgumentList @('run', 'test:collaboration') `
            -WorkingDirectory $frontendDirectory -TimeoutSeconds $processTimeout
        if ($frontend.status -ne 'Passed') { return }
    }

    $lint = Invoke-RecordedProcess -Name 'control-frontend-lint' -FilePath $npm `
        -ArgumentList @('run', 'lint') -WorkingDirectory $frontendDirectory -TimeoutSeconds $processTimeout
    if ($lint.status -eq 'Passed') {
        Invoke-RecordedProcess -Name 'control-frontend-build' -FilePath $npm `
            -ArgumentList @('run', 'build') -WorkingDirectory $frontendDirectory `
            -TimeoutSeconds $processTimeout | Out-Null
    }
}

function Resolve-LiveKitCli {
    if (-not [string]::IsNullOrWhiteSpace($LiveKitCliPath)) {
        $resolved = [System.IO.Path]::GetFullPath($LiveKitCliPath)
        if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
            throw 'LiveKit CLI was not found at the supplied path.'
        }
        return $resolved
    }
    foreach ($relativePath in @(
            '.local-host\tools\livekit-cli\2.18.6\verified\lk.exe',
            '.local-host\tools\livekit-cli\2.18.6\lk.exe')) {
        $pinned = Join-Path $repositoryRoot $relativePath
        if (Test-Path -LiteralPath $pinned -PathType Leaf) { return $pinned }
    }
    return Get-ExecutablePath 'lk.exe'
}

function Get-Percentile {
    param([Parameter(Mandatory)][double[]]$Values, [Parameter(Mandatory)][double]$Percentile)

    if ($Values.Count -eq 0) { return $null }
    $sorted = @($Values | Sort-Object)
    $index = [Math]::Max(0, [Math]::Ceiling($Percentile * $sorted.Count) - 1)
    return [double]$sorted[$index]
}

function Get-LiveKitMetrics {
    param([Parameter(Mandatory)][string]$LogPath)

    $text = Get-Content -LiteralPath $LogPath -Raw
    $text = [regex]::Replace($text, "`e\[[0-9;]*[A-Za-z]", '')
    $rows = [System.Collections.Generic.List[object]]::new()
    $inSubscriberSummary = $false
    foreach ($line in ($text -split '\r?\n')) {
        if ($line -match '(?i)^\s*Subscriber summaries:\s*$') {
            $inSubscriberSummary = $true
            continue
        }
        if (-not $inSubscriberSummary) { continue }
        if ($line -notmatch '(?<received>[0-9][0-9,]*)\s*/\s*(?<expected>[0-9][0-9,]*)') { continue }
        $received = [double](($Matches['received']) -replace ',', '')
        $expected = [double](($Matches['expected']) -replace ',', '')
        $latencyMatch = [regex]::Match($line, '(?<latency>[0-9]+(?:\.[0-9]+)?)\s*ms')
        $dropMatch = [regex]::Match($line,
            '(?<dropped>[0-9][0-9,]*)\s*\(\s*(?<dropPercent>[0-9]+(?:\.[0-9]+)?)%\s*\)')
        $rows.Add([pscustomobject]@{
            isTotal = $line -match '(?i)\bTotal\b'
            received = $received
            expected = $expected
            latencyMilliseconds = if ($latencyMatch.Success) {
                [double]$latencyMatch.Groups['latency'].Value } else { $null }
            dropped = if ($dropMatch.Success) {
                [double](($dropMatch.Groups['dropped'].Value) -replace ',', '') } else { $null }
            dropPercent = if ($dropMatch.Success) {
                [double]$dropMatch.Groups['dropPercent'].Value } else { $null }
        })
    }

    $total = @($rows | Where-Object { $_.isTotal } | Select-Object -Last 1)
    $detailRows = @($rows | Where-Object { -not $_.isTotal })
    $deliveryRows = if ($total.Count -eq 1) { $total } else { $detailRows }
    $receivedValues = @($deliveryRows | ForEach-Object { [double]$_.received })
    $expectedValues = @($deliveryRows | ForEach-Object { [double]$_.expected })
    $receivedTotal = if ($receivedValues.Count -gt 0) {
        [double](($receivedValues | Measure-Object -Sum).Sum)
    } else { 0.0 }
    $expectedTotal = if ($expectedValues.Count -gt 0) {
        [double](($expectedValues | Measure-Object -Sum).Sum)
    } else { 0.0 }
    $deliveryRate = if ($expectedTotal -gt 0) { 100.0 * $receivedTotal / $expectedTotal } else { $null }
    $latencies = @($detailRows | Where-Object { $null -ne $_.latencyMilliseconds } |
        ForEach-Object { [double]$_.latencyMilliseconds })
    $dropPercent = if ($total.Count -eq 1 -and $null -ne $total[0].dropPercent) {
        [double]$total[0].dropPercent
    } else {
        # The Tracks x/y denominator counts subscriptions, while dropped is a
        # packet count. Combining those units would produce a false percentage.
        # Without the CLI's aggregate percentage, fail closed as NotMeasured.
        $null
    }
    return [pscustomobject]@{
        parsedRows = $rows.Count
        trackDeliverySuccessRatePercent = $deliveryRate
        trackLatencyP95Milliseconds = if ($latencies.Count -gt 0) {
            Get-Percentile -Values $latencies -Percentile 0.95
        } else { $null }
        reportedTrackDropPercent = $dropPercent
    }
}

function Test-MeasuredMediaMetrics {
    param(
        [Parameter(Mandatory)]$Metrics,
        [Parameter(Mandatory)][string]$MediaProfile,
        [Parameter(Mandatory)][int]$LoopNumber
    )

    $checks = [System.Collections.Generic.List[object]]::new()
    foreach ($definition in @(
        @{ Name = 'trackDeliverySuccessRatePercent'; Value = $Metrics.trackDeliverySuccessRatePercent;
            Threshold = [double]$thresholds.media.cliMeasured.minimumTrackDeliverySuccessRatePercent; Mode = 'Minimum' },
        @{ Name = 'trackLatencyP95Milliseconds'; Value = $Metrics.trackLatencyP95Milliseconds;
            Threshold = [double]$thresholds.media.cliMeasured.maximumTrackLatencyP95Milliseconds; Mode = 'Maximum' },
        @{ Name = 'reportedTrackDropPercent'; Value = $Metrics.reportedTrackDropPercent;
            Threshold = [double]$thresholds.media.cliMeasured.maximumReportedTrackDropPercent; Mode = 'Maximum' })) {
        $measured = $null -ne $definition.Value
        $passed = $measured -and $(if ($definition.Mode -eq 'Minimum') {
                [double]$definition.Value -ge $definition.Threshold
            } else { [double]$definition.Value -le $definition.Threshold })
        $checks.Add([pscustomobject]@{
            metric = $definition.Name
            status = if ($measured) { 'Measured' } else { 'NotMeasured' }
            value = $definition.Value
            threshold = $definition.Threshold
            mode = $definition.Mode
            passed = $passed
        })
    }
    foreach ($definition in $thresholds.media.requiredQoe.PSObject.Properties) {
        $requiredQoeMetrics.Add([pscustomobject]@{
            profile = $MediaProfile
            loop = $LoopNumber
            metric = $definition.Name
            status = 'NotMeasured'
            threshold = $definition.Value
            passed = $false
            reason = 'The LiveKit CLI does not expose this required browser/WebRTC QoE measurement.'
        })
    }
    return @($checks)
}

function Get-LiveKitSemanticFailureDetail {
    param([AllowEmptyString()][string]$Output)

    if ([string]::IsNullOrWhiteSpace($Output)) {
        return $null
    }

    if ($Output -match '(?im)^\s*could not connect\b' -or
        $Output -match '(?i)\bunauthorized\b' -or
        $Output -match '(?i)\binvalid API key\b') {
        return 'LiveKit load clients could not authenticate or connect.'
    }

    if ($Output -match '(?i)\bNaNmbps\b') {
        return 'LiveKit completed without a measurable media stream.'
    }

    $summaryIndex = $Output.IndexOf('Subscriber summaries:', [StringComparison]::OrdinalIgnoreCase)
    if ($summaryIndex -ge 0) {
        $subscriberSummary = $Output.Substring($summaryIndex)
        $errorCountMatch = [regex]::Match($subscriberSummary,
            '(?im)^.*\bTotal\b.*[│|]\s*(?<errorCount>[1-9][0-9]*)\s*[│|]\s*$')
        if ($errorCountMatch.Success) {
            return 'One or more LiveKit load clients reported an error.'
        }
    }

    if ($Output -match '(?im)^\s*(?:fatal|panic)(?::|\s)') {
        return 'LiveKit reported a fatal runtime error.'
    }

    return $null
}

function Invoke-RecordedMediaProcess {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$FilePath,
        [Parameter(Mandatory)][string[]]$ArgumentList,
        [Parameter(Mandatory)][hashtable]$Environment,
        [Parameter(Mandatory)][ValidateSet('MediaSmoke', 'Local150')][string]$MediaProfile
    )

    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = New-HadronProcessStartInfo -FilePath $FilePath -ArgumentList $ArgumentList `
        -WorkingDirectory $repositoryRoot -Environment $Environment
    if (-not $process.Start()) { throw "Unable to start '$Name'." }
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $sampleIntervalSeconds = [int]$thresholds.hostAbort.sampleIntervalSeconds
    $hardTimeoutSeconds = [int]$thresholds.hostAbort.mediaProcessHardTimeoutSeconds
    $sustainedSeconds = 0
    $sampleNumber = 0
    $abortReason = $null
    $minimumMemory = if ($MediaProfile -eq 'Local150') {
        [double]$thresholds.hostAbort.minimumFreeMemoryGigabytesLocal150
    } else { [double]$thresholds.hostAbort.minimumFreeMemoryGigabytesMediaSmoke }

    while (-not $process.WaitForExit($sampleIntervalSeconds * 1000)) {
        $sampleNumber++
        $includeDocker = $sampleNumber -eq 1 -or
            (($sampleNumber * $sampleIntervalSeconds) % [int]$thresholds.hostAbort.dockerSampleIntervalSeconds -eq 0)
        $sample = Get-ResourceSample -IncludeDocker $includeDocker
        $resourceSamples.Add([pscustomobject]@{
            profile = $MediaProfile
            process = $Name
            observedAtUtc = $sample.observedAtUtc
            cpuPercent = $sample.cpuPercent
            freeMemoryGigabytes = $sample.freeMemoryGigabytes
            memoryMeasurementSource = $sample.memoryMeasurementSource
            dockerMemoryGigabytes = $sample.dockerMemoryGigabytes
        })
        if ($null -ne $sample.cpuPercent -and
            $sample.cpuPercent -ge [double]$thresholds.hostAbort.maximumBurstCpuPercent) {
            $abortReason = "System CPU reached the burst limit of $($thresholds.hostAbort.maximumBurstCpuPercent) percent."
        }
        if ($null -ne $sample.cpuPercent -and
            $sample.cpuPercent -ge [double]$thresholds.hostAbort.maximumSustainedCpuPercent) {
            $sustainedSeconds += $sampleIntervalSeconds
            if ($sustainedSeconds -ge [int]$thresholds.hostAbort.sustainedCpuWindowSeconds) {
                $abortReason = "System CPU remained above the sustained limit for $sustainedSeconds seconds."
            }
        } else { $sustainedSeconds = 0 }
        if ($null -eq $sample.freeMemoryGigabytes) {
            $abortReason = 'Free-memory measurement became unavailable; media load was stopped fail-closed.'
        } elseif ($sample.freeMemoryGigabytes -lt $minimumMemory) {
            $abortReason = "Free memory dropped below the $minimumMemory GB safety boundary."
        }
        if ($null -ne $sample.dockerMemoryGigabytes -and
            $sample.dockerMemoryGigabytes -gt [double]$thresholds.hostAbort.maximumDockerMemoryGigabytes) {
            $abortReason = "Docker memory exceeded $($thresholds.hostAbort.maximumDockerMemoryGigabytes) GB."
        }
        if ($stopwatch.Elapsed.TotalSeconds -ge $hardTimeoutSeconds) {
            $abortReason = "Media process exceeded the ${hardTimeoutSeconds}s hard timeout."
        }
        if (-not [string]::IsNullOrWhiteSpace($abortReason)) {
            Stop-HadronProcess $process
            break
        }
    }
    if (-not $process.HasExited) { Stop-HadronProcess $process }
    $stdout = $stdoutTask.GetAwaiter().GetResult()
    $stderr = $stderrTask.GetAwaiter().GetResult()
    $stopwatch.Stop()
    $combined = (($stdout, $stderr | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) `
            -join [Environment]::NewLine)
    if (-not [string]::IsNullOrWhiteSpace($abortReason)) { $combined += "`nHARNESS ABORT: $abortReason" }
    $logPath = Join-Path $runDirectory ($Name + '.log')
    Write-SafeText -Path $logPath -Value $combined
    $semanticFailureDetail = Get-LiveKitSemanticFailureDetail -Output $combined
    $detail = if (-not [string]::IsNullOrWhiteSpace($abortReason)) {
        $abortReason
    } else {
        $semanticFailureDetail
    }
    $status = if ([string]::IsNullOrWhiteSpace($detail) -and $process.ExitCode -eq 0) {
        'Passed' } else { 'Failed' }
    $processResult = Add-ComponentResult -Name $Name -Status $status `
        -ExitCode $(if ([string]::IsNullOrWhiteSpace($abortReason)) { $process.ExitCode } else { $null }) `
        -DurationMilliseconds $stopwatch.Elapsed.TotalMilliseconds `
        -Log ([System.IO.Path]::GetRelativePath($repositoryRoot, $logPath)) `
        -Coverage 'Partial' -Detail $detail
    return [pscustomobject]@{ result = $processResult; logPath = $logPath }
}

function Invoke-MediaProfile {
    param(
        [Parameter(Mandatory)][ValidateSet('MediaSmoke', 'Local150')][string]$MediaProfile,
        [Parameter(Mandatory)]$Snapshot,
        [Parameter(Mandatory)][hashtable]$LiveKitEnvironment,
        [Parameter(Mandatory)][int]$LoopNumber
    )

    $profileKey = if ($MediaProfile -eq 'MediaSmoke') { 'smoke' } else { 'local150' }
    $preflightReasons = @(Test-MediaPreflight -MediaProfile $MediaProfile -Snapshot $Snapshot)
    if ($preflightReasons.Count -gt 0) {
        Add-RecordedBlock -Name ("media-$profileKey-preflight-loop-{0:D2}" -f $LoopNumber) `
            -Reason (($preflightReasons | ForEach-Object { "- $_" }) -join [Environment]::NewLine)
        return
    }

    $lk = Resolve-LiveKitCli
    if (-not $toolEvidence.Contains('liveKitCli')) {
        $toolEvidence['liveKitCli'] = Get-ToolEvidence -Name 'LiveKit CLI' -Path $lk
    }
    $room = 'hadron-qualification-' + $runId.ToLowerInvariant() + ("-loop-{0:D2}" -f $LoopNumber)
    if ($MediaProfile -eq 'MediaSmoke') {
        $arguments = @('load-test', '--room', $room, '--duration', $MediaDuration,
            '--video-publishers', '2', '--audio-publishers', '2', '--subscribers', '10',
            '--video-resolution', 'low', '--num-per-second', '3', '--layout', 'speaker', '--simulate-speakers')
        $name = 'media-smoke-loop-{0:D2}' -f $LoopNumber
    } else {
        $arguments = @('load-test', '--room', $room, '--duration', $MediaDuration,
            '--video-publishers', '4', '--audio-publishers', '6', '--subscribers', '140',
            '--video-resolution', 'low', '--num-per-second', '5', '--layout', 'speaker', '--simulate-speakers')
        $name = 'media-local150-loop-{0:D2}' -f $LoopNumber
    }
    $mediaInvocations.Add([pscustomobject]@{
        profile = $MediaProfile
        loop = $LoopNumber
        executableSha256 = $toolEvidence['liveKitCli'].sha256
        arguments = $arguments
        credentialsPassedAsArguments = $false
    })
    $run = Invoke-RecordedMediaProcess -Name $name -FilePath $lk -ArgumentList $arguments `
        -Environment $LiveKitEnvironment -MediaProfile $MediaProfile
    if ($run.result.status -ne 'Passed') { return }

    $metrics = Get-LiveKitMetrics -LogPath $run.logPath
    $checks = @(Test-MeasuredMediaMetrics -Metrics $metrics -MediaProfile $MediaProfile `
        -LoopNumber $LoopNumber)
    $metricEvidence = [pscustomobject]@{
        parser = 'LiveKit CLI summary rows'
        metrics = $metrics
        checks = $checks
        requiredQoe = @($requiredQoeMetrics | Where-Object {
                $_.profile -eq $MediaProfile -and $_.loop -eq $LoopNumber })
    }
    $metricsPath = Join-Path $runDirectory ($name + '-metrics.json')
    Write-SafeText -Path $metricsPath -Value ($metricEvidence | ConvertTo-Json -Depth 8)
    $failedChecks = @($checks | Where-Object { $_.status -eq 'Measured' -and -not $_.passed })
    $notMeasuredChecks = @($checks | Where-Object { $_.status -eq 'NotMeasured' })
    $metricStatus = if ($failedChecks.Count -gt 0) { 'Failed' }
    elseif ($notMeasuredChecks.Count -gt 0) { 'Blocked' }
    else { 'Passed' }
    Add-ComponentResult -Name ($name + '-measured-metrics') -Status $metricStatus `
        -Coverage 'Partial' -Log ([System.IO.Path]::GetRelativePath($repositoryRoot, $metricsPath)) `
        -Detail $(if ($metricStatus -eq 'Blocked') { 'The CLI summary did not expose all measurable fields.' }
            elseif ($metricStatus -eq 'Failed') { 'One or more measurable CLI thresholds failed.' }
            else { 'Available CLI metrics passed; required browser QoE metrics remain NotMeasured.' }) | Out-Null
}

function Invoke-MediaLoops {
    param(
        [Parameter(Mandatory)][ValidateSet('MediaSmoke', 'Local150')][string]$MediaProfile,
        [Parameter(Mandatory)]$Snapshot,
        [Parameter(Mandatory)][hashtable]$LiveKitEnvironment
    )

    for ($loop = 1; $loop -le $Loops; $loop++) {
        $resultCountBeforeLoop = $results.Count
        Invoke-MediaProfile -MediaProfile $MediaProfile -Snapshot $Snapshot `
            -LiveKitEnvironment $LiveKitEnvironment -LoopNumber $loop
        $loopResults = @($results | Select-Object -Skip $resultCountBeforeLoop)
        $failed = @($loopResults | Where-Object { $_.status -eq 'Failed' })
        $preflightBlocked = @($loopResults | Where-Object {
                $_.status -eq 'Blocked' -and $_.name -match '-preflight-loop-' })
        if ($loopResults.Count -eq 0 -or $failed.Count -gt 0 -or $preflightBlocked.Count -gt 0) {
            break
        }
    }
}

function Sanitize-And-TestArtifacts {
    $textExtensions = @('.log', '.trx', '.json', '.txt', '.xml')
    foreach ($file in Get-ChildItem -LiteralPath $runDirectory -File -Recurse | Where-Object {
            $_.Extension.ToLowerInvariant() -in $textExtensions -and $_.Name -ne 'manifest.json' }) {
        try {
            $raw = [string](Get-Content -LiteralPath $file.FullName -Raw)
            $safe = Protect-EvidenceText $raw
            if ($safe -cne $raw) {
                [System.IO.File]::WriteAllText($file.FullName, $safe, [System.Text.UTF8Encoding]::new($false))
            }
        }
        catch {
            return [pscustomobject]@{ passed = $false; reason = "Unable to sanitize '$($file.Name)'." }
        }
    }

    $residual = [System.Collections.Generic.List[string]]::new()
    foreach ($file in Get-ChildItem -LiteralPath $runDirectory -File -Recurse | Where-Object {
            $_.Extension.ToLowerInvariant() -in $textExtensions -and $_.Name -ne 'manifest.json' }) {
        $text = [string](Get-Content -LiteralPath $file.FullName -Raw)
        foreach ($secret in $knownSecretValues) {
            if (-not [string]::IsNullOrWhiteSpace($secret) -and $text.Contains($secret)) {
                $residual.Add("Exact secret remains in $($file.Name).")
            }
        }
        if ([regex]::IsMatch($text, 'eyJ[A-Za-z0-9_-]{12,}\.[A-Za-z0-9_-]{12,}\.[A-Za-z0-9_-]{12,}')) {
            $residual.Add("JWT-like value remains in $($file.Name).")
        }
        if ([regex]::IsMatch($text,
                '(?i)(?<![A-Za-z0-9_])(inviteToken|accessTicket|joinToken|mediaToken|token)["'']?\s*[:=]\s*["'']?[A-Za-z0-9_-]{24,512}')) {
            $residual.Add("Labelled token-like value remains in $($file.Name).")
        }
        foreach ($match in [regex]::Matches($text,
                '(?<![A-Za-z0-9_-])[A-Za-z0-9_-]{43,512}(?![A-Za-z0-9_-])')) {
            if (Test-OpaqueTokenCandidate $match.Value) {
                $residual.Add("Opaque token-like value remains in $($file.Name).")
                break
            }
        }
    }
    if ($residual.Count -gt 0) {
        return [pscustomobject]@{ passed = $false; reason = ($residual -join ' ') }
    }
    if ($evidenceRedactionCount -gt 0) {
        return [pscustomobject]@{
            passed = $false
            reason = "$evidenceRedactionCount sensitive/token-like value(s) were detected and removed; the run fails closed."
        }
    }
    return [pscustomobject]@{ passed = $true; reason = 'No secret or token-like evidence was detected.' }
}

function Get-QualificationGates {
    $requiredLoops = [int]$thresholds.qualification.requiredConsecutiveSuccessfulLoops
    $requiredProfiles = @($thresholds.qualification.requiredProfiles)
    $expectedRequiredProfiles = @('ControlPlane', 'MediaSmoke', 'Local150')
    $requiredProfilesMatch = $requiredProfiles.Count -eq $expectedRequiredProfiles.Count -and
        @($expectedRequiredProfiles | Where-Object { $_ -notin $requiredProfiles }).Count -eq 0

    $controlBackend = @($results | Where-Object { $_.name -match '^control-backend-loop-[0-9]{2}$' })
    $controlTrx = @($results | Where-Object { $_.name -match '^control-backend-trx-loop-[0-9]{2}$' })
    $controlFrontend = @($results | Where-Object { $_.name -match '^control-frontend-loop-[0-9]{2}$' })
    $controlLint = @($results | Where-Object { $_.name -eq 'control-frontend-lint' })
    $controlBuild = @($results | Where-Object { $_.name -eq 'control-frontend-build' })
    $controlPassed = $controlBackend.Count -eq $Loops -and $controlTrx.Count -eq $Loops -and
        $controlFrontend.Count -eq $Loops -and $controlLint.Count -eq 1 -and $controlBuild.Count -eq 1 -and
        @($controlBackend + $controlTrx + $controlFrontend + $controlLint + $controlBuild |
            Where-Object { $_.status -ne 'Passed' }).Count -eq 0

    $mediaSmokeProcesses = @($results | Where-Object { $_.name -match '^media-smoke-loop-[0-9]{2}$' })
    $mediaSmokeMetrics = @($results | Where-Object {
            $_.name -match '^media-smoke-loop-[0-9]{2}-measured-metrics$' })
    $mediaSmokePassed = $mediaSmokeProcesses.Count -eq $Loops -and
        $mediaSmokeMetrics.Count -eq $Loops -and
        @($mediaSmokeProcesses + $mediaSmokeMetrics | Where-Object { $_.status -ne 'Passed' }).Count -eq 0

    $mediaLocalProcesses = @($results | Where-Object { $_.name -match '^media-local150-loop-[0-9]{2}$' })
    $mediaLocalMetrics = @($results | Where-Object {
            $_.name -match '^media-local150-loop-[0-9]{2}-measured-metrics$' })
    $mediaLocalPassed = $mediaLocalProcesses.Count -eq $Loops -and
        $mediaLocalMetrics.Count -eq $Loops -and
        @($mediaLocalProcesses + $mediaLocalMetrics | Where-Object { $_.status -ne 'Passed' }).Count -eq 0

    $controlWasAttempted = $controlBackend.Count + $controlTrx.Count + $controlFrontend.Count +
        $controlLint.Count + $controlBuild.Count -gt 0
    $mediaSmokeWasAttempted = @($results | Where-Object {
            $_.name -match '^media-smoke-(loop|preflight-loop)-' }).Count -gt 0
    $mediaLocalWasAttempted = @($results | Where-Object {
            $_.name -match '^media-local150-(loop|preflight-loop)-' }).Count -gt 0
    return @(
        [pscustomobject]@{ gate = 'RequiredProfileConfiguration'; passed = $requiredProfilesMatch;
            status = if ($requiredProfilesMatch) { 'Passed' } else { 'InvalidConfiguration' } },
        [pscustomobject]@{ gate = 'ProfileAll'; passed = $Profile -eq 'All';
            status = if ($Profile -eq 'All') { 'Passed' } else { 'NotRun' } },
        [pscustomobject]@{ gate = 'ConsecutiveLoops'; passed = $Loops -ge $requiredLoops;
            status = if ($Loops -ge $requiredLoops) { 'Passed' } else { 'NotSatisfied' } },
        [pscustomobject]@{ gate = 'ControlPlane'; passed = $controlPassed;
            status = if (-not $controlWasAttempted) { 'NotRun' }
                elseif ($controlPassed) { 'Passed' } else { 'IncompleteOrFailed' } },
        [pscustomobject]@{ gate = 'MediaSmoke'; passed = $mediaSmokePassed;
            status = if (-not $mediaSmokeWasAttempted) { 'NotRun' }
                elseif ($mediaSmokePassed) { 'Passed' } else { 'IncompleteOrFailed' } },
        [pscustomobject]@{ gate = 'MediaLocal150'; passed = $mediaLocalPassed;
            status = if (-not $mediaLocalWasAttempted) { 'NotRun' }
                elseif ($mediaLocalPassed) { 'Passed' } else { 'IncompleteOrFailed' } },
        [pscustomobject]@{ gate = 'RequiredBrowserQoe'; passed = $requiredQoeMetrics.Count -gt 0 -and
                @($requiredQoeMetrics | Where-Object { -not $_.passed }).Count -eq 0;
            status = if ($requiredQoeMetrics.Count -eq 0) { 'NotMeasured' }
                elseif (@($requiredQoeMetrics | Where-Object { -not $_.passed }).Count -eq 0) { 'Passed' }
                else { 'Incomplete' } },
        [pscustomobject]@{ gate = 'ExternalDeviceUat';
            passed = -not [bool]$thresholds.qualification.requiredExternalDeviceUat;
            status = if ([bool]$thresholds.qualification.requiredExternalDeviceUat) { 'NotVerified' }
                else { 'NotRequired' } },
        [pscustomobject]@{ gate = 'TurnAndTcpFallbackUat';
            passed = -not [bool]$thresholds.qualification.requiredTurnAndTcpFallbackUat;
            status = if ([bool]$thresholds.qualification.requiredTurnAndTcpFallbackUat) { 'NotVerified' }
                else { 'NotRequired' } },
        [pscustomobject]@{ gate = 'HumanAccessibilityReview';
            passed = -not [bool]$thresholds.qualification.requiredHumanAccessibilityReview;
            status = if ([bool]$thresholds.qualification.requiredHumanAccessibilityReview) { 'NotVerified' }
                else { 'NotRequired' } },
        [pscustomobject]@{ gate = 'NoOpenPriorityZeroOrOneDefects';
            passed = -not [bool]$thresholds.qualification.requiredNoOpenPriorityZeroOrOneDefects;
            status = if ([bool]$thresholds.qualification.requiredNoOpenPriorityZeroOrOneDefects) {
                    'NotVerified' } else { 'NotRequired' } }
    )
}

try {
    $thresholdsRaw = Get-Content -LiteralPath $thresholdsPath -Raw
    $thresholds = $thresholdsRaw | ConvertFrom-Json
    $thresholdSnapshotPath = Join-Path $runDirectory 'thresholds.snapshot.json'
    Write-SafeText -Path $thresholdSnapshotPath -Value $thresholdsRaw
    $thresholdSnapshotEvidence = Get-FileEvidence $thresholdSnapshotPath

    $configurationEnvironment = Get-HadronEnvironment
    foreach ($secretName in @('LIVEKIT_API_KEY', 'LIVEKIT_API_SECRET', 'POSTGRES_PASSWORD')) {
        if ($configurationEnvironment.ContainsKey($secretName) -and
            -not [string]::IsNullOrWhiteSpace([string]$configurationEnvironment[$secretName])) {
            $knownSecretValues.Add([string]$configurationEnvironment[$secretName])
        }
    }
    $databaseEnvironment = Select-HadronEnvironment @(
        'POSTGRES_HOST', 'POSTGRES_PORT', 'POSTGRES_USER', 'POSTGRES_PASSWORD', 'POSTGRES_DB')
    $liveKitEnvironment = Select-HadronEnvironment @('LIVEKIT_URL', 'LIVEKIT_API_KEY', 'LIVEKIT_API_SECRET')

    $hostSnapshot = Get-HostSnapshot
    $dotnet = Get-ExecutablePath 'dotnet'
    $npm = Get-ExecutablePath 'npm.cmd'
    $git = Get-ExecutablePath 'git.exe'
    $dockerCommand = Get-Command 'docker.exe' -ErrorAction SilentlyContinue
    if ($null -ne $dockerCommand) { $dockerExecutable = $dockerCommand.Source }
    $toolEvidence['dotnet'] = Get-ToolEvidence -Name '.NET SDK' -Path $dotnet
    $toolEvidence['npm'] = Get-ToolEvidence -Name 'npm' -Path $npm
    $toolEvidence['git'] = Get-ToolEvidence -Name 'git' -Path $git
    if ($null -ne $dockerExecutable) {
        $toolEvidence['docker'] = Get-ToolEvidence -Name 'Docker' -Path $dockerExecutable
    } else {
        $toolEvidence['docker'] = [pscustomobject]@{ name = 'Docker'; status = 'Unavailable';
            version = $null; executable = $null; sha256 = $null }
    }
    $gitEvidence = Get-GitEvidence $git
    $sourceEvidence = Get-RelevantSourceEvidence

    switch ($Profile) {
        'ControlPlane' { Invoke-ControlPlane -DatabaseEnvironment $databaseEnvironment }
        'MediaSmoke' {
            Invoke-MediaLoops -MediaProfile 'MediaSmoke' -Snapshot $hostSnapshot `
                -LiveKitEnvironment $liveKitEnvironment
        }
        'Local150' {
            Invoke-MediaLoops -MediaProfile 'Local150' -Snapshot $hostSnapshot `
                -LiveKitEnvironment $liveKitEnvironment
        }
        'All' {
            Invoke-ControlPlane -DatabaseEnvironment $databaseEnvironment
            if (@($results | Where-Object { $_.status -ne 'Passed' }).Count -eq 0) {
                Invoke-MediaLoops -MediaProfile 'MediaSmoke' -Snapshot $hostSnapshot `
                    -LiveKitEnvironment $liveKitEnvironment
                $smokeProcesses = @($results | Where-Object {
                        $_.name -match '^media-smoke-loop-[0-9]{2}$' })
                $smokeMetrics = @($results | Where-Object {
                        $_.name -match '^media-smoke-loop-[0-9]{2}-measured-metrics$' })
                if ($smokeProcesses.Count -eq $Loops -and $smokeMetrics.Count -eq $Loops -and
                    @($smokeProcesses + $smokeMetrics | Where-Object {
                            $_.status -ne 'Passed' }).Count -eq 0) {
                    Invoke-MediaLoops -MediaProfile 'Local150' -Snapshot $hostSnapshot `
                        -LiveKitEnvironment $liveKitEnvironment
                }
            }
        }
    }
    $mainCompleted = $true
}
catch {
    $safeMessage = Protect-EvidenceText $_.Exception.Message
    $safeStack = Protect-EvidenceText $_.ScriptStackTrace
    $harnessFailures.Add([pscustomobject]@{
        stage = 'Execution'
        type = $_.Exception.GetType().FullName
        message = $safeMessage
        scriptStackTrace = $safeStack
    })
    $harnessLogPath = Join-Path $runDirectory 'harness-failure.log'
    Write-SafeText -Path $harnessLogPath -Value ($safeMessage + [Environment]::NewLine + $safeStack)
    Add-ComponentResult -Name 'qualification-harness' -Status 'Failed' `
        -Log ([System.IO.Path]::GetRelativePath($repositoryRoot, $harnessLogPath)) `
        -Detail 'The qualification harness failed before completing the requested profile.' | Out-Null
}
finally {
    try {
        $resourcePath = Join-Path $runDirectory 'resource-samples.json'
        Write-SafeText -Path $resourcePath -Value (ConvertTo-Json -InputObject @($resourceSamples) -Depth 6)
        $artifactScan = Sanitize-And-TestArtifacts
        $scanLogPath = Join-Path $runDirectory 'artifact-secret-scan.log'
        Write-SafeText -Path $scanLogPath -Value $artifactScan.reason
        Add-ComponentResult -Name 'artifact-secret-scan' `
            -Status $(if ($artifactScan.passed) { 'Passed' } else { 'Failed' }) `
            -Log ([System.IO.Path]::GetRelativePath($repositoryRoot, $scanLogPath)) `
            -Detail $artifactScan.reason | Out-Null
    }
    catch {
        $harnessFailures.Add([pscustomobject]@{
            stage = 'ArtifactScan'
            type = $_.Exception.GetType().FullName
            message = Protect-EvidenceText $_.Exception.Message
        })
        Add-ComponentResult -Name 'artifact-secret-scan' -Status 'Failed' `
            -Detail 'Artifact sanitization or secret scanning failed.' | Out-Null
    }

    $qualificationGates = if ($null -ne $thresholds) { @(Get-QualificationGates) } else { @() }
    $finalQualified = $mainCompleted -and $harnessFailures.Count -eq 0 -and
        $qualificationGates.Count -gt 0 -and
        @($qualificationGates | Where-Object { -not $_.passed }).Count -eq 0 -and
        @($results | Where-Object { $_.status -ne 'Passed' }).Count -eq 0
    $failedCount = @($results | Where-Object { $_.status -eq 'Failed' }).Count
    $requestedComponentResults = @($results | Where-Object {
            $_.name -notin @('artifact-secret-scan', 'qualification-harness') })
    $blockedCount = @($requestedComponentResults | Where-Object { $_.status -eq 'Blocked' }).Count
    $passedCount = @($requestedComponentResults | Where-Object { $_.status -eq 'Passed' }).Count
    $partialCoverage = @($requestedComponentResults | Where-Object {
            $_.coverage -eq 'Partial' }).Count -gt 0
    $runOutcome = if ($failedCount -gt 0 -or $harnessFailures.Count -gt 0) { 'Failed' }
    elseif ($blockedCount -gt 0 -and $passedCount -eq 0) { 'Blocked' }
    elseif ($blockedCount -gt 0 -or $partialCoverage) { 'Partial' }
    else { 'ComponentPassed' }

    $manifest = [ordered]@{
        schemaVersion = 2
        runId = $runId
        createdAtUtc = [DateTime]::UtcNow.ToString('O')
        profile = $Profile
        requestedLoops = $Loops
        mediaDuration = $MediaDuration
        runOutcome = $runOutcome
        finalQualified = $finalQualified
        finalQualificationStatus = if ($finalQualified) { 'Passed' } else { 'NotQualified' }
        qualificationGates = $qualificationGates
        harnessFailure = if ($harnessFailures.Count -eq 0) { $null } else { @($harnessFailures) }
        evidenceDirectory = [System.IO.Path]::GetRelativePath($repositoryRoot, $runDirectory)
        evidenceDirectoryIsUniquelyAddressed = $true
        evidenceDirectoryIsImmutable = $false
        git = $gitEvidence
        relevantFiles = $sourceEvidence
        thresholdsSnapshot = $thresholdSnapshotEvidence
        tools = $toolEvidence
        host = $hostSnapshot
        mediaInvocations = @($mediaInvocations)
        requiredQoeMetrics = @($requiredQoeMetrics)
        resourceSamples = [System.IO.Path]::GetRelativePath($repositoryRoot,
            (Join-Path $runDirectory 'resource-samples.json'))
        mediaContentRecordingEnabled = $false
        results = @($results)
    }
    try {
        $manifestPath = Join-Path $runDirectory 'manifest.json'
        [System.IO.File]::WriteAllText($manifestPath,
            (Protect-EvidenceText ($manifest | ConvertTo-Json -Depth 12)),
            [System.Text.UTF8Encoding]::new($false))
    }
    catch {
        Write-Warning "Unable to write the qualification manifest: $($_.Exception.Message)"
    }

    Write-Host "Meeting qualification run outcome: $runOutcome"
    Write-Host "Final qualification: $(if ($finalQualified) { 'Passed' } else { 'NotQualified' })"
    Write-Host "Evidence: $runDirectory"
    if ($runOutcome -eq 'Failed') { exit 1 }
    if ($runOutcome -in @('Blocked', 'Partial')) { exit 2 }
    exit 0
}
