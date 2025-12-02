using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.IO;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Threading;
using System.Linq;
using System.Reflection;
using System.Text;

namespace WebScraper
{
    public partial class LoginWindow : Window
    {
        private readonly FirebaseAuthService _firebaseAuth;
        private bool _isFullscreen = false;
        private double _originalWidth;
        private double _originalHeight;
        private double _originalLeft;
        private double _originalTop;

        // Caps Lock kontrolü için Windows API
        [DllImport("user32.dll")]
        private static extern short GetKeyState(int nVirtKey);

        private const int VK_CAPITAL = 0x14;

        public LoginWindow()
        {
            InitializeComponent();
            _firebaseAuth = new FirebaseAuthService();

            // Orijinal boyutları kaydet
            _originalWidth = this.Width;
            _originalHeight = this.Height;
            _originalLeft = this.Left;
            _originalTop = this.Top;

            // Beni hatırla ayarlarını yükle
            LoadRememberMeSettings();

            // Versiyon bilgisini yükle
            LoadVersionInfo();

            // Email box'a odaklan
            txtLoginEmail.Focus();
            
            // Caps Lock kontrolü için timer başlat
            var timer = new System.Windows.Threading.DispatcherTimer();
            timer.Interval = TimeSpan.FromMilliseconds(100);
            timer.Tick += CheckCapsLock;
            timer.Start();

            // Uygulama yüklendiğinde güncelleme kontrolü yap
            this.Loaded += LoginWindow_Loaded;
        }

        private async void LoginWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Güncelleme kontrolünü başlat
            _ = Task.Run(async () => await CheckForUpdatesFromLogin());
        }

