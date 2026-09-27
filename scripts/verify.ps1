# SPDX-License-Identifier: MPL-2.0
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [switch]$Graphics,
    [switch]$Preview
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$ffmpegCommand = $null
if ($Preview) {
    $ffmpegCommand = Get-Command ffmpeg -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $ffmpegCommand) {
        throw '動画サンプルの作成には既存のFFmpegが必要です。Previewを省略してGraphicsを指定すると、数値検証と描画検証を実行できます。'
    }
}

Push-Location -LiteralPath $projectRoot
try {
    & (Join-Path $PSScriptRoot 'build.ps1') -Configuration $Configuration
    $verificationPath = Join-Path $projectRoot "tests/YMM4Rainfall.Verification/bin/$Configuration/net10.0-windows10.0.19041.0/YMM4Rainfall.Verification.exe"
    $verificationArguments = @()
    if ($Graphics -or $Preview) { $verificationArguments += '--graphics' }
    if ($Preview) { $verificationArguments += '--preview' }
    & $verificationPath @verificationArguments
    if ($LASTEXITCODE -ne 0) { throw '降雨の検証に失敗しました。上の検証結果を確認してください。' }

    if ($Preview) {
        $framePattern = Join-Path $projectRoot 'tmp/rainfall-preview-frames/frame-%04d.png'
        $videoPath = Join-Path $projectRoot 'tmp/rainfall-preview.mp4'
        & $ffmpegCommand.Source -hide_banner -loglevel error -nostdin -y -framerate 30 -i $framePattern -frames:v 90 -an -c:v libx264 -preset veryfast -crf 18 -pix_fmt yuv420p -movflags +faststart $videoPath
        if ($LASTEXITCODE -ne 0) { throw '動画サンプルの作成に失敗しました。' }
        Write-Host "動画サンプル: $videoPath"
    }
}
finally {
    Pop-Location
}
