using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.IO;
using System.Threading.Tasks;

namespace WebScraper
{
    public partial class SelectionWindow : Window
    {
        private readonly FirebaseAuthService _firebaseAuth;
        private readonly SecurityProfileService _securityService = new SecurityProfileService();
        private SecurityProfile? _cachedSecurityProfile;
        
        public SelectionWindow()
        {
            InitializeComponent();
            _firebaseAuth = new FirebaseAuthService();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Pencereyi öne getir
            this.Activate();
            this.Topmost = true;
            this.Topmost = false;
            this.Focus();
        }

        private void PaymentOrderCard_Click(object sender, MouseButtonEventArgs e)
        {
            if (!EnsureSecurityAccess())
            {
                return;
            }

            try
            {
                // Ödeme emri penceresini aç - direkt ana uygulamayı göster
                var paymentWindow = new MainWindow();
                paymentWindow.WindowState = WindowState.Maximized; // Maximized aç
                paymentWindow.ShowMainApplication(); // Direkt ana uygulamayı göster
                paymentWindow.Show();

                // SelectionWindow açık kalacak - gizleme
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ödeme emri penceresi açılırken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SmsCard_Click(object sender, MouseButtonEventArgs e)
        {
            if (!EnsureSecurityAccess())
            {
                return;
            }

            try
            {
                // SMS penceresini aç
                var smsWindow = new SmsWindow();
                smsWindow.WindowState = WindowState.Maximized; // Maximized aç
                smsWindow.Show();

                // SelectionWindow açık kalacak - gizleme
            }
            catch (Exception ex)
            {
                MessageBox.Show($"SMS penceresi açılırken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private void PdksCard_Click(object sender, MouseButtonEventArgs e)
        {
            if (!EnsureSecurityAccess())
            {
                return;
            }

            try
            {
                // PDKS veri işlemleri wizard'ını aç
                var pdksWizard = new PDKSWizardWindow();
                pdksWizard.Show();

                // SelectionWindow açık kalacak - gizleme
            }
            catch (Exception ex)
            {
                MessageBox.Show($"PDKS veri işlemleri penceresi açılırken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool EnsureSecurityAccess()
        {
            try
            {
                if (!_securityService.ProfileExists())
                {
                    var setupWindow = new SecuritySetupWindow(_securityService)
                    {
                        Owner = this
                    };

                    if (setupWindow.ShowDialog() != true)
                    {
                        return false;
                    }

                    _cachedSecurityProfile = setupWindow.ResultProfile ?? _securityService.LoadProfile();
                }
                else if (_cachedSecurityProfile == null)
                {
                    _cachedSecurityProfile = _securityService.LoadProfile();
                }

                if (_cachedSecurityProfile == null)
                {
                    MessageBox.Show("Güvenlik profili yüklenemedi. Lütfen tekrar deneyin.", "Güvenlik", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }

                var prompt = new SecurityPromptWindow(_securityService, _cachedSecurityProfile)
                {
                    Owner = this
                };

                var result = prompt.ShowDialog() == true;

                // yedek kod kullanımı gibi durumlarda profili yeniden yükle
                _cachedSecurityProfile = _securityService.LoadProfile();
                return result;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Güvenlik doğrulaması sırasında hata oluştu: {ex.Message}", "Güvenlik", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }


        private void btnHelp_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Yardım sayfasını aç
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://guleryuzgroup.com",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Yardım sayfası açılırken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnSecuritySettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!_securityService.ProfileExists())
                {
                    MessageBox.Show("Henüz bir güvenlik profili oluşturulmadı. Önce güvenlik kurulumu yapmalısınız.", "Güvenlik", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                if (_cachedSecurityProfile == null)
                {
                    _cachedSecurityProfile = _securityService.LoadProfile();
                }

                if (_cachedSecurityProfile == null)
                {
                    MessageBox.Show("Güvenlik profili yüklenemedi. Lütfen tekrar deneyin.", "Güvenlik", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var promptWindow = new SecurityPromptWindow(_securityService, _cachedSecurityProfile)
                {
                    Owner = this
                };

                if (promptWindow.ShowDialog() != true)
                {
                    _cachedSecurityProfile = _securityService.LoadProfile();
                    return;
                }

                var setupWindow = new SecuritySetupWindow(_securityService, _cachedSecurityProfile)
                {
                    Owner = this
                };

                if (setupWindow.ShowDialog() == true)
                {
                    _cachedSecurityProfile = setupWindow.ResultProfile ?? _securityService.LoadProfile();
                    MessageBox.Show("Güvenlik ayarları güncellendi.", "Güvenlik", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    _cachedSecurityProfile = _securityService.LoadProfile();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Güvenlik ayarları açılırken hata oluştu: {ex.Message}", "Güvenlik", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                // Eğer zaten kapatma işlemi başlatıldıysa devam et
                if (Application.Current.ShutdownMode == ShutdownMode.OnExplicitShutdown)
                {
                    return;
                }

                // Kapatmayı iptal et ve modal göster
                e.Cancel = true;
                
                // Arkaplanı blur yap ve overlay ekle
                this.Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 5 };
                
                var overlay = new System.Windows.Controls.Border
                {
                    Background = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromArgb(150, 0, 0, 0)), // Daha koyu arkaplan
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                    VerticalAlignment = System.Windows.VerticalAlignment.Stretch
                };
                
                // Overlay'i ana grid'e ekle
                if (this.Content is System.Windows.Controls.Grid mainGrid)
                {
                    mainGrid.Children.Add(overlay);
                    System.Windows.Controls.Grid.SetZIndex(overlay, 999); // En üstte
                }

                // Custom Alert ile onay sor
                var customAlert = new CustomAlertWindow(
                    "Uygulama Kapatma Onayı",
                    "Uygulamayı tamamen kapatmak istediğinizden emin misiniz?\n\nBu işlem tüm açık pencereleri ve arka plan işlemlerini sonlandıracaktır.",
                    "Evet, Kapat",
                    "İptal"
                );
                
                customAlert.Owner = this; // Owner'ı ayarla
                var result = customAlert.ShowDialog();
                
                // Overlay'i kaldır
                if (this.Content is System.Windows.Controls.Grid grid)
                {
                    grid.Children.Remove(overlay);
                }
                
                // Blur'ı kaldır
                this.Effect = null;
                
                if (result == true)
                {
                    // Kullanıcı onayladıysa uygulamayı kapat
                    ForceCloseAllBrowsers();
                    ForceCloseAllWebScraperProcesses();
                    
                    // Garbage collection'ı zorla
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    
                    // Uygulamayı tamamen sonlandır
                    Environment.Exit(0);
                }
            }
            catch (Exception ex)
            {
                // Hata olsa bile uygulamayı kapat
                Environment.Exit(0);
            }
        }

        private void ForceCloseAllBrowsers()
        {
            try
            {
                // Sadece Chromium process'lerini kapat
                var chromiumProcesses = System.Diagnostics.Process.GetProcessesByName("chromium");
                foreach (var process in chromiumProcesses)
                {
                    try
                    {
                        process.Kill();
                        process.WaitForExit(3000);
                    }
                    catch { /* Sessizce geç */ }
                }

                // Playwright process'lerini kapat
                var playwrightProcesses = System.Diagnostics.Process.GetProcessesByName("playwright");
                foreach (var process in playwrightProcesses)
                {
                    try
                    {
                        process.Kill();
                        process.WaitForExit(3000);
                    }
                    catch { /* Sessizce geç */ }
                }
            }
            catch (Exception ex)
            {
                // Hata olsa bile devam et
            }
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
            catch
            {
                // Hata durumunda boş string döndür
            }
            return "";
        }

        private void ForceCloseAllWebScraperProcesses()
        {
            try
            {
                // WebScraper process'lerini kapat
                var webScraperProcesses = System.Diagnostics.Process.GetProcessesByName("WebScraper");
                foreach (var process in webScraperProcesses)
                {
                    try
                    {
                        process.Kill();
                        process.WaitForExit(3000);
                    }
                    catch { /* Sessizce geç */ }
                }

                // dotnet process'lerini kontrol et (eğer WebScraper çalışıyorsa)
                var dotnetProcesses = System.Diagnostics.Process.GetProcessesByName("dotnet");
                foreach (var process in dotnetProcesses)
                {
                    try
                    {
                        var commandLine = GetCommandLine(process.Id);
                        if (commandLine.Contains("WebScraper") || commandLine.Contains("WebScraper.dll"))
                        {
                            process.Kill();
                            process.WaitForExit(3000);
                        }
                    }
                    catch { /* Sessizce geç */ }
                }

                // Sadece WebScraper child process'lerini kapat
                var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
                foreach (var process in System.Diagnostics.Process.GetProcesses())
                {
                    try
                    {
                        if (process.Id != currentProcess.Id &&
                            (process.ProcessName.Contains("WebScraper") ||
                             process.ProcessName.Contains("playwright")))
                        {
                            process.Kill();
                            process.WaitForExit(2000);
                        }
                    }
                    catch { /* Sessizce geç */ }
                }
            }
            catch (Exception ex)
            {
                // Hata olsa bile devam et
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
    }
}
