# Terminal Loglama - Test Rehberi

## ✅ Tüm Loglar Terminal'de Görünecek!

`dotnet publish -c Release` çalıştırdığınızda **tüm logları terminal'de sırasıyla göreceksiniz**.

---

## 📋 Göreceğiniz Loglar

### 1. MSBuild Başlangıç Logları
```
========================================
Publish sonrası klasör düzenleniyor...
Publish klasörü: C:\BuildOutput\...
Project dizini: C:\Users\BERKAN\Desktop\PinApp
========================================
```

### 2. organize-publish.ps1 Başlangıç
```
========================================
=== ORGANIZE-PUBLISH.PS1 BAŞLATILIYOR ===
========================================
Zaman: 2025-01-20 10:00:00
PowerShell versiyonu: 5.1.x
Çalışma dizini: C:\Users\BERKAN\Desktop\PinApp
```

### 3. VERSION.json Güncelleme
```
========================================
=== VERSION.json GÜNCELLEME BAŞLATILIYOR ===
========================================
📌 Mevcut versiyon: 1.0.1
✅ VERSION.json güncellendi: 1.0.1 -> 1.0.2
```

### 4. Auto-Release Başlatma
```
========================================
  OTOMATIK RELEASE KONTROLÜ
========================================
✅ Dist klasörü bulundu!
🚀 Auto-release scripti çalıştırılıyor...
```

### 5. auto-release.ps1 Detayları
```
========================================
  AUTO-RELEASE.PS1 BAŞLATILIYOR
========================================
[1/4] Versiyon bilgisi okunuyor...
✅ VERSION.json bulundu!
  Versiyon: 1.0.2
[2/4] dist klasoru zip'leniyor...
[3/4] Git işlemleri...
[4/4] GitHub Release oluşturuluyor...
```

---

## 🔧 Logları Görmek İçin

### Normal Kullanım (Yeterli)
```powershell
dotnet publish -c Release
```

### Daha Detaylı Loglar İçin
```powershell
dotnet publish -c Release -v normal
```

### Maksimum Detay İçin
```powershell
dotnet publish -c Release -v detailed
```

---

## 📝 Notlar

1. **Tüm loglar sırasıyla gösterilir** - Script'ler çalıştıkça
2. **Renkli loglar** - Önemli mesajlar renkli gösterilir
3. **Her adım loglanır** - Ne yapıldığı net bir şekilde görünür
4. **Hata mesajları** - Hatalar detaylı şekilde gösterilir

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

3. **MSBuild'in çıktısını kontrol edin:**
   - Terminal penceresinin scroll edildiğinden emin olun
   - Çıktı buffer'ına bakın

---

## 🎯 Özet

✅ Tüm loglar terminal'de görünecek
✅ Sırasıyla gösterilecek
✅ Renkli ve açıklayıcı
✅ Her adım detaylı loglanacak

**Sadece `dotnet publish -c Release` çalıştırın ve tüm logları görün!** 🚀

