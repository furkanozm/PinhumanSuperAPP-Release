# Squirrel.Windows Release Oluşturma Scripti
# Bu script, dist klasöründeki dosyalardan NuGet paketi oluşturup, Squirrel ile release dosyalarını oluşturur

param(
    [string]$Version = "1.0.1",
    [string]$OutputDir = "releases"
)

$ErrorActionPreference = "Stop"
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $scriptRoot

Write-Host "=== Squirrel.Windows Release Oluşturma ===" -ForegroundColor Cyan
Write-Host ""

# Versiyon bilgisini VERSION.json'dan oku
$versionFile = Join-Path $scriptRoot "VERSION.json"
if (Test-Path $versionFile) {
    $versionData = Get-Content $versionFile | ConvertFrom-Json
    $Version = $versionData.Version
    Write-Host "Versiyon: $Version (VERSION.json'dan okundu)" -ForegroundColor Green
} else {
    Write-Host "VERSION.json bulunamadı, varsayılan versiyon kullanılıyor: $Version" -ForegroundColor Yellow
}

Write-Host ""

# Squirrel.exe yolunu belirle
$squirrelExe = "C:\Users\BERKAN\.nuget\packages\squirrel.windows\2.0.1\tools\Squirrel.exe"
if (-not (Test-Path $squirrelExe)) {
    Write-Host "✗ Squirrel.exe bulunamadı: $squirrelExe" -ForegroundColor Red
    exit 1
}
Write-Host "✓ Squirrel.exe bulundu" -ForegroundColor Green

# Dist klasörünü kontrol et
$distPath = Join-Path $scriptRoot "dist"
if (-not (Test-Path $distPath)) {
    Write-Host "✗ dist klasörü bulunamadı: $distPath" -ForegroundColor Red
    Write-Host "Lütfen önce 'dotnet publish' komutunu çalıştırın." -ForegroundColor Yellow
    exit 1
}
Write-Host "✓ dist klasörü bulundu" -ForegroundColor Green

# NuGet paketi oluştur
Write-Host ""
Write-Host "=== NuGet Paketi Oluşturma ===" -ForegroundColor Cyan
$nuspecFile = Join-Path $scriptRoot "PinhumanSuperAPP.nuspec"
if (-not (Test-Path $nuspecFile)) {
    Write-Host "✗ .nuspec dosyası bulunamadı: $nuspecFile" -ForegroundColor Red
    exit 1
}

# .nuspec dosyasındaki versiyonu güncelle
$nuspecContent = Get-Content $nuspecFile -Raw
$nuspecContent = $nuspecContent -replace '<version>.*?</version>', "<version>$Version</version>"
Set-Content -Path $nuspecFile -Value $nuspecContent -NoNewline

# Output klasörünü oluştur
$outputPath = Join-Path $scriptRoot $OutputDir
if (-not (Test-Path $outputPath)) {
    New-Item -ItemType Directory -Path $outputPath | Out-Null
}

# NuGet paketi oluştur
$nupkgPath = Join-Path $outputPath "PinhumanSuperAPP.$Version.nupkg"
Write-Host "NuGet paketi oluşturuluyor: $nupkgPath" -ForegroundColor Yellow
dotnet pack $nuspecFile -OutputDirectory $outputPath -NoBuild

if (-not (Test-Path $nupkgPath)) {
    Write-Host "✗ NuGet paketi oluşturulamadı!" -ForegroundColor Red
    exit 1
}
Write-Host "✓ NuGet paketi oluşturuldu: $nupkgPath" -ForegroundColor Green

# Squirrel ile release dosyalarını oluştur
Write-Host ""
Write-Host "=== Squirrel Release Dosyaları Oluşturma ===" -ForegroundColor Cyan
Write-Host "Squirrel.exe çalıştırılıyor..." -ForegroundColor Yellow

& $squirrelExe --releasify $nupkgPath --releaseDir $outputPath

if ($LASTEXITCODE -ne 0) {
    Write-Host "✗ Squirrel release oluşturulamadı!" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "✓ Release dosyaları oluşturuldu!" -ForegroundColor Green
Write-Host ""
Write-Host "Release dosyaları şu klasörde: $outputPath" -ForegroundColor Cyan
Write-Host ""
Write-Host "Sonraki adım: Bu dosyaları GitHub Releases'e yükleyin." -ForegroundColor Yellow

