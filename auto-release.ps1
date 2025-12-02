# Otomatik Release Scripti
# Bu script: VERSION.json'dan versiyon okur, zip oluşturur, git commit/push yapar, GitHub Releases'e yükler

param(
    [string]$ProjectDir = "",
    [string]$GitHubToken = "",
    [string]$RepoOwner = "furkanozm",
    [string]$RepoName = "PinhumanSuperAPP-Release",
    [switch]$SkipGit = $false,
    [switch]$SkipGitHub = $false
)

$ErrorActionPreference = "Continue"
$scriptRoot = if ($ProjectDir) { $ProjectDir } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
Set-Location $scriptRoot

Write-Host ""
Write-Host "========================================" -ForegroundColor Magenta
Write-Host "  AUTO-RELEASE.PS1 BAŞLATILIYOR" -ForegroundColor Magenta
Write-Host "========================================" -ForegroundColor Magenta
Write-Host "Zaman: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -ForegroundColor Cyan
Write-Host "Script Root: $scriptRoot" -ForegroundColor Cyan
Write-Host "Çalışma Dizini: $(Get-Location)" -ForegroundColor Cyan
Write-Host "ProjectDir parametresi: $ProjectDir" -ForegroundColor Cyan
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  OTOMATIK RELEASE OLUSTURMA" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# ADIM 1: VERSION.json'dan versiyon oku
Write-Host "[1/4] Versiyon bilgisi okunuyor..." -ForegroundColor Yellow
Write-Host "  VERSION.json aranıyor: $scriptRoot" -ForegroundColor Gray
$versionFile = Join-Path $scriptRoot "VERSION.json"
Write-Host "  Tam yol: $versionFile" -ForegroundColor Gray

if (-not (Test-Path $versionFile)) {
    Write-Host "  ❌ HATA: VERSION.json bulunamadi!" -ForegroundColor Red
    Write-Host "  Aranan yol: $versionFile" -ForegroundColor Red
    Write-Host "  Script root: $scriptRoot" -ForegroundColor Red
    exit 1
}

Write-Host "  ✅ VERSION.json bulundu!" -ForegroundColor Green

$versionData = Get-Content $versionFile | ConvertFrom-Json
$Version = $versionData.Version
$ReleaseNotes = $versionData.ReleaseNotes
$ReleaseDate = if ($versionData.ReleaseDate) { $versionData.ReleaseDate } else { (Get-Date).ToString("yyyy-MM-ddTHH:mm:ss") }

Write-Host "  Versiyon: $Version" -ForegroundColor Green
Write-Host "  Release Notlari: $ReleaseNotes" -ForegroundColor Gray
Write-Host ""

# ADIM 2: dist klasorunu zip'le (kullanıcı verileri hariç)
Write-Host "[2/4] dist klasoru zip'leniyor (kullanici verileri korunuyor)..." -ForegroundColor Yellow
$distPath = Join-Path $scriptRoot "dist"
Write-Host "  Dist klasoru araniyor: $distPath" -ForegroundColor Gray

if (-not (Test-Path $distPath)) {
    Write-Host "  ❌ HATA: dist klasoru bulunamadi!" -ForegroundColor Red
    Write-Host "  Aranan yol: $distPath" -ForegroundColor Red
    Write-Host "  Script root: $scriptRoot" -ForegroundColor Red
    exit 1
}

Write-Host "  ✅ dist klasoru bulundu!" -ForegroundColor Green
$distItemCount = (Get-ChildItem -Path $distPath -Recurse -ErrorAction SilentlyContinue | Measure-Object).Count
Write-Host "  Dosya sayisi: $distItemCount" -ForegroundColor Gray

$releasesPath = Join-Path $scriptRoot "releases"
if (-not (Test-Path $releasesPath)) {
    New-Item -ItemType Directory -Path $releasesPath | Out-Null
}

$zipFileName = "PinhumanSuperAPP-v$Version.zip"
$zipPath = Join-Path $releasesPath $zipFileName

if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

