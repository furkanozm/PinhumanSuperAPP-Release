# Publish sonrasi klasorleri duzenleme script'i
# VERSION.json dosyasini otomatik gunceller
# Her zaman C:\BuildOutput\PinhumanSuperAPP_Publish klasorune cikar

param(
    [string]$PublishPath = ""
)

# Tum hatalari yakalamak icin error action preference - EN BASTA
$ErrorActionPreference = "Continue"
$script:ScriptSuccess = $true
$script:ExitCode = 0

# Log dosyasi yolu (script baslangicinda ayarlanacak)
$script:LogFilePath = $null

# Global hata yakalama - script her zaman basarili cikis yapsin
trap {
    $errorMsg = "[HATA] TRAP: Beklenmeyen hata yakalandi: $($_.Exception.Message)"
    Write-Host $errorMsg -ForegroundColor Red
    Write-Host "TRAP: Hata konumu: $($_.InvocationInfo.ScriptLineNumber) satir" -ForegroundColor Yellow
    Write-Host "TRAP: Hata detayi: $($_.Exception.GetType().FullName)" -ForegroundColor Yellow
    if ($script:LogFilePath) {
        Add-Content -Path $script:LogFilePath -Value "[ERROR] $errorMsg" -Encoding UTF8 | Out-Null
    }
    $script:ScriptSuccess = $false
    continue
}

# Helper fonksiyon - Hem renkli terminal, MSBuild ve log dosyasi icin
function Write-LogBoth {
    param([string]$Msg, [string]$Color = "White")
    
    # Terminal'e yaz (renkli)
    Write-Host $Msg -ForegroundColor $Color
    
    # MSBuild icin stdout'a yaz (pipeline'a gitmemesi icin Out-Null kullan)
    [Console]::Out.WriteLine($Msg) | Out-Null
    
    # Log dosyasina yaz (eger varsa) - terminale yazilmaz
    if ($script:LogFilePath) {
        try {
            # Renk kodlarini temizle ve dosyaya yaz (sessizce, terminale yazilmaz)
            $cleanMsg = $Msg -replace '\x1b\[[0-9;]*m', ''
            $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
            Add-Content -Path $script:LogFilePath -Value "[$timestamp] $cleanMsg" -Encoding UTF8 -ErrorAction SilentlyContinue | Out-Null
        } catch {
            # Log dosyasina yazma hatasi sessizce yok sayilir
        }
    }
}

# Log dosyasi olusturma fonksiyonu
function Initialize-LogFile {
    param([string]$ScriptRoot)
    
    try {
        # Log dosyasini kok dizine yaz
        $timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
        $logFileName = "publish_$timestamp.log"
        $script:LogFilePath = Join-Path $ScriptRoot $logFileName
        
        # Log dosyasi basligi
        $header = @"
========================================
Publish Log Dosyasi
Olusturulma: $(Get-Date -Format "yyyy-MM-dd HH:mm:ss")
PowerShell Versiyonu: $($PSVersionTable.PSVersion)
========================================

"@
        Set-Content -Path $script:LogFilePath -Value $header -Encoding UTF8 | Out-Null
        
        # Log dosyasi sessizce olusturuldu (terminale yazilmaz)
        return $script:LogFilePath
    } catch {
        Write-Host "[UYARI] Log dosyasi olusturulamadi: $($_.Exception.Message)" -ForegroundColor Yellow
        $script:LogFilePath = $null
        return $null
    }
}

