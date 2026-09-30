[CmdletBinding()]
param(
    [string]$OutputDirectory,
    [switch]$KeepStaging,
    [switch]$SkipQuiesce
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repositoryRoot 'output\migration'
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

$timestamp = [DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssZ')
$packageName = "Hadron-ERP-Complete-Handoff-$timestamp"
$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$stagingRoot = [IO.Path]::GetFullPath((Join-Path $temporaryRoot "$packageName-$([Guid]::NewGuid().ToString('N'))"))
if (-not $stagingRoot.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetFileName($stagingRoot) -notlike 'Hadron-ERP-Complete-Handoff-*') {
    throw "Unsafe staging path: $stagingRoot"
}

$bundleRoot = Join-Path $stagingRoot $packageName
$projectDestination = Join-Path $bundleRoot 'project'
$stateDestination = Join-Path $bundleRoot 'state'
$handoffDestination = Join-Path $bundleRoot 'handoff'
$minioDestination = Join-Path $stateDestination 'minio'
New-Item -ItemType Directory -Path $projectDestination, $stateDestination, $handoffDestination, $minioDestination -Force | Out-Null
$localHostManager = Join-Path $PSScriptRoot 'Manage-HadronErpLocalHost.ps1'
$restartLocalHost = $false
$gitSafetyArguments = @('-c', "safe.directory=$repositoryRoot")
$partialZipPath = $null
$minioAccessKeyWasPresent = Test-Path Env:MINIO_ACCESS_KEY
$minioSecretKeyWasPresent = Test-Path Env:MINIO_SECRET_KEY
$previousMinioAccessKey = $env:MINIO_ACCESS_KEY
$previousMinioSecretKey = $env:MINIO_SECRET_KEY

$excludedDirectoryNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
@(
    '.codex', '.codex-build', '.codex-test', '.local-host', '.runtime', '.vite',
    'artifacts', 'backups', 'bin', 'coverage', 'dist', 'logs', 'node_modules',
    'obj', 'TestResults', 'tmp', 'temp'
) | ForEach-Object { [void]$excludedDirectoryNames.Add($_) }

function Test-ExcludedFile {
    param([Parameter(Mandatory)][IO.FileInfo]$File)

    if ($File.Name -eq '.env.example' -or $File.Name -eq 'production.env.example') {
        return $false
    }
    if ($File.Name -eq '.env' -or $File.Name.StartsWith('.env.', [StringComparison]::OrdinalIgnoreCase)) {
        return $true
    }
    if ($File.Name -in @('production.env', 'appsettings.Development.json', 'secrets.json')) {
        return $true
    }
    return $File.Extension -in @('.log', '.tmp')
}

function Copy-PortableTree {
    param(
        [Parameter(Mandatory)][string]$Source,
        [Parameter(Mandatory)][string]$Destination,
        [switch]$IsRepositoryRoot
    )

    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    foreach ($item in Get-ChildItem -LiteralPath $Source -Force) {
        if ($item.PSIsContainer) {
            if ($excludedDirectoryNames.Contains($item.Name) -or ($IsRepositoryRoot -and $item.Name -eq 'output')) {
                continue
            }
            Copy-PortableTree -Source $item.FullName -Destination (Join-Path $Destination $item.Name)
            continue
        }
        if (-not (Test-ExcludedFile -File $item)) {
            Copy-Item -LiteralPath $item.FullName -Destination (Join-Path $Destination $item.Name) -Force
        }
    }
}

function Get-PortableSourceFiles {
    Get-ChildItem -LiteralPath $repositoryRoot -Recurse -Force -File | Where-Object {
        $file = $_
        $relative = [IO.Path]::GetRelativePath($repositoryRoot, $file.FullName)
        $segments = $relative -split '[\\/]'
        if ($segments[0] -eq 'output') {
            return $false
        }
        foreach ($directoryName in $segments[0..([Math]::Max(0, $segments.Count - 2))]) {
            if ($excludedDirectoryNames.Contains($directoryName)) {
                return $false
            }
        }
        return -not (Test-ExcludedFile -File $file)
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

try {
    Push-Location $repositoryRoot

    foreach ($commandName in @('docker', 'git', 'tar')) {
        if (-not (Get-Command $commandName -ErrorAction SilentlyContinue)) {
            throw "Required command is missing: $commandName"
        }
    }
    if (-not (Test-Path -LiteralPath '.env')) {
        throw 'The local .env file is required to read the current Docker services safely.'
    }

    if (-not $SkipQuiesce) {
        $managedTaskNames = @('Hadron ERP Local API', 'Hadron ERP Local Frontend')
        $managedTasks = @($managedTaskNames | ForEach-Object {
            Get-ScheduledTask -TaskName $_ -ErrorAction SilentlyContinue
        } | Where-Object { $null -ne $_ })
        $restartLocalHost = @($managedTasks | Where-Object { $_.State -in @('Running', 'Queued') }).Count -gt 0
        if ($restartLocalHost) {
            & $localHostManager -Action Stop
        }
        if ((Test-LocalTcpPort -Port 5080) -or (Test-LocalTcpPort -Port 5173)) {
            throw 'Hadron API or frontend is still running. Stop every writer or use -SkipQuiesce only for an explicitly accepted non-quiesced backup.'
        }
    }

    Copy-PortableTree -Source $repositoryRoot -Destination $projectDestination -IsRepositoryRoot
    $portableSourceFiles = @(Get-PortableSourceFiles)
    $portableCopiedFiles = @(Get-ChildItem -LiteralPath $projectDestination -Recurse -Force -File)
    if ($portableSourceFiles.Count -ne $portableCopiedFiles.Count) {
        throw "Project snapshot file-count mismatch. Source=$($portableSourceFiles.Count); copy=$($portableCopiedFiles.Count)."
    }
    foreach ($sourceFile in $portableSourceFiles) {
        $relative = [IO.Path]::GetRelativePath($repositoryRoot, $sourceFile.FullName)
        $copiedFile = Join-Path $projectDestination $relative
        if (-not (Test-Path -LiteralPath $copiedFile -PathType Leaf)) {
            throw "Project snapshot is missing: $relative"
        }
        $sourceHash = (Get-FileHash -LiteralPath $sourceFile.FullName -Algorithm SHA256).Hash
        $copiedHash = (Get-FileHash -LiteralPath $copiedFile -Algorithm SHA256).Hash
        if (-not $sourceHash.Equals($copiedHash, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Project changed while the snapshot was copied: $relative. Retry after repository writers stop."
        }
    }
    $handbook = Join-Path $repositoryRoot 'output\pdf\Hadron-ERP-System-User-Handbook-FA.pdf'
    if (Test-Path -LiteralPath $handbook) {
        $handbookDestination = Join-Path $projectDestination 'output\pdf'
        New-Item -ItemType Directory -Path $handbookDestination -Force | Out-Null
        Copy-Item -LiteralPath $handbook -Destination $handbookDestination -Force
    }

    (& git @gitSafetyArguments status --short --branch) | Set-Content -LiteralPath (Join-Path $handoffDestination 'git-status.txt') -Encoding utf8
    Assert-NativeSuccess -Operation 'git status capture'
    (& git @gitSafetyArguments diff --binary --no-ext-diff) | Set-Content -LiteralPath (Join-Path $handoffDestination 'working-tree.patch') -Encoding utf8
    Assert-NativeSuccess -Operation 'git diff capture'
    (& git @gitSafetyArguments ls-files --others --exclude-standard) | Set-Content -LiteralPath (Join-Path $handoffDestination 'untracked-files.txt') -Encoding utf8
    Assert-NativeSuccess -Operation 'untracked file inventory'

    $toolVersionPath = Join-Path $handoffDestination 'tool-versions.txt'
    @(
        "created_utc=$timestamp",
        "git_head=$(& git @gitSafetyArguments rev-parse HEAD)",
        "git_branch=$(& git @gitSafetyArguments branch --show-current)",
        "dotnet_sdk=$(& dotnet --version)",
        "node=$(& node --version)",
        "npm=$(& npm --version)",
        "git=$(& git --version)",
        "powershell=$($PSVersionTable.PSVersion)",
        "docker=$(& docker --version)",
        "docker_compose=$(& docker compose version)"
    ) | Set-Content -LiteralPath $toolVersionPath -Encoding utf8
    Assert-NativeSuccess -Operation 'tool version capture'

    $environmentNames = Get-Content -LiteralPath '.env' |
        Where-Object { $_ -match '^[A-Za-z_][A-Za-z0-9_]*=' } |
        ForEach-Object { ($_ -split '=', 2)[0] } |
        Sort-Object -Unique
    @(
        '# Values are intentionally omitted. Create fresh values on the destination.',
        $environmentNames
    ) | Set-Content -LiteralPath (Join-Path $handoffDestination 'secrets-required.txt') -Encoding utf8

    $postgresContainer = (& docker compose ps -q postgres).Trim()
    Assert-NativeSuccess -Operation 'PostgreSQL container discovery'
    $minioContainer = (& docker compose ps -q minio).Trim()
    Assert-NativeSuccess -Operation 'MinIO container discovery'
    if ([string]::IsNullOrWhiteSpace($postgresContainer) -or [string]::IsNullOrWhiteSpace($minioContainer)) {
        throw 'The Hadron ERP PostgreSQL and MinIO containers must be running.'
    }

    $containerDump = "/tmp/hadron-migration-$([Guid]::NewGuid().ToString('N')).dump"
    & docker compose exec -T postgres sh -ec "pg_dump --username `"`$POSTGRES_USER`" --dbname `"`$POSTGRES_DB`" --format=custom --no-owner --no-acl --file='$containerDump'"
    Assert-NativeSuccess -Operation 'PostgreSQL backup'
    $postgresDump = Join-Path $stateDestination 'postgres.dump'
    & docker cp "${postgresContainer}:$containerDump" $postgresDump
    Assert-NativeSuccess -Operation 'PostgreSQL backup copy'
    try {
        (& docker compose exec -T postgres pg_restore --list $containerDump) |
            Set-Content -LiteralPath (Join-Path $handoffDestination 'postgres-restore-list.txt') -Encoding utf8
        Assert-NativeSuccess -Operation 'PostgreSQL backup validation'
    }
    finally {
        & docker compose exec -T postgres rm -f $containerDump | Out-Null
    }
    $dumpMagic = [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($postgresDump), 0, 5)
    if ($dumpMagic -ne 'PGDMP') {
        throw 'PostgreSQL backup does not have the expected custom-format header.'
    }

    $collaborationAttachmentCountText = (& docker compose exec -T postgres sh -ec `
        'psql --username "$POSTGRES_USER" --dbname "$POSTGRES_DB" --tuples-only --no-align --command "select count(*) from collaboration.attachments;"').Trim()
    Assert-NativeSuccess -Operation 'Collaboration attachment count capture'
    [long]$collaborationAttachmentCount = 0
    if (-not [long]::TryParse($collaborationAttachmentCountText, [ref]$collaborationAttachmentCount)) {
        throw "Invalid Collaboration attachment count: $collaborationAttachmentCountText"
    }

    foreach ($line in Get-Content -LiteralPath '.env') {
        if ($line -match '^(MINIO_ACCESS_KEY|MINIO_SECRET_KEY)=(.*)$') {
            Set-Item -Path "Env:$($Matches[1])" -Value $Matches[2]
        }
    }
    if ([string]::IsNullOrWhiteSpace($env:MINIO_ACCESS_KEY) -or [string]::IsNullOrWhiteSpace($env:MINIO_SECRET_KEY)) {
        throw 'MinIO credentials are missing from the local environment.'
    }

    $minioImage = 'minio/minio:RELEASE.2025-09-07T16-13-09Z'
    $bucketLines = & docker run --rm --network hadron-erp_default `
        --env MINIO_ACCESS_KEY --env MINIO_SECRET_KEY `
        --entrypoint /bin/sh $minioImage -ec `
        'mc alias set source http://minio:9000 "$MINIO_ACCESS_KEY" "$MINIO_SECRET_KEY" >/dev/null; mc ls --json source'
    Assert-NativeSuccess -Operation 'MinIO bucket discovery'
    $bucketNames = @($bucketLines | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object {
        (($_ | ConvertFrom-Json -ErrorAction Stop).key).TrimEnd('/')
    } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique)

    $minioStatistics = @()
    foreach ($bucketName in $bucketNames) {
        $sourceObjectLines = & docker run --rm --network hadron-erp_default `
            --env MINIO_ACCESS_KEY --env MINIO_SECRET_KEY --env "BACKUP_BUCKET=$bucketName" `
            --entrypoint /bin/sh $minioImage -ec `
            'mc alias set source http://minio:9000 "$MINIO_ACCESS_KEY" "$MINIO_SECRET_KEY" >/dev/null; mc ls --recursive --json "source/$BACKUP_BUCKET"'
        Assert-NativeSuccess -Operation "MinIO inventory for $bucketName"
        $sourceObjects = @($sourceObjectLines | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object {
            $_ | ConvertFrom-Json -ErrorAction Stop
        } | Where-Object { $_.type -eq 'file' })
        & docker run --rm --network hadron-erp_default `
            --env MINIO_ACCESS_KEY --env MINIO_SECRET_KEY --env "BACKUP_BUCKET=$bucketName" `
            --mount "type=bind,source=$minioDestination,target=/backup" `
            --entrypoint /bin/sh $minioImage -ec `
            'mc alias set source http://minio:9000 "$MINIO_ACCESS_KEY" "$MINIO_SECRET_KEY" >/dev/null; mkdir -p "/backup/$BACKUP_BUCKET"; mc mirror --overwrite "source/$BACKUP_BUCKET" "/backup/$BACKUP_BUCKET"'
        Assert-NativeSuccess -Operation "MinIO mirror for $bucketName"

        $localBucketPath = Join-Path $minioDestination $bucketName
        $localObjects = @(Get-ChildItem -LiteralPath $localBucketPath -Recurse -Force -File)
        $sourceBytes = ($sourceObjects | Measure-Object -Property size -Sum).Sum
        $localBytes = ($localObjects | Measure-Object -Property Length -Sum).Sum
        if ($null -eq $sourceBytes) { $sourceBytes = 0 }
        if ($null -eq $localBytes) { $localBytes = 0 }
        if ($sourceObjects.Count -ne $localObjects.Count -or $sourceBytes -ne $localBytes) {
            throw "MinIO parity failed for $bucketName. Source=$($sourceObjects.Count)/$sourceBytes; backup=$($localObjects.Count)/$localBytes."
        }
        $minioStatistics += [pscustomobject]@{
            Bucket = $bucketName
            Objects = $localObjects.Count
            Bytes = $localBytes
        }
    }
    $bucketNames | Set-Content -LiteralPath (Join-Path $handoffDestination 'minio-buckets.txt') -Encoding utf8
    $minioStatistics | Export-Csv -LiteralPath (Join-Path $handoffDestination 'minio-parity.csv') -NoTypeInformation -Encoding utf8
    if ($collaborationAttachmentCount -gt 0 -and $bucketNames -notcontains 'hadron-collaboration-files') {
        throw "The database contains $collaborationAttachmentCount Collaboration attachment record(s), but the collaboration MinIO bucket is missing."
    }

    $manifest = [ordered]@{
        format = 'hadron-erp-complete-handoff-v1'
        createdUtc = $timestamp
        sourceGitHead = (& git @gitSafetyArguments rev-parse HEAD).Trim()
        sourceGitBranch = (& git @gitSafetyArguments branch --show-current).Trim()
        workingTreeIncluded = $true
        gitHistoryIncluded = $true
        projectSnapshotFiles = $portableCopiedFiles.Count
        postgresFormat = 'custom'
        minioBuckets = $bucketNames
        collaborationAttachmentRows = $collaborationAttachmentCount
        dataProtectionKeysIncluded = $false
        plaintextSecretsIncluded = $false
        previousBackupsIncluded = $false
        rebuildableCachesIncluded = $false
        securityClassification = 'CONFIDENTIAL - contains ERP database and private object data'
        exclusions = @(
            'plaintext environment/production secrets',
            'ASP.NET Core Data Protection keys and active browser sessions',
            'previous/stale backups',
            'node_modules, bin, obj, dist and build/test caches',
            'runtime processes, logs and temporary files',
            'unrelated hadron-store Docker project and data'
        )
    }
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $handoffDestination 'manifest.json') -Encoding utf8

    @'
# ابتدا این فایل را بخوانید

این بسته، Snapshot کامل و قابل‌توسعه Hadron ERP است. پوشه `project` منبع
کار فعلی و شامل Git، تغییرات ثبت‌نشده، مستندات، قوانین و تست‌هاست. پوشه
`state` شامل Dump فعلی PostgreSQL و Mirror منطقی تمام bucketهای MinIO است.

برای ادامه با Codex جدید:

1. ZIP را Extract و پوشه `project` را به‌عنوان Workspace باز کنید.
2. ابتدا `project/AGENTS.md` و سپس `project/docs/MIGRATION.md` را کامل بخوانید.
3. فایل `.env` عمداً وجود ندارد؛ از `.env.example` با Secretهای جدید بسازید.
4. Restore را ابتدا فقط در Development/Staging انجام دهید.
5. قبل از هر توسعه، `git-status.txt` را با Working Tree واقعی تطبیق دهید؛ بخش
   زیادی از پیاده‌سازی جاری هنوز Commit نشده ولی خود فایل‌ها داخل بسته هستند.

این ZIP محرمانه است: Database شامل کاربران، Password Hash، Audit و داده‌های
عملیاتی است و MinIO ممکن است عکس و فایل خصوصی داشته باشد. آن را عمومی نکنید.
'@ | Set-Content -LiteralPath (Join-Path $bundleRoot 'READ-ME-FIRST-FA.md') -Encoding utf8

    $fileInventory = Get-ChildItem -LiteralPath $bundleRoot -Recurse -Force -File |
        Where-Object { $_.Name -notin @('file-inventory.csv', 'SHA256SUMS.txt') } |
        ForEach-Object {
            [pscustomobject]@{
                Path = [IO.Path]::GetRelativePath($bundleRoot, $_.FullName).Replace('\', '/')
                Bytes = $_.Length
            }
        } | Sort-Object Path
    $fileInventory | Export-Csv -LiteralPath (Join-Path $handoffDestination 'file-inventory.csv') -NoTypeInformation -Encoding utf8

    $checksumPath = Join-Path $handoffDestination 'SHA256SUMS.txt'
    $checksumLines = Get-ChildItem -LiteralPath $bundleRoot -Recurse -Force -File |
        Where-Object { $_.FullName -ne $checksumPath } |
        Sort-Object FullName |
        ForEach-Object {
            $relative = [IO.Path]::GetRelativePath($bundleRoot, $_.FullName).Replace('\', '/')
            $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            "$hash  $relative"
        }
    $checksumLines | Set-Content -LiteralPath $checksumPath -Encoding ascii

    $zipPath = Join-Path $OutputDirectory "$packageName.zip"
    if ((Test-Path -LiteralPath $zipPath) -or (Test-Path -LiteralPath "$zipPath.sha256")) {
        throw "Refusing to overwrite an existing migration bundle: $zipPath"
    }
    $partialZipPath = "$zipPath.partial.zip"
    if (Test-Path -LiteralPath $partialZipPath) {
        throw "A partial migration bundle already exists: $partialZipPath"
    }
    & tar -a -c -f $partialZipPath -C $stagingRoot $packageName
    Assert-NativeSuccess -Operation 'ZIP creation'
    if (-not (Test-Path -LiteralPath $partialZipPath) -or (Get-Item -LiteralPath $partialZipPath).Length -le 0) {
        throw 'ZIP creation did not produce a valid file.'
    }
    $zipListing = @(& tar -tf $partialZipPath)
    Assert-NativeSuccess -Operation 'ZIP validation'
    $requiredZipEntries = @(
        "$packageName/READ-ME-FIRST-FA.md",
        "$packageName/project/AGENTS.md",
        "$packageName/project/.git/HEAD",
        "$packageName/project/docs/MIGRATION.md",
        "$packageName/project/scripts/windows/Restore-HadronErpMigrationBundle.ps1",
        "$packageName/state/postgres.dump",
        "$packageName/handoff/manifest.json",
        "$packageName/handoff/SHA256SUMS.txt",
        "$packageName/handoff/minio-parity.csv",
        "$packageName/handoff/postgres-restore-list.txt"
    )
    $missingZipEntries = @($requiredZipEntries | Where-Object { $zipListing -notcontains $_ })
    if ($zipListing.Count -lt 10 -or $missingZipEntries.Count -gt 0) {
        throw "ZIP validation did not find required entries: $($missingZipEntries -join ', ')"
    }

    Move-Item -LiteralPath $partialZipPath -Destination $zipPath
    $partialZipPath = $null

    $zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    "$zipHash  $([IO.Path]::GetFileName($zipPath))" |
        Set-Content -LiteralPath "$zipPath.sha256" -Encoding ascii

    [pscustomobject]@{
        ZipPath = $zipPath
        Sha256 = $zipHash
        SizeBytes = (Get-Item -LiteralPath $zipPath).Length
        FilesInZip = $zipListing.Count
        PostgresDumpBytes = (Get-Item -LiteralPath $postgresDump).Length
        MinioBuckets = ($bucketNames -join ', ')
    } | Format-List
}
finally {
    Pop-Location -ErrorAction SilentlyContinue
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
    if ($null -ne $partialZipPath -and (Test-Path -LiteralPath $partialZipPath)) {
        $verifiedPartialZip = [IO.Path]::GetFullPath($partialZipPath)
        $verifiedOutputDirectory = [IO.Path]::GetFullPath($OutputDirectory).TrimEnd('\') + '\'
        if (-not $verifiedPartialZip.StartsWith($verifiedOutputDirectory, [StringComparison]::OrdinalIgnoreCase) -or
            [IO.Path]::GetFileName($verifiedPartialZip) -notlike 'Hadron-ERP-Complete-Handoff-*.zip.partial.zip') {
            throw "Refusing to remove unsafe partial ZIP path: $verifiedPartialZip"
        }
        Remove-Item -LiteralPath $verifiedPartialZip -Force
    }
    if ($restartLocalHost) {
        & $localHostManager -Action Start
    }
    if (-not $KeepStaging -and (Test-Path -LiteralPath $stagingRoot)) {
        $verifiedStaging = [IO.Path]::GetFullPath($stagingRoot)
        if (-not $verifiedStaging.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase) -or
            [IO.Path]::GetFileName($verifiedStaging) -notlike 'Hadron-ERP-Complete-Handoff-*') {
            throw "Refusing to remove unsafe staging path: $verifiedStaging"
        }
        Remove-Item -LiteralPath $verifiedStaging -Recurse -Force
    }
}
