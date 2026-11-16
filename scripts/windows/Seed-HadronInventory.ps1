[CmdletBinding()]
param(
    [string]$ApiBaseUrl,
    [string]$UserName,
    [string]$Password,
    [string]$DataFile
)

$ErrorActionPreference = 'Stop'
# Windows PowerShell 5.1 may not load System.Net.Http until explicitly requested.
try {
    Add-Type -AssemblyName System.Net.Http -ErrorAction Stop
} catch {
    throw ('Could not load the .NET System.Net.Http assembly: {0}' -f $_.Exception.Message)
}
$script:Utf8 = New-Object System.Text.UTF8Encoding($false)
$script:Cookies = New-Object System.Net.CookieContainer
$script:Handler = New-Object System.Net.Http.HttpClientHandler
$script:Handler.CookieContainer = $script:Cookies
$script:Client = New-Object System.Net.Http.HttpClient($script:Handler)
$script:Client.Timeout = [TimeSpan]::FromSeconds(60)

function Get-ApiRoot {
    if ($ApiBaseUrl) { return $ApiBaseUrl.TrimEnd('/') }
    $root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    $envFile = Join-Path $root '.env'
    $apiPort = $null
    $frontPort = $null
    if (Test-Path $envFile) {
        foreach ($line in [System.IO.File]::ReadAllLines($envFile)) {
            if ($line -match '^\s*API_PORT\s*=\s*([^\s#]+)') { $apiPort = $Matches[1].Trim('"', "'") }
            if ($line -match '^\s*FRONTEND_PORT\s*=\s*([^\s#]+)') { $frontPort = $Matches[1].Trim('"', "'") }
        }
    }
    if ($apiPort) { return ('http://127.0.0.1:{0}' -f $apiPort) }
    try {
        $mapped = (& docker compose port api 8080 2>$null | Select-Object -First 1)
        if ($LASTEXITCODE -eq 0 -and $mapped) { return ('http://{0}' -f $mapped.Trim()) }
    } catch { }
    if ($frontPort) { return ('http://127.0.0.1:{0}/api' -f $frontPort) }
    return 'http://127.0.0.1:5080'
}

function Invoke-Api {
    param([string]$Method, [string]$Path, $Body, [switch]$AllowNotFound)
    $uri = $script:BaseUrl + $Path
    $httpMethod = New-Object System.Net.Http.HttpMethod($Method)
    $request = New-Object System.Net.Http.HttpRequestMessage($httpMethod, $uri)
    $json = $null
    if ($null -ne $Body) {
        $json = ConvertTo-Json -InputObject $Body -Depth 30 -Compress
        $request.Content = New-Object System.Net.Http.StringContent($json, $script:Utf8, 'application/json')
    }
    if ($script:Csrf) { [void]$request.Headers.TryAddWithoutValidation('X-CSRF-TOKEN', $script:Csrf) }
    try {
        $response = $script:Client.SendAsync($request).GetAwaiter().GetResult()
        $text = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    } catch {
        $detail = $_.Exception.Message
        if ($_.Exception.InnerException) { $detail = $_.Exception.InnerException.Message }
        throw ('Could not connect to Hadron API at {0}. Start Hadron with run-hadron.cmd, then retry. If Hadron is running on another address, pass -ApiBaseUrl. Network detail: {1}' -f $uri, $detail)
    }
    if (-not $response.IsSuccessStatusCode) {
        if ($AllowNotFound -and [int]$response.StatusCode -eq 404) { return $null }
        $message = '{0} {1} failed ({2}): {3}' -f $Method, $Path, [int]$response.StatusCode, $text
        if ($Method -eq 'POST' -and $Path -eq '/api/products' -and $json) {
            $message += "`nProduct request JSON: $json"
        }
        throw $message
    }
    if ([string]::IsNullOrWhiteSpace($text)) { return $null }
    return ($text | ConvertFrom-Json)
}

function Assert-SeedGuid {
    param([string]$Value, [string]$Label)
    $parsed = [Guid]::Empty
    if (-not [Guid]::TryParse($Value, [ref]$parsed) -or $parsed -eq [Guid]::Empty) {
        throw ('Seed lookup did not resolve a valid {0} ID. Check the account permissions and reference-data API response.' -f $Label)
    }
}

function Get-Collection {
    param([string]$Path)
    $result = Invoke-Api GET $Path $null
    # Let PowerShell enumerate collection members into the pipeline. Returning an array
    # with the unary comma makes Where-Object see the whole array as one row, which can
    # turn a matching reference ID into an array instead of a single Guid.
    if ($result -is [System.Array]) { return $result }
    if ($result.items) { return $result.items }
    if ($result.values) { return $result.values }
    return @()
}

function Ensure-Csrf {
    $result = Invoke-Api GET '/api/auth/csrf' $null
    $script:Csrf = $result.token
}

