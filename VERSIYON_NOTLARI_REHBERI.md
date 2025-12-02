# Versiyon Notları ve Güncelleme Sistemi Rehberi

## 📋 Genel Bakış

Bu sistem, uygulamanın versiyonlarını takip eder ve her versiyondaki değişiklikleri kullanıcılara gösterir.

## 📁 Dosya Yapısı

### 1. VERSION.json
Uygulamanın mevcut versiyon bilgisini tutar. Kök dizinde bulunur.

```json
{
  "Version": "1.0.0",
  "ReleaseDate": "2024-01-01T00:00:00",
  "ReleaseNotes": "İlk sürüm"
}
```

**Alanlar:**
- `Version`: Semantik versiyon numarası (örn: "1.0.0", "1.1.0", "2.0.0")
- `ReleaseDate`: Yayınlanma tarihi (ISO 8601 formatında)
- `ReleaseNotes`: Kısa açıklama (opsiyonel)

### 2. UPDATE_NOTES.json
Tüm versiyonların güncelleme notlarını içerir. Kök dizinde bulunur.

```json
{
  "Updates": [
    {
      "Version": "1.1.0",
      "ReleaseDate": "2024-01-15T00:00:00",
      "NewFeatures": [
        "Yeni özellik 1",
        "Yeni özellik 2"
      ],
      "Improvements": [
        "İyileştirme 1",
        "İyileştirme 2"
      ],
      "BugFixes": [
        "Hata düzeltmesi 1",
        "Hata düzeltmesi 2"
      ],
      "Changes": [
        "Değişiklik 1",
        "Değişiklik 2"
      ]
    }
  ]
}
```

**Bölümler:**
- `NewFeatures` (✨ Yeni Özellikler): Eklenen yeni özellikler
- `Improvements` (🔧 İyileştirmeler): Mevcut özelliklerde yapılan iyileştirmeler
- `BugFixes` (🐛 Hata Düzeltmeleri): Düzeltilen hatalar
- `Changes` (📝 Değişiklikler): Diğer önemli değişiklikler

## 🔄 Nasıl Çalışır?

1. **Uygulama Başlangıcı**: Uygulama açıldığında mevcut versiyon kontrol edilir
2. **Drive Kontrolü**: Google Drive'daki `VERSION.json` dosyası okunur
3. **Versiyon Karşılaştırması**: Mevcut versiyon ile Drive'daki versiyon karşılaştırılır
4. **Güncelleme Notları**: Yeni versiyon varsa `UPDATE_NOTES.json` okunur
5. **Kullanıcı Bildirimi**: Güncelleme notları modal pencerede gösterilir

## 📝 Yeni Versiyon Ekleme

### Adım 1: VERSION.json Güncelleme

Google Drive klasörünüzdeki `VERSION.json` dosyasını güncelleyin:

```json
{
  "Version": "1.2.0",
  "ReleaseDate": "2024-02-01T00:00:00",
  "ReleaseNotes": "Yeni özellikler ve iyileştirmeler"
}
```

### Adım 2: UPDATE_NOTES.json Güncelleme

`UPDATE_NOTES.json` dosyasına yeni versiyon için not ekleyin:

```json
{
  "Updates": [
    {
      "Version": "1.2.0",
      "ReleaseDate": "2024-02-01T00:00:00",
      "NewFeatures": [
        "Yeni özellik 1",
        "Yeni özellik 2"
      ],
      "Improvements": [
        "Performans iyileştirmeleri",
        "UI/UX iyileştirmeleri"
      ],
      "BugFixes": [
        "Düzeltilen hata 1",
        "Düzeltilen hata 2"
      ],
      "Changes": [
        "Config yapısı güncellendi"
      ]
    },
    {
      "Version": "1.1.0",
      ...
    }
  ]
}
```

**Önemli:** Yeni versiyon her zaman listenin **en üstüne** eklenmelidir.

### Adım 3: Dosyaları Drive'a Yükleme

1. `VERSION.json` ve `UPDATE_NOTES.json` dosyalarını Google Drive klasörünüze yükleyin
2. `dist` klasöründeki güncellenmiş dosyaları da yükleyin
3. Kullanıcılar uygulamayı açtığında otomatik olarak güncelleme bildirimi alacaklar

## 💡 Versiyon Numaralandırma

Semantik versiyonlama kullanılır: **MAJOR.MINOR.PATCH**

- **MAJOR**: Geriye dönük uyumsuz değişiklikler
- **MINOR**: Yeni özellikler (geriye dönük uyumlu)
- **PATCH**: Hata düzeltmeleri

**Örnekler:**
- `1.0.0` → `1.0.1` (Patch: Hata düzeltmesi)
- `1.0.0` → `1.1.0` (Minor: Yeni özellik)
- `1.0.0` → `2.0.0` (Major: Büyük değişiklik)

## 🎨 Güncelleme Notları Yazma İpuçları

1. **Kısa ve Öz**: Her maddeyi kısa tutun (1-2 cümle)
2. **Kullanıcı Odaklı**: Teknik detaylardan çok kullanıcı deneyimine odaklanın
3. **Kategorize Edin**: Notları doğru kategorilere yerleştirin
4. **Türkçe**: Notları Türkçe yazın
5. **Emoji Kullanın**: Görsel açıdan daha çekici olması için (zaten otomatik ekleniyor)

## 🔍 Örnek Güncelleme Notları

```json
{
  "Version": "1.2.0",
  "ReleaseDate": "2024-02-01T00:00:00",
  "NewFeatures": [
    "Yeni raporlama sistemi eklendi",
    "Export özelliği genişletildi"
  ],
  "Improvements": [
    "Uygulama başlatma süresi %50 azaltıldı",
    "Arama fonksiyonu iyileştirildi",
    "Daha modern kullanıcı arayüzü"
  ],
  "BugFixes": [
    "Bazı durumlarda çökme sorunu düzeltildi",
    "Veri kaybı sorunu çözüldü"
  ],
  "Changes": [
    "Config dosyası yapısı güncellendi",
    "Minimum sistem gereksinimleri değişti"
  ]
}
```

## ⚠️ Önemli Notlar

- Her iki dosya da Google Drive klasörünün kök dizininde olmalı
- Versiyon numaraları her zaman artmalı (daha yüksek olmalı)
- Tarih formatı ISO 8601 olmalı: `YYYY-MM-DDTHH:mm:ss`
- Güncelleme notları boş olabilir, ancak liste formatında olmalı

## 🐛 Sorun Giderme

### Güncelleme notları gösterilmiyor
- Drive'da `UPDATE_NOTES.json` dosyasının olduğundan emin olun
- Dosya formatının doğru olduğunu kontrol edin
- Versiyon numarasının mevcut versiyondan yüksek olduğunu kontrol edin

### Versiyon kontrolü çalışmıyor
- `VERSION.json` dosyasının Drive'da olduğundan emin olun
- Dosya formatının doğru olduğunu kontrol edin
- API Key ve Folder ID'nin doğru olduğunu kontrol edin

