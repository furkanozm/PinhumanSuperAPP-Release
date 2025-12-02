# Google Drive Otomatik Güncelleme Sistemi

## 📋 Genel Bakış

Bu sistem, Google Drive'daki güncel dosyaları otomatik olarak indirerek uygulamanızı güncel tutmanıza olanak sağlar. `dist` klasöründeki dosyalar Google Drive ile senkronize edilir.

## 📝 Güncelleme Notları Sistemi

Sistem, versiyon takibi ve güncelleme notları özelliğine sahiptir:

- **VERSION.json**: Mevcut uygulama versiyonunu tutar
- **UPDATE_NOTES.json**: Her versiyon için güncelleme notlarını içerir

Güncelleme kontrolü sırasında kullanıcıya yeni versiyonun özellikleri, iyileştirmeleri ve hata düzeltmeleri gösterilir.

## 🔧 Kurulum

### 1. Google Drive API Key Oluşturma

1. [Google Cloud Console](https://console.cloud.google.com/)'a gidin
2. Yeni bir proje oluşturun veya mevcut projeyi seçin
3. **APIs & Services > Library** bölümüne gidin
4. **Google Drive API**'yi arayın ve etkinleştirin
5. **APIs & Services > Credentials** bölümüne gidin
6. **Create Credentials > API Key** seçeneğini seçin
7. API Key'inizi kopyalayın

### 2. Google Drive Klasör Paylaşımı

1. Google Drive'da güncellenecek dosyaları içeren bir klasör oluşturun
2. Klasöre sağ tıklayın ve **Paylaş** seçeneğini seçin
3. **"Herkes bu bağlantıya sahip olanlar görüntüleyebilir"** veya **"Herkes"** seçeneğini seçin
4. Klasör URL'sinden Folder ID'yi kopyalayın
   - Örnek URL: `https://drive.google.com/drive/folders/1ABC123xyz...`
   - Folder ID: `1ABC123xyz...` (URL'deki `/folders/` kısmından sonraki kısım)

### 3. Config Dosyasını Güncelleme

`config.json` dosyasını açın ve `Update` bölümünü doldurun:

```json
{
  "Update": {
    "Enabled": true,
    "GoogleDriveFolderId": "1ABC123xyz...",
    "GoogleDriveApiKey": "AIzaSy...",
    "CheckOnStartup": true,
    "AutoDownload": true,
    "CheckIntervalMinutes": 60
  }
}
```

**Parametreler:**
- `Enabled`: Güncelleme sistemini etkinleştirir/kapatır
- `GoogleDriveFolderId`: Google Drive klasör ID'si
- `GoogleDriveApiKey`: Google Drive API Key
- `CheckOnStartup`: Uygulama başlangıcında kontrol yapılsın mı?
- `AutoDownload`: Bulunan güncellemeler otomatik indirilsin mi?
- `CheckIntervalMinutes`: Periyodik kontrol aralığı (dakika)

## 🚀 Kullanım

### Otomatik Güncelleme

Uygulama başlatıldığında otomatik olarak:
1. Google Drive'daki dosyalar kontrol edilir
2. Yerel `dist` klasöründeki dosyalarla karşılaştırılır
3. Yeni veya güncel dosyalar otomatik olarak indirilir
4. Kullanıcıya bilgilendirme mesajı gösterilir

### Manuel Güncelleme

Program kodunda manuel güncelleme kontrolü yapmak için:

```csharp
var config = AppConfig.Load();
if (config.Update.Enabled)
{
    using var updateService = new GoogleDriveUpdateService();
    updateService.Initialize(config.Update.GoogleDriveApiKey);
    
    int updatedCount = await updateService.CheckAndUpdateFilesAsync(
        config.Update.GoogleDriveFolderId,
        "dist", // Hedef klasör
        config.Update.AutoDownload
    );
}
```

## 📁 Klasör Yapısı

Sistem, Google Drive'daki klasör yapısını yerel `dist` klasöründe korur:

```
dist/
├── PinhumanSuperAPP.exe
├── PinhumanSuperAPP.dll
├── config.json
└── diğer dosyalar...
```

## ⚙️ Güncelleme Mantığı

1. **MD5 Checksum**: Dosyaların içeriği MD5 ile kontrol edilir
2. **Modified Time**: MD5 yoksa değiştirilme tarihi karşılaştırılır
3. **File Size**: Her iki bilgi de yoksa dosya boyutu kontrol edilir

## 🔒 Güvenlik Notları

- API Key'inizi asla paylaşmayın
- API Key'i sadece okuma yetkisiyle sınırlandırın
- Google Drive klasörünü herkese açık yaparken dikkatli olun
- Hassas bilgiler içeren dosyaları Drive'a koymayın

## ❓ Sorun Giderme

### "Drive servisi başlatılamadı" Hatası
- API Key'in doğru olduğundan emin olun
- Google Drive API'nin etkinleştirildiğini kontrol edin

### "Dosya bulunamadı" Hatası
- Folder ID'nin doğru olduğundan emin olun
- Klasörün paylaşım ayarlarını kontrol edin

### "İndirme hatası" Uyarıları
- İnternet bağlantınızı kontrol edin
- Dosya izinlerini kontrol edin
- Disk alanını kontrol edin

## 📝 Notlar

- Güncellemeler arka planda çalışır ve kullanıcı deneyimini etkilemez
- Büyük dosyalar için indirme süresi uzun olabilir
- Google Docs, Sheets, Slides gibi dosyalar otomatik olarak uygun formata export edilir

