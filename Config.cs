using System.Text.Json;
using System.IO;

public class AppConfig
{
    public AutoLoginConfig AutoLogin { get; set; } = new();
    public ScrapingConfig Scraping { get; set; } = new();
    public DownloadConfig Download { get; set; } = new();
    public NotificationConfig Notification { get; set; } = new();
    public SmsConfig Sms { get; set; } = new();
    public PinhumanConfig Pinhuman { get; set; } = new();
    public UpdateConfig Update { get; set; } = new();
    
    public static AppConfig Load()
    {
        return ConfigManager.LoadConfig();
    }
}

public class AutoLoginConfig
{
    public bool Enabled { get; set; } = true;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string CompanyCode { get; set; } = "";
    public string TotpSecret { get; set; } = "";
}

public class ScrapingConfig
{
    public string TargetUrl { get; set; } = "";
    public string CssClass { get; set; } = "";
    public string StatusClass { get; set; } = "";
}

public class DownloadConfig
{
    public int MaxConcurrentDownloads { get; set; } = 3;
    public int DownloadTimeout { get; set; } = 1800;
    public bool RetryFailedDownloads { get; set; } = true;
    public string OutputFolder { get; set; } = "";
}

        public class NotificationConfig
{
    public bool Enabled { get; set; } = false;
    public string SenderEmail { get; set; } = "furkan.ozmen@guleryuzgroup.com";
    public List<KeywordNotification> Keywords { get; set; } = new();
}

public class SmsConfig
{
    public bool HeadlessMode { get; set; } = true;
    public bool ShowDuplicates { get; set; } = true;
}

public class PinhumanConfig
{
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
    public string TotpSecret { get; set; } = "";
    public string CompanyName { get; set; } = "HOROZ";
    public string LocationName { get; set; } = "yunusemre";
    public bool HeadlessMode { get; set; } = false;
}

public class UpdateConfig
{
    public bool Enabled { get; set; } = true;
    public string UpdateUrl { get; set; } = "https://github.com/furkanozm/PinhumanSuperAPP-Release";
    public bool CheckOnStartup { get; set; } = true;
    public bool AutoDownload { get; set; } = true;
    public int CheckIntervalMinutes { get; set; } = 60;
}

    public class KeywordNotification
    {
        public string Keyword { get; set; } = "";
        public string EmailRecipient { get; set; } = "";
        public bool Enabled { get; set; } = true;
    }

public static class ConfigManager
{
    private const string ConfigFileName = "config.json";
    
