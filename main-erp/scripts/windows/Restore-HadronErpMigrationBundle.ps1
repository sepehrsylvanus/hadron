[CmdletBinding()]
param(
    [string]$BundleRoot,
    [switch]$ConfirmRestore
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $ConfirmRestore) {
    throw 'Restore replaces PostgreSQL and MinIO data. Re-run with -ConfirmRestore only in an approved Development or Staging environment.'
}

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if ([string]::IsNullOrWhiteSpace($BundleRoot)) {
    # New archive: <bundle>/project/main-erp; legacy archive: <bundle>/project.
    $projectRoot = $repositoryRoot
    $parentRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot '..'))
    if (Test-Path -LiteralPath (Join-Path $parentRoot '.git')) { $projectRoot = $parentRoot }
    $BundleRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot '..'))
}
$BundleRoot = [IO.Path]::GetFullPath($BundleRoot)
$stateRoot = Join-Path $BundleRoot 'state'
$handoffRoot = Join-Path $BundleRoot 'handoff'
$postgresDump = Join-Path $stateRoot 'postgres.dump'
$minioRoot = Join-Path $stateRoot 'minio'
$checksumPath = Join-Path $handoffRoot 'SHA256SUMS.txt'
$manifestPath = Join-Path $handoffRoot 'manifest.json'
$minioParityPath = Join-Path $handoffRoot 'minio-parity.csv'
$postgresRestoreListPath = Join-Path $handoffRoot 'postgres-restore-list.txt'
$localHostManager = Join-Path $PSScriptRoot 'Manage-HadronErpLocalHost.ps1'
$restoreCompleted = $false
$localHostWasRunning = $false
$minioAccessKeyWasPresent = Test-Path Env:MINIO_ACCESS_KEY
$minioSecretKeyWasPresent = Test-Path Env:MINIO_SECRET_KEY
$previousMinioAccessKey = $env:MINIO_ACCESS_KEY
$previousMinioSecretKey = $env:MINIO_SECRET_KEY

foreach ($requiredPath in @(
    $postgresDump,
    $minioRoot,
    $checksumPath,
    $manifestPath,
    $minioParityPath,
    $postgresRestoreListPath,
    (Join-Path $repositoryRoot '.env')
)) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw "Required restore input is missing: $requiredPath"
    }
}

function Assert-NativeSuccess {
    param([Parameter(Mandatory)][string]$Operation)
    if ($LASTEXITCODE -ne 0) {
        throw "$Operation failed with exit code $LASTEXITCODE."
    }
}

function Test-LocalTcpPort {
    param([Parameter(Mandatory)][int]$Port)

    $client = [Net.Sockets.TcpClient]::new()
    try {
        $connection = $client.ConnectAsync('127.0.0.1', $Port)
        return $connection.Wait(750) -and $client.Connected
    }
    catch {
        return $false
    }
    finally {
        $client.Dispose()
    }
}