# VERSION.json dosyasini otomatik guncelle
function Update-VersionFile {
    param(
        [Parameter(Mandatory=$true)][string]$VersionFilePath
    )
    
    Write-Host ""
    Write-Host "=== VERSION.json GUNCELLEME FONKSIYONU BASLATILIYOR ===" -ForegroundColor Cyan
    Write-Host "Dosya yolu: $VersionFilePath" -ForegroundColor Gray
    
    try {
        # Dosya yolunun var olup olmadigini kontrol et
        $versionFileDir = Split-Path -Parent $VersionFilePath
        if (![string]::IsNullOrWhiteSpace($versionFileDir) -and !(Test-Path $versionFileDir)) {
            Write-Host "[UYARI] Klasor bulunamadi, olusturuluyor: $versionFileDir" -ForegroundColor Yellow
            New-Item -ItemType Directory -Path $versionFileDir -Force | Out-Null
        }
        
        if (!(Test-Path $VersionFilePath)) {
            Write-Host "[BILGI] VERSION.json bulunamadi, olusturuluyor..." -ForegroundColor Yellow
            Write-Host "   Konum: $VersionFilePath" -ForegroundColor Gray
            
            $defaultVersion = @{
                Version = "1.0.0"
                ReleaseDate = (Get-Date -Format "yyyy-MM-ddTHH:mm:ss")
                ReleaseNotes = "Ilk surum"
            } | ConvertTo-Json
            
            Write-Host "[BILGI] Varsayilan versiyon olusturuluyor: 1.0.0" -ForegroundColor Gray
            Set-Content -Path $VersionFilePath -Value $defaultVersion -Encoding UTF8
            Write-Host "[OK] VERSION.json olusturuldu: 1.0.0" -ForegroundColor Green
            
            $createdContent = Get-Content -Path $VersionFilePath -Raw
            Write-Host "[BILGI] Olusturulan dosya icerigi:" -ForegroundColor Gray
            Write-Host $createdContent -ForegroundColor DarkGray
            return
        }
        
        Write-Host "[BILGI] VERSION.json dosyasi bulundu, okunuyor..." -ForegroundColor Green
        
        # Mevcut versiyonu oku
        Write-Host "   Dosya okunuyor: $VersionFilePath" -ForegroundColor Gray
        $versionContent = Get-Content -Path $VersionFilePath -Raw -Encoding UTF8
        
        if ([string]::IsNullOrWhiteSpace($versionContent)) {
            Write-Host "[UYARI] VERSION.json dosyasi bos!" -ForegroundColor Yellow
            Write-Host "   Varsayilan versiyon olusturuluyor..." -ForegroundColor Gray
            $defaultVersion = @{
                Version = "1.0.0"
                ReleaseDate = (Get-Date -Format "yyyy-MM-ddTHH:mm:ss")
                ReleaseNotes = "Ilk surum"
            } | ConvertTo-Json
            Set-Content -Path $VersionFilePath -Value $defaultVersion -Encoding UTF8
            Write-Host "[OK] VERSION.json yeniden olusturuldu: 1.0.0" -ForegroundColor Green
            return
        }
        
        Write-Host "[BILGI] Mevcut dosya icerigi:" -ForegroundColor Gray
        Write-Host $versionContent -ForegroundColor DarkGray
        
        Write-Host "[BILGI] JSON parse ediliyor..." -ForegroundColor Gray
        $versionData = $versionContent | ConvertFrom-Json
        
        if ($null -eq $versionData) {
            Write-Host "[HATA] VERSION.json parse edilemedi!" -ForegroundColor Red
            $script:ScriptSuccess = $false
            return
        }
        
        $currentVersion = $versionData.Version
        Write-Host "[BILGI] Mevcut versiyon: $currentVersion" -ForegroundColor Cyan
        Write-Host "   Release Date: $($versionData.ReleaseDate)" -ForegroundColor Gray
        Write-Host "   Release Notes: $($versionData.ReleaseNotes)" -ForegroundColor Gray
        
        if ([string]::IsNullOrWhiteSpace($currentVersion)) {
            Write-Host "[UYARI] Versiyon numarasi bos! Varsayilan 1.0.0 kullaniliyor." -ForegroundColor Yellow
            $currentVersion = "1.0.0"
            $versionData.Version = "1.0.0"
        }
        
        # Versiyon numarasini parcala (orn: "1.2.3")
        Write-Host "[BILGI] Versiyon numarasi parse ediliyor: $currentVersion" -ForegroundColor Gray
        $versionParts = $currentVersion -split '\.'
        
        Write-Host "   Versiyon parcalari: $($versionParts -join ', ')" -ForegroundColor Gray
        
        if ($versionParts.Length -ge 3) {
            try {
                $major = [int]$versionParts[0]
                $minor = [int]$versionParts[1]
                $patch = [int]$versionParts[2]
                
                Write-Host "   Major: $major, Minor: $minor, Patch: $patch" -ForegroundColor Gray
                
                # Patch versiyonunu artir (orn: 1.2.3 -> 1.2.4)
                $oldPatch = $patch
                $patch++
                $newVersion = "$major.$minor.$patch"
                
                Write-Host "[BILGI] Patch versiyonu artiriliyor: $oldPatch -> $patch" -ForegroundColor Cyan
                
                # Versiyonu guncelle
                $versionData.Version = $newVersion
                $versionData.ReleaseDate = (Get-Date -Format "yyyy-MM-ddTHH:mm:ss")
                $versionData.ReleaseNotes = ""
                
                Write-Host "[BILGI] Guncellenmis veri:" -ForegroundColor Gray
                Write-Host "   Versiyon: $newVersion" -ForegroundColor Gray
                Write-Host "   Release Date: $($versionData.ReleaseDate)" -ForegroundColor Gray
                Write-Host "   Release Notes: (bos - kullanici dolduracak)" -ForegroundColor Gray
                
                # JSON'a kaydet
                $updatedContent = $versionData | ConvertTo-Json -Depth 10
                Write-Host "Dosyaya yaziliyor..." -ForegroundColor Gray
                Set-Content -Path $VersionFilePath -Value $updatedContent -Encoding UTF8
                
                # Yazilan dosyanin icerigini dogrula
                $verifyContent = Get-Content -Path $VersionFilePath -Raw
                $verifyData = $verifyContent | ConvertFrom-Json
                if ($verifyData.Version -eq $newVersion) {
                    Write-Host "[OK] VERSION.json guncellendi: $currentVersion -> $newVersion" -ForegroundColor Green
                    Write-Host "   Dogrulama: Basarili" -ForegroundColor Green
                } else {
                    Write-Host "[UYARI] Versiyon guncellemesi dogrulanamadi!" -ForegroundColor Yellow
                    Write-Host "   Beklenen: $newVersion, Okunan: $($verifyData.Version)" -ForegroundColor Yellow
                }
            }
            catch {
                Write-Host "[HATA] Versiyon numarasi parse edilemedi: $($_.Exception.Message)" -ForegroundColor Red
                Write-Host "   Hata detayi: $($_.Exception.GetType().FullName)" -ForegroundColor Red
                Write-Host "   Stack trace: $($_.ScriptStackTrace)" -ForegroundColor Red
                $script:ScriptSuccess = $false
            }
        }
        else {
            Write-Host "[UYARI] Versiyon formati gecersiz! Format: X.Y.Z olmali (orn: 1.0.1)" -ForegroundColor Yellow
            Write-Host "   Mevcut format: $currentVersion (Parca sayisi: $($versionParts.Length))" -ForegroundColor Yellow
            $script:ScriptSuccess = $false
        }
    }
    catch {
        Write-Host "[HATA] VERSION.json guncellenirken hata: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host "   Hata tipi: $($_.Exception.GetType().FullName)" -ForegroundColor Red
        Write-Host "   Dosya yolu: $VersionFilePath" -ForegroundColor Red
        Write-Host "   Stack trace: $($_.ScriptStackTrace)" -ForegroundColor Red
        if ($_.Exception.InnerException) {
            Write-Host "   Ic hata: $($_.Exception.InnerException.Message)" -ForegroundColor Red
        }
        $script:ScriptSuccess = $false
    }
    
    Write-Host "=== VERSION.json GUNCELLEME FONKSIYONU TAMAMLANDI ===" -ForegroundColor Cyan
    Write-Host ""
}

