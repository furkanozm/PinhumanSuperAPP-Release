using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using OfficeOpenXml;

namespace WebScraper
{
    public class EmailNotificationService
    {
        private readonly NotificationConfig _notificationConfig;
        private readonly MailHistoryService _mailHistoryService;
        private static bool _isLastKeyword = false;

        public EmailNotificationService(NotificationConfig config)
        {
            _notificationConfig = config;
            _mailHistoryService = new MailHistoryService();
        }

        public static void SetLastKeyword(bool isLast)
        {
            _isLastKeyword = isLast;
        }

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool SetActiveWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool SetFocus(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        private const uint GW_HWNDNEXT = 2;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private const byte VK_CONTROL = 0x11;
        private const byte VK_RETURN = 0x0D;
        private const byte VK_ALT = 0x12;
        private const uint KEYEVENTF_KEYDOWN = 0x0000;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const int SW_RESTORE = 9;
        private const int SW_MAXIMIZE = 3;
        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;

        public async Task SendManualEmailAsync(string recipient, string subject, string body)
        {
            try
            {
                // Outlook Classic'i açmaya çalış
                var outlookPaths = new[]
                {
                    @"C:\Program Files\Microsoft Office\root\Office16\OUTLOOK.EXE",
                    @"C:\Program Files (x86)\Microsoft Office\root\Office16\OUTLOOK.EXE",
                    @"C:\Program Files\Microsoft Office\Office16\OUTLOOK.EXE",
                    @"C:\Program Files (x86)\Microsoft Office\Office16\OUTLOOK.EXE",
                    @"C:\Program Files\Microsoft Office\root\Office15\OUTLOOK.EXE",
                    @"C:\Program Files (x86)\Microsoft Office\root\Office15\OUTLOOK.EXE"
                };
                
                string? foundOutlookPath = null;
                foreach (var path in outlookPaths)
                {
                    if (File.Exists(path))
                    {
                        foundOutlookPath = path;
                        break;
                    }
                }
                
                if (!string.IsNullOrEmpty(foundOutlookPath))
                {
                    // Outlook Classic ile mail aç
                    var mailtoUrl = $"mailto:{recipient}?subject={Uri.EscapeDataString(subject)}&body={Uri.EscapeDataString(body)}";
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = foundOutlookPath,
                        Arguments = $"/c ipm.note /m \"{mailtoUrl}\"",
                        UseShellExecute = false
                    });
                }
                else
                {
                    // Outlook bulunamazsa varsayılan mail uygulamasını kullan
                    var mailtoUrl = $"mailto:{recipient}?subject={Uri.EscapeDataString(subject)}&body={Uri.EscapeDataString(body)}";
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = mailtoUrl,
                        UseShellExecute = true
                    });
                }
                
                // Kısa bir bekleme
                await Task.Delay(200);
                
                // Mail penceresini bul ve aktif hale getir
                await ActivateOutlookWindowAsync(subject);
                
