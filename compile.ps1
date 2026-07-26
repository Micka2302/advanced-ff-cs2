#!/usr/bin/env pwsh
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

$project = Join-Path $root 'advanced_friendlyfire.csproj'
$buildOutput = Join-Path $root 'bin/Release/net10.0'
$compiledRoot = Join-Path $root 'compiled'
$pluginName = 'advanced_friendlyfire'
$pluginTarget = Join-Path $compiledRoot "addons/counterstrikesharp/plugins/$pluginName"
$pluginDll = Join-Path $buildOutput "$pluginName.dll"

# Recreate the staging directory used to assemble the installable package.
Remove-Item -Recurse -Force $compiledRoot -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $pluginTarget -Force | Out-Null

dotnet restore $project
dotnet build $project -c Release --no-restore --nologo

if (-not (Test-Path -LiteralPath $pluginDll -PathType Leaf)) {
    throw "Plugin DLL not found at $pluginDll"
}

# Stage the plugin binaries and dependency metadata.
Copy-Item -Path (Join-Path $buildOutput '*') -Destination $pluginTarget -Recurse -Force

# CounterStrikeSharp is supplied by the game server and must not be bundled.
$cssApi = Join-Path $pluginTarget 'CounterStrikeSharp.API.dll'
if (Test-Path -LiteralPath $cssApi -PathType Leaf) {
    Remove-Item -LiteralPath $cssApi -Force
}

# If a future dependency adds native runtimes, retain only server platforms.
$runtimeDir = Join-Path $pluginTarget 'runtimes'
if (Test-Path -LiteralPath $runtimeDir -PathType Container) {
    $keep = @('linux-x64', 'win-x64')
    Get-ChildItem $runtimeDir -Directory | Where-Object { $keep -notcontains $_.Name } | Remove-Item -Recurse -Force
}

# Preserve the addons/ tree so the archive can be extracted at the CS2 root.
$zipPath = Join-Path $compiledRoot "$pluginName.zip"
Compress-Archive -Path (Join-Path $compiledRoot 'addons') -DestinationPath $zipPath -Force

Write-Host "[OK] Build finished."
Write-Host " - DLL:    $pluginDll"
Write-Host " - Folder: $pluginTarget"
Write-Host " - Zip:    $zipPath"
