# Otomatik Release Sistemi

Bu proje, `dotnet publish` komutu çalıştırıldığında otomatik olarak release oluşturur.

## Nasıl Çalışır?

1. **Otomatik Versiyon Artışı**: VERSION.json'daki patch versiyon otomatik artar (1.0.1 → 1.0.2)
2. **Kullanıcı Verilerini Koruma**: Kullanıcı ayarları ve veriler zip'den dışlanır
3. **Zip Oluşturma**: `dist` klasörü otomatik olarak zip'lenir (kullanıcı verileri hariç)
4. **Git Commit & Push**: Değişiklikler otomatik commit edilir ve push yapılır
5. **GitHub Releases**: GitHub Releases'e otomatik yüklenir

## Kullanım

### Normal Release

```powershell
dotnet publish -c Release
```

Bu komut çalıştırıldığında:
- Proje publish edilir
- `dist` klasörü düzenlenir
- **VERSION.json otomatik güncellenir** (patch versiyon artar, ReleaseNotes boş kalır)
- `dist` klasörü zip'lenir (`releases/PinhumanSuperAPP-vX.X.X.zip`) - **kullanıcı verileri hariç**
- Git commit & push yapılır
- GitHub Releases'e yüklenir (eğer GitHub CLI yüklüyse)

### Kullanıcı Verileri Korunur

Aşağıdaki dosyalar **zip'den dışlanır** ve güncelleme sırasında korunur:

- `config.json` - Kullanıcı ayarları
- `firebase-config.json` - Firebase konfigürasyonu
- `pdks-config*.json` - PDKS konfigürasyonları
- `personnel-config.json` - Personel konfigürasyonu
- `mail_history.json` - Mail geçmişi
- `previously_downloaded.json` - İndirilen dosya geçmişi
- `pin_security.json` - Güvenlik ayarları
- `remember_me.txt` - Beni hatırla bilgisi
- `debug_log.txt` - Debug logları
- `*.db`, `*.sqlite` - Veritabanı dosyaları

### VERSION.json Güncelleme

**Otomatik**: Her publish'te patch versiyon otomatik artar.

**Manuel**: ReleaseNotes'i doldurmak isterseniz, publish öncesi VERSION.json'u düzenleyin:

```json
{
  "Version": "1.0.2",
  "ReleaseDate": "2025-01-16T10:00:00",
  "ReleaseNotes": "Yeni özellikler eklendi - buraya notlarınızı yazın"
}
```

**ÖNEMLİ**: VERSION.json'u manuel güncelledikten sonra publish yaparsanız, otomatik versiyon artışı yapılır (patch +1). Örneğin:
- Manuel: 1.0.2 → Publish sonrası: 1.0.3

### Otomatik Release'i Atlama

Eğer otomatik release'i atlamak isterseniz:

```powershell
# Organize-publish'i çalıştır ama auto-release'i atla
dotnet publish -c Release /p:SkipAutoRelease=true
```

Ya da `auto-release.ps1` scriptini manuel çalıştırabilirsiniz:

```powershell
.\auto-release.ps1 -SkipGit          # Git işlemlerini atla
.\auto-release.ps1 -SkipGitHub       # GitHub yüklemeyi atla
.\auto-release.ps1 -SkipGit -SkipGitHub  # Sadece zip oluştur
```

## Güncelleme Sırasında Kullanıcı Verileri

Güncelleme yapıldığında (zip açıldığında):
- Kullanıcı ayarları korunur
- İndirme geçmişi korunur
- Konfigürasyonlar korunur
- Sadece uygulama dosyaları güncellenir

## Gereksinimler

- **Git**: Git commit/push için
- **GitHub CLI (opsiyonel)**: GitHub Releases'e otomatik yükleme için

GitHub CLI yüklü değilse, script manuel yükleme talimatlarını gösterecektir.

## Dosya Yapısı

```
PinApp/
├── dist/                          # Publish çıktısı (git'te ignore edilir)
├── releases/                      # Release zip dosyaları
│   └── PinhumanSuperAPP-v1.0.2.zip
├── VERSION.json                   # Versiyon bilgisi (otomatik güncellenir)
├── auto-release.ps1               # Otomatik release scripti
├── organize-publish.ps1           # Publish düzenleme scripti
└── user-data-files.txt            # Korunacak dosya listesi
```

## Notlar

- ✅ Her publish'te versiyon otomatik artar
- ✅ Kullanıcı verileri otomatik korunur
- ✅ ReleaseNotes boş bırakılır (kullanıcı doldurur)
- ⚠️ Release yapmadan önce ReleaseNotes'i kontrol edin
- ⚠️ Git commit/push otomatik yapılır, kontrol edin
- ⚠️ GitHub Releases'e yükleme için GitHub CLI veya GitHub Token gereklidir (opsiyonel)

## GitHub Token Ayarlama

GitHub CLI yüklü değilse, GitHub REST API kullanılabilir. Bunun için bir GitHub Personal Access Token gereklidir:

### Token Oluşturma
1. https://github.com/settings/tokens adresine gidin
2. "Generate new token" > "Generate new token (classic)" seçin
3. Token'a bir isim verin (örn: "PinhumanSuperAPP Release")
4. Gerekli izinleri seçin:
   - `repo` (tam repo erişimi) - Release oluşturmak için gerekli
5. Token'ı kopyalayın (bir daha gösterilmeyecek!)

### Token Kullanımı

**Yöntem 1: Environment Variable (Önerilen)**
```powershell
# PowerShell'de (geçici)
$env:GITHUB_TOKEN = "ghp_your_token_here"

# Kalıcı olarak (User environment variable)
[System.Environment]::SetEnvironmentVariable("GITHUB_TOKEN", "ghp_your_token_here", "User")
```

**Yöntem 2: Script Parametresi**
```powershell
.\auto-release.ps1 -GitHubToken "ghp_your_token_here"
```

**Yöntem 3: GitHub CLI (Alternatif)**
GitHub CLI yüklüyse otomatik kullanılır:
- Windows: `winget install --id GitHub.cli`
- Veya: https://cli.github.com/