# Python kurulum dosyalarini kopyalama fonksiyonu
function Copy-PythonInstallers {
    param(
        [Parameter(Mandatory=$true)][string]$TargetRoot,
        [Parameter(Mandatory=$true)][string]$ScriptRoot
    )

    try {
        # Null kontrolü
        if ([string]::IsNullOrWhiteSpace($TargetRoot)) {
            Write-Host "[UYARI] TargetRoot bos, Python dosyalari kopyalanamadi" -ForegroundColor Yellow
            return
        }
        
        # Hedef klasor
        if (!(Test-Path $TargetRoot)) {
            New-Item -ItemType Directory -Path $TargetRoot -Force | Out-Null
        }
        $piFolder = Join-Path $TargetRoot "python_installer"
        if (!(Test-Path $piFolder)) {
            New-Item -ItemType Directory -Path $piFolder -Force | Out-Null
        }

        # Kaynaklari olasi isim/konumlara gore bul
        $exeCandidates = @(
            (Join-Path -Path $ScriptRoot -ChildPath "python-installer.exe"),
            (Join-Path -Path $ScriptRoot -ChildPath "python-insteller.exe"),
            (Join-Path -Path $ScriptRoot -ChildPath "python_installer\python-installer.exe"),
            (Join-Path -Path $ScriptRoot -ChildPath "python_installer\python-insteller.exe")
        )
        $pyInstaller = $exeCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
        if ($pyInstaller) {
            Copy-Item -Path $pyInstaller -Destination (Join-Path $piFolder "python-installer.exe") -Force
            $destPath = Join-Path $piFolder "python-installer.exe"
            Write-Host "Kopyalandi: $pyInstaller -> $destPath"
        } else {
            Write-Host "Uyari: python-installer.exe bulunamadi."
        }

        $pySetupCandidates = @(
            (Join-Path -Path $ScriptRoot -ChildPath "python-3.14.0-amd64.exe"),
            (Join-Path -Path $ScriptRoot -ChildPath "python_installer\python-3.14.0-amd64.exe")
        )
        $pySetup = $pySetupCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
        if ($pySetup) {
            Copy-Item -Path $pySetup -Destination (Join-Path $piFolder "python-3.14.0-amd64.exe") -Force
            $destPath2 = Join-Path $piFolder "python-3.14.0-amd64.exe"
            Write-Host "Kopyalandi: $pySetup -> $destPath2"
        } else {
            Write-Host "Uyari: python-3.14.0-amd64.exe bulunamadi."
        }
    }
    catch {
        Write-Host "Python kurulum dosyalari kopyalanirken hata: $($_.Exception.Message)"
        $script:ScriptSuccess = $false
    }
}

# Publish ortamini baslat
function Initialize-PublishEnvironment {
    param(
        [Parameter(Mandatory=$true)][string]$PublishPath,
        [Parameter(Mandatory=$true)][string]$ScriptRoot
    )
    
    Write-LogBoth "=== PUBLISH ORTAMI BASLATILIYOR ===" "Cyan"
    Write-LogBoth "PublishPath: $PublishPath" "Gray"
    Write-LogBoth "ScriptRoot: $ScriptRoot" "Gray"
    Write-LogBoth ""
    
    # PublishPath bossa veya goreceli yol ise, her zaman C:\BuildOutput\PinhumanSuperAPP_Publish kullan
    if ([string]::IsNullOrWhiteSpace($PublishPath) -or ![System.IO.Path]::IsPathRooted($PublishPath)) {
        $PublishPath = "C:\BuildOutput\PinhumanSuperAPP_Publish"
        Write-Host "Publish klasoru (varsayilan): $PublishPath"
    } else {
        Write-Host "Publish klasoru (parametreden): $PublishPath"
    }

    Write-Host "Tam yol: $PublishPath"

    if (!(Test-Path $PublishPath)) {
        Write-Host "[UYARI] Publish klasoru bulunamadi, olusturuluyor: $PublishPath"
        try {
            New-Item -ItemType Directory -Path $PublishPath -Force | Out-Null
            Write-Host "[OK] Publish klasoru olusturuldu"
            $script:SkipPublishOperations = $false
        }
        catch {
            Write-Host "[HATA] HATA: Publish klasoru olusturulamadi: $($_.Exception.Message)"
            Write-Host "[UYARI] Script devam ediyor ancak bazi islemler atlanacak..."
            $script:SkipPublishOperations = $true
            $script:ScriptSuccess = $false
        }
    } else {
        Write-Host "[OK] Publish klasoru bulundu"
        $script:SkipPublishOperations = $false
    }
    
    # Return değerini açıkça belirt (Write-LogBoth çıktısı ile karışmaması için)
    $PublishPath | Out-Null
    return $PublishPath
}