# Kullanıcı verilerini koruyacak dosya listesi
$userDataFiles = @(
    "config.json",
    "firebase-config.json",
    "PinhumanSuperAPP.deps.json",
    "security-profile.json",
    "pdks-config*.json",
    "personnel-config.json",
    "scraping-config.json",
    "mail_history.json",
    "previously_downloaded.json",
    "pin_security.json",
    "remember_me.txt",
    "debug_log.txt",
    "*.db",
    "*.sqlite",
    "*.sqlite3",
    "*.db-journal",
    "*.db-shm",
    "*.db-wal",
    "*.tmp",
    "*.temp",
    "*.bak",
    "*.backup"
)

Write-Host "  Kullanici verileri korunuyor..." -ForegroundColor Gray
Write-Host "  Zip dosyasi olusturuluyor: $zipFileName" -ForegroundColor Gray

# Geçici klasör oluştur ve kullanıcı verilerini dışlayarak kopyala
$tempZipDir = Join-Path $env:TEMP "zip_$(New-Guid)"
New-Item -ItemType Directory -Path $tempZipDir -Force | Out-Null

try {
    # Tüm dosyaları kopyala, ama kullanıcı verilerini hariç tut
    $allFiles = Get-ChildItem -Path $distPath -Recurse -File
    $excludedCount = 0
    
    foreach ($file in $allFiles) {
        $relativePath = $file.FullName.Substring($distPath.Length + 1)
        $shouldExclude = $false
        
        # Kullanıcı veri dosyalarını kontrol et
        foreach ($pattern in $userDataFiles) {
            if ($file.Name -like $pattern -or $relativePath -like "*\$pattern") {
                $shouldExclude = $true
                $excludedCount++
                break
            }
        }
        
        if (-not $shouldExclude) {
            $destPath = Join-Path $tempZipDir $relativePath
            $destDir = Split-Path -Parent $destPath
            if (-not (Test-Path $destDir)) {
                New-Item -ItemType Directory -Path $destDir -Force | Out-Null
            }
            Copy-Item -Path $file.FullName -Destination $destPath -Force
        }
    }
    
    if ($excludedCount -gt 0) {
        Write-Host "  $excludedCount kullanici veri dosyasi korundu (zip'den dislandi)" -ForegroundColor Green
    }
    
    # Zip oluştur
    Compress-Archive -Path "$tempZipDir\*" -DestinationPath $zipPath -Force -CompressionLevel Optimal
    
} finally {
    # Geçici klasörü temizle
    Remove-Item -Path $tempZipDir -Recurse -Force -ErrorAction SilentlyContinue
}

if (-not (Test-Path $zipPath)) {
    Write-Host "  HATA: Zip dosyasi olusturulamadi!" -ForegroundColor Red
    exit 1
}

$zipSize = [math]::Round((Get-Item $zipPath).Length / 1MB, 2)
Write-Host "  Zip dosyasi olusturuldu: $zipPath ($zipSize MB)" -ForegroundColor Green
Write-Host ""

# ADIM 3: Git commit & push
if (-not $SkipGit) {
    Write-Host "[3/4] Git commit ve push yapiliyor..." -ForegroundColor Yellow
    
    # Git durumunu kontrol et
    $gitStatus = git status --porcelain 2>$null
    if ($LASTEXITCODE -ne 0) {
        Write-Host "  UYARI: Git repository degil veya git bulunamadi!" -ForegroundColor Yellow
        Write-Host "  Git islemleri atlaniyor..." -ForegroundColor Yellow
    } else {
        # VERSION.json ve release zip dosyasini git'e ekle
        git add VERSION.json 2>$null
        git add releases/$zipFileName 2>$null
        
        # Commit yap
        if ([string]::IsNullOrWhiteSpace($ReleaseNotes)) {
            $commitMessage = "Release v$Version"
        } else {
            $commitMessage = "Release v$Version - $ReleaseNotes"
        }
        git commit -m "$commitMessage" 2>&1 | Out-Null
        
        if ($LASTEXITCODE -eq 0) {
            Write-Host "  Git commit yapildi: $commitMessage" -ForegroundColor Green
            
            # Tag oluştur (zaten varsa güncelle)
            $tagName = "v$Version"
            git tag -f $tagName 2>&1 | Out-Null
            
            # Push yap (tag ile birlikte)
            Write-Host "  Git push yapiliyor..." -ForegroundColor Gray
            git push origin main 2>&1 | Out-Null
            git push origin $tagName --force 2>&1 | Out-Null
            
            if ($LASTEXITCODE -eq 0) {
                Write-Host "  Git push basarili!" -ForegroundColor Green
            } else {
                Write-Host "  UYARI: Git push basarisiz olabilir (devam ediliyor)" -ForegroundColor Yellow
            }
        } else {
            Write-Host "  UYARI: Git commit basarisiz veya degisiklik yok (devam ediliyor)" -ForegroundColor Yellow
        }
    }
} else {
    Write-Host "[3/4] Git islemleri atlandi (--SkipGit)" -ForegroundColor Gray
}
Write-Host ""

