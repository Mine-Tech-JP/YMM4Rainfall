# SPDX-License-Identifier: MPL-2.0
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$environmentNames = @(
    'DOTNET_CLI_HOME',
    'DOTNET_CLI_TELEMETRY_OPTOUT',
    'DOTNET_GENERATE_ASPNET_CERTIFICATE',
    'DOTNET_SKIP_FIRST_TIME_EXPERIENCE',
    'DOTNET_ADD_GLOBAL_TOOLS_TO_PATH',
    'DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE',
    'NUGET_PACKAGES'
)
$previousEnvironment = @{}
foreach ($environmentName in $environmentNames) {
    $previousEnvironment[$environmentName] = [Environment]::GetEnvironmentVariable($environmentName, 'Process')
}

Push-Location -LiteralPath $projectRoot
try {
    $dotnetCommand = Get-Command dotnet -CommandType Application -ErrorAction Stop | Select-Object -First 1
    $localPropsPath = Join-Path $projectRoot 'Directory.Build.local.props'
    if (-not (Test-Path -LiteralPath $localPropsPath -PathType Leaf)) {
        throw 'Directory.Build.local.props.sample を Directory.Build.local.props にコピーし、開発用YMM4の参照先を設定してください。'
    }

    $env:DOTNET_CLI_HOME = Join-Path $projectRoot 'tmp/dotnet-home'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
    $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
    $env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = '0'
    $env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = '1'
    $env:NUGET_PACKAGES = Join-Path $projectRoot 'tmp/nuget-packages'
    New-Item -ItemType Directory -Path $env:DOTNET_CLI_HOME -Force | Out-Null

    & $dotnetCommand.Source --version
    if ($LASTEXITCODE -ne 0) { throw 'global.json に対応する .NET SDK を利用できません。' }

    & $dotnetCommand.Source restore ./YMM4Rainfall.sln --configfile ./NuGet.Config --nologo
    if ($LASTEXITCODE -ne 0) { throw 'オフライン復元に失敗しました。SDKと既存の Windows SDK 参照パッケージの場所を確認してください。' }

    & $dotnetCommand.Source build ./YMM4Rainfall.sln -c $Configuration --no-restore --nologo
    if ($LASTEXITCODE -ne 0) { throw 'ビルドに失敗しました。上のエラーを確認してください。' }

    $outputPath = Join-Path $projectRoot "src/YMM4Rainfall/bin/$Configuration/net10.0-windows10.0.19041.0/YMM4Rainfall.dll"
    if (-not (Test-Path -LiteralPath $outputPath -PathType Leaf)) {
        throw "出力DLLが見つかりません: $outputPath"
    }
    Write-Host "ビルド完了: $outputPath"
    Write-Host 'このスクリプトはYMM4へのDLL配置やYMM4の起動を行いません。'
}
finally {
    foreach ($environmentName in $environmentNames) {
        [Environment]::SetEnvironmentVariable($environmentName, $previousEnvironment[$environmentName], 'Process')
    }
    Pop-Location
}
