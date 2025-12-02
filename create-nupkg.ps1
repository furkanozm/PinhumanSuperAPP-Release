# NuGet Paketi Olusturma Scripti
# Bu script, dist klasorundeki dosyalardan NuGet paketi (.nupkg) olusturur

param(
    [string]$Version = "1.0.1",
    [string]$OutputDir = "releases"
)

$ErrorActionPreference = "Stop"
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $scriptRoot

Write-Host "=== NuGet Paketi Olusturma ===" -ForegroundColor Cyan
Write-Host ""

# Versiyon bilgisini VERSION.json'dan oku
$versionFile = Join-Path $scriptRoot "VERSION.json"
if (Test-Path $versionFile) {
    $versionData = Get-Content $versionFile | ConvertFrom-Json
    $Version = $versionData.Version
    Write-Host "Versiyon: $Version (VERSION.json'dan okundu)" -ForegroundColor Green
}

# Output klasorunu olustur
$outputPath = Join-Path $scriptRoot $OutputDir
if (-not (Test-Path $outputPath)) {
    New-Item -ItemType Directory -Path $outputPath | Out-Null
}

# Gecici klasor olustur
$tempDir = Join-Path $env:TEMP "nupkg_$(New-Guid)"
New-Item -ItemType Directory -Path $tempDir | Out-Null

try {
    # NuGet paket yapisini olustur
    $libDir = Join-Path $tempDir "lib\net45"
    New-Item -ItemType Directory -Path $libDir -Force | Out-Null
    
    # dist klasorundeki dosyalari kopyala
    $distPath = Join-Path $scriptRoot "dist"
    Write-Host "dist klasorundeki dosyalar kopyalaniyor..." -ForegroundColor Yellow
    
    # Tum dosyalari kopyala (alt klasorler dahil)
    Copy-Item -Path "$distPath\*" -Destination $libDir -Recurse -Force -Exclude "dist.zip"
    
    # .nuspec dosyasini olustur
    $nuspecPath = Join-Path $tempDir "PinhumanSuperAPP.nuspec"
    $nuspecXml = '<?xml version="1.0" encoding="utf-8"?>'
    $nuspecXml += [Environment]::NewLine
    $nuspecXml += '<package xmlns="http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd">'
    $nuspecXml += [Environment]::NewLine
    $nuspecXml += '  <metadata>'
    $nuspecXml += [Environment]::NewLine
    $nuspecXml += '    <id>PinhumanSuperAPP</id>'
    $nuspecXml += [Environment]::NewLine
    $nuspecXml += "    <version>$Version</version>"
    $nuspecXml += [Environment]::NewLine
    $nuspecXml += '    <title>PinhumanSuperAPP</title>'
    $nuspecXml += [Environment]::NewLine
    $nuspecXml += '    <authors>Pinhuman</authors>'
    $nuspecXml += [Environment]::NewLine
    $nuspecXml += '    <owners>Pinhuman</owners>'
    $nuspecXml += [Environment]::NewLine
    $nuspecXml += '    <requireLicenseAcceptance>false</requireLicenseAcceptance>'
    $nuspecXml += [Environment]::NewLine
    $nuspecXml += '    <description>PinhumanSuperAPP Desktop Application</description>'
    $nuspecXml += [Environment]::NewLine
    $nuspecXml += '    <releaseNotes>Kilit ekranindaki kilit iconuna basinca acilan modal kaldirildi</releaseNotes>'
    $nuspecXml += [Environment]::NewLine
    $nuspecXml += '  </metadata>'
    $nuspecXml += [Environment]::NewLine
    $nuspecXml += '</package>'
    
    Set-Content -Path $nuspecPath -Value $nuspecXml -Encoding UTF8
    
    # NuGet paketi olustur (zip olarak)
    Write-Host "NuGet paketi (.nupkg) olusturuluyor..." -ForegroundColor Yellow
    $zipPath = Join-Path $outputPath "PinhumanSuperAPP.$Version.zip"
    $nupkgPath = Join-Path $outputPath "PinhumanSuperAPP.$Version.nupkg"
    
    # .nupkg aslinda bir zip dosyasidir, once .zip olarak olustur
    Compress-Archive -Path "$tempDir\*" -DestinationPath $zipPath -Force
    # Sonra .nupkg olarak yeniden adlandir
    Move-Item -Path $zipPath -Destination $nupkgPath -Force
    
    Write-Host "NuGet paketi olusturuldu: $nupkgPath" -ForegroundColor Green
} finally {
    # Gecici klasoru temizle
    Remove-Item -Path $tempDir -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "NuGet paketi hazir: $nupkgPath" -ForegroundColor Cyan
