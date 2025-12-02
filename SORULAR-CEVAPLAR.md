# Otomatik Release Sistemi - Sorular ve Cevaplar

## ❓ Sorularınızın Cevapları

### 1. Publish edince otomatik release zip oluşturuyor mu?
✅ **EVET, tamamen otomatik!**

`dotnet publish -c Release` çalıştırdığınızda:
- `organize-publish.ps1` otomatik çalışır
- Versiyonu otomatik artırır (1.0.1 → 1.0.2)
- `auto-release.ps1`'i çağırır
- `auto-release.ps1` otomatik olarak:
  - `dist` klasörünü zip'ler
  - `releases/PinhumanSuperAPP-v1.0.2.zip` oluşturur
  - Git commit/push yapar
  - GitHub Release oluşturur

**Sonuç:** Manuel hiçbir şey yapmanıza gerek yok! 🎉

---

### 2. Sonra version atıyor mu?
✅ **EVET, otomatik olarak patch versiyonu artar!**

Her `dotnet publish -c Release` çalıştırdığınızda:
- `organize-publish.ps1` içindeki `Update-VersionFile` fonksiyonu çalışır
- Patch versiyonu **otomatik olarak** artar:
  - `1.0.0` → `1.0.1`
  - `1.0.1` → `1.0.2`
  - `1.0.2` → `1.0.3`
  - vs.

**Not:** Major (1.0.0) ve Minor (1.0.0) versiyonlarını değiştirmek isterseniz, `VERSION.json`'ı manuel düzenlemeniz gerekir.

---

### 3. Manuel mi yapıyoruz bunu?
❌ **HAYIR, tamamen otomatik!**

Tek yapmanız gereken:
```powershell
dotnet publish -c Release
```

Bu komut çalıştığında sistem otomatik olarak:
1. ✅ Versiyonu artırır
2. ✅ Zip oluşturur
3. ✅ Git commit/push yapar
4. ✅ GitHub Release oluşturur

**Manuel yapmanız gereken tek şey:**
- Release notes'u `VERSION.json`'da düzenlemek (opsiyonel)
- GitHub web arayüzünden draft release'i yayınlamak (draft olarak oluşturulur)

---

### 4. Versiyonum ile GitHub'ı mı kıyaslıyorsun?
✅ **EVET, tam olarak öyle!**

Uygulama her başladığında (`MainWindow.xaml.cs` içindeki `CheckForUpdates()` metodu):

1. **Yerel versiyon okunur:**
   ```csharp
   var currentVersionInfo = UpdateHelper.GetCurrentVersion();
   // VERSION.json dosyasından okur: "1.0.1"
   ```

2. **GitHub'dan son release kontrol edilir:**
   ```csharp
   var latestRelease = await UpdateHelper.CheckForUpdatesAsync();
   // GitHub Releases API'den: "v1.0.2"
   ```

3. **Karşılaştırılır:**
   ```csharp
   if (UpdateHelper.IsNewerVersion(currentVersion, latestVersion))
   {
       // Yeni versiyon var, kullanıcıya bildirim göster
   }
   ```

**Örnek Senaryo:**
- Yerel `VERSION.json`: `"Version": "1.0.1"`
- GitHub'daki son release tag: `v1.0.2`
- Sonuç: **Güncelleme var!** Kullanıcıya bildirim gösterilir.

---

## 📋 İşlem Akışı Özeti

### Publish İşlemi (Otomatik)
```
dotnet publish -c Release
  ↓
organize-publish.ps1 çalışır
  ↓
VERSION.json güncellenir (1.0.1 → 1.0.2)
  ↓
dist klasörü organize edilir
  ↓
auto-release.ps1 çağrılır
  ↓
Zip oluşturulur (releases/PinhumanSuperAPP-v1.0.2.zip)
  ↓
Git commit/push yapılır
  ↓
GitHub Release oluşturulur (draft)
```

### Güncelleme Kontrolü (Otomatik)
```
Uygulama başlar
  ↓
CheckForUpdates() çalışır
  ↓
Yerel VERSION.json okunur (1.0.1)
  ↓
GitHub Releases API çağrılır (v1.0.2)
  ↓
Karşılaştırma yapılır
  ↓
Güncelleme varsa kullanıcıya bildirim gösterilir
```

---

## 🎯 Özet Tablo

| İşlem | Otomatik/Manuel | Açıklama |
|-------|----------------|----------|
| Versiyon artışı | ✅ **Otomatik** | Patch versiyonu otomatik artar |
| Zip oluşturma | ✅ **Otomatik** | `releases/` klasörüne oluşturulur |
| Git commit | ✅ **Otomatik** | Tüm değişiklikler commit edilir |
| Git push | ✅ **Otomatik** | Commit ve tag push edilir |
| GitHub Release | ✅ **Otomatik** | Draft olarak oluşturulur |
| Release yayınlama | ⚠️ **Manuel** | GitHub web arayüzünden yapılır |
| Versiyon karşılaştırması | ✅ **Otomatik** | Yerel vs GitHub karşılaştırılır |
| Güncelleme bildirimi | ✅ **Otomatik** | Yeni versiyon varsa gösterilir |

---

## 💡 Önemli Notlar

1. **Release Notes:** `organize-publish.ps1` çalıştığında `ReleaseNotes` boş bırakılır. Eğer release notes eklemek isterseniz, `dotnet publish` çalıştırmadan **ÖNCE** `VERSION.json`'daki `ReleaseNotes` alanını doldurun.

2. **Major/Minor Versiyon:** Patch versiyonu otomatik artar, ama Major (1.0.0) veya Minor (1.0.0) versiyonlarını değiştirmek isterseniz `VERSION.json`'ı manuel düzenlemeniz gerekir.

3. **GitHub Release:** Otomatik olarak **draft** olarak oluşturulur. Yayınlamak için GitHub web arayüzüne girmeniz gerekir.

4. **Versiyon Formatı:** Versiyon formatı `X.Y.Z` olmalıdır (örn: `1.0.1`). Tag formatı `vX.Y.Z` şeklinde olur (örn: `v1.0.1`).

