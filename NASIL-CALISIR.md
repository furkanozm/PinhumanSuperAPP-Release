# Nasıl Çalışır? - Terminal Loglama

## ✅ Evet, Tüm Loglar Terminal'de Görünecek!

`dotnet publish -c Release` çalıştırdığınızda **TÜM LOGLARI TERMİNAL'DE SIRASIYLA göreceksiniz**.

---

## 📋 Nasıl Çalışıyor?

### 1. MSBuild Target
```xml
<Target Name="OrganizePublishOutput" AfterTargets="Publish">
  <Exec Command="powershell.exe ... organize-publish.ps1 ..." />
</Target>
```

### 2. PowerShell Çıktısı MSBuild'e İletiliyor
- ✅ `*>&1` ile tüm çıktılar stdout'a yönlendiriliyor
- ✅ MSBuild bu çıktıları yakalıyor
- ✅ Terminal'de görünüyor

### 3. Script'lerde Loglama
- ✅ Her adımda `Write-Host` (renkli) + `[Console]::Out.WriteLine` (MSBuild için)
- ✅ Detaylı loglar her yerde

---

## 🚀 Çalıştırın ve Görün!

```powershell
dotnet publish -c Release
```

**Tüm loglar terminal'de görünecek!** 🎉

