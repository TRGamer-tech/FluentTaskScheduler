# NOTE TO MAINTAINER:
# Replace <SHA256_OF_Setup-x64.msi> with the real SHA-256 hash before packing.
# Compute with: (Get-FileHash "Setup-x64.msi" -Algorithm SHA256).Hash

$ErrorActionPreference = 'Stop'

$packageName   = 'fluenttaskscheduler'
$toolsDir      = "$(Split-Path -parent $MyInvocation.MyCommand.Definition)"
$version       = '1.8.1'

# Detect architecture
$isArm64 = ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') -or ($env:PROCESSOR_ARCHITEW6432 -eq 'ARM64')

# Use correct 'V' prefix for GitHub release tag
$pkgUrl = "https://github.com/TRGamer-tech/FluentTaskScheduler/releases/download/V$version/Setup-x64.msi"
$pkgHash = 'A093637F54D59D5BCE0804FAD524A22F2093934BE683E2A19060007112459D5B'

if ($isArm64) {
    $pkgUrl = "https://github.com/TRGamer-tech/FluentTaskScheduler/releases/download/V$version/Setup-arm64.msi"
    $pkgHash = 'A3A452A30ACDB5EBCFAFAF0BAAE16569C5F34BC0E8C1E8D7E726B1CC57C2BE86'
}

$packageArgs = @{
  packageName    = $packageName
  fileType       = 'msi'
  url64bit       = $pkgUrl
  checksum64     = $pkgHash
  checksumType64 = 'sha256'
  silentArgs     = '/qn /norestart'
  validExitCodes = @(0, 3010, 1641)
}

Install-ChocolateyPackage @packageArgs
