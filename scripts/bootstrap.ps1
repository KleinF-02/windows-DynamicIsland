[CmdletBinding()]
param(
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$localRoot = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet'
$localDotnet = Join-Path $localRoot 'dotnet.exe'
$dotnet = $null
$usingLocal = $false

if (Test-Path -LiteralPath $localDotnet) {
    $dotnet = $localDotnet
    $usingLocal = $true
}
else {
    $command = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        $sdks = & $command.Source --list-sdks 2>$null
        if (($sdks -join "`n") -match '(?m)^10\.') {
            $dotnet = $command.Source
        }
    }
}

if ($null -eq $dotnet) {
    Write-Host 'Installing the .NET 10 SDK for the current user...' -ForegroundColor Cyan
    $installer = Join-Path $env:TEMP 'dotnet-install.ps1'
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer -UseBasicParsing
    Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force -ErrorAction SilentlyContinue
    & $installer -Channel 10.0 -InstallDir $localRoot -NoPath
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $localDotnet)) {
        throw 'The .NET SDK installation failed.'
    }

    $dotnet = $localDotnet
    $usingLocal = $true
}

if ($usingLocal) {
    $env:DOTNET_ROOT = $localRoot
    if (($env:Path -split ';') -notcontains $localRoot) {
        $env:Path = "$localRoot;$env:Path"
    }

    $userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
    if (($userPath -split ';') -notcontains $localRoot) {
        $newUserPath = if ([string]::IsNullOrWhiteSpace($userPath)) { $localRoot } else { "$userPath;$localRoot" }
        [Environment]::SetEnvironmentVariable('Path', $newUserPath, 'User')
        Write-Host "Added $localRoot to the user PATH." -ForegroundColor DarkGray
    }

    [Environment]::SetEnvironmentVariable('DOTNET_ROOT', $localRoot, 'User')
    Write-Host "DOTNET_ROOT=$localRoot" -ForegroundColor DarkGray
}

Write-Host ''
Write-Host "dotnet: $dotnet" -ForegroundColor Green
& $dotnet --version
if ($LASTEXITCODE -ne 0) {
    throw 'dotnet --version failed.'
}

Push-Location $repoRoot
try {
    Write-Host ''
    Write-Host 'Restoring packages...' -ForegroundColor Cyan
    & $dotnet restore $repoRoot\DynamicIsland.sln
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    if (-not $SkipBuild) {
        Write-Host ''
        Write-Host 'Building and testing the skeleton...' -ForegroundColor Cyan
        & $dotnet build $repoRoot\DynamicIsland.sln -c Debug --nologo
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

        & $dotnet test $repoRoot\DynamicIsland.sln -c Debug --no-build --nologo
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
}
finally {
    Pop-Location
}

Write-Host ''
Write-Host 'Development environment is ready.' -ForegroundColor Green
Write-Host 'Run: powershell -ExecutionPolicy Bypass -File .\scripts\run.ps1 -Demo'
Write-Host 'If this terminal existed before bootstrap, open a new terminal so DOTNET_ROOT and PATH are loaded.'
