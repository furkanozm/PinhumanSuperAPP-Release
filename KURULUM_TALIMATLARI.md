# 🖥️ Pinhuman SuperApp - Kurulum Talimatları

## 📋 Gereksinimler

### Sistem Gereksinimleri:
- Windows 10/11 (64-bit)
- En az 4GB RAM
- En az 2GB boş disk alanı
- İnternet bağlantısı

### Yazılım Gereksinimleri:
- .NET 8.0 SDK
- Google Chrome (opsiyonel, Playwright otomatik yükler)

## 🚀 Hızlı Kurulum

### Yöntem 1: Otomatik Kurulum Script'i
1. Proje klasörünü başka PC'ye kopyalayın
2. `setup.bat` dosyasına çift tıklayın
3. Script otomatik olarak tüm kurulumu yapacak

### Yöntem 2: PowerShell Script'i
1. Proje klasörünü başka PC'ye kopyalayın
2. PowerShell'i yönetici olarak açın
3. Proje dizinine gidin
4. Şu komutu çalıştırın:
```powershell
.\setup.ps1
```

## 🔧 Manuel Kurulum

### 1. .NET 8.0 SDK Kurulumu
1. https://dotnet.microsoft.com/download/dotnet/8.0 adresine gidin
2. ".NET 8.0 SDK" indirin ve kurun
3. Kurulumu doğrulayın:
```bash
dotnet --version
```

### 2. Proje Kurulumu
1. Proje klasörünü başka PC'ye kopyalayın
2. Komut satırını açın ve proje dizinine gidin
3. Bağımlılıkları yükleyin:
```bash
dotnet restore
```

### 3. Proje Derleme
```bash
dotnet build
```

### 4. Playwright Kurulumu
```bash
# Playwright CLI'yi yükleyin
dotnet tool install --global Microsoft.Playwright.CLI

# Tarayıcıları yükleyin
playwright install chromium
```

### 5. Uygulamayı Çalıştırma
```bash
dotnet run
```

## 📁 Gerekli Dosyalar

Kurulum için şu dosyaların bulunması gerekiyor:
- `WebScraper.csproj` - Proje dosyası
- `Program.cs` - Ana program
- `MainWindow.xaml` ve `MainWindow.xaml.cs` - Ana pencere
- `SelectionWindow.xaml` ve `SelectionWindow.xaml.cs` - Seçim ekranı
- `SmsWindow.xaml` ve `SmsWindow.xaml.cs` - SMS ekranı
- `WebScraper.cs` - Web scraping motoru
- `Config.cs` - Konfigürasyon yönetimi
- `EmailNotificationService.cs` - E-posta servisi
- `SmsService.cs` - SMS servisi
- `SmsHistoryService.cs` - SMS geçmişi
- `Fonts/` klasörü - Font dosyaları
- Resim dosyaları (`.png` dosyaları)
- `config.json` - Konfigürasyon dosyası (varsa)

## ⚠️ Sorun Giderme

### Playwright Hatası
Eğer "Executable doesn't exist" hatası alırsanız:
```bash
playwright install chromium
```

### .NET Hatası
Eğer .NET bulunamazsa:
1. .NET 8.0 SDK'yı yeniden yükleyin
2. Bilgisayarı yeniden başlatın
3. PATH değişkenlerini kontrol edin

### Derleme Hatası
Eğer derleme hatası alırsanız:
```bash
dotnet clean
dotnet restore
dotnet build
```

## 📞 Destek

Kurulum sırasında sorun yaşarsanız:
1. Hata mesajını not edin
2. Sistem bilgilerini kontrol edin
3. Gerekli dosyaların eksik olup olmadığını kontrol edin

## ✅ Kurulum Tamamlandı

Kurulum başarılı olduktan sonra:
1. `dotnet run` komutu ile uygulamayı başlatın
2. İlk çalıştırmada giriş bilgilerini girin
3. Konfigürasyon ayarlarını yapın
4. Uygulama kullanıma hazır!

---
**Not:** Bu uygulama Windows işletim sistemi için tasarlanmıştır. 