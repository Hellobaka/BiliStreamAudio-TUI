#Requires -Version 5.1
<#
.SYNOPSIS
    Publishes a compact Windows build of BiliStreamAudio-TUI to .\build.

.DESCRIPTION
    Publishes a single-file, framework-dependent win-x64 application and places
    the statically linked ffmpeg-aac.exe beside it. FFmpeg remains a separate
    executable because the player communicates with it through standard output.

    build\ is git-ignored; it is a disposable artifact regenerated on every run.

.EXAMPLE
    .\build.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$project = Join-Path $root 'src\BiliStreamAudio.Tui\BiliStreamAudio.Tui.csproj'
$settingsWindow = Join-Path $root 'src\BiliStreamAudio.Tui\Views\SettingsWindow.cs'
$buildDir = Join-Path $root 'build'
$buildTimestampPlaceholder = '__BILISTREAMAUDIO_BUILD_TIMESTAMP__'

function Get-SizeMB([string]$path) {
    if (-not (Test-Path $path)) { return 0.0 }
    $sum = (Get-ChildItem $path -Recurse -File | Measure-Object -Property Length -Sum).Sum
    return [math]::Round($sum / 1MB, 1)
}

$sourceText = [IO.File]::ReadAllText($settingsWindow)
$placeholderCount = ([regex]::Matches(
    $sourceText,
    [regex]::Escape($buildTimestampPlaceholder))).Count
if ($placeholderCount -ne 1) {
    throw "Expected exactly one build timestamp placeholder in $settingsWindow; found $placeholderCount."
}

$buildTimestamp = [DateTimeOffset]::Now.ToString(
    'yyyy-MM-dd HH:mm:ss zzz',
    [Globalization.CultureInfo]::InvariantCulture)
$stampedSourceText = $sourceText.Replace($buildTimestampPlaceholder, $buildTimestamp)
$utf8WithoutBom = New-Object Text.UTF8Encoding($false)

try {
    [IO.File]::WriteAllText($settingsWindow, $stampedSourceText, $utf8WithoutBom)
    Write-Host "==> Embedded build time: $buildTimestamp" -ForegroundColor Cyan

    Write-Host "==> Cleaning previous build output" -ForegroundColor Cyan
    if (Test-Path $buildDir) { Remove-Item $buildDir -Recurse -Force }

    Write-Host "==> Publishing Release (win-x64, single-file, framework-dependent)" -ForegroundColor Cyan
    dotnet publish $project -c Release -r win-x64 --self-contained false `
        -p:PublishSingleFile=true `
        -o $buildDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

    $ffmpeg = Join-Path $buildDir 'ffmpeg-aac.exe'
    if (-not (Test-Path $ffmpeg)) {
        throw "FFmpeg executable was not copied to the publish output: $ffmpeg"
    }

    Write-Host "==> Removing debug symbols and XML documentation" -ForegroundColor Cyan
    $docFiles = Get-ChildItem $buildDir -Recurse -File -Include *.pdb, *.xml
    $docBytes = ($docFiles | Measure-Object -Property Length -Sum).Sum
    foreach ($file in $docFiles) { Remove-Item $file.FullName -Force }
    Write-Host ("    Removed {0} pdb/xml files ({1:N1} KB)" -f $docFiles.Count, ($docBytes / 1KB))

    $exe = Join-Path $buildDir 'BiliStreamAudio.Tui.exe'
    Write-Host ""
    Write-Host "==> Build complete" -ForegroundColor Green
    Write-Host "    Output : $buildDir"
    Write-Host ("    App    : {0} ({1} MB)" -f (Split-Path $exe -Leaf), (Get-SizeMB $exe))
    Write-Host ("    FFmpeg : {0} ({1} MB)" -f (Split-Path $ffmpeg -Leaf), (Get-SizeMB $ffmpeg))
    Write-Host ("    Size   : {0} MB" -f (Get-SizeMB $buildDir))
}
finally {
    [IO.File]::WriteAllText($settingsWindow, $sourceText, $utf8WithoutBom)
}
