# Modüler Config Sistemi Rehberi

## 📋 Genel Bakış

Pin SuperAPP, her modülün kendi yapılandırma dosyasını kullandığı modüler bir sistem kullanır. Bu yaklaşım sayesinde:

- Her modül bağımsız çalışabilir
- Config çakışmaları önlenir
- Bakım ve güncelleme kolaylaşır
- Farklı ortamlarda farklı ayarlar kullanılabilir

## 📁 Config Dosyaları

### 1. Ana Config (`config.json`)
**Konum**: Proje kök dizini
**İçerik**: Genel uygulama ayarları

```json
{
  "AutoLogin": { ... },
  "General": { ... },
  "Notification": { ... },
  "Modules": {
    "PersonnelEnabled": true,
    "SmsEnabled": true,
    "PaymentEnabled": true,
    "ScrapingEnabled": true
  }
}
```

### 2. Personel Config (`personnel-config.json`)
**Konum**: Proje kök dizini
**İçerik**: Personel yönetimi ayarları

```json
{
  "Personnel": {
    "BaseUrl": "https://www.pinhuman.net",
    "StaffUrl": "https://www.pinhuman.net/AgcStaff",
    "CreateUrl": "https://www.pinhuman.net/AgcStaff/Create",
    "LoginEnabled": true,
    "AutoNavigate": true
  },
  "Browser": {
    "HeadlessMode": false,
    "BrowserType": "chromium",
    "DefaultTimeout": 30000,
    "NavigationTimeout": 60000,
    "SlowMo": 100
  },
  "Templates": {
    "TemplatesDirectory": "templates",
    "DefaultTemplateType": "InternalPersonnel",
    "AutoLoadTemplates": true,
    "MaxTemplates": 50
  },
  "Excel": {
    "DefaultExtension": ".xlsx",
    "MaxRowsPerFile": 10000,
    "IncludeHeaders": true,
    "DateFormat": "dd.MM.yyyy",
    "AutoValidateData": true
  },
  "Processing": {
    "BatchSize": 10,
    "DelayBetweenRecords": 2000,
    "MaxRetries": 3,
    "ContinueOnError": false,
    "SaveProgress": true
  },
  "Validation": {
    "TCKNRequired": true,
    "EmailFormatCheck": true,
    "PhoneFormatCheck": true,
    "DateValidation": true,
    "RequiredFieldCheck": true
  },
  "Notifications": {
    "ShowProgressDialog": true,
    "ShowCompletionMessage": true,
    "LogToFile": false,
    "LogDirectory": "logs/personnel"
  }
}
```

### 3. SMS Config (`sms-config.json`)
**Konum**: Proje kök dizini
**İçerik**: SMS gönderme ayarları

```json
{
  "Sms": {
    "HeadlessMode": false,
    "ShowDuplicates": true,
    "BaseUrl": "https://sms-service.example.com",
    "ApiKey": "",
    "DefaultSender": "",
    "MaxMessageLength": 160,
    "BatchSize": 100,
    "RetryCount": 3,
    "TimeoutSeconds": 30
  },
  "Templates": {
    "TemplatesDirectory": "sms-templates",
    "AutoLoadTemplates": true,
    "DefaultTemplate": "standard_sms"
  }
}
```

### 4. Ödeme Config (`payment-config.json`)
**Konum**: Proje kök dizini
**İçerik**: Ödeme işlemleri ayarları

```json
{
  "Payment": {
    "BaseUrl": "https://payment-service.example.com",
    "ApiUrl": "https://api.payment-service.com",
    "MerchantId": "",
    "ApiKey": "",
    "SecretKey": "",
    "TestMode": true,
    "Currency": "TRY",
    "TimeoutSeconds": 60
  },
  "Bank": {
    "BankCode": "0015",
    "BranchCode": "",
    "AccountNumber": "",
    "Iban": "",
    "CompanyName": "",
    "TaxNumber": ""
  }
}
```

### 5. Scraping Config (`scraping-config.json`)
**Konum**: Proje kök dizini
**İçerik**: Web scraping ayarları

```json
{
  "Scraping": {
    "TargetUrl": "https://pinhuman.net",
    "CssClass": "card-body",
    "StatusClass": "badge-success",
    "BaseUrl": "https://pinhuman.net",
    "LoginUrl": "https://pinhuman.net/Account/Login",
    "DashboardUrl": "https://pinhuman.net/Dashboard"
  },
  "Download": {
    "MaxConcurrentDownloads": 3,
    "DownloadTimeout": 1800,
    "RetryFailedDownloads": true,
    "OutputFolder": "dist/cikti"
  }
}
```

## 🔧 ConfigService Kullanımı

### Config Yükleme

```csharp
using WebScraper;

// Tam config yükleme
var personnelConfig = ConfigService.LoadConfig<PersonnelConfig>("personnel-config.json");

// Ana config'den bölüm yükleme
var autoLogin = ConfigService.LoadConfigSection<AutoLoginSettings>("config.json", "AutoLogin");
```

### Config Kaydetme

```csharp
// Tam config kaydetme
ConfigService.SaveConfig("personnel-config.json", personnelConfig);

// Ana config'e bölüm kaydetme
ConfigService.SaveConfigSection("config.json", "AutoLogin", autoLoginSettings);
```

### Config Kontrolü

