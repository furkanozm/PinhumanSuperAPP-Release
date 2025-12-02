using System;
using System.IO;
using System.Text.Json;

namespace WebScraper
{
    /// <summary>
    /// VERSION.json dosyası yönetimi - Google Drive güncelleme sistemi için kullanılır
    /// </summary>
    public class VersionInfo
    {
        public string Version { get; set; } = "1.0.0";
        public DateTime ReleaseDate { get; set; } = DateTime.Now;
        public string ReleaseNotes { get; set; } = "";

        private static readonly string VersionFilePath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, 
            "VERSION.json"
        );

        public static VersionInfo Load()
        {
            try
            {
                if (File.Exists(VersionFilePath))
                {
                    var jsonContent = File.ReadAllText(VersionFilePath);
                    var version = JsonSerializer.Deserialize<VersionInfo>(jsonContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    
                    if (version != null && !string.IsNullOrEmpty(version.Version))
                    {
                        System.Diagnostics.Debug.WriteLine($"[VersionInfo] Local versiyon yüklendi: {version.Version}");
                        return version;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Versiyon bilgisi yüklenirken hata: {ex.Message}");
            }

            // Varsayılan versiyon bilgisi
            System.Diagnostics.Debug.WriteLine("[VersionInfo] VERSION.json bulunamadı, varsayılan versiyon kullanılıyor: 1.0.0");
            return new VersionInfo
            {
                Version = "1.0.0",
                ReleaseDate = DateTime.Now,
                ReleaseNotes = ""
            };
        }

        public static void Save(VersionInfo versionInfo)
        {
            try
            {
                var jsonContent = JsonSerializer.Serialize(versionInfo, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(VersionFilePath, jsonContent);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Versiyon bilgisi kaydedilirken hata: {ex.Message}");
            }
        }

        public static bool IsNewer(string version1, string version2)
        {
            try
            {
                var v1Parts = version1.Split('.');
                var v2Parts = version2.Split('.');

                int maxLength = Math.Max(v1Parts.Length, v2Parts.Length);

                for (int i = 0; i < maxLength; i++)
                {
                    int v1Part = i < v1Parts.Length ? int.Parse(v1Parts[i]) : 0;
                    int v2Part = i < v2Parts.Length ? int.Parse(v2Parts[i]) : 0;

                    if (v2Part > v1Part) return true;
                    if (v2Part < v1Part) return false;
                }

                return false; // Eşit
            }
            catch
            {
                // Parse hatası durumunda false döndür
                return false;
            }
        }
    }
}