function Invoke-Seed {
    $root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    if (-not $DataFile) { $DataFile = Join-Path $root 'scripts\seed-data\inventory-demo.json' }
    if (-not (Test-Path $DataFile)) { throw ('Dataset not found: {0}' -f $DataFile) }
    $script:BaseUrl = Get-ApiRoot
    if ($script:BaseUrl.EndsWith('/api')) { $script:BaseUrl = $script:BaseUrl.Substring(0, $script:BaseUrl.Length - 4) }
    $dataset = [System.IO.File]::ReadAllText((Resolve-Path $DataFile), [System.Text.Encoding]::UTF8) | ConvertFrom-Json

    Write-Host ('Checking Hadron API at {0}' -f $script:BaseUrl)
    $null = Invoke-Api GET '/api/auth/bootstrap-status' $null
    Ensure-Csrf
    if (-not $UserName) { $UserName = $env:HADRON_SEED_USERNAME }
    if (-not $Password) { $Password = $env:HADRON_SEED_PASSWORD }
    if (-not $UserName) { $UserName = Read-Host 'Hadron username' }
    if (-not $Password) {
        $secure = Read-Host 'Hadron password' -AsSecureString
        $ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
        try { $Password = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr) }
        finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr) }
    }
    $login = Invoke-Api POST '/api/auth/login' @{ userName = $UserName; password = $Password }
    Ensure-Csrf
    Write-Host ('Connected to {0} as {1}' -f $script:BaseUrl, $UserName)

    # API mutations intentionally use server-generated identifiers and are performed in dependency order.
    $units = @{}
    foreach ($u in $dataset.units) {
        $existing = Get-Collection '/api/reference-data/units?page=1&pageSize=100' | Where-Object { $_.code -eq $u.code } | Select-Object -First 1
        if (-not $existing) { $existing = Invoke-Api POST '/api/reference-data/units' $u }
        $units[$u.code] = $existing.id
    }
    $references = @{}
    foreach ($v in $dataset.referenceValues) {
        $existing = Get-Collection ('/api/reference-data/types/{0}/values?page=1&pageSize=100' -f $v.typeCode) | Where-Object { $_.nameEn -eq $v.nameEn -or $_.nameFa -eq $v.nameFa } | Select-Object -First 1
        if (-not $existing) { $existing = Invoke-Api POST ('/api/reference-data/types/{0}/values' -f $v.typeCode) @{ nameFa=$v.nameFa; nameEn=$v.nameEn; sortOrder=$v.sortOrder } }
        $references[$v.key] = $existing.id
    }
    $items = @{}
    foreach ($p in $dataset.products) {
        $existing = Get-Collection '/api/products?page=1&pageSize=100' | Where-Object { $_.nameEn -eq $p.nameEn } | Select-Object -First 1
        if (-not $existing) {
            $categoryId = [string]$references[$p.categoryKey]
            $unitId = [string]$units[$p.baseUnitCode]
            $businessUnitId = [string]$references[$p.businessUnitKey]
            Assert-SeedGuid $categoryId ('product category ' + $p.categoryKey)
            Assert-SeedGuid $unitId ('base unit ' + $p.baseUnitCode)
            Assert-SeedGuid $businessUnitId ('business unit ' + $p.businessUnitKey)
            $payload = @{ nameFa=$p.nameFa; nameEn=$p.nameEn; productCategoryId=$categoryId; baseUnitId=$unitId; businessUnitId=$businessUnitId; safetyStockQty=$p.safetyStockQty; criticalDays=$p.criticalDays; safeStartDays=$p.safeStartDays; excessStartDays=$p.excessStartDays; notes=$p.notes }
            $r = Invoke-Api POST '/api/products' $payload
            $existing = $r.product
        }
        $items[$p.key] = $existing.id
    }
    foreach ($type in @(@{ rows=$dataset.parts; endpoint='/api/parts'; cat='PART_CATEGORY' }, @{ rows=$dataset.components; endpoint='/api/components'; cat='COMPONENT_CATEGORY' })) {
        foreach ($p in $type.rows) {
            $existing = Get-Collection ($type.endpoint + '?page=1&pageSize=100') | Where-Object { $_.nameEn -eq $p.nameEn } | Select-Object -First 1
            if (-not $existing) {
                $r = Invoke-Api POST $type.endpoint @{ nameFa=$p.nameFa; nameEn=$p.nameEn; categoryId=$references[$p.categoryKey]; baseUnitId=$units[$p.baseUnitCode]; supplyMethod=$p.supplyMethod; netWeightKg=$p.netWeightKg; defaultPlanningScrapPercent=$p.defaultPlanningScrapPercent; safetyStockQty=$p.safetyStockQty; criticalDays=$p.criticalDays; safeStartDays=$p.safeStartDays; excessStartDays=$p.excessStartDays; notes=$p.notes }
                $existing = $r.item
            }
            $items[$p.key] = $existing.id
        }
    }
    $overview = Invoke-Api GET '/api/inventory/overview' $null
    $today = [DateTime]::ParseExact([string]$overview.asOfDate, 'yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture)
    Write-Host 'Prerequisite units, categories, products and material items are ready.'
    Write-Warning 'Inventory warehouse, balances, transactions, conversions and transfer creation require the warehouse manager and responsible-user IDs. The script stops after safe prerequisites rather than creating partial ledger data.'
    Write-Host ('Server business date: {0}' -f $today.ToString('yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture))
    try { $null = Invoke-Api POST '/api/auth/logout' @{} } catch { }
}

try { Invoke-Seed; exit 0 }
catch {
    Write-Error $_
    try { if ($script:BaseUrl -and $script:Csrf) { $null = Invoke-Api POST '/api/auth/logout' @{} } } catch { }
    exit 1
}
finally {
    if ($script:Client) { $script:Client.Dispose() }
    if ($script:Handler) { $script:Handler.Dispose() }
}
