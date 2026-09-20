# Copyright (c) 2026 Koichi Kobayashi
# Licensed under the MIT License.

[CmdletBinding()]
param(
    [string]$PropsPath = (Join-Path $PSScriptRoot '..\Directory.Build.props'),
    [string]$ManifestPath = (Join-Path $PSScriptRoot '..\WindowsServicesSearch\Package.appxmanifest')
)

$ErrorActionPreference = 'Stop'

[xml]$buildProps = Get-Content -LiteralPath $PropsPath
$version = $buildProps.Project.PropertyGroup.AppxPackageVersion | Select-Object -First 1

if ([string]::IsNullOrWhiteSpace($version)) {
    throw "AppxPackageVersion was not found in $PropsPath."
}

$version = $version.Trim()
if ($version -notmatch '^(6553[0-5]|655[0-2]\d|65[0-4]\d{2}|6[0-4]\d{3}|[0-5]?\d{0,4})\.(6553[0-5]|655[0-2]\d|65[0-4]\d{2}|6[0-4]\d{3}|[0-5]?\d{0,4})\.(6553[0-5]|655[0-2]\d|65[0-4]\d{2}|6[0-4]\d{3}|[0-5]?\d{0,4})\.0$') {
    throw "AppxPackageVersion must use Store-compatible major.minor.build.0 notation: $version"
}

$manifest = [System.IO.File]::ReadAllText($ManifestPath)
$identityVersionPattern = '(<Identity\b[\s\S]*?\bVersion=")[^"]+("\s*/>)'
$identityVersionMatch = [regex]::Match(
    $manifest,
    $identityVersionPattern,
    [System.Text.RegularExpressions.RegexOptions]::CultureInvariant)

if (-not $identityVersionMatch.Success) {
    throw "The package identity version was not found in $ManifestPath."
}

$updatedManifest = [regex]::Replace(
    $manifest,
    $identityVersionPattern,
    "`${1}$version`${2}",
    [System.Text.RegularExpressions.RegexOptions]::CultureInvariant)

[System.IO.File]::WriteAllText(
    $ManifestPath,
    $updatedManifest,
    [System.Text.UTF8Encoding]::new($false))

Write-Host "Package.appxmanifest version synchronized to $version."
