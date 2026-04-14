# NOTE TO MAINTAINER:
# Replace <SHA256_OF_Setup-x64.msi> with the real SHA-256 hash before packing.
# Compute with: (Get-FileHash "Setup-x64.msi" -Algorithm SHA256).Hash

$ErrorActionPreference = 'Stop'

$packageName   = 'fluenttaskscheduler'
$toolsDir      = "$(Split-Path -parent $MyInvocation.MyCommand.Definition)"
$version       = '1.8.0'
$url64         = "https://github.com/TRGamer-tech/FluentTaskScheduler/releases/download/v$version/Setup-x64.msi"
$checksum64    = '8FFB4D207528D4D80F6178CA9A4EC3B0445A43B442E36C6893170CB1D8E30106'

$packageArgs = @{
  packageName    = $packageName
  fileType       = 'msi'
  url64bit       = $url64
  checksum64     = $checksum64
  checksumType64 = 'sha256'
  silentArgs     = '/qn /norestart'
  validExitCodes = @(0, 3010, 1641)
}

Install-ChocolateyPackage @packageArgs
