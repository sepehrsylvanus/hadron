# Run with Windows PowerShell 5.1: powershell -NoProfile -File tests/windows/Seed-HadronInventory.Tests.ps1
$ErrorActionPreference = 'Stop'
$source = Join-Path $PSScriptRoot '../../scripts/windows/Seed-HadronInventory.ps1'
$tokens = $null; $errors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile((Resolve-Path $source), [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw ($errors | Out-String) }
foreach ($name in @('Assert-SeedGuid', 'Get-Collection', 'Get-PagedCollection', 'Invoke-Seed')) {
    $function = $ast.Find({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name }, $true)
    if (-not $function) { throw "Missing function $name" }
    Invoke-Expression $function.Extent.Text
}
function Get-ApiRoot { 'http://test' }
function Ensure-Csrf { }
$UserName = 'test'; $Password = 'test'; $ApiBaseUrl = 'http://test'
$unitId = [Guid]::NewGuid().ToString(); $itemId = [Guid]::NewGuid().ToString()
$script:warehouses = [System.Collections.ArrayList]::new()
$script:transfers = [System.Collections.ArrayList]::new()
$script:createdTransfers = 0
function Invoke-Api {
    param([string]$Method, [string]$Path, $Body)
    if ($Path -like '/api/auth/*') { return [pscustomobject]@{} }
    if ($Path -eq '/api/reference-data/units?includeInactive=true') { return [pscustomobject]@{ id=$unitId; code='PCE'; isActive=$true } }
    if ($Path -like '/api/products?*') { return [pscustomobject]@{ items=@([pscustomobject]@{ id=$itemId; nameEn='Test item'; isActive=$true }); totalCount=1 } }
    if ($Path -eq '/api/inventory/overview') { return [pscustomobject]@{ asOfDate='2026-11-16'; itemPositionCount=1; negativeBalanceCount=0 } }
    if ($Path -like '/api/inventory/responsible-users*') { return [pscustomobject]@{ items=@([pscustomobject]@{ id=[Guid]::NewGuid().ToString(); isActive=$true }) } }
    if ($Method -eq 'POST' -and $Path -eq '/api/inventory/warehouses') {
        $row = [pscustomobject]@{ id=[Guid]::NewGuid().ToString(); nameEn=$Body.nameEn; isActive=$true }
        [void]$script:warehouses.Add($row); return $row
    }
    if ($Method -eq 'POST' -and $Path -eq '/api/inventory/transfers') {
        $row = [pscustomobject]@{ id=[Guid]::NewGuid().ToString(); reason=$Body.reason; status='DRAFT'; receipts=@() }
        [void]$script:transfers.Add($row); $script:createdTransfers++; return $row
    }
    if ($Path -like '/api/inventory/transactions?*') { return [pscustomobject]@{ items=@(); totalCount=0 } }
    if ($Path -like '/api/inventory/warehouses?*' -or $Path -like '/api/inventory/transfers?*') {
        if ($Path -notmatch '[?&]page=(\d+)&pageSize=100') { throw "Invalid paging: $Path" }
        $page = [int]$Matches[1]
        $all = @(if ($Path -like '/api/inventory/warehouses?*') { $script:warehouses.ToArray() } else { $script:transfers.ToArray() })
        $rows = @($all | Select-Object -Skip (($page - 1) * 100) -First 100)
        return [pscustomobject]@{ items=$rows; totalCount=$all.Count }
    }
    throw "Unexpected API call: $Method $Path"
}
$dataset = @{
    units=@(@{code='PCE'}); referenceValues=@();
    products=@(@{key='P1'; nameEn='Test item'}); parts=@(); components=@();
    warehouses=@(@{key='W1'; nameEn='W1'; isActive=$true}, @{key='W2'; nameEn='W2'; isActive=$true});
    unitConversions=@(); transactions=@(); reversals=@();
    transfers=@(
        @{seedTag='[INV-SEED:T1]'; itemKey='P1'; sourceWarehouseKey='W1'; destinationWarehouseKey='W2'; transactionUnitCode='PCE'; quantity=1; reason='[INV-SEED:T1]'; receipts=@()},
        @{seedTag='[INV-SEED:T2]'; itemKey='P1'; sourceWarehouseKey='W1'; destinationWarehouseKey='W2'; transactionUnitCode='PCE'; quantity=1; reason='[INV-SEED:T2]'; receipts=@()}
    )
}
$DataFile = [System.IO.Path]::GetTempFileName()
try {
    [System.IO.File]::WriteAllText($DataFile, (ConvertTo-Json -InputObject $dataset -Depth 20), [System.Text.Encoding]::UTF8)
    # Initially zero transfers and warehouses: exercise appending the first and second rows.
    Invoke-Seed
    if ($script:createdTransfers -ne 2 -or $script:warehouses.Count -ne 2) { throw 'First run did not create exactly two transfers and warehouses.' }
    Invoke-Seed
    if ($script:createdTransfers -ne 2 -or $script:warehouses.Count -ne 2) { throw 'Rerun duplicated demo records.' }
    # Move the existing demo transfers beyond page one, then ensure they are still reused.
    for ($i = 0; $i -lt 101; $i++) { $script:transfers.Insert(0, [pscustomobject]@{ reason="unrelated-$i" }) }
    Invoke-Seed
    if ($script:createdTransfers -ne 2) { throw 'Rerun duplicated a transfer outside the first page.' }
    Write-Host 'Inventory seed collection and resume tests passed.'
} finally { Remove-Item $DataFile -Force }