        private async Task CheckForUpdatesFromLogin()
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
                    System.Diagnostics.Debug.WriteLine($"Güncelleme kontrolü hatası: {ex.Message}");
                    this.Dispatcher.Invoke(() =>
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

                // Versiyonları karşılaştır (UpdateHelper.IsNewerVersion kullan)
                if (!UpdateHelper.IsNewerVersion(currentVersionClean, latestVersion))
                {
                    // Güncel veya daha yeni versiyon kullanılıyor
                    System.Diagnostics.Debug.WriteLine($"Güncelleme yok. Mevcut: v{currentVersion}, En son: {latestRelease.tag_name}");
                    return;
                }

                // Yeni versiyon VARSA, şimdi pencereyi aç
                hasNewVersion = true;
            
                this.Dispatcher.Invoke(() =>
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
                        System.Diagnostics.Debug.WriteLine($"UpdateLogWindow açılamadı: {ex.Message}");
                        return;
                    }
                });
                
                if (logWindow == null)
                {
                    return;
                }

                // Zip dosyasını bul
                this.Dispatcher.Invoke(() =>
                {
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
                });

                var zipAsset = latestRelease.assets?.FirstOrDefault(a => 
                    a.name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && 
                    a.name.Contains("PinhumanSuperAPP"));
                
                if (zipAsset == null)
                {
                    this.Dispatcher.Invoke(() =>
                    {
                        logWindow?.AddLog("❌ Zip dosyası bulunamadı.");
                        logWindow?.AddLog("   Aranan: *.zip ve PinhumanSuperAPP içeren dosya");
                        logWindow?.SetStatus("Zip dosyası bulunamadı");
                    });
                    return;
                }
                
                this.Dispatcher.Invoke(() =>
                {
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
                    logWindow?.AddLog("⬇️ Güncelleme indiriliyor...");
                    logWindow?.SetStatus("Güncelleme indiriliyor...");
                    logWindow?.SetProgress(0); // Progress bar'ı göster ve 0'dan başlat
                });

                // Güncellemeyi indir ve kur
                try
                {
                    this.Dispatcher.Invoke(() =>
                    {
                        logWindow?.AddLog($"📥 İndirme başlatılıyor: {zipAsset.browser_download_url}");
                    });

                    await UpdateHelper.DownloadAndExtractUpdateAsync(
                        zipAsset.browser_download_url,
                        zipAsset.name,
                        new Progress<double>(percent =>
                        {
                            // Dispatcher.Invoke yerine BeginInvoke kullan (non-blocking)
                            this.Dispatcher.BeginInvoke(new Action(() =>
                            {
                                logWindow?.SetStatus($"İndiriliyor... {percent:F0}%");
                                logWindow?.SetProgress(percent); // Progress bar'ı güncelle
                                // Log'u sadece belirli aralıklarla güncelle (her %5'te bir)
                                if (percent % 5 < 1 || percent >= 100)
                                {
                                    logWindow?.AddLog($"📥 İndirme ilerlemesi: {percent:F0}%");
                                }
                            }));
                        }),
                        zipAsset.url
                    );

                    // Başarılı
                    this.Dispatcher.Invoke(() =>
                    {
                        logWindow?.AddLog("✅ Güncelleme başarıyla indirildi ve kuruldu!");
                        logWindow?.AddLog("🔄 Uygulama yeniden başlatılacak...");
                        logWindow?.SetStatus("Güncelleme tamamlandı");
                        logWindow?.SetProgress(100); // %100 göster
                    });

                    // Uygulamayı yeniden başlat
                    await Task.Delay(2000); // 2 saniye bekle
                    
                    this.Dispatcher.Invoke(() =>
                    {
                        var currentExe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                        if (!string.IsNullOrEmpty(currentExe))
                        {
                            System.Diagnostics.Process.Start(currentExe);
                            Application.Current.Shutdown();
                        }
                    });
                }
                catch (Exception downloadEx)
                {
                    this.Dispatcher.Invoke(() =>
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
                System.Diagnostics.Debug.WriteLine($"Güncelleme kontrolü hatası: {ex.Message}");
                this.Dispatcher.Invoke(() =>
                {
                    logWindow?.AddLog($"❌ HATA: {ex.Message}");
                    if (ex.InnerException != null)
                    {
                        logWindow?.AddLog($"   İç hata: {ex.InnerException.Message}");
                    }
                    var errorMsg = ex.Message.Length > 50 ? ex.Message.Substring(0, 50) + "..." : ex.Message;
                    logWindow?.SetStatus($"Hata: {errorMsg}");
                });
            }
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                this.DragMove();
            }
        }


        // Helper metodlar - UpdateLogWindow için reflection kullanarak güvenli erişim
        private void AddLogToWindow(Window? logWindow, string message)
        {
            if (logWindow == null) return;
            
            try
            {
                var addLogMethod = logWindow.GetType().GetMethod("AddLog", new[] { typeof(string) });
                addLogMethod?.Invoke(logWindow, new object[] { message });
            }
            catch
            {
                // Reflection hatası durumunda sessizce devam et
            }
        }

        private void SetStatusInWindow(Window? logWindow, string status)
        {
            if (logWindow == null) return;
            
            try
            {
                var setStatusMethod = logWindow.GetType().GetMethod("SetStatus", new[] { typeof(string) });
                setStatusMethod?.Invoke(logWindow, new object[] { status });
            }
            catch
            {
                // Reflection hatası durumunda sessizce devam et
            }
        }

        private void btnClose_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void btnFullscreen_Click(object sender, RoutedEventArgs e)
        {
            if (_isFullscreen)
            {
                // Orijinal boyutlara dön
                this.WindowState = WindowState.Normal;
                this.WindowStyle = WindowStyle.None;
                this.ResizeMode = ResizeMode.NoResize;
                this.Width = _originalWidth;
                this.Height = _originalHeight;
                this.Left = _originalLeft;
                this.Top = _originalTop;
                _isFullscreen = false;
            }
            else
            {
                // Tam ekran yap (taskbar görünür kalacak)
                this.WindowState = WindowState.Normal;
                this.WindowStyle = WindowStyle.None;
                this.ResizeMode = ResizeMode.NoResize;

                // Taskbar yüksekliğini hesaba katarak tam ekran yap
                var workArea = SystemParameters.WorkArea;
                this.Left = workArea.Left;
                this.Top = workArea.Top;
                this.Width = workArea.Width;
                this.Height = workArea.Height;
                _isFullscreen = true;
            }
        }

        private void LoadVersionInfo()
        {
            try
            {
                var versionInfo = UpdateHelper.GetCurrentVersion();
                if (!string.IsNullOrEmpty(versionInfo.Version))
                {
                    var versionText = $"v{versionInfo.Version}";
                    if (txtVersion != null)
                    {
                        txtVersion.Text = versionText;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Versiyon bilgisi yüklenemedi: {ex.Message}");
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
                        var rememberMe = lines[0].ToLower() == "true";
                        var email = lines[1];
                        
                        chkRememberMe.IsChecked = rememberMe;
                        if (rememberMe && !string.IsNullOrEmpty(email))
                        {
                            txtLoginEmail.Text = email;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Hata durumunda sessizce devam et
            }
        }

        private void SaveRememberMeSettings()
        {
            try
            {
                var rememberMeFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "remember_me.txt");
                var rememberMe = chkRememberMe.IsChecked == true ? "true" : "false";
                var email = txtLoginEmail.Text;
                
                File.WriteAllText(rememberMeFile, $"{rememberMe}\n{email}");
            }
            catch (Exception ex)
            {
                // Hata durumunda sessizce devam et
            }
        }

        private async void btnLogin_Click(object sender, RoutedEventArgs e)
        {
            await ProcessLogin();
        }

        private void txtLoginEmail_KeyDown(object sender, KeyEventArgs e)
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

        private void txtLoginPassword_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ProcessLogin();
            }
        }

        private void txtLoginPassword_GotFocus(object sender, RoutedEventArgs e)
        {
            CheckCapsLock();
        }

        private void txtLoginPassword_LostFocus(object sender, RoutedEventArgs e)
        {
            // Şifre alanından çıkıldığında Caps Lock uyarısını gizle
            capsLockWarning.Visibility = Visibility.Collapsed;
        }

        private void CheckCapsLock(object? sender = null, EventArgs? e = null)
        {
            // Sadece şifre alanı odakta iken kontrol et
            if (txtLoginPassword.IsFocused)
            {
                bool capsLockOn = IsCapsLockOn();
                capsLockWarning.Visibility = capsLockOn ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private bool IsCapsLockOn()
        {
            return (GetKeyState(VK_CAPITAL) & 0x0001) != 0;
        }

        private async Task ProcessLogin()
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

                btnLogin.IsEnabled = false;
                txtLoginError.Visibility = Visibility.Collapsed;
                
                // Firebase ile giriş yap
                var loginSuccess = await _firebaseAuth.LoginAsync(email, password);
                
                if (loginSuccess)
                {
                    // Beni hatırla ayarlarını kaydet
                    SaveRememberMeSettings();
                    
                    // SelectionWindow'u aç
                    var selectionWindow = new SelectionWindow();
                    selectionWindow.Show();
                    
                    // Bu pencereyi kapat
                    this.Close();
                }
                else
                {
                    ShowLoginError("Giriş başarısız. Lütfen email ve şifrenizi kontrol edin.");
                }
            }
            catch (Exception ex)
            {
                ShowLoginError($"Giriş sırasında hata oluştu: {ex.Message}");
            }
            finally
            {
                btnLogin.IsEnabled = true;
            }
        }

        private void ShowLoginError(string message)
        {
            txtLoginError.Text = message;
            txtLoginError.Visibility = Visibility.Visible;
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
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Web sitesi açılırken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
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
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Web sitesi açılırken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnForgotPassword_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var email = txtLoginEmail.Text?.Trim();
                
                if (string.IsNullOrEmpty(email))
                {
                    MessageBox.Show("Lütfen önce email adresinizi girin.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                    txtLoginEmail.Focus();
                    return;
                }

                if (!IsValidEmail(email))
                {
                    MessageBox.Show("Lütfen geçerli bir email adresi girin.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                    txtLoginEmail.Focus();
                    return;
                }

                var subject = "Şifremi unuttum";
                var body = $"Merhaba,\n\nŞifremi unuttum.\n\nKullanıcı e-posta: {email}\n\nTeşekkürler.";
                var to = "furkan.ozmen@guleryuzgroup.com";

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
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = foundOutlookPath,
                        Arguments = $"/c ipm.note /m \"{to}?subject={Uri.EscapeDataString(subject)}&body={Uri.EscapeDataString(body)}\"",
                        UseShellExecute = true
                    };

                    Process.Start(startInfo);
                    MessageBox.Show("Outlook açılıyor. Şifre talep maili taslağı oluşturuldu.", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    var fallbackMailto = $"mailto:{to}?subject={Uri.EscapeDataString(subject)}&body={Uri.EscapeDataString(body)}";
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = fallbackMailto,
                        UseShellExecute = true
                    });

                    MessageBox.Show("Outlook bulunamadı. Varsayılan mail uygulamanız açılıyor.", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Mail uygulaması açılırken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnUserRequest_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var email = txtLoginEmail.Text?.Trim();
                
                if (string.IsNullOrEmpty(email))
                {
                    MessageBox.Show("Lütfen önce email adresinizi girin.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                    txtLoginEmail.Focus();
                    return;
                }

                if (!IsValidEmail(email))
                {
                    MessageBox.Show("Lütfen geçerli bir email adresi girin.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                    txtLoginEmail.Focus();
                    return;
                }

                // Mail içeriğini hazırla
                var subject = "Kullanıcı Talebi";
                var body = $"Merhaba,\n\nPinhuman SuperApp için kullanıcı talebinde bulunuyorum.\n\nEmail: {email}\n\nİyi çalışmalar dilerim";
                var to = "furkan.ozmen@guleryuzgroup.com";

                // Outlook Classic'i açmaya çalış - birden fazla yol dene
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
                    // Outlook'u aç ve mail oluştur
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = foundOutlookPath,
                        Arguments = $"/c ipm.note /m \"{to}?subject={subject}&body={body}\"",
                        UseShellExecute = false
                    };
                    
                    Process.Start(startInfo);
                    MessageBox.Show("Outlook açılıyor. Kullanıcı talebiniz için mail oluşturuldu.", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    // Outlook bulunamadıysa varsayılan mail uygulamasını aç
                    var mailtoUrl = $"mailto:{to}?subject={Uri.EscapeDataString(subject)}&body={Uri.EscapeDataString(body)}";
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = mailtoUrl,
                        UseShellExecute = true
                    });
                    MessageBox.Show("Varsayılan mail uygulamanız açılıyor. Kullanıcı talebiniz için mail oluşturuldu.", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Mail uygulaması açılırken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
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

        private void LockIcon_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            try
            {
                if (sender is Border border && border.Child is TextBlock textBlock)
                {
                    textBlock.Opacity = 0.7;
                }
            }
            catch
            {
                // Hata durumunda sessizce devam et
            }
        }

        private void LockIcon_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            try
            {
                if (sender is Border border && border.Child is TextBlock textBlock)
                {
                    textBlock.Opacity = 1.0;
                }
            }
            catch
            {
                // Hata durumunda sessizce devam et
            }
        }

        private async void LockIcon_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // Kilit iconuna basınca güncelleme kontrolü yap
            // Log penceresini oluştur ve göster
            Window? logWindow = null;
            
            this.Dispatcher.Invoke(() =>
            {
                try
                {
                    // UpdateLogWindow sınıfını dinamik olarak oluştur
                    var windowType = Type.GetType("WebScraper.UpdateLogWindow");
                    if (windowType != null)
                    {
                        logWindow = Activator.CreateInstance(windowType) as Window;
                        if (logWindow != null)
                        {
                            logWindow.Owner = this;
                            logWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                            logWindow.Show();
                        }
                    }
                }
                catch
                {
                    // UpdateLogWindow bulunamazsa sessizce devam et
                }
            });

            /*
            try
            {
                await CheckForUpdatesAsync(logWindow);
            }
            catch (Exception ex)
            {
                // Hataları log penceresine ekle
                System.Diagnostics.Debug.WriteLine($"Güncelleme kontrolü hatası: {ex.Message}");
                
                AddLogToWindow(logWindow, $"❌ HATA: {ex.Message}");
                AddLogToWindow(logWindow, $"Stack Trace: {ex.StackTrace}");
                SetStatusInWindow(logWindow, $"Hata: {ex.Message.Substring(0, Math.Min(50, ex.Message.Length))}...");
            }
            */
            // Log penceresi açık kalacak, kullanıcı kapatabilir
            
            /* KALDIRILDI - Kilit iconuna basınca modal açılmıyor
            try
            {
                var config = AppConfig.Load();

                if (string.IsNullOrEmpty(config.Update.GoogleDriveApiKey) ||
                    string.IsNullOrEmpty(config.Update.GoogleDriveFolderId))
                {
                    MessageBox.Show(
                        "⚠️ Google Drive API ayarları bulunamadı.\n\nconfig.json dosyasında Update bölümünü kontrol edin.",
                        "API Test",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning
                    );
                    return;
                }

                // API bağlantısını test et
                var testResult = await TestGoogleDriveApi.TestConnection(
                    config.Update.GoogleDriveApiKey, 
                    config.Update.GoogleDriveFolderId
                );

                // Test loglarını göster
                var logWindow = new Window
                {
                    Title = testResult.Success ? "✅ API Test Başarılı" : "❌ API Test Hatası",
                    Width = 700,
                    Height = 550,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = this,
                    ResizeMode = ResizeMode.CanResize,
                    Background = System.Windows.Media.Brushes.WhiteSmoke
                };

                var grid = new Grid();
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var scrollViewer = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Margin = new Thickness(15, 15, 15, 10)
                };

                var textBlock = new TextBlock
                {
                    Text = string.Join(Environment.NewLine, testResult.Logs),
                    FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = System.Windows.Media.Brushes.Black,
                    Margin = new Thickness(5)
                };

                scrollViewer.Content = textBlock;
                Grid.SetRow(scrollViewer, 0);
                grid.Children.Add(scrollViewer);

                var buttonPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(15, 0, 15, 15)
                };

                var button = new Button
                {
                    Content = testResult.Success ? "Devam Et" : "Kapat",
                    Width = 120,
                    Height = 35,
                    Margin = new Thickness(5, 0, 0, 0),
                    Background = testResult.Success 
                        ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(76, 175, 80))
                        : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(244, 67, 54)),
                    Foreground = System.Windows.Media.Brushes.White,
                    BorderThickness = new Thickness(0),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    FontSize = 13
                };
                button.Click += (s, e) => logWindow.Close();

                buttonPanel.Children.Add(button);

                Grid.SetRow(buttonPanel, 1);
                grid.Children.Add(buttonPanel);

                logWindow.Content = grid;
                logWindow.ShowDialog();

                if (!testResult.Success)
                {
                    return;
                }

                // Test başarılıysa güncelleme notlarını göster

                using var updateService = new GoogleDriveUpdateService();
                updateService.Initialize(config.Update.GoogleDriveApiKey);

                // Mevcut versiyon bilgisini yükle
                var currentVersion = VersionInfo.Load();

                // Drive'dan güncelleme notlarını al
                var updateNotes = await updateService.GetDriveUpdateNotesAsync(
                    config.Update.GoogleDriveFolderId,
                    logCallback: (msg) => System.Diagnostics.Debug.WriteLine($"[Güncelleme Test] {msg}")
                );

                if (updateNotes != null)
                {
                    var updatesSince = updateNotes.GetUpdatesSince(currentVersion.Version);
                    
                    // Eğer hiç güncelleme yoksa, tüm güncelleme notlarını göster (test için)
                    if (updatesSince.Count == 0)
                    {
                        var allUpdates = updateNotes.Updates.OrderByDescending(u => u.ReleaseDate).ToList();
                        if (allUpdates.Count > 0)
                        {
                            updatesSince = allUpdates.Take(3).ToList(); // Son 3 güncellemeyi göster
                        }
                    }

                    if (updatesSince.Count > 0)
                    {
                        var updateModal = new UpdateNotesModal();
                        updateModal.Owner = this;
                        updateModal.OnDownloadRequested = async () =>
                        {
                            // Güncellemeleri indir
                            await DownloadUpdatesAsync(config, updateService);
                        };
                        updateModal.ShowDialog();
                    }
                    else
                    {
                        MessageBox.Show(
                            "Henüz güncelleme notu bulunmamaktadır.",
                            "Güncelleme Notları",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information
                        );
                    }
                }
                else
                {
                    // Drive'dan alınamadıysa yerel dosyalardan göster
                    ShowLocalUpdateNotes();
                }
            }
            catch (Exception ex)
            {
                // Hata durumunda yerel dosyalardan göster
                System.Diagnostics.Debug.WriteLine($"Güncelleme notları alınırken hata: {ex.Message}");
                ShowLocalUpdateNotes();
            }
        }

        private void ShowLocalUpdateNotes()
        {
            try
            {
                // Yerel UPDATE_NOTES.json dosyasından göster
                var updateNotes = UpdateNotesCollection.Load();
                
                if (updateNotes != null && updateNotes.Updates.Count > 0)
                {
                    // Son 3 güncellemeyi göster
                    var updateModal = new UpdateNotesModal();
                    updateModal.Owner = this;
                    updateModal.ShowDialog();
                }
                else
                {
                    MessageBox.Show(
                        "Güncelleme notları bulunamadı.\n\nDrive'dan almak için config.json dosyasında Google Drive ayarlarını yapılandırın.",
                        "Güncelleme Notları",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information
                    );
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Güncelleme notları gösterilirken hata oluştu:\n\n{ex.Message}",
                    "Hata",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
            */
        }
    }
}