    public static AppConfig LoadConfig()
    {
        try
        {
            if (File.Exists(ConfigFileName))
            {
                var jsonContent = File.ReadAllText(ConfigFileName);
                
                var config = JsonSerializer.Deserialize<AppConfig>(jsonContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                
                if (config != null)
                {
                    return config;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ Config dosyası okunamadı: {ex.Message}");
            Console.WriteLine("Varsayılan ayarlar kullanılacak.");
        }
        
        // Varsayılan config
        return new AppConfig
        {
            AutoLogin = new AutoLoginConfig
            {
                Enabled = false,
                Username = "",
                Password = "",
                CompanyCode = "",
                TotpSecret = ""
            },
            Scraping = new ScrapingConfig
            {
                TargetUrl = "https://www.pinhuman.net/",
                CssClass = "card-body",
                StatusClass = "badge-success"
            },
            Download = new DownloadConfig
            {
                MaxConcurrentDownloads = 3,
                DownloadTimeout = 1800,
                RetryFailedDownloads = true,
                OutputFolder = ""
            },
            Notification = new NotificationConfig
            {
                Enabled = false,
                SenderEmail = "furkan.ozmen@guleryuzgroup.com",
                                    Keywords = new List<KeywordNotification>
                    {
                        new KeywordNotification
                        {
                            Keyword = "İSTANBUL",
                            EmailRecipient = "istanbul@firma.com",
                            Enabled = true
                        },
                        new KeywordNotification
                        {
                            Keyword = "ANKARA",
                            EmailRecipient = "ankara@firma.com",
                            Enabled = true
                        },
                        new KeywordNotification
                        {
                            Keyword = "KONYA",
                            EmailRecipient = "konya@firma.com",
                            Enabled = true
                        }
                    }
            },
            Sms = new SmsConfig
            {
                HeadlessMode = true,
                ShowDuplicates = true
            },
            Pinhuman = new PinhumanConfig
            {
                Email = "",
                Password = "",
                TotpSecret = "",
                CompanyName = "HOROZ",
                LocationName = "yunusemre",
                HeadlessMode = false
            },
            Update = new UpdateConfig
            {
                Enabled = true,
                UpdateUrl = "https://github.com/furkanozm/PinhumanSuperAPP-Release",
                CheckOnStartup = true,
                AutoDownload = true,
                CheckIntervalMinutes = 60
            }
        };
    }
    
    public static void SaveConfig(AppConfig config)
    {
        try
        {
            var jsonContent = JsonSerializer.Serialize(config, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            
            File.WriteAllText(ConfigFileName, jsonContent);
            Console.WriteLine("✅ Config dosyası kaydedildi.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Config dosyası kaydedilemedi: {ex.Message}");
        }
    }
    
    public static void ShowConfigMenu()
    {
        var config = LoadConfig();
        
        while (true)
        {
            Console.Clear();
            Console.WriteLine("⚙️  AYARLAR MENÜSÜ");
            Console.WriteLine(new string('═', 40));
            Console.WriteLine($"1. Otomatik Giriş: {(config.AutoLogin.Enabled ? "✅ Açık" : "❌ Kapalı")}");
            Console.WriteLine($"2. Kullanıcı Adı: {config.AutoLogin.Username}");
            Console.WriteLine($"3. Firma Kodu: {config.AutoLogin.CompanyCode}");
            Console.WriteLine($"4. TOTP Secret: {(string.IsNullOrEmpty(config.AutoLogin.TotpSecret) ? "❌ Yok" : "✅ Var")}");
            Console.WriteLine($"5. Hedef URL: https://pinhuman.net");
            Console.WriteLine($"6. İndirme Ayarları");
            Console.WriteLine($"7. Config Dosyasını Yeniden Yükle");
            Console.WriteLine($"8. Ana Menüye Dön");
            Console.WriteLine(new string('═', 40));
            Console.Write("Seçiminizi yapın (1-8): ");
            
            var choice = Console.ReadLine()?.Trim();
            
            switch (choice)
            {
                case "1":
                    ToggleAutoLogin(config);
                    break;
                case "2":
                    UpdateUsername(config);
                    break;
                case "3":
                    UpdateCompanyCode(config);
                    break;
                case "4":
                    UpdateTotpSecret(config);
                    break;
                case "5":
                    UpdateTargetUrl(config);
                    break;
                case "6":
                    ShowDownloadSettings(config);
                    break;
                case "7":
                    config = LoadConfig();
                    Console.WriteLine("✅ Config yeniden yüklendi. Enter'a basın...");
                    Console.ReadLine();
                    break;
                case "8":
                    return;
                default:
                    Console.WriteLine("❌ Geçersiz seçim! Enter'a basın...");
                    Console.ReadLine();
                    break;
            }
        }
    }
    
    private static void ToggleAutoLogin(AppConfig config)
    {
        config.AutoLogin.Enabled = !config.AutoLogin.Enabled;
        SaveConfig(config);
        
        Console.WriteLine($"✅ Otomatik giriş {(config.AutoLogin.Enabled ? "açıldı" : "kapatıldı")}.");
        Console.WriteLine("Enter'a basın...");
        Console.ReadLine();
    }
    
    private static void UpdateUsername(AppConfig config)
    {
        Console.Write($"Mevcut kullanıcı adı: {config.AutoLogin.Username}");
        Console.Write("\nYeni kullanıcı adı (boş bırakın değiştirmek istemiyorsanız): ");
        var newUsername = Console.ReadLine()?.Trim();
        
        if (!string.IsNullOrEmpty(newUsername))
        {
            config.AutoLogin.Username = newUsername;
            SaveConfig(config);
            Console.WriteLine("✅ Kullanıcı adı güncellendi.");
        }
        else
        {
            Console.WriteLine("❌ Değişiklik yapılmadı.");
        }
        
        Console.WriteLine("Enter'a basın...");
        Console.ReadLine();
    }
    
    private static void UpdateCompanyCode(AppConfig config)
    {
        Console.Write($"Mevcut firma kodu: {config.AutoLogin.CompanyCode}");
        Console.Write("\nYeni firma kodu (boş bırakın değiştirmek istemiyorsanız): ");
        var newCompanyCode = Console.ReadLine()?.Trim();
        
        if (!string.IsNullOrEmpty(newCompanyCode))
        {
            config.AutoLogin.CompanyCode = newCompanyCode;
            SaveConfig(config);
            Console.WriteLine("✅ Firma kodu güncellendi.");
        }
        else
        {
            Console.WriteLine("❌ Değişiklik yapılmadı.");
        }
        
        Console.WriteLine("Enter'a basın...");
        Console.ReadLine();
    }
    
    private static void UpdateTotpSecret(AppConfig config)
    {
        Console.Write($"Mevcut TOTP Secret: {(string.IsNullOrEmpty(config.AutoLogin.TotpSecret) ? "Yok" : "Var")}");
        Console.Write("\nYeni TOTP Secret (boş bırakın değiştirmek istemiyorsanız): ");
        var newTotpSecret = Console.ReadLine()?.Trim();
        
        if (!string.IsNullOrEmpty(newTotpSecret))
        {
            config.AutoLogin.TotpSecret = newTotpSecret;
            SaveConfig(config);
            Console.WriteLine("✅ TOTP Secret güncellendi.");
        }
        else
        {
            Console.WriteLine("❌ Değişiklik yapılmadı.");
        }
        
        Console.WriteLine("Enter'a basın...");
        Console.ReadLine();
    }
    
    private static void UpdateTargetUrl(AppConfig config)
    {
        Console.Write($"Mevcut hedef URL: https://pinhuman.net");
        Console.Write("\nYeni hedef URL (boş bırakın değiştirmek istemiyorsanız): ");
        var newUrl = Console.ReadLine()?.Trim();
        
        if (!string.IsNullOrEmpty(newUrl))
        {
            config.Scraping.TargetUrl = "https://pinhuman.net";
            SaveConfig(config);
            Console.WriteLine("✅ Hedef URL güncellendi.");
        }
        else
        {
            Console.WriteLine("❌ Değişiklik yapılmadı.");
        }
        
        Console.WriteLine("Enter'a basın...");
        Console.ReadLine();
    }
    
    private static void ShowDownloadSettings(AppConfig config)
    {
        while (true)
        {
            Console.Clear();
            Console.WriteLine("📥 İNDİRME AYARLARI");
            Console.WriteLine(new string('═', 30));
            Console.WriteLine($"1. Eşzamanlı İndirme: {config.Download.MaxConcurrentDownloads}");
            Console.WriteLine($"2. Timeout (saniye): {config.Download.DownloadTimeout}");
            Console.WriteLine($"3. Başarısız İndirmeleri Tekrar Dene: {(config.Download.RetryFailedDownloads ? "✅ Evet" : "❌ Hayır")}");
            Console.WriteLine($"4. Çıktı Klasörü: {(string.IsNullOrEmpty(config.Download.OutputFolder) ? "Varsayılan (cikti)" : config.Download.OutputFolder)}");
            Console.WriteLine($"5. Ana Menüye Dön");
            Console.WriteLine(new string('═', 30));
            Console.Write("Seçiminizi yapın (1-5): ");
            
            var choice = Console.ReadLine()?.Trim();
            
            switch (choice)
            {
                case "1":
                    UpdateMaxConcurrentDownloads(config);
                    break;
                case "2":
                    UpdateDownloadTimeout(config);
                    break;
                case "3":
                    ToggleRetryFailedDownloads(config);
                    break;
                case "4":
                    UpdateOutputFolder(config);
                    break;
                case "5":
                    return;
                default:
                    Console.WriteLine("❌ Geçersiz seçim! Enter'a basın...");
                    Console.ReadLine();
                    break;
            }
        }
    }
    
    private static void UpdateMaxConcurrentDownloads(AppConfig config)
    {
        Console.Write($"Mevcut eşzamanlı indirme: {config.Download.MaxConcurrentDownloads}");
        Console.Write("\nYeni değer (1-10, boş bırakın değiştirmek istemiyorsanız): ");
        var input = Console.ReadLine()?.Trim();
        
        if (int.TryParse(input, out var newValue) && newValue >= 1 && newValue <= 10)
        {
            config.Download.MaxConcurrentDownloads = newValue;
            SaveConfig(config);
            Console.WriteLine("✅ Eşzamanlı indirme sayısı güncellendi.");
        }
        else if (string.IsNullOrEmpty(input))
        {
            Console.WriteLine("❌ Değişiklik yapılmadı.");
        }
        else
        {
            Console.WriteLine("❌ Geçersiz değer! 1-10 arası olmalı.");
        }
        
        Console.WriteLine("Enter'a basın...");
        Console.ReadLine();
    }
    
    private static void UpdateDownloadTimeout(AppConfig config)
    {
        Console.Write($"Mevcut timeout: {config.Download.DownloadTimeout} saniye");
        Console.Write("\nYeni timeout (saniye, 60-3600, boş bırakın değiştirmek istemiyorsanız): ");
        var input = Console.ReadLine()?.Trim();
        
        if (int.TryParse(input, out var newValue) && newValue >= 60 && newValue <= 3600)
        {
            config.Download.DownloadTimeout = newValue;
            SaveConfig(config);
            Console.WriteLine("✅ Timeout güncellendi.");
        }
        else if (string.IsNullOrEmpty(input))
        {
            Console.WriteLine("❌ Değişiklik yapılmadı.");
        }
        else
        {
            Console.WriteLine("❌ Geçersiz değer! 60-3600 arası olmalı.");
        }
        
        Console.WriteLine("Enter'a basın...");
        Console.ReadLine();
    }
    
    private static void ToggleRetryFailedDownloads(AppConfig config)
    {
        config.Download.RetryFailedDownloads = !config.Download.RetryFailedDownloads;
        SaveConfig(config);
        
        Console.WriteLine($"✅ Başarısız indirmeleri tekrar deneme {(config.Download.RetryFailedDownloads ? "açıldı" : "kapatıldı")}.");
        Console.WriteLine("Enter'a basın...");
        Console.ReadLine();
    }
    
    private static void UpdateOutputFolder(AppConfig config)
    {
        Console.WriteLine($"Mevcut çıktı klasörü: {(string.IsNullOrEmpty(config.Download.OutputFolder) ? "Varsayılan (dist/cikti)" : config.Download.OutputFolder)}");
        Console.WriteLine("⚠️  ÖNEMLİ: Domain ortamında mutlak yollar VTROOT'a gidebilir!");
        Console.WriteLine("Önerilen klasör yolları (relatif):");
        Console.WriteLine("  - dist/cikti (önerilen)");
        Console.WriteLine("  - cikti");
        Console.WriteLine("  - output");
        Console.WriteLine("  - downloads");
        Console.WriteLine("  - Boş bırakın varsayılan klasörü kullanmak için");
        Console.Write("\nYeni çıktı klasörü yolu: ");
        var newOutputFolder = Console.ReadLine()?.Trim();
        
        if (string.IsNullOrEmpty(newOutputFolder))
        {
            config.Download.OutputFolder = "";
            SaveConfig(config);
            Console.WriteLine("✅ Varsayılan çıktı klasörü kullanılacak (dist/cikti).");
        }
        else
        {
            try
            {
                // Relatif yol olarak kaydet (mutlak yol yapma)
                config.Download.OutputFolder = newOutputFolder;
                SaveConfig(config);
                Console.WriteLine($"✅ Çıktı klasörü güncellendi: {newOutputFolder}");
                Console.WriteLine($"📁 Tam yol: {System.IO.Path.Combine(Directory.GetCurrentDirectory(), newOutputFolder)}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Geçersiz klasör yolu: {ex.Message}");
            }
        }
        
        Console.WriteLine("Enter'a basın...");
        Console.ReadLine();
    }
}
