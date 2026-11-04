[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

$arguments = @('build', $script:Solution, '-c', $Configuration, '--nologo')
if ($NoRestore) {
    $arguments += '--no-restore'
}

& $script:Dotnet @arguments
exit $LASTEXITCODE
