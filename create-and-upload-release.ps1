# Otomatik Release Olusturma ve GitHub'a Yukleme Scripti
# Bu script: 1) NuGet paketi olusturur, 2) Squirrel release dosyalarini olusturur, 3) GitHub Releases'e yukler

param(
    [string]$RepoOwner = "furkanozm",
    [string]$RepoName = "PinhumanSuperAPP-Release",
    [string]$GitHubToken = ""
)

$ErrorActionPreference = "Stop"
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $scriptRoot

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  PinhumanSuperAPP Release Olusturma" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# ADIM 1: Versiyon bilgisini oku
Write-Host "[1/5] Versiyon bilgisi okunuyor..." -ForegroundColor Yellow
$versionFile = Join-Path $scriptRoot "VERSION.json"
$Version = "1.0.1"
$ReleaseNotes = ""
if (Test-Path $versionFile) {
    $versionData = Get-Content $versionFile | ConvertFrom-Json
    $Version = $versionData.Version
    $ReleaseNotes = $versionData.ReleaseNotes
    Write-Host "  Versiyon: $Version" -ForegroundColor Green
    Write-Host "  Release Notlari: $ReleaseNotes" -ForegroundColor Gray
} else {
    Write-Host "  VERSION.json bulunamadi, varsayilan versiyon kullaniliyor" -ForegroundColor Yellow
}
Write-Host ""

# ADIM 2: NuGet paketi olustur
Write-Host "[2/5] NuGet paketi olusturuluyor..." -ForegroundColor Yellow
$nupkgScript = Join-Path $scriptRoot "create-nupkg.ps1"
if (-not (Test-Path $nupkgScript)) {
    Write-Host "  HATA: create-nupkg.ps1 bulunamadi!" -ForegroundColor Red
    exit 1
}

$nupkgProcess = Start-Process -FilePath "powershell.exe" `
    -ArgumentList "-ExecutionPolicy", "Bypass", "-File", "`"$nupkgScript`"" `
    -NoNewWindow `
    -Wait `
    -PassThru

if ($nupkgProcess.ExitCode -ne 0) {
    Write-Host "  HATA: NuGet paketi olusturulamadi!" -ForegroundColor Red
    exit 1
}

$nupkgPath = Join-Path $scriptRoot "releases\PinhumanSuperAPP.$Version.nupkg"
if (-not (Test-Path $nupkgPath)) {
    Write-Host "  HATA: NuGet paketi olusturulamadi: $nupkgPath" -ForegroundColor Red
    exit 1
}
Write-Host "  NuGet paketi olusturuldu: $nupkgPath" -ForegroundColor Green
Write-Host ""

# ADIM 3: Squirrel release dosyalarini olustur
Write-Host "[3/5] Squirrel release dosyalari olusturuluyor..." -ForegroundColor Yellow
$squirrelExe = "C:\Users\BERKAN\.nuget\packages\squirrel.windows\2.0.1\tools\Squirrel.exe"
if (-not (Test-Path $squirrelExe)) {
    Write-Host "  HATA: Squirrel.exe bulunamadi: $squirrelExe" -ForegroundColor Red
    exit 1
}

$releaseDir = Join-Path $scriptRoot "releases"
Write-Host "  Squirrel.exe calistiriliyor..." -ForegroundColor Gray

