# Squirrel.Windows CLI Kurulum Scripti
# Bu script, Squirrel.Windows CLI'yi GitHub'dan indirip tools klasörüne koyar

Write-Host "Squirrel.Windows CLI kurulumu başlatılıyor..." -ForegroundColor Cyan

# Tools klasörünü oluştur
$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
$toolsDir = Join-Path $scriptPath "tools"
if (-not (Test-Path $toolsDir)) {
    New-Item -ItemType Directory -Path $toolsDir | Out-Null
    Write-Host "✓ Tools klasörü oluşturuldu" -ForegroundColor Green
}

# Squirrel.Windows'u indir
$squirrelVersion = "2.0.1"
$downloadUrl = "https://github.com/Squirrel/Squirrel.Windows/releases/download/$squirrelVersion/Squirrel.Windows.$squirrelVersion.zip"
$zipPath = Join-Path $toolsDir "Squirrel.Windows.zip"

Write-Host "Squirrel.Windows $squirrelVersion indiriliyor..." -ForegroundColor Yellow
$ProgressPreference = 'SilentlyContinue'
Invoke-WebRequest -Uri $downloadUrl -OutFile $zipPath -UseBasicParsing -ErrorAction Stop
Write-Host "✓ İndirme tamamlandı" -ForegroundColor Green

# Zip dosyasını aç
Write-Host "Zip dosyası açılıyor..." -ForegroundColor Yellow
Expand-Archive -Path $zipPath -DestinationPath $toolsDir -Force -ErrorAction Stop
Write-Host "✓ Zip dosyası açıldı" -ForegroundColor Green

# Squirrel.exe dosyasını bul
$squirrelExe = Get-ChildItem -Path $toolsDir -Filter "Squirrel.exe" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1

if ($squirrelExe) {
    Write-Host "✓ Squirrel.exe bulundu: $($squirrelExe.FullName)" -ForegroundColor Green
    Write-Host ""
    Write-Host "Kurulum tamamlandı! Squirrel.Windows kullanıma hazır." -ForegroundColor Green
    Write-Host ""
    Write-Host "Squirrel.exe konumu:" -ForegroundColor Cyan
    Write-Host $squirrelExe.FullName -ForegroundColor White
} else {
    Write-Host "✗ Squirrel.exe bulunamadı!" -ForegroundColor Red
    Write-Host "Zip dosyasının içeriğini kontrol edin: $toolsDir" -ForegroundColor Yellow
}

# Zip dosyasını sil (isteğe bağlı)
Remove-Item $zipPath -Force -ErrorAction SilentlyContinue

