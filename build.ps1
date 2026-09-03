#Requires -Version 5.1
<#
.SYNOPSIS
    Builds BiliStreamAudio-TUI in Release, publishes to .\build, and trims the
    LibVLC plugins that an audio-only TUI never uses.

.DESCRIPTION
    The app plays Bilibili live audio with LibVLC configured as
    --no-video / --intf=dummy, so every video output, video filter, chroma,
    subtitle, visualization, transcode and stream-out plugin is dead weight.
    This script publishes a single-file, framework-dependent win-x64 build to
    the repository-root build\ folder, then removes those unused plugins and
    reports the resulting size.

    build\ is git-ignored; it is a disposable artifact, regenerated on every run.

.PARAMETER NoClean
    Publish only; skip the LibVLC plugin trimming step.

.EXAMPLE
    .\build.ps1
    .\build.ps1 -NoClean
#>
[CmdletBinding()]
param(
    [switch]$NoClean
)

$ErrorActionPreference = 'Stop'

$root     = $PSScriptRoot
$proj     = Join-Path $root 'src\BiliStreamAudio.Tui\BiliStreamAudio.Tui.csproj'
$buildDir = Join-Path $root 'build'

function Get-SizeMB([string]$path) {
    if (-not (Test-Path $path)) { return 0.0 }
    $sum = (Get-ChildItem $path -Recurse -File | Measure-Object -Property Length -Sum).Sum
    return [math]::Round($sum / 1MB, 1)
}

Write-Host "==> Cleaning previous build output" -ForegroundColor Cyan
if (Test-Path $buildDir) { Remove-Item $buildDir -Recurse -Force }

Write-Host "==> Publishing Release (win-x64, single-file, framework-dependent)" -ForegroundColor Cyan
# The VideoLAN.LibVLC.Windows package copies every architecture (x64/x86/arm64)
# when Platform is AnyCPU. We only ship win-x64, so disable the other two at
# build time to avoid dragging ~180 MB of unused LibVLC into the output.
dotnet publish $proj -c Release -r win-x64 --self-contained false `
    -p:PublishSingleFile=true `
    -p:VlcWindowsX86Enabled=false `
    -p:VlcWindowsArm64Enabled=false `
    -o $buildDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

$rawSize = Get-SizeMB $buildDir
Write-Host ("    Published size: {0} MB" -f $rawSize)