# GitHub REST API ile release oluşturma fonksiyonu
function New-GitHubRelease {
    param(
        [string]$Token,
        [string]$Owner,
        [string]$Repo,
        [string]$Tag,
        [string]$Title,
        [string]$Notes,
        [string]$ZipPath
    )
    
    $apiUrl = "https://api.github.com/repos/$Owner/$Repo/releases"
    
    # Release oluştur
    $releaseBody = @{
        tag_name = $Tag
        name = $Title
        body = $Notes
        draft = $false
        prerelease = $false
    } | ConvertTo-Json
    
    $headers = @{
        "Authorization" = "token $Token"
        "Accept" = "application/vnd.github.v3+json"
        "Content-Type" = "application/json"
    }
    
    try {
        Write-Host "  Release olusturuluyor..." -ForegroundColor Gray
        $response = Invoke-RestMethod -Uri $apiUrl -Method Post -Headers $headers -Body $releaseBody -ContentType "application/json"
        $releaseId = $response.id
        $uploadUrl = $response.upload_url -replace '\{.*\}', ''
        
        Write-Host "  Release olusturuldu (ID: $releaseId)" -ForegroundColor Green
        
        # Upload URL kontrolü
        if ([string]::IsNullOrWhiteSpace($uploadUrl)) {
            Write-Host "  HATA: Upload URL bos veya gecersiz!" -ForegroundColor Red
            Write-Host "  Response upload_url: $($response.upload_url)" -ForegroundColor Red
            return $false
        }
        
        Write-Host "  Upload URL: $uploadUrl" -ForegroundColor Gray
        
        # Zip dosyasını yükle
        if (Test-Path $ZipPath) {
            Write-Host "  Zip dosyasi yukleniyor..." -ForegroundColor Gray
            $zipFileName = Split-Path $ZipPath -Leaf
            
            # URI encoding için dosya adını encode et (PowerShell'de güvenli yöntem)
            $encodedFileName = [System.Uri]::EscapeDataString($zipFileName)
            
            # Upload URL'yi oluştur (string concatenation kullanarak güvenli yöntem)
            if ([string]::IsNullOrWhiteSpace($uploadUrl)) {
                Write-Host "  HATA: Upload URL bos, dosya yuklenemiyor!" -ForegroundColor Red
                return $false
            }
            
            $uploadUrlWithName = $uploadUrl + "?name=" + $encodedFileName
            
            Write-Host "  Dosya adi: $zipFileName" -ForegroundColor Gray
            Write-Host "  Encoded dosya adi: $encodedFileName" -ForegroundColor Gray
            Write-Host "  Upload URL (kontrol): $uploadUrl" -ForegroundColor Gray
            Write-Host "  Tam upload URL: $uploadUrlWithName" -ForegroundColor Gray
            
            # URI geçerliliğini kontrol et
            try {
                $uri = [System.Uri]::new($uploadUrlWithName)
                Write-Host "  URI gecerlilik kontrolu: OK" -ForegroundColor Green
            } catch {
                Write-Host "  HATA: Gecersiz URI: $uploadUrlWithName" -ForegroundColor Red
                Write-Host "  Hata detayi: $($_.Exception.Message)" -ForegroundColor Red
                return $false
            }
            
            $fileBytes = [System.IO.File]::ReadAllBytes($ZipPath)
            $uploadHeaders = @{
                "Authorization" = "token $Token"
                "Accept" = "application/vnd.github.v3+json"
                "Content-Type" = "application/zip"
            }
            
            $uploadResponse = Invoke-RestMethod -Uri $uploadUrlWithName -Method Post -Headers $uploadHeaders -Body $fileBytes
            Write-Host "  Zip dosyasi basariyla yuklendi!" -ForegroundColor Green
        }
        
        return $true
    } catch {
        Write-Host "  HATA: $($_.Exception.Message)" -ForegroundColor Red
        if ($_.ErrorDetails.Message) {
            Write-Host "  Detay: $($_.ErrorDetails.Message)" -ForegroundColor Red
        }
        return $false
    }
}

