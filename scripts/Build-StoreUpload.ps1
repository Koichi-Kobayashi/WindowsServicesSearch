# Copyright (c) 2026 Koichi Kobayashi
# Licensed under the MIT License.

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$propsPath = Join-Path $repositoryRoot 'Directory.Build.props'
$projectPath = Join-Path $repositoryRoot 'WindowsServicesSearch\WindowsServicesSearch.csproj'

& (Join-Path $PSScriptRoot 'Sync-PackageVersion.ps1')

[xml]$buildProps = Get-Content -LiteralPath $propsPath -Raw
$versionNode = $buildProps.SelectSingleNode('/Project/PropertyGroup/AppxPackageVersion')
if ($null -eq $versionNode -or [string]::IsNullOrWhiteSpace($versionNode.InnerText)) {
    throw "AppxPackageVersion was not found in $propsPath."
}

$version = $versionNode.InnerText.Trim()

dotnet build $projectPath `
    --configuration Release `
    -p:Platform=x64 `
    -p:PublishTrimmed=false `
    -p:GenerateAppxPackageOnBuild=true `
    -p:AppxPackageSigningEnabled=false `
    -p:AppxPackageVersion=$version `
    -p:AppxBundle=Always `
    '-p:AppxBundlePlatforms=x64|ARM64' `
    -p:UapAppxPackageBuildMode=StoreUpload `
    -p:AppxPackageDir=AppPackages\StoreUpload\

if ($LASTEXITCODE -ne 0) {
    throw 'Store upload package build failed.'
}

Get-ChildItem -Path (Join-Path $repositoryRoot 'WindowsServicesSearch\AppPackages\StoreUpload') `
    -Filter "*_$($version)_*_bundle.msixupload" |
    Select-Object FullName, Length, LastWriteTime
