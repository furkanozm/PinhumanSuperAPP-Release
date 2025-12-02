# GitHub Releases Olusturma Scripti
# Bu script, dist klasorunu zip'leyip GitHub Releases'e yukler

param(
    [string]$Version = "1.0.1",
    [string]$RepoOwner = "furkanozm",
    [string]$RepoName = "PinhumanSuperAPP-Release",
    [string]$GitHubToken = ""
)

$ErrorActionPreference = "Stop"
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $scriptRoot

Write-Host "=== GitHub Release Olusturma ===" -ForegroundColor Cyan
Write-Host ""

# Versiyon bilgisini VERSION.json'dan oku
$versionFile = Join-Path $scriptRoot "VERSION.json"
if (Test-Path $versionFile) {
    $versionData = Get-Content $versionFile | ConvertFrom-Json
    $Version = $versionData.Version
    $ReleaseNotes = $versionData.ReleaseNotes
    Write-Host "Versiyon: $Version (VERSION.json'dan okundu)" -ForegroundColor Green
    Write-Host "Release Notlari: $ReleaseNotes" -ForegroundColor Gray
} else {
    Write-Host "VERSION.json bulunamadi, varsayilan versiyon kullaniliyor: $Version" -ForegroundColor Yellow
    $ReleaseNotes = ""
}

Write-Host ""

# Dist klasorunu kontrol et
$distPath = Join-Path $scriptRoot "dist"
if (-not (Test-Path $distPath)) {
    Write-Host "dist klasoru bulunamadi: $distPath" -ForegroundColor Red
    Write-Host "Lutfen once 'dotnet publish' komutunu calistirin." -ForegroundColor Yellow
    exit 1
}
Write-Host "dist klasoru bulundu" -ForegroundColor Green

# Release klasorunu olustur
$releasesPath = Join-Path $scriptRoot "releases"
if (-not (Test-Path $releasesPath)) {
    New-Item -ItemType Directory -Path $releasesPath | Out-Null
}

# Dist klasorunu zip'le
$zipFileName = "PinhumanSuperAPP-v$Version.zip"
$zipPath = Join-Path $releasesPath $zipFileName

Write-Host ""
Write-Host "dist klasoru zip'leniyor..." -ForegroundColor Yellow
if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

Compress-Archive -Path "$distPath\*" -DestinationPath $zipPath -Force
Write-Host "Zip dosyasi olusturuldu: $zipPath" -ForegroundColor Green

# Zip dosya boyutunu goster
$zipSize = (Get-Item $zipPath).Length / 1MB
Write-Host "Zip dosya boyutu: $([math]::Round($zipSize, 2)) MB" -ForegroundColor Cyan

Write-Host ""
Write-Host "=== GitHub Release Yukleme ===" -ForegroundColor Cyan

# GitHub CLI kontrolu
$ghInstalled = Get-Command gh -ErrorAction SilentlyContinue
if ($ghInstalled) {
    Write-Host "GitHub CLI bulundu" -ForegroundColor Green
    
    # GitHub CLI ile release olustur
    Write-Host ""
    Write-Host "GitHub Release olusturuluyor..." -ForegroundColor Yellow
    
    $tagName = "v$Version"
    $releaseTitle = "v$Version"
    
    # Release olustur (draft olarak, once kontrol edilsin)
    $releaseNotesEscaped = $ReleaseNotes -replace '"', '\"'
    
    gh release create $tagName `
        --repo "$RepoOwner/$RepoName" `
        --title "$releaseTitle" `
        --notes "$releaseNotesEscaped" `
        "$zipPath" `
        --draft
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host ""
        Write-Host "GitHub Release basariyla olusturuldu!" -ForegroundColor Green
        Write-Host "Release URL: https://github.com/$RepoOwner/$RepoName/releases/tag/$tagName" -ForegroundColor Cyan
        Write-Host ""
        Write-Host "Not: Release draft olarak olusturuldu. GitHub'da kontrol edip publish edebilirsiniz." -ForegroundColor Yellow
    } else {
        Write-Host ""
        Write-Host "GitHub Release olusturulamadi! Hata kodu: $LASTEXITCODE" -ForegroundColor Red
        Write-Host ""
        Write-Host "Manuel yukleme icin:" -ForegroundColor Yellow
        Write-Host "1. https://github.com/$RepoOwner/$RepoName/releases/new adresine gidin" -ForegroundColor Cyan
        Write-Host "2. Tag: v$Version" -ForegroundColor Cyan
        Write-Host "3. Release title: v$Version" -ForegroundColor Cyan
        Write-Host "4. Release notes: $ReleaseNotes" -ForegroundColor Cyan
        Write-Host "5. Zip dosyasini yukleyin: $zipPath" -ForegroundColor Cyan
        exit 1
    }
} else {
    Write-Host "GitHub CLI bulunamadi" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Manuel yukleme talimatlari:" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "1. Su adrese gidin:" -ForegroundColor White
    Write-Host "   https://github.com/$RepoOwner/$RepoName/releases/new" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "2. Su bilgileri doldurun:" -ForegroundColor White
    Write-Host "   - Tag: v$Version" -ForegroundColor Cyan
    Write-Host "   - Release title: v$Version" -ForegroundColor Cyan
    Write-Host "   - Release notes: $ReleaseNotes" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "3. Zip dosyasini yukleyin:" -ForegroundColor White
    Write-Host "   $zipPath" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "4. 'Publish release' butonuna basin" -ForegroundColor White
    Write-Host ""
}

Write-Host ""
Write-Host "=== Tamamlandi ===" -ForegroundColor Green
Write-Host "Zip dosyasi hazir: $zipPath" -ForegroundColor Cyan

