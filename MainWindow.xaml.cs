using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Drawing;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Media.Animation;
using OfficeOpenXml;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MaterialDesignThemes.Wpf;
using Microsoft.Win32;

namespace WebScraper
{
    public class DownloadedFileItem
    {
        public string Id { get; set; } = "";
        public DateTime DownloadDate { get; set; }
        public string Period { get; set; } = "";
        public string Status { get; set; } = "İndirildi";
    }

    public partial class MainWindow : Window
    {
        private readonly WebScraperService _scraperService;
        private readonly FirebaseAuthService _firebaseAuth;
        private readonly MailHistoryService _mailHistoryService;
        private readonly DispatcherTimer _statusTimer;
        private CancellationTokenSource? _cancellationTokenSource;
        private bool _isScraping = false;
        private DateTime _scrapingStartTime;
        private bool _isTimerRunning = false;
        private ObservableCollection<HistoryRecord> _historyRecords;
        private ObservableCollection<DownloadedFileItem> _downloadedFiles;
        private const string HISTORY_FILE = "process_history.json";
        private const string DOWNLOADED_FILE = "previously_downloaded.json";
        private string _currentProcessType = "";
        private string _currentPeriod = "";
        private decimal _currentTotalAmount = 0;

        public MainWindow()
        {
            try
            {
                InitializeComponent();
                
                // Güncelleme kontrolünü başlat (async olarak, UI thread'de)
                _ = CheckForUpdates();

                // Geçmiş verilerini yükle
                LoadHistoryRecords();

                // İndirilen dosyaları yükle
                LoadDownloadedFiles();

                _scraperService = new WebScraperService();
                _firebaseAuth = new FirebaseAuthService();
                _mailHistoryService = new MailHistoryService();

                // Status timer
                _statusTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(1)
                };
                _statusTimer.Tick += StatusTimer_Tick;

                // Event handlers
                _scraperService.ProgressChanged += OnProgressChanged;
                _scraperService.StatusChanged += OnStatusChanged;
                _scraperService.LogMessage += OnLogMessage;
                _scraperService.FoundChanged += OnFoundChanged;
                _scraperService.DownloadedChanged += OnDownloadedChanged;
                _scraperService.TotalAmountChanged += OnTotalAmountChanged;

                // Load settings
                LoadSettings();

                // Initialize UI
                UpdateStatus("Hazır", "İşlemi başlatmak için aşağıdaki butona tıklayın.", StatusType.Ready);
                UpdateProgress(0, 0);
                UpdateCounters(0, 0, 0);

                // Sayfa seçimi varsayılan stilini uygula
                UpdatePageSelectionStyle("normal");

                // Başlangıç label renklerini ayarla (varsayılan olarak Onaylandı seçili)
                // Dispatcher.BeginInvoke kullanarak UI thread'de çalıştır
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        if (txtTaslakLabel != null && txtOnaylandiLabel != null)
                        {
                            txtTaslakLabel.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(102, 102, 102)); // #666 - Soluk
                            txtOnaylandiLabel.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 0, 0)); // #000 - Koyu siyah
                        }
                    }
                    catch (Exception ex)
                    {
                        LogMessage($"Label renkleri ayarlanırken hata: {ex.Message}");
                    }
                }));

                LogMessage("Uygulama başlatıldı.");

                // Placeholder ayarlarını yap
                SetupPlaceholders();

                // Giriş ekranını kontrol et
                CheckLoginStatus();

                // Closing event handler ekle - pencere kapandığında SelectionWindow'u göster
                this.Closing += MainWindow_Closing;

                // WebScraperService'i temizle
                // _scraperService?.Cleanup();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Uygulama başlatılırken hata oluştu: {ex.Message}", "Kritik Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                throw;
            }
        }

        private async Task CheckForUpdates()
        {
            UpdateLogWindow? logWindow = null;
            bool hasNewVersion = false;
            
            try
            {
                // Önce arka planda güncelleme kontrolü yap (pencere açmadan)
                var config = ConfigManager.LoadConfig();
                if (config?.Update == null || !config.Update.Enabled)
                {
                    // Güncelleme devre dışı, pencere açma
                    return;
                }

                var currentVersionInfo = UpdateHelper.GetCurrentVersion();
                var currentVersion = currentVersionInfo.Version;

                // GitHub'dan kontrol et (arka planda, pencere açmadan)
                UpdateHelper.GitHubRelease? latestRelease = null;
                try
                {
                    latestRelease = await UpdateHelper.CheckForUpdatesAsync();
                }
                catch (Exception ex)
                {
                    // Hata olsa bile eskiden sessizce devam ediyorduk; artık kullanıcıya da gösterelim
                    Debug.WriteLine($"Güncelleme kontrolü hatası: {ex.Message}");
                    Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show(
                            $"Güncelleme kontrolü sırasında hata oluştu:\n\n{ex.Message}",
                            "Güncelleme Hatası",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning
                        );
                    });
                    return;
                }

                if (latestRelease == null)
                {
                    // Release bulunamadı, sessizce çık
                    return;
                }

                // Versiyon karşılaştırması yap
                var latestVersion = latestRelease.tag_name?.TrimStart('v', 'V') ?? "";
                var currentVersionClean = currentVersion?.TrimStart('v', 'V') ?? "";

                if (string.IsNullOrEmpty(latestVersion) || string.IsNullOrEmpty(currentVersionClean))
                {
                    // Versiyon bilgisi eksik, sessizce çık
                    return;
                }

                // Versiyonları karşılaştır
                if (!UpdateHelper.IsNewerVersion(currentVersionClean, latestVersion))
                {
                    // Güncel veya daha yeni versiyon kullanılıyor
                    Debug.WriteLine($"Güncelleme yok. Mevcut: v{currentVersion}, En son: {latestRelease.tag_name}");
                    return;
                }

                // Yeni versiyon VARSA, şimdi pencereyi aç
                hasNewVersion = true;
                
                Dispatcher.Invoke(() =>
                {
                    try
                    {
                        logWindow = new UpdateLogWindow
                        {
                            Owner = this,
                            WindowStartupLocation = WindowStartupLocation.CenterOwner
                        };
                        logWindow.Show();
                        logWindow.AddLog("🔄 Güncelleme kontrolü başlatılıyor...");
                        logWindow.AddLog($"📋 Config okundu. Update URL: {config.Update.UpdateUrl}");
                        logWindow.AddLog($"📦 Mevcut versiyon: {currentVersion}");
                        logWindow.AddLog($"✅ GitHub Release bulundu: {latestRelease.tag_name}");
                        logWindow.AddLog($"🆕 Yeni versiyon bulundu: {latestRelease.tag_name}");
                        
                        if (latestRelease.prerelease)
                        {
                            logWindow.AddLog("⚠️ Bu bir pre-release (beta) versiyon");
                        }
                        
                        if (!string.IsNullOrEmpty(latestRelease.body))
                        {
                            var bodyPreview = latestRelease.body.Length > 100 
                                ? latestRelease.body.Substring(0, 100) + "..." 
                                : latestRelease.body;
                            logWindow.AddLog($"📝 Release notları: {bodyPreview}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"UpdateLogWindow açılamadı: {ex.Message}");
                        return;
                    }
                });
                
                if (logWindow == null)
                {
                    return;
                }

                // Zip dosyasını bul
                if (latestRelease.assets != null && latestRelease.assets.Length > 0)
                {
                    logWindow?.AddLog($"📋 Mevcut asset'ler ({latestRelease.assets.Length} adet):");
                    foreach (var asset in latestRelease.assets)
                    {
                        logWindow?.AddLog($"   - {asset.name} ({asset.size / 1024 / 1024} MB)");
                    }
                }
                else
                {
                    logWindow?.AddLog("⚠️ Release'de asset bulunamadı!");
                }

                var zipAsset = latestRelease.assets?.FirstOrDefault(a => 
                    a.name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && 
                    a.name.Contains("PinhumanSuperAPP"));
                
                if (zipAsset == null)
                {
                    logWindow?.AddLog("❌ Zip dosyası bulunamadı.");
                    logWindow?.AddLog("   Aranan: *.zip ve PinhumanSuperAPP içeren dosya");
                    logWindow?.SetStatus("Zip dosyası bulunamadı");
                    Debug.WriteLine("Zip dosyası bulunamadı.");
                    return;
                }
                
                logWindow?.AddLog($"📦 Zip dosyası bulundu: {zipAsset.name} ({zipAsset.size / 1024 / 1024} MB)");
                logWindow?.AddLog($"🔗 Download URL: {zipAsset.browser_download_url}");
                if (!string.IsNullOrEmpty(zipAsset.url))
                {
                    logWindow?.AddLog($"🔗 API URL: {zipAsset.url}");
                }
                if (latestRelease.draft)
                {
                    logWindow?.AddLog("⚠️ UYARI: Bu bir draft release!");
                }
                logWindow?.AddLog("⬇️ Güncelleme otomatik olarak indiriliyor...");
                logWindow?.SetStatus("Güncelleme indiriliyor...");
                logWindow?.SetProgress(0); // Progress bar'ı göster ve 0'dan başlat

                // Otomatik indirme ve kurulum
                try
                {
                    logWindow?.AddLog($"📥 İndirme başlatılıyor: {zipAsset.browser_download_url}");

                    await UpdateHelper.DownloadAndExtractUpdateAsync(
                        zipAsset.browser_download_url,
                        zipAsset.name,
                        new Progress<double>(percent =>
                        {
                            // Dispatcher.BeginInvoke kullan (non-blocking) - daha hızlı
                            Dispatcher.BeginInvoke(new Action(() =>
                            {
                                logWindow?.SetStatus($"İndiriliyor... {percent:F0}%");
                                logWindow?.SetProgress(percent); // Progress bar'ı güncelle
                                // Log'u sadece belirli aralıklarla güncelle (her %5'te bir)
                                if (percent % 5 < 1 || percent >= 100)
                                {
                                    logWindow?.AddLog($"📥 İndirme ilerlemesi: {percent:F0}%");
                                }
                            }));
                            // Debug sadece belirli aralıklarla
                            if (percent % 10 < 1 || percent >= 100)
                            {
                                Debug.WriteLine($"İndirme ilerlemesi: {percent:F0}%");
                            }
                        }),
                        zipAsset.url
                    );

                    // Başarılı
                    Dispatcher.Invoke(() =>
                    {
                        logWindow?.AddLog("✅ Güncelleme başarıyla indirildi ve kuruldu!");
                        logWindow?.AddLog("🔄 Uygulama yeniden başlatılacak...");
                        logWindow?.SetStatus("Güncelleme tamamlandı");
                        logWindow?.SetProgress(100); // %100 göster
                    });

                    // Uygulamayı yeniden başlat
                    await Task.Delay(2000); // 2 saniye bekle
                    
                    Dispatcher.Invoke(() =>
                    {
                        var currentExe = Process.GetCurrentProcess().MainModule?.FileName;
                        if (!string.IsNullOrEmpty(currentExe))
                        {
                            Process.Start(currentExe);
                            Application.Current.Shutdown();
                        }
                    });
                }
                catch (Exception downloadEx)
                {
                    Dispatcher.Invoke(() =>
                    {
                        logWindow?.AddLog($"❌ İndirme hatası: {downloadEx.Message}");
                        if (downloadEx.InnerException != null)
                        {
                            logWindow?.AddLog($"   İç hata: {downloadEx.InnerException.Message}");
                            if (downloadEx.InnerException.StackTrace != null)
                            {
                                var stackTrace = downloadEx.InnerException.StackTrace;
                                var firstLine = stackTrace.Split('\n').FirstOrDefault();
                                if (!string.IsNullOrEmpty(firstLine))
                                {
                                    logWindow?.AddLog($"   Konum: {firstLine.Trim()}");
                                }
                            }
                        }
                        if (downloadEx.StackTrace != null)
                        {
                            var stackTrace = downloadEx.StackTrace;
                            var firstLine = stackTrace.Split('\n').FirstOrDefault();
                            if (!string.IsNullOrEmpty(firstLine))
                            {
                                logWindow?.AddLog($"   Stack: {firstLine.Trim()}");
                            }
                        }
                        logWindow?.SetStatus("İndirme hatası");
                    });
                }
            }
            catch (Exception ex)
            {
                // Hata loglama
                Debug.WriteLine($"Güncelleme kontrolü başarısız: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                
                logWindow?.AddLog($"❌ HATA: {ex.Message}");
                if (ex.InnerException != null)
                {
                    logWindow?.AddLog($"   İç hata: {ex.InnerException.Message}");
                }
                var errorMsg = ex.Message.Length > 50 ? ex.Message.Substring(0, 50) + "..." : ex.Message;
                logWindow?.SetStatus($"Hata: {errorMsg}");
            }
            finally
            {
                // UpdateLogWindow'u kapatma - kullanıcı kapatabilir
                // logWindow?.Close(); // Kullanıcı kapatabilir, otomatik kapatma
            }
        }

        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            try
            {
                // Temizlik işlemlerini yap
                if (_isScraping)
                {
                    _cancellationTokenSource?.Cancel();
                }

                // Tüm timer'ları durdur
                _statusTimer?.Stop();

                // CancellationTokenSource'u dispose et
                _cancellationTokenSource?.Dispose();

                // WebScraperService'i temizle
                _scraperService?.ForceStopBrowser();

                // FirebaseAuthService'i temizle
                _firebaseAuth?.Logout();

                // Tüm event handler'ları temizle
                if (_scraperService != null)
                {
                    _scraperService.ProgressChanged -= OnProgressChanged;
                    _scraperService.StatusChanged -= OnStatusChanged;
                    _scraperService.LogMessage -= OnLogMessage;
                    _scraperService.FoundChanged -= OnFoundChanged;
                    _scraperService.DownloadedChanged -= OnDownloadedChanged;
                    _scraperService.TotalAmountChanged -= OnTotalAmountChanged;
                }

                // Sadece Chromium process'lerini kapat
                try
                {
                    var chromiumProcesses = Process.GetProcessesByName("chromium");
                    foreach (var process in chromiumProcesses)
                    {
                        try
                        {
                            if (!process.HasExited)
                            {
                                process.Kill();
                                process.WaitForExit(3000);
                            }
                        }
                        catch { /* Sessizce geç */ }
                    }
                }
                catch { /* Sessizce geç */ }

                // Mevcut SelectionWindow'u bul ve göster
                var selectionWindow = Application.Current.Windows.OfType<SelectionWindow>().FirstOrDefault();
                if (selectionWindow != null)
                {
                    selectionWindow.Show();
                    selectionWindow.WindowState = WindowState.Maximized;
                    selectionWindow.Activate();
                }
            }
            catch (Exception ex)
            {
                // Hata olsa bile devam et
                LogMessage($"Pencere kapanırken hata: {ex.Message}");
            }
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Pencereyi öne getir
                this.Activate();
                this.Topmost = true;
                this.Topmost = false;
                this.Focus();

                // Check for updates
                try
                {
                    //using (var mgr = await UpdateManager.GitHubUpdateManager("https://github.com/furkanozm/PinhumanSuperAPP-Release"))
                    //{
                    //    var release = await mgr.UpdateApp();
                    //}
                }
                catch (Exception ex)
                {
                    LogMessage($"Check for updates failed: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"MainWindow_Loaded method failed: {ex.Message}");
                System.Windows.MessageBox.Show($"MainWindow_Loaded method failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SetupPlaceholders()
        {
            try
            {
                // Kelime arama alanı için placeholder
                if (txtKeywordSearch != null)
                {
                    txtKeywordSearch.GotFocus += (sender, e) =>
                    {
                        if (txtKeywordSearch.Text == "Arama yapabilirsiniz")
                        {
                            txtKeywordSearch.Text = "";
                            txtKeywordSearch.Foreground = System.Windows.Media.Brushes.Black;
                        }
                    };

                    txtKeywordSearch.LostFocus += (sender, e) =>
                    {
                        if (string.IsNullOrWhiteSpace(txtKeywordSearch.Text))
                        {
                            txtKeywordSearch.Text = "Arama yapabilirsiniz";
                            txtKeywordSearch.Foreground = System.Windows.Media.Brushes.Gray;
                        }
                    };

                    // Başlangıçta placeholder'ı göster
                    txtKeywordSearch.Text = "Arama yapabilirsiniz";
                    txtKeywordSearch.Foreground = System.Windows.Media.Brushes.Gray;
                }

                // Mail geçmişi arama alanı için placeholder
                if (txtMailHistorySearch != null)
                {
                    txtMailHistorySearch.GotFocus += (sender, e) =>
                    {
                        if (txtMailHistorySearch.Text == "Mail geçmişinde arama yapabilirsiniz")
                        {
                            txtMailHistorySearch.Text = "";
                            txtMailHistorySearch.Foreground = System.Windows.Media.Brushes.Black;
                        }
                    };

                    txtMailHistorySearch.LostFocus += (sender, e) =>
                    {
                        if (string.IsNullOrWhiteSpace(txtMailHistorySearch.Text))
                        {
                            txtMailHistorySearch.Text = "Mail geçmişinde arama yapabilirsiniz";
                            txtMailHistorySearch.Foreground = System.Windows.Media.Brushes.Gray;
                        }
                    };

                    // Başlangıçta placeholder'ı göster
                    txtMailHistorySearch.Text = "Mail geçmişinde arama yapabilirsiniz";
                    txtMailHistorySearch.Foreground = System.Windows.Media.Brushes.Gray;
                }

                // İndirilen dosyalar arama alanı için placeholder
                if (txtDownloadedSearch != null)
                {
                    txtDownloadedSearch.GotFocus += (sender, e) =>
                    {
                        if (txtDownloadedSearch.Text == "İndirilen dosyalarda arama yapabilirsiniz")
                        {
                            txtDownloadedSearch.Text = "";
                            txtDownloadedSearch.Foreground = System.Windows.Media.Brushes.Black;
                        }
                    };

                    txtDownloadedSearch.LostFocus += (sender, e) =>
                    {
                        if (string.IsNullOrWhiteSpace(txtDownloadedSearch.Text))
                        {
                            txtDownloadedSearch.Text = "İndirilen dosyalarda arama yapabilirsiniz";
                            txtDownloadedSearch.Foreground = System.Windows.Media.Brushes.Gray;
                        }
                    };

                    // Başlangıçta placeholder'ı göster
                    txtDownloadedSearch.Text = "İndirilen dosyalarda arama yapabilirsiniz";
                    txtDownloadedSearch.Foreground = System.Windows.Media.Brushes.Gray;
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Placeholder ayarları sırasında hata: {ex.Message}");
            }
        }

        private void LoadSettings()
        {
            try
            {
                // ConfigManager yoksa varsayılan değerler kullan
                if (System.IO.File.Exists("config.json"))
                {
                    var config = ConfigManager.LoadConfig();

                    // Login settings
                    chkAutoLogin.IsChecked = config.AutoLogin.Enabled;
                    txtUsername.Text = config.AutoLogin.Username;
                    txtPassword.Password = config.AutoLogin.Password;
                    // ComboBox'ta seçili değeri ayarla
                foreach (ComboBoxItem item in cmbCompanyCode.Items)
                {
                    if (item.Content.ToString() == config.AutoLogin.CompanyCode)
                    {
                        cmbCompanyCode.SelectedItem = item;
                        break;
                    }
                }
                    txtTotpSecret.Password = config.AutoLogin.TotpSecret;

                    
                    // SMS settings
                    chkHeadlessMode.IsChecked = config.Sms.HeadlessMode;
                    
                    // Output folder setting
                    txtOutputFolder.Text = config.Download.OutputFolder ?? "";
                    
                    // Mail settings
                    chkMailNotifications.IsChecked = config.Notification.Enabled;
                    // SenderName ve SenderPassword artık kullanılmıyor
                    // txtSenderName.Text = config.Notification.SenderName;
                    // txtSenderPassword.Password = config.Notification.SenderPassword;
                    
                    // Load keywords
                    LoadKeywordList(config.Notification.Keywords);

                    LogMessage("Ayarlar başarıyla yüklendi.");
                }
                else
                {
                    // Varsayılan değerler
                    chkAutoLogin.IsChecked = false;
                    txtUsername.Text = "";
                    txtPassword.Password = "";
                    cmbCompanyCode.SelectedIndex = 0; // İlk seçeneği seç (ikb)
                    txtTotpSecret.Password = "";

                    chkHeadlessMode.IsChecked = true;
                    txtOutputFolder.Text = "";
                    
                    // Default mail settings
                    chkMailNotifications.IsChecked = false;
                    txtSenderPassword.Password = "";
                    
                    // Load default keywords
                    LoadKeywordList(new List<KeywordNotification>());

                    LogMessage("Varsayılan ayarlar kullanılıyor.");
                }
                
                // Beni Hatırla ayarlarını yükle
                LoadRememberMeSettings();
                
                // Manuel gönderim butonu her zaman aktif olsun
                // btnManualSend butonu kaldırıldı
            }
            catch (Exception ex)
            {
                LogMessage($"Ayarlar yüklenirken hata oluştu: {ex.Message}");
                // Hata durumunda varsayılan değerler
                chkAutoLogin.IsChecked = false;
                txtUsername.Text = "";
                txtPassword.Password = "";
                cmbCompanyCode.SelectedIndex = 0; // İlk seçeneği seç (ikb)
                txtTotpSecret.Password = "";

                chkHeadlessMode.IsChecked = true;
                txtOutputFolder.Text = "";
                
                // Default mail settings
                chkMailNotifications.IsChecked = false;
            }
        }

        private void SaveSettings()
        {
            try
            {
                var config = ConfigManager.LoadConfig();

                // Login settings
                config.AutoLogin.Enabled = chkAutoLogin.IsChecked ?? false;
                config.AutoLogin.Username = txtUsername.Text;
                config.AutoLogin.Password = txtPassword.Password;
                config.AutoLogin.CompanyCode = cmbCompanyCode.SelectedItem != null ? 
                    (cmbCompanyCode.SelectedItem as ComboBoxItem)?.Content.ToString() : "ikb";
                config.AutoLogin.TotpSecret = txtTotpSecret.Visibility == Visibility.Visible 
                    ? txtTotpSecret.Password 
                    : txtTotpSecretVisible.Text;

                
                // SMS settings
                config.Sms.HeadlessMode = chkHeadlessMode.IsChecked ?? true;
                
                // Output folder setting
                config.Download.OutputFolder = txtOutputFolder.Text;
                
                // Mail settings
                config.Notification.Enabled = chkMailNotifications.IsChecked ?? false;
                
                // Save keywords
                config.Notification.Keywords = GetKeywordList();

                ConfigManager.SaveConfig(config);
                LogMessage("Ayarlar başarıyla kaydedildi.");
                
                System.Windows.MessageBox.Show("Ayarlar başarıyla kaydedildi.", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                LogMessage($"Ayarlar kaydedilirken hata oluştu: {ex.Message}");
                System.Windows.MessageBox.Show($"Ayarlar kaydedilirken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void btnStartScraping_Click(object sender, RoutedEventArgs e)
        {
            if (_isScraping)
            {
                System.Windows.MessageBox.Show("İşlem zaten devam ediyor.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Sayfa boyutu seçim modal'ını aç
            var pageSizeModal = new PageSizeSelectionModal();
            var modalResult = pageSizeModal.ShowDialog();

            if (modalResult != true)
            {
                LogMessage("Sayfa boyutu seçimi iptal edildi.");
                return;
            }

            // Seçilen sayfa boyutunu al
            int selectedPageSize = pageSizeModal.SelectedPageSize;
            LogMessage($"📊 Sayfa boyutu seçildi: {selectedPageSize} öğe");

            // Checkbox durumuna göre işlem türünü belirle
            bool isCreateMode = chkCreateMode.IsChecked == true;

            // İşlem ID'si olarak timestamp kullan
            string processId = DateTime.Now.ToString("yyyyMMdd_HHmmss");

            // Oluştur modu için çıktı klasörü kontrolü gerekmez
            if (!isCreateMode)
            {
                // Çıktı klasörü kontrolü
                var selectedOutputPath = txtOutputFolder.Text.Trim();
                if (string.IsNullOrEmpty(selectedOutputPath))
                {
                    var result = System.Windows.MessageBox.Show("Çıktı klasörü seçilmemiş. Şimdi klasör seçmek ister misiniz?", 
                        "Çıktı Klasörü Gerekli", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    
                    if (result == MessageBoxResult.Yes)
                    {
                        // Ayarlar sekmesine geç
                        tabControl.SelectedIndex = 1;
                        
                        // Klasör seçme butonuna tıkla
                        btnSelectOutputFolder_Click(sender, e);
                        
                        // Klasör seçildikten sonra tekrar kontrol et
                        selectedOutputPath = txtOutputFolder.Text.Trim();
                        if (string.IsNullOrEmpty(selectedOutputPath))
                        {
                            System.Windows.MessageBox.Show("Çıktı klasörü seçilmedi. İşlem başlatılamıyor.", 
                                "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                            return;
                        }
                    }
                    else
                    {
                        System.Windows.MessageBox.Show("Çıktı klasörü seçilmedi. İşlem başlatılamıyor.", 
                            "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }
            }

            try
            {
                _isScraping = true;
                _cancellationTokenSource = new CancellationTokenSource();

                // Loading overlay'i göster
                ShowLoadingOverlay("İşlem başlatılıyor...");

                // Update UI
                btnStartScraping.IsEnabled = false;
                btnStopScraping.IsEnabled = true;
                
                // Radio button'ları disabled yap
                rbNormalPayment.IsEnabled = false;
                rbAdvancePayment.IsEnabled = false;
                
                UpdateStatus("Başlatılıyor...", "İşlem başlatılıyor...", StatusType.Processing);
                
                // Loglar tabına geç
                tabControl.SelectedIndex = 1; // Loglar tabı
                
                // Timer'ı başlat
                _scrapingStartTime = DateTime.Now;
                _isTimerRunning = true;
                _statusTimer.Start();

                // Get settings
                var config = ConfigManager.LoadConfig();
                var pageType = rbAdvancePayment.IsChecked == true ? "advance" : "normal";

                // Geçmiş için işlem bilgilerini ayarla
                _currentProcessType = isCreateMode ? "Ödeme Emri Oluşturma" : "Taslak Onaylama";
                _currentPeriod = ""; // Gerçek dönem adı işlem sırasında belirlenecek
                _currentTotalAmount = 0;

                // Checkbox durumuna göre işlem türünü belirle
                if (isCreateMode)
                {
                    LogMessage($"📝 Ödeme Emri Oluşturma işlemi başlatılıyor - Sayfa türü: {(pageType == "advance" ? "Avans Ödeme Emri" : "Normal Ödeme Emri")}");
                    
                    // Ödeme emri oluşturma işlemi
                    await _scraperService.StartPaymentOrderCreationAsync(config, _cancellationTokenSource.Token);
                    
                    ShowEmbeddedAlert(
                        "✅ Ödeme Emri Oluşturuldu!",
                        "Ödeme emri oluşturma işlemi başarıyla tamamlandı.",
                        "Tamam",
                        "",
                        () => {
                            // Onaylandığında hiçbir şey yapma, sadece kapat
                        }
                    );
                }
                else
                {
                    // İşlem türünü kontrol et - Switch sağda ise onaylandılar, solda ise taslaklar
                    if (chkIslemTuru.IsChecked == true)
                    {
                        LogMessage($"Normal indirme işlemi başlatılıyor - Sayfa türü: {(pageType == "advance" ? "Avans Ödeme Emri" : "Normal Ödeme Emri")}");
                        
                        // Normal scraping işlemi
                        await _scraperService.StartScrapingAsync(config, pageType, selectedPageSize, _cancellationTokenSource.Token);
                    }
                    else
                    {
                        LogMessage($"Taslak onaylama işlemi başlatılıyor - Sayfa türü: {(pageType == "advance" ? "Avans Ödeme Emri" : "Normal Ödeme Emri")}");
                        
                        // Taslak onaylama işlemi
                        await _scraperService.StartDraftApprovalAsync(config, pageType, selectedPageSize, _cancellationTokenSource.Token);
                    }
                }

                UpdateStatus("Tamamlandı", "İşlem başarıyla tamamlandı.", StatusType.Success);
                LogMessage("İşlem başarıyla tamamlandı!");

                // İşlem tamamlandı uyarısı göster
                var successAlert = new SuccessAlertWindow("Ödeme emri indirme işlemi başarıyla tamamlandı.\n\nİndirilen dosyalar 'cikti' klasöründe bulunmaktadır.");
                successAlert.Show();

                // Klasörü otomatik aç
                try
                {
                    var outputPath = Path.Combine(Directory.GetCurrentDirectory(), "cikti");
                    if (Directory.Exists(outputPath))
                    {
                        Process.Start("explorer.exe", outputPath);
                        LogMessage($"Çıktı klasörü açıldı: {outputPath}");
                    }
                    else
                    {
                        Directory.CreateDirectory(outputPath);
                        Process.Start("explorer.exe", outputPath);
                        LogMessage($"Çıktı klasörü oluşturuldu ve açıldı: {outputPath}");
                    }
                }
                catch (Exception ex)
                {
                    LogMessage($"Çıktı klasörü açılırken hata: {ex.Message}");
                    System.Windows.MessageBox.Show($"Çıktı klasörü açılırken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                }

                // Geçmiş kaydı ekle
                // Geçmiş kaydı WebScraper tarafından ekleniyor, burada ekleme yapmıyoruz
            }
            catch (OperationCanceledException)
            {
                UpdateStatus("İptal Edildi", "İşlem kullanıcı tarafından iptal edildi.", StatusType.Warning);
                LogMessage("İşlem kullanıcı tarafından iptal edildi.");
            }
            catch (Exception ex)
            {
                UpdateStatus("Hata", $"İşlem sırasında hata oluştu: {ex.Message}", StatusType.Error);
                LogMessage($"İşlem sırasında hata oluştu: {ex.Message}");
                System.Windows.MessageBox.Show($"İşlem sırasında hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // Loading overlay'i gizle
                HideLoadingOverlay();
                
                _isScraping = false;
                btnStartScraping.IsEnabled = true;
                btnStopScraping.IsEnabled = false;
                
                // Radio button'ları tekrar enabled yap
                rbNormalPayment.IsEnabled = true;
                rbAdvancePayment.IsEnabled = true;
                
                // Timer'ı durdur
                _isTimerRunning = false;
                _statusTimer.Stop();
                _cancellationTokenSource?.Dispose();
            }
        }



        private void btnStopScraping_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                LogMessage("Zorla durdurma işlemi başlatılıyor...");
                
                // Loading overlay'i gizle
                HideLoadingOverlay();
                
                // Cancellation token'ı iptal et
                _cancellationTokenSource?.Cancel();
                
                UpdateStatus("Durduruluyor...", "Scraping işlemi zorla durduruluyor...", StatusType.Warning);
                LogMessage("Scraping işlemi durdurma talebi gönderildi.");
                
                // Chrome'u zorla kapat
                _scraperService.ForceStopBrowser();
                
                // Tüm timer'ları durdur
                _isTimerRunning = false;
                _statusTimer.Stop();
                
                // UI'yi sıfırla
                _isScraping = false;
                btnStartScraping.IsEnabled = true;
                btnStopScraping.IsEnabled = false;
                
                // Radio button'ları tekrar enabled yap
                rbNormalPayment.IsEnabled = true;
                rbAdvancePayment.IsEnabled = true;
                
                // Progress'i sıfırla
                UpdateProgress(0, 0);
                UpdateCounters(0, 0, 0);
                
                UpdateStatus("Durduruldu", "Scraping işlemi zorla durduruldu.", StatusType.Warning);
                LogMessage("Scraping işlemi başarıyla durduruldu.");
            }
            catch (Exception ex)
            {
                LogMessage($"Durdurma işlemi sırasında hata: {ex.Message}");
                UpdateStatus("Hata", "Durdurma işlemi sırasında hata oluştu.", StatusType.Error);
            }
        }


        private void btnSettings_Click(object sender, RoutedEventArgs e)
        {
            // Switch to settings tab
            tabControl.SelectedIndex = 2;
        }

        private void btnLogs_Click(object sender, RoutedEventArgs e)
        {
            // Switch to logs tab
            tabControl.SelectedIndex = 1;
        }



        private void btnHelp_Click(object sender, RoutedEventArgs e)
        {
            // Switch to about tab
            tabControl.SelectedIndex = 2;
        }


        private void btnThemeToggle_Checked(object sender, RoutedEventArgs e)
        {
            // Karanlık tema
            if (btnThemeToggle != null)
            {
                btnThemeToggle.Content = "☀️";
                btnThemeToggle.ToolTip = "Açık temaya geç";
            }
            ApplyDarkTheme();
        }

        private void btnThemeToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            // Açık tema
            if (btnThemeToggle != null)
            {
                btnThemeToggle.Content = "🌙";
                btnThemeToggle.ToolTip = "Koyu temaya geç";
            }
            ApplyLightTheme();
        }

        private void ApplyDarkTheme()
        {
            // Ana pencere arkaplanını değiştir
            this.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(45, 45, 48));

           // Log alanının renklerini değiştir
           if (txtLog != null)
           {
               txtLog.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 30, 30));
               txtLog.Foreground = new SolidColorBrush(System.Windows.Media.Colors.White);
           }

            // Log border rengini değiştir
            if (LogBorder != null)
            {
                LogBorder.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(30, 30, 30));
                LogBorder.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(51, 51, 51));
            }

            // Status indicator rengini değiştir
            if (statusIndicator != null)
            {
                statusIndicator.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(70, 70, 70));
            }

            // Diğer UI elementlerinin renklerini değiştir
            if (txtStatus != null)
            {
                txtStatus.Foreground = new SolidColorBrush(System.Windows.Media.Colors.White);
            }

            if (txtStatusDetail != null)
            {
                txtStatusDetail.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(200, 200, 200));
            }

            // Tab control ve diğer UI elementleri
            if (tabControl != null)
            {
                tabControl.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(35, 35, 35));
            }
        }

        private void ApplyLightTheme()
        {
            // Ana pencere arkaplanını değiştir
            this.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(248, 249, 250));

            // Log alanının renklerini değiştir
            if (txtLog != null)
            {
                txtLog.Background = new SolidColorBrush(System.Windows.Media.Colors.White);
                txtLog.Foreground = new SolidColorBrush(System.Windows.Media.Colors.Black);
            }

            // Log border rengini değiştir
            if (LogBorder != null)
            {
                LogBorder.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(250, 250, 250));
                LogBorder.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(224, 224, 224));
            }

            // Status indicator rengini değiştir
            if (statusIndicator != null)
            {
                statusIndicator.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(200, 200, 200));
            }

            // Diğer UI elementlerinin renklerini değiştir
            if (txtStatus != null)
            {
                txtStatus.Foreground = new SolidColorBrush(System.Windows.Media.Colors.Black);
            }

            if (txtStatusDetail != null)
            {
                txtStatusDetail.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(100, 100, 100));
            }

            // Tab control ve diğer UI elementleri
            if (tabControl != null)
            {
                tabControl.Background = new SolidColorBrush(System.Windows.Media.Colors.White);
            }
        }

        private void LogoBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                // guleryuzgroup.com sitesini varsayılan tarayıcıda aç
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://guleryuzgroup.com",
                    UseShellExecute = true
                });
                
                LogMessage("Güleryüz Group web sitesi açıldı.");
            }
            catch (Exception ex)
            {
                LogMessage($"Web sitesi açılırken hata oluştu: {ex.Message}");
                System.Windows.MessageBox.Show($"Web sitesi açılırken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void PinhumanLogo_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                // pinhuman.net sitesini varsayılan tarayıcıda aç
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://pinhuman.net",
                    UseShellExecute = true
                });
                
                LogMessage("Pinhuman web sitesi açıldı.");
            }
            catch (Exception ex)
            {
                LogMessage($"Web sitesi açılırken hata oluştu: {ex.Message}");
                System.Windows.MessageBox.Show($"Web sitesi açılırken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnForgotPassword_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // En son başarılı giriş yapılan mail adresini al
                var lastEmail = "";
                var rememberMeFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "remember_me.txt");
                if (File.Exists(rememberMeFile))
                {
                    var lines = File.ReadAllLines(rememberMeFile);
                    if (lines.Length >= 2)
                    {
                        lastEmail = lines[1];
                    }
                }
                
                // Eğer remember_me.txt'den mail bulunamazsa, login ekranındaki mail'i kullan
                if (string.IsNullOrEmpty(lastEmail) && txtLoginEmail != null)
                {
                    lastEmail = txtLoginEmail.Text?.Trim();
                }
                
                var subject = "Şifre Sıfırlama Talebi";
                var body = $"Merhaba,\n\nŞifremi unuttum. Destek rica ederim.\n\nMail Adresi: {lastEmail}\n\nSaygılarımla";
                var to = "furkan.ozmen@guleryuzgroup.com";
                
                // Windows 11'de Outlook 365 için önce COM API'yi dene
                try
                {
                    // Outlook COM API'sini dene
                    SendPasswordResetViaOutlookCOM(to, subject, body);
                    LogMessage("✅ Outlook 365 COM API ile şifre sıfırlama maili açıldı.");
                }
                catch (Exception comEx)
                {
                    LogMessage($"⚠️ Outlook COM API başarısız: {comEx.Message}, klasik yöntem deneniyor...");

                    // COM API başarısız olursa klasik outlook.exe yöntemini dene
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
                        var mailtoUrl = $"mailto:{to}?subject={Uri.EscapeDataString(subject)}&body={Uri.EscapeDataString(body)}";
                        var outlookProcess = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = foundOutlookPath,
                                Arguments = $"/c ipm.note /m \"{mailtoUrl}\"",
                                UseShellExecute = false
                            }
                        };

                        outlookProcess.Start();
                        LogMessage($"✅ Outlook Classic açıldı: {foundOutlookPath}");

                        // Outlook kapatılana kadar bekle (kullanıcı mail'i gönderdikten sonra)
                        outlookProcess.WaitForExit();
                        LogMessage("📧 Outlook kapatıldı, mail gönderme işlemi tamamlandı.");
                    }
                    else
                    {
                        // Outlook bulunamazsa varsayılan mail uygulamasını kullan
                        var mailtoUrl = $"mailto:{to}?subject={Uri.EscapeDataString(subject)}&body={Uri.EscapeDataString(body)}";
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = mailtoUrl,
                            UseShellExecute = true
                        });

                        LogMessage("ℹ️ Outlook bulunamadı, varsayılan mail uygulaması açıldı.");
                    }
                }

                LogMessage("Şifre sıfırlama maili başarıyla gönderildi.");

                // Outlook kapatıldıktan sonra başarılı alert göster
                var successAlert = new SuccessAlertWindow("Şifre sıfırlama talebiniz başarıyla gönderildi!\n\nDestek ekibimiz en kısa sürede sizinle iletişime geçecektir.");
                successAlert.Show();
            }
            catch (Exception ex)
            {
                LogMessage($"Mail açılırken hata oluştu: {ex.Message}");
                System.Windows.MessageBox.Show($"Mail açılırken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnUserRequest_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Arka background overlay'i oluştur
                var overlay = new Border
                {
                    Background = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromArgb(150, 0, 0, 0)), // Daha koyu yarı şeffaf siyah
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                    VerticalAlignment = System.Windows.VerticalAlignment.Stretch
                };
                System.Windows.Controls.Panel.SetZIndex(overlay, 5); // LoginPanel'in altında ama diğer elementlerin üstünde

                // Overlay'i ana pencereye ekle
                if (this.Content is Grid mainGrid)
                {
                    mainGrid.Children.Add(overlay);
                }

                var userRequestModal = new UserRequestModal();
                userRequestModal.ShowDialog();
                
                // Modal kapandığında overlay'i kaldır
                if (this.Content is Grid grid && grid.Children.Contains(overlay))
                {
                    grid.Children.Remove(overlay);
                }
                
                LogMessage("Kullanıcı talebi modalı açıldı.");
            }
            catch (Exception ex)
            {
                LogMessage($"Kullanıcı talebi modalı açılırken hata: {ex.Message}");
                System.Windows.MessageBox.Show($"Kullanıcı talebi modalı açılırken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnClearLog_Click(object sender, RoutedEventArgs e)
        {
            txtLog.Clear();
            UpdateLogStatistics();
            LogMessage("Log temizlendi.");
        }

        private void btnExportLog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var saveFileDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "Metin Dosyası (*.txt)|*.txt|Tüm Dosyalar (*.*)|*.*",
                    DefaultExt = "txt",
                    FileName = $"log_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    File.WriteAllText(saveFileDialog.FileName, txtLog.Text);
                    LogMessage($"Log dosyası dışa aktarıldı: {saveFileDialog.FileName}");
                    ShowEmbeddedAlert(
                        "✅ Log Dışa Aktarıldı!",
                        $"Log dosyası başarıyla dışa aktarıldı.\nDosya: {Path.GetFileName(saveFileDialog.FileName)}",
                        "Tamam",
                        "",
                        () => {
                            // Onaylandığında hiçbir şey yapma, sadece kapat
                        }
                    );
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Log dışa aktarılırken hata oluştu: {ex.Message}");
                System.Windows.MessageBox.Show($"Log dışa aktarılırken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateLogStatistics()
        {
            try
            {
                if (txtLog != null)
                {
                    var logText = txtLog.Text;
                    var lines = logText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    var lineCount = lines.Length;
                    
                    var errorCount = lines.Count(line => line.Contains("✗") || line.Contains("❌") || line.ToLower().Contains("hata"));
                    var warningCount = lines.Count(line => line.Contains("⚠") || line.ToLower().Contains("uyarı"));
                    var successCount = lines.Count(line => line.Contains("★") || line.Contains("✅") || line.Contains("✓") || line.ToLower().Contains("başarı"));
                    
                    if (txtLogLineCount != null) txtLogLineCount.Text = $"{lineCount} satır";
                    if (txtLogErrorCount != null) txtLogErrorCount.Text = $"{errorCount} hata";
                    if (txtLogWarningCount != null) txtLogWarningCount.Text = $"{warningCount} uyarı";
                    if (txtLogSuccessCount != null) txtLogSuccessCount.Text = $"{successCount} başarı";
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Log istatistikleri güncellenirken hata: {ex.Message}");
            }
        }



        private void btnReportError_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Ekran görüntüsü al
                var screenshotPath = TakeScreenshot();
                
                // Hata raporu oluştur
                var errorReport = CreateErrorReport();
                
                // Outlook Classic'i aç ve mail gönder
                SendErrorReportViaOutlook(errorReport, screenshotPath);
                
                LogMessage("Hata raporu başarıyla gönderildi.");
                ShowEmbeddedAlert(
                    "✅ Hata Raporu Gönderildi!",
                    "Hata raporu başarıyla gönderildi.",
                    "Tamam",
                    "",
                    () => {
                        // Onaylandığında hiçbir şey yapma, sadece kapat
                    }
                );
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Hata raporu gönderilirken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                LogMessage($"Hata raporu gönderilirken hata oluştu: {ex.Message}");
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("CA1416", "Validate platform compatibility")]
        private string TakeScreenshot()
        {
            try
            {
#if WINDOWS
                var screenshotPath = Path.Combine(Path.GetTempPath(), $"ErrorScreenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png");
                
                // Ekran görüntüsü al
                using (var bitmap = new Bitmap(System.Windows.Forms.Screen.PrimaryScreen.Bounds.Width, System.Windows.Forms.Screen.PrimaryScreen.Bounds.Height))
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.CopyFromScreen(0, 0, 0, 0, bitmap.Size);
                    bitmap.Save(screenshotPath, System.Drawing.Imaging.ImageFormat.Png);
                }
                
                return screenshotPath;
#else
                // Windows dışı platformlar için ekran görüntüsü alınamaz
                LogMessage("Windows dışı platformlarda ekran görüntüsü alınamıyor.");
                return null;
#endif
            }
            catch (Exception ex)
            {
                LogMessage($"Ekran görüntüsü alınırken hata: {ex.Message}");
                return null;
            }
        }

        private string CreateErrorReport()
        {
            var report = new System.Text.StringBuilder();
            report.AppendLine("=== HATA RAPORU ===");
            report.AppendLine($"Tarih/Saat: {DateTime.Now:dd.MM.yyyy HH:mm:ss}");
            report.AppendLine($"Kullanıcı: {GetLastLoginEmail()}");
            report.AppendLine($"Bilgisayar: {Environment.MachineName}");
            report.AppendLine($"İşletim Sistemi: {Environment.OSVersion}");
            report.AppendLine($"Uygulama: Ödeme Emri Oluşturucu v1.0");
            report.AppendLine();
            report.AppendLine("=== SON LOG MESAJLARI ===");
            report.AppendLine(txtLog.Text);
            report.AppendLine();
            report.AppendLine("=== SİSTEM BİLGİLERİ ===");
            report.AppendLine($"Çalışma Dizini: {Environment.CurrentDirectory}");
            report.AppendLine($"Bellek Kullanımı: {GC.GetTotalMemory(false) / 1024 / 1024} MB");
            
            return report.ToString();
        }

        private string GetLastLoginEmail()
        {
            try
            {
                // Log mesajlarından son giriş yapılan mail adresini bul
                var logLines = txtLog.Text.Split('\n');
                for (int i = logLines.Length - 1; i >= 0; i--)
                {
                    var line = logLines[i];
                    if (line.Contains("Sisteme başarıyla giriş yapıldı:") && line.Contains("@"))
                    {
                        // "Sisteme başarıyla giriş yapıldı:" kısmından sonrasını al
                        var searchText = "Sisteme başarıyla giriş yapıldı:";
                        var startIndex = line.IndexOf(searchText) + searchText.Length;
                        var email = line.Substring(startIndex).Trim();
                        return email;
                    }
                }
            }
            catch { }
            
            return Environment.UserName; // Fallback olarak sistem kullanıcı adı
        }

        private void SendErrorReportViaOutlook(string errorReport, string screenshotPath)
        {
            try
            {
                // Windows 11'de Outlook 365 için gelişmiş yöntem kullan
                try
                {
                    // Outlook 365 için mailto protokolü ile gelişmiş yöntem dene
                    SendEmailViaOutlook365(errorReport, screenshotPath);
                    LogMessage("✅ Outlook 365 ile hata raporu hazırlandı.");
                }
                catch (Exception outlook365Ex)
                {
                    LogMessage($"⚠️ Outlook 365 yöntem başarısız: {outlook365Ex.Message}, klasik yöntem deneniyor...");

                    // Outlook 365 başarısız olursa klasik outlook.exe yöntemini dene
                    var outlookProcess = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "outlook.exe",
                            Arguments = $"/c ipm.note /m \"furkan.ozmen@guleryuzgroup.com; furkanozm@gmail.com?subject=Hata Raporu - Ödeme Emri Oluşturucu&body={Uri.EscapeDataString(errorReport)}\"",
                            UseShellExecute = true
                        }
                    };

                    outlookProcess.Start();
                    LogMessage("✅ Outlook klasik yöntemle açıldı ve hata raporu hazırlandı.");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"❌ Outlook açılırken hata: {ex.Message}");
                throw;
            }
        }

        private void SendEmailViaOutlook365(string errorReport, string screenshotPath)
        {
            try
            {
                // Outlook 365 için gelişmiş mailto protokolü kullan
                var to = "furkan.ozmen@guleryuzgroup.com;furkan.ozm@gmail.com";
                var subject = "Hata Raporu - Ödeme Emri Oluşturucu";
                var body = errorReport;

                // Özel encoding ile mailto URL oluştur
                var mailtoUrl = $"mailto:{to}?subject={Uri.EscapeDataString(subject)}&body={Uri.EscapeDataString(body)}";

                // Outlook 365'i açmak için outlook: protokolünü dene
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "outlook",
                        Arguments = $"mailto:{to}?subject={Uri.EscapeDataString(subject)}&body={Uri.EscapeDataString(body)}",
                        UseShellExecute = true
                    });
                }
                catch
                {
                    // outlook komutu başarısız olursa outlook.exe'yi dene
                    var outlookPaths = new[]
                    {
                        @"C:\Program Files\Microsoft Office\root\Office16\OUTLOOK.EXE",
                        @"C:\Program Files (x86)\Microsoft Office\root\Office16\OUTLOOK.EXE",
                        @"C:\Program Files\Microsoft Office\Office16\OUTLOOK.EXE",
                        @"C:\Program Files (x86)\Microsoft Office\Office16\OUTLOOK.EXE"
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
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = foundOutlookPath,
                            Arguments = $"/c ipm.note /m \"{mailtoUrl}\"",
                            UseShellExecute = false
                        });
                    }
                    else
                    {
                        // Son çare olarak varsayılan mail uygulamasını kullan
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = mailtoUrl,
                            UseShellExecute = true
                        });
                    }
                }

                LogMessage("📧 Outlook 365 mail penceresi açıldı.");
            }
            catch (Exception ex)
            {
                LogMessage($"❌ Outlook 365 hatası: {ex.Message}");
                throw;
            }
        }

        private void SendPasswordResetViaOutlookCOM(string to, string subject, string body)
        {
            try
            {
                // Outlook 365 için gelişmiş yöntem kullan
                var mailtoUrl = $"mailto:{to}?subject={Uri.EscapeDataString(subject)}&body={Uri.EscapeDataString(body)}";

                // Outlook 365'i açmak için outlook: protokolünü dene
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "outlook",
                        Arguments = mailtoUrl,
                        UseShellExecute = true
                    });
                }
                catch
                {
                    // outlook komutu başarısız olursa outlook.exe'yi dene
                    var outlookPaths = new[]
                    {
                        @"C:\Program Files\Microsoft Office\root\Office16\OUTLOOK.EXE",
                        @"C:\Program Files (x86)\Microsoft Office\root\Office16\OUTLOOK.EXE",
                        @"C:\Program Files\Microsoft Office\Office16\OUTLOOK.EXE",
                        @"C:\Program Files (x86)\Microsoft Office\Office16\OUTLOOK.EXE"
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
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = foundOutlookPath,
                            Arguments = $"/c ipm.note /m \"{mailtoUrl}\"",
                            UseShellExecute = false
                        });
                    }
                    else
                    {
                        // Son çare olarak varsayılan mail uygulamasını kullan
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = mailtoUrl,
                            UseShellExecute = true
                        });
                    }
                }

                LogMessage("📧 Outlook 365 şifre sıfırlama maili açıldı.");
            }
            catch (Exception ex)
            {
                LogMessage($"❌ Outlook 365 şifre sıfırlama hatası: {ex.Message}");
                throw;
            }
        }

        private void btnOpenOutputFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Kök dizindeki cikti klasörünü aç
                var defaultOutputPath = Path.Combine(Directory.GetCurrentDirectory(), "cikti");
                
                if (Directory.Exists(defaultOutputPath))
                {
                    Process.Start("explorer.exe", defaultOutputPath);
                    LogMessage($"Çıktı klasörü açıldı: {defaultOutputPath}");
                }
                else
                {
                    // Klasör yoksa oluştur ve aç
                    Directory.CreateDirectory(defaultOutputPath);
                    Process.Start("explorer.exe", defaultOutputPath);
                    LogMessage($"Çıktı klasörü oluşturuldu ve açıldı: {defaultOutputPath}");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Klasör açılırken hata oluştu: {ex.Message}");
                System.Windows.MessageBox.Show($"Klasör açılırken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnExportReport_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var saveFileDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "Excel Dosyası (*.xlsx)|*.xlsx|Tüm Dosyalar (*.*)|*.*",
                    DefaultExt = "xlsx",
                    FileName = $"scraper_report_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    _scraperService.ExportReport(saveFileDialog.FileName);
                    System.Windows.MessageBox.Show("Rapor başarıyla oluşturuldu.", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
                    LogMessage($"Rapor oluşturuldu: {saveFileDialog.FileName}");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Rapor oluşturulurken hata oluştu: {ex.Message}");
                System.Windows.MessageBox.Show($"Rapor oluşturulurken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }



        private void btnSaveSettings_Click(object sender, RoutedEventArgs e)
        {
            SaveSettings();
        }

        private void btnResetSettings_Click(object sender, RoutedEventArgs e)
        {
            var result = System.Windows.MessageBox.Show("Kullanıcı adı, parola ve TOTP secret alanlarını temizlemek istediğinizden emin misiniz?", 
                "Onay", MessageBoxButton.YesNo, MessageBoxImage.Question);
            
            if (result == MessageBoxResult.Yes)
            {
                // Önce checkbox'ı işaretle ve alanları etkinleştir
                chkAutoLogin.IsChecked = true;
                
                // Kullanıcı adı, parola ve TOTP secret alanlarını temizle
                txtUsername.Text = "";
                txtPassword.Password = "";
                txtTotpSecret.Password = "";
                txtTotpSecretVisible.Text = "";
                
                // Checkbox'ı tekrar kapat
                chkAutoLogin.IsChecked = false;
                
                System.Windows.MessageBox.Show("Kullanıcı adı, parola ve TOTP secret alanları temizlendi.", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
                LogMessage("Kullanıcı adı, parola ve TOTP secret alanları temizlendi.");
            }
        }

        private void btnTotpInfo_Click(object sender, RoutedEventArgs e)
        {
            var totpInfoModal = new TotpInfoModal();
            totpInfoModal.Owner = this;
            totpInfoModal.ShowDialog();
        }

        private void btnToggleTotpSecret_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (txtTotpSecret.Visibility == Visibility.Visible)
                {
                    // Gizli'den görünür'e geç
                    txtTotpSecretVisible.Text = txtTotpSecret.Password;
                    txtTotpSecret.Visibility = Visibility.Collapsed;
                    txtTotpSecretVisible.Visibility = Visibility.Visible;
                    btnToggleTotpSecret.Content = "🙈";
                    btnToggleTotpSecret.ToolTip = "TOTP Secret'ı gizle";
                }
                else
                {
                    // Görünür'den gizli'ye geç
                    txtTotpSecret.Password = txtTotpSecretVisible.Text;
                    txtTotpSecretVisible.Visibility = Visibility.Collapsed;
                    txtTotpSecret.Visibility = Visibility.Visible;
                    btnToggleTotpSecret.Content = "👁️";
                    btnToggleTotpSecret.ToolTip = "TOTP Secret'ı göster";
                }
            }
            catch (Exception ex)
            {
                LogMessage($"TOTP Secret göster/gizle hatası: {ex.Message}");
            }
        }

        // Event handlers for settings changes
        private void chkAutoLogin_Changed(object sender, RoutedEventArgs e)
        {
            var isEnabled = chkAutoLogin.IsChecked ?? false;
            txtUsername.IsEnabled = isEnabled;
            txtPassword.IsEnabled = isEnabled;
                            cmbCompanyCode.IsEnabled = isEnabled;
            txtTotpSecret.IsEnabled = isEnabled;
        }

        private void chkHeadlessMode_Changed(object sender, RoutedEventArgs e)
        {
            var isHeadless = chkHeadlessMode.IsChecked ?? true;
            LogMessage($"Gizli mod ayarı değiştirildi: {(isHeadless ? "Açık" : "Kapalı")}");
        }



        // Service event handlers
        private void OnProgressChanged(object? sender, System.ComponentModel.ProgressChangedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                // Progress percentage yerine gerçek sayıları kullan
                var completed = e.ProgressPercentage;
                var total = e.UserState as int? ?? 100;
                UpdateProgress(completed, total);
            });
        }

        private void OnStatusChanged(object? sender, StatusChangedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                UpdateStatus(e.Status, e.Detail, e.StatusType);

                // İşlem tamamlandıysa, counters'ları ana UI'da göster
                if (e.StatusType == StatusType.Success && e.Status.Contains("Tamamlandı"))
                {
                    // Loading overlay kapanmadan önce değerleri koru
                    // İstatistikler modaldan kaldırıldı
                }
            });
        }

        private void OnLogMessage(object? sender, LogMessageEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                LogMessage(e.Message);

                // Log mesajından istatistikleri çıkar
                var message = e.Message.ToLower();
                if (LoadingOverlay != null && LoadingOverlay.Visibility == Visibility.Visible)
                {
                    // Bulunan dosya sayısını çıkar
                    if (message.Contains("adet onaylandı dosya bulundu"))
                    {
                        // İstatistikler modaldan kaldırıldı
                    }
                }
            });
        }

        private void OnFoundChanged(object? sender, FoundChangedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                // Sadece ana UI'daki bulunan dosya sayısını güncelle
                if (txtFoundFiles != null)
                {
                    txtFoundFiles.Text = e.FoundCount.ToString();
                }
            });
        }

        private void OnDownloadedChanged(object? sender, DownloadedChangedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                // Sadece ana UI'daki indirilen dosya sayısını güncelle
                if (txtDownloadedFiles != null)
                {
                    txtDownloadedFiles.Text = e.DownloadedCount.ToString();
                }
            });
        }

        private void OnTotalAmountChanged(object? sender, TotalAmountChangedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                if (txtTotalAmount != null)
                {
                    txtTotalAmount.Text = $"{e.TotalAmount:N2} TL";
                }

                // Geçmiş için toplam tutarı güncelle
                _currentTotalAmount = e.TotalAmount;
            });
        }

        private void StatusTimer_Tick(object? sender, EventArgs e)
        {
            if (txtStatusTime != null)
            {
                if (_isTimerRunning)
                {
                    var elapsed = DateTime.Now - _scrapingStartTime;
                    txtStatusTime.Text = $"{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
                }
                else
                {
                    txtStatusTime.Text = "00:00";
                }
            }
        }

        // UI update methods
        private void UpdateStatus(string status, string detail, StatusType statusType)
        {
            if (txtStatus != null) txtStatus.Text = status;
            if (txtStatusDetail != null) txtStatusDetail.Text = detail;
            if (txtStatusBar != null) txtStatusBar.Text = status;

            var color = statusType switch
            {
                StatusType.Ready => "#4CAF50",
                StatusType.Processing => "#2196F3",
                StatusType.Success => "#4CAF50",
                StatusType.Warning => "#FF9800",
                StatusType.Error => "#F44336",
                _ => "#888888"
            };

            if (statusIndicator != null)
            {
                statusIndicator.Fill = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
            }

            // Loading overlay'de istatistikleri göster
            if (LoadingOverlay != null && LoadingOverlay.Visibility == Visibility.Visible)
            {
                // İndirme işlemi sırasında istatistikleri göster
                if (status.Contains("İndirme") || status.Contains("İndiriliyor") ||
                    status.Contains("bulundu") || status.Contains("atlandı") ||
                    status.Contains("tamamlandı") || detail.Contains("adet"))
                {
                    // İstatistikler modaldan kaldırıldı
                }

                // İstatistikler modaldan kaldırıldı
            }
        }

        private void UpdateProgress(int current, int total)
        {
            if (total > 0)
            {
                if (progressBar != null) progressBar.Value = (double)current / total * 100;
                if (txtProgress != null) txtProgress.Text = $"{current}/{total}";
            }
            else
            {
                if (progressBar != null) progressBar.Value = 0;
                if (txtProgress != null) txtProgress.Text = "0/0";
            }
        }

        private void UpdateCounters(int found, int downloaded, decimal totalAmount)
        {
            if (txtFoundFiles != null) txtFoundFiles.Text = found.ToString();

            // Loading overlay'de istatistikleri güncelle
            if (LoadingOverlay != null && LoadingOverlay.Visibility == Visibility.Visible)
            {
                // İstatistikler modaldan kaldırıldı
            }
            if (txtDownloadedFiles != null) txtDownloadedFiles.Text = downloaded.ToString();
            if (txtTotalAmount != null) txtTotalAmount.Text = $"{totalAmount:N2} TL";
        }

        private void LogMessage(string message)
        {
            if (txtLog != null)
            {
                var timestamp = DateTime.Now.ToString("HH:mm:ss");
                var logEntry = $"[{timestamp}] {message}\n";
                txtLog.AppendText(logEntry);
                txtLog.ScrollToEnd();
                
                // Log istatistiklerini güncelle
                UpdateLogStatistics();
            }

            // Önemli mesajları loading overlay'de de göster
            if (LoadingOverlay != null && LoadingOverlay.Visibility == Visibility.Visible)
            {
                var lowerMessage = message.ToLower();
                var type = "info";

                // Mesaj türünü belirle
                if (lowerMessage.Contains("hata") || lowerMessage.Contains("hatası"))
                    type = "error";
                else if (lowerMessage.Contains("uyarı") || lowerMessage.Contains("dikkat"))
                    type = "warning";
                else if (lowerMessage.Contains("tamamlandı") || lowerMessage.Contains("başarılı") || lowerMessage.Contains("yüklendi"))
                    type = "success";
                else if (lowerMessage.Contains("başlatıldı") || lowerMessage.Contains("başlatılıyor") || 
                         lowerMessage.Contains("indiriliyor") || lowerMessage.Contains("bulundu") ||
                         lowerMessage.Contains("login") || lowerMessage.Contains("giriş") ||
                         lowerMessage.Contains("chrome") || lowerMessage.Contains("tarayıcı"))
                    type = "info";

                // Önemli mesajları loading overlay'de göster
                if (type != "info" || lowerMessage.Contains("başlatıldı") || lowerMessage.Contains("başlatılıyor") ||
                    lowerMessage.Contains("indiriliyor") || lowerMessage.Contains("bulundu") ||
                    lowerMessage.Contains("login") || lowerMessage.Contains("giriş") ||
                    lowerMessage.Contains("chrome") || lowerMessage.Contains("tarayıcı") ||
                    lowerMessage.Contains("tamamlandı") || lowerMessage.Contains("başarılı"))
                {
                    AddLogMessageToOverlay(message, type);
                }
            }
        }



        private void btnSelectOutputFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