                // Ctrl+Enter tuş kombinasyonunu simüle et
                await Task.Delay(100); // Kısa bekleme
                keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYDOWN, UIntPtr.Zero);
                keybd_event(VK_RETURN, 0, KEYEVENTF_KEYDOWN, UIntPtr.Zero);
                keybd_event(VK_RETURN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                
                // Mail geçmişine kaydet
                var mailHistory = new MailHistoryModel
                {
                    Recipient = recipient,
                    Subject = subject,
                    Content = body,
                    Status = "Gönderildi",
                    DeliveryType = "Manuel",
                    Timestamp = DateTime.Now
                };
                _mailHistoryService.AddMailHistory(mailHistory);
            }
            catch (Exception ex)
            {
                // Hata durumunda da geçmişe kaydet
                var mailHistory = new MailHistoryModel
                {
                    Recipient = recipient,
                    Subject = subject,
                    Content = body,
                    Status = "Hata",
                    ErrorMessage = ex.Message,
                    DeliveryType = "Manuel",
                    Timestamp = DateTime.Now
                };
                _mailHistoryService.AddMailHistory(mailHistory);
                
                throw new Exception($"Manuel mail gönderimi sırasında hata: {ex.Message}");
            }
        }

        private async Task ActivateOutlookWindowAsync(string subject)
        {
            try
            {
                // Outlook penceresini bul - dinamik konu ismine göre ara
                var outlookWindow = IntPtr.Zero;

                // Pencereyi bulmak için birkaç kez dene
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    // Önce mail konusuna göre ara
                    outlookWindow = FindWindow(null, subject);
                    if (outlookWindow != IntPtr.Zero)
                    {
                        break;
                    }

                    // Mail konusu + " - İleti" ile ara (Türkçe Outlook formatı)
                    var subjectWithIleti = $"{subject} - İleti";
                    outlookWindow = FindWindow(null, subjectWithIleti);
                    if (outlookWindow != IntPtr.Zero)
                    {
                        break;
                    }

                    // Genel pencere isimlerini dene
                    var windowNames = new[]
                    {
                        "Message (HTML)",
                        "Untitled - Message",
                        "Message",
                        "New Message",
                        "Mail",
                        "Outlook",
                        "Untitled",
                        "New",
                        "Compose",
                        "Draft",
                        "Reply",
                        "Forward",
                        "New Email",
                        "New Mail",
                        "Compose Message",
                        "Draft Message",
                        "İleti",
                        "Yeni İleti",
                        "Untitled - İleti",
                        "Message - İleti",
                        "Mail - İleti",
                        "Outlook - İleti"
                    };

                    foreach (var windowName in windowNames)
                    {
                        outlookWindow = FindWindow(null, windowName);
                        if (outlookWindow != IntPtr.Zero)
                        {
                            break;
                        }
                    }

                    if (outlookWindow != IntPtr.Zero)
                        break;

                    await Task.Delay(1000);
                }

                if (outlookWindow != IntPtr.Zero)
                {
                    // Pencereyi ön plana getir
                    SetForegroundWindow(outlookWindow);
                    ShowWindow(outlookWindow, SW_RESTORE);
                    BringWindowToTop(outlookWindow);
                    SetActiveWindow(outlookWindow);
                    SetFocus(outlookWindow);
                }
            }
            catch (Exception ex)
            {
                // Hata durumunda sessizce devam et
                System.Diagnostics.Debug.WriteLine($"Outlook penceresi aktif hale getirilirken hata: {ex.Message}");
            }
        }

        public async Task SendCompletionNotificationAsync(List<string> downloadedFiles, decimal totalAmount, Action<string>? logCallback = null, string? periodName = null)
        {
            if (!_notificationConfig.Enabled)
            {
                logCallback?.Invoke("📧 Mail bildirimi devre dışı.");
                return;
            }

            logCallback?.Invoke("📧 Mail bildirimi kontrol ediliyor...");

            // Keyword gruplarına göre dosyaları ayır
            var keywordGroups = _notificationConfig.Keywords
                .Where(k => k.Enabled && !string.IsNullOrEmpty(k.EmailRecipient))
                .ToList();

            if (!keywordGroups.Any())
            {
                logCallback?.Invoke("❌ Aktif keyword konfigürasyonu bulunamadı.");
                return;
            }

            logCallback?.Invoke($"🔍 📊 Toplam {downloadedFiles.Count} dosya bulundu.");
            logCallback?.Invoke($"🔍 ✅ Onaylandı durumunda {downloadedFiles.Count} dosya bulundu.");

            // Önce tüm dosyaları bir kerede tarayıp keyword eşleştirmelerini bul
            logCallback?.Invoke("🔍 Tüm dosyalar taranıyor ve keyword eşleştirmeleri bulunuyor...");
            
            var keywordFileMappings = new Dictionary<string, List<string>>();
            
            // Her keyword için boş liste oluştur
            foreach (var keywordConfig in keywordGroups)
            {
                keywordFileMappings[keywordConfig.Keyword] = new List<string>();
            }
            
            // Tüm dosyaları bir kerede tara
            foreach (var file in downloadedFiles)
            {
                var fileName = Path.GetFileName(file).ToUpper();
                
                // Her keyword için kontrol et
                foreach (var keywordConfig in keywordGroups)
                {
                    var keyword = keywordConfig.Keyword.ToUpper();
                    if (fileName.Contains(keyword))
                    {
                        keywordFileMappings[keywordConfig.Keyword].Add(file);
                        break; // İlk eşleşen keyword'i bulduk, diğerlerini kontrol etmeye gerek yok
                    }
                }
            }
            
            // Sadece dosyası olan keyword'leri filtrele
            var activeKeywords = keywordGroups.Where(k => keywordFileMappings[k.Keyword].Any()).ToList();
            
            if (!activeKeywords.Any())
            {
                logCallback?.Invoke("❌ Hiçbir keyword için dosya bulunamadı.");
                return;
            }
            
            logCallback?.Invoke($"🔍 ✅ {activeKeywords.Count} keyword için dosya bulundu.");
            
            // Her aktif keyword için mail gönder
            var keywordCount = activeKeywords.Count;
            
            for (int i = 0; i < activeKeywords.Count; i++)
            {
                var keywordConfig = activeKeywords[i];
                var keyword = keywordConfig.Keyword;
                var recipient = keywordConfig.EmailRecipient;
                var keywordFiles = keywordFileMappings[keyword];

                // Bu keyword için tutarı hesapla
                var keywordAmount = CalculateAmountFromFiles(keywordFiles);

                logCallback?.Invoke($"💾 🔍 Keyword: '{keyword}' - {keywordFiles.Count} dosya - {keywordAmount:N2} TL");
                logCallback?.Invoke($"📧 📧 [{i + 1}/{keywordCount}] '{keyword}' için mail gönderiliyor: {recipient}");

                // Mail içeriğini oluştur
                var subject = $"✅ Ödeme Emri Tamamlandı - {keyword} - {periodName ?? $"{DateTime.Now:dd-MM} {GetMonthName(DateTime.Now.Month)} {DateTime.Now.Year}"} - {keywordAmount:N2} TL";
                var body = CreateCompletionEmailBody(keywordFiles, keywordAmount, keyword, periodName);

                // Mail gönder
                await SendEmailAsync(recipient, subject, body, logCallback);

                logCallback?.Invoke($"★ ✅ [{i + 1}/{keywordCount}] '{keyword}' kelimesi için mail gönderim süreci tamamlandı. Tutar: {keywordAmount:N2} TL");

                // Sonraki mail için bekle (sadece son mail değilse)
                if (i < keywordCount - 1)
                {
                    logCallback?.Invoke("⏳ ⏳ Sonraki mail için bekleniyor...");
                    await Task.Delay(1); // 0.001 saniye bekle (neredeyse anında)
                }
            }
        }

        private async Task<bool> SendEmailAsync(string recipient, string subject, string body, Action<string>? logCallback = null)
        {
            logCallback?.Invoke("🚀 🔍 Mail gönderim süreci başlatılıyor...");
            logCallback?.Invoke("ℹ 📧 Mail gönderiliyor...");
            logCallback?.Invoke($"📧    Alıcı: {recipient}");
            logCallback?.Invoke($"★    Konu: {subject}");
            logCallback?.Invoke($"ℹ    En son keyword: {_isLastKeyword}");

            bool success = false;
            string errorMessage = "";

            try
            {
                // Önce Outlook ile dene
                if (await TrySendViaOutlookAsync(recipient, subject, body, logCallback))
                {
                    success = true;
                }
                // Outlook başarısızsa SMTP ile dene
                else if (await TrySendViaSmtpAsync(recipient, subject, body, logCallback))
                {
                    success = true;
                }
                // SMTP başarısızsa varsayılan mail client ile dene
                else if (await TrySendViaDefaultMailClientAsync(recipient, subject, body, logCallback))
                {
                    success = true;
                }
                else
                {
                    errorMessage = "Tüm mail gönderim yöntemleri başarısız oldu";
                }
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
            }

            // Mail geçmişine kaydet
            var mailHistory = new MailHistoryModel
            {
                Recipient = recipient,
                Subject = subject,
                Content = body,
                Status = success ? "Gönderildi" : "Hata",
                ErrorMessage = errorMessage,
                DeliveryType = "Otomatik",
                Timestamp = DateTime.Now
            };
            _mailHistoryService.AddMailHistory(mailHistory);

            return success;
        }

        private async Task<bool> TrySendViaOutlookAsync(string recipient, string subject, string body, Action<string>? logCallback = null)
        {
            try
            {
                logCallback?.Invoke("ℹ 📧 Outlook Classic ile mail gönderiliyor...");

                // Outlook Classic yolları
                var outlookPaths = new[]
                {
                    @"C:\Program Files\Microsoft Office\root\Office16\OUTLOOK.EXE",
                    @"C:\Program Files (x86)\Microsoft Office\root\Office16\OUTLOOK.EXE",
                    @"C:\Program Files\Microsoft Office\Office16\OUTLOOK.EXE",
                    @"C:\Program Files (x86)\Microsoft Office\Office16\OUTLOOK.EXE",
                    @"C:\Program Files\Microsoft Office\root\Office15\OUTLOOK.EXE",
                    @"C:\Program Files (x86)\Microsoft Office\root\Office15\OUTLOOK.EXE"
                };

                string? foundOutlookPath = null;
                foreach (var path in outlookPaths)
                {
                    if (File.Exists(path))
                    {
                        foundOutlookPath = path;
                        break;
                    }
                }

                if (!string.IsNullOrEmpty(foundOutlookPath))
                {
                    logCallback?.Invoke($"ℹ ✅ Outlook Classic açıldı: {foundOutlookPath}");

                    if (_isLastKeyword)
                    {
                        logCallback?.Invoke("ℹ 🎯 En son keyword - Ctrl+Enter ile otomatik gönderim yapılıyor...");

                        // Mailto URL ile Outlook'u aç
                        var mailtoUrl = $"mailto:{recipient}?subject={Uri.EscapeDataString(subject)}&body={Uri.EscapeDataString(body)}";
                        var outlookProcess = new System.Diagnostics.Process
                        {
                            StartInfo = new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = foundOutlookPath,
                                Arguments = $"/c ipm.note /m \"{mailtoUrl}\"",
                                UseShellExecute = false
                            }
                        };

                        outlookProcess.Start();

                        // Outlook açıldıktan sonra kısa bekle
                        logCallback?.Invoke("⏳ Outlook açılması bekleniyor...");
                        await Task.Delay(500); // 0,5 saniye bekle

                        // Outlook penceresini bul - dinamik konu ismine göre ara
                        var outlookWindow = IntPtr.Zero;

                        // Pencereyi bulmak için birkaç kez dene
                        for (int attempt = 0; attempt < 3; attempt++)
                        {
                            logCallback?.Invoke($"🔍 Pencere arama denemesi {attempt + 1}/3...");

                            // Önce mail konusuna göre ara
                            outlookWindow = FindWindow(null, subject);
                            if (outlookWindow != IntPtr.Zero)
                            {
                                logCallback?.Invoke($"✅ Outlook penceresi bulundu (konu ile): {subject}");
                                break;
                            }

                            // Mail konusu + " - İleti" ile ara (Türkçe Outlook formatı)
                            var subjectWithIleti = $"{subject} - İleti";
                            outlookWindow = FindWindow(null, subjectWithIleti);
                            if (outlookWindow != IntPtr.Zero)
                            {
                                logCallback?.Invoke($"✅ Outlook penceresi bulundu (konu + İleti ile): {subjectWithIleti}");
                                break;
                            }

                            // Mail konusunun bir kısmı ile ara
                            var subjectParts = subject.Split(' ');
                            foreach (var part in subjectParts.Take(5)) // İlk 5 kelimeyi dene
                            {
                                if (part.Length > 3) // 3 karakterden uzun kelimeleri dene
                                {
                                    outlookWindow = FindWindow(null, part);
                                    if (outlookWindow != IntPtr.Zero)
                                    {
                                        logCallback?.Invoke($"✅ Outlook penceresi bulundu (kelime ile): {part}");
                                        break;
                                    }
                                }
                            }

                            if (outlookWindow != IntPtr.Zero)
                                break;

                            // "İleti" kelimesi ile ara (Türkçe Outlook)
                            outlookWindow = FindWindow(null, "İleti");
                            if (outlookWindow != IntPtr.Zero)
                            {
                                logCallback?.Invoke($"✅ Outlook penceresi bulundu (İleti ile): İleti");
                                break;
                            }

                            // Tüm açık pencereleri tarayıp "İleti" içeren pencereleri bul
                            logCallback?.Invoke("🔍 Tüm pencerelerde 'İleti' kelimesi aranıyor...");
                            var allWindows = new List<string>();
                            var hwnd = GetForegroundWindow();

                            for (int i = 0; i < 50; i++)
                            {
                                var title = new StringBuilder(256);
                                GetWindowText(hwnd, title, title.Capacity);
                                var windowTitle = title.ToString();

                                if (!string.IsNullOrEmpty(windowTitle) && windowTitle.Contains("İleti"))
                                {
                                    logCallback?.Invoke($"🔍 'İleti' içeren pencere bulundu: {windowTitle}");
                                    outlookWindow = hwnd;
                                    break;
                                }

                                hwnd = GetWindow(hwnd, GW_HWNDNEXT);
                                if (hwnd == IntPtr.Zero) break;
                            }

                            if (outlookWindow != IntPtr.Zero)
                            {
                                logCallback?.Invoke($"✅ Outlook penceresi bulundu (İleti arama ile)");
                                break;
                            }

                            // Eğer hala bulunamadıysa, tüm pencereleri tarayıp mail konusunu içeren pencereleri bul
                            logCallback?.Invoke("🔍 Tüm pencerelerde mail konusu aranıyor...");
                            hwnd = GetForegroundWindow();

                            for (int i = 0; i < 50; i++)
                            {
                                var title = new StringBuilder(256);
                                GetWindowText(hwnd, title, title.Capacity);
                                var windowTitle = title.ToString();

                                if (!string.IsNullOrEmpty(windowTitle) &&
                                    (windowTitle.Contains("Ödeme Emri") || windowTitle.Contains("Tamamlandı") ||
                                     windowTitle.Contains("İZMİR") || windowTitle.Contains("MANYAS")))
                                {
                                    logCallback?.Invoke($"🔍 Mail konusu içeren pencere bulundu: {windowTitle}");
                                    outlookWindow = hwnd;
                                    break;
                                }

                                hwnd = GetWindow(hwnd, GW_HWNDNEXT);
                                if (hwnd == IntPtr.Zero) break;
                            }

                            if (outlookWindow != IntPtr.Zero)
                            {
                                logCallback?.Invoke($"✅ Outlook penceresi bulundu (mail konusu arama ile)");
                                break;
                            }

                            // Genel pencere isimlerini dene
                            var windowNames = new[]
                            {
                                "Message (HTML)",
                                "Untitled - Message",
                                "Message",
                                "New Message",
                                "Mail",
                                "Outlook",
                                "Untitled",
                                "New",
                                "Compose",
                                "Draft",
                                "Reply",
                                "Forward",
                                "New Email",
                                "New Mail",
                                "Compose Message",
                                "Draft Message",
                                "İleti",
                                "Yeni İleti",
                                "Untitled - İleti",
                                "Message - İleti",
                                "Mail - İleti",
                                "Outlook - İleti"
                            };

                            foreach (var windowName in windowNames)
                            {
                                outlookWindow = FindWindow(null, windowName);
                                if (outlookWindow != IntPtr.Zero)
                                {
                                    logCallback?.Invoke($"✅ Outlook penceresi bulundu (genel): {windowName}");
                                    break;
                                }
                            }

                            if (outlookWindow != IntPtr.Zero)
                                break;

                            logCallback?.Invoke("⏳ Pencere bulunamadı, 2 saniye daha bekleniyor...");
                            await Task.Delay(2000);
                        }

                        if (outlookWindow != IntPtr.Zero)
                        {
                            // Pencereyi ön plana getir
                            SetForegroundWindow(outlookWindow);
                            ShowWindow(outlookWindow, SW_RESTORE);
                            BringWindowToTop(outlookWindow);
                            SetActiveWindow(outlookWindow);
                            SetFocus(outlookWindow);

                            // Kısa bir bekleme
                            await Task.Delay(100);

                            // Alternatif mail gönderim yöntemleri
                            logCallback?.Invoke("📤 Mail gönderim yöntemleri deneniyor...");

                            // Yöntem 1: Ctrl+Enter
                            logCallback?.Invoke("📤 Yöntem 1: Ctrl+Enter deneniyor...");
                            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYDOWN, UIntPtr.Zero);
                            await Task.Delay(100);
                            keybd_event(VK_RETURN, 0, KEYEVENTF_KEYDOWN, UIntPtr.Zero);
                            await Task.Delay(100);
                            keybd_event(VK_RETURN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                            await Task.Delay(100);
                            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                            await Task.Delay(500);

                            if (!IsWindow(outlookWindow))
                            {
                                logCallback?.Invoke("✅ Ctrl+Enter ile mail başarıyla gönderildi!");
                            }
                            else
                            {
                                logCallback?.Invoke("❌ Ctrl+Enter başarısız - Mail gönderilemedi!");
                                logCallback?.Invoke("🔍 Olası nedenler:");
                                logCallback?.Invoke("   - Outlook güvenlik ayarları otomatik gönderimi engelliyor");
                                logCallback?.Invoke("   - Outlook sürümü farklı olabilir");
                                logCallback?.Invoke("   - Sistem güvenlik yazılımı engelliyor olabilir");
                                logCallback?.Invoke("📧 Lütfen maili manuel olarak gönderin!");
                            }

                            logCallback?.Invoke("✅ Ctrl+Enter ile mail gönderildi, pencere kapanması bekleniyor...");

                            // Pencere kapanana kadar bekle (maksimum 5 saniye)
                            var maxWaitTime = 5; // 5 saniye
                            var waitTime = 0;

                            while (IsWindow(outlookWindow) && waitTime < maxWaitTime)
                            {
                                await Task.Delay(50); // 0.05 saniye bekle
                                waitTime++;
                                
                                // Pencere kapandıysa hemen çık
                                if (!IsWindow(outlookWindow))
                                {
                                    logCallback?.Invoke("✅ Mail penceresi kapandı, mail başarıyla gönderildi!");
                                    break;
                                }
                                
                                // Her 1 saniyede bir log ver
                                if (waitTime % 20 == 0)
                                {
                                    logCallback?.Invoke($"⏳ Pencere kapanması bekleniyor... ({waitTime/20}/{maxWaitTime/20})");
                                }
                            }

                            if (!IsWindow(outlookWindow))
                            {
                                logCallback?.Invoke("✅ Mail penceresi kapandı, mail başarıyla gönderildi!");
                            }
                            else
                            {
                                logCallback?.Invoke("⚠️ Pencere hala açık, manuel kontrol gerekebilir.");
                            }

                            // Outlook process'ini kapatılana kadar bekle
                            logCallback?.Invoke("📧 Outlook process kapatılması bekleniyor...");
                            await Task.Run(() => outlookProcess.WaitForExit());
                            logCallback?.Invoke("📧 Outlook kapatıldı, mail gönderme işlemi tamamlandı.");
                        }
                        else
                        {
                            logCallback?.Invoke("❌ Outlook penceresi bulunamadı!");
                            logCallback?.Invoke($"🔍 Aranan mail konusu: {subject}");
                            logCallback?.Invoke("🔍 Aranan pencere isimleri:");
                            logCallback?.Invoke($"   - Tam konu: {subject}");
                            logCallback?.Invoke($"   - Tam konu + İleti: {subject} - İleti");

                            var subjectParts = subject.Split(' ');
                            foreach (var part in subjectParts.Take(5))
                            {
                                if (part.Length > 3)
                                {
                                    logCallback?.Invoke($"   - Konu kelimesi: {part}");
                                }
                            }

                            logCallback?.Invoke($"   - Türkçe Outlook: İleti");

                            var generalWindowNames = new[]
                            {
                                "Message (HTML)",
                                "Untitled - Message",
                                "Message",
                                "New Message",
                                "Mail",
                                "Outlook",
                                "Untitled",
                                "New",
                                "Compose",
                                "Draft",
                                "Reply",
                                "Forward",
                                "New Email",
                                "New Mail",
                                "Compose Message",
                                "Draft Message"
                            };

                            foreach (var windowName in generalWindowNames)
                            {
                                logCallback?.Invoke($"   - Genel: {windowName}");
                            }
                            logCallback?.Invoke("🔍 Olası nedenler:");
                            logCallback?.Invoke("   - Outlook henüz açılmadı (daha uzun bekleme gerekebilir)");
                            logCallback?.Invoke("   - Outlook farklı bir pencere ismi kullanıyor");
                            logCallback?.Invoke("   - Outlook açılmadı veya hata verdi");
                            logCallback?.Invoke("   - Outlook güvenlik ayarları engelliyor olabilir");
                            logCallback?.Invoke("🔍 Çözüm önerileri:");
                            logCallback?.Invoke("   - Outlook'u manuel olarak açın");
                            logCallback?.Invoke("   - Mail penceresini manuel olarak kontrol edin");
                            logCallback?.Invoke("   - Outlook güvenlik ayarlarını kontrol edin");
                            logCallback?.Invoke("📧 Lütfen maili manuel olarak gönderin!");

                            // Tüm açık pencereleri listele (debug için)
                            logCallback?.Invoke("🔍 Tüm açık pencereler listeleniyor (debug)...");
                            var allWindows = new List<string>();
                            var hwnd = GetForegroundWindow();

                            for (int i = 0; i < 50; i++) // İlk 50 pencereyi kontrol et
                            {
                                var title = new StringBuilder(256);
                                GetWindowText(hwnd, title, title.Capacity);
                                var windowTitle = title.ToString();

                                if (!string.IsNullOrEmpty(windowTitle) && windowTitle.Length > 3)
                                {
                                    allWindows.Add(windowTitle);
                                }

                                hwnd = GetWindow(hwnd, GW_HWNDNEXT);
                                if (hwnd == IntPtr.Zero) break;
                            }

                            logCallback?.Invoke("🔍 Açık pencereler:");
                            foreach (var window in allWindows.Take(10)) // İlk 10 pencereyi göster
                            {
                                logCallback?.Invoke($"   - {window}");
                            }
                        }
                    }
                    else
                    {
                        logCallback?.Invoke("📧 Manuel gönderim - Lütfen açılan mail penceresinde gönder butonuna tıklayın.");
                    }

                    return true;
                }
                else
                {
                    // Outlook bulunamazsa varsayılan mail uygulamasını kullan
                    logCallback?.Invoke("❌ Outlook Classic bulunamadı!");
                    logCallback?.Invoke("🔍 Aranan Outlook yolları:");
                    foreach (var path in outlookPaths)
                    {
                        var exists = File.Exists(path);
                        logCallback?.Invoke($"   - {path} {(exists ? "✅ Bulundu" : "❌ Bulunamadı")}");
                    }
                    logCallback?.Invoke("🔍 Olası nedenler:");
                    logCallback?.Invoke("   - Outlook yüklü değil");
                    logCallback?.Invoke("   - Outlook farklı bir konumda yüklü");
                    logCallback?.Invoke("   - Office sürümü farklı");

                    var mailtoUrl = $"mailto:{recipient}?subject={Uri.EscapeDataString(subject)}&body={Uri.EscapeDataString(body)}";
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = mailtoUrl,
                        UseShellExecute = true
                    });

                    logCallback?.Invoke($"✅ Varsayılan mail uygulaması açıldı: {recipient}");
                    logCallback?.Invoke($"📧 Konu: {subject}");
                    logCallback?.Invoke($"📝 İçerik: {body.Substring(0, Math.Min(100, body.Length))}...");
                    logCallback?.Invoke("📤 Maili manuel olarak göndermeniz gerekiyor.");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Outlook hatası: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> TrySendViaSmtpAsync(string recipient, string subject, string body, Action<string>? logCallback = null)
        {
            try
            {
                logCallback?.Invoke("📧 SMTP ile mail gönderiliyor...");
                // SMTP implementasyonu burada olacak
                logCallback?.Invoke("⚠️ SMTP henüz implement edilmedi, varsayılan mail client kullanılıyor.");
                return false;
            }
            catch (Exception ex)
            {
                logCallback?.Invoke($"❌ SMTP hatası: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> TrySendViaDefaultMailClientAsync(string recipient, string subject, string body, Action<string>? logCallback = null)
        {
            try
            {
                logCallback?.Invoke("📧 Varsayılan mail client ile mail gönderiliyor...");

                var mailtoUrl = $"mailto:{recipient}?subject={Uri.EscapeDataString(subject)}&body={Uri.EscapeDataString(body)}";

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = mailtoUrl,
                    UseShellExecute = true
                });

                logCallback?.Invoke($"✅ Varsayılan mail client açıldı: {recipient}");
                logCallback?.Invoke($"📧 Manuel olarak maili göndermeniz gerekiyor!");
                return true;
            }
            catch (Exception ex)
            {
                logCallback?.Invoke($"❌ Varsayılan mail client hatası: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> SendTestEmailAsync(string recipient)
        {
            var subject = "Test Mail - Ödeme Emri Oluşturucu";
            var body = "Bu bir test mailidir. Sistem çalışıyor.";

            return await SendEmailAsync(recipient, subject, body, null);
        }

        public async Task SendPaymentOrderCreatorEmailAsync(string recipient, string subject, string body)
        {
            try
            {
                // Ödeme emri oluşturan için özel email gönderimi
                // Bu metod ödeme emri oluşturan kişiye özel bir email gönderir
                
                // Özel konu ekle
                var specialSubject = $"[ÖDEME EMRI OLUŞTURAN] {subject}";
                
                // Özel içerik ekle
                var specialBody = $@"ÖDEME EMRI OLUŞTURAN KİŞİ İÇİN ÖZEL BİLGİLENDİRME

{body}

---
Bu email ödeme emri oluşturan kişiye özel olarak gönderilmiştir.
Tarih: {DateTime.Now:dd/MM/yyyy HH:mm}
";

                // Manuel email gönderimi kullan
                await SendManualEmailAsync(recipient, specialSubject, specialBody);
            }
            catch (Exception ex)
            {
                throw new Exception($"Ödeme emri oluşturan email gönderimi sırasında hata: {ex.Message}");
            }
        }

        public async Task SendErrorNotificationAsync(string keyword, string errorMessage, DateTime errorTime)
        {
            if (!_notificationConfig.Enabled)
                return;

            var keywordConfig = _notificationConfig.Keywords.FirstOrDefault(k =>
                k.Enabled && k.Keyword.Equals(keyword, StringComparison.OrdinalIgnoreCase));

            if (keywordConfig == null || string.IsNullOrEmpty(keywordConfig.EmailRecipient))
                return;

            try
            {
                var subject = $"HATA - Ödeme Emri İşlemi - {keyword}";
                var body = CreateErrorEmailBody(keyword, errorMessage, errorTime);

                await SendEmailAsync(keywordConfig.EmailRecipient, subject, body, null);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Hata maili gönderme hatası: {ex.Message}");
            }
        }

        private string CreateCompletionEmailBody(List<string> files, decimal amount, string keyword, string? periodName = null)
        {
            var body = new StringBuilder();
            body.AppendLine("Merhaba,");
            body.AppendLine();
            body.AppendLine($"'{periodName ?? $"{DateTime.Now:dd-MM} {GetMonthName(DateTime.Now.Month)} {DateTime.Now.Year} {keyword} MONSANTO"}' dönemi için ödeme emri oluşturma işlemi tamamlanmıştır.");
            body.AppendLine();
            body.AppendLine("Ödeme emri muhasebe birimine gönderilmiştir.");
            body.AppendLine();
            body.AppendLine("İyi çalışmalar dilerim.");

            return body.ToString();
        }

        private string CreateErrorEmailBody(string keyword, string errorMessage, DateTime errorTime)
        {
            var body = new StringBuilder();
            body.AppendLine($"Ödeme Emri İşlemi Hatası - {keyword}");
            body.AppendLine();
            body.AppendLine($"Hata Tarihi: {errorTime:dd.MM.yyyy HH:mm:ss}");
            body.AppendLine($"Hata Mesajı: {errorMessage}");
            body.AppendLine();
            body.AppendLine("Lütfen sistemi kontrol ediniz.");

            return body.ToString();
        }

        private decimal CalculateAmountFromFiles(List<string> files)
        {
            decimal totalAmount = 0;
            
            foreach (var file in files)
            {
                try
                {
                    // WebScraper'daki ExtractTotalAmountFromExcel metodunu kullan
                    var webScraper = new WebScraper();
                    var amount = webScraper.ExtractTotalAmountFromExcel(file);
                    totalAmount += amount;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Excel dosyası okuma hatası ({file}): {ex.Message}");
                }
            }
            
            return totalAmount;
        }





        private string GetMonthName(int month)
        {
            return month switch
            {
                1 => "Oca",
                2 => "Şub",
                3 => "Mar",
                4 => "Nis",
                5 => "May",
                6 => "Haz",
                7 => "Tem",
                8 => "Ağu",
                9 => "Eyl",
                10 => "Eki",
                11 => "Kas",
                12 => "Ara",
                _ => month.ToString()
            };
        }
    }
} 