# VERSION dosyalarini kopyala
function Copy-VersionFiles {
    param(
        [Parameter(Mandatory=$true)][string]$ScriptRoot,
        [Parameter(Mandatory=$true)][string]$PublishPath,
        [Parameter(Mandatory=$true)][string]$ProjectDist
    )
    
    Write-Host ""
    Write-Host "=== VERSION DOSYALARI KOPYALAMA ===" -ForegroundColor Cyan
    $versionFiles = @("VERSION.json", "UPDATE_NOTES.json")
    Write-Host "Script kok: $ScriptRoot" -ForegroundColor Gray
    Write-Host "Dist klasoru: $ProjectDist" -ForegroundColor Gray
    Write-Host "Publish klasoru: $PublishPath" -ForegroundColor Gray
    
    foreach ($versionFile in $versionFiles) {
        Write-Host ""
        Write-Host "[BILGI] Isleniyor: $versionFile" -ForegroundColor Cyan
        $sourceFile = Join-Path $ScriptRoot $versionFile
        
        if (Test-Path $sourceFile) {
            $sourceInfo = Get-Item $sourceFile
            Write-Host "   Kaynak dosya bulundu: $sourceFile" -ForegroundColor Green
            Write-Host "   Dosya boyutu: $($sourceInfo.Length) bytes" -ForegroundColor Gray
            Write-Host "   Son degistirme: $($sourceInfo.LastWriteTime)" -ForegroundColor Gray
            
            # VERSION.json icin icerigi goster
            if ($versionFile -eq "VERSION.json") {
                try {
                    $sourceContent = Get-Content -Path $sourceFile -Raw -ErrorAction Stop
                    $sourceData = $sourceContent | ConvertFrom-Json -ErrorAction Stop
                    if ($sourceData.Version) {
                        Write-Host "   Versiyon: $($sourceData.Version)" -ForegroundColor Cyan
                    }
                }
                catch {
                    Write-Host "   [UYARI] VERSION.json icerigi okunamadi: $($_.Exception.Message)" -ForegroundColor Yellow
                }
            }
            
            # Publish klasorune kopyala (sadece klasor varsa ve PublishPath geçerli ise)
            if (!$script:SkipPublishOperations -and ![string]::IsNullOrWhiteSpace($PublishPath) -and (Test-Path $PublishPath)) {
                $publishDest = Join-Path $PublishPath $versionFile
                
                # Aynı dosyaya kopyalamaya çalışıyorsa atla
                if ($publishDest -ne $sourceFile) {
                    Write-Host "   [BILGI] Publish klasorune kopyalaniyor: $publishDest" -ForegroundColor Gray
                    try {
                        Copy-Item -Path $sourceFile -Destination $publishDest -Force -ErrorAction Stop
                        Write-Host "   [OK] Kopyalandi: $versionFile -> Publish klasorune" -ForegroundColor Green
                        
                        # Dogrulama
                        if (Test-Path $publishDest) {
                            $destInfo = Get-Item $publishDest
                            if ($destInfo.Length -eq $sourceInfo.Length) {
                                Write-Host "   [OK] Dogrulama: Dosya boyutu eslesiyor" -ForegroundColor Green
                            } else {
                                Write-Host "   [UYARI] Dogrulama: Dosya boyutu farkli! (Kaynak: $($sourceInfo.Length), Hedef: $($destInfo.Length))" -ForegroundColor Yellow
                            }
                        }
                    }
                    catch {
                        Write-Host "   [HATA] UYARI: $versionFile Publish klasorune kopyalanamadi: $($_.Exception.Message)" -ForegroundColor Red
                        Write-Host "      Hata tipi: $($_.Exception.GetType().FullName)" -ForegroundColor Red
                        $script:ScriptSuccess = $false
                    }
                } else {
                    Write-Host "   [BILGI] Publish klasorune kopyalama atlandi (kaynak ve hedef ayni)" -ForegroundColor Yellow
                }
            } else {
                Write-Host "   [BILGI] Publish klasoru islemleri atlandi (SkipPublishOperations veya PublishPath gecersiz)" -ForegroundColor Yellow
            }
            
            # Dist klasorune de kopyala (hata olsa bile devam et)
            if (Test-Path $ProjectDist) {
                $distDest = Join-Path $ProjectDist $versionFile
                Write-Host "   [BILGI] Dist klasorune kopyalaniyor: $distDest" -ForegroundColor Gray
                try {
                    Copy-Item -Path $sourceFile -Destination $distDest -Force -ErrorAction Stop
                    Write-Host "   [OK] Kopyalandi: $versionFile -> dist klasorune" -ForegroundColor Green
                    
                    # Dogrulama
                    if (Test-Path $distDest) {
                        $destInfo = Get-Item $distDest
                        if ($destInfo.Length -eq $sourceInfo.Length) {
                            Write-Host "   [OK] Dogrulama: Dosya boyutu eslesiyor" -ForegroundColor Green
                        } else {
                            Write-Host "   [UYARI] Dogrulama: Dosya boyutu farkli! (Kaynak: $($sourceInfo.Length), Hedef: $($destInfo.Length))" -ForegroundColor Yellow
                        }
                    }
                }
                catch {
                    Write-Host "   [UYARI] UYARI: $versionFile dist klasorune kopyalanamadi (dosya kilitli olabilir - uygulamayi kapatin): $($_.Exception.Message)" -ForegroundColor Yellow
                    Write-Host "      Bu normal olabilir, eger uygulama dist klasorunden calisiyorsa." -ForegroundColor Gray
                    Write-Host "      Hata tipi: $($_.Exception.GetType().FullName)" -ForegroundColor Gray
                    $script:ScriptSuccess = $false
                }
            } else {
                Write-Host "   [UYARI] Dist klasoru bulunamadi: $ProjectDist" -ForegroundColor Yellow
            }
        } else {
            Write-Host "   [UYARI] Uyari: $versionFile bulunamadi: $sourceFile" -ForegroundColor Yellow
        }
    }
    Write-Host ""
}

