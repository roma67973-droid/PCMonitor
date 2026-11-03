<#
.SYNOPSIS
    Собирает PC Monitor: прогоняет тесты и публикует приложение в .\dist.

.PARAMETER ToolsRoot
    Папка с локальным .NET SDK и кэшами NuGet. По умолчанию D:\DevTools (на C: нет места).
    Если там нет SDK, используется dotnet из PATH.

.PARAMETER SkipTests
    Не запускать тесты (быстрая пересборка).

.EXAMPLE
    .\build.ps1
    .\build.ps1 -SkipTests
#>
[CmdletBinding()]
param(
    [string] $ToolsRoot = 'D:\DevTools',
    [switch] $SkipTests
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$dotnet = Join-Path $ToolsRoot 'dotnet\dotnet.exe'
if (Test-Path $dotnet) {
    # Все кэши и временные файлы — рядом с SDK, а не на системном диске.
    $env:DOTNET_ROOT = Join-Path $ToolsRoot 'dotnet'
    $env:DOTNET_CLI_HOME = Join-Path $ToolsRoot 'clihome'
    $env:NUGET_PACKAGES = Join-Path $ToolsRoot 'nuget\packages'
    $env:NUGET_HTTP_CACHE_PATH = Join-Path $ToolsRoot 'nuget\http-cache'
    $env:NUGET_PLUGINS_CACHE_PATH = Join-Path $ToolsRoot 'nuget\plugins'
    $env:TEMP = Join-Path $ToolsRoot 'tmp'
    $env:TMP = $env:TEMP
}
else {
    $dotnet = 'dotnet'
}
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

function Invoke-Dotnet {
    & $dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($args[0]) завершился с кодом $LASTEXITCODE" }
}

if (-not $SkipTests) {
    Write-Host '▶ Тесты' -ForegroundColor Cyan
    Invoke-Dotnet test PCMonitor.sln -c Release --nologo -v q
}

Write-Host '▶ Публикация' -ForegroundColor Cyan
$dist = Join-Path $PSScriptRoot 'dist'
Invoke-Dotnet publish src\PCMonitor\PCMonitor.csproj -c Release -r win-x64 --self-contained false -o $dist --nologo -v q

Write-Host "✔ Готово: $dist\PCMonitor.exe" -ForegroundColor Green
