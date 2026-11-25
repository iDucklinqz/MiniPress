# Builds dist\MiniPress.exe: one self-contained file with FFmpeg inside.
# Uses the C# compiler that ships with Windows (.NET Framework 4.x); no Visual Studio or .NET SDK needed:
#   powershell -ExecutionPolicy Bypass -File build.ps1
# Needs assets\ffmpeg.exe (build it with tools\build-ffmpeg.sh, see README).
# -NoFFmpeg builds a small exe that uses an ffmpeg.exe found on the PC instead (for development).
param([switch]$NoFFmpeg)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $csc)) { throw "csc.exe not found (needs .NET Framework 4.x, built into Windows 10/11)" }

$dist = Join-Path $root 'dist'
$obj = Join-Path $root 'obj'
New-Item -ItemType Directory -Force $dist, $obj | Out-Null
$out = Join-Path $dist 'MiniPress.exe'
$icon = Join-Path $root 'assets\icon.ico'
$sources = Get-ChildItem (Join-Path $root 'src') -Filter *.cs | ForEach-Object { $_.FullName }
$resources = @("/resource:$icon,MiniPress.icon.ico")

if (-not $NoFFmpeg) {
    $ffmpeg = Join-Path $root 'assets\ffmpeg.exe'
    if (-not (Test-Path $ffmpeg)) { throw "assets\ffmpeg.exe is missing: build it with tools\build-ffmpeg.sh (see README), or pass -NoFFmpeg" }
    $gz = Join-Path $obj 'ffmpeg.exe.gz'
    if (-not (Test-Path $gz) -or (Get-Item $gz).LastWriteTimeUtc -lt (Get-Item $ffmpeg).LastWriteTimeUtc) {
        Write-Host "Compressing ffmpeg.exe ..."
        $in = [IO.File]::OpenRead($ffmpeg)
        $outStream = [IO.File]::Create($gz)
        $zip = New-Object IO.Compression.GZipStream($outStream, [IO.Compression.CompressionLevel]::Optimal)
        $in.CopyTo($zip)
        $zip.Dispose(); $outStream.Dispose(); $in.Dispose()
    }
    $resources += "/resource:$gz,MiniPress.ffmpeg.exe.gz"
}

& $csc /nologo /codepage:65001 /target:winexe /optimize+ /platform:anycpu /nowarn:1607 `
    "/out:$out" "/win32icon:$icon" "/win32manifest:$(Join-Path $root 'src\app.manifest')" `
    $resources `
    /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll `
    $sources
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

$size = (Get-Item $out).Length
if ($size -gt 1MB) { $text = "{0:N1} MB" -f ($size / 1MB) } else { $text = "{0:N0} KB" -f ($size / 1KB) }
Write-Host "OK  $out  ($text)"
