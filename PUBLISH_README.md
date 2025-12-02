# PinhumanSuperAPP - Publish Klasör Yapısı

Bu klasör publish sonrası otomatik olarak düzenlenmiştir.

## Klasör Yapısı

```
📁 PinhumanSuperAPP_Publish/
├── 📄 PinhumanSuperAPP.exe          # Ana uygulama
├── 📄 *.json                        # Config dosyaları
├── 📄 *.docx                        # Belgeler
├── 📁 libs/                         # .NET DLL'leri
├── 📁 runtime/                      # Runtime dosyaları (.pdb, .xml)
├── 📁 resources/                    # Resource dosyaları (.ico, .png)
│   └── 📁 Fonts/                    # Font dosyaları
└── 📄 organize-publish.ps1         # Düzenleme script'i
```

## Dosya Grupları

### Ana Dosyalar (Root)
- `PinhumanSuperAPP.exe` - Ana uygulama executable'ı
- `*.json` - Yapılandırma dosyaları (personnel-config.json, firebase-config.json, vb.)
- `*.docx` - Belge dosyaları

### libs/ Klasörü
- Tüm .NET assembly'leri (.dll dosyaları)
- Third-party kütüphaneler

### runtime/ Klasörü
- Debug dosyaları (.pdb)
- XML documentation dosyaları (.xml)
- Yardımcı runtime dosyaları

### resources/ Klasörü
- İkonlar (.ico, .png)
- Font dosyaları (Fonts/ klasörü içinde)

## Çalıştırma

Uygulamayı çalıştırmak için `PinhumanSuperAPP.exe` dosyasını çalıştırın.

## Notlar

- Bu düzenleme publish sonrası otomatik olarak yapılır
- Klasör yapısı uygulamanın çalışması için gerekli değildir
- İhtiyaç halinde dosyaları yeniden düzenleyebilirsiniz