# Publish dosyalarini organize et
function Organize-PublishFiles {
    param(
        [Parameter(Mandatory=$true)][string]$PublishPath
    )
    
    if ($script:SkipPublishOperations) {
        return
    }
    
    # Runtime dosyalarini runtime klasorune tasi
    try {
        if (!(Test-Path $PublishPath)) {
            Write-Host "[UYARI] PublishPath bulunamadi: $PublishPath" -ForegroundColor Yellow
            return
        }
        
        $runtimeFiles = @()
        $exeFiles = Get-ChildItem -Path $PublishPath -Filter "*.exe" -File -ErrorAction SilentlyContinue
        if ($exeFiles) {
            $runtimeFiles += $exeFiles | Where-Object { $_.Name -notlike "PinhumanSuperAPP.exe" }
        }
        $pdbFiles = Get-ChildItem -Path $PublishPath -Filter "*.pdb" -File -ErrorAction SilentlyContinue
        if ($pdbFiles) {
            $runtimeFiles += $pdbFiles
        }
        $xmlFiles = Get-ChildItem -Path $PublishPath -Filter "*.xml" -File -ErrorAction SilentlyContinue
        if ($xmlFiles) {
            $runtimeFiles += $xmlFiles
        }

        if ($runtimeFiles.Count -gt 0) {
            $runtimePath = Join-Path $PublishPath "runtime"
            if (!(Test-Path $runtimePath)) {
                New-Item -ItemType Directory -Path $runtimePath -Force | Out-Null
            }

            foreach ($file in $runtimeFiles) {
                try {
                    $destination = Join-Path $runtimePath $file.Name
                    Move-Item -Path $file.FullName -Destination $destination -Force -ErrorAction SilentlyContinue
                    Write-Host "Tasindi: $($file.Name) -> runtime\"
                } catch {
                    Write-Host "[UYARI] Dosya tasinamadi: $($file.Name) - $($_.Exception.Message)" -ForegroundColor Yellow
                }
            }
        }
    }
    catch {
        Write-Host "[UYARI] Runtime dosyalari tasinirken hata: $($_.Exception.Message)"
        $script:ScriptSuccess = $false
    }

    # Resource dosyalarini resources klasorune tasi
    try {
        if (!(Test-Path $PublishPath)) {
            Write-Host "[UYARI] PublishPath bulunamadi: $PublishPath" -ForegroundColor Yellow
            return
        }
        
        $resourceFiles = @()
        $resDllFiles = Get-ChildItem -Path $PublishPath -Filter "*.resources.dll" -File -ErrorAction SilentlyContinue
        if ($resDllFiles) {
            $resourceFiles += $resDllFiles
        }
        $icoFiles = Get-ChildItem -Path $PublishPath -Filter "*.ico" -File -ErrorAction SilentlyContinue
        if ($icoFiles) {
            $resourceFiles += $icoFiles
        }
        $pngFiles = Get-ChildItem -Path $PublishPath -Filter "*.png" -File -ErrorAction SilentlyContinue
        if ($pngFiles) {
            $resourceFiles += $pngFiles
        }

        if ($resourceFiles.Count -gt 0) {
            $resourcesPath = Join-Path $PublishPath "resources"
            if (!(Test-Path $resourcesPath)) {
                New-Item -ItemType Directory -Path $resourcesPath -Force | Out-Null
            }

            foreach ($file in $resourceFiles) {
                try {
                    $destination = Join-Path $resourcesPath $file.Name
                    Move-Item -Path $file.FullName -Destination $destination -Force -ErrorAction SilentlyContinue
                    Write-Host "Tasindi: $($file.Name) -> resources\"
                } catch {
                    Write-Host "[UYARI] Dosya tasinamadi: $($file.Name) - $($_.Exception.Message)" -ForegroundColor Yellow
                }
            }
        }
    }
    catch {
        Write-Host "[UYARI] Resource dosyalari tasinirken hata: $($_.Exception.Message)"
        $script:ScriptSuccess = $false
    }

    # Fonts klasorunu olustur ve tasi
    try {
        if (!(Test-Path $PublishPath)) {
            Write-Host "[UYARI] PublishPath bulunamadi: $PublishPath" -ForegroundColor Yellow
            return
        }
        
        $fontsPath = Join-Path $PublishPath "Fonts"
        if (Test-Path $fontsPath) {
            $fontsDestPath = Join-Path $PublishPath "resources\Fonts"
            if (!(Test-Path $fontsDestPath)) {
                New-Item -ItemType Directory -Path $fontsDestPath -Force | Out-Null
            }
            Move-Item -Path $fontsPath -Destination $fontsDestPath -Force -ErrorAction SilentlyContinue
            Write-Host "Tasindi: Fonts\ -> resources\Fonts\"
        }
    }
    catch {
        Write-Host "[UYARI] Fonts klasoru tasinirken hata: $($_.Exception.Message)"
        $script:ScriptSuccess = $false
    }
}

