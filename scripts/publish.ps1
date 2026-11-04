[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64',
    [switch]$FrameworkDependent,
    [switch]$NoZip
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

$version = '0.1.0'
$selfContained = -not $FrameworkDependent
$selfContainedValue = if ($selfContained) { 'true' } else { 'false' }
$suffix = if ($FrameworkDependent) { '-fdd' } else { '' }
$output = Join-Path $script:RepoRoot "artifacts\publish\DynamicIsland-$version-$Runtime$suffix"
$artifactsRoot = Join-Path $script:RepoRoot 'artifacts\publish'

if (Test-Path -LiteralPath $output) {
    $resolved = (Resolve-Path -LiteralPath $output).Path
    if (-not $resolved.StartsWith($artifactsRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove an unexpected path: $resolved"
    }

    Remove-Item -LiteralPath $resolved -Recurse -Force
}

$arguments = @(
    'publish', $script:AppProject,
    '-c', 'Release',
    '-r', $Runtime,
    '--self-contained', $selfContainedValue,
    '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:DebugType=None',
    '-p:DebugSymbols=false',
    '-o', $output,
    '--nologo'
)

& $script:Dotnet @arguments
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if (-not $NoZip) {
    $zip = "$output.zip"
    if (Test-Path -LiteralPath $zip) {
        Remove-Item -LiteralPath $zip -Force
    }

    Compress-Archive -Path (Join-Path $output '*') -DestinationPath $zip -Force
    Write-Host "Archive: $zip" -ForegroundColor Green
}

Write-Host "Published to: $output" -ForegroundColor Green
Write-Host 'The self-contained build can be launched on a Windows 11 machine without installing .NET.'
