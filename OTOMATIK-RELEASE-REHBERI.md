# Otomatik Release Sistemi - Detaylı Rehber

## 📋 Genel Bakış

`dotnet publish -c Release` çalıştırdığınızda sistem **tamamen otomatik** olarak şunları yapar:

1. ✅ **VERSION.json otomatik güncellenir** (Patch versiyonu artar: 1.0.0 → 1.0.1)
2. ✅ **Release zip dosyası otomatik oluşturulur** (`releases/PinhumanSuperAPP-v1.0.1.zip`)
3. ✅ **Git commit/push otomatik yapılır**
4. ✅ **GitHub Release otomatik oluşturulur** (draft olarak)

---

## 🔄 Otomatik İşlem Akışı

### 1️⃣ Publish Komutu
```powershell
dotnet publish -c Release
```

### 2️⃣ organize-publish.ps1 Çalışır
- **VERSION.json'ı okur**
- **Patch versiyonunu otomatik artırır** (1.0.0 → 1.0.1)
- `ReleaseDate` güncellenir
- `ReleaseNotes` boş bırakılır (siz dolduracaksınız)
- `dist` klasörünü organize eder
- **Sonunda auto-release.ps1'i çağırır**

### 3️⃣ auto-release.ps1 Çalışır
- **VERSION.json'dan versiyon okur**
- **dist klasörünü zip'ler** (kullanıcı verileri hariç)
- **Git commit yapar** (`git add`, `git commit`)
- **Git tag oluşturur** (`git tag v1.0.1`)
- **Git push yapar** (`git push`, `git push --tags`)
- **GitHub Release oluşturur** (draft, manuel yayınlarsınız)

---

## 📝 Versiyon Güncelleme

### Otomatik Artış
- **Patch versiyonu otomatik artar**: `1.0.0` → `1.0.1` → `1.0.2`
- **Major/Minor değişmez** (bunları manuel değiştirmeniz gerekir)

### Manuel Versiyon Değiştirme
Eğer Major veya Minor versiyonunu değiştirmek isterseniz, `VERSION.json` dosyasını manuel düzenleyin:

```json
{
  "Version": "1.1.0",  // Minor versiyonu artırdınız
  "ReleaseDate": "2025-01-20T10:00:00",
  "ReleaseNotes": "Yeni özellikler eklendi"
}
```

**Not:** `ReleaseNotes`'i düzenlemek isterseniz, `organize-publish.ps1` çalışmadan **ÖNCE** düzenleyin. Script çalıştığında `ReleaseNotes` boş bırakılır.

---

## 🔍 Versiyon Karşılaştırması

Uygulama çalıştığında:

1. **Yerel versiyon okunur**: `VERSION.json` dosyasından
2. **GitHub'dan son release kontrol edilir**: GitHub Releases API'den
3. **Karşılaştırılır**: Yerel versiyon < GitHub versiyonu ise güncelleme gösterilir

**Örnek:**
- Yerel versiyon: `1.0.0`
- GitHub'daki son release: `1.0.2`
- Sonuç: **Güncelleme var!** Kullanıcıya bildirim gösterilir

---

## ⚙️ Manuel İşlemler

### Release Notes Ekleme
1. `VERSION.json` dosyasını açın
2. `ReleaseNotes` alanını doldurun:
```json
{
  "Version": "1.0.1",
  "ReleaseDate": "2025-01-20T10:00:00",
  "ReleaseNotes": "• Yeni özellik eklendi\n• Hata düzeltmeleri\n• Performans iyileştirmeleri"
}
```
3. `dotnet publish -c Release` çalıştırın

### GitHub Release'i Yayınlama
`auto-release.ps1` otomatik olarak **draft** release oluşturur. Yayınlamak için:

1. GitHub reponuza gidin: `https://github.com/furkanozm/PinhumanSuperAPP-Release`
2. **Releases** sekmesine tıklayın
3. Draft release'i bulun
4. **Edit** butonuna tıklayın
5. Release notes'u düzenleyin (gerekirse)
6. **Publish release** butonuna tıklayın

---

## 🎯 Özet

| İşlem | Durum | Açıklama |
|-------|-------|----------|
| Versiyon artışı | ✅ Otomatik | Patch versiyonu otomatik artar |
| Zip oluşturma | ✅ Otomatik | `releases/` klasörüne oluşturulur |
| Git commit | ✅ Otomatik | Tüm değişiklikler commit edilir |
| Git tag | ✅ Otomatik | `v1.0.1` formatında tag oluşturulur |
| Git push | ✅ Otomatik | Commit ve tag push edilir |
| GitHub Release | ✅ Otomatik | Draft olarak oluşturulur |
| Release yayınlama | ⚠️ Manuel | GitHub web arayüzünden yapılır |
| Release Notes | ⚠️ Manuel | VERSION.json'dan okunur, manuel düzenlenebilir |

---

## ❓ Sık Sorulan Sorular

### Q: Versiyon otomatik artıyor mu?
**A:** Evet! Her `dotnet publish -c Release` çalıştırdığınızda patch versiyonu otomatik artar.

### Q: GitHub Release'i manuel mi oluşturuyorum?
**A:** Hayır, otomatik oluşturulur. Ancak **draft** olarak oluşturulur, yayınlamak için GitHub web arayüzüne girmeniz gerekir.

### Q: Versiyonum ile GitHub'ı karşılaştırıyor mu?
**A:** Evet! Uygulama başladığında:
- Yerel `VERSION.json`'dan versiyon okunur
- GitHub Releases'den son release kontrol edilir
- Karşılaştırılır ve güncelleme varsa bildirim gösterilir

### Q: Major/Minor versiyonunu nasıl artırırım?
**A:** `VERSION.json` dosyasını manuel olarak düzenleyin. Patch versiyonu otomatik artar, Major/Minor manuel değiştirmeniz gerekir.

---

## 🔗 İlgili Dosyalar

- `organize-publish.ps1` - Versiyon güncelleme ve klasör organizasyonu
- `auto-release.ps1` - Zip oluşturma, Git işlemleri, GitHub Release
- `VERSION.json` - Mevcut versiyon bilgisi
- `UpdateHelper.cs` - GitHub Releases kontrolü
- `MainWindow.xaml.cs` - Güncelleme kontrolü ve bildirimi

