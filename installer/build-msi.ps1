<#
.SYNOPSIS
    Publishes a self-contained build of SSH Tunneler and packs it into a per-user MSI.

.DESCRIPTION
    Requires the WiX command line tool:  dotnet tool install --global wix
    The resulting installer needs no admin rights and no .NET runtime on the target machine.

.EXAMPLE
    pwsh installer/build-msi.ps1 -Version 1.0.0
#>
[CmdletBinding()]
param(
    [string] $Version = "1.0.0",
    [string] $Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $root "artifacts\publish"
$outputDir = Join-Path $root "artifacts"
$msiPath = Join-Path $outputDir "SshTunneler-$Version-$Runtime.msi"

if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
New-Item -ItemType Directory -Path $publishDir -Force | Out-Null

Write-Host "Publishing $Runtime self-contained build..." -ForegroundColor Cyan
dotnet publish (Join-Path $root "src\SshTunneler\SshTunneler.csproj") `
    -c Release -r $Runtime --self-contained true `
    -p:Version=$Version -p:DebugType=none `
    -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

Write-Host "Building MSI..." -ForegroundColor Cyan
wix build (Join-Path $PSScriptRoot "SshTunneler.wxs") `
    -arch x64 `
    -d "Version=$Version" `
    -d "PublishDir=$publishDir" `
    -o $msiPath
if ($LASTEXITCODE -ne 0) { throw "wix build failed" }

$size = [math]::Round((Get-Item $msiPath).Length / 1MB, 1)
Write-Host "Done: $msiPath ($size MB)" -ForegroundColor Green
