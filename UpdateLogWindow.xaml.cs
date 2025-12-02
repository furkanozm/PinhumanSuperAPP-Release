using System;
using System.Windows;
using System.Windows.Threading;

namespace WebScraper
{
    public partial class UpdateLogWindow : Window
    {
        public UpdateLogWindow()
        {
            InitializeComponent();
        }

        public void AddLog(string message)
        {
            this.Dispatcher.Invoke(() =>
            {
                var timestamp = DateTime.Now.ToString("HH:mm:ss");
                logText.Text += $"[{timestamp}] {message}\n";
                
                // Scroll'u en alta kaydır
                logScrollViewer.ScrollToEnd();
            });
        }

        public void SetStatus(string status)
        {
            this.Dispatcher.Invoke(() =>
            {
                statusText.Text = status;
            });
        }

        public void SetProgress(double percentage)
        {
            this.Dispatcher.Invoke(() =>
            {
                if (progressBarContainer != null)
                {
                    progressBarContainer.Visibility = Visibility.Visible;
                    
                    // Layout'u güncelle (genişlik hesaplaması için)
                    progressBarContainer.UpdateLayout();
                    
                    if (progressBarFill != null)
                    {
                        // Progress bar'ın genişliğini hesapla
                        // Parent Border'ın genişliğini al (Padding hariç)
                        var parentBorder = progressBarContainer.Parent as FrameworkElement;
                        double availableWidth = 750; // Varsayılan genişlik
                        
                        if (parentBorder != null)
                        {
                            availableWidth = parentBorder.ActualWidth > 0 
                                ? parentBorder.ActualWidth - 30 // Padding için (15*2)
                                : 750;
                        }
                        else if (progressBarContainer.ActualWidth > 0)
                        {
                            availableWidth = progressBarContainer.ActualWidth;
                        }
                        
                        var clampedPercentage = Math.Max(0, Math.Min(100, percentage));
                        progressBarFill.Width = clampedPercentage / 100.0 * availableWidth;
                    }
                    
                    if (progressText != null)
                    {
                        progressText.Text = $"{Math.Round(percentage, 1)}%";
                    }
                }
            });
        }

        public void HideProgress()
        {
            this.Dispatcher.Invoke(() =>
            {
                if (progressBarContainer != null)
                {
                    progressBarContainer.Visibility = Visibility.Collapsed;
                }
            });
        }

        public void ClearLogs()
        {
            this.Dispatcher.Invoke(() =>
            {
                logText.Text = "";
                statusText.Text = "Güncelleme kontrol ediliyor...";
                logScrollViewer.ScrollToTop();
            });
        }

        private void btnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void btnCopyLogs_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (logText != null && !string.IsNullOrEmpty(logText.Text))
                {
                    System.Windows.Clipboard.SetText(logText.Text);
                    
                    // Başarılı mesajı göster
                    var originalContent = btnCopyLogs.Content;
                    btnCopyLogs.Content = "✅ Kopyalandı!";
                    btnCopyLogs.IsEnabled = false;
                    
                    // 2 saniye sonra eski haline dön
                    var timer = new System.Windows.Threading.DispatcherTimer();
                    timer.Interval = TimeSpan.FromSeconds(2);
                    timer.Tick += (s, args) =>
                    {
                        btnCopyLogs.Content = originalContent;
                        btnCopyLogs.IsEnabled = true;
                        timer.Stop();
                    };
                    timer.Start();
                }
                else
                {
                    System.Windows.MessageBox.Show(
                        "Kopyalanacak log bulunamadı.",
                        "Bilgi",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information
                    );
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Loglar kopyalanırken hata oluştu:\n\n{ex.Message}",
                    "Hata",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }
    }
}

