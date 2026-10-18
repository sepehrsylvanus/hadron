[CmdletBinding()]
param(
    [ValidateSet('Enable', 'Status', 'Disable')]
    [string]$Action = 'Status',

    [string]$HostAddress = '192.168.0.175',

    [string]$RemoteSubnet = '192.168.0.0/24'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$hadronRuleName = 'Hadron ERP Local Frontend (Company LAN)'
$hadronLiveKitSignalingRuleName = 'Hadron ERP LiveKit Signaling (Company LAN)'
$hadronLiveKitMediaRuleName = 'Hadron ERP LiveKit Media (Company LAN)'
$hadronNodeExecutable = Join-Path (Join-Path $env:ProgramFiles 'nodejs') 'node.exe'

function Test-HadronAdministrator {
    $hadronIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $hadronPrincipal = [Security.Principal.WindowsPrincipal]::new($hadronIdentity)
    return $hadronPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-HadronFirewallProfile {
    $hadronAddress = Get-NetIPAddress -AddressFamily IPv4 -IPAddress $HostAddress -ErrorAction Stop |
        Select-Object -First 1
    $hadronConnection = Get-NetConnectionProfile -InterfaceIndex $hadronAddress.InterfaceIndex -ErrorAction Stop
    switch ([string]$hadronConnection.NetworkCategory) {
        'DomainAuthenticated' { return 'Domain' }
        'Private' { return 'Private' }
        'Public' { return 'Public' }
        default { throw "Unsupported Windows network category: $($hadronConnection.NetworkCategory)" }
    }
}

function Show-HadronLanStatus {
    foreach ($hadronExpectedRule in @($hadronRuleName, $hadronLiveKitSignalingRuleName, $hadronLiveKitMediaRuleName)) {
        $hadronRule = Get-NetFirewallRule -DisplayName $hadronExpectedRule -ErrorAction SilentlyContinue
        if ($null -eq $hadronRule) {
            [pscustomobject]@{
                Rule = $hadronExpectedRule
                State = 'NotConfigured'
                Url = "http://${HostAddress}:5173/login"
            } | Format-List
            continue
        }

        $hadronPort = Get-NetFirewallPortFilter -AssociatedNetFirewallRule $hadronRule
        $hadronAddress = Get-NetFirewallAddressFilter -AssociatedNetFirewallRule $hadronRule
        $hadronApplication = Get-NetFirewallApplicationFilter -AssociatedNetFirewallRule $hadronRule
        [pscustomobject]@{
            Rule = $hadronRule.DisplayName
            Enabled = $hadronRule.Enabled
            Profile = $hadronRule.Profile
            Action = $hadronRule.Action
            Protocol = $hadronPort.Protocol
            LocalPort = $hadronPort.LocalPort
            LocalAddress = $hadronAddress.LocalAddress -join ','
            RemoteAddress = $hadronAddress.RemoteAddress -join ','
            Program = $hadronApplication.Program
            Url = "http://${HostAddress}:5173/login"
        } | Format-List
    }
}

switch ($Action) {
    'Enable' {
        if (-not (Test-HadronAdministrator)) {
            throw 'Administrator privileges are required to configure the Windows Firewall rule.'
        }
        if (-not (Test-Path -LiteralPath $hadronNodeExecutable)) {
            throw "Node.js executable is missing: $hadronNodeExecutable"
        }

        $hadronProfile = Get-HadronFirewallProfile
        $hadronRules = @(Get-NetFirewallRule -DisplayName $hadronRuleName -ErrorAction SilentlyContinue)
        if ($hadronRules.Count -gt 1) {
            throw "More than one firewall rule is named '$hadronRuleName'; no changes were made."
        }

        if ($hadronRules.Count -eq 0) {
            New-NetFirewallRule `
                -DisplayName $hadronRuleName `
                -Description 'Hadron ERP frontend access restricted to the approved company LAN; API and data services remain loopback-only.' `
                -Enabled True `
                -Direction Inbound `
                -Action Allow `
                -Protocol TCP `
                -LocalPort 5173 `
                -LocalAddress $HostAddress `
                -RemoteAddress $RemoteSubnet `
                -Program $hadronNodeExecutable `
                -Profile $hadronProfile | Out-Null
        }
        else {
            $hadronRule = $hadronRules[0]
            Set-NetFirewallRule -InputObject $hadronRule -Enabled True -Direction Inbound -Action Allow `
                -Profile $hadronProfile `
                -Description 'Hadron ERP frontend access restricted to the approved company LAN; API and data services remain loopback-only.' | Out-Null
            Get-NetFirewallPortFilter -AssociatedNetFirewallRule $hadronRule |
                Set-NetFirewallPortFilter -Protocol TCP -LocalPort 5173 | Out-Null
            Get-NetFirewallAddressFilter -AssociatedNetFirewallRule $hadronRule |
                Set-NetFirewallAddressFilter -LocalAddress $HostAddress -RemoteAddress $RemoteSubnet | Out-Null
            Get-NetFirewallApplicationFilter -AssociatedNetFirewallRule $hadronRule |
                Set-NetFirewallApplicationFilter -Program $hadronNodeExecutable | Out-Null
        }

        $hadronLiveKitRules = @(
            @{ Name = $hadronLiveKitSignalingRuleName; Protocol = 'TCP'; Port = '7880,7881'; Description = 'Hadron ERP LiveKit signaling and TCP fallback restricted to the approved company LAN.' },
            @{ Name = $hadronLiveKitMediaRuleName; Protocol = 'UDP'; Port = '50000-50100'; Description = 'Hadron ERP LiveKit media traffic restricted to the approved company LAN.' }
        )
        foreach ($hadronLiveKitRuleDefinition in $hadronLiveKitRules) {
            $hadronRules = @(Get-NetFirewallRule -DisplayName $hadronLiveKitRuleDefinition.Name -ErrorAction SilentlyContinue)
            if ($hadronRules.Count -gt 1) {
                throw "More than one firewall rule is named '$($hadronLiveKitRuleDefinition.Name)'; no changes were made to that rule."
            }
            if ($hadronRules.Count -eq 0) {
                New-NetFirewallRule `
                    -DisplayName $hadronLiveKitRuleDefinition.Name `
                    -Description $hadronLiveKitRuleDefinition.Description `
                    -Enabled True `
                    -Direction Inbound `
                    -Action Allow `
                    -Protocol $hadronLiveKitRuleDefinition.Protocol `
                    -LocalPort $hadronLiveKitRuleDefinition.Port `
                    -LocalAddress $HostAddress `
                    -RemoteAddress $RemoteSubnet `
                    -Profile $hadronProfile | Out-Null
            }
            else {
                $hadronLiveKitRule = $hadronRules[0]
                Set-NetFirewallRule -InputObject $hadronLiveKitRule -Enabled True -Direction Inbound -Action Allow `
                    -Profile $hadronProfile -Description $hadronLiveKitRuleDefinition.Description | Out-Null
                Get-NetFirewallPortFilter -AssociatedNetFirewallRule $hadronLiveKitRule |
                    Set-NetFirewallPortFilter -Protocol $hadronLiveKitRuleDefinition.Protocol -LocalPort $hadronLiveKitRuleDefinition.Port | Out-Null
                Get-NetFirewallAddressFilter -AssociatedNetFirewallRule $hadronLiveKitRule |
                    Set-NetFirewallAddressFilter -LocalAddress $HostAddress -RemoteAddress $RemoteSubnet | Out-Null
            }
        }

        Show-HadronLanStatus
    }
    'Status' {
        Show-HadronLanStatus
    }
    'Disable' {
        if (-not (Test-HadronAdministrator)) {
            throw 'Administrator privileges are required to remove the Windows Firewall rule.'
        }
        foreach ($hadronRuleToRemove in @($hadronRuleName, $hadronLiveKitSignalingRuleName, $hadronLiveKitMediaRuleName)) {
            Get-NetFirewallRule -DisplayName $hadronRuleToRemove -ErrorAction SilentlyContinue |
                Remove-NetFirewallRule
        }
        Show-HadronLanStatus
    }
}
