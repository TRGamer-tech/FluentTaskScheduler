$ErrorActionPreference = 'Stop'

$packageName   = 'fluenttaskscheduler'
$toolsDir      = "$(Split-Path -parent $MyInvocation.MyCommand.Definition)"
$version       = '1.9.0'

$isArm64 = ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') -or ($env:PROCESSOR_ARCHITEW6432 -eq 'ARM64')

$pkgUrl = "https://github.com/TRGamer-tech/FluentTaskScheduler/releases/download/V$version/Setup-x64.msi"
$pkgHash = 'AFAD2A1E61E4B3F87C1EE2B7ED4E55CFAC9B13DBC5AF7CA99745DA4977539362'

if ($isArm64) {
    $pkgUrl = "https://github.com/TRGamer-tech/FluentTaskScheduler/releases/download/V$version/Setup-arm64.msi"
    $pkgHash = '8CDF80B8DAD1D75F5D107CEFB5A3626DBC3FB627911C978641AF51771D2A54D1'
}

# Chocolatey's helpers take one installer for 32-bit and one for 64-bit Windows, and none for
# ARM64, so the 64-bit slot carries whichever installer was chosen above.
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
