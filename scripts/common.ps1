# Shared helpers for the developer scripts. Dot-source this file.

$script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$script:LocalDotnetRoot = Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet'

function Get-ProjectDotnet {
    $localDotnet = Join-Path $script:LocalDotnetRoot 'dotnet.exe'
    if (Test-Path -LiteralPath $localDotnet) {
        $env:DOTNET_ROOT = $script:LocalDotnetRoot
        if (($env:Path -split ';') -notcontains $script:LocalDotnetRoot) {
            $env:Path = "$script:LocalDotnetRoot;$env:Path"
        }

        return $localDotnet
    }

    $command = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -eq $command) {
        throw 'No .NET SDK was found. Run scripts\bootstrap.ps1 first.'
    }

    return $command.Source
}

function Assert-Dotnet10 {
    param([Parameter(Mandatory = $true)][string]$DotnetPath)

    $sdks = & $DotnetPath --list-sdks 2>$null
    if (($sdks -join "`n") -notmatch '(?m)^10\.') {
        throw 'A .NET 10 SDK is required. Run scripts\bootstrap.ps1 to install one for this user.'
    }
}

$script:Dotnet = Get-ProjectDotnet
Assert-Dotnet10 -DotnetPath $script:Dotnet
$script:Solution = Join-Path $script:RepoRoot 'DynamicIsland.sln'
$script:AppProject = Join-Path $script:RepoRoot 'src\DynamicIsland.App\DynamicIsland.App.csproj'
