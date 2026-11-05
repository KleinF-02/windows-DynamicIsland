[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$RepositoryUrl,
    [switch]$PushTags
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

if ($RepositoryUrl -notmatch '^(https://|git@)') {
    throw 'RepositoryUrl must be an HTTPS or SSH Git URL.'
}

$origin = (git remote) -split "`n"
if ($origin -contains 'origin') {
    git remote set-url origin $RepositoryUrl
} else {
    git remote add origin $RepositoryUrl
}

git push -u origin main
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if ($PushTags) {
    git push origin --tags
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

Write-Host "Pushed main to $RepositoryUrl" -ForegroundColor Green
if ($PushTags) {
    Write-Host 'Pushed all version tags.' -ForegroundColor Green
}