$squirrelProcess = Start-Process -FilePath $squirrelExe `
    -ArgumentList "--releasify", "`"$nupkgPath`"", "--releaseDir", "`"$releaseDir`"" `
    -NoNewWindow `
    -Wait `
    -PassThru

if ($squirrelProcess.ExitCode -ne 0) {
    Write-Host "  UYARI: Squirrel.exe exit code: $($squirrelProcess.ExitCode)" -ForegroundColor Yellow
    Write-Host "  Devam ediliyor..." -ForegroundColor Yellow
}

# Release dosyalarini kontrol et
$setupExe = Join-Path $releaseDir "Setup.exe"
$releasesFile = Join-Path $releaseDir "RELEASES"
$fullNupkg = Join-Path $releaseDir "PinhumanSuperAPP.$Version-full.nupkg"

$releaseFilesCreated = $false
if (Test-Path $setupExe) {
    Write-Host "  Setup.exe olusturuldu" -ForegroundColor Green
    $releaseFilesCreated = $true
}
if (Test-Path $releasesFile) {
    Write-Host "  RELEASES dosyasi olusturuldu" -ForegroundColor Green
    $releaseFilesCreated = $true
}
if (Test-Path $fullNupkg) {
    Write-Host "  Full NuGet paketi olusturuldu" -ForegroundColor Green
    $releaseFilesCreated = $true
}

if (-not $releaseFilesCreated) {
    Write-Host "  UYARI: Release dosyalari olusturulamadi!" -ForegroundColor Yellow
    Write-Host "  Squirrel.Windows sorunlu olabilir. Manuel kontrol gerekebilir." -ForegroundColor Yellow
}
Write-Host ""

# ADIM 4: Release dosyalarini hazirla (zip olarak)
Write-Host "[4/5] Release dosyalari hazirlaniyor..." -ForegroundColor Yellow
$releaseZip = Join-Path $releaseDir "PinhumanSuperAPP-v$Version.zip"

# Eger Squirrel dosyalari olusturulduysa, Setup.exe ve RELEASES'i zip'le
# Degilse, sadece dist klasorunu zip'le
$distPath = Join-Path $scriptRoot "dist"
if ($releaseFilesCreated) {
    Write-Host "  Squirrel release dosyalari zip'leniyor..." -ForegroundColor Gray
    $filesToZip = @($setupExe, $releasesFile, $fullNupkg) | Where-Object { Test-Path $_ }
    if ($filesToZip.Count -gt 0) {
        Compress-Archive -Path $filesToZip -DestinationPath $releaseZip -Force
    }
} else {
    Write-Host "  dist klasoru zip'leniyor..." -ForegroundColor Gray
    Compress-Archive -Path "$distPath\*" -DestinationPath $releaseZip -Force -Exclude "dist.zip"
}

if (Test-Path $releaseZip) {
    $zipSize = [math]::Round((Get-Item $releaseZip).Length / 1MB, 2)
    Write-Host "  Release zip olusturuldu: $releaseZip ($zipSize MB)" -ForegroundColor Green
} else {
    Write-Host "  HATA: Release zip olusturulamadi!" -ForegroundColor Red
    exit 1
}
Write-Host ""

# ADIM 5: GitHub Releases'e yukle (manuel talimatlar)
Write-Host "[5/5] GitHub Releases'e yukleme talimatlari" -ForegroundColor Yellow
Write-Host ""
Write-Host "GitHub Releases'e manuel yuklemek icin:" -ForegroundColor Cyan
Write-Host ""
Write-Host "1. Su adrese gidin:" -ForegroundColor White
Write-Host "   https://github.com/$RepoOwner/$RepoName/releases/new" -ForegroundColor Cyan
Write-Host ""
Write-Host "2. Su bilgileri doldurun:" -ForegroundColor White
Write-Host "   - Tag version: v$Version" -ForegroundColor Cyan
Write-Host "   - Release title: v$Version" -ForegroundColor Cyan
Write-Host "   - Release notes:" -ForegroundColor Cyan
Write-Host "     $ReleaseNotes" -ForegroundColor Gray
Write-Host ""
Write-Host "3. Dosya yukleme:" -ForegroundColor White
if ($releaseFilesCreated) {
    Write-Host "   - Setup.exe (zorunlu)" -ForegroundColor Cyan
    Write-Host "   - RELEASES (zorunlu)" -ForegroundColor Cyan
    Write-Host "   - PinhumanSuperAPP.$Version-full.nupkg (zorunlu)" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "   VEYA tum dosyalari iceren zip:" -ForegroundColor Yellow
}
Write-Host "   - $releaseZip" -ForegroundColor Cyan
Write-Host ""
Write-Host "4. 'Publish release' butonuna basin" -ForegroundColor White
Write-Host ""

# Sonuc ozeti
Write-Host "========================================" -ForegroundColor Green
Write-Host "  Release Hazirlama Tamamlandi!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "Olusturulan dosyalar:" -ForegroundColor Cyan
Write-Host "  - NuGet paketi: $nupkgPath" -ForegroundColor White
if ($releaseFilesCreated) {
    Write-Host "  - Setup.exe: $setupExe" -ForegroundColor White
    Write-Host "  - RELEASES: $releasesFile" -ForegroundColor White
    Write-Host "  - Full NuGet: $fullNupkg" -ForegroundColor White
}
Write-Host "  - Release Zip: $releaseZip" -ForegroundColor White
Write-Host ""
Write-Host "Sonraki adim: GitHub Releases'e yukleyin" -ForegroundColor Yellow

