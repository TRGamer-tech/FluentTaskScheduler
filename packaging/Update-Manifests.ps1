<#
.SYNOPSIS
Writes a release into the manifests for winget, Scoop and Chocolatey in this folder.

.DESCRIPTION
Puts in the version, the links to the release, the SHA-256 of the four downloads and the product
codes of the two installers. Everything else in the manifests stays as it is, byte for byte. If
one of these values is not where it is expected, no manifest is changed.

The release workflow runs this on the files it has built and keeps the result as the artifact
"package-manifests". Reading the product codes needs Windows. The script keeps to what Windows
PowerShell 5.1 and PowerShell 7 both understand.

.EXAMPLE
gh release download V2.0.0 --dir downloads --pattern "Setup-*.msi" --pattern "Portable-*.zip"
.\packaging\Update-Manifests.ps1 -Version 2.0.0 -Downloads downloads

The same by hand, for a release that is published.
#>
[CmdletBinding()]
param(
    # The version of the release, such as 2.0.0.
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $Version,

    # The folder with Setup-x64.msi, Setup-arm64.msi, Portable-x64.zip and Portable-arm64.zip.
    [Parameter(Mandatory = $true)]
    [string] $Downloads
)

$ErrorActionPreference = 'Stop'

# Every build of an installer has a product code of its own. winget tells by it which installed
# app a package belongs to.
function Get-ProductCode([string] $Msi) {
    # Windows PowerShell does not see the members of Windows Installer's objects, so they are
    # called by name.
    function Invoke-Member($Object, [string] $Name, [System.Reflection.BindingFlags] $Kind, [object[]] $Arguments) {
        $Object.GetType().InvokeMember($Name, $Kind, $null, $Object, $Arguments)
    }

    $windowsInstaller = $database = $view = $record = $null
    try {
        $windowsInstaller = New-Object -ComObject WindowsInstaller.Installer
        $database = Invoke-Member $windowsInstaller 'OpenDatabase' 'InvokeMethod' @($Msi, 0)
        $view = Invoke-Member $database 'OpenView' 'InvokeMethod' @("SELECT Value FROM Property WHERE Property = 'ProductCode'")
        Invoke-Member $view 'Execute' 'InvokeMethod' $null | Out-Null
        $record = Invoke-Member $view 'Fetch' 'InvokeMethod' $null
        $code = if ($record) { Invoke-Member $record 'StringData' 'GetProperty' @(1) }
    }
    finally {
        # The database keeps the file open for as long as it lives.
        foreach ($object in $record, $view, $database, $windowsInstaller) {
            if ($object) { [System.Runtime.InteropServices.Marshal]::ReleaseComObject($object) | Out-Null }
        }
    }

    if ("$code" -notmatch '^\{[0-9A-F]{8}(-[0-9A-F]{4}){3}-[0-9A-F]{12}\}$') {
        throw "No product code was found in $Msi."
    }
    $code
}

$installers = 'Setup-x64.msi', 'Setup-arm64.msi'
$portables = 'Portable-x64.zip', 'Portable-arm64.zip'

$hash = @{}
$productCode = @{}
foreach ($download in $installers + $portables) {
    $file = (Resolve-Path -LiteralPath (Join-Path $Downloads $download)).ProviderPath
    $hash[$download] = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
    if ($installers -contains $download) { $productCode[$download] = Get-ProductCode $file }
}

$winget = 'winget/TRGamer-tech.FluentTaskScheduler'
$wingetInstaller = "$winget.installer.yaml"
$wingetLocale = "$winget.locale.en-US.yaml"
$scoop = 'scoop/fluenttaskscheduler.json'
$nuspec = 'chocolatey/fluenttaskscheduler.nuspec'
$chocolateyInstall = 'chocolatey/tools/chocolateyInstall.ps1'

# Read and written one byte per character: whatever encoding and line ends a manifest has, the
# bytes that are not replaced here stay what they are.
$bytewise = [System.Text.Encoding]::GetEncoding(28591)
$text = @{}
foreach ($manifest in "$winget.yaml", $wingetLocale, $wingetInstaller, $scoop, $nuspec, $chocolateyInstall) {
    $text[$manifest] = $bytewise.GetString([System.IO.File]::ReadAllBytes((Join-Path $PSScriptRoot $manifest)))
}

# Puts a value in the place a pattern describes. The place has to be there exactly as often as
# expected: a manifest that changed its shape is not to be half updated.
function Set-Value([string] $Manifest, [string] $Pattern, [string] $Value, [int] $Times = 1) {
    $place = New-Object System.Text.RegularExpressions.Regex($Pattern, 'Multiline')
    $found = $place.Matches($text[$Manifest]).Count
    if ($found -ne $Times) {
        throw "packaging/$Manifest should have $Times place(s) for '$Value' and has $found (looked for: $Pattern). No manifest was changed."
    }
    $text[$Manifest] = $place.Replace($text[$Manifest], $Value)
}

# In every manifest the SHA-256 of a download stands on the line after the link to it. It is
# written in the case the manifest has used so far: Scoop's is lower case, the others are upper.
function Set-Hash([string] $Manifest, [string] $Download) {
    $pattern = '(?<=/' + [regex]::Escape($Download) + '["'',]*\r?\n[^\r\n]*?)\b[0-9A-Fa-f]{64}\b'
    $current = [regex]::Match($text[$Manifest], $pattern).Value
    $value = if ($current -ceq $current.ToLowerInvariant()) { $hash[$Download].ToLowerInvariant() } else { $hash[$Download] }
    Set-Value $Manifest $pattern $value
}

$anyVersion = '\d+\.\d+\.\d+'
$inDownloadLink = '(?<=/releases/download/V)' + $anyVersion
$inReleaseLink = '(?<=/releases/tag/V)' + $anyVersion

foreach ($manifest in "$winget.yaml", $wingetLocale, $wingetInstaller) {
    Set-Value $manifest ('(?<=^PackageVersion: )' + $anyVersion) $Version
}
Set-Value $wingetLocale $inReleaseLink $Version
Set-Value $wingetInstaller $inDownloadLink $Version -Times 2
foreach ($installer in $installers) {
    Set-Hash $wingetInstaller $installer
    $afterTheHash = '(?<=/' + [regex]::Escape($installer) + '\r?\n[^\r\n]*\r?\n\s*ProductCode: ")\{[0-9A-Fa-f-]{36}\}'
    Set-Value $wingetInstaller $afterTheHash $productCode[$installer]
}

Set-Value $scoop ('(?<="version": ")' + $anyVersion) $Version
Set-Value $scoop $inDownloadLink $Version -Times 2
foreach ($portable in $portables) { Set-Hash $scoop $portable }

Set-Value $nuspec ('(?<=<version>)' + $anyVersion + '(?=</version>)') $Version
Set-Value $nuspec $inReleaseLink $Version
Set-Value $chocolateyInstall ('(?<=^\$version\s*=\s*'')' + $anyVersion) $Version
foreach ($installer in $installers) { Set-Hash $chocolateyInstall $installer }

foreach ($manifest in $text.Keys) {
    [System.IO.File]::WriteAllBytes((Join-Path $PSScriptRoot $manifest), $bytewise.GetBytes($text[$manifest]))
}

"The manifests in $PSScriptRoot describe version $Version now:"
foreach ($download in $installers + $portables) {
    ('  {0,-18} {1}  {2}' -f $download, $hash[$download], $productCode[$download]).TrimEnd()
}
