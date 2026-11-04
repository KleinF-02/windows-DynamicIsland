[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [switch]$Demo,
    [switch]$Settings,
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

if (-not $NoBuild) {
    & $script:Dotnet build $script:Solution -c $Configuration --nologo
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

$arguments = @('run', '--project', $script:AppProject, '-c', $Configuration, '--no-build')
if ($Demo -or $Settings) {
    $arguments += '--'
    if ($Demo) { $arguments += '--demo' }
    if ($Settings) { $arguments += '--settings' }
}

& $script:Dotnet @arguments
exit $LASTEXITCODE
