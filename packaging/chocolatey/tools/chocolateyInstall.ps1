$ErrorActionPreference = 'Stop'

$packageName   = 'fluenttaskscheduler'
$toolsDir      = "$(Split-Path -parent $MyInvocation.MyCommand.Definition)"
$version       = '2.0.0'

$isArm64 = ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') -or ($env:PROCESSOR_ARCHITEW6432 -eq 'ARM64')

$pkgUrl = "https://github.com/TRGamer-tech/FluentTaskScheduler/releases/download/V$version/Setup-x64.msi"
$pkgHash = '852F1F51B8F250B94C0A1D458F43E03B0E9FFB3883B50286BCEFDE2F5AFD83C4'

if ($isArm64) {
    $pkgUrl = "https://github.com/TRGamer-tech/FluentTaskScheduler/releases/download/V$version/Setup-arm64.msi"
    $pkgHash = '4AE2C3E159321EE16947167198BBB4344FC44DCA45EC964CC53159831B16C812'
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
