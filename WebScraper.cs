using Microsoft.Playwright;
using HtmlAgilityPack;
using OfficeOpenXml;
using System.Text.RegularExpressions;
using OtpNet;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using NPOI.HSSF.UserModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Net.Http;
using Newtonsoft.Json;

namespace WebScraper
{
    // Native Windows API methods for window management
    internal static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern bool BringWindowToTop(IntPtr hWnd);
    }

    public enum StatusType
    {
        Ready,
        Processing,
        Success,
        Warning,
        Error
    }

    public class ScrapedItem
    {
        public string OriginalUrl { get; set; } = "";
        public string FullUrl { get; set; } = "";
        public string Status { get; set; } = "";
        public string ElementText { get; set; } = "";
        public string CreatorName { get; set; } = "";
        public string ItemId { get; set; } = ""; // URL'den çıkarılan UUID
        public string? DownloadedFilePath { get; set; }
        public long DownloadSize { get; set; }
        public DateTime? DownloadDate { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class WebScraper
    {
        private readonly List<ScrapedItem> _scrapedItems = new();
        
        public async Task ScrapeAndDownloadAsync(string username, string password, string companyCode, string totpSecret, string cssClass, string statusClass, AppConfig config, string pageType, int pageSize, CancellationToken cancellationToken, Action<int, int>? progressCallback = null, Action<string, string, StatusType>? statusCallback = null, Action<string>? logCallback = null, Action<int>? foundCallback = null, Action<int>? downloadedCallback = null, Action<decimal>? totalAmountCallback = null)
        {
            // Her yeni işlem başladığında önceki verileri temizle
            lock (_scrapedItems)
            {
                _scrapedItems.Clear();
            }
            
            // URL'i hardcoded yap
            const string url = "https://www.pinhuman.net";
            logCallback?.Invoke($"URL scraping başlatılıyor: {url}");
            
            var playwright = await Playwright.CreateAsync();
            IBrowser browser;
            
            // Cross-platform tarayıcı başlatma
            try
            {
                // Config'den headless mod ayarını al
                var isHeadless = config.Sms.HeadlessMode;
                logCallback?.Invoke($"Gizli mod ayarı: {(isHeadless ? "Açık" : "Kapalı")}");
                
                browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Headless = isHeadless, // Config'den alınan ayar
                    Args = new[] { "--no-sandbox", "--disable-dev-shm-usage", "--disable-web-security", "--disable-features=VizDisplayCompositor" }
                });
            }
            catch (Exception ex) when (ex.Message.Contains("Executable doesn't exist"))
            {
                logCallback?.Invoke("Playwright tarayıcıları yükleniyor...");
                
                // Cross-platform Chrome yolları
                var chromePaths = new List<string>();
                
                if (OperatingSystem.IsWindows())
                {
                    chromePaths.AddRange(new[]
                    {
                        @"C:\Program Files\Google\Chrome\Application\chrome.exe",
                        @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + @"\Google\Chrome\Application\chrome.exe"
                    });
                }
                else if (OperatingSystem.IsMacOS())
                {
                    chromePaths.AddRange(new[]
                    {
                        "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
                        "/Applications/Chromium.app/Contents/MacOS/Chromium"
                    });
                }
                else if (OperatingSystem.IsLinux())
                {
                    chromePaths.AddRange(new[]
                    {
                        "/usr/bin/google-chrome",
                        "/usr/bin/google-chrome-stable",
                        "/usr/bin/chromium-browser",
                        "/usr/bin/chromium"
                    });
                }
                
                // Mevcut Chrome'u bul ve kullan
                string? foundChromePath = null;
                foreach (var path in chromePaths)
                {
                    if (System.IO.File.Exists(path))
                    {
                        foundChromePath = path;
                        break;
                    }
                }
                
                if (!string.IsNullOrEmpty(foundChromePath))
                {
                    try
                    {
                        browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                        {
                            Headless = config.Sms.HeadlessMode, // Config'den alınan ayar
                            ExecutablePath = foundChromePath,
                            Args = new[] { "--no-sandbox", "--disable-dev-shm-usage", "--disable-web-security", "--disable-features=VizDisplayCompositor" }
                        });
                    }
                    catch (Exception chromeEx)
                    {
                        logCallback?.Invoke($"Chrome başlatılamadı: {chromeEx.Message}");
                        ShowInstallationInstructions(logCallback);
                        throw;
                    }
                }
                else
                {
                    ShowInstallationInstructions(logCallback);
                    throw new Exception("Playwright tarayıcıları yüklenmemiş. Lütfen yukarıdaki komutları çalıştırın.");
                }
            }
            
            var page = await browser.NewPageAsync();
            
            // Page'i de dispose etme - browser ile birlikte açık kalacak
            
            try
            {
                statusCallback?.Invoke("Login", "Login sayfası yükleniyor...", StatusType.Processing);
                logCallback?.Invoke("Login sayfası yükleniyor...");
                
                await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
                
                // Login işlemi - config'e göre otomatik veya manuel
                if (config.AutoLogin.Enabled)
                {
                    await PerformLoginAsync(page, username, password, companyCode, totpSecret, statusCallback, logCallback);
                }
                else
                {
                    statusCallback?.Invoke("Manuel Giriş", "Manuel giriş bekleniyor...", StatusType.Warning);
                    logCallback?.Invoke("Manuel giriş bekleniyor...");
                    
                    // Dıt sesi çal - kullanıcıya cevap vermesi gerektiğini bildir
                    PlayNotificationSound();
                    
                    // Manuel giriş için bekle - daha uzun süre
                    await Task.Delay(25000, cancellationToken);
                    
                    // Login başarısını kontrol et
                    await CheckLoginSuccessAsync(page, statusCallback, logCallback);
                }
                
                statusCallback?.Invoke("Sayfa Seçimi", "Hedef sayfa seçiliyor...", StatusType.Processing);
                logCallback?.Invoke("Hedef sayfa seçiliyor...");
                
                string targetPageUrl;
                string pageName;
                
                switch (pageType)
                {
                    case "advance":
                        targetPageUrl = "https://www.pinhuman.net/StaffAdvancePaymentOrder";
                        pageName = "Avans Ödeme Emri";
                        break;
                    case "normal":
                    default:
                        targetPageUrl = "https://www.pinhuman.net/StaffPaymentOrder";
                        pageName = "Normal Ödeme Emri";
                        break;
                }
                
                logCallback?.Invoke($"{pageName} sayfasına gidiliyor...");
                
                // Seçilen sayfaya git
                await page.GotoAsync(targetPageUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
                logCallback?.Invoke($"{pageName} sayfası yüklendi.");
                
                // Sayfa sayısını 120'ye ayarla
                await SetPageSizeAsync(page, pageSize, statusCallback, logCallback);
                
                // URL'leri otomatik bul ve işle
                var onaylandiCount = await FindAndProcessUrlsAsync(page, cssClass, statusClass, config, progressCallback, statusCallback, logCallback, cancellationToken, foundCallback, downloadedCallback, totalAmountCallback);
                
                // İlk sayfada bulunan onaylandı sayısı kadar onaylandı aranacak
                await CheckOtherPagesIfNeeded(page, cssClass, statusClass, onaylandiCount, statusCallback, logCallback, cancellationToken);
            }
            catch (Exception)
            {
                logCallback?.Invoke("Scraping sırasında hata oluştu.");
                throw;
            }
            finally
            {
                try
                {
                    // Tarayıcıyı açık bırak, sadece playwright'ı dispose et
                    if (browser != null)
                    {
                        // Browser'ı kapatma, sadece playwright'ı dispose et
                        logCallback?.Invoke("🔍 Tarayıcı açık bırakıldı. Manuel işlem yapabilirsiniz.");
                    }
                }
                catch (Exception ex)
                {
                    logCallback?.Invoke($"Tarayıcı işlemi sırasında hata: {ex.Message}");
                }
            }
        }
        
        public void ForceCloseBrowser()
        {
            try
            {
                // Tüm Chrome/Chromium process'lerini kapat
                var chromeProcesses = System.Diagnostics.Process.GetProcessesByName("chrome");
                var chromiumProcesses = System.Diagnostics.Process.GetProcessesByName("chromium");
                var msedgeProcesses = System.Diagnostics.Process.GetProcessesByName("msedge");
                
                var allBrowserProcesses = chromeProcesses.Concat(chromiumProcesses).Concat(msedgeProcesses);
                
                foreach (var process in allBrowserProcesses)
                {
                    try
                    {
                        var commandLine = GetCommandLine(process.Id);
                        
                        // Playwright'ın açtığı tarayıcılarda bu argümanlar bulunur
                        if (commandLine.Contains("--remote-debugging-port") || 
                            commandLine.Contains("--disable-dev-shm-usage") ||
                            commandLine.Contains("--no-sandbox") ||
                            commandLine.Contains("--disable-background-timer-throttling") ||
                            commandLine.Contains("--disable-backgrounding-occluded-windows") ||
                            commandLine.Contains("--disable-renderer-backgrounding"))
                        {
                            process.Kill(true); // Force kill
                            process.WaitForExit(5000);
                        }
                    }
                    catch { /* Sessizce geç */ }
                }
                
                // Ayrıca tüm Playwright ile ilgili process'leri de kapat
                var playwrightProcesses = System.Diagnostics.Process.GetProcessesByName("playwright");
                foreach (var process in playwrightProcesses)
                {
                    try
                    {
                        process.Kill(true);
                        process.WaitForExit(3000);
                    }
                    catch { /* Sessizce geç */ }
                }
            }
            catch { /* Sessizce geç */ }
        }

        private string GetCommandLine(int processId)
        {
            try
            {
                using var searcher = new System.Management.ManagementObjectSearcher(
                    $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {processId}");
                using var objects = searcher.Get();
                
                foreach (System.Management.ManagementObject obj in objects)
                {
                    return obj["CommandLine"]?.ToString() ?? "";
                }
            }
            catch { /* Sessizce geç */ }
            
            return "";
        }

        private void ShowInstallationInstructions(Action<string>? logCallback)
        {
            logCallback?.Invoke("\n" + new string('=', 60));
            logCallback?.Invoke("🔧 KURULUM TALİMATLARI");
            logCallback?.Invoke(new string('=', 60));
            
            if (OperatingSystem.IsWindows())
            {
                logCallback?.Invoke("\n📋 Windows için:");
                logCallback?.Invoke("1. Google Chrome'u indirin: https://www.google.com/chrome/");
                logCallback?.Invoke("2. Chrome'u yükleyin");
                logCallback?.Invoke("3. Programı tekrar çalıştırın");
                logCallback?.Invoke("\nAlternatif olarak:");
                logCallback?.Invoke("1. dotnet tool install --global Microsoft.Playwright.CLI");
                logCallback?.Invoke("2. playwright install chromium");
                logCallback?.Invoke("3. Programı tekrar çalıştırın");
            }
            else if (OperatingSystem.IsMacOS())
            {
                logCallback?.Invoke("\n📋 macOS için:");
                logCallback?.Invoke("1. Homebrew ile Chrome yükleyin:");
                logCallback?.Invoke("   brew install --cask google-chrome");
                logCallback?.Invoke("2. Programı tekrar çalıştırın");
                logCallback?.Invoke("\nAlternatif olarak:");
                logCallback?.Invoke("1. dotnet tool install --global Microsoft.Playwright.CLI");
                logCallback?.Invoke("2. playwright install chromium");
                logCallback?.Invoke("3. Programı tekrar çalıştırın");
            }
            else if (OperatingSystem.IsLinux())
            {
                logCallback?.Invoke("\n📋 Linux için:");
                logCallback?.Invoke("Ubuntu/Debian:");
                logCallback?.Invoke("1. sudo apt update");
                logCallback?.Invoke("2. sudo apt install google-chrome-stable");
                logCallback?.Invoke("3. Programı tekrar çalıştırın");
                logCallback?.Invoke("\nCentOS/RHEL/Fedora:");
                logCallback?.Invoke("1. sudo dnf install google-chrome-stable");
                logCallback?.Invoke("2. Programı tekrar çalıştırın");
                logCallback?.Invoke("\nAlternatif olarak:");
                logCallback?.Invoke("1. dotnet tool install --global Microsoft.Playwright.CLI");
                logCallback?.Invoke("2. playwright install chromium");
                logCallback?.Invoke("3. Programı tekrar çalıştırın");
            }
            
            logCallback?.Invoke(new string('=', 60));
        }
        
        private async Task PerformLoginAsync(IPage page, string username, string password, string companyCode, string totpSecret, Action<string, string, StatusType>? statusCallback, Action<string>? logCallback)
        {
            statusCallback?.Invoke("Login", "Login işlemi başlatılıyor...", StatusType.Processing);
            logCallback?.Invoke("Login işlemi başlatılıyor...");
            
            // Bu form için özel login işlemi
            await FillLoginFormAsync(page, username, password, companyCode, statusCallback, logCallback);
            
            // İlk login butonuna tıkla
            await ClickLoginButtonAsync(page, statusCallback, logCallback);
            
            // 2FA kontrolü ve TOTP kodu üretimi
            await Handle2FAWithTOTPAsync(page, totpSecret, statusCallback, logCallback);
            
            // Login başarısını kontrol et
            await CheckLoginSuccessAsync(page, statusCallback, logCallback);
        }
        
        private async Task FillLoginFormAsync(IPage page, string username, string password, string companyCode, Action<string, string, StatusType>? statusCallback, Action<string>? logCallback)
        {
            statusCallback?.Invoke("Form Doldurma", "Login formu dolduruluyor...", StatusType.Processing);
            logCallback?.Invoke("Login formu dolduruluyor...");
            
            // Kullanıcı adı alanı
            var usernameField = await page.QuerySelectorAsync("#UserName");
            if (usernameField != null)
            {
                await usernameField.FillAsync(username);
                logCallback?.Invoke("Kullanıcı adı girildi.");
            }
            else
            {
                logCallback?.Invoke("Kullanıcı adı alanı bulunamadı!");
            }
            
            // Firma kodu alanı
            var companyCodeField = await page.QuerySelectorAsync("#CompanyCode");
            if (companyCodeField != null)
            {
                await companyCodeField.FillAsync(companyCode);
                logCallback?.Invoke("Firma kodu girildi.");
            }
            else
            {
                logCallback?.Invoke("Firma kodu alanı bulunamadı!");
            }
            
            // Şifre alanı
            var passwordField = await page.QuerySelectorAsync("#Password");
            if (passwordField != null)
            {
                await passwordField.FillAsync(password);
                logCallback?.Invoke("Şifre girildi.");
            }
            else
            {
                logCallback?.Invoke("Şifre alanı bulunamadı!");
            }
        }
        
        private async Task ClickLoginButtonAsync(IPage page, Action<string, string, StatusType>? statusCallback, Action<string>? logCallback)
        {
            // GİRİŞ butonunu bul - daha spesifik selector
            var loginButton = await page.QuerySelectorAsync("button.btn.btn-lg.btn-success.btn-block");
            
            if (loginButton != null)
            {
                // Butona tıklamadan önce biraz bekle
                await page.WaitForTimeoutAsync(2000);
                
                // Önce butonun görünür olduğundan emin ol
                await loginButton.WaitForElementStateAsync(ElementState.Visible);
                
                // JavaScript ile tıkla - daha güvenilir
                await page.EvaluateAsync(@"
                    const button = document.querySelector('button.btn.btn-lg.btn-success.btn-block');
                    if (button) {
                        button.click();
                    }
                ");
                
                // Form submit'i bekle - daha hızlı
                await page.WaitForTimeoutAsync(2000);
            }
            else
            {
                logCallback?.Invoke("Login butonu bulunamadı! Manuel olarak giriş yapın...");
            }
        }
        
        private async Task Handle2FAWithTOTPAsync(IPage page, string totpSecret, Action<string, string, StatusType>? statusCallback, Action<string>? logCallback)
        {
            // 2FA alanını bekle (3 saniye) - daha hızlı
            try
            {
                var twoFactorField = await page.WaitForSelectorAsync("#Code, input[name='code'], input[name='2fa'], input[name='otp'], input[placeholder*='code'], input[placeholder*='2fa'], input[placeholder*='OTP'], input[placeholder*='doğrulama'], input[placeholder*='verification']", new PageWaitForSelectorOptions { Timeout = 3000 });
                
                if (twoFactorField != null)
                {
                    string twoFactorCode;
                    
                    if (!string.IsNullOrEmpty(totpSecret))
                    {
                        // TOTP kodu üret
                        twoFactorCode = GenerateTOTPCode(totpSecret);
                    }
                    else
                    {
                        // Manuel kod girişi
                        logCallback?.Invoke("2FA kodu manuel olarak girilmeli.");
                        twoFactorCode = "";
                    }
                    
                    if (!string.IsNullOrEmpty(twoFactorCode))
                    {
                        // Kodu temizle ve gir
                        await twoFactorField.FillAsync("");
                        await twoFactorField.FillAsync(twoFactorCode);
                        
                        // Biraz bekle - daha hızlı
                        await page.WaitForTimeoutAsync(500);
                        
                        // 2FA submit butonunu bul ve tıkla
                        var submitButton = await page.QuerySelectorAsync("button.btn.btn-lg.btn-success.btn-block, button[type='submit'], input[type='submit']");
                        if (submitButton != null)
                        {
                            // JavaScript ile tıkla
                            await page.EvaluateAsync(@"
                                const button = document.querySelector('button.btn.btn-lg.btn-success.btn-block, button[type=""submit""]');
                                if (button) {
                                    button.click();
                                }
                            ");
                            
                            // Submit sonrası bekle - daha hızlı
                            await page.WaitForTimeoutAsync(1000);
                        }
                        else
                        {
                            logCallback?.Invoke("2FA submit butonu bulunamadı. Manuel olarak doğrulayın...");
                        }
                    }
                }
                else
                {
                    logCallback?.Invoke("2FA alanı bulunamadı veya gerekli değil.");
                }
            }
            catch (Exception ex)
            {
                logCallback?.Invoke($"2FA kontrolü sırasında hata: {ex.Message}");
            }
        }
        
        private string GenerateTOTPCode(string secret)
        {
            try
            {
                // Base32 secret key'i decode et
                var secretBytes = Base32Encoding.ToBytes(secret);
                
                // TOTP generator oluştur
                var totp = new Totp(secretBytes);
                
                // Mevcut TOTP kodunu al
                var code = totp.ComputeTotp();
                
                return code;
            }
            catch (Exception)
            {
                return "";
            }
        }
        
        /// <summary>
        /// Login başarısını kontrol eder (statusCallback ile)
        /// </summary>
        private async Task CheckLoginSuccessAsync(IPage page, Action<string, string, StatusType>? statusCallback, Action<string>? logCallback)
        {
            try
            {
                logCallback?.Invoke("Login başarısı kontrol ediliyor...");
                
                // Sayfanın yüklenmesini bekle
                await page.WaitForTimeoutAsync(1000);
                
                // Login başarısını kontrol et - dashboard veya ana sayfa elementlerini ara
                var successIndicator = await page.QuerySelectorAsync(".dashboard, .main-content, .user-info, .logout, [href*='logout'], .navbar, .header, .sidebar");
                
                if (successIndicator != null)
                {
                    logCallback?.Invoke("✅ Login başarılı - dashboard bulundu.");
                    return;
                }
                else
                {
                    // URL'yi kontrol et
                    var currentUrl = page.Url;
                    if (!currentUrl.Contains("login") && !currentUrl.Contains("Login") && !currentUrl.Contains("Account"))
                    {
                        logCallback?.Invoke("✅ Login başarılı - URL login sayfasında değil.");
                        return;
                    }
                    else
                    {
                        // Sayfa içeriğini kontrol et
                        var pageContent = await page.ContentAsync();
                        var hasLoginForm = pageContent.Contains("UserName") || pageContent.Contains("Password") || pageContent.Contains("GİRİŞ");
                        
                        if (!hasLoginForm)
                        {
                            logCallback?.Invoke("✅ Login başarılı - login formu bulunamadı.");
                            return;
                        }
                        else
                        {
                            logCallback?.Invoke("⚠️ Login durumu belirsiz, login formu hala mevcut.");
                            return;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logCallback?.Invoke($"❌ Login kontrolü sırasında hata: {ex.Message}");
                return;
            }
        }
        
        private async Task SetPageSizeAsync(IPage page, int pageSize, Action<string, string, StatusType>? statusCallback, Action<string>? logCallback)
        {
            try
            {
                // ItemPerPage_ dropdown'ını bul
                var dropdown = await page.QuerySelectorAsync("#ItemPerPage_");

                if (dropdown != null)
                {
                    // Seçilen sayfa boyutunu ayarla
                    await dropdown.SelectOptionAsync(pageSize.ToString());

                    // Asenkron yükleme işlemini bekle
                    logCallback?.Invoke($"Sayfa boyutu {pageSize} öğeye ayarlanıyor...");

                    // Network isteklerinin tamamlanmasını bekle
                    await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

                    // Sayfa boyutuna göre bekleme süresi ayarla
                    int waitTime = pageSize switch
                    {
                        5 => 3000,   // Az öğe için kısa süre
                        15 => 4000,  // Orta öğe için orta süre
                        60 => 5000,  // Çok öğe için uzun süre
                        120 => 8000, // En çok öğe için en uzun süre
                        _ => 5000    // Varsayılan
                    };

                    await page.WaitForTimeoutAsync(waitTime);

                    logCallback?.Invoke($"Sayfa boyutu {pageSize} öğeye ayarlandı.");
                }
            }
            catch (Exception ex)
            {
                logCallback?.Invoke($"Sayfa sayısı ayarlanırken hata: {ex.Message}");
            }
        }
        
        private async Task<int> FindAndProcessUrlsAsync(IPage page, string cssClass, string statusClass, AppConfig config, Action<int, int>? progressCallback, Action<string, string, StatusType>? statusCallback, Action<string>? logCallback, CancellationToken cancellationToken, Action<int>? foundCallback = null, Action<int>? downloadedCallback = null, Action<decimal>? totalAmountCallback = null)
        {
            statusCallback?.Invoke("URL Arama", "Onaylandı durumundaki URL'ler aranıyor...", StatusType.Processing);
            logCallback?.Invoke("Onaylandı durumundaki URL'ler aranıyor...");
            
            // Daha önce indirilen dosyaların ID'lerini al
            var previouslyDownloadedIds = GetPreviouslyDownloadedItemIds(config);
            logCallback?.Invoke($"Daha önce indirilen {previouslyDownloadedIds.Count} dosya tespit edildi.");
            
            // Sayfadaki tüm satırları bul (tablo satırları)
            var rows = await page.QuerySelectorAllAsync("tr, .row, .item-row");
            
            var scrapedItems = new List<ScrapedItem>();
            var onaylandiCount = 0;
            var bekleyenCount = 0;
            var reddedildiCount = 0;
            var digerCount = 0;
            var previouslyDownloadedCount = 0;
            var creatorNames = new HashSet<string>(); // Ödeme emrini oluşturan kişiler
            
            var rowCount = 0;
            foreach (var row in rows)
            {
                // Her satırda cancellation kontrolü
                cancellationToken.ThrowIfCancellationRequested();
                rowCount++;
                
                // Her 20 satırda bir ek cancellation kontrolü ve log
                if (rowCount % 20 == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    logCallback?.Invoke($"İşlenen satır: {rowCount}/{rows.Count}");
                }
                
                try
                {
                    // Sadece tablo satırlarını kontrol et (td içeren)
                    var hasTd = await row.QuerySelectorAsync("td");
                    if (hasTd == null) continue;
                    
                    // Satırdaki durum bilgisini kontrol et - tüm sütunları kontrol et
                    var allTds = await row.QuerySelectorAllAsync("td");
                    string status = "";
                    
                    // Tüm sütunlarda durum ara
                    foreach (var td in allTds)
                    {
                        var tdText = await td.InnerTextAsync();
                        if (tdText.ToLower().Contains("onaylandı") || 
                            tdText.ToLower().Contains("bekleyen") || 
                            tdText.ToLower().Contains("reddedildi") ||
                            tdText.ToLower().Contains("işlemde"))
                        {
                            status = tdText;
                            break;
                        }
                    }
                    
                    if (!string.IsNullOrEmpty(status))
                    {
                        // Durum sayacını güncelle
                        if (status.ToLower().Contains("onaylandı"))
                        {
                            onaylandiCount++;
                        }
                        else if (status.ToLower().Contains("bekleyen"))
                        {
                            bekleyenCount++;
                        }
                        else if (status.ToLower().Contains("reddedildi"))
                        {
                            reddedildiCount++;
                        }
                        else
                        {
                            digerCount++;
                        }
                        
                        // "Onaylandı" durumunu kontrol et
                        if (status.ToLower().Contains("onaylandı"))
                        {
                            // Ödeme emrini oluşturan kişiyi bul - tüm sütunları kontrol et
                            var allTdsForCreator = await row.QuerySelectorAllAsync("td");
                            string creatorName = "";
                            
                            // Kişi ismi 7. sütunda (index 6)
                            if (allTdsForCreator.Count > 6)
                            {
                                var creatorTdText = await allTdsForCreator[6].InnerTextAsync();
                                if (!string.IsNullOrEmpty(creatorTdText))
                                {
                                    creatorName = creatorTdText.Trim();
                                }
                            }
                            
                            if (!string.IsNullOrEmpty(creatorName))
                            {
                                creatorNames.Add(creatorName.Trim());
                            }
                            
                            // Fatura dönemi bilgisini al (3. sütun)
                            var periodElement = await row.QuerySelectorAsync("td:nth-child(3) a");
                            var periodText = periodElement != null ? await periodElement.InnerTextAsync() : "";
                            
                            // Tutar bilgisini al (5. sütun)
                            var amountElement = await row.QuerySelectorAsync("td:nth-child(5)");
                            var amountText = amountElement != null ? await amountElement.InnerTextAsync() : "";
                            
                            // Bu satırdaki "Talimatı indir" linklerini bul - SADECE İLK LINKİ AL
                            var downloadLinks = await row.QuerySelectorAllAsync("a[href*='DownloadFile']");
                            
                            if (downloadLinks.Count > 0)
                            {
                                // Sadece ilk linki al
                                var downloadLink = downloadLinks[0];
                                var href = await downloadLink.GetAttributeAsync("href");
                                if (!string.IsNullOrEmpty(href))
                                {
                                    // Tam URL'ye çevir
                                    var fullUrl = MakeAbsoluteUrl(href);
                                    
                                    // URL'den item ID'sini çıkar
                                    var itemId = ExtractItemIdFromUrl(href);
                                    
                                    // Daha önce indirilmiş mi kontrol et
                                    if (!string.IsNullOrEmpty(itemId) && previouslyDownloadedIds.Contains(itemId))
                                    {
                                        previouslyDownloadedCount++;
                                        logCallback?.Invoke($"Daha once indirilmis dosya atlandi: ID {itemId} ({periodText} - {amountText})");
                                        continue; // Bu dosyayı atla
                                    }
                                    
                                    // Link metnini al
                                    var linkText = await downloadLink.InnerTextAsync();
                                    
                                    // Element metnini birleştir
                                    var elementText = $"Dönem: {periodText} | Tutar: {amountText} | Durum: {status}";
                                    
                                    scrapedItems.Add(new ScrapedItem
                                    {
                                        OriginalUrl = href,
                                        FullUrl = fullUrl,
                                        Status = status,
                                        ElementText = elementText,
                                        CreatorName = creatorName.Trim(),
                                        ItemId = itemId
                                    });
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    logCallback?.Invoke($"Satır işlenirken hata: {ex.Message}");
                }
            }
            
            // Durum özetini göster
            logCallback?.Invoke("\n=== DURUM ÖZETİ ===");
            logCallback?.Invoke($"Onaylandı: {onaylandiCount}");
            logCallback?.Invoke($"Bekleyen: {bekleyenCount}");
            logCallback?.Invoke($"Reddedildi: {reddedildiCount}");
            logCallback?.Invoke($"Diğer: {digerCount}");
            logCallback?.Invoke($"Daha Önce İndirilen: {previouslyDownloadedCount}");
            logCallback?.Invoke("==================\n");
            
            // Onaylandı sayısını raporla
            logCallback?.Invoke($"Toplam {onaylandiCount} adet onaylandı dosya bulundu.");
            logCallback?.Invoke($"{previouslyDownloadedCount} adet daha önce indirilmiş dosya atlandı.");
            
            // Bulunan dosya sayısını güncelle
            foundCallback?.Invoke(onaylandiCount);
            
            if (!scrapedItems.Any())
            {
                logCallback?.Invoke("Onaylandı durumunda indirme linki bulunamadı!");
                return onaylandiCount;
            }
            
            logCallback?.Invoke($"{scrapedItems.Count} adet onaylandı durumunda indirme linki bulundu.");
            
            // Ödeme emrini oluşturan kişileri göster
            logCallback?.Invoke("\n" + new string('═', 50));
            logCallback?.Invoke("📋 ÖDEME EMRİNİ OLUŞTURAN KİŞİLER");
            logCallback?.Invoke(new string('═', 50));
            var creatorList = creatorNames.ToList();
            for (int i = 0; i < creatorList.Count; i++)
            {
                logCallback?.Invoke($"{i + 1}. {creatorList[i]}");
            }
            logCallback?.Invoke(new string('═', 50) + "\n");
            
            // Dıt sesi ver - kullanıcıya cevap vermesi gerektiğini bildir
            PlayNotificationSound();
            
            // Kullanıcıdan seçim al
            List<string> selectedCreators = creatorList;
            try
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var win = new SelectCreatorsWindow(creatorList) { Owner = Application.Current.MainWindow };
                    if (win.ShowDialog() == true && win.SelectedCreators.Any())
                    {
                        selectedCreators = win.SelectedCreators;
                    }
                });
            }
            catch { /* Headless durumlarda sessizce geç */ }
            
            logCallback?.Invoke($"Seçilen kişiler: {string.Join(", ", selectedCreators)}");
            
            // Seçilen kişilerin dosyalarını filtrele
            var filteredItems = scrapedItems.Where(item => selectedCreators.Contains(item.CreatorName)).ToList();
            logCallback?.Invoke($"Seçilen kişilerin toplam {filteredItems.Count} dosyası indirilecek.");
            
            // URL'leri indir
                            await DownloadUrlsAsync(page, filteredItems, onaylandiCount, config, progressCallback, statusCallback, logCallback, cancellationToken, foundCallback, downloadedCallback, totalAmountCallback, previouslyDownloadedCount);
            
            logCallback?.Invoke("İşlem tamamlandı!");
            
            return onaylandiCount;
        }
        
        private string MakeAbsoluteUrl(string url)
        {
            if (url.StartsWith("http://") || url.StartsWith("https://"))
            {
                return url;
            }
            
            if (url.StartsWith("//"))
            {
                return "https:" + url;
            }
            
            if (url.StartsWith("/"))
            {
                // Base URL'yi hardcoded olarak ekle
                return "https://www.pinhuman.net" + url;
            }
            
            // Relative URL ise
            return "https://www.pinhuman.net/" + url;
        }
        
        private async Task DownloadUrlsAsync(IPage page, List<ScrapedItem> items, int expectedOnaylandiCount, AppConfig config, Action<int, int>? progressCallback, Action<string, string, StatusType>? statusCallback, Action<string>? logCallback, CancellationToken cancellationToken, Action<int>? foundCallback = null, Action<int>? downloadedCallback = null, Action<decimal>? totalAmountCallback = null, int previouslyDownloaded = 0)
        {
            statusCallback?.Invoke("İndirme", "URL'ler indiriliyor...", StatusType.Processing);
            logCallback?.Invoke("URL'ler indiriliyor...");
            
            // Ayarlardan seçilen çıktı klasörünü logla
            var outputFolder = config.Download.OutputFolder;
            
            // Eğer config'den gelen değer boşsa, varsayılan değeri kullan
            if (string.IsNullOrEmpty(outputFolder))
            {
                outputFolder = "cikti";
                logCallback?.Invoke($"⚠️ Config'den OutputFolder değeri boş, varsayılan 'cikti' klasörü kullanılıyor.");
            }
            else
            {
                logCallback?.Invoke($"✅ Config'den OutputFolder değeri alındı: {outputFolder}");
            }
            
            logCallback?.Invoke($"📁 Çıktı klasörü: {outputFolder}");
            
            // Duplicate URL'leri filtrele - her URL'den sadece bir kez indir
            var uniqueItems = items
                .Where(i => i.Status.ToLower().Contains("onaylandı") || string.IsNullOrEmpty(i.Status))
                .GroupBy(i => i.FullUrl)
                .Select(g => g.First())
                .ToList();
            
            var downloadTasks = new List<Task>();
            var semaphore = new SemaphoreSlim(3, 3); // Aynı anda maksimum 3 indirme (daha hızlı)
            var completedCount = 0;
            var totalCount = uniqueItems.Count;
            
            // Debug için toplam sayıyı logla
            logCallback?.Invoke($"Toplam indirilecek benzersiz dosya sayisi: {totalCount}");
            if (uniqueItems.Count != items.Count)
            {
                logCallback?.Invoke($"{items.Count - uniqueItems.Count} adet duplicate URL filtrelendi");
            }
            
            var downloadAnalysis = new List<string>();
            var startTime = DateTime.Now;
            var timeoutCount = 0;
            var networkErrorCount = 0;
            var serverErrorCount = 0;
            var successCount = 0;
            var zipCount = 0;
            var normalFileCount = 0;
            
            // Thread-safe sayaçlar için lock objesi
            var lockObj = new object();
            
            foreach (var item in uniqueItems)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                downloadTasks.Add(DownloadSingleUrlWithSemaphoreAsync(page, item, semaphore, config, (result) => {
                    lock (lockObj)
                    {
                        completedCount++;
                        
                        // Progress callback
                        progressCallback?.Invoke(completedCount, totalCount);
                        
                        // Hata analizi
                        if (result.ErrorMessage != null)
                        {
                            var errorType = AnalyzeDownloadError(result.ErrorMessage);
                            switch (errorType)
                            {
                                case "TIMEOUT":
                                    timeoutCount++;
                                    break;
                                case "NETWORK_ERROR":
                                    networkErrorCount++;
                                    break;
                                case "SERVER_ERROR":
                                    serverErrorCount++;
                                    break;
                            }
                            downloadAnalysis.Add($"❌ {result.FullUrl}: {errorType} - {result.ErrorMessage}");
                        }
                        else
                        {
                            successCount++;
                            
                            // İndirilen dosya sayısını güncelle
                            downloadedCallback?.Invoke(successCount);
                            
                            // Dosya türü sayacını güncelle - ZIP dosyası çıkarıldıysa ZIP sayacını artır
                            if (result.DownloadedFilePath != null)
                            {
                                var extension = Path.GetExtension(result.DownloadedFilePath).ToLowerInvariant();
                                if (extension == ".zip")
                                {
                                    zipCount++;
                                }
                                else
                                {
                                    normalFileCount++;
                                }
                            }
                            
                            downloadAnalysis.Add($"✅ {result.FullUrl}: Başarılı");
                        }
                    }
                }, logCallback, cancellationToken));
            }
            
            // Tüm indirmelerin tamamlanmasını bekle - cancellation token ile
            try
            {
                await Task.WhenAll(downloadTasks);
            }
            catch (OperationCanceledException)
            {
                logCallback?.Invoke("Indirme islemi kullanici tarafindan iptal edildi.");
                throw;
            }
            
            var totalTime = DateTime.Now - startTime;
            logCallback?.Invoke($"\nINDIRME TAMAMLANDI");
            logCallback?.Invoke($"Toplam sure: {totalTime.TotalSeconds:F1} saniye");
            logCallback?.Invoke($"Islenen toplam dosya: {uniqueItems.Count}");
            logCallback?.Invoke($"Basariyla indirilen: {successCount}");
            if (timeoutCount > 0) logCallback?.Invoke($"Zaman asimi: {timeoutCount}");
            if (networkErrorCount > 0) logCallback?.Invoke($"Ag hatasi: {networkErrorCount}");
            if (serverErrorCount > 0) logCallback?.Invoke($"Sunucu hatasi: {serverErrorCount}");
            logCallback?.Invoke($"Dosya turleri - XLS: {normalFileCount} | ZIP: {zipCount}");
            
            // Durum güncelle
            if (successCount == totalCount)
            {
                statusCallback?.Invoke("Tamamlandı", "Tüm dosyalar başarıyla indirildi.", StatusType.Success);
            }
            else if (successCount > 0)
            {
                statusCallback?.Invoke("Kısmen Tamamlandı", $"{totalCount - successCount} dosya indirilemedi.", StatusType.Warning);
            }
            else
            {
                statusCallback?.Invoke("Başarısız", "Hiçbir dosya indirilemedi.", StatusType.Error);
            }
            
            // Toplam tutarı hesapla ve göster
            var totalAmount = CalculateTotalAmountFromDownloadedFiles();
            logCallback?.Invoke($"🔍 Debug - Hesaplanan toplam tutar: {totalAmount:N2} TL");
            if (totalAmount > 0)
            {
                logCallback?.Invoke($"💰 Toplam Ödeme Emri Tutarı: {totalAmount:N2} TL");
            }
            else
            {
                logCallback?.Invoke($"⚠️ Uyarı - Toplam tutar 0 olarak hesaplandı!");
            }
            
            // Toplam tutarı güncelle
            totalAmountCallback?.Invoke(totalAmount);
            
            // İşlem tamamlandığında MainWindow'a gerçek verilerle geçmiş kaydı ekle
            var mainWindow = System.Windows.Application.Current.MainWindow as MainWindow;
            if (mainWindow != null && successCount > 0)
            {
                // İndirilen dosyalardan dönem adını al
                var periodName = items.FirstOrDefault()?.ElementText?.Split('\n').FirstOrDefault() ?? "";
                if (string.IsNullOrEmpty(periodName))
                {
                    periodName = items.FirstOrDefault()?.CreatorName ?? "";
                }
                
                // Process ID oluştur
                var processId = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                
                // Geçmişe kaydet
                mainWindow.Dispatcher.Invoke(() =>
                {
                    mainWindow.AddHistoryRecord("Taslak Onaylama", periodName, processId, totalAmount, "Başarılı");
                });
            }
            
            // Mail bildirimi gönder
            logCallback?.Invoke("📧 Mail gönderimi başlatılıyor...");
            await SendCompletionNotificationAsync(successCount, totalCount, config, logCallback, items);
            logCallback?.Invoke("📧 Mail gönderimi tamamlandı.");
            
            // İşlem özeti Windows alert'i göster (mail gönderiminden sonra)
            logCallback?.Invoke("🔔 Windows alert gösteriliyor...");
            var completionTime = DateTime.Now - startTime;
            ShowCompletionAlert(successCount, totalCount, totalAmount, config, logCallback, previouslyDownloaded, completionTime);
            logCallback?.Invoke("🔔 Windows alert gösterildi.");
            
            // İndirme analiz raporu
            GenerateDownloadAnalysisReport(successCount, timeoutCount, networkErrorCount, serverErrorCount, totalCount, completionTime, downloadAnalysis, expectedOnaylandiCount, zipCount, normalFileCount, logCallback);
            
                            // Başarısız indirmeler için otomatik tekrar deneme
                var failedCount = totalCount - successCount;
                if (failedCount > 0)
                {
                    logCallback?.Invoke("");
                    logCallback?.Invoke("⚠️ Başarısız indirmeler tespit edildi!");
                    logCallback?.Invoke("🔄 Başarısız dosyalar otomatik olarak tekrar deneniyor...");
                    
                    // Başarısız dosyaları filtrele - hem hata mesajı hem de dosya varlığını kontrol et
                    var failedItems = _scrapedItems.Where(item => 
                        !string.IsNullOrEmpty(item.ErrorMessage) || 
                        !IsFileSuccessfullyDownloaded(item)).ToList();
                    
                    if (failedItems.Any())
                    {
                        // Başarısız dosyaları tekrar indir
                        await RetryFailedDownloadsAsync(page, failedItems, config, progressCallback, statusCallback, logCallback, cancellationToken, foundCallback, downloadedCallback, totalAmountCallback);
                    }
                }
        }
        
        // Diğer metodlar buraya eklenecek...
        private async Task DownloadSingleUrlWithSemaphoreAsync(IPage page, ScrapedItem item, SemaphoreSlim semaphore, AppConfig config, Action<ScrapedItem> onComplete, Action<string>? logCallback = null, CancellationToken cancellationToken = default)
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                await DownloadSingleUrlAsync(page, item, config, logCallback, cancellationToken);
                onComplete?.Invoke(item);
            }
            finally
            {
                semaphore.Release();
            }
        }
        
        private async Task DownloadSingleUrlAsync(IPage page, ScrapedItem item, AppConfig config, Action<string>? logCallback = null, CancellationToken cancellationToken = default)
        {
            try
            {
                // CancellationToken kontrolü
                cancellationToken.ThrowIfCancellationRequested();
                
                // İndirme başladı - sessizce devam et
                logCallback?.Invoke($"İndiriliyor: {item.FullUrl}");
                
                // Playwright'dan cookie'leri al
                var cookies = await page.Context.CookiesAsync();
                
                // HttpClient oluştur ve cookie'leri ekle
                using var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromMinutes(30); // Büyük dosyalar için daha uzun timeout
                
                // Cookie'leri header'a ekle
                var cookieHeader = string.Join("; ", cookies.Select(c => $"{c.Name}={c.Value}"));
                if (!string.IsNullOrEmpty(cookieHeader))
                {
                    httpClient.DefaultRequestHeaders.Add("Cookie", cookieHeader);
                }
                
                // User-Agent ekle
                httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
                
                // İndirme işlemini başlat ve progress takibi yap
                using var response = await httpClient.GetAsync(item.FullUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();
                
                // Content-Length kontrolü (sessiz)
                var contentLength = response.Content.Headers.ContentLength;
                
                // Stream olarak oku (bellek kullanımını azalt)
                using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                var content = new List<byte>();
                var buffer = new byte[8192];
                var totalBytesRead = 0L;
                
                int bytesRead;
                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
                {
                    // Her okuma işleminde cancellation kontrolü
                    cancellationToken.ThrowIfCancellationRequested();
                    content.AddRange(buffer.Take(bytesRead));
                    totalBytesRead += bytesRead;
                    
                    // Her 512KB'da bir cancellation kontrolü yap (daha sık)
                    if (totalBytesRead % (512 * 1024) == 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                    
                    // Her 2MB'da bir log mesajı
                    if (totalBytesRead % (2 * 1024 * 1024) == 0)
                    {
                        logCallback?.Invoke($"İndiriliyor: {totalBytesRead / (1024 * 1024)}MB");
                    }
                }
                
                var contentArray = content.ToArray();
                
                // Content-Disposition header'ından dosya adını al
                string? fileName = null;
                if (response.Content.Headers.ContentDisposition != null)
                {
                    fileName = response.Content.Headers.ContentDisposition.FileName;
                    if (!string.IsNullOrEmpty(fileName))
                    {
                        fileName = fileName.Trim('"', '\'');
                    }
                }
                
                // Eğer Content-Disposition'dan alınamadıysa URL'den al
                if (string.IsNullOrEmpty(fileName))
                {
                    fileName = GetFileNameFromUrl(item.FullUrl);
                }
                
                // Dosya uzantısını kontrol et
                var extension = Path.GetExtension(fileName ?? "").ToLowerInvariant();
                
                // Fatura dönem adını çıkar
                var periodName = ExtractPeriodName(item);
                
                // Sicil adını çıkar (element metninden)
                var sicilName = ExtractSicilName(item);
                
                // Tarih bazlı üst klasör oluştur
                var today = DateTime.Now;
                var dateFolder = today.ToString("dd.MM.yyyy");
                
                // Ödeme emrini oluşturan kişi adını temizle - DOSYA YOLU UYUMLU HALE GETİR
                var cleanCreatorName = CleanFileName(item.CreatorName);
                
                // Çıktı klasörünü belirle - config'den gelen klasörü kullan, yoksa varsayılan
                var baseOutputPath = config.Download.OutputFolder;
                
                // Eğer config'den gelen değer boşsa, varsayılan değeri kullan
                if (string.IsNullOrEmpty(baseOutputPath))
                {
                    // Comodo gibi antivirüs yazılımlarının vtroot sanal disk sorununu önlemek için
                    // Önce Desktop'ı dene
                    var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                    baseOutputPath = Path.Combine(desktopPath, "PinhumanSuperApp", "Cikti");

                    // Desktop erişilebilir değilse Documents'ı dene
                    if (!IsPathAccessible(baseOutputPath))
                    {
                        var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                        baseOutputPath = Path.Combine(documentsPath, "PinhumanSuperApp", "Cikti");
                        logCallback?.Invoke($"ℹ️ Desktop erişilebilir değil, Documents kullanılıyor: {baseOutputPath}");
                    }
                    else
                    {
                        logCallback?.Invoke($"✅ Comodo uyumlu Desktop klasörü kullanılıyor: {baseOutputPath}");
                    }
                }
                else
                {
                    // Kullanıcının belirlediği klasörü kullan
                    // Eğer mutlak yol değilse, uygulama dizinine göre relatif yol olarak kullan
                    if (!Path.IsPathRooted(baseOutputPath))
                    {
                        baseOutputPath = Path.Combine(Directory.GetCurrentDirectory(), baseOutputPath);
                    }
                    logCallback?.Invoke($"✅ Kullanıcı tarafından belirlenen çıktı klasörü kullanılıyor: {baseOutputPath}");
                }
                

                
                // Cikti klasörünü oluştur (Windows/Mac uyumlu) - Kişi/Dönem/Sicil bazlı
                var ciktiPath = Path.Combine(baseOutputPath, dateFolder, cleanCreatorName, CleanFileName(periodName));
                
                // Aynı dosya, tutar ve ödeme emri kontrolü
                if (IsDuplicateFile(ciktiPath, item))
                {
                    logCallback?.Invoke($"⏭️ Aynı dosya zaten mevcut, geçiliyor: {periodName}");
                    return;
                }
                
                // Klasörü oluştur - hata kontrolü ile
                try
                {
                    Directory.CreateDirectory(ciktiPath);
                    
                    // Dönem ID'si için gizli txt dosyası oluştur - URL'den çıkarılan UUID'yi kullan
                    var periodId = item.ItemId; // URL'den çıkarılan UUID'yi dönem ID'si olarak kullan
                    if (!string.IsNullOrEmpty(periodId))
                    {
                        var periodIdFilePath = Path.Combine(ciktiPath, ".period_id.txt");
                        try
                        {
                            File.WriteAllText(periodIdFilePath, periodId);
                            logCallback?.Invoke($"📝 Dönem ID'si kaydedildi: {periodId}");
                        }
                        catch (Exception ex)
                        {
                            logCallback?.Invoke($"⚠️ Dönem ID'si kaydedilemedi: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    logCallback?.Invoke($"Klasör oluşturma hatası: {ex.Message}");
                    // Alternatif klasör adı dene
                    ciktiPath = Path.Combine(baseOutputPath, dateFolder, "Dosyalar", CleanFileName(periodName));
                    Directory.CreateDirectory(ciktiPath);
                }
                
                // Dosya adını oluştur (fatura dönemi + sicil adı)
                var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                var finalFileName = "";
                
                // Fatura dönemi adını ekle
                if (!string.IsNullOrEmpty(periodName))
                {
                    finalFileName = CleanFileName(periodName);
                }
                
                // Sicil adını ekle (eğer period name içinde yoksa ve boş değilse)
                if (!string.IsNullOrEmpty(sicilName))
                {
                    // Sicil adını temizle
                    var cleanSicilName = CleanFileName(sicilName);
                    
                    // Eğer temizlenmiş sicil adı boş değilse ekle
                    if (!string.IsNullOrEmpty(cleanSicilName))
                    {
                        if (!string.IsNullOrEmpty(finalFileName))
                        {
                            finalFileName = $"{finalFileName}_{cleanSicilName}";
                        }
                        else
                        {
                            finalFileName = cleanSicilName;
                        }
                    }
                }
                
                // Eğer hala boşsa varsayılan isim kullan
                if (string.IsNullOrEmpty(finalFileName))
                {
                    finalFileName = "talimat";
                }
                
                // Dosya uzantısını belirle
                if (string.IsNullOrEmpty(extension) || extension == ".zip")
                {
                    extension = ".xls";
                }
                
                fileName = $"{finalFileName}{extension}";
                
                // Dosya yolunu oluştur
                var filePath = Path.Combine(ciktiPath, fileName);
                
                // Aynı isimde dosya varsa numara ekle
                var counter = 1;
                var originalFilePath = filePath;
                while (File.Exists(filePath))
                {
                    var nameWithoutExt2 = Path.GetFileNameWithoutExtension(originalFilePath);
                    var ext2 = Path.GetExtension(originalFilePath);
                    filePath = Path.Combine(ciktiPath, $"{nameWithoutExt2}_{counter}{ext2}");
                    counter++;
                }
                
                // Dosyayı kaydet
                await File.WriteAllBytesAsync(filePath, contentArray);
                
                // Dosya bütünlüğünü kontrol et
                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length == 0)
                {
                    throw new Exception("İndirilen dosya boş!");
                }
                
                logCallback?.Invoke($"✅ Başarılı: {fileName} ({fileInfo.Length} bytes)");
                
                // ZIP dosyası ise aç ve içindeki dosyaları çıkar
                if (extension == ".zip" || IsZipFile(contentArray))
                {
                    // Fatura dönemi adını al
                    var extractedPeriodName = ExtractPeriodName(item);
                    var extractedFiles = await ExtractZipFile(filePath, ciktiPath, sicilName, extractedPeriodName);
                    
                    // Eğer ZIP'den dosya çıkarıldıysa ZIP'i sil
                    if (extractedFiles.Any())
                    {
                        File.Delete(filePath);
                        
                        // Çıkarılan dosyalardan birini ana dosya olarak kullan
                        var firstExtractedFile = extractedFiles.First();
                        filePath = firstExtractedFile;
                        
                        // ZIP'den çıkarılan tüm Excel dosyaları için Word dosyası oluştur
                        foreach (var extractedFile in extractedFiles)
                        {
                            try
                            {
                                var extractedFileName = Path.GetFileName(extractedFile);
                                var wordTemplatePath = Path.Combine(Directory.GetCurrentDirectory(), "İŞBANKASI İKB MAAŞ TALİMAT.docx");
                                var outputFileName = Path.GetFileNameWithoutExtension(extractedFileName) + ".docx";
                                
                                // Excel dosyasının bulunduğu klasöre Word dosyasını kaydet
                                var excelDir = Path.GetDirectoryName(extractedFile);
                                if (!string.IsNullOrEmpty(excelDir))
                                {
                                    var outputPath = Path.Combine(excelDir, outputFileName);
                                    ProcessWordTemplateWithExcelData(extractedFile, wordTemplatePath, outputPath);
                                }
                            }
                            catch (Exception ex)
                            {
                                logCallback?.Invoke($"ZIP'den çıkarılan dosya için Word şablonu işleme hatası: {ex.Message}");
                            }
                        }
                    }
                }
                else if (extension == ".xlsx")
                {
                    // XLSX'den XLS'e dönüştür (sadece uzantı değişikliği)
                    var xlsFilePath = Path.ChangeExtension(filePath, ".xls");
                    if (File.Exists(filePath))
                    {
                        File.Move(filePath, xlsFilePath);
                        filePath = xlsFilePath;
                        fileName = Path.GetFileName(xlsFilePath);
                    }
                }
                
                item.DownloadedFilePath = filePath;
                item.DownloadSize = new FileInfo(filePath).Length;
                item.DownloadDate = DateTime.Now;
                
                // Başarıyla indirilen dosyayı JSON dosyasına ekle
                var itemId = ExtractItemIdFromUrl(item.FullUrl);
                if (!string.IsNullOrEmpty(itemId))
                {
                    // Fatura dönemi adını al (indirme için)
                    var downloadPeriodName = ExtractPeriodName(item);
                    AddToPreviouslyDownloadedIds(itemId, downloadPeriodName);
                }
                
                // Excel dosyası ise Word şablonunu işle
                if (extension == ".xls" || extension == ".xlsx")
                {
                    try
                    {
                        // Windows ve Mac uyumlu dosya yolu
                        var wordTemplatePath = Path.Combine(Directory.GetCurrentDirectory(), "İŞBANKASI İKB MAAŞ TALİMAT.docx");
                        var outputFileName = Path.GetFileNameWithoutExtension(fileName) + ".docx";
                        
                        // Excel dosyasının bulunduğu klasöre Word dosyasını kaydet
                        var excelDir = Path.GetDirectoryName(filePath);
                        if (!string.IsNullOrEmpty(excelDir))
                        {
                            var outputPath = Path.Combine(excelDir, outputFileName);
                            ProcessWordTemplateWithExcelData(filePath, wordTemplatePath, outputPath);
                        }
                    }
                    catch (Exception ex)
                    {
                        logCallback?.Invoke($"Word şablonu işleme hatası: {ex.Message}");
                    }
                }
                
                lock (_scrapedItems)
                {
                    _scrapedItems.Add(item);
                }
                
                // Başarılı indirme sonrası bekleme (rate limiting ve dosya işleme için) - DAHA HIZLI
                await Task.Delay(500);
            }
            catch (Exception ex)
            {
                logCallback?.Invoke($"İndirme hatası ({item.FullUrl}): {ex.Message}");
                item.ErrorMessage = ex.Message;
                
                // Hata durumunda da item'ı listeye ekle
                lock (_scrapedItems)
                {
                    _scrapedItems.Add(item);
                }
                
                // Hata durumunda biraz bekle (rate limiting için) - DAHA HIZLI
                await Task.Delay(1000);
            }
        }
        
        private async Task CheckOtherPagesIfNeeded(IPage page, string cssClass, string statusClass, int onaylandiCount, Action<string, string, StatusType>? statusCallback, Action<string>? logCallback, CancellationToken cancellationToken)
        {
            // Sayfa kontrolü simülasyonu
            logCallback?.Invoke("Diğer sayfalar kontrol ediliyor...");
            await Task.Delay(1000, cancellationToken);
        }
        
        private async Task RetryFailedDownloadsAsync(IPage page, List<ScrapedItem> failedItems, AppConfig config, Action<int, int>? progressCallback, Action<string, string, StatusType>? statusCallback, Action<string>? logCallback, CancellationToken cancellationToken, Action<int>? foundCallback = null, Action<int>? downloadedCallback = null, Action<decimal>? totalAmountCallback = null)
        {
            if (!failedItems.Any())
                return;
                
            logCallback?.Invoke($"🔄 {failedItems.Count} başarısız dosya tekrar deneniyor...");
            
            // Başarısız dosyaları temizle ve tekrar dene
            var retryItems = new List<ScrapedItem>();
            
            foreach (var failedItem in failedItems)
            {
                // Hata mesajını temizle
                failedItem.ErrorMessage = null;
                failedItem.DownloadedFilePath = null;
                failedItem.DownloadSize = 0;
                failedItem.DownloadDate = null;
                
                retryItems.Add(failedItem);
            }
            
            // Tekrar deneme için semaphore
            var semaphore = new SemaphoreSlim(2, 2); // Tekrar denemelerde daha dikkatli
            var completedCount = 0;
            var totalCount = retryItems.Count;
            var successCount = 0;
            
            var retryTasks = new List<Task>();
            
            foreach (var item in retryItems)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                retryTasks.Add(DownloadSingleUrlWithSemaphoreAsync(page, item, semaphore, config, (result) => {
                    completedCount++;
                    progressCallback?.Invoke(completedCount, totalCount);
                    
                    if (string.IsNullOrEmpty(result.ErrorMessage))
                    {
                        successCount++;
                        logCallback?.Invoke($"✅ Tekrar deneme başarılı: {Path.GetFileName(result.DownloadedFilePath ?? "")}");
                    }
                    else
                    {
                        logCallback?.Invoke($"❌ Tekrar deneme başarısız: {result.ErrorMessage}");
                    }
                }, logCallback, cancellationToken));
            }
            
            // Tüm tekrar denemelerin tamamlanmasını bekle
            await Task.WhenAll(retryTasks);
            
            logCallback?.Invoke($"🔄 Tekrar deneme tamamlandı: {successCount}/{totalCount} başarılı");
            
            if (successCount > 0)
            {
                statusCallback?.Invoke("Tekrar Deneme", $"{successCount} dosya tekrar deneme ile indirildi.", StatusType.Success);
            }
            else
            {
                statusCallback?.Invoke("Tekrar Deneme", "Tekrar denemeler başarısız oldu.", StatusType.Error);
            }
        }
        
        private string AnalyzeDownloadError(string errorMessage)
        {
            return "UNKNOWN_ERROR";
        }
        
        private decimal CalculateTotalAmountFromDownloadedFiles()
        {
            decimal totalAmount = 0;
            
            try
            {
                // Debug için scraped items sayısını logla
                System.Diagnostics.Debug.WriteLine($"CalculateTotalAmountFromDownloadedFiles - ScrapedItems count: {_scrapedItems?.Count ?? 0}");
                
                // İndirilen dosyalardan toplam tutarı hesapla
                foreach (var item in _scrapedItems)
                {
                    if (!string.IsNullOrEmpty(item.DownloadedFilePath) && 
                        File.Exists(item.DownloadedFilePath))
                    {
                        var extension = Path.GetExtension(item.DownloadedFilePath).ToLowerInvariant();
                        
                        // Excel dosyalarından tutar çıkar
                        if (extension == ".xls" || extension == ".xlsx")
                        {
                            var amount = ExtractTotalAmountFromExcel(item.DownloadedFilePath);
                            System.Diagnostics.Debug.WriteLine($"CalculateTotalAmountFromDownloadedFiles - File: {item.DownloadedFilePath}, Amount: {amount}");
                            totalAmount += amount;
                        }
                    }
                }
                
                System.Diagnostics.Debug.WriteLine($"CalculateTotalAmountFromDownloadedFiles - Final total: {totalAmount}");
                return totalAmount;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CalculateTotalAmountFromDownloadedFiles - Error: {ex.Message}");
                return 0;
            }
        }
        
        private void GenerateDownloadAnalysisReport(int successCount, int timeoutCount, int networkErrorCount, int serverErrorCount, int totalCount, TimeSpan totalTime, List<string> downloadAnalysis, int expectedOnaylandiCount, int zipCount, int normalFileCount, Action<string>? logCallback)
        {
            logCallback?.Invoke("İndirme analiz raporu oluşturuldu.");
        }
        
        private bool IsFileSuccessfullyDownloaded(ScrapedItem item)
        {
            try
            {
                // Eğer indirilen dosya yolu yoksa başarısız
                if (string.IsNullOrEmpty(item.DownloadedFilePath))
                    return false;
                
                // Dosya fiziksel olarak var mı kontrol et
                if (!File.Exists(item.DownloadedFilePath))
                    return false;
                
                // Dosya boyutu 0 ise başarısız
                var fileInfo = new FileInfo(item.DownloadedFilePath);
                if (fileInfo.Length == 0)
                    return false;
                
                // Dosya uzantısını kontrol et
                var extension = Path.GetExtension(item.DownloadedFilePath).ToLowerInvariant();
                
                // Excel dosyası ise Word dosyasının da var olup olmadığını kontrol et
                if (extension == ".xls" || extension == ".xlsx")
                {
                    var wordFilePath = Path.ChangeExtension(item.DownloadedFilePath, ".docx");
                    if (!File.Exists(wordFilePath))
                    {
                        // Word dosyası yoksa başarısız say
                        return false;
                    }
                    
                    // Word dosyasının da boyutu 0 olmamalı
                    var wordFileInfo = new FileInfo(wordFilePath);
                    if (wordFileInfo.Length == 0)
                        return false;
                }
                
                return true;
            }
            catch
            {
                // Herhangi bir hata durumunda başarısız say
                return false;
            }
        }
        
        private string GetFileNameFromUrl(string url)
        {
            try
            {
                var uri = new Uri(url);
                var fileName = Path.GetFileName(uri.LocalPath);
                
                if (string.IsNullOrEmpty(fileName))
                {
                    fileName = "talimat";
                }
                
                // UUID'yi kaldır ve anlamlı isim oluştur
                if (fileName.Contains("-") && fileName.Length > 20)
                {
                    // UUID formatındaki dosya adını temizle
                    fileName = "talimat";
                }
                
                // Benzersiz dosya adı oluştur
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var extension = Path.GetExtension(fileName);
                var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                
                return $"{nameWithoutExt}_{timestamp}{extension}";
            }
            catch
            {
                return $"talimat_{DateTime.Now:yyyyMMdd_HHmmss}.xls";
            }
        }
        
        private string ExtractPeriodName(ScrapedItem item)
        {
            try
            {
                // Element metninden fatura dönem adını çıkar
                var elementText = item.ElementText;
                
                // Farklı formatları dene
                var patterns = new[]
                {
                    @"Dönem:\s*(.+?)\s*\|", // "Dönem: " formatı
                    @"(\d{1,2}-\d{1,2}\s+\w+\s+\d{4})", // "16-31 Tem 2025" formatı
                    @"(\d{1,2}/\d{1,2}/\d{4})", // "16/31/2025" formatı
                    @"(\d{4}-\d{2})", // "2025-07" formatı
                };
                
                foreach (var pattern in patterns)
                {
                    var match = Regex.Match(elementText, pattern, RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        var periodText = match.Groups[1].Value.Trim();
                        
                        // Tarih formatını okunabilir hale getir ve kısalt
                        var cleanPeriod = periodText
                            .Replace("(", "")
                            .Replace(")", "")
                            .Replace("Temmuz", "Tem")
                            .Replace("Ocak", "Oca")
                            .Replace("Şubat", "Şub")
                            .Replace("Mart", "Mar")
                            .Replace("Nisan", "Nis")
                            .Replace("Mayıs", "May")
                            .Replace("Haziran", "Haz")
                            .Replace("Ağustos", "Ağu")
                            .Replace("Eylül", "Eyl")
                            .Replace("Ekim", "Eki")
                            .Replace("Kasım", "Kas")
                            .Replace("Aralık", "Ara");
                        
                        // Klasör adını kısalt (çok uzun olmasın)
                        if (cleanPeriod.Length > 40)
                        {
                            cleanPeriod = cleanPeriod.Substring(0, 40);
                        }
                        
                        return cleanPeriod;
                    }
                }
                
                // Varsayılan olarak bugünün tarihini kullan
                return DateTime.Now.ToString("yyyy-MM");
            }
            catch
            {
                return DateTime.Now.ToString("yyyy-MM");
            }
        }
        
        private string ExtractSicilName(ScrapedItem item)
        {
            try
            {
                // Element metninden sicil adını çıkar
                var elementText = item.ElementText;
                
                // Önce period name'i al
                var periodName = ExtractPeriodName(item);
                
                // Farklı formatları dene
                var patterns = new[]
                {
                    @"\(([^)]+)\)", // Parantez içindeki metin
                    @"([A-ZÇĞIİÖŞÜ][A-ZÇĞIİÖŞÜ\s]+)", // Büyük harfli Türkçe kelimeler
                };
                
                foreach (var pattern in patterns)
                {
                    var match = Regex.Match(elementText, pattern, RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        var sicilName = match.Groups[1].Value.Trim();
                        
                        // Period name içinde bu sicil adı zaten var mı kontrol et
                        if (!string.IsNullOrEmpty(periodName) && !string.IsNullOrEmpty(sicilName))
                        {
                            var periodNameUpper = periodName.ToUpper();
                            var sicilNameUpper = sicilName.ToUpper();
                            
                            // Sicil adının period name içinde olup olmadığını kontrol et
                            var sicilWords = sicilNameUpper.Split(' ', '_');
                            var isSicilInPeriod = sicilWords.Any(word => word.Length > 2 && periodNameUpper.Contains(word));
                            
                            // Eğer sicil adı period name içinde varsa, boş döndür
                            if (isSicilInPeriod)
                            {
                                return "";
                            }
                        }
                        
                        // Temizle ve kısalt
                        sicilName = sicilName
                            .Replace("/", "_")
                            .Replace("\\", "_")
                            .Replace(":", "_")
                            .Replace("*", "_")
                            .Replace("?", "_")
                            .Replace("\"", "_")
                            .Replace("<", "_")
                            .Replace(">", "_")
                            .Replace("|", "_");
                        
                        if (sicilName.Length > 30)
                        {
                            sicilName = sicilName.Substring(0, 30);
                        }
                        
                        return sicilName;
                    }
                }
                
                return "";
            }
            catch
            {
                return "";
            }
        }

        private string ExtractKeywordFromItem(ScrapedItem item, List<KeywordNotification> keywords)
        {
            try
            {
                // Element metninden ve sicil adından kelime ara
                var elementText = item.ElementText.ToUpper();
                var sicilName = ExtractSicilName(item).ToUpper();
                var periodName = ExtractPeriodName(item).ToUpper();
                
                // Dosya/klasör adından da keyword ara
                var fileName = "";
                if (!string.IsNullOrEmpty(item.DownloadedFilePath))
                {
                    fileName = Path.GetFileName(item.DownloadedFilePath).ToUpper();
                    // Klasör adını da al
                    var folderName = Path.GetDirectoryName(item.DownloadedFilePath);
                    if (!string.IsNullOrEmpty(folderName))
                    {
                        fileName += " " + Path.GetFileName(folderName).ToUpper();
                    }
                }
                
                // Oluşturulacak dosya adını da kontrol et
                var futureFileName = "";
                if (!string.IsNullOrEmpty(periodName))
                {
                    futureFileName = periodName.ToUpper();
                }
                if (!string.IsNullOrEmpty(sicilName))
                {
                    if (!string.IsNullOrEmpty(futureFileName))
                    {
                        futureFileName += "_" + sicilName;
                    }
                    else
                    {
                        futureFileName = sicilName;
                    }
                }
                
                // Tüm metinleri birleştir
                var allText = $"{elementText} {sicilName} {periodName} {fileName} {futureFileName}";
                
                // Debug: Element metnini yazdır
                Console.WriteLine($"🔍 Element Text: {item.ElementText}");
                Console.WriteLine($"🔍 Sicil Name: {sicilName}");
                Console.WriteLine($"🔍 Period Name: {periodName}");
                Console.WriteLine($"🔍 File Name: {fileName}");
                Console.WriteLine($"🔍 Future File Name: {futureFileName}");
                Console.WriteLine($"🔍 All Text: {allText}");
                
                // Aktif kelimeleri kontrol et
                foreach (var keyword in keywords.Where(k => k.Enabled))
                {
                    Console.WriteLine($"🔍 Keyword kontrol ediliyor: {keyword.Keyword}");
                    if (allText.Contains(keyword.Keyword.ToUpper()))
                    {
                        Console.WriteLine($"✅ Keyword bulundu: {keyword.Keyword}");
                        return keyword.Keyword;
                    }
                }

                Console.WriteLine($"❌ Hiçbir keyword bulunamadı, 'Genel' döndürülüyor");
                // Varsayılan olarak "Genel" döndür
                return "Genel";
            }
            catch
            {
                Console.WriteLine($"❌ Hata durumunda 'Genel' döndürülüyor");
                return "Genel";
            }
        }
        
        private bool IsZipFile(byte[] content)
        {
            // ZIP dosyası başlangıç imzası: PK
            return content.Length >= 2 && content[0] == 0x50 && content[1] == 0x4B;
        }
        
        private async Task<List<string>> ExtractZipFile(string zipFilePath, string extractPath, string sicilName, string periodName = "")
        {
            var extractedFiles = new List<string>();
            
            try
            {
                using var archive = System.IO.Compression.ZipFile.OpenRead(zipFilePath);
                
                foreach (var entry in archive.Entries)
                {
                    if (entry.Name.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) || 
                        entry.Name.EndsWith(".xls", StringComparison.OrdinalIgnoreCase))
                    {
                        // ZIP içindeki dosya adından sicil ismini çıkar
                        var extractedSicilName = ExtractSicilNameFromFileName(entry.Name);
                        if (string.IsNullOrEmpty(extractedSicilName))
                        {
                            extractedSicilName = sicilName; // Eğer çıkarılamazsa ana sicil adını kullan
                        }
                        
                        // Her sicil için ayrı klasör oluştur
                        var sicilFolder = Path.Combine(extractPath, extractedSicilName);
                        Directory.CreateDirectory(sicilFolder);
                        
                        string extractedFileName;
                        
                        // Fatura dönemi adını ekle
                        if (!string.IsNullOrEmpty(periodName))
                        {
                            extractedFileName = $"{periodName}_{entry.Name}";
                        }
                        else
                        {
                            extractedFileName = entry.Name;
                        }
                        
                        // XLSX dosyalarını XLS olarak kaydet
                        if (entry.Name.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
                        {
                            extractedFileName = $"{periodName}_{Path.GetFileNameWithoutExtension(entry.Name)}.xls";
                        }
                        
                        var extractedFilePath = Path.Combine(sicilFolder, extractedFileName);
                        
                        // Aynı isimde dosya varsa numara ekle
                        var counter = 1;
                        var originalFilePath = extractedFilePath;
                        while (File.Exists(extractedFilePath))
                        {
                            var nameWithoutExt = Path.GetFileNameWithoutExtension(originalFilePath);
                            var ext = Path.GetExtension(originalFilePath);
                            extractedFilePath = Path.Combine(sicilFolder, $"{nameWithoutExt}_{counter}{ext}");
                            counter++;
                        }
                        
                        // Dosyayı çıkar
                        using var entryStream = entry.Open();
                        using var fileStream = File.Create(extractedFilePath);
                        await entryStream.CopyToAsync(fileStream);
                        
                        // Dosya bütünlüğünü kontrol et
                        var fileInfo = new FileInfo(extractedFilePath);
                        if (fileInfo.Length > 0)
                        {
                            extractedFiles.Add(extractedFilePath);
                        }
                        else
                        {
                            File.Delete(extractedFilePath);
                        }
                    }
                }
                
                // Boş klasörleri temizle
                CleanEmptyFolders(extractPath);
            }
            catch (Exception)
            {
                // Sessizce geç
            }
            
            return extractedFiles;
        }
        
        private string ExtractSicilNameFromFileName(string fileName)
        {
            try
            {
                // Dosya adından sicil ismini çıkar
                var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                
                // Farklı formatları dene
                var patterns = new[]
                {
                    @"^(.+?)_talimat", // "SICIL_ADI_talimat" formatı
                    @"^(.+?)_", // "SICIL_ADI_" formatı
                    @"^(.+?)$", // Sadece sicil adı
                };
                
                foreach (var pattern in patterns)
                {
                    var match = Regex.Match(nameWithoutExt, pattern, RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        var sicilName = match.Groups[1].Value.Trim();
                        if (!string.IsNullOrEmpty(sicilName))
                        {
                            // Sicil adını temizle
                            return sicilName
                                .Replace(" ", "_")
                                .Replace("/", "_")
                                .Replace("\\", "_")
                                .Replace("(", "")
                                .Replace(")", "");
                        }
                    }
                }
                
                return "";
            }
            catch
            {
                return "";
            }
        }
        
        private void CleanEmptyFolders(string basePath)
        {
            try
            {
                var directories = Directory.GetDirectories(basePath, "*", SearchOption.AllDirectories);
                foreach (var dir in directories)
                {
                    if (!Directory.EnumerateFileSystemEntries(dir).Any())
                    {
                        Directory.Delete(dir);
                    }
                }
            }
            catch
            {
                // Sessizce geç
            }
        }
        
        private bool IsDuplicateFile(string ciktiPath, ScrapedItem item)
        {
            try
            {
                // Klasör zaten var mı kontrol et
                if (!Directory.Exists(ciktiPath))
                    return false;
                    
                // Klasördeki dosyaları kontrol et
                var existingFiles = Directory.GetFiles(ciktiPath, "*.*", SearchOption.AllDirectories);
                
                if (!existingFiles.Any())
                    return false;
                    
                // Excel dosyalarını bul
                var excelFiles = existingFiles.Where(f => 
                    Path.GetExtension(f).ToLowerInvariant() == ".xls" || 
                    Path.GetExtension(f).ToLowerInvariant() == ".xlsx").ToList();
                    
                if (!excelFiles.Any())
                    return false;
                    
                // Her Excel dosyasının tutarını kontrol et
                foreach (var excelFile in excelFiles)
                {
                    try
                    {
                        var existingAmount = ExtractTotalAmountFromExcel(excelFile);
                        
                        // Eğer aynı tutar varsa, bu aynı ödeme emri olabilir
                        if (existingAmount > 0)
                        {
                            // Dosya adından dönem bilgisini çıkar
                            var fileName = Path.GetFileNameWithoutExtension(excelFile);
                            var periodName = ExtractPeriodName(item);
                            
                            // Dosya adı ve dönem bilgisi aynıysa, bu aynı dosya
                            if (fileName.Contains(periodName) || periodName.Contains(fileName))
                            {
                                return true;
                            }
                        }
                    }
                    catch
                    {
                        // Sessizce geç
                    }
                }
            }
            catch
            {
                // Sessizce geç
            }
            
            return false;
        }
        
        private List<string> GetPreviouslyDownloadedItemIds(AppConfig config)
        {
            var previouslyDownloadedIds = new List<string>();
            
            try
            {
                // Önce TXT dosyasından oku
                var txtIdsWithPeriods = LoadPreviouslyDownloadedIdsFromTxt();
                if (txtIdsWithPeriods.Any())
                {
                    previouslyDownloadedIds.AddRange(txtIdsWithPeriods.Keys);
                    return previouslyDownloadedIds;
                }
                
                // TXT dosyası yoksa çıktı klasörünü tara (geriye uyumluluk için)
                var baseOutputPath = config.Download.OutputFolder;
                
                // Eğer config'den gelen değer boşsa, varsayılan değeri kullan
                if (string.IsNullOrEmpty(baseOutputPath))
                {
                    baseOutputPath = Path.Combine(Directory.GetCurrentDirectory(), "dist", "cikti");
                }
                else if (!Path.IsPathRooted(baseOutputPath))
                {
                    baseOutputPath = Path.Combine(Directory.GetCurrentDirectory(), baseOutputPath);
                }
                
                // Tüm alt klasörleri tara
                if (Directory.Exists(baseOutputPath))
                {
                    var allDirectories = Directory.GetDirectories(baseOutputPath, "*", SearchOption.AllDirectories);
                    
                    foreach (var directory in allDirectories)
                    {
                        // Klasördeki Excel dosyalarını bul
                        var excelFiles = Directory.GetFiles(directory, "*.xls")
                            .Concat(Directory.GetFiles(directory, "*.xlsx"))
                            .ToList();
                            
                        foreach (var excelFile in excelFiles)
                        {
                            try
                            {
                                // Excel dosyasından item ID'sini çıkar
                                var itemId = ExtractItemIdFromExcelFile(excelFile);
                                if (!string.IsNullOrEmpty(itemId))
                                {
                                    previouslyDownloadedIds.Add(itemId);
                                }
                            }
                            catch
                            {
                                // Sessizce geç
                            }
                        }
                    }
                }
                
                // Bulunan ID'leri TXT dosyasına kaydet
                if (previouslyDownloadedIds.Any())
                {
                    var idsWithPeriods = previouslyDownloadedIds.ToDictionary(id => id, id => "");
                    SavePreviouslyDownloadedIdsToTxt(idsWithPeriods);
                }
            }
            catch
            {
                // Sessizce geç
            }
            
            return previouslyDownloadedIds;
        }
        
        private Dictionary<string, string> LoadPreviouslyDownloadedIdsFromTxt()
        {
            var result = new Dictionary<string, string>();
            try
            {
                var txtFilePath = Path.Combine(Directory.GetCurrentDirectory(), "previously_downloaded.txt");
                if (File.Exists(txtFilePath))
                {
                    var lines = File.ReadAllLines(txtFilePath);
                    foreach (var line in lines)
                    {
                        if (!string.IsNullOrWhiteSpace(line) && !line.StartsWith("#"))
                        {
                            // ID|Dönem formatını parse et
                            var parts = line.Split('|');
                            var id = parts[0].Trim();
                            var period = parts.Length > 1 ? parts[1].Trim() : "";

                            if (!result.ContainsKey(id))
                            {
                                result[id] = period;
                            }
                        }
                    }
                }
            }
            catch
            {
                // Sessizce geç
            }
            
            return result;
        }
        
        private void SavePreviouslyDownloadedIdsToTxt(Dictionary<string, string> itemIdsWithPeriods)
        {
            try
            {
                var txtFilePath = Path.Combine(Directory.GetCurrentDirectory(), "previously_downloaded.txt");
                var lines = new List<string>
                {
                    $"# Daha önce indirilen dosyaların ID'leri ve dönem bilgileri",
                    $"# Format: ID|Dönem Adı",
                    $"# Son güncelleme: {DateTime.Now:dd.MM.yyyy HH:mm:ss}",
                    $"# Toplam dosya sayısı: {itemIdsWithPeriods.Count}",
                    ""
                };

                foreach (var kvp in itemIdsWithPeriods)
                {
                    lines.Add($"{kvp.Key}|{kvp.Value}");
                }

                File.WriteAllLines(txtFilePath, lines);
            }
            catch
            {
                // Sessizce geç
            }
        }
        
        private void AddToPreviouslyDownloadedIds(string itemId, string periodName = "")
        {
            try
            {
                var existingIdsWithPeriods = LoadPreviouslyDownloadedIdsFromTxt();
                if (!existingIdsWithPeriods.ContainsKey(itemId))
                {
                    existingIdsWithPeriods[itemId] = periodName;
                    SavePreviouslyDownloadedIdsToTxt(existingIdsWithPeriods);
                }

                // MainWindow'a da kaydet (dönem bilgisi ile)
                if (Application.Current.MainWindow is MainWindow mainWindow)
                {
                    mainWindow.AddDownloadedFile(itemId, periodName);
                }
            }
            catch
            {
                // Sessizce geç
            }
        }
        
        private string ExtractItemIdFromExcelFile(string excelFilePath)
        {
            try
            {
                // Dosya adından veya içeriğinden item ID'sini çıkar
                var fileName = Path.GetFileNameWithoutExtension(excelFilePath);
                
                // Dosya adında ID varsa çıkar
                var idMatch = Regex.Match(fileName, @"[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}");
                if (idMatch.Success)
                {
                    return idMatch.Value;
                }
                
                // Excel dosyasının içeriğinden ID çıkarmaya çalış
                using var fs = new FileStream(excelFilePath, FileMode.Open, FileAccess.Read);
                IWorkbook workbook;
                
                try
                {
                    fs.Position = 0;
                    workbook = new XSSFWorkbook(fs);
                }
                catch
                {
                    fs.Position = 0;
                    workbook = new HSSFWorkbook(fs);
                }
                
                var sheet = workbook.GetSheetAt(0);
                
                // İlk birkaç satırda ID ara
                for (int rowIndex = 0; rowIndex < Math.Min(10, sheet.LastRowNum); rowIndex++)
                {
                    var row = sheet.GetRow(rowIndex);
                    if (row == null) continue;
                    
                    for (int colIndex = 0; colIndex < row.LastCellNum; colIndex++)
                    {
                        var cell = row.GetCell(colIndex);
                        if (cell != null)
                        {
                            var cellValue = cell.ToString();
                            if (!string.IsNullOrEmpty(cellValue))
                            {
                                var idMatch2 = Regex.Match(cellValue, @"[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}");
                                if (idMatch2.Success)
                                {
                                    return idMatch2.Value;
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // Sessizce geç
            }
            
            return "";
        }
        
        public void CreateExcelFile(string filePath)
        {
            try
            {
                // EPPlus lisansını ayarla (EPPlus 8+ için)
                try
                {
                    // EPPlus 8+ için yeni lisans API'si
                    ExcelPackage.License.SetNonCommercialPersonal("WebScraper");
                }
                catch { }
                
                using var package = new ExcelPackage();
                var worksheet = package.Workbook.Worksheets.Add("Scraped Data");
                
                // Başlıkları ekle
                worksheet.Cells[1, 1].Value = "Orijinal URL";
                worksheet.Cells[1, 2].Value = "Tam URL";
                worksheet.Cells[1, 3].Value = "Durum";
                worksheet.Cells[1, 4].Value = "İndirilen Dosya";
                worksheet.Cells[1, 5].Value = "İndirme Tarihi";
                worksheet.Cells[1, 6].Value = "Hata Mesajı";
                worksheet.Cells[1, 7].Value = "Element Metni";
                
                // Başlık stilini ayarla
                using (var range = worksheet.Cells[1, 1, 1, 7])
                {
                    range.Style.Font.Bold = true;
                    range.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                    range.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
                }
                
                // Verileri ekle
                for (int i = 0; i < _scrapedItems.Count; i++)
                {
                    var item = _scrapedItems[i];
                    var row = i + 2;
                    
                    worksheet.Cells[row, 1].Value = item.OriginalUrl;
                    worksheet.Cells[row, 2].Value = item.FullUrl;
                    worksheet.Cells[row, 3].Value = item.Status;
                    worksheet.Cells[row, 4].Value = item.DownloadedFilePath ?? "";
                    worksheet.Cells[row, 5].Value = item.DownloadDate?.ToString("yyyy-MM-dd HH:mm:ss") ?? "";
                    worksheet.Cells[row, 6].Value = item.ErrorMessage ?? "";
                    worksheet.Cells[row, 7].Value = item.ElementText;
                }
                
                // Sütun genişliklerini ayarla
                worksheet.Column(1).Width = 30;
                worksheet.Column(2).Width = 40;
                worksheet.Column(3).Width = 15;
                worksheet.Column(4).Width = 30;
                worksheet.Column(5).Width = 20;
                worksheet.Column(6).Width = 30;
                worksheet.Column(7).Width = 50;
                
                // Dosyayı kaydet
                package.SaveAs(new System.IO.FileInfo(filePath));
            }
            catch (Exception)
            {
                throw;
            }
        }
        
        private void ProcessWordTemplateWithExcelData(string excelFilePath, string wordTemplatePath, string outputPath)
        {
            try
            {
                // Excel'den dip toplam bilgisini al
                var totalAmount = ExtractTotalAmountFromExcel(excelFilePath);
                
                if (totalAmount <= 0)
                {
                    return;
                }
                
                // Word şablonunu kopyala
                if (!File.Exists(wordTemplatePath))
                {
                    return;
                }
                
                // Çıktı dosyasını oluştur
                var outputDir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }
                
                // Şablonu kopyala
                File.Copy(wordTemplatePath, outputPath, true);
                
                // Word dosyasını aç ve placeholder'ları değiştir
                using var document = WordprocessingDocument.Open(outputPath, true);
                var mainDocumentPart = document.MainDocumentPart;
                
                if (mainDocumentPart?.Document?.Body == null)
                {
                    return;
                }
                
                // Placeholder'ları değiştir
                var currencyFormat = totalAmount.ToString("C", new System.Globalization.CultureInfo("tr-TR"));
                var numberFormat = totalAmount.ToString("N2", new System.Globalization.CultureInfo("tr-TR"));
                var wordsFormat = NumberToWords(totalAmount);
                
                var replacements = new Dictionary<string, string>
                {
                    {"{{TUTAR}}", currencyFormat},
                    {"{{TUTAR_SAYI}}", numberFormat},
                    {"{{TUTAR_YAZI}}", wordsFormat},
                    {"{{TOPLAM_TUTAR}}", $"{numberFormat} TL"},
                    {"{{TOPLAM_TUTAR_TL}}", $"{numberFormat} TL"},
                    {"{{TOPLAM_TUTAR_YAZI}}", $"{wordsFormat}"},
                    {"<TOPLAM_TUTAR>", $"{numberFormat} TL"},
                    {"<TUTAR>", $"{numberFormat} TL"}
                };
                
                foreach (var replacement in replacements)
                {
                    ReplacePlaceholder(mainDocumentPart.Document.Body, replacement.Key, replacement.Value);
                }
                
                // Dosyayı kaydet
                mainDocumentPart.Document.Save();
            }
            catch (Exception)
            {
                // Sessizce geç
            }
        }
        
        private void ReplacePlaceholder(Body body, string placeholder, string replacement)
        {
            try
            {
                // Tüm paragrafları tara
                foreach (var paragraph in body.Elements<Paragraph>())
                {
                    // Paragraftaki tüm çalıştırmaları tara
                    foreach (var run in paragraph.Elements<Run>())
                    {
                        foreach (var text in run.Elements<Text>())
                        {
                            if (text.Text != null && text.Text.Contains(placeholder))
                            {
                                text.Text = text.Text.Replace(placeholder, replacement);
                            }
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Sessizce geç
            }
        }
        
        private string NumberToWords(decimal number)
        {
            try
            {
                var integerPart = (long)Math.Floor(number);
                var decimalPart = (long)Math.Round((number - integerPart) * 100);
                
                var words = "";
                
                if (integerPart == 0)
                {
                    words = "Sıfır";
                }
                else
                {
                    words = ConvertNumberToWords(integerPart);
                }
                
                if (decimalPart > 0)
                {
                    words += $" TL {ConvertNumberToWords(decimalPart)} Kuruş";
                }
                else
                {
                    words += " TL";
                }
                
                return words;
            }
            catch
            {
                return number.ToString("C");
            }
        }
        
        private string ConvertNumberToWords(long number)
        {
            if (number == 0) return "Sıfır";
            
            var words = "";
            
            if (number < 0)
            {
                words = "Eksi ";
                number = Math.Abs(number);
            }
            
            if (number >= 1000000000)
            {
                var billions = number / 1000000000;
                words += ConvertNumberToWords(billions) + " Milyar ";
                number %= 1000000000;
            }
            
            if (number >= 1000000)
            {
                var millions = number / 1000000;
                words += ConvertNumberToWords(millions) + " Milyon ";
                number %= 1000000;
            }
            
            if (number >= 1000)
            {
                var thousands = number / 1000;
                if (thousands == 1)
                {
                    words += "Bin ";
                }
                else
                {
                    words += ConvertNumberToWords(thousands) + " Bin ";
                }
                number %= 1000;
            }
            
            if (number >= 100)
            {
                var hundreds = number / 100;
                if (hundreds == 1)
                {
                    words += "Yüz ";
                }
                else
                {
                    words += GetDigitName(hundreds) + " Yüz ";
                }
                number %= 100;
            }
            
            if (number >= 20)
            {
                var tens = number / 10;
                words += GetTensName(tens);
                number %= 10;
            }
            
            if (number > 0)
            {
                words += GetDigitName(number);
            }
            
            return words.Trim();
        }
        
        private string GetDigitName(long digit)
        {
            return digit switch
            {
                1 => "Bir",
                2 => "İki",
                3 => "Üç",
                4 => "Dört",
                5 => "Beş",
                6 => "Altı",
                7 => "Yedi",
                8 => "Sekiz",
                9 => "Dokuz",
                _ => ""
            };
        }
        
        private string GetTensName(long tens)
        {
            return tens switch
            {
                2 => "Yirmi ",
                3 => "Otuz ",
                4 => "Kırk ",
                5 => "Elli ",
                6 => "Altmış ",
                7 => "Yetmiş ",
                8 => "Seksen ",
                9 => "Doksan ",
                _ => ""
            };
        }
        
        public decimal ExtractTotalAmountFromExcel(string excelFilePath)
        {
            try
            {
                using var fs = new FileStream(excelFilePath, FileMode.Open, FileAccess.Read);
                IWorkbook workbook;
                
                // Dosya formatını tespit et - önce XLSX olarak dene
                try
                {
                    fs.Position = 0;
                    workbook = new XSSFWorkbook(fs);
                }
                catch
                {
                    try
                    {
                        fs.Position = 0;
                        workbook = new HSSFWorkbook(fs);
                    }
                    catch
                    {
                        return 0;
                    }
                }
                
                var sheet = workbook.GetSheetAt(0); // İlk worksheet
                
                // G sütununu tara (7. sütun, index 6)
                for (int rowIndex = 0; rowIndex <= sheet.LastRowNum; rowIndex++)
                {
                    var row = sheet.GetRow(rowIndex);
                    if (row == null) continue;
                    
                    // A sütununu da kontrol et (index 0)
                    var cellA = row.GetCell(0);
                    var cellG = row.GetCell(6); // G sütunu = index 6
                    
                    if (cellA != null)
                    {
                        var cellValueA = cellA.ToString() ?? "";
                        
                        // A sütununda "toplam" kelimesini ara
                        if (cellValueA.ToLower().Contains("toplam") && cellG != null)
                        {
                            var cellValueG = cellG.ToString() ?? "";
                            
                            // Eğer formül varsa, hesaplanmış değeri al
                            if (cellValueG.StartsWith("SUM("))
                            {
                                // Formülün sonucunu hesapla
                                try
                                {
                                    var formulaEvaluator = workbook.GetCreationHelper().CreateFormulaEvaluator();
                                    var cellValue = formulaEvaluator.Evaluate(cellG);
                                    if (cellValue != null && cellValue.CellType == CellType.Numeric)
                                    {
                                        var numericValue = cellValue.NumberValue;
                                        return (decimal)numericValue;
                                    }
                                }
                                catch
                                {
                                    // Formül hesaplanamazsa, manuel hesapla
                                    var range = cellValueG.Replace("SUM(", "").Replace(")", "");
                                    var parts = range.Split(':');
                                    if (parts.Length == 2)
                                    {
                                        decimal total = 0;
                                        for (int i = 4; i <= 602; i++) // G4:G602 aralığı
                                        {
                                            var dataRow = sheet.GetRow(i);
                                            if (dataRow != null)
                                            {
                                                var dataCell = dataRow.GetCell(6);
                                                if (dataCell != null && dataCell.CellType == CellType.Numeric)
                                                {
                                                    total += (decimal)dataCell.NumericCellValue;
                                                }
                                            }
                                        }
                                        return total;
                                    }
                                }
                            }
                            else
                            {
                                // Normal sayısal değer
                                var cleanValue = cellValueG.Replace(",", "").Replace("₺", "").Replace("TL", "").Replace(".", "").Trim();
                                var numericValue = new string(cleanValue.Where(char.IsDigit).ToArray());
                                
                                if (!string.IsNullOrEmpty(numericValue) && decimal.TryParse(numericValue, out var amount))
                                {
                                    return amount;
                                }
                            }
                        }
                    }
                }
                
                return 0;
            }
            catch
            {
                return 0;
            }
        }

        // Comodo vtroot sanal disk sorununu kontrol etmek için path erişilebilirlik kontrolü
        private static bool IsPathAccessible(string path)
        {
            try
            {
                // Path'in bulunduğu klasörü kontrol et
                var directory = Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(directory))
                    return false;

                // Directory.Exists ile kontrol et
                if (!Directory.Exists(directory))
                {
                    // Klasör mevcut değilse oluşturmayı dene
                    Directory.CreateDirectory(directory);
                }

                // Test dosyası oluşturup silmeyi dene
                var testFile = Path.Combine(directory, "test_access.tmp");
                File.WriteAllText(testFile, "test");
                File.Delete(testFile);

                return true;
            }
            catch
            {
                // Erişim hatası varsa false döndür
                return false;
            }
        }

        private string CleanFileName(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                return "Dosya";
            
            // Windows'ta geçersiz karakterleri temizle
            var invalidChars = Path.GetInvalidFileNameChars();
            var cleanName = fileName;
            
            // Geçersiz karakterleri _ ile değiştir
            foreach (var invalidChar in invalidChars)
            {
                cleanName = cleanName.Replace(invalidChar, '_');
            }
            
            // Ay isimlerini kısalt (dosya yolu uzunluğunu azalt)
            cleanName = cleanName
                .Replace("Ocak", "Oca")
                .Replace("Şubat", "Şub")
                .Replace("Mart", "Mar")
                .Replace("Nisan", "Nis")
                .Replace("Mayıs", "May")
                .Replace("Haziran", "Haz")
                .Replace("Temmuz", "Tem")
                .Replace("Ağustos", "Ağu")
                .Replace("Eylül", "Eyl")
                .Replace("Ekim", "Eki")
                .Replace("Kasım", "Kas")
                .Replace("Aralık", "Ara");
            
            // Ek temizlik işlemleri
            cleanName = cleanName
                .Replace(" ", "_")
                .Replace("(", "")
                .Replace(")", "")
                .Replace("[", "")
                .Replace("]", "")
                .Replace("{", "")
                .Replace("}", "")
                .Replace("<", "")
                .Replace(">", "")
                .Replace("|", "_")
                .Replace(":", "_")
                .Replace("*", "_")
                .Replace("?", "_")
                .Replace("\"", "_")
                .Replace("'", "_")
                .Replace("\\", "_")
                .Replace("/", "_")
                .Replace("&", "_")
                .Replace("+", "_")
                .Replace("=", "_")
                .Replace(";", "_")
                .Replace(",", "_")
                .Replace(".", "_")
                .Replace("!", "_")
                .Replace("@", "_")
                .Replace("#", "_")
                .Replace("$", "_")
                .Replace("%", "_")
                .Replace("^", "_")
                .Replace("~", "_")
                .Replace("`", "_");
            
            // Ardışık alt çizgileri tek alt çizgiye çevir
            while (cleanName.Contains("__"))
            {
                cleanName = cleanName.Replace("__", "_");
            }
            
            // Başındaki ve sonundaki alt çizgileri kaldır
            cleanName = cleanName.Trim('_');
            
            // Boşsa varsayılan isim ver
            if (string.IsNullOrEmpty(cleanName))
            {
                cleanName = "Dosya";
            }
            
            // Çok uzunsa kısalt (Windows dosya yolu sınırı)
            if (cleanName.Length > 80) // Daha da kısalt
            {
                cleanName = cleanName.Substring(0, 80);
            }
            
            return cleanName;
        }
        
        private void PlayNotificationSound()
        {
            try
            {
                // Platform kontrolü yap
                if (OperatingSystem.IsWindows())
                {
                    Console.Beep(800, 500); // Windows için
                }
                else if (OperatingSystem.IsMacOS())
                {
                    // macOS için say command kullan
                    System.Diagnostics.Process.Start("say", "Giriş yaptıktan sonra terminalde entere basın.");
                }
                else if (OperatingSystem.IsLinux())
                {
                    // Linux için beep command kullan
                    System.Diagnostics.Process.Start("beep");
                }
            }
            catch (Exception)
            {
                // Ses çalınamazsa sadece mesaj göster
            }
        }

        /// <summary>
        /// Dönem adından dönem ID'sini çıkarır
        /// </summary>
        private string ExtractPeriodIdFromPeriodName(string periodName)
        {
            if (string.IsNullOrEmpty(periodName))
                return string.Empty;

            try
            {
                // Dosya adında tarih formatı ara (örnek: "2024-01", "2024_01", "2024.01")
                var periodMatch = System.Text.RegularExpressions.Regex.Match(periodName, @"(\d{4})[-_.](\d{1,2})");
                if (periodMatch.Success)
                {
                    var year = periodMatch.Groups[1].Value;
                    var month = periodMatch.Groups[2].Value.PadLeft(2, '0');
                    return $"{year}{month}"; // "202401" formatında döndür
                }
                
                // Türkçe ay isimleri ile tarih formatı ara (örnek: "01-15_Tem_2025")
                var turkishMonthMatch = System.Text.RegularExpressions.Regex.Match(periodName, @"(\d{1,2})-(\d{1,2})_([A-Za-z]+)_(\d{4})");
                if (turkishMonthMatch.Success)
                {
                    var year = turkishMonthMatch.Groups[4].Value;
                    var monthName = turkishMonthMatch.Groups[3].Value.ToLower();
                    
                    // Türkçe ay isimlerini sayıya çevir
                    var monthNumber = monthName switch
                    {
                        "ocak" => "01",
                        "şubat" => "02",
                        "mart" => "03",
                        "nisan" => "04",
                        "mayıs" => "05",
                        "haziran" => "06",
                        "temmuz" => "07",
                        "ağustos" => "08",
                        "eylül" => "09",
                        "ekim" => "10",
                        "kasım" => "11",
                        "aralık" => "12",
                        "tem" => "07", // Kısaltma
                        "may" => "05", // Kısaltma
                        _ => DateTime.Now.ToString("MM")
                    };
                    
                    return $"{year}{monthNumber}"; // "202507" formatında döndür
                }

                // Sadece yıl-ay formatı ara (örnek: "2025-07")
                var simpleMatch = System.Text.RegularExpressions.Regex.Match(periodName, @"(\d{4})-(\d{1,2})");
                if (simpleMatch.Success)
                {
                    var year = simpleMatch.Groups[1].Value;
                    var month = simpleMatch.Groups[2].Value.PadLeft(2, '0');
                    return $"{year}{month}"; // "202507" formatında döndür
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Dönem ID çıkarma hatası: {ex.Message}");
            }
            
            return string.Empty;
        }

        private async Task SendCompletionNotificationAsync(int successCount, int totalCount, AppConfig config, Action<string>? logCallback, List<ScrapedItem>? scrapedItems = null)
        {
            try
            {
                logCallback?.Invoke("📧 Mail bildirimi kontrol ediliyor...");
                
                if (config.Notification?.Enabled != true)
                {
                    logCallback?.Invoke("❌ Mail bildirimi kapalı.");
                    return;
                }

                logCallback?.Invoke("✅ Mail bildirimi aktif.");

                var emailService = new EmailNotificationService(config.Notification);
                var outputFolder = config.Download.OutputFolder ?? "cikti";
                var completionTime = DateTime.Now;

                // Kelime bazlı tutar hesaplama ve mail gönderme
                if (scrapedItems != null && scrapedItems.Any())
                {
                    logCallback?.Invoke($"📊 Toplam {scrapedItems.Count} dosya bulundu.");
                    
                    var onaylandiItems = scrapedItems.Where(item => !string.IsNullOrEmpty(item.Status) && item.Status.ToLower().Contains("onaylandı")).ToList();
                    logCallback?.Invoke($"✅ Onaylandı durumunda {onaylandiItems.Count} dosya bulundu.");
                    
                    if (!onaylandiItems.Any())
                    {
                        logCallback?.Invoke("ℹ️ Onaylandı durumunda dosya bulunamadı, mail gönderilmeyecek.");
                        return;
                    }
                    
                    var keywordGroups = onaylandiItems
                        .Where(item => item != null)
                        .GroupBy(item => ExtractKeywordFromItem(item, config.Notification.Keywords))
                        .ToList();

                    logCallback?.Invoke($"🔍 {keywordGroups.Count} farklı keyword grubu bulundu.");
                    
                    // Toplam mail sayısını hesapla
                    var totalMailCount = keywordGroups.Count;
                    var currentMailIndex = 0;
                    
                    foreach (var keywordGroup in keywordGroups)
                    {
                        currentMailIndex++;
                        var keyword = keywordGroup.Key;
                        var keywordItems = keywordGroup.ToList();
                        var keywordTotalAmount = CalculateTotalAmountFromItems(keywordItems);
                        
                        // Dönem adını al (ilk item'dan)
                        var periodName = "";
                        if (keywordItems.Any())
                        {
                            periodName = ExtractPeriodName(keywordItems.First());
                        }

                        logCallback?.Invoke($"🔍 Keyword: '{keyword}' - {keywordItems.Count} dosya - {keywordTotalAmount:N2} TL");

                        // Bu kelime için mail ayarı var mı kontrol et
                        var keywordConfig = config.Notification.Keywords.FirstOrDefault(k => 
                            k.Enabled && k.Keyword.Equals(keyword, StringComparison.OrdinalIgnoreCase));

                        if (keywordConfig != null && !string.IsNullOrEmpty(keywordConfig.EmailRecipient))
                        {
                            // Her keyword için Ctrl+Enter ile otomatik gönderim
                            EmailNotificationService.SetLastKeyword(true);
                            
                            logCallback?.Invoke($"📧 [{currentMailIndex}/{totalMailCount}] '{keyword}' için mail gönderiliyor: {keywordConfig.EmailRecipient}");
                            
                            // Mail gönderim detaylarını logla
                            logCallback?.Invoke($"🔍 Mail gönderim süreci başlatılıyor...");
                            
                            // Keyword için dosya listesi oluştur
                            var keywordFiles = keywordItems
                                .Where(item => !string.IsNullOrEmpty(item.DownloadedFilePath))
                                .Select(item => item.DownloadedFilePath!)
                                .Where(path => !string.IsNullOrEmpty(path))
                                .ToList();
                            
                            await emailService.SendCompletionNotificationAsync(keywordFiles, keywordTotalAmount, logCallback, periodName);
                            
                            logCallback?.Invoke($"✅ [{currentMailIndex}/{totalMailCount}] '{keyword}' kelimesi için mail gönderim süreci tamamlandı. Tutar: {keywordTotalAmount:N2} TL");
                            
                            // Son mail değilse kısa bir bekleme süresi
                            if (currentMailIndex < totalMailCount)
                            {
                                logCallback?.Invoke($"⏳ Sonraki mail için bekleniyor...");
                                await Task.Delay(3000); // 3 saniye bekle (mail gönderimi için daha uzun süre)
                            }
                        }
                        else
                        {
                            logCallback?.Invoke($"ℹ️ '{keyword}' kelimesi için mail alıcısı tanımlanmamış.");
                        }
                    }
                }
                else
                {
                    // Genel mail gönder
                    var allFiles = scrapedItems
                        .Where(item => !string.IsNullOrEmpty(item.DownloadedFilePath))
                        .Select(item => item.DownloadedFilePath)
                        .ToList();
                    
                    var calculatedTotalAmount = CalculateTotalAmountFromItems(scrapedItems);
                    await emailService.SendCompletionNotificationAsync(allFiles, calculatedTotalAmount, logCallback, null);
                    logCallback?.Invoke($"✅ Genel mail bildirimi gönderildi. Başarılı: {successCount}/{totalCount}");
                }
            }
            catch (Exception ex)
            {
                logCallback?.Invoke($"❌ Mail bildirimi gönderilemedi: {ex.Message}");
            }
        }

        private decimal CalculateTotalAmountFromItems(List<ScrapedItem> items)
        {
            try
            {
                decimal totalAmount = 0;
                
                foreach (var item in items)
                {
                    // İndirilen dosya varsa Excel'den tutarı al
                    if (!string.IsNullOrEmpty(item.DownloadedFilePath) && File.Exists(item.DownloadedFilePath))
                    {
                        var excelAmount = ExtractTotalAmountFromExcel(item.DownloadedFilePath);
                        if (excelAmount > 0)
                        {
                            totalAmount += excelAmount;
                            continue; // Excel'den tutar alındıysa diğer yöntemleri deneme
                        }
                    }
                    
                    // Excel'den alınamadıysa element metninden çıkar
                    var amount = ExtractAmountFromElementText(item.ElementText);
                    totalAmount += amount;
                }
                
                return totalAmount;
            }
            catch
            {
                return 0;
            }
        }

        private decimal ExtractAmountFromElementText(string elementText)
        {
            try
            {
                // Tutar formatlarını dene
                var patterns = new[]
                {
                    @"(\d{1,3}(?:\.\d{3})*(?:,\d{2})?)\s*TL", // "1.234,56 TL" formatı
                    @"(\d{1,3}(?:\.\d{3})*(?:,\d{2})?)", // "1.234,56" formatı
                    @"TL\s*(\d{1,3}(?:\.\d{3})*(?:,\d{2})?)", // "TL 1.234,56" formatı
                };

                foreach (var pattern in patterns)
                {
                    var match = Regex.Match(elementText, pattern, RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        var amountText = match.Groups[1].Value.Trim();
                        
                        // Türk para birimi formatını parse et
                        amountText = amountText.Replace(".", "").Replace(",", ".");
                        
                        if (decimal.TryParse(amountText, out decimal amount))
                        {
                            return amount;
                        }
                    }
                }
                
                return 0;
            }
            catch
            {
                return 0;
            }
        }
        
        private void ShowCompletionAlert(int successCount, int totalCount, decimal totalAmount, AppConfig config, Action<string>? logCallback, int previouslyDownloaded = 0, TimeSpan? totalTime = null)
        {
            try
            {
                // Ayarlardan seçilen çıktı klasörünü al
                var outputFolder = config.Download.OutputFolder;
                
                // Eğer config'den gelen değer boşsa, varsayılan değeri kullan
                if (string.IsNullOrEmpty(outputFolder))
                {
                    outputFolder = "cikti";
                }
                
                logCallback?.Invoke("Modern completion modal gösteriliyor...");
                
                // Windows alert göster - doğrudan çağır
                var alertMessage = $"İşlem Tamamlandı!\n\n" +
                                  $"📁 Bulunan Dosya: {totalCount}\n" +
                                  $"✅ Başarıyla İndirilen: {successCount}\n" +
                                  $"❌ Başarısız: {totalCount - successCount}\n" +
                                  $"💰 Toplam Tutar: {totalAmount:N2} TL\n" +
                                  $"⏭️ Daha Önce İndirilen: {previouslyDownloaded}\n" +
                                  $"⏱️ Toplam Süre: {totalTime?.TotalSeconds:F0} saniye\n\n" +
                                  $"📂 Dosyalar '{outputFolder}' klasörüne kaydedildi.";
                
                // Ana thread'de Topmost MessageBox göster
                var mainWindow = System.Windows.Application.Current.MainWindow as MainWindow;
                if (mainWindow != null)
                {
                    mainWindow.Dispatcher.Invoke(() =>
                    {
                        // Topmost özelliği ile MessageBox göster
                        var result = System.Windows.MessageBox.Show(alertMessage, "İşlem Tamamlandı", 
                            System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                        
                                        // MessageBox'ı en öne getir
                var hwnd = NativeMethods.FindWindow(null, "İşlem Tamamlandı");
                if (hwnd != IntPtr.Zero)
                {
                    NativeMethods.SetForegroundWindow(hwnd);
                    NativeMethods.BringWindowToTop(hwnd);
                }
                    });
                }
                else
                {
                    // Fallback - doğrudan MessageBox göster
                    var result = System.Windows.MessageBox.Show(alertMessage, "İşlem Tamamlandı", 
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                    
                    // MessageBox'ı en öne getir
                    var hwnd = NativeMethods.FindWindow(null, "İşlem Tamamlandı");
                    if (hwnd != IntPtr.Zero)
                    {
                        NativeMethods.SetForegroundWindow(hwnd);
                        NativeMethods.BringWindowToTop(hwnd);
                    }
                }
            }
            catch (Exception ex)
            {
                logCallback?.Invoke($"Modal gösterilirken hata: {ex.Message}");
                
                // Ayarlardan seçilen çıktı klasörünü al (catch bloğunda tekrar al)
                var outputFolder = config.Download.OutputFolder;
                
                // Eğer config'den gelen değer boşsa, varsayılan değeri kullan
                if (string.IsNullOrEmpty(outputFolder))
                {
                    outputFolder = "cikti";
                }
                
                // Fallback olarak eski MessageBox'ı göster
                var message = $"İşlem Tamamlandı!\n\n" +
                             $"Bulunan Dosya: {totalCount}\n" +
                             $"Başarıyla İndirilen: {successCount}\n" +
                             $"Başarısız: {totalCount - successCount}\n" +
                             $"Toplam Tutar: {totalAmount:N2} TL\n\n" +
                             $"Dosyalar '{outputFolder}' klasörüne kaydedildi.";
                
                var result = System.Windows.MessageBox.Show(message, "İşlem Tamamlandı", 
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                
                // MessageBox'ı en öne getir
                var hwnd = NativeMethods.FindWindow(null, "İşlem Tamamlandı");
                if (hwnd != IntPtr.Zero)
                {
                    NativeMethods.SetForegroundWindow(hwnd);
                    NativeMethods.BringWindowToTop(hwnd);
                }
            }
        }

        // Yeni taslak onaylama metodu
        public async Task ApproveDraftItemsAsync(string username, string password, string companyCode, string totpSecret, AppConfig config, string pageType, int pageSize, CancellationToken cancellationToken, Action<string, string, StatusType>? statusCallback = null, Action<string>? logCallback = null, Action<int, int>? progressCallback = null, Action<int>? foundCallback = null, Action<int>? downloadedCallback = null, Action<decimal>? totalAmountCallback = null)
        {
            var startTime = DateTime.Now;
            logCallback?.Invoke("Taslak onaylama işlemi başlatılıyor...");
            
            // URL'i hardcoded yap
            const string url = "https://www.pinhuman.net";
            logCallback?.Invoke($"URL scraping başlatılıyor: {url}");
            
            var playwright = await Playwright.CreateAsync();
            IBrowser browser;
            
            try
            {
                // Config'den headless mod ayarını al
                var isHeadless = config.Sms.HeadlessMode;
                logCallback?.Invoke($"Gizli mod ayarı: {(isHeadless ? "Açık" : "Kapalı")}");
                
                browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Headless = isHeadless, // Config'den alınan ayar
                    Args = new[] { "--no-sandbox", "--disable-dev-shm-usage", "--disable-web-security", "--disable-features=VizDisplayCompositor" }
                });
            }
            catch (Exception ex) when (ex.Message.Contains("Executable doesn't exist"))
            {
                logCallback?.Invoke("Playwright tarayıcıları yükleniyor...");
                
                var chromePaths = new List<string>();
                
                if (OperatingSystem.IsWindows())
                {
                    chromePaths.AddRange(new[]
                    {
                        @"C:\Program Files\Google\Chrome\Application\chrome.exe",
                        @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + @"\Google\Chrome\Application\chrome.exe"
                    });
                }
                else if (OperatingSystem.IsMacOS())
                {
                    chromePaths.AddRange(new[]
                    {
                        "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
                        "/Applications/Chromium.app/Contents/MacOS/Chromium"
                    });
                }
                else if (OperatingSystem.IsLinux())
                {
                    chromePaths.AddRange(new[]
                    {
                        "/usr/bin/google-chrome",
                        "/usr/bin/google-chrome-stable",
                        "/usr/bin/chromium-browser",
                        "/usr/bin/chromium"
                    });
                }
                
                string? foundChromePath = null;
                foreach (var path in chromePaths)
                {
                    if (System.IO.File.Exists(path))
                    {
                        foundChromePath = path;
                        break;
                    }
                }
                
                if (!string.IsNullOrEmpty(foundChromePath))
                {
                    browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                    {
                        Headless = config.Sms.HeadlessMode, // Config'den alınan ayar
                        ExecutablePath = foundChromePath,
                        Args = new[] { "--no-sandbox", "--disable-dev-shm-usage", "--disable-web-security", "--disable-features=VizDisplayCompositor" }
                    });
                }
                else
                {
                    throw new Exception("Chrome tarayıcısı bulunamadı.");
                }
            }
            
            var context = await browser.NewContextAsync();
            var page = await context.NewPageAsync();
            var hasDraftItems = false; // Taslak öğelerin varlığını takip etmek için
            
            try
            {
                statusCallback?.Invoke("Login", "Login sayfası yükleniyor...", StatusType.Processing);
                logCallback?.Invoke("Login sayfası yükleniyor...");
                
                await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
                
                // Login işlemi
                if (config.AutoLogin.Enabled)
                {
                    await PerformLoginAsync(page, username, password, companyCode, totpSecret, statusCallback, logCallback);
                }
                else
                {
                    logCallback?.Invoke("Otomatik login devre dışı. Manuel giriş bekleniyor...");
                    await page.WaitForSelectorAsync("input[type='email'], input[name='email'], input[id='email']", new PageWaitForSelectorOptions { Timeout = 30000 });
                }
                
                // Sayfa türüne göre doğru URL'yi oluştur
                string targetUrl;
                string pageTypeText;
                
                if (pageType == "advance")
                {
                    // Avans ödeme emri için sabit URL
                    targetUrl = "https://www.pinhuman.net/StaffAdvancePaymentOrder";
                    pageTypeText = "Avans Ödeme Emri";
                }
                else
                {
                    // Normal ödeme emri için sabit URL
                    targetUrl = "https://www.pinhuman.net/StaffPaymentOrder";
                    pageTypeText = "Normal Ödeme Emri";
                }
                
                statusCallback?.Invoke($"{pageTypeText} Sayfasına Gidiliyor", $"{pageTypeText} sayfasına yönlendiriliyor...", StatusType.Processing);
                logCallback?.Invoke($"{pageTypeText} sayfasına gidiliyor...");
                
                // Seçilen sayfa türüne git
                await page.GotoAsync(targetUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
                logCallback?.Invoke($"{pageTypeText} sayfasına gidildi: {targetUrl}");
                
                // Sayfa sayısını 120'ye ayarla
                await SetPageSizeAsync(page, pageSize, statusCallback, logCallback);
                
                statusCallback?.Invoke("Taslak Öğeler Aranıyor", "Taslak durumundaki öğeler aranıyor...", StatusType.Processing);
                logCallback?.Invoke("Taslak öğeler aranıyor...");
                
                // StaffPaymentOrder sayfasında taslak öğeleri bul
                var draftItems = await FindDraftItemsAsync(page, logCallback);
                
                hasDraftItems = draftItems.Count > 0;
                
                if (!hasDraftItems)
                {
                    logCallback?.Invoke("Taslak durumunda öğe bulunamadı.");
                    statusCallback?.Invoke("Tamamlandı", "Taslak öğe bulunamadı.", StatusType.Success);
                    
                    // Taslak bulunamadığında Step 2'ye geç ama tarayıcıyı kapatma
                    logCallback?.Invoke("\n" + new string('═', 60));
                    logCallback?.Invoke("🔄 STEP 2: ONAYLANDI DURUMUNDAKİ DOSYALAR İNDİRİLİYOR");
                    logCallback?.Invoke(new string('═', 60));
                    
                    try
                    {
                        // Ana sayfaya geri dön ve onaylandı dosyaları indir
                        await page.BringToFrontAsync();
                        
                        // Sayfa sayısını 120'ye ayarla
                        await SetPageSizeAsync(page, pageSize, statusCallback, logCallback);
                        
                        // Onaylandı dosyaları bul ve indir
                        var onaylandiCount = await FindAndProcessUrlsAsync(page, "", "", config, progressCallback, statusCallback, logCallback, cancellationToken, foundCallback, downloadedCallback, totalAmountCallback);
                        
                        // Diğer sayfaları kontrol et
                        await CheckOtherPagesIfNeeded(page, "", "", onaylandiCount, statusCallback, logCallback, cancellationToken);
                        
                        logCallback?.Invoke("✅ Step 2 tamamlandı: Onaylandı dosyalar indirildi.");
                    }
                    catch (Exception ex)
                    {
                        logCallback?.Invoke($"❌ Step 2 sırasında hata: {ex.Message}");
                        statusCallback?.Invoke("Hata", "Onaylandı dosyalar indirilirken hata oluştu.", StatusType.Error);
                    }
                    
                    // Taslak bulunamadığında tarayıcıyı kapatma, kullanıcı manuel işlem yapabilir
                    logCallback?.Invoke("🔍 Tarayıcı açık bırakıldı. Manuel işlem yapabilirsiniz.");
                    return;
                }
                
                logCallback?.Invoke($"{draftItems.Count} adet taslak öğe bulundu. Onaylama işlemi başlatılıyor...");
                
                // Önce tüm taslak öğeler için sekmeleri aç
                var detailPages = new List<IPage>();
                
                statusCallback?.Invoke("Sekmeler Açılıyor", "Tüm taslak öğeler için sekmeler açılıyor...", StatusType.Processing);
                logCallback?.Invoke("Tüm taslak öğeler için sekmeler açılıyor...");
                
                foreach (var item in draftItems)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        logCallback?.Invoke("İşlem kullanıcı tarafından durduruldu.");
                        break;
                    }
                    
                    try
                    {
                        // Yeni sekme aç
                        var detailPage = await context.NewPageAsync();
                        
                        // Detay sayfasına git
                        string fullUrl;
                        if (item.DetailUrl.StartsWith("http"))
                        {
                            fullUrl = item.DetailUrl;
                        }
                        else
                        {
                            var currentUrl = page.Url;
                            var uri = new Uri(currentUrl);
                            var baseUrl = $"{uri.Scheme}://{uri.Host}";
                            if (uri.Port != 80 && uri.Port != 443)
                            {
                                baseUrl += $":{uri.Port}";
                            }
                            fullUrl = baseUrl + item.DetailUrl;
                        }
                        
                        logCallback?.Invoke($"Sekme açılıyor: {item.Id}");
                        await detailPage.GotoAsync(fullUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
                        
                        // Sekmeyi ve item ID'sini eşleştirmek için page'e metadata ekle
                        await detailPage.EvaluateAsync($"() => {{ window.itemId = '{item.Id}'; }}");
                        
                        detailPages.Add(detailPage);
                    }
                    catch (Exception ex)
                    {
                        logCallback?.Invoke($"Sekme açılırken hata ({item.Id}): {ex.Message}");
                    }
                }
                
                logCallback?.Invoke($"{detailPages.Count} adet sekme açıldı. Onaylama işlemi başlatılıyor...");
                
                // Şimdi her sekmeyi sırayla işle ve kapat - aktif sekme geçişi ile
                statusCallback?.Invoke("Öğeler Onaylanıyor", "Her sekme sırayla işleniyor...", StatusType.Processing);
                
                for (int i = 0; i < detailPages.Count; i++)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        logCallback?.Invoke("İşlem kullanıcı tarafından durduruldu.");
                        break;
                    }
                    
                    var detailPage = detailPages[i];
                    
                    try
                    {
                        // Sekmeye aktif olarak geç
                        await detailPage.BringToFrontAsync();
                        logCallback?.Invoke($"Sekme aktif hale getirildi ({i + 1}/{detailPages.Count})");
                        
                        // Kısa bekleme - kullanıcının görmesi için
                        await page.WaitForTimeoutAsync(500);
                        
                        // Item ID'sini al
                        var itemId = await detailPage.EvaluateAsync<string>("() => window.itemId");
                        logCallback?.Invoke($"Öğe onaylanıyor: {itemId} ({i + 1}/{detailPages.Count})");
                        
                        await ProcessSingleDraftPageAsync(detailPage, itemId, logCallback);
                        
                        // İşlem tamamlandıktan sonra kısa bekleme
                        await page.WaitForTimeoutAsync(1000);
                        
                        // Sekmeyi kapat
                        await detailPage.CloseAsync();
                        logCallback?.Invoke($"Sekme kapatıldı: {itemId}");
                    }
                    catch (Exception ex)
                    {
                        logCallback?.Invoke($"Sekme işlenirken hata: {ex.Message}");
                        try
                        {
                            await detailPage.CloseAsync();
                        }
                        catch { /* Sekme zaten kapalıysa hata verme */ }
                    }
                }
                
                // İşlem bittikten sonra ana listeye geri dön
                try
                {
                    await page.BringToFrontAsync();
                    logCallback?.Invoke("Ana liste sekmesine geri dönüldü");
                }
                catch (Exception ex)
                {
                    logCallback?.Invoke($"Ana sekmeye dönüşte hata: {ex.Message}");
                }
                
                // Ana listeye dön (ana sekme hala açık)
                logCallback?.Invoke("Ana liste sayfasına dönülüyor...");
                
                statusCallback?.Invoke("Tamamlandı", "Taslak onaylama işlemi tamamlandı.", StatusType.Success);
                logCallback?.Invoke("Taslak onaylama işlemi tamamlandı.");

                // Step 1.5: Onay bekleyen öğeleri son onay için işle
                logCallback?.Invoke("\n" + new string('═', 60));
                logCallback?.Invoke("🔄 STEP 1.5: ONAY BEKLEYEN ÖĞELERİ SON ONAY İÇİN İŞLEME");
                logCallback?.Invoke(new string('═', 60));

                try
                {
                    // Sayfa sayısını 120'ye ayarla
                    await SetPageSizeAsync(page, pageSize, statusCallback, logCallback);

                    // Onay bekleyen öğeleri bul ve son onay işlemini gerçekleştir
                    var waitingApprovalCount = await FindAndApproveWaitingItemsAsync(page, statusCallback, logCallback, cancellationToken);

                    if (waitingApprovalCount > 0)
                    {
                        logCallback?.Invoke($"✅ Step 1.5 tamamlandı: {waitingApprovalCount} onay bekleyen öğe son onaylandı.");
                    }
                    else
                    {
                        logCallback?.Invoke("ℹ️ Step 1.5: Onay bekleyen öğe bulunamadı, sonraki adıma geçiliyor.");
                    }
                }
                catch (Exception ex)
                {
                    logCallback?.Invoke($"❌ Step 1.5 sırasında hata: {ex.Message}");
                    statusCallback?.Invoke("Uyarı", "Onay bekleyen öğeler işlenirken hata oluştu, sonraki adıma geçiliyor.", StatusType.Warning);
                }

                // Step 2: Onaylandı durumundaki dosyaları indir
                logCallback?.Invoke("\n" + new string('═', 60));
                logCallback?.Invoke("🔄 STEP 2: ONAYLANDI DURUMUNDAKİ DOSYALAR İNDİRİLİYOR");
                logCallback?.Invoke(new string('═', 60));
                
                try
                {
                    // Ana sayfaya geri dön ve onaylandı dosyaları indir
                    await page.BringToFrontAsync();
                    
                    // Sayfa sayısını 120'ye ayarla
                    await SetPageSizeAsync(page, pageSize, statusCallback, logCallback);
                    
                    // Onaylandı dosyaları bul ve indir
                    var onaylandiCount = await FindAndProcessUrlsAsync(page, "", "", config, progressCallback, statusCallback, logCallback, cancellationToken, foundCallback, downloadedCallback, totalAmountCallback);
                    
                    // Diğer sayfaları kontrol et
                    await CheckOtherPagesIfNeeded(page, "", "", onaylandiCount, statusCallback, logCallback, cancellationToken);
                    
                    logCallback?.Invoke("✅ Step 2 tamamlandı: Onaylandı dosyalar indirildi.");
                    
                    // Toplam işlem süresini hesapla ve göster
                    var totalTime = DateTime.Now - startTime;
                    logCallback?.Invoke($"⏱️ Toplam işlem süresi: {totalTime.Minutes:D2}:{totalTime.Seconds:D2}");
                }
                catch (Exception ex)
                {
                    logCallback?.Invoke($"❌ Step 2 sırasında hata: {ex.Message}");
                    statusCallback?.Invoke("Hata", "Onaylandı dosyalar indirilirken hata oluştu.", StatusType.Error);
                }
            }
            finally
            {
                // Her durumda tarayıcıyı açık bırak
                try
                {
                    // Sadece playwright'ı dispose et, tarayıcıyı kapatma
                    playwright.Dispose();
                    logCallback?.Invoke("🔍 Tarayıcı açık bırakıldı. Manuel işlem yapabilirsiniz.");
                }
                catch (Exception ex)
                {
                    logCallback?.Invoke($"Tarayıcı işlemi sırasında hata: {ex.Message}");
                }
            }
        }

        private async Task<List<DraftItem>> FindDraftItemsAsync(IPage page, Action<string>? logCallback)
        {
            var draftItems = new List<DraftItem>();
            
            try
            {
                logCallback?.Invoke("Mevcut sayfadaki taslak öğeler aranıyor...");
                
                // Mevcut sayfadaki taslak öğeleri bul
                draftItems = await FindDraftItemsInCurrentPageAsync(page, logCallback);
                
                logCallback?.Invoke($"Toplam {draftItems.Count} taslak öğe bulundu.");
            }
            catch (Exception ex)
            {
                logCallback?.Invoke($"Taslak öğeler aranırken hata: {ex.Message}");
            }
            
            return draftItems;
        }
        
        private async Task<List<DraftItem>> FindDraftItemsInCurrentPageAsync(IPage page, Action<string>? logCallback)
        {
            var draftItems = new List<DraftItem>();
            
            try
            {
                logCallback?.Invoke("Mevcut sayfadaki tüm satırlar taranıyor...");
                
                // Tüm tablo satırlarını bul - daha geniş selector kullan
                var allRows = await page.QuerySelectorAllAsync("table tbody tr, .table tbody tr, tr[data-index], tr");
                logCallback?.Invoke($"Toplam {allRows.Count} satır bulundu.");
                
                var processedCount = 0;
                var taslakCount = 0;
                
                foreach (var row in allRows)
                {
                    try
                    {
                        processedCount++;
                        
                        // Satırın tüm hücrelerini al
                        var allCells = await row.QuerySelectorAllAsync("td");
                        
                        if (allCells.Count == 0)
                        {
                            // Header satırı veya boş satır, geç
                            continue;
                        }
                        
                        // Durum bilgisini bul - tüm sütunları kontrol et
                        string status = "";
                        foreach (var cell in allCells)
                        {
                            var cellText = await cell.TextContentAsync();
                            if (!string.IsNullOrEmpty(cellText))
                            {
                                var cleanText = cellText.Trim().ToLower();
                                if (cleanText.Contains("taslak") || cleanText.Contains("bekleyen") || cleanText.Contains("onaylandı") || cleanText.Contains("reddedildi"))
                                {
                                    status = cellText.Trim();
                                    break;
                                }
                            }
                        }
                        
                        // Taslak durumunu kontrol et
                        if (status.ToLower().Contains("taslak"))
                        {
                            taslakCount++;
                            logCallback?.Invoke($"Taslak bulundu (Satır {processedCount}): {status}");
                            
                            // Detay linkini bul - daha geniş arama
                            var detailLink = await row.QuerySelectorAsync("a[href*='Details'], a[href*='Detail'], a[href*='/StaffPaymentOrder/'], a[href*='/AdvancePaymentOrder/']");
                            
                            if (detailLink != null)
                            {
                                var href = await detailLink.GetAttributeAsync("href");
                                if (!string.IsNullOrEmpty(href))
                                {
                                    var itemId = ExtractItemIdFromUrl(href);
                                    if (!string.IsNullOrEmpty(itemId))
                                    {
                                        draftItems.Add(new DraftItem
                                        {
                                            Id = itemId,
                                            DetailUrl = href,
                                            RowElement = row
                                        });
                                        logCallback?.Invoke($"Taslak öğe eklendi: {itemId}");
                                    }
                                    else
                                    {
                                        logCallback?.Invoke($"Item ID çıkarılamadı: {href}");
                                    }
                                }
                                else
                                {
                                    logCallback?.Invoke($"Detay linki href'i boş (Satır {processedCount})");
                                }
                            }
                            else
                            {
                                logCallback?.Invoke($"Detay linki bulunamadı (Satır {processedCount})");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        logCallback?.Invoke($"Satır {processedCount} işlenirken hata: {ex.Message}");
                    }
                }
                
                logCallback?.Invoke($"Tarama tamamlandı: {processedCount} satır işlendi, {taslakCount} taslak bulundu, {draftItems.Count} öğe eklendi.");
            }
            catch (Exception ex)
            {
                logCallback?.Invoke($"Mevcut sayfadaki taslak öğeler aranırken hata: {ex.Message}");
            }
            
            return draftItems;
        }

        private async Task<int> FindAndApproveWaitingItemsAsync(IPage page, Action<string, string, StatusType>? statusCallback, Action<string>? logCallback, CancellationToken cancellationToken)
        {
            var approvedCount = 0;

            try
            {
                logCallback?.Invoke("Onay bekleyen öğeler aranıyor...");

                // Tüm tablo satırlarını bul - daha geniş selector kullan
                var allRows = await page.QuerySelectorAllAsync("table tbody tr, .table tbody tr, tr[data-index], tr");
                logCallback?.Invoke($"Toplam {allRows.Count} satır bulundu.");

                var processedCount = 0;
                var waitingCount = 0;

                foreach (var row in allRows)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        logCallback?.Invoke("İşlem kullanıcı tarafından durduruldu.");
                        break;
                    }

                    try
                    {
                        processedCount++;

                        // Satırın tüm hücrelerini al
                        var allCells = await row.QuerySelectorAllAsync("td");

                        if (allCells.Count == 0)
                        {
                            // Header satırı veya boş satır, geç
                            continue;
                        }

                        // Durum hücresini bul - genellikle son hücrelerde olur
                        string status = "";
                        string itemId = "";

                        // Tüm hücreleri tara
                        for (int i = 0; i < allCells.Count; i++)
                        {
                            var cell = allCells[i];
                            var cellText = await cell.TextContentAsync();
                            var cleanText = cellText?.Trim() ?? "";

                            // ID hücresi (genellikle ilk hücre)
                            if (i == 0 && cleanText.Length > 0)
                            {
                                itemId = cleanText;
                            }

                            // Durum hücresini ara
                            if (cleanText.Contains("bekleyen") || cleanText.Contains("onay bekliyor"))
                            {
                                status = cleanText;
                                break;
                            }
                        }

                        // Onay bekleyen öğe bulunduysa işle
                        if (!string.IsNullOrEmpty(status) && (status.ToLower().Contains("bekleyen") || status.ToLower().Contains("onay bekliyor")))
                        {
                            waitingCount++;
                            logCallback?.Invoke($"Onay bekleyen öğe bulundu: {itemId} - {status}");

                            try
                            {
                                // Bu satır için onay işlemini gerçekleştir
                                var approvalResult = await ApproveWaitingItemAsync(page, row, itemId, logCallback);
                                if (approvalResult)
                                {
                                    approvedCount++;
                                    logCallback?.Invoke($"✅ Öğe başarıyla onaylandı: {itemId}");
                                }
                                else
                                {
                                    logCallback?.Invoke($"❌ Öğe onaylanamadı: {itemId}");
                                }

                                // Kısa bekleme - sistemi yormamak için
                                await page.WaitForTimeoutAsync(1000);
                            }
                            catch (Exception ex)
                            {
                                logCallback?.Invoke($"Öğe onaylanırken hata ({itemId}): {ex.Message}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        logCallback?.Invoke($"Satır işlenirken hata: {ex.Message}");
                    }
                }

                logCallback?.Invoke($"Tarama tamamlandı: {processedCount} satır işlendi, {waitingCount} onay bekleyen bulundu, {approvedCount} öğe onaylandı.");
            }
            catch (Exception ex)
            {
                logCallback?.Invoke($"Onay bekleyen öğeler aranırken hata: {ex.Message}");
            }

            return approvedCount;
        }

        private async Task<bool> ApproveWaitingItemAsync(IPage page, IElementHandle row, string itemId, Action<string>? logCallback)
        {
            try
            {
                // Satır içindeki Onayla butonunu bul
                var approveButton = await row.QuerySelectorAsync("button:has-text('Onayla'), a:has-text('Onayla'), input[value='Onayla'], button[title*='Onayla']");

                if (approveButton != null)
                {
                    // Butonu vurgula
                    await page.EvaluateAsync("(button) => { button.style.border = '3px solid green'; button.style.backgroundColor = 'yellow'; }", approveButton);
                    await page.WaitForTimeoutAsync(500);

                    // Butona tıkla
                    await approveButton.ClickAsync();
                    await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                    await page.WaitForTimeoutAsync(2000);

                    logCallback?.Invoke($"Onayla butonu tıklandı: {itemId}");
                    return true;
                }
                else
                {
                    // Onayla butonu bulunamadıysa, İşlemler menüsünden ara
                    logCallback?.Invoke($"Onayla butonu bulunamadı, işlemler menüsünden aranıyor: {itemId}");

                    var processButton = await row.QuerySelectorAsync("button[title='İşlemler'], button.dropdown-toggle");
                    if (processButton != null)
                    {
                        await processButton.ClickAsync();
                        await page.WaitForTimeoutAsync(1000);

                        // Dropdown menüden Onayla seçeneğini bul
                        var approveOption = await page.QuerySelectorAsync(".dropdown-menu.show a:has-text('Onayla'), .dropdown-menu.show button:has-text('Onayla')");
                        if (approveOption != null)
                        {
                            await approveOption.ClickAsync();
                            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                            await page.WaitForTimeoutAsync(2000);

                            logCallback?.Invoke($"İşlemler menüsünden onaylandı: {itemId}");
                            return true;
                        }
                        else
                        {
                            logCallback?.Invoke($"Onayla seçeneği bulunamadı: {itemId}");
                        }
                    }
                    else
                    {
                        logCallback?.Invoke($"İşlemler butonu bulunamadı: {itemId}");
                    }
                }
            }
            catch (Exception ex)
            {
                logCallback?.Invoke($"Onay işlemi sırasında hata ({itemId}): {ex.Message}");
            }

            return false;
        }

        private async Task ProcessSingleDraftPageAsync(IPage page, string itemId, Action<string>? logCallback)
        {
            try
            {
                logCallback?.Invoke($"İşlemler menüsü aranıyor: {itemId}");
                
                // Doğru işlemler menüsü seçicisi - title="İşlemler" olan button
                var processButton = await page.QuerySelectorAsync("button[title='İşlemler'], button.dropdown-toggle[title='İşlemler']");
                if (processButton != null)
                {
                    await page.EvaluateAsync("(button) => { button.style.border = '3px solid blue'; }", processButton);
                    await page.WaitForTimeoutAsync(1000);
                    
                    await processButton.ClickAsync();
                    logCallback?.Invoke($"İşlemler menüsü açıldı: {itemId}");
                    await page.WaitForTimeoutAsync(1500);
                    
                    var sendApproveButton = await page.QuerySelectorAsync("a[href*='SendApprove'], a:has-text('Onaya Gönder')");
                    if (sendApproveButton != null)
                    {
                        await page.EvaluateAsync("(button) => { button.style.border = '3px solid red'; }", sendApproveButton);
                        await page.WaitForTimeoutAsync(1000);
                        
                        await sendApproveButton.ClickAsync();
                        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                        logCallback?.Invoke($"Onaya Gönder seçeneği tıklandı: {itemId}");
                        await page.WaitForTimeoutAsync(2000);
                        
                        // İkinci işlemler menüsü - aynı seçiciyi kullan
                        var processButton2 = await page.QuerySelectorAsync("button[title='İşlemler'], button.dropdown-toggle[title='İşlemler']");
                        if (processButton2 != null)
                        {
                            await page.EvaluateAsync("(button) => { button.style.border = '3px solid purple'; }", processButton2);
                            await page.WaitForTimeoutAsync(1000);
                            
                            await processButton2.ClickAsync();
                            await page.WaitForTimeoutAsync(2000);
                            
                            // Dropdown menünün açık olduğundan emin ol
                            await page.WaitForSelectorAsync(".dropdown-menu.show", new PageWaitForSelectorOptions { Timeout = 5000 });
                            
                            // Değerlendir butonunu bul - dropdown menüde ve href'inde ApproveReject geçen
                            var evaluateButton = await page.QuerySelectorAsync(".dropdown-menu.show a.dropdown-item[href*='ApproveReject'], .dropdown-menu.show a:has-text('Değerlendir')");
                            
                            // Eğer bulamazsa, dropdown menüdeki tüm linkleri kontrol et
                            if (evaluateButton == null)
                            {
                                var allDropdownItems = await page.QuerySelectorAllAsync(".dropdown-menu.show a.dropdown-item");
                                foreach (var item in allDropdownItems)
                                {
                                    var href = await item.GetAttributeAsync("href");
                                    var text = await item.TextContentAsync();
                                    logCallback?.Invoke($"Dropdown item bulundu: {text} - {href}");
                                    
                                    if (href?.Contains("ApproveReject") == true || text?.Contains("Değerlendir") == true)
                                    {
                                        evaluateButton = item;
                                        break;
                                    }
                                }
                            }
                            if (evaluateButton != null)
                            {
                                await page.EvaluateAsync("(button) => { button.style.border = '3px solid green'; }", evaluateButton);
                                await page.WaitForTimeoutAsync(1000);
                                
                                await evaluateButton.ClickAsync();
                                logCallback?.Invoke($"Değerlendir seçeneği tıklandı: {itemId}");
                                await page.WaitForTimeoutAsync(3000);
                                
                                // Modal açıldıktan sonra direkt Kaydet butonuna tıkla (Onayla zaten seçili)
                                var saveButton = await page.QuerySelectorAsync("button.btn-modal-submit, button[type='submit'], input[type='submit'], button:has-text('Kaydet')");
                                if (saveButton != null)
                                {
                                                                            await page.EvaluateAsync("(button) => { button.style.border = '3px solid purple'; }", saveButton);
                                    await page.WaitForTimeoutAsync(500);
                                    
                                    await saveButton.ClickAsync();
                                    await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                                    logCallback?.Invoke($"Öğe başarıyla onaylandı: {itemId}");
                                }
                                else
                                {
                                    logCallback?.Invoke($"Kaydet butonu bulunamadı: {itemId}");
                                }
                            }
                            else
                            {
                                logCallback?.Invoke($"Değerlendir seçeneği bulunamadı: {itemId}");
                            }
                        }
                        else
                        {
                            logCallback?.Invoke($"İkinci işlemler menüsü bulunamadı: {itemId}");
                        }
                    }
                    else
                    {
                        logCallback?.Invoke($"Onaya Gönder seçeneği bulunamadı: {itemId}");
                    }
                }
                else
                {
                    logCallback?.Invoke($"İşlemler menüsü bulunamadı: {itemId}");
                }
            }
            catch (Exception ex)
            {
                logCallback?.Invoke($"Öğe işlenirken hata ({itemId}): {ex.Message}");
            }
        }

        private string ExtractItemIdFromUrl(string url)
        {
            try
            {
                // Önce DownloadFile URL'lerinden ID çıkar
                var downloadMatch = System.Text.RegularExpressions.Regex.Match(url, @"/StaffPaymentOrder/DownloadFile/([a-f0-9-]+)");
                if (downloadMatch.Success)
                {
                    return downloadMatch.Groups[1].Value;
                }
                
                // Sonra Details URL'lerinden ID çıkar
                var detailsMatch = System.Text.RegularExpressions.Regex.Match(url, @"/StaffPaymentOrder/Details/([a-f0-9-]+)");
                if (detailsMatch.Success)
                {
                    return detailsMatch.Groups[1].Value;
                }
                
                return string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private string GetBaseUrl(string detailUrl)
        {
            try
            {
                // URL'den base URL'i çıkar
                var uri = new Uri(detailUrl, UriKind.RelativeOrAbsolute);
                if (uri.IsAbsoluteUri)
                {
                    return $"{uri.Scheme}://{uri.Authority}";
                }
                return string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        public class DraftItem
        {
            public string Id { get; set; } = "";
            public string DetailUrl { get; set; } = "";
            public IElementHandle? RowElement { get; set; }
        }

        /// <summary>
        /// "Yeni İşçi Ödeme Emri" butonuna tıklar
        /// </summary>
        private async Task ClickCreatePaymentOrderButtonAsync(IPage page, Action<string>? logCallback)
        {
            try
            {
                logCallback?.Invoke("'Yeni İşçi Ödeme Emri' butonu aranıyor...");
                
                // Butonu bul - farklı selector'ları dene
                var createButton = await page.QuerySelectorAsync("a.btn.btn-primary[href='/StaffPaymentOrder/Create'], a[href*='/StaffPaymentOrder/Create']");
                
                if (createButton == null)
                {
                    // JavaScript ile text içeren butonları ara ve tıkla
                    var found = await page.EvaluateAsync<bool>(@"
                        (function() {
                            // Tüm linkleri kontrol et
                            var links = document.querySelectorAll('a');
                            for (var i = 0; i < links.length; i++) {
                                var link = links[i];
                                if (link.textContent && link.textContent.includes('Yeni İşçi Ödeme Emri')) {
                                    link.click();
                                    return true;
                                }
                                if (link.textContent && link.textContent.includes('Create')) {
                                    link.click();
                                    return true;
                                }
                            }
                            
                            // Tüm butonları kontrol et
                            var buttons = document.querySelectorAll('button');
                            for (var i = 0; i < buttons.length; i++) {
                                var button = buttons[i];
                                if (button.textContent && button.textContent.includes('Yeni')) {
                                    button.click();
                                    return true;
                                }
                            }
                            
                            return false;
                        })();
                    ");
                    
                    if (found)
                    {
                        logCallback?.Invoke("✅ JavaScript ile buton bulundu ve tıklandı.");
                        
                        // Sayfanın yüklenmesini bekle
                        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                        await page.WaitForTimeoutAsync(1000);
                        
                        logCallback?.Invoke("✅ Sayfa yüklendi.");
                        
                        // Dönemleri filtrele ve modal'da göster
                        await ProcessPaymentOrderPeriodsAsync(page, logCallback);
                        return; // Metodu burada bitir
                    }
                }
                
                if (createButton != null)
                {
                    logCallback?.Invoke("'Yeni İşçi Ödeme Emri' butonu bulundu, tıklanıyor...");
                    
                    // Butona tıkla
                    await createButton.ClickAsync();
                    
                    // Sayfanın yüklenmesini bekle
                    await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                    await page.WaitForTimeoutAsync(1000);
                    
                    logCallback?.Invoke("✅ 'Yeni İşçi Ödeme Emri' butonuna tıklandı ve sayfa yüklendi.");
                    
                    // Dönemleri filtrele ve modal'da göster
                    await ProcessPaymentOrderPeriodsAsync(page, logCallback);
                }
                else
                {
                    logCallback?.Invoke("⚠️ 'Yeni İşçi Ödeme Emri' butonu bulunamadı. Manuel olarak tıklayın.");
                }
            }
            catch (Exception ex)
            {
                logCallback?.Invoke($"'Yeni İşçi Ödeme Emri' butonuna tıklarken hata: {ex.Message}");
            }
        }

        /// <summary>
        /// Ödeme emri dönemlerini işler - direkt HTML'den okur
        /// </summary>
        private async Task ProcessPaymentOrderPeriodsAsync(IPage page, Action<string>? logCallback)
        {
            try
            {
                logCallback?.Invoke("HTML'den dönemler okunuyor ve filtreleniyor...");
                
                // Direkt HTML'den dönem seçeneklerini oku
                logCallback?.Invoke("HTML'den dönem seçenekleri okunuyor...");
                
                // Direkt HTML'den dönem seçeneklerini oku
                logCallback?.Invoke("HTML'den dönem seçenekleri okunuyor...");
                
                // Önce sayfanın HTML'ini kontrol et
                var pageHtml = await page.ContentAsync();
                logCallback?.Invoke($"Sayfa HTML uzunluğu: {pageHtml.Length} karakter");
                
                // AgcServiceRecieptPeriodId elementinin varlığını kontrol et
                var selectElement = await page.QuerySelectorAsync("#AgcServiceRecieptPeriodId");
                if (selectElement == null)
                {
                    logCallback?.Invoke("❌ AgcServiceRecieptPeriodId elementi bulunamadı!");
                    return;
                }
                
                logCallback?.Invoke("✅ AgcServiceRecieptPeriodId elementi bulundu.");
                
                // HTML'i parse ederken 2024 geldiğinde dur
                logCallback?.Invoke("HTML'den option'ları okurken 2024 kontrolü yapılıyor...");
                
                var optionElements = new List<IElementHandle>();
                var found2024 = false;
                
                // HTML'den option'ları tek tek oku ve 2024 geldiğinde dur
                var allOptions = await selectElement.QuerySelectorAllAsync("option");
                logCallback?.Invoke($"HTML'de {allOptions.Count} option bulundu, 2025'leri arıyor...");
                
                for (int i = 0; i < allOptions.Count && !found2024; i++)
                {
                    try
                    {
                        var option = allOptions[i];
                    var value = await option.GetAttributeAsync("value");
                    var text = await option.InnerTextAsync();
                    
                    if (!string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(text))
                    {
                            var cleanText = text.Trim();
                            
                            // 2024 geldiği anda HTML okumayı da dur
                            if (cleanText.Contains("2024"))
                            {
                                logCallback?.Invoke($"🛑 HTML okuma durduruldu! 2024 dönemi bulundu: {cleanText} (Option {i + 1}/{allOptions.Count})");
                                found2024 = true;
                                break; // HTML okumayı da dur
                            }
                            
                            // Sadece 2025 dönemlerini listeye ekle
                            if (cleanText.Contains("2025"))
                            {
                                optionElements.Add(option);
                                logCallback?.Invoke($"✅ 2025 dönemi HTML'e eklendi: {cleanText}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        logCallback?.Invoke($"HTML okuma hatası (Option {i + 1}): {ex.Message}");
                    }
                }
                
                logCallback?.Invoke($"HTML okuma tamamlandı! {optionElements.Count} adet 2025 dönemi bulundu.");
                
                // Tüm option'ları logla (debug için)
                logCallback?.Invoke("🔍 Tüm option elementleri:");
                for (int i = 0; i < optionElements.Count; i++)
                {
                    try
                    {
                        var option = optionElements[i];
                    var value = await option.GetAttributeAsync("value");
                    var text = await option.InnerTextAsync();
                    
                    if (!string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(text))
                    {
                            logCallback?.Invoke($"  {i + 1}. value='{value}', text='{text}'");
                        }
                    }
                    catch (Exception ex)
                    {
                        logCallback?.Invoke($"Option {i + 1} okuma hatası: {ex.Message}");
                    }
                }
                
                var periodOptionsData = new List<object>();
                
                // optionElements zaten sadece 2025 dönemlerini içeriyor, direkt periodOptionsData'ya ekle
                logCallback?.Invoke($"HTML'den {optionElements.Count} adet 2025 dönemi bulundu, periodOptionsData'ya ekleniyor...");
                
                foreach (var option in optionElements)
                {
                    try
                    {
                        var value = await option.GetAttributeAsync("value");
                        var text = await option.InnerTextAsync();
                        
                        // Value'nun geçerli olduğundan emin ol
                        if (!string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(text) && value != "Create" && value != "0")
                        {
                            logCallback?.Invoke($"✅ Geçerli dönem bulundu: value='{value}', text='{text}'");
                            periodOptionsData.Add(new Dictionary<string, object>
                            {
                                ["value"] = value,
                                ["text"] = text
                            });
                        }
                        else
                        {
                            logCallback?.Invoke($"⚠️ Geçersiz dönem atlandı: value='{value}', text='{text}'");
                        }
                    }
                    catch (Exception ex)
                    {
                        logCallback?.Invoke($"Option işleme hatası: {ex.Message}");
                    }
                }
                
                logCallback?.Invoke($"✅ {periodOptionsData.Count} adet 2025 dönemi periodOptionsData'ya eklendi.");
                
                logCallback?.Invoke($"🛑 Okuma durduruldu! Toplam {periodOptionsData.Count} adet 2025 dönemi bulundu.");
                
                logCallback?.Invoke($"HTML'den {periodOptionsData.Count} dönem seçeneği okundu.");
                
                // İlk 20 dönem seçeneğini logla (debug için)
                logCallback?.Invoke("🔍 İlk 20 dönem seçeneği:");
                for (int i = 0; i < Math.Min(20, periodOptionsData.Count); i++)
                {
                    try
                    {
                        var optionDict = periodOptionsData[i] as IDictionary<string, object>;
                        if (optionDict != null)
                        {
                            var value = optionDict["value"]?.ToString() ?? "";
                            var text = optionDict["text"]?.ToString() ?? "";
                            logCallback?.Invoke($"  {i + 1}. '{text}' (value: {value})");
                        }
                    }
                    catch { }
                }
                
                // Son 10 dönem seçeneğini de logla
                logCallback?.Invoke("🔍 Son 10 dönem seçeneği:");
                var startIndex = Math.Max(0, periodOptionsData.Count - 10);
                for (int i = startIndex; i < periodOptionsData.Count; i++)
                {
                    try
                    {
                        var optionDict = periodOptionsData[i] as IDictionary<string, object>;
                        if (optionDict != null)
                        {
                            var value = optionDict["value"]?.ToString() ?? "";
                            var text = optionDict["text"]?.ToString() ?? "";
                            logCallback?.Invoke($"  {i + 1}. '{text}' (value: {value})");
                        }
                    }
                    catch { }
                }
                
                var filteredPeriods = new List<(string Value, string Text)>();
                
                // periodOptionsData zaten sadece 2025 dönemlerini içeriyor, direkt filteredPeriods'a ekle
                foreach (var optionData in periodOptionsData)
                {
                    try
                    {
                        // Dynamic object'ten değerleri al
                        var optionDict = optionData as IDictionary<string, object>;
                        if (optionDict != null)
                        {
                            var value = optionDict["value"]?.ToString() ?? "";
                            var text = optionDict["text"]?.ToString() ?? "";
                            
                            if (!string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(text))
                        {
                            filteredPeriods.Add((value, text));
                        }
                        }
                    }
                    catch (Exception ex)
                    {
                        logCallback?.Invoke($"Dönem seçeneği işleme hatası: {ex.Message}");
                    }
                }
                
                logCallback?.Invoke($"Toplam {filteredPeriods.Count} adet dönem bulundu.");
                
                if (filteredPeriods.Any())
                {
                    // Dönem seçim modal'ını göster ve seçilen dönemleri işle
                    await ShowPeriodSelectionModalAsync(page, filteredPeriods, logCallback);
                }
                else
                {
                    logCallback?.Invoke("⚠️ Hiçbir uygun dönem bulunamadı!");
                }
            }
            catch (Exception ex)
            {
                logCallback?.Invoke($"Dönem işleme hatası: {ex.Message}");
            }
        }

        /// <summary>
        /// Dönem seçim modal'ını gösterir ve seçilen dönemleri işler
        /// </summary>
        private async Task ShowPeriodSelectionModalAsync(IPage page, List<(string Value, string Text)> periods, Action<string>? logCallback)
        {
            try
            {
                logCallback?.Invoke("WPF dönem seçim modal'ı gösteriliyor...");
                
                // WPF modal'ını ana thread'de göster
                List<(string Value, string Text)> selectedPeriods = new List<(string Value, string Text)>();
                
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        // PeriodSelectionModal'ı oluştur ve göster
                        var modal = new PeriodSelectionModal(periods);
                        modal.Owner = Application.Current.MainWindow;
                        
                        // Modal açıldığında bip sesi çal
                        PlayNotificationSound();
                        
                        // Modal'ı öne getir (Show kullanmadan)
                        modal.Topmost = true;
                        modal.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                        
                        // Modal'ı modal olarak göster
                        var result = modal.ShowDialog();
                        
                        if (result == true)
                        {
                            selectedPeriods = modal.SelectedPeriods;
                        }
                    }
                    catch (Exception ex)
                    {
                        logCallback?.Invoke($"WPF modal hatası: {ex.Message}");
                    }
                });
                
                if (selectedPeriods.Any())
                {
                    logCallback?.Invoke($"Seçilen {selectedPeriods.Count} dönem işlenecek.");
                    
                    // Seçilen dönemleri object array'e çevir ve geçerliliğini kontrol et
                    var validPeriods = new List<object>();
                    
                    foreach (var period in selectedPeriods)
                    {
                        if (!string.IsNullOrEmpty(period.Value) && period.Value != "Create" && period.Value != "0")
                        {
                            logCallback?.Invoke($"✅ Geçerli dönem seçildi: {period.Text} (Value: {period.Value})");
                            validPeriods.Add(new Dictionary<string, object>
                            {
                                ["value"] = period.Value,
                                ["text"] = period.Text
                            });
                        }
                        else
                        {
                            logCallback?.Invoke($"❌ Geçersiz dönem seçimi atlandı: {period.Text} (Value: {period.Value})");
                        }
                    }
                    
                    if (validPeriods.Any())
                    {
                        var selectedPeriodsArray = validPeriods.ToArray();
                        logCallback?.Invoke($"Toplam {validPeriods.Count} geçerli dönem işlenecek.");
                
                // Seçilen dönemleri işle
                    await ProcessSelectedPeriodsAsync(page, selectedPeriodsArray, logCallback);
                    }
                    else
                    {
                        logCallback?.Invoke("⚠️ Hiçbir geçerli dönem seçilmedi!");
                    }
                }
                else
                {
                    logCallback?.Invoke("Hiçbir dönem seçilmedi, işlem iptal edildi.");
                }
                
            }
            catch (Exception ex)
            {
                logCallback?.Invoke($"Modal gösterme hatası: {ex.Message}");
            }
        }

        /// <summary>
        /// Dropdown seçimini güvenli şekilde yapar
        /// </summary>
        private async Task<bool> SafeDropdownSelectionAsync(IPage page, string periodValue, string periodText, Action<string>? logCallback)
        {
            try
            {
                logCallback?.Invoke($"🔍 Dropdown seçimi başlatılıyor: {periodText}");
                
                // Önce mevcut seçili değeri kontrol et
                var initialValue = await page.EvaluateAsync<string>("() => document.querySelector('#AgcServiceRecieptPeriodId').value");
                logCallback?.Invoke($"🔍 Başlangıçta seçili değer: {initialValue}");
                
                // JavaScript ile direkt dropdown seçimi yap - güvenli yöntem
                logCallback?.Invoke($"🔧 JavaScript ile dropdown seçimi yapılıyor...");
                
                var selectionResult = await page.EvaluateAsync<bool>($@"
                    (function() {{
                        var select = document.querySelector('#AgcServiceRecieptPeriodId');
                        if (!select) return false;
                        
                        // Önce mevcut değeri logla
                        console.log('Mevcut değer:', select.value);
                        
                        // Doğru option'ı bul
                        for (var i = 0; i < select.options.length; i++) {{
                            var option = select.options[i];
                            if (option.value === '{periodValue}' && option.value !== 'Create' && option.value !== '0') {{
                                // Option'ı seç
                                select.selectedIndex = i;
                                option.selected = true;
                                
                                // Change event'ini tetikle
                                var changeEvent = new Event('change', {{ bubbles: true }});
                                select.dispatchEvent(changeEvent);
                                
                                console.log('Seçilen değer:', select.value);
                                console.log('Seçilen metin:', option.text);
                                
                                return true;
                            }}
                        }}
                        
                        return false;
                    }})();
                ");
                
                if (selectionResult)
                {
                    logCallback?.Invoke($"✅ JavaScript ile dropdown seçimi başarılı");
                    await page.WaitForTimeoutAsync(1000);
                }
                else
                {
                    logCallback?.Invoke($"❌ JavaScript ile dropdown seçimi başarısız");
                    
                    // Fallback: Select2 yöntemi dene
                    try
                    {
                        await page.ClickAsync("#select2-AgcServiceRecieptPeriodId-container");
                        await page.WaitForTimeoutAsync(1000);
                        
                        var searchInput = await page.QuerySelectorAsync(".select2-search__field");
                            if (searchInput != null)
                            {
                            await searchInput.FillAsync(periodText);
                            await page.WaitForTimeoutAsync(1000);
                            await searchInput.PressAsync("Enter");
                            await page.WaitForTimeoutAsync(1000);
                        }
                    }
                    catch
                    {
                        logCallback?.Invoke($"❌ Select2 fallback da başarısız");
                        return false;
                    }
                }
                
                // Seçimi doğrula - HTML'deki mevcut değeri kontrol et
                var selectedValue = await page.EvaluateAsync<string>("() => document.querySelector('#AgcServiceRecieptPeriodId').value");
                logCallback?.Invoke($"🔍 HTML'de seçili değer: {selectedValue}");
                
                // Ek kontrol: Seçili option'ın metnini kontrol et
                var selectedOptionText = await page.EvaluateAsync<string>(@"
                    () => {
                        var select = document.querySelector('#AgcServiceRecieptPeriodId');
                        if (select && select.selectedIndex >= 0) {
                            return select.options[select.selectedIndex].text;
                        }
                        return '';
                    }
                ");
                logCallback?.Invoke($"🔍 Seçili option metni: {selectedOptionText}");
                
                var isValid = !string.IsNullOrEmpty(selectedValue) && 
                             selectedValue != "Create" && 
                             selectedValue != "0" &&
                             !string.IsNullOrEmpty(selectedOptionText) &&
                             !selectedOptionText.Contains("Create") &&
                             !selectedOptionText.Contains("Seçiniz");
                
                if (isValid)
                {
                    logCallback?.Invoke($"✅ Dropdown seçimi başarılı: {selectedValue} - {selectedOptionText}");
                            }
                            else
                            {
                    logCallback?.Invoke($"❌ Dropdown seçimi başarısız: {selectedValue} - {selectedOptionText}");
                    
                    // Hata durumunda dropdown'ı tekrar aç ve manuel seçim yap
                    if (string.IsNullOrEmpty(selectedValue) || selectedValue == "Create" || selectedValue == "0")
                    {
                        logCallback?.Invoke($"🔄 Dropdown'ı tekrar açıp manuel seçim yapılıyor...");
                        
                        // Dropdown'ı tekrar aç
                        await page.ClickAsync("#select2-AgcServiceRecieptPeriodId-container");
                        await page.WaitForTimeoutAsync(1000);
                        
                        // Tüm option'ları listele
                        var allOptions = await page.EvaluateAsync<string>(@"
                            () => {
                                var select = document.querySelector('#AgcServiceRecieptPeriodId');
                                var options = [];
                                if (select) {
                                    for (var i = 0; i < select.options.length; i++) {
                                        var option = select.options[i];
                                        if (option.value && option.value !== 'Create' && option.value !== '0') {
                                            options.push(option.value + ': ' + option.text);
                                        }
                                    }
                                }
                                return options.join('\n');
                            }
                        ");
                        
                        logCallback?.Invoke($"🔍 Mevcut geçerli option'lar:\n{allOptions}");
                    }
                }
                
                return isValid;
            }
            catch (Exception ex)
            {
                logCallback?.Invoke($"❌ Dropdown seçim hatası: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Seçilen dönemleri sırayla işler - mevcut Create sayfasında kalır
        /// </summary>
        private async Task ProcessSelectedPeriodsAsync(IPage page, object[] selectedPeriods, Action<string>? logCallback)
        {
            try
            {
                logCallback?.Invoke($"Seçilen {selectedPeriods.Length} dönem için işlem başlatılıyor...");
                logCallback?.Invoke($"🔍 Mevcut Create sayfasında kalıp dönemleri işliyoruz...");
                
                // Create URL'ini belirle
                var currentUrlForCreate = page.Url;
                string createUrl;
                if (!currentUrlForCreate.Contains("/Create", StringComparison.OrdinalIgnoreCase))
                {
                    if (currentUrlForCreate.Contains("/StaffPaymentOrder", StringComparison.OrdinalIgnoreCase))
                    {
                        var idx = currentUrlForCreate.IndexOf("/StaffPaymentOrder", StringComparison.OrdinalIgnoreCase);
                        var basePart = currentUrlForCreate.Substring(0, idx);
                        createUrl = basePart + "/StaffPaymentOrder/Create";
                    }
                    else if (currentUrlForCreate.Contains("/StaffAdvancePaymentOrder", StringComparison.OrdinalIgnoreCase))
                    {
                        var idx = currentUrlForCreate.IndexOf("/StaffAdvancePaymentOrder", StringComparison.OrdinalIgnoreCase);
                        var basePart = currentUrlForCreate.Substring(0, idx);
                        createUrl = basePart + "/StaffAdvancePaymentOrder/Create";
                    }
                    else
                    {
                        createUrl = currentUrlForCreate.TrimEnd('/') + "/Create";
                    }
                }
                else
                {
                    createUrl = currentUrlForCreate;
                }
                
                foreach (var periodData in selectedPeriods)
                {
                    try
                    {
                        // Dynamic object'ten değerleri al
                        var periodDict = periodData as IDictionary<string, object>;
                        if (periodDict == null) continue;
                        
                        var periodValue = periodDict["value"]?.ToString() ?? "";
                        var periodText = periodDict["text"]?.ToString() ?? "";
                        
                        // Value'nun geçerli olduğundan emin ol
                        if (string.IsNullOrEmpty(periodValue) || periodValue == "Create" || periodValue == "0")
                        {
                            logCallback?.Invoke($"❌ Geçersiz dönem value'su atlandı: {periodText} (Value: {periodValue})");
                            continue;
                        }
                        
                        logCallback?.Invoke($"🔄 Dönem işleniyor: {periodText} (Value: {periodValue})");
                        
                        // Her dönemden önce Create sayfasına kesin dönüş yap
                        try
                        {
                            if (!page.Url.Contains("/Create", StringComparison.OrdinalIgnoreCase))
                            {
                                logCallback?.Invoke($"↩️ Create sayfasına dönülüyor: {createUrl}");
                                await page.GotoAsync(createUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
                                await page.WaitForTimeoutAsync(500);
                            }
                        }
                        catch { }
                        
                        // Mevcut sayfada dropdown'ı temizle ve yeni değer seç
                        logCallback?.Invoke($"🔧 Dropdown temizleniyor ve yeni değer seçiliyor...");
                        
                        // Önce dropdown'ı temizle
                        await page.EvaluateAsync(@"
                            () => {
                                var select = document.querySelector('#AgcServiceRecieptPeriodId');
                                if (select) {
                                    select.selectedIndex = -1;
                                    select.value = '';
                                                // Change event'ini tetikle
                                    var changeEvent = new Event('change', { bubbles: true });
                                    select.dispatchEvent(changeEvent);
                                }
                            }
                        ");
                        
                        await page.WaitForTimeoutAsync(1000);
                        
                        // Güvenli dropdown seçimi yap
                        logCallback?.Invoke($"🔍 Güvenli dropdown seçimi yapılıyor: {periodText} (Value: {periodValue})");
                        
                        var dropdownSelected = await SafeDropdownSelectionAsync(page, periodValue, periodText, logCallback);
                        
                        if (!dropdownSelected)
                        {
                            logCallback?.Invoke($"❌ Dropdown seçimi başarısız! Dönem atlanıyor: {periodText}");
                            continue; // Bu dönemi atla
                        }
                        
                        // Dropdown seçiminden sonra HTML'deki mevcut değeri tekrar kontrol et
                        await page.WaitForTimeoutAsync(2000); // Daha uzun bekleme
                        var finalSelectedValue = await page.EvaluateAsync<string>("() => document.querySelector('#AgcServiceRecieptPeriodId').value");
                        logCallback?.Invoke($"🔍 Final kontrol - HTML'de seçili değer: {finalSelectedValue}");
                        
                        // Dropdown seçimini daha detaylı kontrol et
                        if (string.IsNullOrEmpty(finalSelectedValue) || finalSelectedValue == "Create" || finalSelectedValue == "0")
                        {
                            logCallback?.Invoke($"❌ Final kontrol başarısız! Seçilen değer: {finalSelectedValue}");
                            logCallback?.Invoke($"❌ Bu dönem atlanıyor: {periodText}");
                            continue; // Bu dönemi atla
                        }
                        
                        // Ek kontrol: Dropdown'da gerçekten seçili olan option'ı kontrol et
                        var selectedOptionText = await page.EvaluateAsync<string>(@"
                            () => {
                                var select = document.querySelector('#AgcServiceRecieptPeriodId');
                                if (select && select.selectedIndex >= 0) {
                                    return select.options[select.selectedIndex].text;
                                }
                                return '';
                            }
                        ");
                        
                        logCallback?.Invoke($"🔍 Seçili option metni: {selectedOptionText}");
                        
                        if (string.IsNullOrEmpty(selectedOptionText) || selectedOptionText.Contains("Create") || selectedOptionText.Contains("Seçiniz"))
                        {
                            logCallback?.Invoke($"❌ Seçili option metni geçersiz: {selectedOptionText}");
                            logCallback?.Invoke($"❌ Bu dönem atlanıyor: {periodText}");
                            continue; // Bu dönemi atla
                        }
                        
                        logCallback?.Invoke($"✅ Dönem seçimi doğrulandı: {periodText} (Value: {finalSelectedValue}, Text: {selectedOptionText})");
                        
                        // Dönem seçimi tamamlandı, KAYDET butonu için bekleniyor
                            logCallback?.Invoke($"⏳ Dönem seçimi tamamlandı, KAYDET butonu için bekleniyor...");
                        await page.WaitForTimeoutAsync(500);
                            
                            // KAYDET butonunu gerçek kullanıcı gibi bul ve tıkla
                            logCallback?.Invoke($"🔍 KAYDET butonu aranıyor...");
                            
                            // 1. Önce button.btn.btn-primary seçicisi ile dene
                        var saveButton = await page.QuerySelectorAsync("button.btn.btn-primary");
                            if (saveButton != null)
                            {
                            // Butona tıkla
                                await saveButton.ClickAsync();
                                logCallback?.Invoke($"💾 KAYDET butonuna tıklandı");
                            await page.WaitForTimeoutAsync(300);
                            }
                            else
                            {
                                // 2. Tüm butonları kontrol et
                            var allButtons = await page.QuerySelectorAllAsync("button");
                                bool buttonFound = false;
                                
                                foreach (var button in allButtons)
                                {
                                    var buttonText = await button.TextContentAsync();
                                    if (!string.IsNullOrEmpty(buttonText) && buttonText.ToUpper().Contains("KAYDET"))
                                    {
                                                                            // Butona tıkla
                                        await button.ClickAsync();
                                        logCallback?.Invoke($"💾 KAYDET butonuna tıklandı");
                                        await page.WaitForTimeoutAsync(300);
                                        buttonFound = true;
                                        break;
                                    }
                                }
                                
                                if (!buttonFound)
                                {
                                    // 3. Submit input'larını kontrol et
                                var submitInputs = await page.QuerySelectorAllAsync("input[type='submit']");
                                    foreach (var input in submitInputs)
                                    {
                                        var inputValue = await input.GetAttributeAsync("value");
                                        if (!string.IsNullOrEmpty(inputValue) && inputValue.ToUpper().Contains("KAYDET"))
                                        {
                                                                                    // Input'a tıkla
                                            await input.ClickAsync();
                                            logCallback?.Invoke($"💾 KAYDET butonuna tıklandı");
                                            await page.WaitForTimeoutAsync(300);
                                            buttonFound = true;
                                            break;
                                        }
                                    }
                                    
                                    if (!buttonFound)
                                    {
                                        logCallback?.Invoke($"⚠️ KAYDET butonu bulunamadı");
                                    }
                                }
                            }
                            
                        // KAYDET butonu tıklandı, işlem tamamlanıyor
                                logCallback?.Invoke("💾 KAYDET butonuna tıklandı");
                            
                            // İşlemin tamamlanmasını bekle
                        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                        await page.WaitForTimeoutAsync(700);
                        
                        // Sayfa yönlendirme kontrolü yap
                        var currentUrl = page.Url;
                        var hasRedirected = !currentUrl.Contains("/Create") && !currentUrl.Contains("Create");
                        
                        if (hasRedirected)
                        {
                            logCallback?.Invoke($"✅ Sayfa yönlendirildi: {currentUrl}");
                        }
                        else
                        {
                            logCallback?.Invoke($"⚠️ Sayfa yönlendirilmedi, hata olabilir: {currentUrl}");
                        }
                        
                        // Hata kontrolü yap
                        var pageContent = await page.ContentAsync();
                        var hasError = pageContent.Contains("The value 'Create' is not valid for Id") || 
                                     pageContent.Contains("not valid for Id") ||
                                     pageContent.Contains("error") ||
                                     pageContent.Contains("Error") ||
                                     pageContent.Contains("\"key\": \"Id\"") ||
                                     pageContent.Contains("\"value\": \"The value 'Create' is not valid for Id\"");
                        
                        // Console hatası kontrolü kaldırıldı; yalancı pozitifleri engelle
                        var hasConsoleError = false;
                        
                        if (hasError)
                        {
                            logCallback?.Invoke($"❌ HATA TESPİT EDİLDİ! Sayfa kapatılmıyor, hata bekleniyor...");
                            logCallback?.Invoke($"🔍 Hata detayları için sayfa açık bırakıldı: {periodText}");
                            
                            logCallback?.Invoke($"🔍 Sayfa içeriğinde hata bulundu");
                            
                            // Hata detaylarını daha spesifik kontrol et
                            if (pageContent.Contains("\"key\": \"Id\""))
                            {
                                logCallback?.Invoke($"🔍 HATA TÜRÜ: 'Id' key'i ile ilgili hata");
                            }
                            
                            if (pageContent.Contains("\"value\": \"The value 'Create' is not valid for Id\""))
                            {
                                logCallback?.Invoke($"🔍 HATA TÜRÜ: 'Create' değeri Id için geçersiz");
                                logCallback?.Invoke($"🔍 ÇÖZÜM: Dropdown'dan geçerli bir dönem seçilmeli");
                            }
                            
                            // Hata durumunda kısa bekleme
                            logCallback?.Invoke($"⏳ Hata nedeniyle 1 saniye bekleniyor...");
                            await page.WaitForTimeoutAsync(1000);
                            
                            // Hata durumunda dropdown'ı tekrar kontrol et
                            try
                            {
                                var currentDropdownValue = await page.EvaluateAsync<string>("() => document.querySelector('#AgcServiceRecieptPeriodId').value");
                                var currentDropdownText = await page.EvaluateAsync<string>(@"
                                    () => {
                                        var select = document.querySelector('#AgcServiceRecieptPeriodId');
                                        if (select && select.selectedIndex >= 0) {
                                            return select.options[select.selectedIndex].text;
                                        }
                                        return '';
                                    }
                                ");
                                
                                logCallback?.Invoke($"🔍 Hata sonrası dropdown durumu - Value: {currentDropdownValue}, Text: {currentDropdownText}");
                            }
                            catch { }
                            
                            // Hata durumunda bu dönemi atla ve diğerlerine devam et
                            logCallback?.Invoke($"❌ Hata nedeniyle bu dönem atlandı: {periodText}");
                            continue;
                        }
                        else
                        {
                            // Başarılı durumda sayfayı yenile ve bir sonraki dönem için hazırla
                            logCallback?.Invoke($"✅ Dönem kaydedildi: {periodText}");
                            
                            // Eğer son dönem değilse, sayfayı yenile ve devam et
                            var isLastPeriod = Array.IndexOf(selectedPeriods, periodData) == selectedPeriods.Length - 1;
                            
                            if (!isLastPeriod)
                            {
                                logCallback?.Invoke($"🔄 Sayfa yenileniyor ve bir sonraki dönem için hazırlanıyor...");
                                await page.ReloadAsync();
                                await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
                                await page.WaitForTimeoutAsync(3000);
                                logCallback?.Invoke($"✅ Sayfa yenilendi ve bir sonraki dönem için hazır");
                            }
                            else
                            {
                                logCallback?.Invoke($"🎉 Son dönem işlendi: {periodText}");
                            }
                        }
                        
                        // Bir sonraki dönem için kısa bekleme
                        await Task.Delay(1000);
                        
                    }
                    catch (Exception ex)
                    {
                        logCallback?.Invoke($"❌ Dönem işlenirken hata: {ex.Message}");
                        
                        // Hata türünü kontrol et
                        if (ex.Message.Contains("Create") || ex.Message.Contains("not valid for Id"))
                        {
                            logCallback?.Invoke("🔍 Bu hata dropdown seçimi ile ilgili olabilir.");
                            logCallback?.Invoke("🔍 Lütfen dönem seçimini kontrol edin.");
                            
                            // HTML'deki mevcut değeri kontrol et
                            try
                            {
                                var currentValue = await page.EvaluateAsync<string>("() => document.querySelector('#AgcServiceRecieptPeriodId').value");
                                logCallback?.Invoke($"🔍 Hata sırasında HTML'de seçili değer: {currentValue}");
                            }
                            catch { }
                            
                            // Hata durumunda bu dönemi atla
                            logCallback?.Invoke($"❌ Dropdown hatası nedeniyle dönem atlandı");
                            continue;
                        }
                        
                        continue; // Diğer hatalar için devam et
                    }
                }
                
                logCallback?.Invoke("🎉 Tüm dönemler işlendi!");
                
            }
            catch (Exception ex)
            {
                logCallback?.Invoke($"Dönem işleme genel hatası: {ex.Message}");
                
                // Hata türünü belirle
                if (ex.Message.Contains("Create") || ex.Message.Contains("not valid for Id"))
                {
                    logCallback?.Invoke("🔍 HATA TÜRÜ: Dropdown seçim hatası");
                    logCallback?.Invoke("🔍 ÇÖZÜM: Lütfen dönem seçimini kontrol edin ve geçerli değerler seçin.");
                    logCallback?.Invoke("🔍 DETAY: HTML'de 'Create' değeri seçili olmamalı, geçerli dönem ID'si seçilmeli.");
                }
                else if (ex.Message.Contains("timeout") || ex.Message.Contains("Timeout"))
                {
                    logCallback?.Invoke("🔍 HATA TÜRÜ: Zaman aşımı");
                    logCallback?.Invoke("🔍 ÇÖZÜM: İnternet bağlantınızı kontrol edin.");
                }
                else
                {
                    logCallback?.Invoke("🔍 HATA TÜRÜ: Genel hata");
                    logCallback?.Invoke("🔍 ÇÖZÜM: Lütfen tekrar deneyin.");
                }
            }
        }

        /// <summary>
        /// Ödeme emri oluşturma işlemini başlatır
        /// </summary>
        public async Task CreatePaymentOrdersAsync(
            string pageType,
            string username,
            string password,
            string companyCode,
            string totpSecret,
            AppConfig config,
            CancellationToken cancellationToken,
            Action<string, string, StatusType>? statusCallback = null,
            Action<string>? logCallback = null,
            Action<int, object?>? progressCallback = null,
            Action<int>? foundCallback = null,
            Action<int>? downloadedCallback = null,
            Action<decimal>? totalAmountCallback = null)
        {
            IBrowser? browser = null;
            
            try
            {
                statusCallback?.Invoke("Tarayıcı Başlatılıyor", "Chrome tarayıcısı başlatılıyor...", StatusType.Processing);
                logCallback?.Invoke("Chrome tarayıcısı başlatılıyor...");

                // Playwright'ı başlat
                var playwright = await Playwright.CreateAsync();
                
                // Config'den headless mod ayarını al
                var isHeadless = config.Sms.HeadlessMode;
                
                logCallback?.Invoke($"Gizli mod ayarı: {(isHeadless ? "Açık" : "Kapalı")}");
                
                // Browser'ı başlat
                browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Headless = isHeadless,
                    Args = new[] { 
                        "--disable-blink-features=AutomationControlled", 
                        "--disable-web-security", 
                        "--remote-debugging-port=9222",
                        "--disable-extensions",
                        "--disable-plugins",
                        "--disable-images",
                        "--disable-javascript",
                        "--disable-background-timer-throttling",
                        "--disable-backgrounding-occluded-windows",
                        "--disable-renderer-backgrounding"
                    }
                });

                var context = await browser.NewContextAsync(new BrowserNewContextOptions
                {
                    IgnoreHTTPSErrors = true,
                    BypassCSP = true
                });
                
                var page = await context.NewPageAsync();
                await page.SetViewportSizeAsync(1024, 768);
                
                logCallback?.Invoke($"Chrome tarayıcısı başarıyla başlatıldı. (Gizli mod: {(isHeadless ? "Açık" : "Kapalı")})");

                // Sayfa türüne göre doğru URL'yi oluştur
                string targetUrl;
                string pageTypeText;
                
                if (pageType == "advance")
                {
                    // Avans ödeme emri için sabit URL
                    targetUrl = "https://www.pinhuman.net/StaffAdvancePaymentOrder";
                    pageTypeText = "Avans Ödeme Emri";
                }
                else
                {
                    // Normal ödeme emri için sabit URL
                    targetUrl = "https://www.pinhuman.net/StaffPaymentOrder";
                    pageTypeText = "Normal Ödeme Emri";
                }

                // İndirme işlemindeki gibi login yap
                statusCallback?.Invoke("Login", "Login sayfası yükleniyor...", StatusType.Processing);
                logCallback?.Invoke("Login sayfası yükleniyor...");
                
                await page.GotoAsync(targetUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
                
                // Login işlemi - config'e göre otomatik veya manuel
                if (config.AutoLogin.Enabled)
                {
                    await PerformLoginAsync(page, username, password, companyCode, totpSecret, statusCallback, logCallback);
                }
                else
                {
                    statusCallback?.Invoke("Manuel Giriş", "Manuel giriş bekleniyor...", StatusType.Warning);
                    logCallback?.Invoke("Manuel giriş bekleniyor...");
                    
                    // Dıt sesi çal - kullanıcıya cevap vermesi gerektiğini bildir
                    PlayNotificationSound();
                    
                    // Manuel giriş için bekle - daha uzun süre
                    await Task.Delay(25000, cancellationToken);
                    
                    // Login başarısını kontrol et
                    await CheckLoginSuccessAsync(page, statusCallback, logCallback);
                }
                
                statusCallback?.Invoke($"{pageTypeText} Oluşturuluyor", $"{pageTypeText} sayfasına gidiliyor...", StatusType.Processing);
                logCallback?.Invoke($"{pageTypeText} sayfasına gidiliyor...");
                
                // Seçilen sayfa türüne git
                logCallback?.Invoke($"{pageTypeText} sayfasına gidiliyor...");
                
                await page.GotoAsync(targetUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
                logCallback?.Invoke($"{pageTypeText} sayfası yüklendi.");
                
                // Create sayfasına git
                var createUrl = $"{targetUrl}/Create";
                logCallback?.Invoke($"Create sayfasına gidiliyor: {createUrl}");
                await page.GotoAsync(createUrl, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
                logCallback?.Invoke("Create sayfası yüklendi.");
                
                // Sayfanın tamamen yüklenmesini bekle
                await page.WaitForTimeoutAsync(3000);
                logCallback?.Invoke("Sayfa tamamen yüklendi, dönemler aranıyor...");
                
                // HTML'den dönemleri oku
                logCallback?.Invoke("HTML'den dönemler okunuyor...");
                await ProcessPaymentOrderPeriodsAsync(page, logCallback);
                
                logCallback?.Invoke("✅ Ödeme emri oluşturma işlemi tamamlandı!");
                
            }
            catch (Exception ex)
            {
                logCallback?.Invoke($"Ödeme emri oluşturma sırasında hata: {ex.Message}");
                throw;
            }
            finally
            {
                try
                {
                    // Tarayıcıyı açık bırak, sadece playwright'ı dispose et
                    if (browser != null)
                    {
                        // Browser'ı kapatma, sadece playwright'ı dispose et
                        logCallback?.Invoke("🔍 Tarayıcı açık bırakıldı. Manuel işlem yapabilirsiniz.");
                    }
                }
                catch (Exception ex)
                {
                    logCallback?.Invoke($"Tarayıcı işlemi sırasında hata: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Sisteme giriş yapar
        /// </summary>
        private async Task<bool> LoginToSystemAsync(IPage page, string username, string password, string companyCode, string totpSecret, Action<string>? logCallback)
        {
            try
            {
                logCallback?.Invoke("Sisteme giriş yapılıyor...");
                
                // Load config for login credentials
                var config = ConfigManager.LoadConfig();
                
                await page.GotoAsync("https://www.pinhuman.net");
                await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
                
                // Sayfanın tamamen yüklenmesini bekle
                await page.WaitForTimeoutAsync(1000);

                // Login formunu doldur
                await FillLoginFormAsync(page, username, password, companyCode, logCallback);
                
                // Form doldurulduktan sonra biraz bekle
                await page.WaitForTimeoutAsync(500);
                
                // Login butonuna tıkla
                await ClickLoginButtonAsync(page, logCallback);
                
                // Form submit sonrası daha uzun bekle
                await page.WaitForTimeoutAsync(1000);
                
                // 2FA kontrolü ve TOTP kodu üretimi
                await Handle2FAWithTOTPAsync(page, totpSecret, logCallback);
                
                // 2FA sonrası bekle
                await page.WaitForTimeoutAsync(1000);
                
                // Login başarısını kontrol et
                var success = await CheckLoginSuccessAsync(page, logCallback);
                
                // Login sonrası 1 saniye bekle
                await page.WaitForTimeoutAsync(500);
                
                if (success)
                {
                    logCallback?.Invoke("✅ Sisteme başarıyla giriş yapıldı.");
                }
                else
                {
                    logCallback?.Invoke("⚠️ Login başarısı kontrol edilemedi, tekrar deneniyor...");
                    
                    // Tekrar kontrol et
                    await page.WaitForTimeoutAsync(1000);
                    success = await CheckLoginSuccessAsync(page, logCallback);
                    
                    if (success)
                    {
                        logCallback?.Invoke("✅ İkinci kontrol: Giriş başarılı!");
                    }
                }
                
                return success;
            }
            catch (Exception ex)
            {
                logCallback?.Invoke($"❌ Login sırasında hata: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Login formunu doldurur
        /// </summary>
        private async Task FillLoginFormAsync(IPage page, string username, string password, string companyCode, Action<string>? logCallback)
        {
            logCallback?.Invoke("Login formu dolduruluyor...");
            
            // Kullanıcı adı alanı
            var usernameField = await page.QuerySelectorAsync("#UserName");
            if (usernameField != null)
            {
                await usernameField.FillAsync(username);
                logCallback?.Invoke("Kullanıcı adı girildi.");
            }
            else
            {
                logCallback?.Invoke("Kullanıcı adı alanı bulunamadı!");
            }
            
            // Firma kodu alanı
            var companyCodeField = await page.QuerySelectorAsync("#CompanyCode");
            if (companyCodeField != null)
            {
                await companyCodeField.FillAsync(companyCode);
                logCallback?.Invoke("Firma kodu girildi.");
            }
            else
            {
                logCallback?.Invoke("Firma kodu alanı bulunamadı!");
            }
            
            // Şifre alanı
            var passwordField = await page.QuerySelectorAsync("#Password");
            if (passwordField != null)
            {
                await passwordField.FillAsync(password);
                logCallback?.Invoke("Şifre girildi.");
            }
            else
            {
                logCallback?.Invoke("Şifre alanı bulunamadı!");
            }
        }

        /// <summary>
        /// Login butonuna tıklar
        /// </summary>
        private async Task ClickLoginButtonAsync(IPage page, Action<string>? logCallback)
        {
            // GİRİŞ butonunu bul
            var loginButton = await page.QuerySelectorAsync("button.btn.btn-lg.btn-success.btn-block");
            
            if (loginButton != null)
            {
                // Butona tıklamadan önce biraz bekle
                await page.WaitForTimeoutAsync(2000);
                
                // Önce butonun görünür olduğundan emin ol
                await loginButton.WaitForElementStateAsync(ElementState.Visible);
                
                // JavaScript ile tıkla
                await page.EvaluateAsync(@"
                    const button = document.querySelector('button.btn.btn-lg.btn-success.btn-block');
                    if (button) {
                        button.click();
                    }
                ");
                
                // Form submit'i bekle
                await page.WaitForTimeoutAsync(2000);
                logCallback?.Invoke("Login butonuna tıklandı.");
            }
            else
            {
                logCallback?.Invoke("Login butonu bulunamadı! Manuel olarak giriş yapın...");
            }
        }

        /// <summary>
        /// 2FA işlemini yapar
        /// </summary>
        private async Task Handle2FAWithTOTPAsync(IPage page, string totpSecret, Action<string>? logCallback)
        {
            try
            {
                var twoFactorField = await page.WaitForSelectorAsync("#Code, input[name='code'], input[name='2fa'], input[name='otp'], input[placeholder*='code'], input[placeholder*='2fa'], input[placeholder*='OTP'], input[placeholder*='doğrulama'], input[placeholder*='verification']", new PageWaitForSelectorOptions { Timeout = 3000 });
                
                if (twoFactorField != null)
                {
                    string twoFactorCode;
                    
                    if (!string.IsNullOrEmpty(totpSecret))
                    {
                        // TOTP kodu üret
                        twoFactorCode = GenerateTOTPCode(totpSecret);
                        logCallback?.Invoke("TOTP kodu üretildi.");
                    }
                    else
                    {
                        // Manuel kod girişi
                        logCallback?.Invoke("2FA kodu manuel olarak girilmeli.");
                        return;
                    }
                    
                    if (!string.IsNullOrEmpty(twoFactorCode))
                    {
                        // Kodu temizle ve gir
                        await twoFactorField.FillAsync("");
                        await twoFactorField.FillAsync(twoFactorCode);
                        logCallback?.Invoke("2FA kodu girildi.");
                        
                        // Biraz bekle
                        await page.WaitForTimeoutAsync(500);
                        
                        // 2FA submit butonunu bul ve tıkla
                        var submitButton = await page.QuerySelectorAsync("button.btn.btn-lg.btn-success.btn-block, button[type='submit'], input[type='submit']");
                        if (submitButton != null)
                        {
                            // JavaScript ile tıkla
                            await page.EvaluateAsync(@"
                                const button = document.querySelector('button.btn.btn-lg.btn-success.btn-block, button[type=""submit""]');
                                if (button) {
                                    button.click();
                                }
                            ");
                            
                            // Submit sonrası bekle
                            await page.WaitForTimeoutAsync(1000);
                            logCallback?.Invoke("2FA submit butonuna tıklandı.");
                        }
                        else
                        {
                            logCallback?.Invoke("2FA submit butonu bulunamadı. Manuel olarak doğrulayın...");
                        }
                    }
                }
                else
                {
                    logCallback?.Invoke("2FA alanı bulunamadı, 2FA gerekmiyor olabilir.");
                }
            }
            catch (Exception ex)
            {
                logCallback?.Invoke($"2FA işlemi sırasında hata: {ex.Message}");
            }
        }

        /// <summary>
        /// Login başarısını kontrol eder
        /// </summary>
        private async Task<bool> CheckLoginSuccessAsync(IPage page, Action<string>? logCallback)
        {
            try
            {
                logCallback?.Invoke("Login başarısı kontrol ediliyor...");
                
                // Sayfanın yüklenmesini bekle
                await page.WaitForTimeoutAsync(500);
                
                // Login başarısını kontrol et - dashboard veya ana sayfa elementlerini ara
                var successIndicator = await page.QuerySelectorAsync(".dashboard, .main-content, .user-info, .logout, [href*='logout'], .navbar, .header, .sidebar");
                
                if (successIndicator != null)
                {
                    logCallback?.Invoke("✅ Login başarılı - dashboard bulundu.");
                    return true;
                }
                else
                {
                    // URL'yi kontrol et
                    var currentUrl = page.Url;
                    if (!currentUrl.Contains("login") && !currentUrl.Contains("Login") && !currentUrl.Contains("Account"))
                    {
                        logCallback?.Invoke("✅ Login başarılı - URL login sayfasında değil.");
                        return true;
                    }
                    else
                    {
                        // Sayfa içeriğini kontrol et
                        var pageContent = await page.ContentAsync();
                        var hasLoginForm = pageContent.Contains("UserName") || pageContent.Contains("Password") || pageContent.Contains("GİRİŞ");
                        
                        if (!hasLoginForm)
                        {
                            logCallback?.Invoke("✅ Login başarılı - login formu bulunamadı.");
                            return true;
                        }
                        else
                        {
                            logCallback?.Invoke("⚠️ Login durumu belirsiz, login formu hala mevcut.");
                            return false;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logCallback?.Invoke($"❌ Login kontrolü sırasında hata: {ex.Message}");
                return false;
            }
        }

    }
} 