#if WINDOWS
                var folderDialog = new System.Windows.Forms.FolderBrowserDialog
                {
                    Description = "Çıktı klasörünü seçin",
                    ShowNewFolderButton = true
                };

                if (folderDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    txtOutputFolder.Text = folderDialog.SelectedPath;
                    LogMessage($"Çıktı klasörü seçildi: {folderDialog.SelectedPath}");
                }
#else
                // Windows dışı platformlar için alternatif çözüm
                var result = System.Windows.MessageBox.Show("Windows dışı platformlarda klasör seçimi desteklenmiyor. Manuel olarak klasör yolunu girin.", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
                LogMessage("Windows dışı platformlarda klasör seçimi desteklenmiyor.");
#endif
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Klasör seçilirken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                LogMessage($"Klasör seçilirken hata oluştu: {ex.Message}");
            }
        }

        private void btnClearOutputFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var result = System.Windows.MessageBox.Show("Çıktı klasörü ayarını temizlemek istediğinizden emin misiniz?", 
                    "Onay", MessageBoxButton.YesNo, MessageBoxImage.Question);
                
                if (result == MessageBoxResult.Yes)
                {
                    txtOutputFolder.Text = "";
                    
                    // Ayarları kaydet
                    SaveSettings();
                    
                    LogMessage("Çıktı klasörü ayarı temizlendi ve kaydedildi.");
                    System.Windows.MessageBox.Show("Çıktı klasörü ayarı temizlendi ve kaydedildi.", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Klasör ayarı temizlenirken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                LogMessage($"Klasör ayarı temizlenirken hata oluştu: {ex.Message}");
            }
        }

        private void rbNormalPayment_Checked(object sender, RoutedEventArgs e)
        {
            LogMessage("Normal Ödeme Emri seçildi");
            UpdatePageSelectionStyle("normal");
        }

        private void rbAdvancePayment_Checked(object sender, RoutedEventArgs e)
        {
            LogMessage("Avans Ödeme Emri seçildi");
            UpdatePageSelectionStyle("advance");
        }

        private void chkIslemTuru_Checked(object sender, RoutedEventArgs e)
        {
            LogMessage("Onaylandılar için işlem yap seçildi (Switch sağda)");
            
            // Label renklerini güncelle - Onaylandı aktif
            try
            {
                if (txtTaslakLabel != null && txtOnaylandiLabel != null)
                {
                    txtTaslakLabel.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(102, 102, 102)); // #666 - Soluk
                    txtOnaylandiLabel.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 0, 0)); // #000 - Koyu siyah
                }
                
                // Mod yazısını güncelle - Onay Modu
                if (txtModLabel != null)
                {
                    txtModLabel.Text = "ONAY MODU";
                    txtModLabel.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(33, 150, 243)); // Mavi
                }
                
                // Icon'u güncelle - Onay Modu için mavi icon
                if (txtPageIcon != null)
                {
                    txtPageIcon.Text = "📋";
                    txtPageIcon.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(33, 150, 243)); // Mavi
                }
                
                // GroupBox arka plan rengini güncelle - Onay Modu için mavi tonları
                if (gbPageSelection != null)
                {
                    var gradient = new System.Windows.Media.LinearGradientBrush();
                    gradient.StartPoint = new System.Windows.Point(0, 0);
                    gradient.EndPoint = new System.Windows.Point(1, 1);
                    gradient.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromRgb(240, 248, 255), 0)); // Çok açık mavi
                    gradient.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromRgb(220, 240, 250), 0.5)); // Orta mavi
                    gradient.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromRgb(200, 230, 245), 1)); // Koyu mavi
                    gbPageSelection.Background = gradient;
                    gbPageSelection.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(33, 150, 243)); // Mavi border
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Label renkleri güncellenirken hata: {ex.Message}");
            }
        }

        private void chkIslemTuru_Unchecked(object sender, RoutedEventArgs e)
        {
            LogMessage("Taslaktakiler için işlem yap seçildi (Switch solda)");
            
            // Label renklerini güncelle - Taslak aktif
            try
            {
                if (txtTaslakLabel != null && txtOnaylandiLabel != null)
                {
                    txtTaslakLabel.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 0, 0)); // #000 - Koyu siyah
                    txtOnaylandiLabel.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(102, 102, 102)); // #666 - Soluk
                }
                
                // Mod yazısını güncelle - Taslak Modu
                if (txtModLabel != null)
                {
                    txtModLabel.Text = "TASLAK MODU";
                    txtModLabel.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(76, 175, 80)); // Yeşil
                }
                
                // Icon'u güncelle - Taslak Modu için yeşil icon
                if (txtPageIcon != null)
                {
                    txtPageIcon.Text = "📝";
                    txtPageIcon.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(76, 175, 80)); // Yeşil
                }
                
                // GroupBox arka plan rengini güncelle - Taslak Modu için yeşil tonları
                if (gbPageSelection != null)
                {
                    var gradient = new System.Windows.Media.LinearGradientBrush();
                    gradient.StartPoint = new System.Windows.Point(0, 0);
                    gradient.EndPoint = new System.Windows.Point(1, 1);
                    gradient.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromRgb(240, 255, 240), 0)); // Çok açık yeşil
                    gradient.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromRgb(220, 250, 220), 0.5)); // Orta yeşil
                    gradient.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromRgb(200, 240, 200), 1)); // Koyu yeşil
                    gbPageSelection.Background = gradient;
                    gbPageSelection.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(76, 175, 80)); // Yeşil border
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Label renkleri güncellenirken hata: {ex.Message}");
            }
        }

        private void UpdatePageSelectionStyle(string pageType)
        {
            try
            {
                if (pageType == "normal")
                {
                    // Normal ödeme emri için mavi tonları
                    gbPageSelection.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(33, 150, 243)); // #2196F3
                    var gradient1 = new System.Windows.Media.LinearGradientBrush();
                    gradient1.StartPoint = new System.Windows.Point(0, 0);
                    gradient1.EndPoint = new System.Windows.Point(1, 1);
                    gradient1.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromRgb(240, 248, 255), 0)); // AliceBlue
                    gradient1.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromRgb(230, 240, 250), 1)); // Açık mavi
                    gbPageSelection.Background = gradient1;
                    
                    // Normal ödeme emri için icon
                    txtPageIcon.Text = "📄";
                    txtPageIcon.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(33, 150, 243));
                }
                else if (pageType == "advance")
                {
                    // Avans ödeme emri için yeşil tonları
                    gbPageSelection.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(76, 175, 80)); // #4CAF50
                    var gradient2 = new System.Windows.Media.LinearGradientBrush();
                    gradient2.StartPoint = new System.Windows.Point(0, 0);
                    gradient2.EndPoint = new System.Windows.Point(1, 1);
                    gradient2.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromRgb(240, 255, 240), 0)); // Honeydew
                    gradient2.GradientStops.Add(new System.Windows.Media.GradientStop(System.Windows.Media.Color.FromRgb(230, 250, 230), 1)); // Açık yeşil
                    gbPageSelection.Background = gradient2;
                    
                    // Avans ödeme emri için icon
                    txtPageIcon.Text = "💰";
                    txtPageIcon.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(76, 175, 80));
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Sayfa seçimi stili güncellenirken hata: {ex.Message}");
            }
        }

        #region Giriş Sistemi

        private void CheckLoginStatus()
        {
            try
            {
                // Basit giriş ekranını göster
                txtLoginMessage.Text = "Sisteme giriş yapmak için email ve şifrenizi girin:";
                ShowLoginScreen();
                
                // Beni hatırla ayarlarını yükle
                LoadRememberMeSettings();
                
                // Login email box'a odaklan
                txtLoginEmail.Focus();
            }
            catch (Exception ex)
            {
                LogMessage($"Giriş kontrolü sırasında hata: {ex.Message}");
                ShowMainApplication(); // Hata durumunda ana uygulamayı göster
            }
        }

        private void ShowLoginScreen()
        {
            LoginPanel.Visibility = Visibility.Visible;
            MainPanel.Visibility = Visibility.Collapsed;
            txtLoginError.Visibility = Visibility.Collapsed;
            txtLoginPassword.Clear();
            
            // Email'i temizleme - Beni hatırla ayarları korunacak
            // txtLoginEmail.Clear(); // Bu satırı kaldırdık
            
            // Şifre tekrarı alanını gizle
            txtPasswordConfirm.Visibility = Visibility.Collapsed;
        }

        public void ShowMainApplication()
        {
            LoginPanel.Visibility = Visibility.Collapsed;
            MainPanel.Visibility = Visibility.Visible;
            // Ödeme emri panelini göster
            ShowPaymentOrderPanel();
            // Email'i temizleme - Beni hatırla ayarları korunacak
            // txtLoginEmail.Clear(); // Bu satırı kaldırdık
            txtLoginPassword.Clear();
        }

        private void ShowPaymentOrderPanel()
        {
            PaymentOrderPanel.Visibility = Visibility.Visible;
            LogMessage("Ödeme emri paneli gösteriliyor.");
        }

        private void btnLogin_Click(object sender, RoutedEventArgs e)
        {
            ProcessLogin();
        }

        private void txtLoginEmail_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                txtLoginPassword.Focus();
            }
        }

        private void txtLoginEmail_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Email değiştiğinde hata mesajını temizle
            if (txtLoginError.Visibility == Visibility.Visible)
            {
                txtLoginError.Visibility = Visibility.Collapsed;
            }
        }

        private void txtLoginPassword_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ProcessLogin();
            }
        }
        


        private async void ProcessLogin()
        {
            try
            {
                var email = txtLoginEmail.Text?.Trim();
                var password = txtLoginPassword.Password;
                
                if (string.IsNullOrEmpty(email))
                {
                    ShowLoginError("Lütfen email adresinizi girin.");
                    txtLoginEmail.Focus();
                    return;
                }
                
                if (string.IsNullOrEmpty(password))
                {
                    ShowLoginError("Lütfen şifrenizi girin.");
                    txtLoginPassword.Focus();
                    return;
                }

                // Email formatı kontrolü
                if (!IsValidEmail(email))
                {
                    ShowLoginError("Geçerli bir email adresi girin.");
                    txtLoginEmail.Focus();
                    return;
                }

                // Firebase ile giriş yap
                var success = await _firebaseAuth.LoginAsync(email, password);
                
                if (success)
                {
                    // Beni hatırla ayarlarını kaydet
                    SaveRememberMeSettings(email);
                    
                    // Başarı toast'u göster
                    ShowToast($"🎉 Hoş geldiniz! Sisteme başarıyla giriş yapıldı.", "✅", "success");
                    
                    ShowMainApplication();
                    LogMessage($"Sisteme başarıyla giriş yapıldı: {email}");
                }
                else
                {
                    ShowLoginError("Giriş başarısız. Lütfen email ve şifrenizi kontrol edin. Firebase Console'dan kullanıcının doğru oluşturulduğundan emin olun.");
                    txtLoginPassword.Clear();
                    txtLoginPassword.Focus();
                }
            }
            catch (Exception ex)
            {
                ShowLoginError($"Giriş sırasında hata oluştu: {ex.Message}");
                LogMessage($"Giriş hatası: {ex.Message}");
            }
        }

        private bool IsValidEmail(string email)
        {
            try
            {
                var addr = new System.Net.Mail.MailAddress(email);
                return addr.Address == email;
            }
            catch
            {
                return false;
            }
        }

        private void ShowLoginError(string message)
        {
            // Sadece toast notification göster
            ShowToast(message, "❌", "error");
        }
        
        private void ShowToast(string message, string icon = "ℹ️", string type = "info")
        {
            try
            {
                ToastMessage.Text = message;
                ToastIcon.Text = icon;
                
                // Toast tipine göre renk ayarla
                var toastBorder = ToastArea.Child as Border;
                if (toastBorder != null)
                {
                    switch (type.ToLower())
                    {
                        case "success":
                            toastBorder.Background = new System.Windows.Media.SolidColorBrush(
                                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#4CAF50")); // Yeşil
                            break;
                        case "error":
                            toastBorder.Background = new System.Windows.Media.SolidColorBrush(
                                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F44336")); // Kırmızı
                            break;
                        default:
                            toastBorder.Background = new System.Windows.Media.SolidColorBrush(
                                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#333333")); // Koyu gri
                            break;
                    }
                }
                
                // Animasyonlu giriş
                ShowToastWithAnimation();
                
                LogMessage($"Toast gösterildi: {message}");
            }
            catch (Exception ex)
            {
                LogMessage($"Toast gösterilirken hata: {ex.Message}");
            }
        }
        
        private void ShowToastWithAnimation()
        {
            // Toast'u görünür yap
            ToastArea.Visibility = Visibility.Visible;
            
            // Giriş animasyonu
            var fadeInAnimation = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(300)
            };
            
            var scaleAnimation = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 0.8,
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(300)
            };
            
            var translateAnimation = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = -20,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(300)
            };
            
            // Animasyonları başlat
            ToastArea.BeginAnimation(OpacityProperty, fadeInAnimation);
            ToastScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnimation);
            ToastScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnimation);
            ToastTranslate.BeginAnimation(TranslateTransform.YProperty, translateAnimation);
            
            // 3 saniye sonra çıkış animasyonu
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            
            timer.Tick += (sender, e) =>
            {
                HideToastWithAnimation();
                timer.Stop();
            };
            
            timer.Start();
        }
        
        private void HideToastWithAnimation()
        {
            // Çıkış animasyonu
            var fadeOutAnimation = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 1,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(300)
            };
            
            var scaleAnimation = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 1.0,
                To = 0.8,
                Duration = TimeSpan.FromMilliseconds(300)
            };
            
            var translateAnimation = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 0,
                To = -20,
                Duration = TimeSpan.FromMilliseconds(300)
            };
            
            // Animasyon tamamlandığında gizle
            fadeOutAnimation.Completed += (sender, e) =>
            {
                ToastArea.Visibility = Visibility.Collapsed;
                ToastArea.Opacity = 0;
                ToastScale.ScaleX = 0.8;
                ToastScale.ScaleY = 0.8;
                ToastTranslate.Y = -20;
            };
            
            // Animasyonları başlat
            ToastArea.BeginAnimation(OpacityProperty, fadeOutAnimation);
            ToastScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnimation);
            ToastScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnimation);
            ToastTranslate.BeginAnimation(TranslateTransform.YProperty, translateAnimation);
        }

        public void ShowSuccessAlert(string message)
        {
            try
            {
                // Yerleşik alert'i göster
                ShowEmbeddedAlert(
                    "✅ İşlem Başarıyla Tamamlandı!",
                    message,
                    "Tamam",
                    "",
                    () => {
                        // Onaylandığında hiçbir şey yapma, sadece kapat
                    }
                );
                
                LogMessage("Yerleşik başarı alert'i gösterildi.");
            }
            catch (Exception ex)
            {
                LogMessage($"Başarı alert'i gösterilirken hata: {ex.Message}");
                System.Windows.MessageBox.Show($"Başarı alert'i gösterilirken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadRememberMeSettings()
        {
            try
            {
                var rememberMeFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "remember_me.txt");
                if (File.Exists(rememberMeFile))
                {
                    var lines = File.ReadAllLines(rememberMeFile);
                    if (lines.Length >= 2)
                    {
                        var rememberMe = bool.Parse(lines[0]);
                        var savedEmail = lines[1];
                        
                        chkRememberMe.IsChecked = rememberMe;
                        if (rememberMe && !string.IsNullOrEmpty(savedEmail))
                        {
                            txtLoginEmail.Text = savedEmail;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Beni hatırla ayarları yüklenirken hata: {ex.Message}");
            }
        }

        private void SaveRememberMeSettings(string email)
        {
            try
            {
                var rememberMeFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "remember_me.txt");
                var rememberMe = chkRememberMe.IsChecked ?? false;
                
                if (rememberMe)
                {
                    // Email'i kaydet
                    File.WriteAllLines(rememberMeFile, new[] { "true", email });
                }
                else
                {
                    // Dosyayı sil
                    if (File.Exists(rememberMeFile))
                    {
                        File.Delete(rememberMeFile);
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Beni hatırla ayarları kaydedilirken hata: {ex.Message}");
            }
        }



        #endregion

        #region Mail Settings

        private void LoadKeywordList(List<KeywordNotification> keywords)
        {
            try
            {
                spRegionList.Children.Clear();
                
                var index = 0;
                foreach (var keyword in keywords)
                {
                    index++;
                    
                    // İnteraktif panel oluştur (border'sız)
                    var panel = new Border
                    {
                        Background = System.Windows.Media.Brushes.Transparent,
                        BorderThickness = new Thickness(0),
                        Margin = new Thickness(0, 0, 0, 15),
                        Padding = new Thickness(8, 8, 8, 8)
                    };

                    var grid = new Grid();
                    grid.Margin = new Thickness(0, 2, 0, 2);
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Sıra no için
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Ok işareti için
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                    // Kelime TextBox
                    var txtKeyword = new System.Windows.Controls.TextBox
                    {
                        Text = keyword.Keyword,
                        FontSize = 12,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 10, 0),
                        FontWeight = FontWeights.Bold,
                        BorderThickness = new Thickness(1),
                        BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(200, 200, 200)),
                        Background = System.Windows.Media.Brushes.White,
                        Padding = new Thickness(5, 2, 5, 2),
                        Height = 28
                    };
                    
                    // TextBox'ı Border ile sar (border radius için)
                    var keywordBorder = new Border
                    {
                        Child = txtKeyword,
                        CornerRadius = new CornerRadius(6),
                        BorderThickness = new Thickness(1),
                        BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(200, 200, 200)),
                        Background = System.Windows.Media.Brushes.White,
                        Margin = new Thickness(0, 0, 5, 0)
                    };
                    
                    // Sıra numarası (Circle Badge)
                    var indexBadge = new Border
                    {
                        Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(33, 150, 243)), // Mavi
                        CornerRadius = new CornerRadius(12), // Circle için
                        Width = 28,
                        Height = 28,
                        Margin = new Thickness(0, 0, 8, 0),
                        VerticalAlignment = VerticalAlignment.Center,
                        Child = new TextBlock
                        {
                            Text = $"{index}",
                            FontSize = 10,
                            FontWeight = FontWeights.Normal,
                            VerticalAlignment = VerticalAlignment.Center,
                            HorizontalAlignment = HorizontalAlignment.Center,
                            Foreground = System.Windows.Media.Brushes.White,
                            TextWrapping = TextWrapping.NoWrap
                        }
                    };
                    Grid.SetColumn(indexBadge, 0);

                    // TextBox'ın border'ını kaldır
                    txtKeyword.BorderThickness = new Thickness(0);
                    txtKeyword.Background = System.Windows.Media.Brushes.Transparent;
                    Grid.SetColumn(keywordBorder, 1);

                    // Ok işareti
                    var arrowText = new TextBlock
                    {
                        Text = "→",
                        FontSize = 16,
                        FontWeight = FontWeights.Bold,
                        VerticalAlignment = VerticalAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(100, 100, 100)),
                        Margin = new Thickness(5, 0, 5, 0)
                    };
                    Grid.SetColumn(arrowText, 2);

                    // E-posta TextBox
                    var txtEmail = new System.Windows.Controls.TextBox
                    {
                        Text = keyword.EmailRecipient,
                        FontSize = 12,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 15, 0),
                        BorderThickness = new Thickness(1),
                        BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(200, 200, 200)),
                        Background = System.Windows.Media.Brushes.White,
                        Padding = new Thickness(5, 2, 5, 2),
                        Height = 28
                    };
                    
                    // TextBox'ı Border ile sar (border radius için)
                    var emailBorder = new Border
                    {
                        Child = txtEmail,
                        CornerRadius = new CornerRadius(6),
                        BorderThickness = new Thickness(1),
                        BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(200, 200, 200)),
                        Background = System.Windows.Media.Brushes.White
                    };
                    
                    // TextBox'ın border'ını kaldır
                    txtEmail.BorderThickness = new Thickness(0);
                    txtEmail.Background = System.Windows.Media.Brushes.Transparent;
                    Grid.SetColumn(emailBorder, 3);

                    // Aktif/Pasif CheckBox
                    var chkEnabled = new System.Windows.Controls.CheckBox
                    {
                        IsChecked = keyword.Enabled,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(10, 0, 15, 0),
                        Content = keyword.Enabled ? "Aktif" : "Pasif"
                    };
                    chkEnabled.Checked += (s, e) => chkEnabled.Content = "Aktif";
                    chkEnabled.Unchecked += (s, e) => chkEnabled.Content = "Pasif";
                    Grid.SetColumn(chkEnabled, 4);



                    // Sil Butonu
                    var btnDelete = new Button
                    {
                        Content = "🗑️",
                        Width = 30,
                        Height = 30,
                        Background = new System.Windows.Media.LinearGradientBrush(
                            System.Windows.Media.Color.FromRgb(244, 67, 54),
                            System.Windows.Media.Color.FromRgb(211, 47, 47),
                            new System.Windows.Point(0, 0),
                            new System.Windows.Point(1, 1)
                        ),
                        Foreground = System.Windows.Media.Brushes.White,
                        BorderThickness = new Thickness(0),
                        Cursor = System.Windows.Input.Cursors.Hand,
                        FontSize = 12
                    };
                    btnDelete.Click += (s, e) => DeleteKeyword(panel);
                    Grid.SetColumn(btnDelete, 5);

                    grid.Children.Add(indexBadge);
                    grid.Children.Add(keywordBorder);
                    grid.Children.Add(arrowText);
                    grid.Children.Add(emailBorder);
                    grid.Children.Add(chkEnabled);

                    grid.Children.Add(btnDelete);

                    panel.Child = grid;
                    spRegionList.Children.Add(panel);
                }
                
                // Badge sayılarını güncelle
                UpdateBadgeCounts();
            }
            catch (Exception ex)
            {
                LogMessage($"Kelime listesi yüklenirken hata: {ex.Message}");
            }
        }

        private List<KeywordNotification> GetKeywordList()
        {
            var keywords = new List<KeywordNotification>();
            
            try
            {
                foreach (Border keywordPanel in spRegionList.Children)
                {
                    if (keywordPanel.Child is Grid grid)
                    {
                        // Border'lar içindeki TextBox'ları bul (sıra no eklendikten sonra indeksler değişti)
                        var keywordBorder = grid.Children.OfType<Border>().Skip(1).FirstOrDefault(); // İlk Border sıra no badge'i
                        var emailBorder = grid.Children.OfType<Border>().Skip(2).FirstOrDefault(); // İkinci Border email
                        var chkEnabled = grid.Children.OfType<System.Windows.Controls.CheckBox>().FirstOrDefault();

                        var txtKeyword = keywordBorder?.Child as System.Windows.Controls.TextBox;
                        var txtEmail = emailBorder?.Child as System.Windows.Controls.TextBox;

                        if (txtKeyword != null && txtEmail != null && chkEnabled != null)
                        {
                            keywords.Add(new KeywordNotification
                            {
                                Keyword = txtKeyword.Text.Trim(),
                                EmailRecipient = txtEmail.Text.Trim(),
                                Enabled = chkEnabled.IsChecked ?? false
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Kelime listesi alınırken hata: {ex.Message}");
            }

            return keywords;
        }

        private void UpdateBadgeCounts()
        {
            try
            {
                var keywords = GetKeywordList();
                var keywordCount = keywords.Count;
                var uniqueEmailCount = keywords.Select(k => k.EmailRecipient).Distinct().Count();

                txtKeywordCount.Text = $"{keywordCount} Kelime";
                txtUniqueEmailCount.Text = $"{uniqueEmailCount} Tekil Mail";
            }
            catch (Exception ex)
            {
                LogMessage($"Badge sayıları güncellenirken hata: {ex.Message}");
            }
        }

        private void chkMailNotifications_Changed(object sender, RoutedEventArgs e)
        {
            // Mail bildirimleri açılıp kapanırken UI'ı güncelle
            var isEnabled = chkMailNotifications.IsChecked ?? false;
            // btnManualSend.IsEnabled = isEnabled; // Manuel gönderim butonu her zaman aktif olsun
        }

        private void btnManualSend_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Modal overlay'ini göster
                ModalOverlay.Visibility = Visibility.Visible;
                
                // Manuel gönderim modalını aç
                var manualSendModal = new ManualSendModal();
                manualSendModal.Owner = this;
                manualSendModal.Closed += (s, args) => {
                    // Modal kapandığında overlay'i gizle
                    Dispatcher.Invoke(() => {
                        ModalOverlay.Visibility = Visibility.Collapsed;
                    });
                };
                manualSendModal.ShowDialog();
            }
            catch (Exception ex)
            {
                // Hata durumunda overlay'i gizle
                ModalOverlay.Visibility = Visibility.Collapsed;
                LogMessage($"Manuel gönderim modalı açılırken hata oluştu: {ex.Message}");
                System.Windows.MessageBox.Show($"Manuel gönderim modalı açılırken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnResetMailSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var result = System.Windows.MessageBox.Show("Mail ayarlarını sıfırlamak istediğinizden emin misiniz?", 
                    "Onay", MessageBoxButton.YesNo, MessageBoxImage.Question);
                
                if (result == MessageBoxResult.Yes)
                {
                    chkMailNotifications.IsChecked = false;
                    spRegionList.Children.Clear();
                    
                    LogMessage("Mail ayarları sıfırlandı.");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Mail ayarları sıfırlanırken hata: {ex.Message}");
            }
        }

        private void btnSaveMailSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var config = ConfigManager.LoadConfig();
                
                config.Notification.Enabled = chkMailNotifications.IsChecked ?? false;
                config.Notification.Keywords = GetKeywordList();

                ConfigManager.SaveConfig(config);
                
                LogMessage("Mail ayarları başarıyla kaydedildi.");
                ShowEmbeddedAlert(
                    "✅ Mail Ayarları Kaydedildi!",
                    "Mail ayarları başarıyla kaydedildi.",
                    "Tamam",
                    "",
                    () => {
                        // Onaylandığında hiçbir şey yapma, sadece kapat
                    }
                );
            }
            catch (Exception ex)
            {
                LogMessage($"Mail ayarları kaydedilirken hata: {ex.Message}");
                System.Windows.MessageBox.Show($"Mail ayarları kaydedilirken hata: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region Excel Import to Config

        public static void ImportExcelToConfig()
        {
            try
            {
                // EPPlus lisans ayarı - EPPlus 8+ için yeni API
                ExcelPackage.License.SetNonCommercialPersonal("WebScraper");
                
                Console.WriteLine("Excel dosyasından config.json'a veri aktarılıyor...");
                
                // Excel dosyasını oku
                var excelPath = "Kelime_Bazli_Mail_Sablonu.xlsx";
                if (!File.Exists(excelPath))
                {
                    Console.WriteLine($"Hata: {excelPath} dosyası bulunamadı!");
                    return;
                }
                
                var keywords = ReadExcelFile(excelPath);
                
                // Config dosyasını güncelle
                UpdateConfigFile(keywords);
                
                Console.WriteLine($"✅ {keywords.Count} adet kelime başarıyla config.json'a kaydedildi!");
                Console.WriteLine("Config dosyası güncellendi: config.json");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Hata: {ex.Message}");
            }
        }
        
        private static List<KeywordNotification> ReadExcelFile(string filePath)
        {
            var keywords = new List<KeywordNotification>();
            
            using var package = new ExcelPackage(new FileInfo(filePath));
            var worksheet = package.Workbook.Worksheets.FirstOrDefault();
            
            if (worksheet == null)
            {
                throw new Exception("Excel dosyasında çalışma sayfası bulunamadı.");
            }
            
            var row = 2; // İlk satır başlık
            
            while (worksheet.Cells[row, 1].Value != null)
            {
                var keyword = worksheet.Cells[row, 1].Value?.ToString()?.Trim();
                var email = worksheet.Cells[row, 2].Value?.ToString()?.Trim();
                var isActive = worksheet.Cells[row, 3].Value?.ToString()?.Trim();
                
                if (!string.IsNullOrEmpty(keyword) && !string.IsNullOrEmpty(email))
                {
                    var isEnabled = string.IsNullOrEmpty(isActive) || 
                                   isActive.Equals("Evet", StringComparison.OrdinalIgnoreCase);
                    
                    keywords.Add(new KeywordNotification
                    {
                        Keyword = keyword,
                        EmailRecipient = email,
                        Enabled = isEnabled
                    });
                    
                    Console.WriteLine($"Okunan: {keyword} → {email} ({(isEnabled ? "Aktif" : "Pasif")})");
                }
                
                row++;
            }
            
            return keywords;
        }
        
        private static void UpdateConfigFile(List<KeywordNotification> keywords)
        {
            var configPath = "config.json";
            if (!File.Exists(configPath))
            {
                throw new Exception("config.json dosyası bulunamadı!");
            }
            
            // Mevcut config'i oku
            var configJson = File.ReadAllText(configPath);
            var config = JsonSerializer.Deserialize<AppConfig>(configJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            // Keywords'ü güncelle
            config.Notification.Keywords = keywords;

            // Config'i kaydet
            var updatedConfigJson = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(configPath, updatedConfigJson);
        }

        #endregion

        private void tabControl_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            try
            {
                // Ayarlar sekmesine geçildiğinde keyword listesini yeniden yükle
                if (tabControl.SelectedIndex == 2) // Ayarlar sekmesi (artık 2. sırada)
                {
                    var config = ConfigManager.LoadConfig();
                    LoadKeywordList(config.Notification.Keywords);
                    LogMessage("Keyword listesi yeniden yüklendi.");
                }
                // Loglar sekmesine geçildiğinde istatistikleri güncelle
                else if (tabControl.SelectedIndex == 1) // Loglar sekmesi (1. sırada)
                {
                    UpdateLogStatistics();
                }
                // Mail Geçmişi sekmesine geçildiğinde mail geçmişini yükle
                else if (tabControl.SelectedIndex == 4) // Mail Geçmişi sekmesi (4. sırada)
                {
                    LoadMailHistory();
                    LogMessage("Mail geçmişi yüklendi.");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Tab değişim sırasında hata: {ex.Message}");
            }
        }

        private void btnAddKeyword_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Önce mevcut UI'daki keyword'leri al
                var currentKeywords = GetKeywordList();
                
                // Yeni keyword ekle
                var newKeyword = new KeywordNotification
                {
                    Keyword = "YENİ_KELİME",
                    EmailRecipient = "ornek@firma.com",
                    Enabled = true
                };
                
                // Mevcut keyword'lere yeni keyword'ü ekle
                currentKeywords.Add(newKeyword);

                // Config'i güncelle
                var config = ConfigManager.LoadConfig();
                config.Notification.Keywords = currentKeywords;
                ConfigManager.SaveConfig(config);

                // Listeyi yeniden yükle
                LoadKeywordList(config.Notification.Keywords);
                LogMessage("Yeni kelime eklendi. Lütfen bilgileri düzenleyip kaydedin.");
            }
            catch (Exception ex)
            {
                LogMessage($"Yeni kelime eklenirken hata: {ex.Message}");
            }
        }

        private void txtKeywordSearch_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            try
            {
                var searchText = txtKeywordSearch.Text.Trim().ToLower();
                
                // Placeholder metni ise arama yapma
                if (searchText == "arama yapabilirsiniz")
                {
                    searchText = "";
                }
                
                // Arama butonunu göster/gizle
                btnClearSearch.Visibility = string.IsNullOrEmpty(searchText) ? Visibility.Collapsed : Visibility.Visible;
                
                var visibleCount = 0;
                var totalCount = spRegionList.Children.Count;
                
                // Tüm keyword panellerini kontrol et
                foreach (Border keywordPanel in spRegionList.Children)
                {
                    if (keywordPanel.Child is Grid grid)
                    {
                        // Grid içindeki tüm TextBox'ları bul
                        var textBoxes = new List<System.Windows.Controls.TextBox>();
                        FindAllTextBoxes(grid, textBoxes);
                        
                        if (textBoxes.Count >= 2)
                        {
                            var txtKeyword = textBoxes[0]; // İlk TextBox keyword
                            var txtEmail = textBoxes[1];   // İkinci TextBox email
                            
                            var keywordText = txtKeyword.Text.ToLower();
                            var emailText = txtEmail.Text.ToLower();
                            
                            // Arama metni boşsa tümünü göster, değilse filtrele
                            var shouldShow = string.IsNullOrEmpty(searchText) || 
                                           keywordText.Contains(searchText) || 
                                           emailText.Contains(searchText);
                            
                            keywordPanel.Visibility = shouldShow ? Visibility.Visible : Visibility.Collapsed;
                            
                            if (shouldShow) visibleCount++;
                        }
                        else
                        {
                            // TextBox'lar bulunamadıysa paneli göster
                            keywordPanel.Visibility = Visibility.Visible;
                            visibleCount++;
                        }
                    }
                }
                
                // Badge sayılarını güncelle
                UpdateBadgeCounts();
            }
            catch (Exception ex)
            {
                LogMessage($"❌ Arama işlemi sırasında hata: {ex.Message}");
            }
        }
        
        private void FindAllTextBoxes(System.Windows.DependencyObject parent, List<System.Windows.Controls.TextBox> textBoxes)
        {
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                
                if (child is System.Windows.Controls.TextBox textBox)
                {
                    textBoxes.Add(textBox);
                }
                else
                {
                    FindAllTextBoxes(child, textBoxes);
                }
            }
        }

        private void btnClearSearch_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                txtKeywordSearch.Text = "Arama yapabilirsiniz";
                txtKeywordSearch.Foreground = System.Windows.Media.Brushes.Gray;
                btnClearSearch.Visibility = Visibility.Collapsed;
                
                // Tüm keyword'leri göster
                foreach (Border keywordPanel in spRegionList.Children)
                {
                    keywordPanel.Visibility = Visibility.Visible;
                }
                
                // Badge sayılarını güncelle
                UpdateBadgeCounts();
                
                LogMessage("🔍 Arama temizlendi, tüm kelimeler gösteriliyor.");
            }
            catch (Exception ex)
            {
                LogMessage($"Arama temizleme sırasında hata: {ex.Message}");
            }
        }
        
        private void KeywordCountBadge_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                // Modal overlay'ini göster
                ModalOverlay.Visibility = Visibility.Visible;
                
                var keywords = GetKeywordList();
                var keywordListModal = new KeywordListModal(keywords);
                keywordListModal.Owner = this;
                keywordListModal.Closed += (s, args) => {
                    // Modal kapandığında overlay'i gizle
                    Dispatcher.Invoke(() => {
                        ModalOverlay.Visibility = Visibility.Collapsed;
                    });
                };
                keywordListModal.ShowDialog();
            }
            catch (Exception ex)
            {
                // Hata durumunda overlay'i gizle
                ModalOverlay.Visibility = Visibility.Collapsed;
                LogMessage($"Kelime listesi modalı açılırken hata oluştu: {ex.Message}");
                System.Windows.MessageBox.Show($"Kelime listesi modalı açılırken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void UniqueEmailCountBadge_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                // Modal overlay'ini göster
                ModalOverlay.Visibility = Visibility.Visible;
                
                var keywords = GetKeywordList();
                var uniqueEmailListModal = new UniqueEmailListModal(keywords);
                uniqueEmailListModal.Owner = this;
                uniqueEmailListModal.Closed += (s, args) => {
                    // Modal kapandığında overlay'i gizle
                    Dispatcher.Invoke(() => {
                        ModalOverlay.Visibility = Visibility.Collapsed;
                    });
                };
                uniqueEmailListModal.ShowDialog();
            }
            catch (Exception ex)
            {
                // Hata durumunda overlay'i gizle
                ModalOverlay.Visibility = Visibility.Collapsed;
                LogMessage($"Tekil mail listesi modalı açılırken hata oluştu: {ex.Message}");
                System.Windows.MessageBox.Show($"Tekil mail listesi modalı açılırken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveKeywordChanges()
        {
            try
            {
                LogMessage("💾 Kelime ayarları kaydediliyor...");
                
                var keywords = new List<KeywordNotification>();
                var keywordCount = 0;
                
                // Mevcut config'i yükle
                var config = ConfigManager.LoadConfig();
                var existingKeywords = config.Notification.Keywords.ToList();
                
                foreach (Border keywordPanel in spRegionList.Children)
                {
                    if (keywordPanel.Child is Grid grid)
                    {
                        // Border'lar içindeki TextBox'ları bul
                        var keywordBorder = grid.Children.OfType<Border>().FirstOrDefault();
                        var emailBorder = grid.Children.OfType<Border>().Skip(1).FirstOrDefault();
                        var chkEnabled = grid.Children.OfType<System.Windows.Controls.CheckBox>().FirstOrDefault();

                        var txtKeyword = keywordBorder?.Child as System.Windows.Controls.TextBox;
                        var txtEmail = emailBorder?.Child as System.Windows.Controls.TextBox;

                        if (txtKeyword != null && txtEmail != null && chkEnabled != null)
                        {
                            var newKeyword = txtKeyword.Text.Trim();
                            var newEmail = txtEmail.Text.Trim();
                            var newEnabled = chkEnabled.IsChecked ?? false;
                            
                            // Mevcut keyword'ü bul ve güncelle (eğer keyword değişmemişse)
                            var existingKeyword = existingKeywords.FirstOrDefault(k => 
                                k.Keyword.Equals(newKeyword, StringComparison.OrdinalIgnoreCase) ||
                                k.EmailRecipient.Equals(newEmail, StringComparison.OrdinalIgnoreCase));
                            
                            KeywordNotification keyword;
                            if (existingKeyword != null)
                            {
                                // Mevcut keyword'ü güncelle
                                existingKeyword.Keyword = newKeyword;
                                existingKeyword.EmailRecipient = newEmail;
                                existingKeyword.Enabled = newEnabled;
                                keyword = existingKeyword;
                                
                                // Mevcut listeden kaldır ki tekrar eklenmesin
                                existingKeywords.Remove(existingKeyword);
                            }
                            else
                            {
                                // Yeni keyword oluştur
                                keyword = new KeywordNotification
                                {
                                    Keyword = newKeyword,
                                    EmailRecipient = newEmail,
                                    Enabled = newEnabled
                                };
                            }
                            
                            keywords.Add(keyword);
                            keywordCount++;
                            
                            LogMessage($"📝 Keyword {keywordCount}: '{keyword.Keyword}' -> '{keyword.EmailRecipient}' (Aktif: {keyword.Enabled})");
                        }
                    }
                }

                LogMessage($"📊 Toplam {keywordCount} keyword işlendi.");

                // Config'i güncelle
                config.Notification.Keywords = keywords;
                ConfigManager.SaveConfig(config);

                // Badge sayılarını güncelle
                UpdateBadgeCounts();

                LogMessage("✅ Kelime ayarları başarıyla kaydedildi.");
                ShowEmbeddedAlert(
                    "✅ Kelime Ayarları Kaydedildi!",
                    $"Kelime ayarları başarıyla kaydedildi. ({keywordCount} keyword)",
                    "Tamam",
                    "",
                    () => {
                        // Onaylandığında hiçbir şey yapma, sadece kapat
                    }
                );
            }
            catch (Exception ex)
            {
                LogMessage($"❌ Kelime ayarları kaydedilirken hata: {ex.Message}");
                System.Windows.MessageBox.Show($"Kelime ayarları kaydedilirken hata: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DeleteKeyword(Border keywordPanel)
        {
            try
            {
                // Silinecek keyword'ü önce bul
                string keywordToDelete = "";
                if (keywordPanel.Child is Grid grid)
                {
                    var keywordBorder = grid.Children.OfType<Border>().FirstOrDefault();
                    var txtKeyword = keywordBorder?.Child as System.Windows.Controls.TextBox;
                    if (txtKeyword != null)
                    {
                        keywordToDelete = txtKeyword.Text.Trim();
                    }
                }

                // Yerleşik alert ile onay sor
                ShowEmbeddedAlert(
                    "Kelime Silme Onayı",
                    $"'{keywordToDelete}' kelimesini silmek istediğinizden emin misiniz?",
                    "Evet, Sil",
                    "İptal",
                    () => {
                        // Panel'i listeden kaldır
                        spRegionList.Children.Remove(keywordPanel);

                        // Config'i güncelle - mevcut config'i yükle ve sadece silinen keyword'ü çıkar
                        var config = ConfigManager.LoadConfig();
                        var keywords = config.Notification.Keywords.Where(k => 
                            !k.Keyword.Equals(keywordToDelete, StringComparison.OrdinalIgnoreCase)).ToList();
                        
                        config.Notification.Keywords = keywords;
                        ConfigManager.SaveConfig(config);

                        // Badge sayılarını güncelle
                        UpdateBadgeCounts();

                        LogMessage($"🗑️ '{keywordToDelete}' kelimesi başarıyla silindi.");
                        ShowToast($"'{keywordToDelete}' kelimesi başarıyla silindi.", "✅", "success");
                    }
                );
            }
            catch (Exception ex)
            {
                LogMessage($"Kelime silinirken hata: {ex.Message}");
            }
        }
        
        // Yerleşik Alert Sistemi
        private Action? _currentAlertCallback;
        
        private void ShowEmbeddedAlert(string title, string message, string confirmText, string cancelText, Action? onConfirm = null)
        {
            try
            {
                // Alert içeriğini ayarla
                AlertTitle.Text = title;
                AlertMessage.Text = message;
                AlertConfirmButton.Content = confirmText;
                AlertCancelButton.Content = cancelText;
                
                // İptal butonu boşsa gizle
                if (string.IsNullOrEmpty(cancelText))
                {
                    AlertCancelButton.Visibility = Visibility.Collapsed;
                    AlertConfirmButton.Margin = new Thickness(0); // Margin'i sıfırla
                }
                else
                {
                    AlertCancelButton.Visibility = Visibility.Visible;
                    AlertConfirmButton.Margin = new Thickness(0, 0, 15, 0); // Normal margin
                }
                
                // Callback'i sakla
                _currentAlertCallback = onConfirm;
                
                // Alert'i göster
                AlertOverlay.Visibility = Visibility.Visible;
                
                // Animasyon ekle
                var fadeInAnimation = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 0,
                    To = 1,
                    Duration = TimeSpan.FromMilliseconds(300)
                };
                
                AlertOverlay.BeginAnimation(OpacityProperty, fadeInAnimation);
                
                // Pencereyi en öne getir
                this.Topmost = true;
                this.Activate();
                this.Focus();
                
                // ESC tuşu ile kapatma özelliği ekle
                this.KeyDown += AlertKeyDownHandler;
                
                LogMessage($"Yerleşik alert gösterildi: {title}");
            }
            catch (Exception ex)
            {
                LogMessage($"Yerleşik alert gösterilirken hata: {ex.Message}");
            }
        }
        
        private void HideEmbeddedAlert()
        {
            try
            {
                // ESC tuşu event handler'ını kaldır
                this.KeyDown -= AlertKeyDownHandler;
                
                // Animasyon ekle
                var fadeOutAnimation = new System.Windows.Media.Animation.DoubleAnimation
                {
                    From = 1,
                    To = 0,
                    Duration = TimeSpan.FromMilliseconds(300)
                };
                
                fadeOutAnimation.Completed += (sender, e) =>
                {
                    AlertOverlay.Visibility = Visibility.Collapsed;
                    _currentAlertCallback = null;
                    
                    // Topmost özelliğini kaldır
                    this.Topmost = false;
                };
                
                AlertOverlay.BeginAnimation(OpacityProperty, fadeOutAnimation);
            }
            catch (Exception ex)
            {
                LogMessage($"Yerleşik alert gizlenirken hata: {ex.Message}");
            }
        }
        
        private void AlertConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Callback'i çalıştır
                _currentAlertCallback?.Invoke();
                
                // Alert'i gizle
                HideEmbeddedAlert();
            }
            catch (Exception ex)
            {
                LogMessage($"Alert onay butonu hatası: {ex.Message}");
                HideEmbeddedAlert();
            }
        }
        
        private void AlertCancelButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Alert'i gizle
                HideEmbeddedAlert();
            }
            catch (Exception ex)
            {
                LogMessage($"Alert iptal butonu hatası: {ex.Message}");
                HideEmbeddedAlert();
            }
        }
        
        private void AlertKeyDownHandler(object sender, KeyEventArgs e)
        {
            try
            {
                // ESC tuşuna basıldığında alert'ı kapat
                if (e.Key == Key.Escape)
                {
                    HideEmbeddedAlert();
                    e.Handled = true; // Event'i işlendi olarak işaretle
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Alert ESC tuşu hatası: {ex.Message}");
            }
        }

        #region Mail Geçmişi Event Handlers

        private void btnRefreshMailHistory_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                LoadMailHistory();
                LogMessage("Mail geçmişi yenilendi.");
            }
            catch (Exception ex)
            {
                LogMessage($"Mail geçmişi yenilenirken hata: {ex.Message}");
            }
        }

        private void btnClearMailHistory_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ShowEmbeddedAlert("Mail Geçmişi Temizleme", 
                    "Tüm mail geçmişini silmek istediğinizden emin misiniz? Bu işlem geri alınamaz.", 
                    "Evet", "İptal",
                    () =>
                    {
                        _mailHistoryService.ClearAllHistory();
                        LoadMailHistory();
                        LogMessage("Mail geçmişi temizlendi.");
                    });
            }
            catch (Exception ex)
            {
                LogMessage($"Mail geçmişi temizlenirken hata: {ex.Message}");
            }
        }

        private void btnExportMailHistory_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var saveFileDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "Excel Dosyası (*.xlsx)|*.xlsx|CSV Dosyası (*.csv)|*.csv|JSON Dosyası (*.json)|*.json",
                    DefaultExt = "xlsx",
                    FileName = $"Mail_Geçmişi_{DateTime.Now:yyyyMMdd_HHmmss}"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    var mailHistory = _mailHistoryService.GetAllMailHistory();
                    
                    if (saveFileDialog.FileName.EndsWith(".xlsx"))
                    {
                        ExportMailHistoryToExcel(mailHistory, saveFileDialog.FileName);
                    }
                    else if (saveFileDialog.FileName.EndsWith(".csv"))
                    {
                        ExportMailHistoryToCsv(mailHistory, saveFileDialog.FileName);
                    }
                    else if (saveFileDialog.FileName.EndsWith(".json"))
                    {
                        ExportMailHistoryToJson(mailHistory, saveFileDialog.FileName);
                    }

                    LogMessage($"Mail geçmişi dışa aktarıldı: {saveFileDialog.FileName}");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Mail geçmişi dışa aktarılırken hata: {ex.Message}");
            }
        }

        private void txtMailHistorySearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                FilterMailHistory();
            }
            catch (Exception ex)
            {
                LogMessage($"Mail geçmişi arama hatası: {ex.Message}");
            }
        }

        private void cmbMailStatusFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                FilterMailHistory();
            }
            catch (Exception ex)
            {
                LogMessage($"Mail geçmişi filtreleme hatası: {ex.Message}");
            }
        }

        private void btnClearMailFilters_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                txtMailHistorySearch.Text = "Mail geçmişinde arama yapabilirsiniz";
                txtMailHistorySearch.Foreground = System.Windows.Media.Brushes.Gray;
                cmbMailStatusFilter.SelectedIndex = 0;
                LoadMailHistory(); // Filtreleri temizledikten sonra tüm mail geçmişini yükle
                LogMessage("Mail geçmişi filtreleri temizlendi.");
            }
            catch (Exception ex)
            {
                LogMessage($"Mail geçmişi filtreleri temizlenirken hata: {ex.Message}");
            }
        }

        private void dgMailHistory_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (dgMailHistory.SelectedItem is MailHistoryModel selectedMail)
                {
                    ShowMailDetails(selectedMail);
                }
                else
                {
                    ClearMailDetails();
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Mail detayları gösterilirken hata: {ex.Message}");
            }
        }

        #endregion

        #region Mail Geçmişi Helper Methods

        private void LoadMailHistory()
        {
            try
            {
                LogMessage("Mail geçmişi yükleniyor...");
                var mailHistory = _mailHistoryService.GetAllMailHistory();
                LogMessage($"Toplam {mailHistory.Count} mail kaydı bulundu.");
                
                dgMailHistory.ItemsSource = mailHistory;
                
                // DataGrid'i yenile
                dgMailHistory.Items.Refresh();
                
                LogMessage("Mail geçmişi başarıyla yüklendi.");
            }
            catch (Exception ex)
            {
                LogMessage($"Mail geçmişi yüklenirken hata: {ex.Message}");
            }
        }

        private void FilterMailHistory()
        {
            try
            {
                var searchTerm = txtMailHistorySearch.Text;
                
                // Placeholder metni ise arama yapma
                if (searchTerm == "Mail geçmişinde arama yapabilirsiniz")
                {
                    searchTerm = "";
                }
                
                var statusFilter = cmbMailStatusFilter.SelectedItem as ComboBoxItem;
                var statusText = statusFilter?.Content.ToString();

                // Tüm mail geçmişini al
                var allHistory = _mailHistoryService.GetAllMailHistory();
                var filteredHistory = allHistory.AsEnumerable();

                // Arama filtresi uygula
                if (!string.IsNullOrWhiteSpace(searchTerm))
                {
                    filteredHistory = filteredHistory.Where(x => 
                        x.Recipient.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                        x.Subject.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                        x.Content.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                        x.Status.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                        x.DeliveryType.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)
                    );
                }

                // Durum filtresi uygula
                if (statusText != null && statusText != "Tüm Durumlar")
                {
                    filteredHistory = filteredHistory.Where(x => x.Status == statusText);
                }

                // Filtrelenmiş sonuçları DataGrid'e yükle
                var filteredList = filteredHistory.ToList();
                dgMailHistory.ItemsSource = filteredList;
                dgMailHistory.Items.Refresh();
                
                LogMessage($"Mail geçmişi filtrelendi. {filteredList.Count} kayıt bulundu.");
            }
            catch (Exception ex)
            {
                LogMessage($"Mail geçmişi filtrelenirken hata: {ex.Message}");
            }
        }

        private void ShowMailDetails(MailHistoryModel mail)
        {
            try
            {
                spMailDetails.Children.Clear();

                // Tarih
                spMailDetails.Children.Add(new TextBlock
                {
                    Text = $"📅 Tarih: {mail.Timestamp:dd/MM/yyyy HH:mm:ss}",
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 0, 0, 5)
                });

                // Alıcı
                spMailDetails.Children.Add(new TextBlock
                {
                    Text = $"👤 Alıcı: {mail.Recipient}",
                    Margin = new Thickness(0, 0, 0, 5)
                });

                // Konu
                spMailDetails.Children.Add(new TextBlock
                {
                    Text = $"📧 Konu: {mail.Subject}",
                    Margin = new Thickness(0, 0, 0, 5)
                });

                // Durum
                var statusColor = mail.Status switch
                {
                    "Gönderildi" => "#4CAF50",
                    "Hata" => "#F44336",
                    "Beklemede" => "#FF9800",
                    _ => "#666666"
                };

                spMailDetails.Children.Add(new TextBlock
                {
                    Text = $"📊 Durum: {mail.Status}",
                    Foreground = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(statusColor)),
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 0, 0, 5)
                });

                // Ek sayısı
                spMailDetails.Children.Add(new TextBlock
                {
                    Text = $"📎 Ek Sayısı: {mail.AttachmentCount}",
                    Margin = new Thickness(0, 0, 0, 5)
                });

                // Ekler
                if (mail.Attachments.Any())
                {
                    spMailDetails.Children.Add(new TextBlock
                    {
                        Text = "📎 Ekler:",
                        FontWeight = FontWeights.Bold,
                        Margin = new Thickness(0, 10, 0, 5)
                    });

                    foreach (var attachment in mail.Attachments)
                    {
                        spMailDetails.Children.Add(new TextBlock
                        {
                            Text = $"  • {attachment}",
                            Margin = new Thickness(20, 0, 0, 2),
                            TextWrapping = TextWrapping.Wrap
                        });
                    }
                }

                // İçerik
                if (!string.IsNullOrEmpty(mail.Content))
                {
                    spMailDetails.Children.Add(new TextBlock
                    {
                        Text = "📝 İçerik:",
                        FontWeight = FontWeights.Bold,
                        Margin = new Thickness(0, 10, 0, 5)
                    });

                    spMailDetails.Children.Add(new TextBlock
                    {
                        Text = mail.Content,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(20, 0, 0, 5)
                    });
                }

                // Hata mesajı
                if (!string.IsNullOrEmpty(mail.ErrorMessage))
                {
                    spMailDetails.Children.Add(new TextBlock
                    {
                        Text = "❌ Hata Mesajı:",
                        FontWeight = FontWeights.Bold,
                        Foreground = new SolidColorBrush(Colors.Red),
                        Margin = new Thickness(0, 10, 0, 5)
                    });

                    spMailDetails.Children.Add(new TextBlock
                    {
                        Text = mail.ErrorMessage,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = new SolidColorBrush(Colors.Red),
                        Margin = new Thickness(20, 0, 0, 5)
                    });
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Mail detayları gösterilirken hata: {ex.Message}");
            }
        }

        private void ClearMailDetails()
        {
            try
            {
                spMailDetails.Children.Clear();
                spMailDetails.Children.Add(new TextBlock
                {
                    Text = "Mail seçiniz...",
                    Foreground = new SolidColorBrush(Colors.Gray),
                    FontStyle = FontStyles.Italic
                });
            }
            catch (Exception ex)
            {
                LogMessage($"Mail detayları temizlenirken hata: {ex.Message}");
            }
        }

        private void ExportMailHistoryToExcel(List<MailHistoryModel> mailHistory, string filePath)
        {
            try
            {
                ExcelPackage.License.SetNonCommercialPersonal("WebScraper");

                using var package = new ExcelPackage();
                var worksheet = package.Workbook.Worksheets.Add("Mail Geçmişi");

                // Başlıklar
                worksheet.Cells[1, 1].Value = "Tarih";
                worksheet.Cells[1, 2].Value = "Alıcı";
                worksheet.Cells[1, 3].Value = "Konu";
                worksheet.Cells[1, 4].Value = "Durum";
                worksheet.Cells[1, 5].Value = "Ek Sayısı";
                worksheet.Cells[1, 6].Value = "Hata Mesajı";

                // Veriler
                for (int i = 0; i < mailHistory.Count; i++)
                {
                    var mail = mailHistory[i];
                    var row = i + 2;

                    worksheet.Cells[row, 1].Value = mail.Timestamp.ToString("dd/MM/yyyy HH:mm:ss");
                    worksheet.Cells[row, 2].Value = mail.Recipient;
                    worksheet.Cells[row, 3].Value = mail.Subject;
                    worksheet.Cells[row, 4].Value = mail.Status;
                    worksheet.Cells[row, 5].Value = mail.AttachmentCount;
                    worksheet.Cells[row, 6].Value = mail.ErrorMessage;
                }

                // Başlık stilini ayarla
                using (var range = worksheet.Cells[1, 1, 1, 6])
                {
                    range.Style.Font.Bold = true;
                    range.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                    range.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightBlue);
                }

                // Sütun genişliklerini ayarla
                worksheet.Column(1).Width = 20;
                worksheet.Column(2).Width = 30;
                worksheet.Column(3).Width = 40;
                worksheet.Column(4).Width = 15;
                worksheet.Column(5).Width = 12;
                worksheet.Column(6).Width = 50;

                package.SaveAs(new FileInfo(filePath));
            }
            catch (Exception ex)
            {
                LogMessage($"Excel dışa aktarma hatası: {ex.Message}");
                throw;
            }
        }

        private void ExportMailHistoryToCsv(List<MailHistoryModel> mailHistory, string filePath)
        {
            try
            {
                using var writer = new StreamWriter(filePath, false, System.Text.Encoding.UTF8);
                
                // Başlık satırı
                writer.WriteLine("Tarih,Alıcı,Konu,Durum,Ek Sayısı,Hata Mesajı");
                
                // Veri satırları
                foreach (var mail in mailHistory)
                {
                    var line = $"\"{mail.Timestamp:dd/MM/yyyy HH:mm:ss}\",\"{mail.Recipient}\",\"{mail.Subject}\",\"{mail.Status}\",{mail.AttachmentCount},\"{mail.ErrorMessage}\"";
                    writer.WriteLine(line);
                }
            }
            catch (Exception ex)
            {
                LogMessage($"CSV dışa aktarma hatası: {ex.Message}");
                throw;
            }
        }

        private void ExportMailHistoryToJson(List<MailHistoryModel> mailHistory, string filePath)
        {
            try
            {
                var json = JsonSerializer.Serialize(mailHistory, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(filePath, json, System.Text.Encoding.UTF8);
            }
            catch (Exception ex)
            {
                LogMessage($"JSON dışa aktarma hatası: {ex.Message}");
                throw;
            }
        }

        #endregion

        #region Loading Overlay Methods

        private void ShowLoadingOverlay(string message = "İşlem yapılıyor...")
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    LoadingText.Text = message;
                    LoadingOverlay.Visibility = Visibility.Visible;
                    ClearLoadingLogs(); // Önceki log mesajlarını temizle

                    // Loading overlay gösterildiğinde istatistikleri ana UI'dan kopyala
                    // İstatistikler modaldan kaldırıldı

                    // Focus'u loading overlay'e ver ki ESC tuşu çalışsın
                    LoadingOverlay.Focus();
                });
            }
            catch (Exception ex)
            {
                LogMessage($"Loading overlay gösterilirken hata: {ex.Message}");
            }
        }

        private void HideLoadingOverlay()
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    LoadingOverlay.Visibility = Visibility.Collapsed;
                });
            }
            catch (Exception ex)
            {
                LogMessage($"Loading overlay gizlenirken hata: {ex.Message}");
            }
        }

        private void UpdateLoadingText(string message)
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    LoadingText.Text = message;
                });
            }
            catch (Exception ex)
            {
                LogMessage($"Loading text güncellenirken hata: {ex.Message}");
            }
        }

        private void ShowProgressBar(int current, int total, string message = "")
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    ProgressContainer.Visibility = Visibility.Visible;
                    ProgressText.Text = $"{current} / {total} tamamlandı";
                    ProgressPercentage.Text = $"{Math.Round((double)current / total * 100)}%";
                    
                    if (total > 0)
                    {
                        var percentage = (double)current / total;
                        ProgressFill.Width = percentage * 300; // 300 is the width of ProgressContainer
                    }
                    
                    if (!string.IsNullOrEmpty(message))
                    {
                        LoadingText.Text = message;
                    }
                });
            }
            catch (Exception ex)
            {
                LogMessage($"Progress bar güncellenirken hata: {ex.Message}");
            }
        }

        private void HideProgressBar()
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    ProgressContainer.Visibility = Visibility.Collapsed;
                });
            }
            catch (Exception ex)
            {
                LogMessage($"Progress bar gizlenirken hata: {ex.Message}");
            }
        }

        private void AddLogMessageToOverlay(string message, string type = "info")
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    // Null kontrolleri
                    if (LogMessagesPanel == null || LogContainer == null)
                    {
                        return;
                    }

                    // Log mesajı için TextBlock oluştur
                    var logTextBlock = new TextBlock
                    {
                        Text = $"[{DateTime.Now:HH:mm:ss}] {message}",
                        FontSize = 14,
                        FontFamily = new System.Windows.Media.FontFamily("pack://application:,,,/Fonts/Poppins-Regular.ttf#Poppins"),
                        FontWeight = System.Windows.FontWeights.Normal,
                        Foreground = System.Windows.Media.Brushes.White,
                        TextWrapping = TextWrapping.NoWrap,
                        Margin = new Thickness(0, 0, 15, 0),
                        HorizontalAlignment = HorizontalAlignment.Left,
                        VerticalAlignment = VerticalAlignment.Center,
                        Opacity = 0
                    };

                    // Font yükleme kontrolü
                    try
                    {
                        var fontUri = new Uri("pack://application:,,,/Fonts/Poppins-Regular.ttf");
                        var fontFamily = new System.Windows.Media.FontFamily(fontUri, "Poppins");
                        logTextBlock.FontFamily = fontFamily;
                    }
                    catch
                    {
                        // Font yüklenemezse varsayılan font kullan
                        logTextBlock.FontFamily = new System.Windows.Media.FontFamily("Segoe UI");
                    }

                    // Animasyon için RenderTransform ekle
                    var transform = new TranslateTransform(300, 0); // Sağdan başla
                    logTextBlock.RenderTransform = transform;

                    // Önceki tüm log mesajlarını temizle
                    LogMessagesPanel.Children.Clear();

                    // Log mesajını panele ekle
                    LogMessagesPanel.Children.Add(logTextBlock);

                    // Animasyon oluştur
                    var animation = new DoubleAnimation
                    {
                        From = 300,
                        To = 0,
                        Duration = TimeSpan.FromMilliseconds(500),
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };

                    // Opacity animasyonu
                    var opacityAnimation = new DoubleAnimation
                    {
                        From = 0,
                        To = 1,
                        Duration = TimeSpan.FromMilliseconds(300),
                        BeginTime = TimeSpan.FromMilliseconds(200)
                    };

                    // Animasyonları başlat
                    transform.BeginAnimation(TranslateTransform.XProperty, animation);
                    logTextBlock.BeginAnimation(OpacityProperty, opacityAnimation);

                    // Scroll'u en sağa kaydır
                    var scrollViewer = LogContainer.FindName("ScrollViewer") as ScrollViewer;
                    if (scrollViewer != null)
                    {
                        scrollViewer.ScrollToRightEnd();
                    }
                });
            }
            catch (Exception ex)
            {
                LogMessage($"Loading overlay'e log mesajı eklenirken hata: {ex.Message}");
            }
        }

        private System.Windows.Media.Brush GetLogColor(string type)
        {
            return type switch
            {
                "error" => System.Windows.Media.Brushes.Red,
                "warning" => System.Windows.Media.Brushes.Orange,
                "success" => System.Windows.Media.Brushes.Green,
                "info" => System.Windows.Media.Brushes.Blue,
                _ => System.Windows.Media.Brushes.Black
            };
        }

        private void ClearLoadingLogs()
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    if (LogMessagesPanel != null)
                    {
                        LogMessagesPanel.Children.Clear();
                    }

                    // İstatistikleri sıfırla
                    // İstatistikler modaldan kaldırıldı
                });
            }
            catch (Exception ex)
            {
                LogMessage($"Loading logları temizlenirken hata: {ex.Message}");
            }
        }

        private void btnCloseLoading_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Onay sor
                var result = System.Windows.MessageBox.Show(
                    "İşlemi iptal etmek istediğinizden emin misiniz?",
                    "Onay",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                
                if (result == MessageBoxResult.Yes)
                {
                    if (_cancellationTokenSource != null)
                    {
                        _cancellationTokenSource.Cancel();
                        LogMessage("İşlem kullanıcı tarafından iptal edildi.");
                    }
                    
                    // Tarayıcıyı zorla kapat
                    _scraperService?.ForceStopBrowser();
                    LogMessage("Tarayıcı zorla kapatıldı.");
                    
                    HideLoadingOverlay();
                    UpdateStatus("İşlem iptal edildi", "Kullanıcı tarafından iptal edildi.", StatusType.Warning);
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Loading iptal edilirken hata: {ex.Message}");
            }
        }

        private void LoadingOverlay_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            try
            {
                if (e.Key == System.Windows.Input.Key.Escape)
                {
                    // ESC tuşuna basıldığında onay sor
                    var result = System.Windows.MessageBox.Show(
                        "İşlemi iptal etmek istediğinizden emin misiniz?",
                        "Onay",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);
                    
                    if (result == MessageBoxResult.Yes)
                    {
                        if (_cancellationTokenSource != null)
                        {
                            _cancellationTokenSource.Cancel();
                            LogMessage("İşlem kullanıcı tarafından iptal edildi.");
                        }
                        
                        // Tarayıcıyı zorla kapat
                        _scraperService?.ForceStopBrowser();
                        LogMessage("Tarayıcı zorla kapatıldı.");
                        
                        HideLoadingOverlay();
                        UpdateStatus("İşlem iptal edildi", "Kullanıcı tarafından iptal edildi.", StatusType.Warning);
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Loading overlay key event hatası: {ex.Message}");
            }
        }

        private void FurkanOzmen_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                // LinkedIn profilini varsayılan tarayıcıda aç
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://tr.linkedin.com/in/furkanozm?trk=people-guest_people_search-card",
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(psi);
                
                LogMessage("Furkan ÖZMEN LinkedIn profili açıldı.");
            }
            catch (Exception ex)
            {
                LogMessage($"LinkedIn profili açılırken hata: {ex.Message}");
                System.Windows.MessageBox.Show($"LinkedIn profili açılırken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region Checkbox Event Handlers

        private void chkCreateMode_Checked(object sender, RoutedEventArgs e)
        {
            try
            {
                txtStartButtonText.Text = "Oluştur";
                LogMessage("📝 Oluştur modu aktif - Ödeme emri oluşturma işlemi başlatılacak.");

                // Oluştur modu aktifken taslak/onaylı switch'i devre dışı bırak
                if (chkIslemTuru != null)
                {
                    chkIslemTuru.IsEnabled = false;
                }
                // İlgili etiketleri de görsel olarak pasifleştir
                if (txtTaslakLabel != null)
                {
                    txtTaslakLabel.Opacity = 0.5;
                }
                if (txtOnaylandiLabel != null)
                {
                    txtOnaylandiLabel.Opacity = 0.5;
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Checkbox checked event hatası: {ex.Message}");
            }
        }

        private void chkCreateMode_Unchecked(object sender, RoutedEventArgs e)
        {
            try
            {
                txtStartButtonText.Text = "Başlat";
                LogMessage("🚀 Normal mod aktif - İndirme işlemi başlatılacak.");

                // Oluştur modu kapalıyken taslak/onaylı switch tekrar aktif
                if (chkIslemTuru != null)
                {
                    chkIslemTuru.IsEnabled = true;
                }
                // Etiket opaklıklarını geri al
                if (txtTaslakLabel != null)
                {
                    txtTaslakLabel.Opacity = 1.0;
                }
                if (txtOnaylandiLabel != null)
                {
                    txtOnaylandiLabel.Opacity = 1.0;
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Checkbox unchecked event hatası: {ex.Message}");
            }
        }
        
        #endregion

        #region Geçmiş Yönetimi

        private void LoadHistoryRecords()
        {
            try
            {
                _historyRecords = new ObservableCollection<HistoryRecord>();

                if (File.Exists(HISTORY_FILE))
                {
                    string json = File.ReadAllText(HISTORY_FILE);
                    var records = JsonSerializer.Deserialize<List<HistoryRecord>>(json);
                    if (records != null)
                    {
                        foreach (var record in records.OrderByDescending(r => r.ProcessDate))
                        {
                            _historyRecords.Add(record);
                        }
                    }
                }

                // DataGrid'e bağla
                if (dgHistory != null)
                {
                    dgHistory.ItemsSource = _historyRecords;
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Geçmiş verilerini yükleme hatası: {ex.Message}");
                _historyRecords = new ObservableCollection<HistoryRecord>();
            }
        }

        private void SaveHistoryRecords()
        {
            try
            {
                var records = _historyRecords.ToList();
                string json = JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(HISTORY_FILE, json);
            }
            catch (Exception ex)
            {
                LogMessage($"Geçmiş verilerini kaydetme hatası: {ex.Message}");
            }
        }

        public void AddHistoryRecord(string processType, string period, string id, decimal amount, string status = "Başarılı")
        {
            try
            {
                var record = new HistoryRecord(processType, period, id, amount, status);
                _historyRecords.Insert(0, record); // En üste ekle

                // Maksimum 1000 kayıt tut
                if (_historyRecords.Count > 1000)
                {
                    _historyRecords.RemoveAt(_historyRecords.Count - 1);
                }

                SaveHistoryRecords();
            }
            catch (Exception ex)
            {
                LogMessage($"Geçmiş kaydı ekleme hatası: {ex.Message}");
            }
        }

        #endregion

        #region Geçmiş Silme İşlemleri

        private void btnClearSelectedHistory_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (dgHistory == null || dgHistory.SelectedItems.Count == 0)
                {
                    System.Windows.MessageBox.Show("Lütfen silmek istediğiniz kayıtları seçin.",
                                                   "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var result = System.Windows.MessageBox.Show(
                    $"{dgHistory.SelectedItems.Count} adet geçmiş kaydını silmek istediğinizden emin misiniz?",
                    "Geçmiş Kayıtlarını Sil",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    var selectedItems = new List<HistoryRecord>();
                    
                    // Seçilen öğeleri güvenli bir şekilde HistoryRecord'a cast et
                    foreach (var item in dgHistory.SelectedItems)
                    {
                        if (item is HistoryRecord historyRecord)
                        {
                            selectedItems.Add(historyRecord);
                        }
                    }

                    foreach (var item in selectedItems)
                    {
                        _historyRecords.Remove(item);
                    }

                    SaveHistoryRecords();
                    LogMessage($"{selectedItems.Count} adet geçmiş kaydı silindi.");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Seçili geçmiş kayıtlarını silme hatası: {ex.Message}");
                System.Windows.MessageBox.Show($"Geçmiş kayıtlarını silerken hata oluştu: {ex.Message}",
                                               "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnClearAllHistory_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_historyRecords.Count == 0)
                {
                    System.Windows.MessageBox.Show("Silinecek geçmiş kaydı bulunamadı.",
                                                   "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var result = System.Windows.MessageBox.Show(
                    $"Tüm geçmiş kayıtlarını ({_historyRecords.Count} adet) silmek istediğinizden emin misiniz?\n\nBu işlem geri alınamaz.",
                    "Tüm Geçmiş Kayıtlarını Sil",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    int recordCount = _historyRecords.Count;
                    _historyRecords.Clear();

                    // process_history.json dosyasını da sil
                    if (File.Exists(HISTORY_FILE))
                    {
                        File.Delete(HISTORY_FILE);
                    }

                    LogMessage($"{recordCount} adet geçmiş kaydı tamamen silindi.");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Tüm geçmiş kayıtlarını silme hatası: {ex.Message}");
                System.Windows.MessageBox.Show($"Geçmiş kayıtlarını silerken hata oluştu: {ex.Message}",
                                               "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region İndirilen Dosyalar Yönetimi

        private void LoadDownloadedFiles()
        {
            try
            {
                _downloadedFiles = new ObservableCollection<DownloadedFileItem>();

                // Önce JSON dosyasını dene
                if (File.Exists(DOWNLOADED_FILE))
                {
                    var json = File.ReadAllText(DOWNLOADED_FILE);
                    var items = JsonSerializer.Deserialize<List<DownloadedFileItem>>(json);
                    if (items != null)
                    {
                        foreach (var item in items)
                        {
                            _downloadedFiles.Add(item);
                        }
                    }
                }

                // TXT dosyasından da direkt verileri oku ve ekle
                if (File.Exists("previously_downloaded.txt"))
                {
                    var lines = File.ReadAllLines("previously_downloaded.txt");
                    foreach (var line in lines)
                    {
                        if (!string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith("#"))
                        {
                            // ID|Dönem formatını parse et
                            var parts = line.Split('|');
                            var id = parts[0].Trim();
                            var period = parts.Length > 1 ? parts[1].Trim() : "";

                            // Eğer bu ID zaten JSON'dan eklenmemişse ekle
                            if (!_downloadedFiles.Any(x => x.Id == id))
                            {
                                _downloadedFiles.Add(new DownloadedFileItem
                                {
                                    Id = id,
                                    DownloadDate = DateTime.Now,
                                    Period = period, // TXT'den dönem bilgisini al
                                    Status = "İndirildi"
                                });
                            }
                        }
                    }
                }

                // Eğer hiç veri yoksa ve sadece txt varsa migration yap (geriye uyumluluk için)
                if (_downloadedFiles.Count == 0 && !File.Exists(DOWNLOADED_FILE) && File.Exists("previously_downloaded.txt"))
                {
                    MigrateFromTxtToJson();
                }

                // DataGrid'e bağla
                if (dgDownloadedFiles != null)
                {
                    dgDownloadedFiles.ItemsSource = _downloadedFiles;
                }

                // İstatistikleri güncelle
                UpdateDownloadedStatistics();
            }
            catch (Exception ex)
            {
                LogMessage($"İndirilen dosyaları yükleme hatası: {ex.Message}");
            }
        }

        private void MigrateFromTxtToJson()
        {
            try
            {
                if (!File.Exists("previously_downloaded.txt")) return;

                var lines = File.ReadAllLines("previously_downloaded.txt");
                foreach (var line in lines)
                {
                    if (!string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith("#"))
                    {
                        // ID|Dönem formatını parse et
                        var parts = line.Split('|');
                        var id = parts[0].Trim();
                        var period = parts.Length > 1 ? parts[1].Trim() : "";

                        _downloadedFiles.Add(new DownloadedFileItem
                        {
                            Id = id,
                            DownloadDate = DateTime.Now,
                            Period = period, // Yeni formatta dönem bilgisi var
                            Status = "İndirildi"
                        });
                    }
                }

                // JSON olarak kaydet
                SaveDownloadedFiles();

                // Eski dosyayı yedekle
                File.Move("previously_downloaded.txt", "previously_downloaded.txt.backup");
            }
            catch (Exception ex)
            {
                LogMessage($"TXT'den JSON'a migration hatası: {ex.Message}");
            }
        }

        private void UpdateDownloadedStatistics()
        {
            try
            {
                if (_downloadedFiles == null) return;

                var total = _downloadedFiles.Count;
                var today = _downloadedFiles.Count(x => x.DownloadDate.Date == DateTime.Today);
                var active = total; // Tüm kayıtlar aktif olarak kabul ediliyor

                Dispatcher.Invoke(() =>
                {
                    txtTotalDownloadedCount.Text = total.ToString();
                    txtActiveDownloadedCount.Text = active.ToString();
                    txtTodayDownloadedCount.Text = today.ToString();
                });
            }
            catch (Exception ex)
            {
                LogMessage($"İndirilen dosyalar istatistiklerini güncelleme hatası: {ex.Message}");
            }
        }

        private void SaveDownloadedFiles()
        {
            try
            {
                var json = JsonSerializer.Serialize(_downloadedFiles.ToList(), new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(DOWNLOADED_FILE, json);
            }
            catch (Exception ex)
            {
                LogMessage($"İndirilen dosyaları kaydetme hatası: {ex.Message}");
            }
        }

        private void btnRefreshDownloaded_Click(object sender, RoutedEventArgs e)
        {
            LoadDownloadedFiles();
            System.Windows.MessageBox.Show("İndirilen dosyalar listesi yenilendi.", "Bilgi",
                                         MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void btnClearAllDownloaded_Click(object sender, RoutedEventArgs e)
        {
            var result = System.Windows.MessageBox.Show(
                "Tüm indirilen dosya kayıtlarını silmek istediğinizden emin misiniz?\nBu işlem geri alınamaz.",
                "Onay", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    _downloadedFiles.Clear();
                    SaveDownloadedFiles();
                    UpdateDownloadedStatistics();

                    // JSON dosyasını temizle
                    if (File.Exists(DOWNLOADED_FILE))
                    {
                        File.WriteAllText(DOWNLOADED_FILE, "[]");
                    }

                    // TXT dosyasını da temizle
                    if (File.Exists("previously_downloaded.txt"))
                    {
                        File.WriteAllText("previously_downloaded.txt", "# Daha önce indirilen dosyaların ID'leri ve dönem bilgileri\n# Format: ID|Dönem Adı\n# Son güncelleme: " + DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss") + "\n# Toplam dosya sayısı: 0\n\n");
                    }

                    System.Windows.MessageBox.Show("Tüm kayıtlar başarıyla silindi.", "Başarılı",
                                                 MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    LogMessage($"Tüm indirilen dosyaları silme hatası: {ex.Message}");
                    System.Windows.MessageBox.Show($"Kayıtları silerken hata oluştu: {ex.Message}",
                                                 "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void btnClearSelectedDownloaded_Click(object sender, RoutedEventArgs e)
        {
            var selectedItems = new List<DownloadedFileItem>();
            
            // Seçilen öğeleri güvenli bir şekilde DownloadedFileItem'a cast et
            foreach (var item in dgDownloadedFiles.SelectedItems)
            {
                if (item is DownloadedFileItem downloadedItem)
                {
                    selectedItems.Add(downloadedItem);
                }
            }

            if (selectedItems.Count == 0)
            {
                System.Windows.MessageBox.Show("Lütfen silmek istediğiniz kayıtları seçin.", "Uyarı",
                                             MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = System.Windows.MessageBox.Show(
                $"{selectedItems.Count} adet kaydı silmek istediğinizden emin misiniz?",
                "Onay", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    // Silinecek ID'leri topla
                    var idsToRemove = selectedItems.Select(x => x.Id).ToList();

                    foreach (var item in selectedItems)
                    {
                        _downloadedFiles.Remove(item);
                    }

                    SaveDownloadedFiles();
                    UpdateDownloadedStatistics();

                    // TXT dosyasından da silinen ID'leri kaldır
                    if (File.Exists("previously_downloaded.txt"))
                    {
                        var lines = File.ReadAllLines("previously_downloaded.txt").ToList();
                        var filteredLines = lines.Where(line =>
                        {
                            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#"))
                                return true; // Header ve boş satırları tut

                            // ID|Dönem formatından ID'yi çıkar
                            var parts = line.Split('|');
                            var id = parts[0].Trim();
                            return !idsToRemove.Contains(id); // Silinecek ID'leri çıkar
                        }).ToList();

                        // Dosya başlığını güncelle
                        var headerLines = filteredLines.Where(line => line.TrimStart().StartsWith("#")).ToList();
                        var dataLines = filteredLines.Where(line => !string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith("#")).ToList();

                        var updatedLines = new List<string>();
                        updatedLines.AddRange(headerLines);

                        // Toplam sayı satırını güncelle
                        var countLineIndex = updatedLines.FindIndex(line => line.Contains("# Toplam dosya sayısı:"));
                        if (countLineIndex >= 0)
                        {
                            updatedLines[countLineIndex] = $"# Toplam dosya sayısı: {dataLines.Count}";
                        }

                        updatedLines.Add("");
                        updatedLines.AddRange(dataLines);

                        File.WriteAllLines("previously_downloaded.txt", updatedLines);
                    }

                    System.Windows.MessageBox.Show($"{selectedItems.Count} adet kayıt başarıyla silindi.", "Başarılı",
                                                 MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    LogMessage($"Seçilen indirilen dosyaları silme hatası: {ex.Message}");
                    System.Windows.MessageBox.Show($"Kayıtları silerken hata oluştu: {ex.Message}",
                                                 "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void txtDownloadedSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                var searchText = txtDownloadedSearch.Text?.ToLower() ?? "";

                // Placeholder metni ise arama yapma
                if (searchText == "indirilen dosyalarda arama yapabilirsiniz")
                {
                    searchText = "";
                }

                if (string.IsNullOrEmpty(searchText))
                {
                    // Arama boşsa tüm kayıtları göster
                    dgDownloadedFiles.ItemsSource = _downloadedFiles;
                }
                else
                {
                    // Arama metnine göre filtrele
                    var filtered = _downloadedFiles.Where(x =>
                        x.Id.ToLower().Contains(searchText) ||
                        x.Period.ToLower().Contains(searchText)).ToList();

                    dgDownloadedFiles.ItemsSource = new ObservableCollection<DownloadedFileItem>(filtered);
                }
            }
            catch (Exception ex)
            {
                LogMessage($"İndirilen dosyalar arama hatası: {ex.Message}");
            }
        }

        public void AddDownloadedFile(string fileId, string periodName)
        {
            try
            {
                // Zaten varsa güncelleme yap
                var existingItem = _downloadedFiles.FirstOrDefault(x => x.Id == fileId);
                if (existingItem != null)
                {
                    existingItem.DownloadDate = DateTime.Now;
                    existingItem.Period = periodName;
                    existingItem.Status = "İndirildi";
                }
                else
                {
                    // Yeni kayıt ekle
                    var newItem = new DownloadedFileItem
                    {
                        Id = fileId,
                        DownloadDate = DateTime.Now,
                        Period = periodName,
                        Status = "İndirildi"
                    };
                    _downloadedFiles.Insert(0, newItem);
                }

                // Dosyaya kaydet
                SaveDownloadedFiles();

                // İstatistikleri güncelle
                UpdateDownloadedStatistics();

                LogMessage($"İndirilen dosya kaydedildi: {fileId} - {periodName}");
            }
            catch (Exception ex)
            {
                LogMessage($"İndirilen dosya kaydetme hatası: {ex.Message}");
            }
        }

        #endregion

    }
}

