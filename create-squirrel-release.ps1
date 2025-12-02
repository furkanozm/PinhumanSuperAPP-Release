# Squirrel.Windows Release Olusturma Scripti

$ErrorActionPreference = "Stop"
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $scriptRoot

Write-Host "=== Squirrel.Windows Release Olusturma ===" -ForegroundColor Cyan
Write-Host ""

# Squirrel.exe yolunu belirle
$squirrelExe = "C:\Users\BERKAN\.nuget\packages\squirrel.windows\2.0.1\tools\Squirrel.exe"
if (-not (Test-Path $squirrelExe)) {
    Write-Host "Squirrel.exe bulunamadi: $squirrelExe" -ForegroundColor Red
    exit 1
}
Write-Host "Squirrel.exe bulundu" -ForegroundColor Green

# NuGet paketi yolunu belirle
$nupkgPath = Join-Path $scriptRoot "releases\PinhumanSuperAPP.1.0.1.nupkg"
if (-not (Test-Path $nupkgPath)) {
    Write-Host "NuGet paketi bulunamadi: $nupkgPath" -ForegroundColor Red
    exit 1
}
Write-Host "NuGet paketi bulundu: $nupkgPath" -ForegroundColor Green

# Release klasorunu olustur
$releaseDir = Join-Path $scriptRoot "releases"
if (-not (Test-Path $releaseDir)) {
    New-Item -ItemType Directory -Path $releaseDir | Out-Null
}

Write-Host ""
Write-Host "Squirrel release dosyalari olusturuluyor..." -ForegroundColor Yellow
Write-Host ""

# Squirrel.exe ile release dosyalarini olustur
& $squirrelExe --releasify $nupkgPath --releaseDir $releaseDir

if ($LASTEXITCODE -eq 0) {
    Write-Host ""
    Write-Host "Release dosyalari basariyla olusturuldu!" -ForegroundColor Green
    Write-Host "Release dosyalari: $releaseDir" -ForegroundColor Cyan
} else {
    Write-Host ""
    Write-Host "Squirrel release olusturulamadi! Hata kodu: $LASTEXITCODE" -ForegroundColor Red
    exit 1
}

