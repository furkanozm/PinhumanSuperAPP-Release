using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace WebScraper
{
    public class UpdateHelper
    {
        private const string VERSION_FILE = "VERSION.json";
        
        // GitHub Repository Bilgileri (Varsayılan)
        private const string DEFAULT_GITHUB_OWNER = "furkanozm";
        private const string DEFAULT_GITHUB_REPO = "PinhumanSuperAPP-Release";
        
        private static string GetGitHubApiBase()
        {
            try
            {
                var config = ConfigManager.LoadConfig();
                if (!string.IsNullOrEmpty(config.Update.UpdateUrl))
                {
                    var url = config.Update.UpdateUrl.TrimEnd('/');
                    // https://github.com/user/repo -> https://api.github.com/repos/user/repo/releases/latest
                    if (url.StartsWith("https://github.com/"))
                    {
                        var repoPath = url.Replace("https://github.com/", "").TrimEnd('/');
                        var apiUrl = $"https://api.github.com/repos/{repoPath}/releases/latest";
                        Debug.WriteLine($"Config'den GitHub API URL: {apiUrl}");
                        return apiUrl;
                    }
                    // Zaten API URL ise direkt kullan
                    if (url.StartsWith("https://api.github.com/"))
                    {
                        Debug.WriteLine($"Config'den direkt API URL kullanılıyor: {url}");
                        return url;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Config okuma hatası: {ex.Message}");
            }
            // Varsayılan: furkanozm/PinhumanSuperAPP-Release
            var defaultUrl = $"https://api.github.com/repos/{DEFAULT_GITHUB_OWNER}/{DEFAULT_GITHUB_REPO}/releases/latest";
            Debug.WriteLine($"Varsayılan GitHub API URL kullanılıyor: {defaultUrl}");
            return defaultUrl;
        }
        
        public class VersionInfo
        {
            public string Version { get; set; } = "";
            public string ReleaseDate { get; set; } = "";
            public string ReleaseNotes { get; set; } = "";
        }
        
        public class GitHubRelease
        {
            public string tag_name { get; set; } = "";
            public string name { get; set; } = "";
            public string body { get; set; } = "";
            public bool prerelease { get; set; }
            public bool draft { get; set; }
            public DateTime published_at { get; set; }
            public GitHubAsset[] assets { get; set; } = Array.Empty<GitHubAsset>();
        }
        
        public class GitHubAsset
        {
            public string name { get; set; } = "";
            public string browser_download_url { get; set; } = "";
            public string url { get; set; } = ""; // API URL (draft release'ler için)
            public long size { get; set; }
        }
        
        public static VersionInfo GetCurrentVersion()
        {
            try
            {
                if (File.Exists(VERSION_FILE))
                {
                    var content = File.ReadAllText(VERSION_FILE);
                    return JsonSerializer.Deserialize<VersionInfo>(content) ?? new VersionInfo();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Versiyon okuma hatası: {ex.Message}");
            }
            return new VersionInfo();
        }
        
        public static async Task<GitHubRelease?> CheckForUpdatesAsync()
        {
            string? apiUrl = null;
            try
            {
                // HttpClientHandler ile performans optimizasyonları
                using (var handler = new HttpClientHandler())
                {
                    // Otomatik compression desteği (gzip, deflate)
                    handler.AutomaticDecompression = System.Net.DecompressionMethods.All;
                    // Connection pooling için ayarlar
                    handler.MaxConnectionsPerServer = 10;
                    
                    using (var client = new HttpClient(handler))
                    {
                        // Timeout ayarla (30 saniye)
                        client.Timeout = TimeSpan.FromSeconds(30);
                        client.DefaultRequestHeaders.Add("User-Agent", "PinhumanSuperAPP");
                        client.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");
                        // Compression desteği için Accept-Encoding header'ı ekle
                        client.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate, br");
                    
                        // GitHub Token kontrolü (eğer varsa authentication ekle)
                        string? token = null;
                        bool usedToken = false;
                        try
                        {
                            var config = ConfigManager.LoadConfig();
                            // Config'de token yoksa environment variable'dan dene
                            token = Environment.GetEnvironmentVariable("GITHUB_TOKEN") ?? 
                                    Environment.GetEnvironmentVariable("GH_TOKEN");
                            
                            if (!string.IsNullOrEmpty(token))
                            {
                                client.DefaultRequestHeaders.Authorization = 
                                    new AuthenticationHeaderValue("Bearer", token);
                                usedToken = true;
                                Debug.WriteLine("GitHub Token kullanılıyor (environment variable'dan)");
                            }
                        }
                        catch
                        {
                            // Token yoksa public repository olarak devam et
                        }
                        
                        apiUrl = GetGitHubApiBase();
                        Debug.WriteLine($"GitHub API URL: {apiUrl}");

                        async Task<GitHubRelease?> CallApiAsync(bool withoutToken = false)
                        {
                            if (withoutToken)
                            {
                                // Token'ı temizle ve anonim istek gönder
                                client.DefaultRequestHeaders.Authorization = null;
                                Debug.WriteLine("GitHub Token geçersiz, anonim istek ile yeniden deneniyor...");
                            }
                            
                            var json = await client.GetStringAsync(apiUrl);
                            
                            if (string.IsNullOrWhiteSpace(json))
                            {
                                Debug.WriteLine("GitHub API'den boş yanıt alındı.");
                                throw new Exception("GitHub API'den boş yanıt alındı.");
                            }
                            
                            var rel = JsonSerializer.Deserialize<GitHubRelease>(json);
                            
                            if (rel == null)
                            {
                                Debug.WriteLine("GitHub release deserialize edilemedi.");
                                throw new Exception("GitHub release deserialize edilemedi. Yanıt: " + (json.Length > 200 ? json.Substring(0, 200) : json));
                            }
                            
                            Debug.WriteLine($"GitHub Release bulundu: {rel.tag_name}");
                            return rel;
                        }

                        try
                        {
                            // Önce mevcut token ile dene (varsa)
                            return await CallApiAsync();
                        }
                        catch (HttpRequestException ex) when (usedToken && (ex.Message.Contains("401") || ex.Message.Contains("403")))
                        {
                            // Token hatalı / süresi geçmiş ise: bir kere tokensız tekrar dene
                            Debug.WriteLine($"GitHub token ile istek yetkisiz (401/403): {ex.Message}");
                            Debug.WriteLine("Anonim (tokensız) GitHub isteği ile tekrar deneniyor...");
                            return await CallApiAsync(withoutToken: true);
                        }
                    }
                }
            }
            catch (TaskCanceledException ex)
            {
                Debug.WriteLine($"Güncelleme kontrolü timeout: {ex.Message}");
                throw new Exception($"GitHub API'ye bağlanırken timeout oluştu (30 saniye). URL: {apiUrl}", ex);
            }
            catch (HttpRequestException ex)
            {
                Debug.WriteLine($"Güncelleme kontrolü HTTP hatası: {ex.Message}");
                
                // 404 hatası için özel mesaj
                if (ex.Message.Contains("404") || ex.Message.Contains("Not Found"))
                {
                    var repoInfo = apiUrl?.Replace("https://api.github.com/repos/", "").Replace("/releases/latest", "") ?? "bilinmiyor";
                    throw new Exception($"GitHub repository bulunamadı (404).\n\n" +
                                      $"Repository: {repoInfo}\n" +
                                      $"Olası nedenler:\n" +
                                      $"1. Repository adı yanlış veya repository mevcut değil\n" +
                                      $"2. Repository private ve authentication gerekiyor\n" +
                                      $"3. Repository henüz oluşturulmamış\n\n" +
                                      $"URL: {apiUrl}", ex);
                }
                
                var statusCode = ex.Data.Contains("StatusCode") ? ex.Data["StatusCode"]?.ToString() : "Bilinmiyor";
                throw new Exception($"GitHub API'ye bağlanırken HTTP hatası: {ex.Message} (Status: {statusCode}). URL: {apiUrl}", ex);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Güncelleme kontrolü hatası: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                throw new Exception($"GitHub güncelleme kontrolü hatası: {ex.Message}. URL: {apiUrl}", ex);
            }
        }
        
        public static bool IsNewerVersion(string currentVersion, string newVersion)
        {
            try
            {
                var current = new Version(currentVersion.Replace("v", ""));
                var latest = new Version(newVersion.Replace("v", ""));
                return latest > current;
            }
            catch
            {
                return false;
            }
        }
        
        public static async Task<bool> DownloadAndExtractUpdateAsync(string downloadUrl, string zipFileName, IProgress<double>? progress = null, string? apiAssetUrl = null)
        {
            try
            {
                // Programın çalıştığı dizini dinamik olarak bul
                // Assembly.Location en güvenilir yöntem (her PC'de çalışır)
                var assemblyLocation = Assembly.GetExecutingAssembly().Location;
                var appDirectory = string.IsNullOrEmpty(assemblyLocation) 
                    ? AppDomain.CurrentDomain.BaseDirectory 
                    : Path.GetDirectoryName(assemblyLocation) ?? AppDomain.CurrentDomain.BaseDirectory;
                
                // Eğer hala boşsa, mevcut dizini kullan
                if (string.IsNullOrEmpty(appDirectory))
                {
                    appDirectory = Directory.GetCurrentDirectory();
                }
                
                Debug.WriteLine($"📁 Güncelleme dizini: {appDirectory}");
                var tempZipPath = Path.Combine(Path.GetTempPath(), zipFileName);
                
                // Zip'i indir
                Debug.WriteLine($"İndirme URL'si: {downloadUrl}");
                Debug.WriteLine($"Geçici zip yolu: {tempZipPath}");
                
                // HttpClientHandler ile performans optimizasyonları
                using (var handler = new HttpClientHandler())
                {
                    // Otomatik compression desteği (gzip, deflate)
                    handler.AutomaticDecompression = System.Net.DecompressionMethods.All;
                    // Connection pooling için ayarlar
                    handler.MaxConnectionsPerServer = 10;
                    
                    using (var client = new HttpClient(handler))
                    {
                        client.Timeout = TimeSpan.FromMinutes(10); // 10 dakika timeout (büyük dosyalar için)
                        client.DefaultRequestHeaders.Add("User-Agent", "PinhumanSuperAPP");
                        client.DefaultRequestHeaders.Add("Accept", "application/octet-stream");
                        // Compression desteği için Accept-Encoding header'ı ekle
                        client.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate, br");
                    
                        // GitHub Token kontrolü (eğer varsa authentication ekle)
                        try
                        {
                            var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN") ?? 
                                       Environment.GetEnvironmentVariable("GH_TOKEN");
                            
                            if (!string.IsNullOrEmpty(token))
                            {
                                client.DefaultRequestHeaders.Authorization = 
                                    new AuthenticationHeaderValue("Bearer", token);
                                Debug.WriteLine("GitHub Token kullanılıyor (indirme için)");
                            }
                        }
                        catch
                        {
                            // Token yoksa public repository olarak devam et
                        }
                        
                        Debug.WriteLine("HTTP isteği gönderiliyor...");
                        HttpResponseMessage? response = null;
                        
                        // Önce browser_download_url'i dene
                        try
                        {
                            response = await client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                            Debug.WriteLine($"HTTP Status Code (browser_download_url): {response.StatusCode}");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"browser_download_url hatası: {ex.Message}");
                        }
                        
                        // Eğer 404 alındıysa ve API URL varsa, API URL'ini dene
                        if (response != null && response.StatusCode == System.Net.HttpStatusCode.NotFound && !string.IsNullOrEmpty(apiAssetUrl))
                        {
                            Debug.WriteLine("browser_download_url 404 döndü, API URL deneniyor...");
                            response?.Dispose();
                            
                            // API URL'ini kullan (draft release'ler için)
                            client.DefaultRequestHeaders.Accept.Clear();
                            client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/octet-stream"));
                            
                            response = await client.GetAsync(apiAssetUrl, HttpCompletionOption.ResponseHeadersRead);
                            Debug.WriteLine($"HTTP Status Code (API URL): {response.StatusCode}");
                        }
                        
                        if (response == null)
                        {
                            throw new HttpRequestException("HTTP isteği oluşturulamadı.");
                        }
                        
                        Debug.WriteLine($"Content Length: {response.Content.Headers.ContentLength}");
                        
                        if (!response.IsSuccessStatusCode)
                        {
                            var errorContent = await response.Content.ReadAsStringAsync();
                            Debug.WriteLine($"Hata yanıtı: {errorContent}");
                            
                            // 404 hatası için özel mesaj
                            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                            {
                                throw new HttpRequestException($"Asset bulunamadı (404).\n\n" +
                                                              $"Olası nedenler:\n" +
                                                              $"1. Release draft durumunda ve asset henüz yüklenmemiş\n" +
                                                              $"2. Asset adı veya URL yanlış\n" +
                                                              $"3. Repository private ve authentication gerekiyor\n" +
                                                              $"4. Asset henüz tam yüklenmemiş (yükleme devam ediyor)\n\n" +
                                                              $"URL: {downloadUrl}\n" +
                                                              $"{(string.IsNullOrEmpty(apiAssetUrl) ? "" : $"API URL: {apiAssetUrl}\n")}" +
                                                              $"Yanıt: {errorContent.Substring(0, Math.Min(500, errorContent.Length))}");
                            }
                            
                            throw new HttpRequestException($"HTTP {response.StatusCode}: {response.ReasonPhrase}. URL: {downloadUrl}. Yanıt: {errorContent.Substring(0, Math.Min(500, errorContent.Length))}");
                        }
                        
                        response.EnsureSuccessStatusCode();
                        
                        var totalBytes = response.Content.Headers.ContentLength ?? 0;
                        var downloadedBytes = 0L;
                        
                        // FileStream buffer'ını artır (256KB) - daha hızlı yazma için
                        using (var fileStream = new FileStream(tempZipPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 262144))
                        using (var httpStream = await response.Content.ReadAsStreamAsync())
                        {
                            // Buffer boyutunu önemli ölçüde artır (1MB) - daha hızlı indirme için
                            var buffer = new byte[1048576]; // 1 MB (256 KB yerine)
                        int bytesRead;
                        var lastProgressUpdate = 0.0;
                        var lastProgressUpdateTime = DateTime.Now;
                        var progressUpdateInterval = 2.0; // Her %2'de bir güncelle (daha az UI güncellemesi)
                        var minTimeBetweenUpdates = TimeSpan.FromMilliseconds(200); // Minimum 200ms aralık
                        
                            while ((bytesRead = await httpStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                            {
                                await fileStream.WriteAsync(buffer, 0, bytesRead);
                                downloadedBytes += bytesRead;
                                
                                if (totalBytes > 0 && progress != null)
                                {
                                    var percentage = (double)downloadedBytes / totalBytes * 100;
                                    var timeSinceLastUpdate = DateTime.Now - lastProgressUpdateTime;
                                    
                                    // Progress'i sadece belirli aralıklarla güncelle (performans için)
                                    // Hem yüzde hem de zaman bazlı kontrol
                                    if ((percentage - lastProgressUpdate >= progressUpdateInterval || percentage >= 100) &&
                                        (timeSinceLastUpdate >= minTimeBetweenUpdates || percentage >= 100))
                                    {
                                        progress.Report(percentage);
                                        lastProgressUpdate = percentage;
                                        lastProgressUpdateTime = DateTime.Now;
                                    }
                                }
                            }
                            
                            // Son progress güncellemesi
                            if (totalBytes > 0 && progress != null)
                            {
                                progress.Report(100);
                            }
                        }
                    }
                }
                
                    // Progress zaten yukarıda güncelleniyor, burada tekrar güncellemeye gerek yok
                
                // Kullanıcı verilerini korumak için yedekle
                var userDataFiles = new[]
                {
                    "config.json",
                    "firebase-config.json",
                    "PinhumanSuperAPP.deps.json",
                    "security-profile.json",
                    "mail_history.json",
                    "previously_downloaded.json",
                    "pin_security.json",
                    "remember_me.txt",
                    "debug_log.txt"
                };
                
                var backupDir = Path.Combine(Path.GetTempPath(), $"backup_{DateTime.Now:yyyyMMddHHmmss}");
                Directory.CreateDirectory(backupDir);
                
                foreach (var file in userDataFiles)
                {
                    var sourcePath = Path.Combine(appDirectory, file);
                    if (File.Exists(sourcePath))
                    {
                        var destPath = Path.Combine(backupDir, file);
                        File.Copy(sourcePath, destPath, true);
                    }
                }
                
                // Kullanıcı verilerini içeren dosya pattern'lerini yedekle
                foreach (var pattern in new[] { "pdks-config*.json", "personnel-config*.json", "*.db", "*.sqlite" })
                {
                    var files = Directory.GetFiles(appDirectory, pattern);
                    foreach (var file in files)
                    {
                        var fileName = Path.GetFileName(file);
                        var destPath = Path.Combine(backupDir, fileName);
                        File.Copy(file, destPath, true);
                    }
                }
                
                // Zip'i çıkar (kullanıcı verilerini üzerine yazmadan)
                using (var archive = ZipFile.OpenRead(tempZipPath))
                {
                    foreach (var entry in archive.Entries)
                    {
                        try
                        {
                            // Entry FullName'ini temizle (başındaki ve sonundaki slash'leri kaldır)
                            var entryName = entry.FullName.Replace('\\', '/').TrimStart('/');
                            
                            // Eğer absolute path içeriyorsa, sadece dosya adını al
                            if (Path.IsPathRooted(entryName))
                            {
                                entryName = Path.GetFileName(entryName);
                            }
                            
                            // Boş entry'leri atla
                            if (string.IsNullOrWhiteSpace(entryName) || entryName == "/")
                            {
                                continue;
                            }
                            
                            // Klasör entry'lerini atla (sadece dosyaları çıkar)
                            if (entryName.EndsWith("/") || entryName.EndsWith("\\"))
                            {
                                continue;
                            }
                            
                            var destPath = Path.Combine(appDirectory, entryName);
                            
                            // Path uzunluğunu kontrol et (Windows limit: 260 karakter, uzun path desteği varsa 32767)
                            if (destPath.Length > 260)
                            {
                                Debug.WriteLine($"⚠️ Path çok uzun, atlanıyor: {destPath.Substring(0, Math.Min(100, destPath.Length))}...");
                                continue;
                            }
                            
                            var destDir = Path.GetDirectoryName(destPath);
                            
                            if (!string.IsNullOrEmpty(destDir))
                            {
                                // Klasörü oluştur (yoksa)
                                if (!Directory.Exists(destDir))
                                {
                                    Directory.CreateDirectory(destDir);
                                }
                            }
                            
                            // Kullanıcı verilerini atla
                            var shouldSkip = false;
                            foreach (var userFile in userDataFiles)
                            {
                                if (entryName.Equals(userFile, StringComparison.OrdinalIgnoreCase) ||
                                    entryName.EndsWith(userFile, StringComparison.OrdinalIgnoreCase))
                                {
                                    shouldSkip = true;
                                    break;
                                }
                            }
                            
                            if (shouldSkip) continue;
                            
                            // Pattern eşleşmelerini kontrol et
                            if (entryName.Contains("pdks-config") || 
                                entryName.Contains("personnel-config") ||
                                entryName.Contains("security-profile") ||
                                entryName.EndsWith(".deps.json") ||
                                entryName.EndsWith(".db") || 
                                entryName.EndsWith(".sqlite"))
                            {
                                continue;
                            }
                            
                            // Dosyayı çıkar
                            entry.ExtractToFile(destPath, overwrite: true);
                        }
                        catch (Exception entryEx)
                        {
                            // Tek bir entry hatası tüm işlemi durdurmasın
                            Debug.WriteLine($"⚠️ Entry çıkarılamadı: {entry.FullName} - {entryEx.Message}");
                            // Devam et, diğer dosyaları çıkarmaya devam et
                            continue;
                        }
                    }
                }
                
                // Yedeklenen kullanıcı verilerini geri yükle
                foreach (var backupFile in Directory.GetFiles(backupDir))
                {
                    var fileName = Path.GetFileName(backupFile);
                    var destPath = Path.Combine(appDirectory, fileName);
                    if (File.Exists(backupFile))
                    {
                        File.Copy(backupFile, destPath, true);
                    }
                }
                
                // Yedek klasörünü temizle
                Directory.Delete(backupDir, true);
                
                // Geçici zip dosyasını sil
                File.Delete(tempZipPath);
                
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Güncelleme indirme/çıkarma hatası: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                if (ex.InnerException != null)
                {
                    Debug.WriteLine($"Inner exception: {ex.InnerException.Message}");
                }
                // Exception'ı fırlat ki çağıran kod hata detaylarını görebilsin
                throw new Exception($"Güncelleme indirme/çıkarma hatası: {ex.Message}", ex);
            }
        }
    }
}


