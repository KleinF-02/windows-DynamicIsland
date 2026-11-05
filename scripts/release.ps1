[CmdletBinding()]
param(
    [string]$Version = '',
    [switch]$SkipTests,
    [switch]$Push
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = (Get-Content (Join-Path $script:RepoRoot 'VERSION') -Raw).Trim()
}

if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$') {
    throw "Invalid semantic version: $Version"
}

$versionFile = (Get-Content (Join-Path $script:RepoRoot 'VERSION') -Raw).Trim()
if ($versionFile -ne $Version) {
    throw "VERSION is $versionFile but release version is $Version. Update VERSION and Directory.Build.props first."
}

$tag = "v$Version"
if ((git tag --list $tag) -eq $tag) {
    throw "Tag $tag already exists."
}

$dirty = git status --porcelain
if ($dirty) {
    throw "Working tree is not clean:`n$dirty`nCommit the release changes first."
}

if (-not $SkipTests) {
    & $script:Dotnet test $script:Solution -c Release --nologo
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

& (Join-Path $PSScriptRoot 'publish.ps1')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

git tag -a $tag -m "Dynamic Island $tag"
Write-Host "Created annotated tag $tag." -ForegroundColor Green

if ($Push) {
    $remotes = git remote
    if (($remotes -split "`n") -notcontains 'origin') {
        throw 'No origin remote is configured. Add it with: git remote add origin <repository-url>'
    }

    git push origin main
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    git push origin $tag
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

Write-Host ''
Write-Host "Next:"
Write-Host "  git push origin main"
Write-Host "  git push origin $tag"
Write-Host "GitHub Actions will create the release from the v* tag." -ForegroundColor Cyan