# Dist klasorune tum publish dosyalarini kopyala (.NET runtime dahil)
function Copy-PublishToDist {
    param(
        [Parameter(Mandatory=$true)][string]$PublishPath,
        [Parameter(Mandatory=$true)][string]$ScriptRoot
    )
    
    Write-Host ""
    Write-Host "=== DIST KLASORUNE PUBLISH DOSYALARI KOPYALAMA ===" -ForegroundColor Cyan
    Write-Host "Publish klasoru: $PublishPath" -ForegroundColor Gray
    Write-Host "Script root: $ScriptRoot" -ForegroundColor Gray
    
    $projectDist = Join-Path $ScriptRoot "dist"
    
    if (-not (Test-Path $PublishPath)) {
        Write-Host "[UYARI] Publish klasoru bulunamadi: $PublishPath" -ForegroundColor Yellow
        Write-Host "  Dist klasorune kopyalama atlaniyor..." -ForegroundColor Yellow
        return
    }
    
    if (-not (Test-Path $projectDist)) {
        Write-Host "[BILGI] Dist klasoru bulunamadi, olusturuluyor: $projectDist" -ForegroundColor Yellow
        New-Item -ItemType Directory -Path $projectDist -Force | Out-Null
    }
    
    Write-Host "[BILGI] Publish dosyalari dist klasorune kopyalaniyor..." -ForegroundColor Cyan
    Write-Host "  Kaynak: $PublishPath" -ForegroundColor Gray
    Write-Host "  Hedef: $projectDist" -ForegroundColor Gray
    
    try {
        # Önce mevcut dist klasörünü temizle (eğer varsa)
        if (Test-Path $projectDist) {
            Write-Host "  [BILGI] Mevcut dist klasoru temizleniyor..." -ForegroundColor Gray
            Remove-Item -Path $projectDist -Recurse -Force -ErrorAction SilentlyContinue
        }
        
        # Dist klasörünü yeniden oluştur
        New-Item -ItemType Directory -Path $projectDist -Force | Out-Null
        
        # Robocopy kullanarak dosyaları kopyala (dosya adları tam olarak korunur)
        Write-Host "  [BILGI] Dosyalar kopyalaniyor (dosya adlari korunuyor - robocopy kullaniliyor)..." -ForegroundColor Gray
        
        $copiedCount = 0
        $skippedCount = 0
        
        try {
            # Robocopy ile kopyalama (dosya adları tam olarak korunur, encoding sorunları olmaz)
            # /E: Alt klasörleri dahil et
            # /COPYALL: Tüm dosya özelliklerini kopyala
            # /R:3: 3 kez deneme
            # /W:1: 1 saniye bekleme
            # /NFL: Dosya listesi yok
            # /NDL: Klasör listesi yok
            # /NP: İlerleme yok
            $robocopyResult = & robocopy $PublishPath $projectDist /E /COPYALL /R:3 /W:1 /NFL /NDL /NP
            
            # Robocopy çıkış kodunu kontrol et (0-7 başarılı, 8+ hata)
            if ($LASTEXITCODE -le 7) {
                # Kopyalanan dosya sayısını say
                $copiedCount = (Get-ChildItem -Path $projectDist -Recurse -File -ErrorAction SilentlyContinue | Measure-Object).Count
                Write-Host "  [OK] Robocopy ile kopyalama basarili!" -ForegroundColor Green
            } else {
                throw "Robocopy hatasi: Cikis kodu $LASTEXITCODE"
            }
            
        } catch {
            # Robocopy başarısız olursa, Copy-Item ile kopyala
            Write-Host "  [UYARI] Robocopy basarisiz, Copy-Item kullaniliyor..." -ForegroundColor Yellow
            Write-Host "  Hata: $($_.Exception.Message)" -ForegroundColor Yellow
            
            try {
                # Copy-Item ile kopyalama (dosya adları korunur)
                Copy-Item -Path "$PublishPath\*" -Destination $projectDist -Recurse -Force -ErrorAction Stop
                
                # Kopyalanan dosya sayısını say
                $copiedCount = (Get-ChildItem -Path $projectDist -Recurse -File -ErrorAction SilentlyContinue | Measure-Object).Count
                
            } catch {
                # Copy-Item de başarısız olursa, dosya dosya kopyala
                Write-Host "  [UYARI] Copy-Item basarisiz, dosya dosya kopyalaniyor..." -ForegroundColor Yellow
                
                $sourceItems = Get-ChildItem -LiteralPath $PublishPath -Recurse -ErrorAction SilentlyContinue
                
                foreach ($item in $sourceItems) {
                    try {
                        # Dosya adını koruyarak relative path oluştur
                        $sourcePath = $item.FullName
                        $relativePath = $sourcePath.Replace($PublishPath, "").TrimStart('\', '/')
                        $destPath = [System.IO.Path]::Combine($projectDist, $relativePath)
                        
                        if ($item.PSIsContainer) {
                            # Klasör ise oluştur
                            if (-not (Test-Path -LiteralPath $destPath)) {
                                New-Item -ItemType Directory -LiteralPath $destPath -Force | Out-Null
                            }
                        } else {
                            # Dosya ise kopyala
                            $destDir = [System.IO.Path]::GetDirectoryName($destPath)
                            if (-not (Test-Path -LiteralPath $destDir)) {
                                New-Item -ItemType Directory -LiteralPath $destDir -Force | Out-Null
                            }
                            
                            # Dosya adını koruyarak kopyala
                            Copy-Item -LiteralPath $sourcePath -Destination $destPath -Force -ErrorAction Stop
                            $copiedCount++
                        }
                    } catch {
                        # Dosya kilitli olabilir (uygulama çalışıyorsa), atla
                        $skippedCount++
                        $fileName = $item.Name
                        Write-Host "  [UYARI] Kopyalanamadi (kilitli olabilir): $fileName" -ForegroundColor Yellow
                    }
                }
            }
        }
        
        Write-Host "[OK] Dist klasorune kopyalama tamamlandi!" -ForegroundColor Green
        Write-Host "  Kopyalanan dosya sayisi: $copiedCount" -ForegroundColor Gray
        if ($skippedCount -gt 0) {
            Write-Host "  Atlanan dosya sayisi: $skippedCount (kilitli olabilir)" -ForegroundColor Yellow
        }
        
        # .NET runtime dosyalarının varlığını kontrol et
        $criticalFiles = @("hostfxr.dll", "hostpolicy.dll", "PinhumanSuperAPP.exe")
        $missingFiles = @()
        
        foreach ($file in $criticalFiles) {
            $filePath = Join-Path $projectDist $file
            if (-not (Test-Path $filePath)) {
                $missingFiles += $file
            }
        }
        
        if ($missingFiles.Count -gt 0) {
            Write-Host "[UYARI] Kritik .NET runtime dosyalari bulunamadi:" -ForegroundColor Yellow
            foreach ($file in $missingFiles) {
                Write-Host "  - $file" -ForegroundColor Yellow
            }
        } else {
            Write-Host "[OK] Kritik .NET runtime dosyalari mevcut" -ForegroundColor Green
        }
        
    } catch {
        Write-Host "[HATA] Dist klasorune kopyalama hatasi: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host "  Hata tipi: $($_.Exception.GetType().FullName)" -ForegroundColor Red
        $script:ScriptSuccess = $false
    }
    
    Write-Host "=== DIST KLASORUNE KOPYALAMA TAMAMLANDI ===" -ForegroundColor Cyan
    Write-Host ""
}

# Auto-release scriptini calistir
function Invoke-AutoRelease {
    param(
        [Parameter(Mandatory=$true)][string]$ScriptRoot
    )
    
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Magenta
    Write-Host "  OTOMATIK RELEASE KONTROLU" -ForegroundColor Magenta
    Write-Host "========================================" -ForegroundColor Magenta
    Write-Host ""
    
    $projectDist = Join-Path $ScriptRoot "dist"
    Write-Host "[BILGI] Dist klasoru kontrol ediliyor: $projectDist" -ForegroundColor Cyan
    
    if (Test-Path $projectDist) {
        Write-Host "[OK] Dist klasoru bulundu!" -ForegroundColor Green
        $distContents = Get-ChildItem -Path $projectDist -ErrorAction SilentlyContinue | Measure-Object
        Write-Host "   Icerik: $($distContents.Count) oge" -ForegroundColor Gray
        
        Write-Host ""
        Write-Host "========================================" -ForegroundColor Cyan
        Write-Host "  OTOMATIK RELEASE OLUSTURMA" -ForegroundColor Cyan
        Write-Host "========================================" -ForegroundColor Cyan
        Write-Host ""
        
        $autoReleaseScript = Join-Path $ScriptRoot "auto-release.ps1"
        Write-Host "[BILGI] Auto-release scripti araniyor: $autoReleaseScript" -ForegroundColor Cyan
        
        if (Test-Path $autoReleaseScript) {
            Write-Host "[OK] auto-release.ps1 bulundu!" -ForegroundColor Green
            Write-Host "[BILGI] Auto-release scripti calistiriliyor..." -ForegroundColor Yellow
            
            # GitHub Token'ı environment variable'dan al (varsa)
            $githubToken = [System.Environment]::GetEnvironmentVariable("GITHUB_TOKEN", "User")
            if ([string]::IsNullOrWhiteSpace($githubToken)) {
                $githubToken = $env:GITHUB_TOKEN
            }
            
            # Komut satırını oluştur
            $cmdArgs = @("-ExecutionPolicy", "Bypass", "-NoProfile", "-File", "`"$autoReleaseScript`"", "-ProjectDir", "`"$ScriptRoot`"")
            if (-not [string]::IsNullOrWhiteSpace($githubToken)) {
                $cmdArgs += "-GitHubToken"
                $cmdArgs += "`"$githubToken`""
                Write-Host "   GitHub Token bulundu, parametre olarak geciriliyor..." -ForegroundColor Gray
            } else {
                Write-Host "   UYARI: GitHub Token bulunamadi, manuel yukleme gerekebilir" -ForegroundColor Yellow
            }
            $cmdLine = "powershell.exe " + ($cmdArgs -join " ")
            Write-Host "   Komut: $cmdLine" -ForegroundColor Gray
            Write-Host ""
            
            try {
                # Token'ı parametre olarak geçir
                if (-not [string]::IsNullOrWhiteSpace($githubToken)) {
                    $releaseOutput = & powershell.exe -ExecutionPolicy Bypass -NoProfile -File $autoReleaseScript -ProjectDir $ScriptRoot -GitHubToken $githubToken 2>&1
                } else {
                    $releaseOutput = & powershell.exe -ExecutionPolicy Bypass -NoProfile -File $autoReleaseScript -ProjectDir $ScriptRoot 2>&1
                }
                
                Write-Host "[BILGI] Auto-release ciktisi:" -ForegroundColor Cyan
                Write-Host $releaseOutput -ForegroundColor White
                
                if ($LASTEXITCODE -eq 0) {
                    Write-Host ""
                    Write-Host "[OK] Auto-release basariyla tamamlandi!" -ForegroundColor Green
                } else {
                    Write-Host ""
                    Write-Host "[UYARI] Auto-release cikis kodu: $LASTEXITCODE" -ForegroundColor Yellow
                }
            } catch {
                Write-Host ""
                Write-Host "[HATA] Auto-release hatasi: $($_.Exception.Message)" -ForegroundColor Red
                Write-Host "   Hata tipi: $($_.Exception.GetType().FullName)" -ForegroundColor Red
                Write-Host "   Stack trace: $($_.ScriptStackTrace)" -ForegroundColor Yellow
            }
        } else {
            Write-Host ""
            Write-Host "[HATA] auto-release.ps1 bulunamadi!" -ForegroundColor Red
            Write-Host "   Aranan yol: $autoReleaseScript" -ForegroundColor Yellow
            Write-Host "   Script root: $ScriptRoot" -ForegroundColor Yellow
        }
    } else {
        Write-Host "[UYARI] Dist klasoru bulunamadi: $projectDist" -ForegroundColor Yellow
        Write-Host "   Auto-release atlaniyor..." -ForegroundColor Gray
    }
    
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Magenta
    Write-Host "  OTOMATIK RELEASE KONTROLU TAMAMLANDI" -ForegroundColor Magenta
    Write-Host "========================================" -ForegroundColor Magenta
    Write-Host ""
}

# ============================================
# ANA SCRIPT BASLANGICI
# ============================================

# Script baslangici
$timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
$psVersion = $PSVersionTable.PSVersion
$workDir = Get-Location

Write-LogBoth ""
Write-LogBoth "========================================" "Magenta"
Write-LogBoth "=== ORGANIZE-PUBLISH.PS1 BASLATILIYOR ===" "Magenta"
Write-LogBoth "========================================" "Magenta"
Write-LogBoth "Zaman: $timestamp" "Cyan"
Write-LogBoth "PowerShell versiyonu: $psVersion" "Cyan"
Write-LogBoth "Calisma dizini: $workDir" "Cyan"
Write-LogBoth ""

# Script root konumunu tespit et
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($scriptRoot)) {
    $scriptRoot = $PSScriptRoot
    if ([string]::IsNullOrWhiteSpace($scriptRoot)) {
        $scriptRoot = Get-Location
    }
    Write-Host "[UYARI] Script konumu tespit edilemedi, alternatif yol kullaniliyor: $scriptRoot" -ForegroundColor Yellow
}

# Log dosyasini baslat (kok dizine yazilacak)
Initialize-LogFile -ScriptRoot $scriptRoot

# 1. Publish ortamini baslat
try {
    $PublishPath = Initialize-PublishEnvironment -PublishPath $PublishPath -ScriptRoot $scriptRoot
} catch {
    Write-Host "[HATA] Publish ortami baslatilamadi: $($_.Exception.Message)" -ForegroundColor Red
    $script:ScriptSuccess = $false
}

# 2. VERSION.json guncelle
try {
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Magenta
    Write-Host "=== VERSION.json GUNCELLEME BASLATILIYOR ===" -ForegroundColor Magenta
    Write-Host "========================================" -ForegroundColor Magenta
    Write-Host "Script kok dizini: $scriptRoot" -ForegroundColor Cyan
    $versionFilePath = Join-Path $scriptRoot "VERSION.json"
    Write-Host "VERSION.json yolu: $versionFilePath" -ForegroundColor Cyan
    
    # Dosyanin var olup olmadigini kontrol et
    if (Test-Path $versionFilePath) {
        Write-Host "[OK] VERSION.json dosyasi bulundu" -ForegroundColor Green
        $fileInfo = Get-Item $versionFilePath
        Write-Host "   Dosya boyutu: $($fileInfo.Length) bytes" -ForegroundColor Gray
        Write-Host "   Son degistirme: $($fileInfo.LastWriteTime)" -ForegroundColor Gray
        
        # Mevcut versiyonu goster
        try {
            $currentContent = Get-Content -Path $versionFilePath -Raw -ErrorAction SilentlyContinue
            if ($currentContent) {
                $currentVersion = $currentContent | ConvertFrom-Json -ErrorAction SilentlyContinue
                if ($currentVersion -and $currentVersion.Version) {
                    Write-Host "   [BILGI] Mevcut versiyon: $($currentVersion.Version)" -ForegroundColor Yellow
                }
            }
        } catch {
            Write-Host "   [UYARI] Mevcut versiyon okunamadi" -ForegroundColor Yellow
        }
    } else {
        Write-Host "[UYARI] VERSION.json dosyasi bulunamadi, olusturulacak" -ForegroundColor Yellow
    }
    
    Write-Host ""
    Update-VersionFile -VersionFilePath $versionFilePath
    
    # Guncelleme sonrasi kontrol
    if (Test-Path $versionFilePath) {
        $updatedContent = Get-Content -Path $versionFilePath -Raw -ErrorAction SilentlyContinue
        if ($updatedContent) {
            Write-Host "[OK] VERSION.json guncellemesi tamamlandi" -ForegroundColor Green
            $updatedData = $updatedContent | ConvertFrom-Json -ErrorAction SilentlyContinue
            if ($updatedData -and $updatedData.Version) {
                Write-Host "   Guncel versiyon: $($updatedData.Version)" -ForegroundColor Cyan
            }
        } else {
            Write-Host "[UYARI] VERSION.json dosyasi bos olabilir" -ForegroundColor Yellow
        }
    } else {
        Write-Host "[HATA] VERSION.json dosyasi hala bulunamıyor!" -ForegroundColor Red
        $script:ScriptSuccess = $false
    }
    Write-Host ""
} catch {
    Write-Host "[HATA] VERSION.json guncelleme hatasi: $($_.Exception.Message)" -ForegroundColor Red
    $script:ScriptSuccess = $false
}

# 3. DLLleri rootta birak (uygulama acilmasi icin gerekli)
Write-Host "DLLler rootta birakiliyor (uygulama acilmasi icin gerekli)"

# 4. Python kurulum dosyalarini kopyala
try {
    if (!$script:SkipPublishOperations) {
        Copy-PythonInstallers -TargetRoot $PublishPath -ScriptRoot $scriptRoot
    }
    
    $projectDist = Join-Path $scriptRoot "dist"
    if (Test-Path $projectDist) {
        Copy-PythonInstallers -TargetRoot $projectDist -ScriptRoot $scriptRoot
    }
} catch {
    Write-Host "[HATA] Python dosyalari kopyalama hatasi: $($_.Exception.Message)" -ForegroundColor Red
    $script:ScriptSuccess = $false
}

# 5. Publish dosyalarini dist klasorune kopyala (.NET runtime dahil)
try {
    if (!$script:SkipPublishOperations) {
        Copy-PublishToDist -PublishPath $PublishPath -ScriptRoot $scriptRoot
    }
} catch {
    Write-Host "[HATA] Dist klasorune kopyalama hatasi: $($_.Exception.Message)" -ForegroundColor Red
    $script:ScriptSuccess = $false
}

# 6. VERSION dosyalarini kopyala
try {
    $projectDist = Join-Path $scriptRoot "dist"
    Copy-VersionFiles -ScriptRoot $scriptRoot -PublishPath $PublishPath -ProjectDist $projectDist
} catch {
    Write-Host "[HATA] VERSION dosyalari kopyalama hatasi: $($_.Exception.Message)" -ForegroundColor Red
    $script:ScriptSuccess = $false
}

# 7. Publish dosyalarini organize et
try {
    Organize-PublishFiles -PublishPath $PublishPath
} catch {
    Write-Host "[HATA] Dosya organizasyon hatasi: $($_.Exception.Message)" -ForegroundColor Red
    $script:ScriptSuccess = $false
}

# 8. Final klasor yapisini goster
try {
    if (!$script:SkipPublishOperations) {
        if (Test-Path $PublishPath) {
            Write-Host ""
            Write-Host "Final klasor yapisi:"
            Get-ChildItem -Path $PublishPath -ErrorAction SilentlyContinue | Format-Table Name, Length -AutoSize 
        }
    }
} catch {
    Write-Host "[UYARI] Final klasor yapisi gosterilemedi: $($_.Exception.Message)"
}

# 9. Sonuc mesajlari
Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "=== PUBLISH KLASORU DUZENLEME TAMAMLANDI! ===" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""

Write-Host "Islem tamamlandi! PinhumanSuperAPP hazir."
if (!$script:SkipPublishOperations) {
    Write-Host "Ana uygulama: $PublishPath\PinhumanSuperAPP.exe"
}

if ($script:ScriptSuccess) {
    Write-Host "[OK] Script basariyla tamamlandi"
} else {
    Write-Host "[UYARI] Script bazi hatalarla tamamlandi ancak build devam ediyor"
}

# 10. Auto-release calistir
try {
    Invoke-AutoRelease -ScriptRoot $scriptRoot
} catch {
    Write-Host "[HATA] Auto-release hatasi: $($_.Exception.Message)" -ForegroundColor Red
    $script:ScriptSuccess = $false
}

# Script sonu - her zaman basarili cikis yap
Write-Host ""
Write-Host "=== SCRIPT SONLANDIRILIYOR (EXIT CODE: 0) ===" -ForegroundColor Green

# Tum hata ayarlarini kapat
$ErrorActionPreference = "SilentlyContinue"

# Cikis kodunu acikca 0 yap
$global:LASTEXITCODE = 0
[System.Environment]::ExitCode = 0

exit 0
