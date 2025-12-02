# Squirrel.Windows Release Olusturma Scripti (Tam Versiyon)
# Bu script, NuGet paketinden Squirrel release dosyalarini olusturur

$ErrorActionPreference = "Stop"
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $scriptRoot

Write-Host "=== Squirrel.Windows Release Olusturma ===" -ForegroundColor Cyan
Write-Host ""

# Versiyon bilgisini oku
$versionFile = Join-Path $scriptRoot "VERSION.json"
$Version = "1.0.1"
if (Test-Path $versionFile) {
    $versionData = Get-Content $versionFile | ConvertFrom-Json
    $Version = $versionData.Version
}
Write-Host "Versiyon: $Version" -ForegroundColor Green

# Squirrel.exe yolunu belirle
$squirrelExe = "C:\Users\BERKAN\.nuget\packages\squirrel.windows\2.0.1\tools\Squirrel.exe"
if (-not (Test-Path $squirrelExe)) {
    Write-Host "Squirrel.exe bulunamadi: $squirrelExe" -ForegroundColor Red
    exit 1
}
Write-Host "Squirrel.exe bulundu" -ForegroundColor Green

# NuGet paketi yolunu belirle
$nupkgPath = Join-Path $scriptRoot "releases\PinhumanSuperAPP.$Version.nupkg"
if (-not (Test-Path $nupkgPath)) {
    Write-Host "NuGet paketi bulunamadi: $nupkgPath" -ForegroundColor Red
    Write-Host "Once NuGet paketi olusturun: .\create-nupkg.ps1" -ForegroundColor Yellow
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
Write-Host "Squirrel.exe calistiriliyor: $squirrelExe" -ForegroundColor Gray
Write-Host "NuGet paketi: $nupkgPath" -ForegroundColor Gray
Write-Host "Release klasoru: $releaseDir" -ForegroundColor Gray
Write-Host ""

# Squirrel.exe ile release dosyalarini olustur
# -r veya --releaseDir parametresi ile release klasorunu belirt
$process = Start-Process -FilePath $squirrelExe `
    -ArgumentList "--releasify", "`"$nupkgPath`"", "--releaseDir", "`"$releaseDir`"" `
    -NoNewWindow `
    -Wait `
    -PassThru `
    -RedirectStandardOutput "$releaseDir\squirrel_output.txt" `
    -RedirectStandardError "$releaseDir\squirrel_error.txt"

Write-Host "Squirrel.exe tamamlandi. Exit code: $($process.ExitCode)" -ForegroundColor Cyan

# Ciktiları goster
if (Test-Path "$releaseDir\squirrel_output.txt") {
    Write-Host ""
    Write-Host "=== Squirrel Output ===" -ForegroundColor Yellow
    Get-Content "$releaseDir\squirrel_output.txt"
}

if (Test-Path "$releaseDir\squirrel_error.txt") {
    $errorContent = Get-Content "$releaseDir\squirrel_error.txt" -Raw
    if ($errorContent.Trim()) {
        Write-Host ""
        Write-Host "=== Squirrel Errors ===" -ForegroundColor Red
        Get-Content "$releaseDir\squirrel_error.txt"
    }
}

# Release dosyalarini kontrol et
Write-Host ""
Write-Host "=== Release Dosyalari Kontrolu ===" -ForegroundColor Cyan
$releaseFiles = Get-ChildItem -Path $releaseDir -File | Where-Object { $_.Name -notlike "*.nupkg" -and $_.Name -notlike "*output*" -and $_.Name -notlike "*error*" }
if ($releaseFiles.Count -gt 0) {
    Write-Host "Olusturulan dosyalar:" -ForegroundColor Green
    $releaseFiles | ForEach-Object {
        $sizeMB = [math]::Round($_.Length / 1MB, 2)
        Write-Host "  - $($_.Name) ($sizeMB MB)" -ForegroundColor White
    }
} else {
    Write-Host "UYARI: Release dosyasi bulunamadi!" -ForegroundColor Yellow
}

if ($process.ExitCode -eq 0 -and $releaseFiles.Count -gt 0) {
    Write-Host ""
    Write-Host "Release dosyalari basariyla olusturuldu!" -ForegroundColor Green
    Write-Host "Release klasoru: $releaseDir" -ForegroundColor Cyan
} else {
    Write-Host ""
    Write-Host "Squirrel release olusturulamadi!" -ForegroundColor Red
    if ($process.ExitCode -ne 0) {
        Write-Host "Exit code: $($process.ExitCode)" -ForegroundColor Red
    }
    exit 1
}