if ($NoClean) {
    Write-Host "==> Skipping plugin trim (-NoClean)" -ForegroundColor Yellow
}
else {
    Write-Host "==> Trimming unused LibVLC plugins (audio-only TUI)" -ForegroundColor Cyan
    $plugins = Join-Path $buildDir 'libvlc\win-x64\plugins'

    if (Test-Path $plugins) {
        # Whole plugin categories that only serve video / subtitles / output /
        # transcoding / discovery. None of them are reachable from an
        # audio-only, no-video, dummy-interface playback path.
        $removeDirs = @(
            'video_output', 'video_filter', 'video_chroma', 'video_splitter',
            'visualization', 'text_renderer', 'spu',
            'access_output', 'stream_out', 'mux',
            'd3d11', 'd3d9', 'services_discovery', 'meta_engine'
        )
        foreach ($d in $removeDirs) {
            $p = Join-Path $plugins $d
            if (Test-Path $p) { Remove-Item $p -Recurse -Force }
        }

        # Individual plugins inside shared categories. Audio codecs, filters,
        # resamplers and the FLV/TS/fMP4/HLS demuxers are deliberately kept.
        # Audio is rendered by NAudio/WASAPI from LibVLC's PCM callback, so the
        # only LibVLC audio-output module that remains necessary is amem.
        $removeFiles = [ordered]@{
            'codec' = @(
                # video codecs / hardware video decode
                'libvpx_plugin.dll', 'libaom_plugin.dll', 'libdav1d_plugin.dll',
                'libx26410b_plugin.dll', 'libtheora_plugin.dll',
                'libdxva2_plugin.dll', 'libd3d11va_plugin.dll', 'libqsv_plugin.dll',
                'libcrystalhd_plugin.dll', 'libdmo_plugin.dll', 'libmft_plugin.dll',
                'libaraw_plugin.dll', 'librawvideo_plugin.dll', 'librtpvideo_plugin.dll',
                'libcdg_plugin.dll',
                # subtitle / teletext / closed caption
                'liblibass_plugin.dll', 'libzvbi_plugin.dll', 'libwebvtt_plugin.dll',
                'libttml_plugin.dll', 'libdvbsub_plugin.dll', 'libsubsdec_plugin.dll',
                'libcc_plugin.dll', 'libscte27_plugin.dll', 'libcvdsub_plugin.dll',
                'libsvcdsub_plugin.dll', 'libsubstx3g_plugin.dll', 'libtextst_plugin.dll',
                'libspudec_plugin.dll', 'libsubsusf_plugin.dll',
                # image / midi / other non-audio
                'libpng_plugin.dll', 'libjpeg_plugin.dll', 'libfluidsynth_plugin.dll',
                'libschroedinger_plugin.dll'
            )
            'demux' = @(
                'libgme_plugin.dll', 'libmod_plugin.dll', 'libmkv_plugin.dll',
                'libsubtitle_plugin.dll', 'libvobsub_plugin.dll', 'libimage_plugin.dll',
                'libdemux_chromecast_plugin.dll', 'libnsc_plugin.dll', 'libty_plugin.dll',
                'libsmf_plugin.dll', 'libnuv_plugin.dll', 'libpva_plugin.dll',
                'librawvid_plugin.dll', 'libnsv_plugin.dll', 'libvoc_plugin.dll',
                'libdemux_stl_plugin.dll', 'librawdv_plugin.dll', 'libtta_plugin.dll',
                'libdemuxdump_plugin.dll', 'libvc1_plugin.dll', 'libdiracsys_plugin.dll',
                'libdemux_cdg_plugin.dll', 'libxa_plugin.dll',
                'libdirectory_demux_plugin.dll', 'libnoseek_plugin.dll',
                'libmjpeg_plugin.dll', 'libh26x_plugin.dll', 'libavi_plugin.dll',
                'libps_plugin.dll'
            )
            'access' = @(
                'libaccess_srt_plugin.dll', 'libdcp_plugin.dll', 'liblibbluray_plugin.dll',
                'libdtv_plugin.dll', 'libsftp_plugin.dll', 'libcdda_plugin.dll',
                'libbluray-j2se-1.3.2.jar', 'libbluray-awt-j2se-1.3.2.jar',
                'librtp_plugin.dll', 'liblive555_plugin.dll', 'libnfs_plugin.dll',
                'libaccess_mms_plugin.dll', 'libvcd_plugin.dll', 'libvdr_plugin.dll',
                'libsatip_plugin.dll', 'libsmb_plugin.dll', 'librist_plugin.dll',
                'libscreen_plugin.dll'
            )
            'misc' = @(
                'libaddonsfsstorage_plugin.dll', 'libaddonsvorepository_plugin.dll',
                'libfingerprinter_plugin.dll'
            )
            'audio_filter' = @(
                'libspatialaudio_plugin.dll', 'libspatializer_plugin.dll'
            )
            'audio_output' = @(
                'libadummy_plugin.dll', 'libafile_plugin.dll',
                'libdirectsound_plugin.dll', 'libmmdevice_plugin.dll',
                'libwasapi_plugin.dll', 'libwaveout_plugin.dll'
            )
        }

        $removed = 0
        foreach ($dir in $removeFiles.Keys) {
            $base = Join-Path $plugins $dir
            if (-not (Test-Path $base)) { continue }
            foreach ($f in $removeFiles[$dir]) {
                $p = Join-Path $base $f
                if (Test-Path $p) { Remove-Item $p -Force; $removed++ }
            }
        }
        Write-Host "    Removed $removed individual plugin files plus unused categories."
    }
    else {
        Write-Warning "    Plugin directory not found; nothing to trim: $plugins"
    }
}

Write-Host "==> Removing debug symbols and XML documentation" -ForegroundColor Cyan
$docFiles = Get-ChildItem $buildDir -Recurse -File -Include *.pdb, *.xml
$docBytes = ($docFiles | Measure-Object -Property Length -Sum).Sum
foreach ($f in $docFiles) { Remove-Item $f.FullName -Force }
Write-Host ("    Removed {0} pdb/xml files ({1:N1} KB)" -f $docFiles.Count, ($docBytes / 1KB))

$finalSize = Get-SizeMB $buildDir
$exe = Join-Path $buildDir 'BiliStreamAudio.Tui.exe'
Write-Host ""
Write-Host "==> Build complete" -ForegroundColor Green
Write-Host "    Output : $buildDir"
Write-Host ("    Entry  : {0} ({1} MB)" -f (Split-Path $exe -Leaf), (Get-SizeMB $exe))
Write-Host ("    Size   : {0} MB (published {1} MB)" -f $finalSize, $rawSize)
if (-not $NoClean) {
    Write-Host ("    Saved  : {0} MB of unused LibVLC plugins" -f [math]::Round($rawSize - $finalSize, 1))
}