```csharp
// Config dosyası var mı?
bool exists = ConfigService.ConfigExists("personnel-config.json");

// Tüm config dosyalarını listele
string[] allConfigs = ConfigService.ListConfigFiles();
```

## 📝 Config Güncelleme Kuralları

### 1. Geriye Uyumluluk
- Yeni config alanları eklerken varsayılan değerler belirleyin
- Mevcut config'leri bozmayacak şekilde güncelleyin

### 2. Validasyon
- Kritik config değerlerini uygulama başlangıcında doğrulayın
- Geçersiz değerler için uygun varsayılanlar kullanın

### 3. Güvenlik
- API anahtarları, şifreler gibi hassas bilgileri config'de tutmayın
- Hassas veriler için ayrı güvenli depolama kullanın

### 4. Sürüm Yönetimi
- Config formatunda değişiklik yapıldığında versiyon bilgisi ekleyin
- Eski config'leri yeni formata otomatik dönüştürün

## 🚀 Yeni Modül Ekleme

1. **Config Sınıfı Oluşturun**:
```csharp
public class MyModuleConfig
{
    public string ApiUrl { get; set; } = "https://api.example.com";
    public bool Enabled { get; set; } = true;
}
```

2. **Config Dosyası Oluşturun**:
```json
{
  "MyModule": {
    "ApiUrl": "https://api.example.com",
    "Enabled": true
  }
}
```

3. **ConfigService ile Kullanın**:
```csharp
var config = ConfigService.LoadConfig<MyModuleConfig>("my-module-config.json");
```

4. **Ana Config'e Ekleyin**:
```json
{
  "Modules": {
    "MyModuleEnabled": true
  }
}
```

## 🔍 Sorun Giderme

### Config Dosyası Bulunamıyor
- Dosyanın proje kök dizininde olduğundan emin olun
- Dosya adının doğru yazıldığından emin olun

### Config Parse Hatası
- JSON formatının geçerli olduğunu kontrol edin
- Özel karakterleri düzgün escape ettiğinizi kontrol edin

### Config Değişiklikleri Uygulanmıyor
- Uygulamayı yeniden başlatın
- Cache temizliği yapın

### Çoklu Instance Çakışması
- Config dosyalarını farklı instance'lar için ayırın
- File locking kullanın

## 📊 Excel İşlemleri

### EPPlus Lisans Ayarı
Uygulama başlangıcında otomatik olarak ayarlanır:
```csharp
// Program.cs static constructor - EPPlus 8+ için yeni API
static Program()
{
    OfficeOpenXml.ExcelPackage.License.SetNonCommercialPersonal("WebScraper");
}
```

## 📥 Personel Şablonu İndirme

Personel İşlemleri sayfasında bulunan "📥 Şablon İndir" butonu ile sabit personel şablonunu indirebilirsiniz.

### Şablon İçeriği
İndirilen Excel şablonu aşağıdaki sütunları içerir:

- **TCKN**: 11 haneli TC kimlik numarası
- **AD**: Personelin adı
- **SOYAD**: Personelin soyadı
- **CİNSİYET**: Erkek/Kadın (dropdown)
- **BABA ADI**: Baba adı
- **ANA ADI**: Ana adı
- **EMEKLİ Mİ?**: Evet/Hayır (dropdown)
- **DOĞUM TARİHİ**: GG.AA.YYYY formatında
- **ÖĞRENİM DURUMU**: İlkokul/Ortaokul/Lise/Üniversite/Yüksek Lisans/Doktora (dropdown)
- **ENGELLİ**: Evet/Hayır (checkbox)
- **İŞKUR KAYDI**: Evet/Hayır (dropdown - her zaman Evet seçilir)
- **İL**: İl adı (dropdown - İstanbul illerinden)
- **İLÇE**: İlçe adı (dropdown - il seçildikten sonra yüklenir)
- **BANKA**: Banka adı (dropdown - 12 banka seçeneği)
- **HESAP ADI**: Hesap sahibi adı
- **İBAN**: 26 haneli IBAN numarası

### Web Sitesi Yapısı
Personel ekleme formu 3 farklı tab içerir:

**Tab 1**: Temel Bilgiler (TCKN, Ad, Soyad, Cinsiyet, Baba adı, Ana adı, Emekli durumu, Doğum tarihi, Öğrenim durumu, Engelli, Alt yüklenici)

**Tab 2**: İŞKUR ve Adres Bilgileri (İŞKUR kaydı, İl, İlçe)

**Tab 3**: Banka Bilgileri (Banka, Hesap adı, IBAN)

Form doldurulurken tab'lar arasında otomatik geçiş yapılır.

### Kullanım
1. "📥 Şablon İndir" butonuna tıklayın
2. Dosyanın kaydedileceği yeri seçin
3. Excel dosyası otomatik olarak açılır
4. Örnek satırı inceleyin ve kendi verilerinizi girin
5. Dosyayı kaydedin ve personel ekleme işleminde kullanın

### Excel Şablonu Özellikleri
- Otomatik dosya kaydetme dialog'u
- Başlıklar ve örnek veriler
- Excel dosyası otomatik açılır
- .xlsx formatında kaydedilir

## 📞 Destek

Config ile ilgili sorunlar için:
1. Config dosyalarının formatını kontrol edin
2. Uygulama loglarını inceleyin
3. ConfigService exception mesajlarını okuyun
