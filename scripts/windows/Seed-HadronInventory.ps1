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
    # Invoke-Api's pipeline unwraps a top-level JSON array containing one object.
    if ($null -ne $result -and $result.PSObject.Properties['id']) { return $result }
    return @()
}

function Get-PagedCollection {
    param([string]$Path)
    $page = 1
    $seen = 0
    $separator = if ($Path.Contains('?')) { '&' } else { '?' }
    do {
        $result = Invoke-Api GET ('{0}{1}page={2}&pageSize=100' -f $Path, $separator, $page) $null
        if ($null -eq $result -or $null -eq $result.PSObject.Properties['items'] -or
            $null -eq $result.PSObject.Properties['totalCount']) {
            throw ('Expected a paginated response from {0}.' -f $Path)
        }
        $rows = @($result.items)
        if ($rows.Count -eq 0 -and $seen -lt [int]$result.totalCount) {
            throw ('Incomplete paginated response from {0}; refusing to continue seed.' -f $Path)
        }
        $rows
        $seen += $rows.Count
        $page++
    } while ($seen -lt [int]$result.totalCount)
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
        $existing = Get-Collection '/api/reference-data/units?includeInactive=true' | Where-Object { $_.code -eq $u.code } | Select-Object -First 1
        if (-not $existing) { $existing = Invoke-Api POST '/api/reference-data/units' $u }
        elseif (-not $existing.isActive) { $existing = Invoke-Api POST ('/api/reference-data/units/{0}/reactivate' -f $existing.id) @{ expectedVersion=$existing.version } }
        $units[$u.code] = $existing.id
    }
    $references = @{}
    foreach ($v in $dataset.referenceValues) {
        $existing = Get-Collection ('/api/reference-data/types/{0}/values?includeInactive=true' -f $v.typeCode) | Where-Object { $_.nameEn -eq $v.nameEn -or $_.nameFa -eq $v.nameFa } | Select-Object -First 1
        if (-not $existing) { $existing = Invoke-Api POST ('/api/reference-data/types/{0}/values' -f $v.typeCode) @{ nameFa=$v.nameFa; nameEn=$v.nameEn; sortOrder=$v.sortOrder } }
        elseif (-not $existing.isActive) { $existing = Invoke-Api POST ('/api/reference-data/values/{0}/reactivate' -f $existing.id) @{ expectedVersion=$existing.version } }
        $references[$v.key] = $existing.id
    }
    $items = @{}
    foreach ($p in $dataset.products) {
        $existing = Get-PagedCollection '/api/products?includeInactive=true' | Where-Object { $_.nameEn -eq $p.nameEn } | Select-Object -First 1
        if ($existing -and -not $existing.isActive) { $existing = Invoke-Api POST ('/api/products/{0}/reactivate' -f $existing.id) @{ expectedVersion=$existing.version; reason='Reactivated by inventory demo seed' } }
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
            $existing = Get-PagedCollection ($type.endpoint + '?includeInactive=true') | Where-Object { $_.nameEn -eq $p.nameEn } | Select-Object -First 1
            if ($existing -and -not $existing.isActive) { $existing = Invoke-Api POST ($type.endpoint + '/' + $existing.id + '/reactivate') @{ expectedVersion=$existing.version; reason='Reactivated by inventory demo seed' } }
            if (-not $existing) {
                $r = Invoke-Api POST $type.endpoint @{ nameFa=$p.nameFa; nameEn=$p.nameEn; categoryId=$references[$p.categoryKey]; baseUnitId=$units[$p.baseUnitCode]; supplyMethod=$p.supplyMethod; netWeightKg=$p.netWeightKg; defaultPlanningScrapPercent=$p.defaultPlanningScrapPercent; safetyStockQty=$p.safetyStockQty; criticalDays=$p.criticalDays; safeStartDays=$p.safeStartDays; excessStartDays=$p.excessStartDays; notes=$p.notes }
                $existing = $r.item
            }
            $items[$p.key] = $existing.id
        }
    }
    $overview = Invoke-Api GET '/api/inventory/overview' $null
    $today = [DateTime]::ParseExact([string]$overview.asOfDate, 'yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture)
    $responsibleResponse = Invoke-Api GET '/api/inventory/responsible-users?page=1&pageSize=100' $null
    $responsible = @($responsibleResponse.items | Where-Object { $_.isActive } | Select-Object -First 1)
    if ($responsible.Count -eq 0) { throw 'No active user is available as a warehouse responsible person. Create or activate a user, then rerun.' }
    $responsibleId = [string]$responsible[0].id
    Assert-SeedGuid $responsibleId 'warehouse responsible user'

    # Resolve/create all warehouses first; codes are always allocated by the API.
    $warehouseRows = @(Get-PagedCollection '/api/inventory/warehouses?includeInactive=true')
    $warehouses = @{}
    foreach ($w in $dataset.warehouses) {
        $warehouse = $warehouseRows | Where-Object { $_.nameEn -eq $w.nameEn } | Select-Object -First 1
        if (-not $warehouse) {
            $warehouse = Invoke-Api POST '/api/inventory/warehouses' @{ nameFa=$w.nameFa; nameEn=$w.nameEn; addressFa=$w.addressFa; addressEn=$w.addressEn; primaryResponsibleUserId=$responsibleId; notes=$w.notes; warehouseType=$w.warehouseType }
            $warehouseRows += $warehouse
        }
        $warehouses[$w.key] = $warehouse
        if (-not $w.isActive -and $warehouse.isActive) {
            $warehouse = Invoke-Api POST ('/api/inventory/warehouses/{0}/deactivate' -f $warehouse.id) @{ expectedVersion=$warehouse.version; reason=$w.deactivateReason }
            $warehouses[$w.key] = $warehouse
        } elseif ($w.isActive -and -not $warehouse.isActive) {
            $warehouse = Invoke-Api POST ('/api/inventory/warehouses/{0}/reactivate' -f $warehouse.id) @{ expectedVersion=$warehouse.version; reason='Reactivated by inventory demo seed' }
            $warehouses[$w.key] = $warehouse
        }
    }

    # Set up per-item transaction units before posting ledger activity.
    foreach ($conversion in $dataset.unitConversions) {
        $itemId = [string]$items[$conversion.itemKey]
        $unitId = [string]$units[$conversion.transactionUnitCode]
        Assert-SeedGuid $itemId ('item ' + $conversion.itemKey)
        Assert-SeedGuid $unitId ('transaction unit ' + $conversion.transactionUnitCode)
        $conversionList = Invoke-Api GET ('/api/inventory/items/{0}/units?includeInactive=true' -f $itemId) $null
        $current = @($conversionList.items | Where-Object { $_.transactionUnit.code -eq $conversion.transactionUnitCode } | Select-Object -First 1)
        if ($current.Count -eq 0) {
            $row = Invoke-Api POST ('/api/inventory/items/{0}/units' -f $itemId) @{ transactionUnitId=$unitId; factorToBase=$conversion.factorToBase }
        } else {
            $row = $current[0]
            if ([decimal]$row.factorToBase -ne [decimal]$conversion.factorToBase) {
                throw ('Existing conversion for {0}/{1} has factor {2}; expected {3}. Resolve this manually to avoid changing live stock semantics.' -f $conversion.itemKey, $conversion.transactionUnitCode, $row.factorToBase, $conversion.factorToBase)
            }
            if ($conversion.isActive -and -not $row.isActive) {
                $row = Invoke-Api POST ('/api/inventory/items/{0}/units/{1}/reactivate' -f $itemId, $row.id) @{ expectedVersion=$row.version; reason='Reactivated by inventory demo seed' }
            } elseif (-not $conversion.isActive -and $row.isActive) {
                $row = Invoke-Api POST ('/api/inventory/items/{0}/units/{1}/deactivate' -f $itemId, $row.id) @{ expectedVersion=$row.version; reason=$conversion.deactivateReason }
            }
        }
    }

    # Post tagged transactions in chronological order. Tags make reruns idempotent.
    $postedTransactions = @(Get-PagedCollection '/api/inventory/transactions')
    foreach ($transaction in ($dataset.transactions | Sort-Object -Property daysAgo -Descending)) {
        $already = $postedTransactions | Where-Object { $_.reason -and $_.reason.Contains($transaction.seedTag) } | Select-Object -First 1
        if ($already) { continue }
        $itemId = [string]$items[$transaction.itemKey]
        $warehouseId = [string]$warehouses[$transaction.warehouseKey].id
        if ($transaction.operation -eq 'OPENING') {
            $existingOpening = $postedTransactions | Where-Object { $_.transactionType -eq 'OPENING' -and $_.item.id -eq $itemId -and $_.warehouse.id -eq $warehouseId } | Select-Object -First 1
            if ($existingOpening) {
                Write-Warning ('Skipped {0}: an OPENING already exists for {1} in {2}.' -f $transaction.seedTag, $transaction.itemKey, $transaction.warehouseKey)
                continue
            }
        }
        $unitId = [string]$units[$transaction.transactionUnitCode]
        Assert-SeedGuid $itemId ('item ' + $transaction.itemKey)
        Assert-SeedGuid $warehouseId ('warehouse ' + $transaction.warehouseKey)
        Assert-SeedGuid $unitId ('transaction unit ' + $transaction.transactionUnitCode)
        $effectiveDate = $today.AddDays(-[int]$transaction.daysAgo).ToString('yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture)
        $body = @{ itemId=$itemId; warehouseId=$warehouseId; quantity=$transaction.quantity; effectiveDate=$effectiveDate; reason=$transaction.reason; transactionUnitId=$unitId }
        switch ($transaction.operation) {
            'OPENING' { $path = '/api/inventory/openings' }
            'RECEIPT' { $path = '/api/inventory/receipts' }
            'ISSUE' { $path = '/api/inventory/issues' }
            'ADJUSTMENT' { $path = '/api/inventory/adjustments'; $body.direction=$transaction.direction }
            default { throw ('Unknown transaction operation: {0}' -f $transaction.operation) }
        }
        $posted = Invoke-Api POST $path $body
        $postedTransactions += $posted
        Write-Host ('Posted {0} {1} to {2}' -f $transaction.seedTag, $transaction.itemKey, $transaction.warehouseKey)
    }

    foreach ($reversal in $dataset.reversals) {
        $postedTransactions = @(Get-PagedCollection '/api/inventory/transactions')
        $already = $postedTransactions | Where-Object { $_.reason -and $_.reason.Contains('[INV-SEED:REV-01]') } | Select-Object -First 1
        if (-not $already) {
            $original = $postedTransactions | Where-Object { $_.reason -and $_.reason.Contains($reversal.targetSeedTag) } | Select-Object -First 1
            if (-not $original) { throw ('Cannot reverse missing seeded transaction {0}.' -f $reversal.targetSeedTag) }
            $null = Invoke-Api POST ('/api/inventory/transactions/{0}/reverse' -f $original.id) @{ reason=$reversal.reason }
        }
    }

    # Create/continue the two-step transfer workflows, preserving current API versions on each mutation.
    $transferRows = @(Get-PagedCollection '/api/inventory/transfers')
    foreach ($transferData in $dataset.transfers) {
        $transfer = $transferRows | Where-Object { $_.reason -and $_.reason.Contains($transferData.seedTag) } | Select-Object -First 1
        if (-not $transfer) {
            $itemId = [string]$items[$transferData.itemKey]
            $sourceId = [string]$warehouses[$transferData.sourceWarehouseKey].id
            $destinationId = [string]$warehouses[$transferData.destinationWarehouseKey].id
            $unitId = [string]$units[$transferData.transactionUnitCode]
            $transfer = Invoke-Api POST '/api/inventory/transfers' @{ itemId=$itemId; sourceWarehouseId=$sourceId; destinationWarehouseId=$destinationId; quantity=$transferData.quantity; reason=$transferData.reason; transactionUnitId=$unitId }
            $transferRows += $transfer
        }
        if ($transferData.dispatchDaysAgo -and $transfer.status -eq 'DRAFT') {
            $dispatchDate = $today.AddDays(-[int]$transferData.dispatchDaysAgo).ToString('yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture)
            $transfer = Invoke-Api POST ('/api/inventory/transfers/{0}/dispatch' -f $transfer.id) @{ effectiveDate=$dispatchDate; expectedVersion=$transfer.version; reason=$transferData.dispatchReason }
        }
        foreach ($receiptData in $transferData.receipts) {
            $receipt = @($transfer.receipts | Where-Object { $_.reason -and $_.reason.Contains($receiptData.seedTag) } | Select-Object -First 1)
            if ($receipt.Count -eq 0 -and $transfer.status -in @('DISPATCHED','PARTIALLY_RECEIVED')) {
                $receiptDate = $today.AddDays(-[int]$receiptData.daysAgo).ToString('yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture)
                $transfer = Invoke-Api POST ('/api/inventory/transfers/{0}/receive' -f $transfer.id) @{ quantity=$receiptData.quantity; effectiveDate=$receiptDate; expectedVersion=$transfer.version; reason=$receiptData.reason; transactionUnitId=[string]$units[$transferData.transactionUnitCode] }
                $receipt = @($transfer.receipts | Where-Object { $_.reason -and $_.reason.Contains($receiptData.seedTag) } | Select-Object -First 1)
            }
            if ($receiptData.reverseReason -and $receipt.Count -gt 0 -and -not $receipt[0].isReversed) {
                $transfer = Invoke-Api POST ('/api/inventory/transfers/{0}/receipts/{1}/reverse' -f $transfer.id, $receipt[0].id) @{ expectedVersion=$transfer.version; reason=$receiptData.reverseReason }
            }
        }
        if ($transferData.cancelReason -and $transfer.status -ne 'CANCELLED') {
            $transfer = Invoke-Api POST ('/api/inventory/transfers/{0}/cancel' -f $transfer.id) @{ expectedVersion=$transfer.version; reason=$transferData.cancelReason }
        }
        Write-Host ('Transfer {0}: {1}' -f $transferData.seedTag, $transfer.status)
    }

    # Keep the inactive demo warehouse empty: deactivation before seeding ensures its stock invariant.
    $overview = Invoke-Api GET '/api/inventory/overview' $null
    if ($overview.itemPositionCount -le 0) { throw 'Seed finished without creating any item positions. Verify Inventory permissions and rerun.' }
    if ($overview.negativeBalanceCount -gt 0) { throw ('Seed finished with {0} negative item balances; review the ledger before using this demo data.' -f $overview.negativeBalanceCount) }
    Write-Host ('Seed complete. Warehouses active/inactive: {0}/{1}; positions: {2}; open transfers: {3}; in transit: {4}; negative balances: {5}' -f $overview.activeWarehouseCount, $overview.inactiveWarehouseCount, $overview.itemPositionCount, $overview.openTransferCount, $overview.totalInTransit, $overview.negativeBalanceCount)
    Write-Host ('Business date: {0}' -f $today.ToString('yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture))
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
