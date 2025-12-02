# Pin - SuperAPP

## 🚀 Özellikler

- **Modüler Yapı**: Her modül kendi config dosyasına sahip
- **Personel Yönetimi**: Excel'den toplu personel kaydı
- **SMS Yönetimi**: Toplu SMS gönderme
- **Ödeme İşlemleri**: Banka entegrasyonları
- **Web Scraping**: Otomatik veri toplama

## ⚙️ Yapılandırma (Configuration)

Uygulama modüler bir yapılandırma sistemi kullanır. Her modül kendi ayarlarını ayrı dosyalarda tutar:

### Ana Yapılandırma (`config.json`)
- Genel uygulama ayarları
- Login bilgileri
- Email bildirimleri
- Modül aktiflik durumları

### Modül Yapılandırmaları

#### 📋 Personel Yönetimi (`personnel-config.json`)
```json
{
  "Personnel": {
    "BaseUrl": "https://www.pinhuman.net",
    "StaffUrl": "https://www.pinhuman.net/AgcStaff"
  },
  "Browser": {
    "HeadlessMode": false,
    "BrowserType": "chromium"
  },
  "Templates": {
    "TemplatesDirectory": "templates"
  }
}
```

#### 📱 SMS Yönetimi (`sms-config.json`)
```json
{
  "Sms": {
    "BaseUrl": "https://sms-service.example.com",
    "ApiKey": "your-api-key"
  }
}
```

#### 💳 Ödeme İşlemleri (`payment-config.json`)
```json
{
  "Payment": {
    "BaseUrl": "https://payment-service.example.com",
    "MerchantId": "your-merchant-id"
  }
}
```

#### 🌐 Web Scraping (`scraping-config.json`)
```json
{
  "Scraping": {
    "TargetUrl": "https://target-website.com"
  }
}
```

## GitHub'dan İndirme Sonrası Paket Kurulumu

Repository'yi klonladıktan sonra aşağıdaki adımları takip edin:

### Gereksinimler
- .NET 9.0 SDK (https://dotnet.microsoft.com/download/dotnet/9.0) - Sadece geliştirici için gerekli

### 1. NuGet Paketlerini Restore Edin
```bash
dotnet restore
```

Eğer restore çalışmazsa cache'i temizleyin:
```bash
dotnet nuget locals all --clear
dotnet restore --force
```

### 2. Playwright Tarayıcılarını Yükleyin
```bash
dotnet tool install --global Microsoft.Playwright.CLI
playwright install
```

### 3. Uygulamayı Çalıştırın

**Seçenek 1: .NET Runtime ile Çalıştırma (Geliştirici için)**
```bash
dotnet run
```

**Seçenek 2: Bağımsız Executable (Önerilen - .NET kurulumu gerektirmez)**
Proje klasöründe `run.bat` dosyasını çift tıklayarak uygulamayı başlatabilirsiniz. Bu executable .NET runtime'ını içinde barındırır ve ayrı .NET kurulumu gerektirmez.

## 📦 Kullanılan Paketler

- Firebase.Auth (1.0.0)
- FontAwesome.WPF (4.7.0.9)
- MaterialDesignThemes (5.2.1)
- Microsoft.Playwright (1.42.0)
- HtmlAgilityPack (1.11.59)
- EPPlus (8.1.0) - Ücretsiz lisans ile
- Otp.NET (1.3.0)
- DocumentFormat.OpenXml (3.0.1)
- NPOI (2.6.2)

## 📖 Kullanım

### Personel Yönetimi

1. **Şablon Oluşturma**:
   - Şablon Yönetimi'ne gidin
   - İhtiyacınız olan alanları seçin (TCKN, Ad, Soyad, vb.)
   - **Hızlı Seçim**: Checkbox'ların üstündeki "Tümünü Seç" veya "Tümünü Kaldır" butonlarını kullanın
   - "Excel Şablonu İndir" butonuna tıklayın
   - **Şablonlar kaydedilir**: `templates/` klasörü (JSON)
   - **Excel şablonları**: Kullanıcı tarafından indirilen .xlsx dosyaları

2. **Şablon Yönetimi**:
   - Personel İşlemleri sayfasında şablonları görebilirsiniz
   - Her şablon kartında "📊 Excel İndir" butonu vardır
   - Şablonları düzenleyebilir veya silebilirsiniz

3. **Excel Hazırlama**:
   - İndirdiğiniz şablonu doldurun
   - Her satır bir personel için

4. **Toplu Kayıt**:
   - Personel Ekle'ye gidin
   - Şablon seçin
   - Excel dosyasını yükleyin
   - İşlemi başlatın

#### 📁 Şablon Dosyaları
```
templates/
├── test-template.json          # Örnek şablon (JSON)
├── personel-template-001.json  # Personel şablonu (JSON)
└── custom-template.json        # Özel şablon (JSON)

# Excel şablonları indirme yoluyla oluşturulur:
# 📊 personel-template.xlsx     # İndirilen Excel şablonu
# 📊 custom-template.xlsx       # İndirilen Excel şablonu
```

#### ⚙️ Web Scraping Ayarları
WebScraperService.cs'deki ayarlar **config.json** dosyasından gelir:

```json
{
  "AutoLogin": {
    "Username": "kullanici@firma.com",
    "Password": "şifre",
    "CompanyCode": "firma-kodu",
    "TotpSecret": "totp-secret"
  },
  "Scraping": {
    "TargetUrl": "https://pinhuman.net"
  }
}
```

Bu ayarları değiştirmek için `config.json` dosyasını düzenleyin.

### Config Güncellemeleri

Her modülün config dosyasını düzenleyerek ayarları değiştirebilirsiniz:

```bash
# Personel ayarlarını düzenleme
notepad personnel-config.json

# SMS ayarlarını düzenleme
notepad sms-config.json
```

## 🔄 Otomatik Güncelleme

Uygulama Google Drive üzerinden otomatik güncelleme kontrolü yapar. `organize-publish.ps1` scripti publish işlemlerinde `VERSION.json` dosyasını otomatik günceller.

## 🔧 Gelişmiş Yapılandırma

### ConfigService Kullanımı

Uygulama içinde config'lere erişim:

```csharp
// Personel config'i yükleme
var personnelConfig = ConfigService.LoadConfig<PersonnelConfig>("personnel-config.json");

// Bölüm güncelleme
ConfigService.SaveConfigSection("config.json", "AutoLogin", newLoginSettings);
```

### Özel Config Sınıfları

Her modül için kendi config sınıfınızı oluşturabilirsiniz:

```csharp
public class MyModuleConfig
{
    public string ApiUrl { get; set; }
    public bool Enabled { get; set; }
    public List<string> Settings { get; set; }
}
```
