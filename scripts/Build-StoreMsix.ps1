# Copyright (c) 2026 Koichi Kobayashi
# Licensed under the MIT License.

[CmdletBinding()]
param(
    [ValidateSet('x64', 'ARM64')]
    [string[]]$Platform = @('x64', 'ARM64')
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot 'WindowsServicesSearch\WindowsServicesSearch.csproj'

& (Join-Path $PSScriptRoot 'Sync-PackageVersion.ps1')

foreach ($targetPlatform in $Platform) {
    $packageDirectory = "AppPackages\$targetPlatform\"

    dotnet build $projectPath `
        --configuration Release `
        -p:Platform=$targetPlatform `
        -p:PublishTrimmed=false `
        -p:GenerateAppxPackageOnBuild=true `
        -p:AppxPackageSigningEnabled=false `
        -p:AppxPackageDir=$packageDirectory

    if ($LASTEXITCODE -ne 0) {
        throw "MSIX build failed for $targetPlatform."
    }
}

Get-ChildItem -Path (Join-Path $repositoryRoot 'WindowsServicesSearch\AppPackages') `
    -Recurse -Filter '*.msix' |
    Select-Object FullName, Length, LastWriteTime
