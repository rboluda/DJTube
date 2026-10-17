<#
.SYNOPSIS
    Genera los paquetes de DJTube para distribuir: instalador (Setup.exe) y ZIP portable.

.DESCRIPTION
    - Publica la app "self-contained" para Windows x64: lleva dentro .NET y las DLL de BASS,
      así que quien la instala no necesita nada más.
    - Añade yt-dlp y Deno (necesarios para los enlaces de YouTube) en la carpeta tools.
      Se descargan de sus releases oficiales de GitHub en las versiones fijadas aquí abajo
      y se guardan en artifacts/cache para no descargarlos cada vez.
    - Crea artifacts/DJTube-<versión>-win-x64-portable.zip y artifacts/DJTube-Setup-<versión>.exe
      (este último con Inno Setup 6: winget install JRSoftware.InnoSetup).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File build/publish.ps1
#>
param(
    [string]$YtDlpVersion = '2026.08.19',
    [string]$DenoVersion = '2.9.7',
    [switch]$SkipInstaller
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $root 'artifacts'
$publish = Join-Path $artifacts 'publish\DJTube'
$cache = Join-Path $artifacts 'cache'

# Versión del programa (Directory.Build.props).
[xml]$props = Get-Content (Join-Path $root 'Directory.Build.props')
$version = ($props.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1)
Write-Host "DJTube $version" -ForegroundColor Cyan

# 1) Publicación autocontenida.
if (Test-Path $publish) { Remove-Item -Recurse -Force $publish }
dotnet publish (Join-Path $root 'src\DJTube.App\DJTube.App.csproj') -c Release -r win-x64 --self-contained true -o $publish -nologo
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish ha fallado' }

# 2) yt-dlp y Deno.
New-Item -ItemType Directory -Force $cache, (Join-Path $publish 'tools') | Out-Null
function Get-Cached([string]$url, [string]$file) {
    $path = Join-Path $cache $file
    if (-not (Test-Path $path)) {
        Write-Host "Descargando $url"
        Invoke-WebRequest -Uri $url -OutFile "$path.part" -UseBasicParsing
        Move-Item "$path.part" $path
    }
    return $path
}
$ytDlp = Get-Cached "https://github.com/yt-dlp/yt-dlp/releases/download/$YtDlpVersion/yt-dlp.exe" "yt-dlp-$YtDlpVersion.exe"
Copy-Item $ytDlp (Join-Path $publish 'tools\yt-dlp.exe')

$denoZip = Get-Cached "https://github.com/denoland/deno/releases/download/v$DenoVersion/deno-x86_64-pc-windows-msvc.zip" "deno-$DenoVersion.zip"
$denoDir = Join-Path $cache "deno-$DenoVersion"
if (-not (Test-Path (Join-Path $denoDir 'deno.exe'))) { Expand-Archive $denoZip -DestinationPath $denoDir -Force }
Copy-Item (Join-Path $denoDir 'deno.exe') (Join-Path $publish 'tools\deno.exe')

# 3) Avisos de licencias y léeme.
Copy-Item (Join-Path $root 'THIRD-PARTY-NOTICES.txt') $publish
Copy-Item (Join-Path $root 'CHANGELOG.md') $publish

# 4) ZIP portable.
$zip = Join-Path $artifacts "DJTube-$version-win-x64-portable.zip"
if (Test-Path $zip) { Remove-Item -Force $zip }
Compress-Archive -Path (Join-Path $publish '*') -DestinationPath $zip -CompressionLevel Optimal
Write-Host "ZIP: $zip" -ForegroundColor Green

# 5) Instalador.
if (-not $SkipInstaller) {
    $iscc = @(
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $iscc) { throw 'No se encuentra Inno Setup 6 (winget install JRSoftware.InnoSetup)' }
    & $iscc "/DAppVersion=$version" "/DSourceDir=$publish" "/DOutputDir=$artifacts" (Join-Path $root 'installer\DJTube.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Inno Setup ha fallado' }
    Write-Host "Instalador: $(Join-Path $artifacts "DJTube-Setup-$version.exe")" -ForegroundColor Green
}
