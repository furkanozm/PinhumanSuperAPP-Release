# Test: dotnet publish -c Release

## ✅ Tüm Loglar Terminal'de Görünecek!

`dotnet publish -c Release` çalıştırdığınızda terminal'de **tüm logları sırasıyla göreceksiniz**.

---

## 📋 Yapılan Değişiklikler

### 1. WebScraper.csproj
- ✅ PowerShell script çıktısı MSBuild'e yönlendirildi
- ✅ Tüm çıktılar terminal'de görünecek
- ✅ Başlangıç ve bitiş mesajları eklendi

### 2. organize-publish.ps1
- ✅ Helper fonksiyon eklendi (`Write-LogBoth`)
- ✅ Tüm loglar hem renkli terminal hem de stdout'a yazılıyor
- ✅ Detaylı loglama her adımda

### 3. auto-release.ps1
- ✅ Detaylı loglama eklendi
- ✅ Her adımda açıklayıcı mesajlar

---

## 🚀 Nasıl Test Ederim?

### 1. Basit Test:
```powershell
cd C:\Users\BERKAN\Desktop\PinApp
dotnet publish -c Release
```

### 2. Detaylı Loglar İçin:
```powershell
dotnet publish -c Release -v normal
```

### 3. Maksimum Detay:
```powershell
dotnet publish -c Release -v detailed
```

---

## 📺 Terminal'de Göreceğiniz Loglar

```
Build succeeded.

========================================
🚀 PUBLISH SONRASI KLASÖR DÜZENLEME BAŞLATILIYOR...
========================================

========================================
=== ORGANIZE-PUBLISH.PS1 BAŞLATILIYOR ===
========================================
Zaman: 2025-01-20 10:00:00
...

=== VERSION.json GÜNCELLEME BAŞLATILIYOR ===
📌 Mevcut versiyon: 1.0.1
✅ VERSION.json güncellendi: 1.0.1 -> 1.0.2
...

========================================
  OTOMATIK RELEASE KONTROLÜ
========================================
✅ Dist klasörü bulundu!
🚀 Auto-release scripti çalıştırılıyor...
...

========================================
  AUTO-RELEASE.PS1 BAŞLATILIYOR
========================================
[1/4] Versiyon bilgisi okunuyor...
[2/4] dist klasoru zip'leniyor...
[3/4] Git işlemleri...
[4/4] GitHub Release oluşturuluyor...
```

---

## ⚠️ Eğer Loglar Görünmüyorsa

1. **Verbosity seviyesini artırın:**
   ```powershell
   dotnet publish -c Release -v detailed
   ```

2. **Script'i manuel test edin:**
   ```powershell
   cd C:\Users\BERKAN\Desktop\PinApp
   powershell -ExecutionPolicy Bypass -NoProfile -File organize-publish.ps1
   ```

3. **MSBuild loglarını kontrol edin:**
   - Terminal penceresinin scroll edildiğinden emin olun
   - Çıktı buffer'ına bakın

---

## ✅ Sonuç

Artık `dotnet publish -c Release` çalıştırdığınızda **TÜM LOGLARI TERMİNAL'DE SIRASIYLA göreceksiniz**! 🎉