Push-Location $repositoryRoot
try {
    $checksummedPaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $bundlePrefix = $BundleRoot.TrimEnd('\') + '\'
    foreach ($line in Get-Content -LiteralPath $checksumPath) {
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }
        if ($line -notmatch '^([0-9a-fA-F]{64})  (.+)$') {
            throw "Invalid checksum row: $line"
        }
        $hash = $Matches[1]
        $relative = $Matches[2]
        if (-not $checksummedPaths.Add($relative)) {
            throw "Duplicate checksum path: $relative"
        }
        $candidate = [IO.Path]::GetFullPath((Join-Path $BundleRoot ($relative.Replace('/', '\'))))
        if (-not $candidate.StartsWith($bundlePrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Unsafe checksum path: $relative"
        }
        if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            throw "Checksummed file is missing: $relative"
        }
        $actual = (Get-FileHash -LiteralPath $candidate -Algorithm SHA256).Hash
        if (-not $actual.Equals($hash, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Checksum mismatch: $relative"
        }
    }

    $actualPayloadPaths = @(Get-ChildItem -LiteralPath $BundleRoot -Recurse -Force -File |
        Where-Object { $_.FullName -ne $checksumPath } |
        ForEach-Object { [IO.Path]::GetRelativePath($BundleRoot, $_.FullName).Replace('\', '/') })
    if ($actualPayloadPaths.Count -ne $checksummedPaths.Count) {
        throw "Bundle inventory mismatch. Checksummed=$($checksummedPaths.Count); actual=$($actualPayloadPaths.Count)."
    }
    foreach ($actualPayloadPath in $actualPayloadPaths) {
        if (-not $checksummedPaths.Contains($actualPayloadPath)) {
            throw "Unlisted payload file is not allowed: $actualPayloadPath"
        }
    }

    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.format -ne 'hadron-erp-complete-handoff-v1' -or
        $manifest.plaintextSecretsIncluded -ne $false -or
        $manifest.postgresFormat -ne 'custom') {
        throw 'The bundle manifest is unsupported or unsafe.'
    }
    $dumpBytes = [IO.File]::ReadAllBytes($postgresDump)
    if ($dumpBytes.Length -lt 5 -or [Text.Encoding]::ASCII.GetString($dumpBytes, 0, 5) -ne 'PGDMP') {
        throw 'postgres.dump is not a PostgreSQL custom-format archive.'
    }

    $managedTaskNames = @('Hadron ERP Local API', 'Hadron ERP Local Frontend')
    $managedTasks = @($managedTaskNames | ForEach-Object {
        Get-ScheduledTask -TaskName $_ -ErrorAction SilentlyContinue
    } | Where-Object { $null -ne $_ })
    $localHostWasRunning = @($managedTasks | Where-Object { $_.State -in @('Running', 'Queued') }).Count -gt 0
    if ($localHostWasRunning) {
        & $localHostManager -Action Stop
    }
    if ((Test-LocalTcpPort -Port 5080) -or (Test-LocalTcpPort -Port 5173)) {
        throw 'The Hadron API or frontend is still running. Stop every local application process before restoring.'
    }

    & docker compose up -d --wait postgres minio
    Assert-NativeSuccess -Operation 'Storage startup'
    $postgresContainer = (& docker compose ps -q postgres).Trim()
    Assert-NativeSuccess -Operation 'PostgreSQL container discovery'
    $containerDump = "/tmp/hadron-restore-$([Guid]::NewGuid().ToString('N')).dump"
    & docker cp $postgresDump "${postgresContainer}:$containerDump"
    Assert-NativeSuccess -Operation 'PostgreSQL restore copy'
    try {
        & docker compose exec -T postgres pg_restore --list $containerDump | Out-Null
        Assert-NativeSuccess -Operation 'PostgreSQL restore archive validation'

        $destinationTableCountText = (& docker compose exec -T postgres sh -ec `
            'psql --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" --tuples-only --no-align --command "select count(*) from pg_tables where schemaname not in (''pg_catalog'', ''information_schema'');"').Trim()
        Assert-NativeSuccess -Operation 'Destination PostgreSQL emptiness check'
        [long]$destinationTableCount = 0
        if (-not [long]::TryParse($destinationTableCountText, [ref]$destinationTableCount)) {
            throw "Invalid destination table count: $destinationTableCountText"
        }
        if ($destinationTableCount -ne 0) {
            throw "Destination PostgreSQL is not empty ($destinationTableCount user table(s)). Use a fresh isolated Development/Staging database."
        }

        foreach ($line in Get-Content -LiteralPath '.env') {
            if ($line -match '^(MINIO_ACCESS_KEY|MINIO_SECRET_KEY)=(.*)$') {
                Set-Item -Path "Env:$($Matches[1])" -Value $Matches[2]
            }
        }
        if ([string]::IsNullOrWhiteSpace($env:MINIO_ACCESS_KEY) -or [string]::IsNullOrWhiteSpace($env:MINIO_SECRET_KEY)) {
            throw 'MinIO credentials are missing from the destination .env file.'
        }

        $minioImage = 'minio/minio:RELEASE.2025-09-07T16-13-09Z'
        $minioParity = @(Import-Csv -LiteralPath $minioParityPath)
        $backupBucketDirectories = @(Get-ChildItem -LiteralPath $minioRoot -Directory | Sort-Object Name)
        $parityBucketNames = @($minioParity | ForEach-Object { $_.Bucket } | Sort-Object -Unique)
        $directoryBucketNames = @($backupBucketDirectories | ForEach-Object { $_.Name } | Sort-Object -Unique)
        if (($parityBucketNames -join "`n") -ne ($directoryBucketNames -join "`n")) {
            throw 'MinIO backup directories do not match the signed parity inventory.'
        }

        foreach ($bucketDirectory in $backupBucketDirectories) {
            $targetObjectLines = & docker run --rm --network hadron-erp_default `
                --env MINIO_ACCESS_KEY --env MINIO_SECRET_KEY --env "RESTORE_BUCKET=$($bucketDirectory.Name)" `
                --entrypoint /bin/sh $minioImage -ec `
                'mc alias set target http://minio:9000 "$MINIO_ACCESS_KEY" "$MINIO_SECRET_KEY" >/dev/null; if mc stat "target/$RESTORE_BUCKET" >/dev/null 2>&1; then mc ls --recursive --json "target/$RESTORE_BUCKET"; fi'
            Assert-NativeSuccess -Operation "Destination MinIO emptiness check for $($bucketDirectory.Name)"
            $targetObjects = @($targetObjectLines | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object {
                $_ | ConvertFrom-Json -ErrorAction Stop
            } | Where-Object { $_.type -eq 'file' })
            if ($targetObjects.Count -ne 0) {
                throw "Destination MinIO bucket '$($bucketDirectory.Name)' is not empty. Use fresh isolated object storage."
            }
        }

        & docker compose exec -T postgres sh -ec "pg_restore --single-transaction --exit-on-error --no-owner --no-acl --username `"`$POSTGRES_USER`" --dbname `"`$POSTGRES_DB`" '$containerDump'"
        Assert-NativeSuccess -Operation 'PostgreSQL restore'

        foreach ($bucketDirectory in $backupBucketDirectories) {
            & docker run --rm --network hadron-erp_default `
                --env MINIO_ACCESS_KEY --env MINIO_SECRET_KEY --env "RESTORE_BUCKET=$($bucketDirectory.Name)" `
                --mount "type=bind,source=$minioRoot,target=/backup,readonly" `
                --entrypoint /bin/sh $minioImage -ec `
                'mc alias set target http://minio:9000 "$MINIO_ACCESS_KEY" "$MINIO_SECRET_KEY" >/dev/null; mc mb --ignore-existing "target/$RESTORE_BUCKET" >/dev/null; mc mirror --overwrite "/backup/$RESTORE_BUCKET" "target/$RESTORE_BUCKET"'
            Assert-NativeSuccess -Operation "MinIO restore for $($bucketDirectory.Name)"

            $restoredObjectLines = & docker run --rm --network hadron-erp_default `
                --env MINIO_ACCESS_KEY --env MINIO_SECRET_KEY --env "RESTORE_BUCKET=$($bucketDirectory.Name)" `
                --entrypoint /bin/sh $minioImage -ec `
                'mc alias set target http://minio:9000 "$MINIO_ACCESS_KEY" "$MINIO_SECRET_KEY" >/dev/null; mc ls --recursive --json "target/$RESTORE_BUCKET"'
            Assert-NativeSuccess -Operation "MinIO post-restore inventory for $($bucketDirectory.Name)"
            $restoredObjects = @($restoredObjectLines | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object {
                $_ | ConvertFrom-Json -ErrorAction Stop
            } | Where-Object { $_.type -eq 'file' })
            $restoredBytes = ($restoredObjects | Measure-Object -Property size -Sum).Sum
            if ($null -eq $restoredBytes) { $restoredBytes = 0 }
            $expectedParity = $minioParity | Where-Object { $_.Bucket -eq $bucketDirectory.Name } | Select-Object -First 1
            if ($restoredObjects.Count -ne [long]$expectedParity.Objects -or $restoredBytes -ne [long]$expectedParity.Bytes) {
                throw "MinIO post-restore parity failed for $($bucketDirectory.Name)."
            }
        }
    }
    finally {
        & docker compose exec -T postgres rm -f $containerDump | Out-Null
    }

    $restoreCompleted = $true
    if ($localHostWasRunning) {
        & $localHostManager -Action Start
    }
}
finally {
    if ($minioAccessKeyWasPresent) {
        Set-Item -Path Env:MINIO_ACCESS_KEY -Value $previousMinioAccessKey
    }
    else {
        Remove-Item -Path Env:MINIO_ACCESS_KEY -ErrorAction SilentlyContinue
    }
    if ($minioSecretKeyWasPresent) {
        Set-Item -Path Env:MINIO_SECRET_KEY -Value $previousMinioSecretKey
    }
    else {
        Remove-Item -Path Env:MINIO_SECRET_KEY -ErrorAction SilentlyContinue
    }
    Pop-Location -ErrorAction SilentlyContinue
    if (-not $restoreCompleted -and $localHostWasRunning) {
        Write-Warning 'Restore did not complete. The ERP application remains stopped so it cannot write partial data.'
    }
}