# ADIM 4: GitHub Releases'e yukle (SADECE REST API, CLI KULLANILMAZ)
if (-not $SkipGitHub) {
    Write-Host "[4/4] GitHub Releases'e yukleniyor..." -ForegroundColor Yellow
    
    $tagName = "v$Version"
    $releaseSuccess = $false

    # GitHub Token kontrolü (parametre veya environment variable)
    $githubToken = $GitHubToken
    if ([string]::IsNullOrWhiteSpace($githubToken)) {
        $githubToken = $env:GITHUB_TOKEN
    }
    if ([string]::IsNullOrWhiteSpace($githubToken)) {
        $githubToken = $env:GH_TOKEN
    }

    if ([string]::IsNullOrWhiteSpace($githubToken)) {
        Write-Host "  HATA: GitHub Token bulunamadi!" -ForegroundColor Red
        Write-Host "  Bu makinede otomatik GitHub Release olusturulmayacak." -ForegroundColor Red
        Write-Host "  Token ayarlamak icin:" -ForegroundColor Cyan
        Write-Host "    - Environment variable: `$env:GITHUB_TOKEN = 'your_token'" -ForegroundColor White
        Write-Host "    - veya script parametresi: -GitHubToken 'your_token'" -ForegroundColor White
        Write-Host "    - Token olusturma: https://github.com/settings/tokens" -ForegroundColor White
        Write-Host ""
        Write-Host "  Manuel yukleme icin GitHub'ta yeni bir release olusturup zip dosyasini ekleyebilirsiniz." -ForegroundColor Yellow
    } else {
        Write-Host "  GitHub Token bulundu, REST API ile upload yapiliyor..." -ForegroundColor Gray
        Write-Host "  Tag: $tagName" -ForegroundColor Gray
        Write-Host "  Repo: $RepoOwner/$RepoName" -ForegroundColor Gray
        Write-Host "  Zip yolu: $zipPath" -ForegroundColor Gray
        if (Test-Path $zipPath) {
            $zipInfo = Get-Item $zipPath
            Write-Host "  Zip boyutu (MB): $([math]::Round($zipInfo.Length / 1MB, 2))" -ForegroundColor Gray
        } else {
            Write-Host "  HATA: Zip dosyasi REST upload oncesi bulunamadi!" -ForegroundColor Red
        }

        $releaseSuccess = New-GitHubRelease -Token $githubToken -Owner $RepoOwner -Repo $RepoName -Tag $tagName -Title "v$Version" -Notes $ReleaseNotes -ZipPath $zipPath
        
        if ($releaseSuccess) {
            Write-Host "  GitHub Release (REST) basariyla olusturuldu!" -ForegroundColor Green
            Write-Host "  Release URL: https://github.com/$RepoOwner/$RepoName/releases/tag/$tagName" -ForegroundColor Cyan
        } else {
            Write-Host "  HATA: GitHub Release (REST) olusturulamadi." -ForegroundColor Red
        }
    }
} else {
    Write-Host "[4/4] GitHub yukleme atlandi (--SkipGitHub)" -ForegroundColor Gray
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "  OTOMATIK RELEASE TAMAMLANDI!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "Olusturulan dosya: $zipPath" -ForegroundColor Cyan

