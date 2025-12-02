# Log Sorunu ve Çözümü

## 🎯 Sorun

Terminal'de organize-publish.ps1 çalışıyor ama:
1. ❌ Script'in içindeki loglar görünmüyor
2. ❌ Zipleme işlemleri görünmüyor
3. ❌ Git işlemleri görünmüyor
4. ❌ Auto-release çıktısı görünmüyor

## ✅ Çözüm

PowerShell script'inin çıktısı MSBuild'e iletilemiyor. Çözüm için:

### 1. WebScraper.csproj Güncellendi
- `StandardOutputImportance="high"` eklendi
- PowerShell çıktısı doğru şekilde yönlendirildi

### 2. organize-publish.ps1 Güncellendi
- `Write-LogBoth` fonksiyonu eklendi
- Hem `Write-Host` hem de `Write-Output` kullanılıyor
- MSBuild'in görmesi için stdout'a yazıyor

### 3. Şimdi Ne Yapmalı?

```powershell
dotnet publish -c Release -v detailed
```

**Tüm loglar terminal'de görünecek!**

---

## 📋 Göreceğiniz Loglar

1. ✅ organize-publish.ps1 başlangıç logları
2. ✅ VERSION.json güncelleme logları
3. ✅ Auto-release kontrol logları
4. ✅ auto-release.ps1 çalışma logları
5. ✅ Zip oluşturma logları
6. ✅ Git işlemleri logları
7. ✅ GitHub Release logları

---

## ⚠️ Eğer Hala Görünmüyorsa

1. Verbosity seviyesini artırın: `-v detailed`
2. Script'i manuel test edin
3. Terminal buffer'ını kontrol edin

