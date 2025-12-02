using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Data;
using System.Linq;
using System.IO;

namespace WebScraper
{
    public class BooleanToObjectConverter : System.Windows.Data.IValueConverter
    {
        public object TrueValue { get; set; } = null!;
        public object FalseValue { get; set; } = null!;

        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is bool boolValue)
            {
                return boolValue ? TrueValue : FalseValue;
            }
            return FalseValue;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public partial class SmsWindow : Window
    {
        private readonly SmsService _smsService;
        private readonly SmsHistoryService _smsHistoryService;
        private CancellationTokenSource? _cancellationTokenSource;
        private bool _isProcessing = false;
        private List<PeriodInfo> _availablePeriods = new List<PeriodInfo>();
        private string _sentTodayFilePath;
        private string _lastSmsSentFilePath;
        private Dictionary<string, List<SmsRecipientInfo>> _recipientsCache = new Dictionary<string, List<SmsRecipientInfo>>();
        private System.Collections.ObjectModel.ObservableCollection<SmsRecipientInfo> _recipientsCollection = new System.Collections.ObjectModel.ObservableCollection<SmsRecipientInfo>();
        private System.Collections.ObjectModel.ObservableCollection<SmsRecipientInfo> _duplicateRecipientsCollection = new System.Collections.ObjectModel.ObservableCollection<SmsRecipientInfo>();
        private System.Windows.Threading.DispatcherTimer? _recipientsTimer;
        private bool _isLoadingRecipients = false;
        
        // Progress tracking variables
        private int _totalItems = 0;
        private int _completedItems = 0;
        private int _errorCount = 0;
        private int _timeoutCount = 0;
        private List<string> _errorMessages = new List<string>();
        private int? _appliedRecipientFilterLimit = null;
        
        private bool _pendingRecipientFilterChange = false;
        
        /// <summary>
        /// Filtreyi uygula: sayfadaki "Seçili / Toplam" bilgisinden toplamı alır ve limit olarak saklar
        /// </summary>
        private async void btnApplyRecipientFilter_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                LogMessage("✅ Filtreyi Uygula tıklandı. Sayfadaki toplam sayısı limit olarak alınacak.");
                
                // Önce bekleyen filtre değişikliklerini uygula ve sayaçları güncelle
                if (_pendingRecipientFilterChange)
                {
                    ApplyRecipientFilters();
                }
                else
                {
                    // Yine de mevcut filtreyle listeyi normalize edip sayaç metnini güncelleyelim
                    ApplyRecipientFilters();
                }
                
                // UI metninden toplamı al
                var text = txtRecipientCount?.Text ?? string.Empty;
                var match = System.Text.RegularExpressions.Regex.Match(text, @"Seçili:\s*\d+\s*/\s*Toplam:\s*(\d+)");
                if (match.Success)
                {
                    _appliedRecipientFilterLimit = int.Parse(match.Groups[1].Value);
                    LogMessage($"Filtre limiti uygulandı: Toplam = {_appliedRecipientFilterLimit}");
                }
                else
                {
                    _appliedRecipientFilterLimit = null;
                    LogMessage("Toplam sayısı UI'den okunamadı. Limit temizlendi.");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Filtre uygulanırken hata: {ex.Message}");
            }
        }

        public SmsWindow()
        {
            InitializeComponent();
            
            // Ayarlardan URL'i al ve SmsService'i oluştur
            var config = ConfigManager.LoadConfig();
            var baseUrl = "https://pinhuman.net";
            
            // URL'den domain'i çıkar (https://www.pinhuman.net -> https://www.pinhuman.net)
            if (baseUrl.EndsWith("/"))
                baseUrl = baseUrl.TrimEnd('/');
            
            _smsService = new SmsService(baseUrl);
            _smsHistoryService = new SmsHistoryService();
            _sentTodayFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sms_sent_today.txt");
            _lastSmsSentFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "last_sms_sent.txt");
            
            LogMessage($"Son SMS tarihleri dosyası: {_lastSmsSentFilePath}");
            
            // Event handlers
            _smsService.LogMessage += OnLogMessage;
            _smsService.StatusChanged += OnStatusChanged;
            
            LogMessage("SMS Gönderim Sistemi başlatıldı.");
            UpdateDateTime();
            
            // Başlangıç istatistiklerini ayarla
            UpdateStatisticsBadges(0, 0, 0);
            
            // Ayarları yükle
            LoadSettings();
            
            // Initialize placeholders
            UpdateListPlaceholders();
            
            // SMS geçmişini yükle (async olarak)
            Task.Run(async () =>
            {
                await Task.Delay(100); // Kısa bir bekleme
                Dispatcher.Invoke(() => LoadSmsHistory());
            });
            
            // Timer to update datetime
            var timer = new System.Windows.Threading.DispatcherTimer();
            timer.Interval = TimeSpan.FromMinutes(1);
            timer.Tick += (s, e) => UpdateDateTime();
            timer.Start();
            
            // Timer to auto-load SMS recipients
            _recipientsTimer = new System.Windows.Threading.DispatcherTimer();
            _recipientsTimer.Interval = TimeSpan.FromSeconds(2);
            _recipientsTimer.Tick += async (s, e) => await AutoLoadRecipients();
            _recipientsTimer.Start();

            // Closing event handler ekle - pencere kapandığında SelectionWindow'u göster
            this.Closing += SmsWindow_Closing;
        }

        private void SmsWindow_Closing(object sender, CancelEventArgs e)
        {
            try
            {
                // Temizlik işlemlerini yap
                if (_isProcessing)
                {
                    _cancellationTokenSource?.Cancel();
                }

                // Tüm timer'ları durdur
                _recipientsTimer?.Stop();

                // CancellationTokenSource'u dispose et
                _cancellationTokenSource?.Dispose();

                // SmsService'i temizle
                _smsService?.StopAsync().Wait(2000); // 2 saniye bekle

                // Tüm event handler'ları temizle
                if (_smsService != null)
                {
                    _smsService.LogMessage -= OnLogMessage;
                }

                // ObservableCollection'ı temizle
                _recipientsCollection?.Clear();

                // Cache'i temizle
                _recipientsCache?.Clear();

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

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Pencereyi öne getir
            this.Activate();
            this.Topmost = true;
            this.Topmost = false;
            this.Focus();
        }

        private void UpdateDateTime()
        {
            txtDateTime.Text = DateTime.Now.ToString("dd.MM.yyyy HH:mm");
        }


        private void btnCloseLoading_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Yükleme işlemini durdur
                _cancellationTokenSource?.Cancel();

                // Loading overlay'i gizle
                LoadingOverlay.Visibility = Visibility.Collapsed;

                // ESC tuşu event handler'ını kaldır
                this.KeyDown -= LoadingKeyDownHandler;

                // Butonları eski haline getir
                btnLoadPeriods.IsEnabled = true;
                btnStartSms.IsEnabled = false;
                btnStopSms.IsEnabled = false;

                // Durumu güncelle
                txtStatus.Text = "Yükleme işlemi durduruldu";
                LogMessage("Yükleme işlemi kullanıcı tarafından durduruldu.");

                // Ana tab'a geri dön
                tabControl.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                LogMessage($"Yükleme işlemi durdurulurken hata oluştu: {ex.Message}");
                LoadingOverlay.Visibility = Visibility.Collapsed;

                // ESC tuşu event handler'ını kaldır
                this.KeyDown -= LoadingKeyDownHandler;
            }
        }

        private void LoadingKeyDownHandler(object sender, KeyEventArgs e)
        {
            try
            {
                // ESC tuşuna basıldığında loading overlay'i kapat
                if (e.Key == Key.Escape && LoadingOverlay.Visibility == Visibility.Visible)
                {
                    btnCloseLoading_Click(sender, new RoutedEventArgs());
                    e.Handled = true; // Event'i işlendi olarak işaretle
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Loading ESC handler hatası: {ex.Message}");
            }
        }


        private async void btnLoadPeriods_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Cancellation token oluştur
                _cancellationTokenSource = new CancellationTokenSource();
                
                // Loading overlay'i göster ve İşlem Logları tab'ine geç
                LoadingOverlay.Visibility = Visibility.Visible;
                tabControl.SelectedIndex = 2; // İşlem Logları tab'ine geç (index 2)
                
                btnLoadPeriods.IsEnabled = false;
                txtStatus.Text = "Dönemler yükleniyor...";
                LogMessage("Dönem listesi yükleniyor...");

                // Gerçek web scraping ile dönemleri yükle
                _availablePeriods = await LoadPeriodsFromWeb();
                
                RefreshPeriodList();
                btnStartSms.IsEnabled = _availablePeriods.Any();
                btnStopSms.IsEnabled = true; // Dönemler yüklendikten sonra durdur butonu aktif olsun
                
                // İstatistikleri hesapla ve göster
                var totalData = _availablePeriods.Count;
                var totalPreApproval = 0;
                var totalPendingApproval = 0;
                
                foreach (var period in _availablePeriods)
                {
                    var descriptionParts = period.Description.Split(',');
                    foreach (var part in descriptionParts)
                    {
                        var trimmedPart = part.Trim();
                        if (trimmedPart.StartsWith("Ön Onay:"))
                        {
                            var countStr = trimmedPart.Replace("Ön Onay:", "").Trim();
                            if (int.TryParse(countStr, out int count))
                            {
                                totalPreApproval += count;
                            }
                        }
                        else if (trimmedPart.StartsWith("Onay Bekleyen:"))
                        {
                            var countStr = trimmedPart.Replace("Onay Bekleyen:", "").Trim();
                            if (int.TryParse(countStr, out int count))
                            {
                                totalPendingApproval += count;
                            }
                        }
                    }
                }
                
                UpdateStatisticsBadges(totalData, totalPreApproval, totalPendingApproval);
                
                txtStatus.Text = $"{_availablePeriods.Count} dönem yüklendi";
                LogMessage($"{_availablePeriods.Count} dönem başarıyla yüklendi.");
                LogMessage($"Toplam: {totalData}, Ön Onay: {totalPreApproval}, Onay Bekleyen: {totalPendingApproval}");

                // Dönemler yüklendikten sonra, tüm dönemleri otomatik seç ve SMS alıcılarını getir
                await AutoSelectAllPeriodsAndLoadRecipients();
            }
            catch (Exception ex)
            {
                txtStatus.Text = "Dönem yükleme hatası";
                LogMessage($"Dönemler yüklenirken hata: {ex.Message}");
                System.Windows.MessageBox.Show($"Dönemler yüklenirken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                
                // Hata durumunda
            }
            finally
            {
                // Cancellation kontrolü
                if (_cancellationTokenSource?.Token.IsCancellationRequested == true)
                {
                    txtStatus.Text = "Dönem yükleme işlemi iptal edildi";
                    LogMessage("Dönem yükleme işlemi kullanıcı tarafından iptal edildi.");
                }
                
                // Loading overlay'i gizle ve SMS Gönderimi tab'ine geri dön
                LoadingOverlay.Visibility = Visibility.Collapsed;
                tabControl.SelectedIndex = 0; // SMS Gönderimi tab'ine geri dön (index 0)
                
                btnLoadPeriods.IsEnabled = true;
                // Hata olsa bile durdur butonu aktif olsun
                btnStopSms.IsEnabled = true;
            }
        }

        private async Task<List<PeriodInfo>> LoadPeriodsFromWeb()
        {
            try
            {
                LogMessage("Web sayfasından dönemler yükleniyor...");
                
                // Cancellation token kontrolü
                if (_cancellationTokenSource?.Token.IsCancellationRequested == true)
                {
                    LogMessage("Dönem yükleme işlemi iptal edildi.");
                    return new List<PeriodInfo>();
                }
                
                // SmsService'i kullanarak dönemleri yükle
                var periods = await _smsService.LoadPeriodsFromWebAsync();
                
                LogMessage($"{periods.Count} dönem bulundu.");
                
                // Tüm dönemleri al ama sadece onay sayısı > 0 olanları döndür
                var validPeriods = periods.Where(p => p.ApprovalCount > 0).ToList();
                LogMessage($"120 dönem arasından onay sayısı > 0 olan {validPeriods.Count} dönem filtrelendi.");
                
                // Cancellation token kontrolü
                if (_cancellationTokenSource?.Token.IsCancellationRequested == true)
                {
                    LogMessage("Dönem yükleme işlemi iptal edildi.");
                    return new List<PeriodInfo>();
                }
                
                // Tekrar eden dönemleri temizle
                var uniquePeriods = RemoveDuplicatePeriods(validPeriods);
                LogMessage($"Tekrar eden dönemler temizlendi: {validPeriods.Count} -> {uniquePeriods.Count}");
                
                return uniquePeriods;
            }
            catch (Exception ex)
            {
                LogMessage($"Dönem yükleme hatası: {ex.Message}");
                throw;
            }
        }
        
        private List<PeriodInfo> RemoveDuplicatePeriods(List<PeriodInfo> periods)
        {
            var uniquePeriods = new List<PeriodInfo>();
            var seenIds = new HashSet<string>();
            var seenNames = new HashSet<string>();
            var seenNormalizedNames = new HashSet<string>();
            
            foreach (var period in periods)
            {
                // ID kontrolü
                if (seenIds.Contains(period.Id))
                {
                    LogMessage($"⚠️ Aynı ID'ye sahip dönem atlandı: {period.Name} (ID: {period.Id})");
                    continue;
                }
                
                // Tam isim kontrolü
                if (seenNames.Contains(period.Name))
                {
                    LogMessage($"⚠️ Aynı isme sahip dönem atlandı: {period.Name}");
                    continue;
                }
                
                // Normalize edilmiş isim kontrolü (çizgi ve boşlukları kaldır)
                var normalizedName = period.Name.Replace("-", "").Replace(" ", "").ToLower();
                if (seenNormalizedNames.Contains(normalizedName))
                {
                    LogMessage($"⚠️ Benzer isme sahip dönem atlandı: {period.Name}");
                    continue;
                }
                
                // Şirket adı ve tarih kontrolü
                var companyMatch = System.Text.RegularExpressions.Regex.Match(period.Name, @"\(([^)]+)\)");
                if (companyMatch.Success)
                {
                    var companyName = companyMatch.Groups[1].Value.Trim();
                    var dateMatch = System.Text.RegularExpressions.Regex.Match(period.Name, @"(\d{1,2})\s*[-–]\s*(\d{1,2})\s+(\w+)\s+(\d{4})");
                    
                    if (dateMatch.Success)
                    {
                        var dateRange = $"{dateMatch.Groups[1].Value}-{dateMatch.Groups[2].Value} {dateMatch.Groups[3].Value} {dateMatch.Groups[4].Value}";
                        var key = $"{companyName}_{dateRange}";
                        
                        if (seenNormalizedNames.Contains(key))
                        {
                            LogMessage($"⚠️ Aynı şirket ve tarih aralığına sahip dönem atlandı: {period.Name}");
                            continue;
                        }
                        seenNormalizedNames.Add(key);
                    }
                }
                
                // Dönemi ekle
                uniquePeriods.Add(period);
                seenIds.Add(period.Id);
                seenNames.Add(period.Name);
                seenNormalizedNames.Add(normalizedName);
                
                LogMessage($"✅ Dönem eklendi: {period.Name}");
            }
            
            return uniquePeriods;
        }

        private void RefreshPeriodList()
        {
            // Clear existing Grid elements
            var gridsToRemove = PeriodSelectionPanel.Children
                .OfType<Grid>()
                .ToList();
            
            foreach (var item in gridsToRemove)
            {
                PeriodSelectionPanel.Children.Remove(item);
            }
            
            // Clear existing checkboxes except "Select All"
            var itemsToRemove = PeriodSelectionPanel.Children
                .OfType<CheckBox>()
                .Where(cb => cb.Name != "chkSelectAll")
                .ToList();
            
            foreach (var item in itemsToRemove)
            {
                PeriodSelectionPanel.Children.Remove(item);
            }
            
            // Remove separators
            var separatorsToRemove = PeriodSelectionPanel.Children
                .OfType<Separator>()
                .ToList();
            
            foreach (var separator in separatorsToRemove)
            {
                PeriodSelectionPanel.Children.Remove(separator);
            }
            
            // "Tümünü Seç" checkbox'ını da sıfırla
            chkSelectAll.IsChecked = false;
            
            // "Tüm dönemleri seç" checkbox'ının enabled durumunu ayarla
            var hasAnyEnabledPeriods = _availablePeriods.Any(p => !WasSentToday(p.Id) || (toggleResendToday?.IsChecked == true));
            chkSelectAll.IsEnabled = hasAnyEnabledPeriods;
            
            // Add separator after "Select All"
            PeriodSelectionPanel.Children.Add(new Separator { Margin = new Thickness(0, 0, 0, 10) });
            
            // İstatistikleri hesapla
            var totalData = _availablePeriods.Count;
            var totalPreApproval = 0;
            var totalPendingApproval = 0;
            
            // Add period checkboxes with row numbers
            for (int i = 0; i < _availablePeriods.Count; i++)
            {
                var period = _availablePeriods[i];
                var sentToday = WasSentToday(period.Id);
                var lastSmsDate = GetLastSmsSentDate(period.Id, period.Name);
                
                // Sıra numarası badge'i
                var rowNumberBadge = new Border
                {
                    Background = System.Windows.Media.Brushes.LightGray,
                    BorderBrush = System.Windows.Media.Brushes.Gray,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(6, 2, 6, 2),
                    Margin = new Thickness(0, 0, 8, 0),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Width = 30,
                    MinWidth = 30
                };

                var rowNumberText = new TextBlock
                {
                    Text = (i + 1).ToString(),
                      FontSize = 9,
                    FontWeight = FontWeights.Bold,
                    Foreground = System.Windows.Media.Brushes.DarkGray,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };

                rowNumberBadge.Child = rowNumberText;
                
                // Son SMS tarihi badge'i
                var lastSmsBadgeBorder = new Border
                {
                    Background = System.Windows.Media.Brushes.LightGreen,
                    BorderBrush = System.Windows.Media.Brushes.Green,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(4, 3, 4, 3),
                    Margin = new Thickness(10, 0, 5, 0),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Width = 100,
                    MinWidth = 100
                };

                var lastSmsBadgeText = new TextBlock
                {
                    Text = $"Son SMS: {lastSmsDate}",
                    FontSize = 10,
                    FontWeight = FontWeights.Medium,
                    Foreground = System.Windows.Media.Brushes.DarkGreen,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };

                lastSmsBadgeBorder.Child = lastSmsBadgeText;
                
                // Dönem bilgileri badge'i (sağ tarafta)
                var badgeBorder = new Border
                {
                    Background = System.Windows.Media.Brushes.LightBlue,
                    BorderBrush = System.Windows.Media.Brushes.Blue,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(4, 3, 4, 3),
                    Margin = new Thickness(5, 0, 15, 0),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Width = 180,
                    MinWidth = 180
                };

                var badgeText = new TextBlock
                {
                    Text = period.Description,
                    FontSize = 10,
                    FontWeight = FontWeights.Medium,
                    Foreground = System.Windows.Media.Brushes.DarkBlue,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };

                badgeBorder.Child = badgeText;

                var mainGrid = new Grid();
                mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Sıra numarası
                mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Checkbox
                mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Son SMS
                mainGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Dönem bilgisi

                // Dönem adını temizle (yeni satır karakterlerini kaldır ve uzunluğu sınırla)
                var cleanPeriodName = period.Name.Replace("\r", "").Replace("\n", "").Replace("\t", " ").Trim();
                if (cleanPeriodName.Length > 50)
                {
                    cleanPeriodName = cleanPeriodName.Substring(0, 47) + "...";
                }
                
                var checkbox = new CheckBox
                {
                    Content = cleanPeriodName + (sentToday ? " (Bugün gönderildi ✓)" : ""),
                    Tag = period,
                    Margin = new Thickness(0, 0, 0, 5),
                    IsChecked = false, // Hiçbiri seçili gelmesin
                    IsEnabled = toggleResendToday?.IsChecked == true || !sentToday, // Switch açıksa veya bugün gönderilmemişse aktif
                    Foreground = sentToday ? System.Windows.Media.Brushes.Gray : System.Windows.Media.Brushes.Black,
                    FontSize = 12,
                    FontWeight = sentToday ? FontWeights.Normal : FontWeights.Medium,
                    VerticalAlignment = VerticalAlignment.Center
                };

                // Checkbox seçim değişikliğini dinle
                checkbox.Checked += async (s, e) => await OnPeriodSelectionChanged();
                checkbox.Unchecked += async (s, e) => await OnPeriodSelectionChanged();

                Grid.SetColumn(rowNumberBadge, 0);
                Grid.SetColumn(checkbox, 1);
                Grid.SetColumn(lastSmsBadgeBorder, 2);
                Grid.SetColumn(badgeBorder, 3);

                mainGrid.Children.Add(rowNumberBadge);
                mainGrid.Children.Add(checkbox);
                mainGrid.Children.Add(lastSmsBadgeBorder);
                mainGrid.Children.Add(badgeBorder);
                
                PeriodSelectionPanel.Children.Add(mainGrid);
                
                // Satırları ayırmak için çizgi ekle
                var separator = new Separator
                {
                    Margin = new Thickness(20, 8, 0, 8),
                    Background = System.Windows.Media.Brushes.LightGray,
                    Opacity = 0.6
                };
                PeriodSelectionPanel.Children.Add(separator);
                
                // İstatistikleri güncelle
                var descriptionParts = period.Description.Split(',');
                foreach (var part in descriptionParts)
                {
                    var trimmedPart = part.Trim();
                    if (trimmedPart.StartsWith("Ön Onay:"))
                    {
                        var countStr = trimmedPart.Replace("Ön Onay:", "").Trim();
                        if (int.TryParse(countStr, out int count))
                        {
                            totalPreApproval += count;
                        }
                    }
                    else if (trimmedPart.StartsWith("Onay Bekleyen:"))
                    {
                        var countStr = trimmedPart.Replace("Onay Bekleyen:", "").Trim();
                        if (int.TryParse(countStr, out int count))
                        {
                            totalPendingApproval += count;
                        }
                    }
                }
            }
            
            // İstatistik badge'lerini güncelle
            UpdateStatisticsBadges(totalData, totalPreApproval, totalPendingApproval);
        }

        private void UpdateStatisticsBadges(int totalData, int totalPreApproval, int totalPendingApproval)
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    if (txtTotalData != null)
                        txtTotalData.Text = $"Toplam: {totalData}";
                    
                    if (txtPreApproval != null)
                        txtPreApproval.Text = $"Ön Onay: {totalPreApproval}";
                    
                    if (txtPendingApproval != null)
                        txtPendingApproval.Text = $"Onay Bekleyen: {totalPendingApproval}";
                });
            }
            catch (Exception ex)
            {
                LogMessage($"İstatistik badge'leri güncellenirken hata: {ex.Message}");
            }
        }

        private bool WasSentToday(string periodId)
        {
            try
            {
                if (!File.Exists(_sentTodayFilePath))
                    return false;
                
                var today = DateTime.Today.ToString("yyyy-MM-dd");
                var lines = File.ReadAllLines(_sentTodayFilePath);
                
                return lines.Any(line => line.StartsWith($"{today}|{periodId}|"));
            }
            catch
            {
                return false;
            }
        }

        private void MarkAsSentToday(string periodId, string periodName)
        {
            try
            {
                var today = DateTime.Today.ToString("yyyy-MM-dd");
                var time = DateTime.Now.ToString("HH:mm");
                var entry = $"{today}|{periodId}|{periodName}|{time}";
                
                File.AppendAllText(_sentTodayFilePath, entry + Environment.NewLine);
                
                // Son SMS gönderim tarihini de kaydet
                var lastSmsEntry = $"{periodId}|{periodName}|{today} {time}";
                SaveLastSmsSentDate(periodId, periodName, $"{today} {time}");
                
                LogMessage($"{periodName} için bugün SMS gönderildi olarak işaretlendi.");
            }
            catch (Exception ex)
            {
                LogMessage($"SMS gönderim kaydı tutulurken hata: {ex.Message}");
            }
        }

        private string GetLastSmsSentDate(string periodId, string periodName)
        {
            try
            {
                if (!File.Exists(_lastSmsSentFilePath))
                    return "-";
                
                var lines = File.ReadAllLines(_lastSmsSentFilePath);
                
                foreach (var line in lines)
                {
                    var parts = line.Split('|');
                    if (parts.Length >= 3)
                    {
                        var storedPeriodId = parts[0];
                        var storedPeriodName = parts[1];
                        
                        // ID veya isim eşleşmesi kontrol et
                        if (storedPeriodId == periodId || storedPeriodName == periodName)
                        {
                            return parts[2]; // Son gönderim tarihi
                        }
                    }
                }
                
                return "-";
            }
            catch
            {
                return "-";
            }
        }

        private void SaveLastSmsSentDate(string periodId, string periodName, string dateTime)
        {
            try
            {
                var lines = new List<string>();
                
                // Mevcut kayıtları oku
                if (File.Exists(_lastSmsSentFilePath))
                {
                    lines = File.ReadAllLines(_lastSmsSentFilePath).ToList();
                }
                
                // Aynı dönem için eski kaydı kaldır
                lines.RemoveAll(line => 
                {
                    var parts = line.Split('|');
                    return parts.Length >= 2 && (parts[0] == periodId || parts[1] == periodName);
                });
                
                // Yeni kaydı ekle
                lines.Add($"{periodId}|{periodName}|{dateTime}");
                
                // Dosyaya yaz
                File.WriteAllLines(_lastSmsSentFilePath, lines);
            }
            catch (Exception ex)
            {
                LogMessage($"Son SMS gönderim tarihi kaydedilirken hata: {ex.Message}");
            }
        }

        private async void chkSelectAll_Checked(object sender, RoutedEventArgs e)
        {
            var periodCheckboxes = PeriodSelectionPanel.Children
                .OfType<Grid>()
                .SelectMany(grid => grid.Children.OfType<CheckBox>())
                .Where(cb => cb.IsEnabled);
            
            foreach (var checkbox in periodCheckboxes)
            {
                checkbox.IsChecked = true;
            }
            
            LogMessage("Tüm dönemler seçildi.");
            
            // SMS alıcılarını güncelle
            await OnPeriodSelectionChanged();
        }

        private async void chkSelectAll_Unchecked(object sender, RoutedEventArgs e)
        {
            var periodCheckboxes = PeriodSelectionPanel.Children
                .OfType<Grid>()
                .SelectMany(grid => grid.Children.OfType<CheckBox>())
                .Where(cb => cb.IsEnabled);
            
            foreach (var checkbox in periodCheckboxes)
            {
                checkbox.IsChecked = false;
            }
            
            LogMessage("Tüm dönemler seçimi kaldırıldı.");
            
            // SMS alıcılarını güncelle
            await OnPeriodSelectionChanged();
        }

        private async void btnStartSms_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var selectedPeriods = GetSelectedPeriods();
                
                if (!selectedPeriods.Any())
                {
                    System.Windows.MessageBox.Show("Lütfen en az bir dönem seçin.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Custom Alert ile onay sor
                var customAlert = new CustomAlertWindow(
                    "SMS Gönderimi Onayı",
                    $"Seçili {selectedPeriods.Count} dönem için SMS gönderimi yapılacak.\n\n" +
                    $"Dönemler:\n{string.Join("\n", selectedPeriods.Take(3).Select(p => $"• {p.Name}"))}" +
                    (selectedPeriods.Count > 3 ? $"\n... ve {selectedPeriods.Count - 3} dönem daha" : ""),
                    "SMS Gönder",
                    "İptal"
                );
                
                // Modal konumlandırma - ana pencere ortası
                customAlert.Owner = this;
                customAlert.WindowStartupLocation = WindowStartupLocation.CenterOwner;
                customAlert.Topmost = true;
                
                var result = customAlert.ShowDialog();
                if (result != true)
                {
                    LogMessage("SMS gönderimi kullanıcı tarafından iptal edildi.");
                    return;
                }

                _isProcessing = true;
                _cancellationTokenSource = new CancellationTokenSource();
                
                // Loading overlay'i göster
                LoadingOverlay.Visibility = Visibility.Visible;
                LoadingText.Text = "SMS gönderimi başlatılıyor...";
                
                // Progress bar'ı göster
                ShowProgressBar(selectedPeriods.Count);
                
                btnStartSms.IsEnabled = false;
                btnStopSms.IsEnabled = true;
                btnLoadPeriods.IsEnabled = false;
                
                txtStatus.Text = "SMS gönderimi başlatılıyor...";
                LogMessage($"SMS gönderimi başlatıldı. {selectedPeriods.Count} dönem için işlem yapılacak.");
                
                // İşlem Logları tabına geç
                tabControl.SelectedIndex = 2; // İşlem Logları tabının indeksi

                // Seçili SMS alıcılarını al
                var selectedRecipients = _recipientsCollection?.Where(r => r.IsSelected).ToList() ?? new List<SmsRecipientInfo>();
                
                if (!selectedRecipients.Any())
                {
                    System.Windows.MessageBox.Show("Lütfen en az bir SMS alıcısı seçin.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                LogMessage($"Seçili {selectedRecipients.Count} alıcıya SMS gönderilecek.");

                // Her seçili dönem için SMS gönder
                var totalPeriods = selectedPeriods.Count;
                var currentPeriod = 0;
                
                foreach (var period in selectedPeriods)
                {
                    _cancellationTokenSource.Token.ThrowIfCancellationRequested();
                    
                    currentPeriod++;
                    LogMessage($"📱 {currentPeriod}/{totalPeriods} - {period.Name} dönemi için SMS gönderimi başlatılıyor...");
                    
                    // Progress bar'ı güncelle
                    UpdateProgressBar(currentPeriod - 1, totalPeriods);
                    LoadingText.Text = $"{period.Name} dönemi için SMS gönderiliyor...";
                    
                    try
                    {
                        // SMS gönderim işlemini başlat
                        await _smsService.SendSmsForPeriodAsync(period, selectedRecipients, _cancellationTokenSource.Token);
                        
                        // SMS geçmişine kayıt ekle
                        try
                        {
                            var periodRecipients = selectedRecipients
                                .Where(r => string.Equals(r.PeriodName?.Trim(), period.Name?.Trim(), StringComparison.OrdinalIgnoreCase))
                                .ToList();

                            if (periodRecipients.Any())
                            {
                                await _smsHistoryService.AddBulkSmsRecordsAsync(periodRecipients, period.Name, "Başarılı");
                                UpdateSmsStatistics();
                                LogMessage($"✅ {periodRecipients.Count} adet SMS kaydı geçmişe eklendi (Dönem: {period.Name}).");
                            }
                            else
                            {
                                LogMessage($"⚠ {period.Name} dönemi için eşleşen alıcı bulunamadı; geçmişe kayıt eklenmedi.");
                            }
                        }
                        catch (Exception historyEx)
                        {
                            LogMessage($"SMS geçmişine kayıt eklenirken hata: {historyEx.Message}");
                        }
                        
                        // Mark as sent today
                        MarkAsSentToday(period.Id, period.Name);
                        
                        LogMessage($"✅ {period.Name} dönemi için SMS gönderimi tamamlandı ({currentPeriod}/{totalPeriods})");
                        
                        // Progress bar'ı güncelle
                        UpdateProgressBar(currentPeriod, totalPeriods);
                    }
                    catch (Exception periodEx)
                    {
                        LogMessage($"❌ {period.Name} dönemi için SMS gönderimi başarısız: {periodEx.Message}");
                        
                        // Hata bilgisini progress bar'a ekle
                        bool isTimeout = periodEx.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase) || 
                                       periodEx.Message.Contains("zaman aşımı", StringComparison.OrdinalIgnoreCase);
                        AddError($"{period.Name}: {periodEx.Message}", isTimeout);
                        
                        // Hata durumunda da geçmişe kayıt ekle (başarısız olarak)
                        try
                        {
                            var periodRecipients = selectedRecipients
                                .Where(r => string.Equals(r.PeriodName?.Trim(), period.Name?.Trim(), StringComparison.OrdinalIgnoreCase))
                                .ToList();

                            if (periodRecipients.Any())
                            {
                                await _smsHistoryService.AddBulkSmsRecordsAsync(periodRecipients, period.Name, "Başarısız");
                                UpdateSmsStatistics();
                            }
                            else
                            {
                                LogMessage($"⚠ {period.Name} dönemi için eşleşen alıcı bulunamadı; başarısız kayıt eklenmedi.");
                            }
                        }
                        catch (Exception historyEx)
                        {
                            LogMessage($"Hatalı SMS geçmişine kayıt eklenirken hata: {historyEx.Message}");
                        }
                    }
                }
                
                // Refresh list to show sent status
                RefreshPeriodList();
                
                txtStatus.Text = "SMS gönderimi tamamlandı";
                LogMessage($"🎉 SMS gönderimi başarıyla tamamlandı! Toplam {totalPeriods} dönem işlendi.");
                
                ShowEmbeddedAlert(
                    "✅ SMS Gönderimi Tamamlandı!",
                    $"SMS gönderimi başarıyla tamamlandı.\nBaşarılı: {totalPeriods}/{totalPeriods}",
                    "Tamam",
                    "",
                    () => {
                        // Onaylandığında hiçbir şey yapma, sadece kapat
                    }
                );
            }
            catch (OperationCanceledException)
            {
                txtStatus.Text = "SMS gönderimi iptal edildi";
                LogMessage("SMS gönderimi kullanıcı tarafından iptal edildi.");
                
                // Progress bar'ı gizle
                HideProgressBar();
            }
            catch (Exception ex)
            {
                txtStatus.Text = "SMS gönderimi hatası";
                LogMessage($"SMS gönderimi sırasında hata: {ex.Message}");
                System.Windows.MessageBox.Show($"SMS gönderimi sırasında hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                
                // Progress bar'ı gizle
                HideProgressBar();
            }
            finally
            {
                // Progress bar'ı gizle
                HideProgressBar();
                
                // Loading overlay'i gizle
                LoadingOverlay.Visibility = Visibility.Collapsed;
                
                _isProcessing = false;
                btnStartSms.IsEnabled = true;
                btnStopSms.IsEnabled = false;
                btnLoadPeriods.IsEnabled = true;
                _cancellationTokenSource?.Dispose();
            }
        }

        private async void btnStopSms_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _cancellationTokenSource?.Cancel();
                txtStatus.Text = "SMS gönderimi durduruluyor...";
                LogMessage("SMS gönderimi durdurma talebi gönderildi.");
                
                // SmsService'deki tarayıcıyı da kapat
                await _smsService.StopAsync();
                LogMessage("Tarayıcı kapatıldı ve işlem durduruldu.");
                
                // Buton durumlarını sıfırla
                btnStartSms.IsEnabled = true;
                btnStopSms.IsEnabled = false;
                btnLoadPeriods.IsEnabled = true;
                
                _isProcessing = false;
                txtStatus.Text = "İşlem durduruldu";
            }
            catch (Exception ex)
            {
                LogMessage($"Durdurma işlemi sırasında hata: {ex.Message}");
                txtStatus.Text = "Durdurma hatası";
            }
        }

        private List<PeriodInfo> GetSelectedPeriods()
        {
            var selectedPeriods = new List<PeriodInfo>();
            
            foreach (var child in PeriodSelectionPanel.Children)
            {
                if (child is Grid grid)
                {
                    foreach (var gridChild in grid.Children)
                    {
                        if (gridChild is CheckBox checkbox && checkbox.Tag is PeriodInfo periodInfo)
                        {
                            // Checkbox'ın gerçek durumunu kontrol et
                            if (checkbox.IsChecked == true)
                            {
                                selectedPeriods.Add(periodInfo);
                                LogMessage($"DEBUG: Seçili dönem bulundu: {periodInfo.Name} (IsChecked: {checkbox.IsChecked})");
                            }
                            else
                            {
                                LogMessage($"DEBUG: Seçilmemiş dönem: {periodInfo.Name} (IsChecked: {checkbox.IsChecked})");
                            }
                        }
                    }
                }
            }
            
            LogMessage($"DEBUG: Toplam seçili dönem sayısı: {selectedPeriods.Count}");
            return selectedPeriods;
        }

        private void OnLogMessage(object? sender, LogMessageEventArgs e)
        {
            Dispatcher.Invoke(() => LogMessage(e.Message));
        }

        private void OnStatusChanged(object? sender, string status)
        {
            Dispatcher.Invoke(() => txtStatus.Text = status);
        }

        private void LogMessage(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            var logEntry = $"[{timestamp}] {message}\n";
            txtLog.AppendText(logEntry);
            
            // Otomatik scroll - hem TextBox hem de ScrollViewer için
            txtLog.CaretIndex = txtLog.Text.Length;
            txtLog.ScrollToEnd();
            
            // ScrollViewer'ı da en alta kaydır
            var scrollViewer = FindParentScrollViewer(txtLog);
            if (scrollViewer != null)
            {
                scrollViewer.ScrollToEnd();
                scrollViewer.ScrollToBottom();
            }
            
            // Alternatif olarak UI thread'de tekrar scroll yap
            Dispatcher.BeginInvoke(new Action(() =>
            {
                txtLog.ScrollToEnd();
                if (scrollViewer != null)
                {
                    scrollViewer.ScrollToEnd();
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        /// <summary>
        /// TextBox'ın parent ScrollViewer'ını bulur
        /// </summary>
        private ScrollViewer? FindParentScrollViewer(DependencyObject child)
        {
            try
            {
                var parent = VisualTreeHelper.GetParent(child);
                while (parent != null)
                {
                    if (parent is ScrollViewer scrollViewer)
                    {
                        return scrollViewer;
                    }
                    parent = VisualTreeHelper.GetParent(parent);
                }
                return null;
            }
            catch
            {
                return null;
            }
        }


        // Ayar işlevleri
        private void LoadSettings()
        {
            try
            {
                var config = ConfigManager.LoadConfig();
                
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
                txtTargetUrl.Text = "https://pinhuman.net";
                chkHeadlessMode.IsChecked = config.Sms.HeadlessMode;

                // txtOutputFolder.Text = config.Download.OutputFolder; // SMS için gerekli değil
            }
            catch (Exception ex)
            {
                LogMessage($"Ayarlar yüklenirken hata: {ex.Message}");
            }
        }

        private void SaveSettings()
        {
            try
            {
                var config = ConfigManager.LoadConfig();
                
                config.AutoLogin.Enabled = chkAutoLogin.IsChecked ?? false;
                config.AutoLogin.Username = txtUsername.Text;
                config.AutoLogin.Password = txtPassword.Password;
                config.AutoLogin.CompanyCode = cmbCompanyCode.SelectedItem != null ? 
                    (cmbCompanyCode.SelectedItem as ComboBoxItem)?.Content.ToString() : "ikb";
                // TOTP Secret'ı doğru alanından al
                config.AutoLogin.TotpSecret = txtTotpSecret.Visibility == Visibility.Visible 
                    ? txtTotpSecret.Password 
                    : txtTotpSecretVisible.Text;
                config.Scraping.TargetUrl = "https://pinhuman.net";
                config.Sms.HeadlessMode = chkHeadlessMode.IsChecked ?? true;

                // config.Download.OutputFolder = txtOutputFolder.Text; // SMS için gerekli değil
                
                ConfigManager.SaveConfig(config);
                LogMessage("Ayarlar başarıyla kaydedildi.");
                
                ShowEmbeddedAlert(
                    "✅ Ayarlar Kaydedildi!",
                    "Ayarlar başarıyla kaydedildi.",
                    "Tamam",
                    "",
                    () => {
                        // Onaylandığında hiçbir şey yapma, sadece kapat
                    }
                );
            }
            catch (Exception ex)
            {
                LogMessage($"Ayarlar kaydedilirken hata: {ex.Message}");
                System.Windows.MessageBox.Show($"Ayarlar kaydedilirken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ResetSettings()
        {
            try
            {
                var result = System.Windows.MessageBox.Show("Tüm ayarları sıfırlamak istediğinizden emin misiniz?", 
                    "Onay", MessageBoxButton.YesNo, MessageBoxImage.Question);
                
                if (result == MessageBoxResult.Yes)
                {
                    var config = new AppConfig();
                    ConfigManager.SaveConfig(config);
                    LoadSettings();
                    LogMessage("Ayarlar sıfırlandı.");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Ayarlar sıfırlanırken hata: {ex.Message}");
            }
        }

        private void chkAutoLogin_Changed(object sender, RoutedEventArgs e)
        {
            var isEnabled = chkAutoLogin.IsChecked ?? false;
            txtUsername.IsEnabled = isEnabled;
            txtPassword.IsEnabled = isEnabled;
                            cmbCompanyCode.IsEnabled = isEnabled;
            txtTotpSecret.IsEnabled = isEnabled;
            txtTargetUrl.IsEnabled = isEnabled;
        }

        private void chkHeadlessMode_Changed(object sender, RoutedEventArgs e)
        {
            var isHeadless = chkHeadlessMode.IsChecked ?? true;
            LogMessage($"Gizli mod ayarı değiştirildi: {(isHeadless ? "Açık" : "Kapalı")}");
        }



        /// <summary>
        /// Tekrar eden SMS alıcılarını filtreler
        /// Aynı isim ve telefon numarasına sahip alıcıları tek bir alıcı olarak birleştirir
        /// </summary>
        private List<SmsRecipientInfo> RemoveDuplicateRecipients(List<SmsRecipientInfo> recipients)
        {
            try
            {
                var uniqueRecipients = new List<SmsRecipientInfo>();
                var seenCombinations = new HashSet<string>();

                foreach (var recipient in recipients)
                {
                    // İsim ve telefon numarasını birleştirerek benzersiz bir anahtar oluştur
                    var key = $"{recipient.Name.Trim().ToLowerInvariant()}_{recipient.Phone.Trim()}";
                    
                    if (!seenCombinations.Contains(key))
                    {
                        seenCombinations.Add(key);
                        recipient.IsDuplicate = false;
                        uniqueRecipients.Add(recipient);
                    }
                    else
                    {
                        // Tekrar eden alıcıyı logla
                        LogMessage($"Tekrar eden alıcı filtrelendi: {recipient.Name} ({recipient.Phone}) - Dönem: {recipient.PeriodName}");
                    }
                }

                return uniqueRecipients;
            }
            catch (Exception ex)
            {
                LogMessage($"Tekrar eden alıcıları filtrelerken hata: {ex.Message}");
                return recipients; // Hata durumunda orijinal listeyi döndür
            }
        }

        /// <summary>
        /// Tekrar eden SMS alıcılarını işaretler
        /// Aynı isim ve telefon numarasına sahip alıcıları tespit eder ve işaretler
        /// </summary>
        private List<SmsRecipientInfo> MarkDuplicateRecipients(List<SmsRecipientInfo> recipients)
        {
            try
            {
                var processedRecipients = new List<SmsRecipientInfo>();
                var seenCombinations = new Dictionary<string, int>();

                foreach (var recipient in recipients)
                {
                    // İsim ve telefon numarasını birleştirerek benzersiz bir anahtar oluştur
                    var key = $"{recipient.Name.Trim().ToLowerInvariant()}_{recipient.Phone.Trim()}";
                    
                    if (!seenCombinations.ContainsKey(key))
                    {
                        seenCombinations[key] = 1;
                        // İlk kez görülen alıcı - normal
                        recipient.PeriodName = $"{recipient.PeriodName}";
                        recipient.IsDuplicate = false;
                    }
                    else
                    {
                        seenCombinations[key]++;
                        // Tekrar eden alıcı - dönem adına tekrar sayısını ekle
                        recipient.PeriodName = $"{recipient.PeriodName} (Tekrar #{seenCombinations[key]})";
                        recipient.IsDuplicate = true;
                        LogMessage($"Tekrar eden alıcı işaretlendi: {recipient.Name} ({recipient.Phone}) - Dönem: {recipient.PeriodName}");
                    }
                    
                    processedRecipients.Add(recipient);
                }

                return processedRecipients;
            }
            catch (Exception ex)
            {
                LogMessage($"Tekrar eden alıcıları işaretlerken hata: {ex.Message}");
                return recipients; // Hata durumunda orijinal listeyi döndür
            }
        }

        private void btnTotpInfo_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.MessageBox.Show(
                "TOTP Secret, Google Authenticator veya benzeri 2FA uygulamalarında kullanılan gizli anahtardır.\n\n" +
                "Bu anahtarı sistem yöneticinizden alabilirsiniz.",
                "TOTP Secret Bilgisi",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void btnToggleTotpVisibility_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (txtTotpSecret.Visibility == Visibility.Visible)
                {
                    // Görünür yap - TextBox'a geç
                    txtTotpSecretVisible.Text = txtTotpSecret.Password;
                    txtTotpSecret.Visibility = Visibility.Collapsed;
                    txtTotpSecretVisible.Visibility = Visibility.Visible;
                    btnToggleTotpVisibility.Content = "🙈";
                    btnToggleTotpVisibility.ToolTip = "TOTP Secret'ı gizle";
                }
                else
                {
                    // Gizli yap - PasswordBox'a geç
                    txtTotpSecret.Password = txtTotpSecretVisible.Text;
                    txtTotpSecretVisible.Visibility = Visibility.Collapsed;
                    txtTotpSecret.Visibility = Visibility.Visible;
                    btnToggleTotpVisibility.Content = "👁️";
                    btnToggleTotpVisibility.ToolTip = "TOTP Secret'ı göster";
                }
            }
            catch (Exception ex)
            {
                LogMessage($"TOTP visibility toggle hatası: {ex.Message}");
            }
        }

        // private void btnSelectOutputFolder_Click(object sender, RoutedEventArgs e)
        // {
        //     // SMS için gerekli değil
        // }

        // private void btnClearOutputFolder_Click(object sender, RoutedEventArgs e)
        // {
        //     // SMS için gerekli değil
        // }

        private void btnSaveSettings_Click(object sender, RoutedEventArgs e)
        {
            SaveSettings();
        }

        private void btnResetSettings_Click(object sender, RoutedEventArgs e)
        {
            ResetSettings();
        }

        /// <summary>
        /// Seçili dönemler için SMS alıcılarını yükler
        /// </summary>
        private async Task LoadSmsRecipientsForSelectedPeriods()
        {
            try
            {
                // Loading overlay'i göster ve SMS Gönderimi tab'ine geç
                await Dispatcher.InvokeAsync(() =>
                {
                    LoadingOverlay.Visibility = Visibility.Visible;
                    LoadingText.Text = "SMS Alıcıları Getiriliyor..."; // SMS alıcıları için farklı mesaj
                    tabControl.SelectedIndex = 0; // SMS Gönderimi tab'ine geç (index 0)

                    // ESC tuşu ile kapatma özelliği ekle
                    this.KeyDown += LoadingKeyDownHandler;
                });
                
                LogMessage("DEBUG: LoadSmsRecipientsForSelectedPeriods başladı!");
                LogMessage("Seçili dönemler için SMS alıcıları yükleniyor...");
                
                var selectedPeriods = GetSelectedPeriods();
                LogMessage($"DEBUG: Seçili dönem sayısı: {selectedPeriods.Count}");
                
                if (!selectedPeriods.Any())
                {
                    LogMessage("Hiç dönem seçilmemiş, SMS alıcıları yüklenmeyecek.");
                    return;
                }
                
                // Eğer zaten yükleme yapılıyorsa, çık
                if (_isLoadingRecipients)
                {
                    LogMessage("SMS alıcıları zaten yükleniyor, işlem atlanıyor.");
                    return;
                }
                
                _isLoadingRecipients = true;
                
                // Yükleme sırasında yenile butonunu pasif et
                await Dispatcher.InvokeAsync(() =>
                {
                    btnRefreshRecipients.IsEnabled = false;
                    btnRefreshRecipients.Content = "Yükleniyor...";
                });
                
                try
                {
                    foreach (var period in selectedPeriods)
                    {
                        LogMessage($"DEBUG: Seçili dönem: {period.Name} (ID: {period.Id})");
                    }

                    // Progress bar'ı göster
                    ShowProgressBar(selectedPeriods.Count);

                    var allRecipients = new List<SmsRecipientInfo>();
                    var completedPeriods = 0;

                    foreach (var period in selectedPeriods)
                    {
                        // Cancellation kontrolü - ESC ile durdurulmuşsa çık
                        if (_cancellationTokenSource?.Token.IsCancellationRequested == true)
                        {
                            LogMessage("SMS alıcıları yükleme işlemi kullanıcı tarafından iptal edildi.");
                            break;
                        }

                        try
                        {
                            LogMessage($"DEBUG: {period.Name} dönemi işleniyor...");

                            // Loading text'i güncelle
                            await Dispatcher.InvokeAsync(() =>
                            {
                                LoadingText.Text = $"{period.Name} dönemi için SMS alıcıları getiriliyor...";
                            });

                            // Cache'de var mı kontrol et
                            if (_recipientsCache.ContainsKey(period.Id))
                            {
                                var cachedRecipients = _recipientsCache[period.Id];
                                allRecipients.AddRange(cachedRecipients);
                                LogMessage($"{period.Name} dönemi için cache'den {cachedRecipients.Count} SMS alıcısı alındı.");

                                // Progress'i güncelle
                                completedPeriods++;
                                UpdateProgressBar(completedPeriods, selectedPeriods.Count);
                                continue;
                            }

                            LogMessage($"{period.Name} dönemi için SMS alıcıları alınıyor...");

                            var recipients = await _smsService.GetSmsRecipientsForPeriodAsync(period);
                            LogMessage($"DEBUG: {period.Name} dönemi için {recipients.Count} alıcı döndü.");
                            
                            if (recipients.Any())
                            {
                                // Cache'e kaydet
                                _recipientsCache[period.Id] = recipients.ToList();
                                
                                allRecipients.AddRange(recipients);
                                LogMessage($"{period.Name} dönemi için {recipients.Count} SMS alıcısı bulundu ve cache'e kaydedildi.");
                            }
                            else
                            {
                                LogMessage($"{period.Name} dönemi için SMS alıcısı bulunamadı.");
                            }
                            
                            // Progress'i güncelle
                            completedPeriods++;
                            UpdateProgressBar(completedPeriods, selectedPeriods.Count);
                        }
                        catch (Exception ex)
                        {
                            LogMessage($"{period.Name} dönemi için SMS alıcıları alınırken hata: {ex.Message}");
                            
                            // Hata bilgisini progress bar'a ekle
                            bool isTimeout = ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase) || 
                                           ex.Message.Contains("zaman aşımı", StringComparison.OrdinalIgnoreCase);
                            AddError($"{period.Name}: {ex.Message}", isTimeout);
                            
                            // Hata olsa bile progress'i güncelle
                            completedPeriods++;
                            UpdateProgressBar(completedPeriods, selectedPeriods.Count);
                            continue;
                        }
                    }
                    
                    if (allRecipients.Any())
                    {
                        LogMessage($"Toplam {allRecipients.Count} SMS alıcısı bulundu.");
                        
                        // Tüm alıcıları göster (tekrar eden alıcılar ayrı tab'de gösterilecek)
                        var processedRecipients = allRecipients;
                        LogMessage($"Tüm alıcılar gösteriliyor: {processedRecipients.Count} alıcı");
                        
                        // SMS alıcılarını listeye yükle (UI thread'de)
                        await Dispatcher.InvokeAsync(() =>
                        {
                            try
                            {
                                LogMessage("UI thread'e geçildi, ObservableCollection güncelleniyor...");
                                
                                // ObservableCollection'ı temizle ve yeniden doldur
                                _recipientsCollection.Clear();
                                
                                LogMessage($"Alıcı ekleme başlıyor: {processedRecipients.Count} alıcı var");
                                
                                foreach (var recipient in processedRecipients)
                                {
                                    recipient.IsSelected = false; // Varsayılan: seçili gelmesin
                                    LogMessage($"Alıcı ekleniyor: Name='{recipient.Name}', Phone='{recipient.Phone}', PeriodName='{recipient.PeriodName}', IsSelected={recipient.IsSelected}");
                                    _recipientsCollection.Add(recipient);
                                }
                                
                                // ListView'ın ItemsSource'unu ObservableCollection'a ayarla
                                lstSmsRecipients.ItemsSource = _recipientsCollection;
                                
                                // Dönem filtresi dropdown'unu doldur
                                PopulatePeriodFilter(processedRecipients);
                                // Ay filtresi dropdown'unu doldur
                                PopulateMonthFilter(processedRecipients);
                                
                                // SMS alıcıları yüklendi
                                
                                // Debug: ListView'ın durumunu kontrol et
                                LogMessage($"DEBUG: ListView ItemsSource ayarlandı: {lstSmsRecipients.ItemsSource != null}");
                                LogMessage($"DEBUG: ObservableCollection count: {_recipientsCollection.Count}");
                                LogMessage($"DEBUG: ListView Items count: {lstSmsRecipients.Items.Count}");
                                
                                // Alıcıların detaylarını logla
                                for (int i = 0; i < Math.Min(_recipientsCollection.Count, 3); i++)
                                {
                                    var recipient = _recipientsCollection[i];
                                    LogMessage($"DEBUG: Alıcı {i+1}: Name='{recipient.Name}', Phone='{recipient.Phone}', PeriodName='{recipient.PeriodName}', IsSelected={recipient.IsSelected}");
                                }
                                
                                LogMessage($"ObservableCollection {_recipientsCollection.Count} alıcı ile güncellendi.");
                                UpdateRecipientCount();
                                LogMessage($"UI'da {_recipientsCollection.Count} alıcı listeye yüklendi.");
                                
                                // ListView'ın görünürlüğünü kontrol et
                                LogMessage($"ListView görünür: {lstSmsRecipients.IsVisible}, ItemsSource: {lstSmsRecipients.ItemsSource != null}");
                                LogMessage($"ListView MaxHeight: {lstSmsRecipients.MaxHeight}, ActualHeight: {lstSmsRecipients.ActualHeight}");
                                LogMessage($"ListView Visibility: {lstSmsRecipients.Visibility}");
                                
                                // ListView'ı yeniden boyutlandır
                                lstSmsRecipients.MaxHeight = 400;
                                LogMessage($"ListView MaxHeight 400 olarak ayarlandı");
                                
                                // ListView'ı zorla yenile
                                lstSmsRecipients.Items.Refresh();
                                LogMessage("ListView Items.Refresh() çağrıldı");
                                
                                // Tekrar eden alıcıları işle
                                ProcessDuplicateRecipients(processedRecipients);
                                
                                // Yenile butonunu güncelle
                                UpdateRecipientCount();
                            }
                            catch (Exception ex)
                            {
                                LogMessage($"UI güncelleme hatası: {ex.Message}");
                            }
                        });
                    }
                    else
                    {
                        LogMessage("Hiç SMS alıcısı bulunamadı.");
                        
                        await Dispatcher.InvokeAsync(() =>
                        {
                            try
                            {
                                _recipientsCollection.Clear();
                                lstSmsRecipients.ItemsSource = null;
                                UpdateRecipientCount();
                            }
                            catch (Exception ex)
                            {
                                LogMessage($"UI temizleme hatası: {ex.Message}");
                            }
                        });
                    }
                }
                finally
                {
                    // Progress bar'ı gizle
                    HideProgressBar();

                    // Loading overlay'i gizle ve SMS Gönderimi tab'ine geri dön
                    await Dispatcher.InvokeAsync(() =>
                    {
                        LoadingOverlay.Visibility = Visibility.Collapsed;
                        LoadingText.Text = "Dönemler yükleniyor..."; // Mesajı geri değiştir
                        tabControl.SelectedIndex = 0; // SMS Gönderimi tab'ine geri dön (index 0)

                        // ESC tuşu event handler'ını kaldır
                        this.KeyDown -= LoadingKeyDownHandler;

                        // Yenile butonunu güncelle
                        UpdateRecipientCount();
                    });

                    _isLoadingRecipients = false;

                    // Cancellation olduysa log mesajı ekle
                    if (_cancellationTokenSource?.Token.IsCancellationRequested == true)
                    {
                        LogMessage("❌ SMS alıcıları yükleme işlemi ESC ile durduruldu.");
                        await Dispatcher.InvokeAsync(() =>
                        {
                            txtStatus.Text = "SMS alıcıları yükleme işlemi durduruldu";
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage($"SMS alıcıları yüklenirken hata: {ex.Message}");
                
                // Progress bar'ı gizle
                HideProgressBar();
                
                // Loading overlay'i gizle ve SMS Gönderimi tab'ine geri dön
                await Dispatcher.InvokeAsync(() =>
                {
                    LoadingOverlay.Visibility = Visibility.Collapsed;
                    LoadingText.Text = "Dönemler yükleniyor..."; // Mesajı geri değiştir
                    tabControl.SelectedIndex = 0; // SMS Gönderimi tab'ine geri dön (index 0)
                    
                    // Yenile butonunu güncelle
                    UpdateRecipientCount();
                });
                
                _isLoadingRecipients = false;
            }
        }

        /// <summary>
        /// Dönem seçimi değiştiğinde çağrılır
        /// </summary>
        private async Task OnPeriodSelectionChanged()
        {
            try
            {
                LogMessage("DEBUG: OnPeriodSelectionChanged çağrıldı!");
                
                // Eğer zaten yükleme yapılıyorsa, çık
                if (_isLoadingRecipients)
                {
                    LogMessage("SMS alıcıları zaten yükleniyor, işlem atlanıyor.");
                    return;
                }
                
                // Kısa bir gecikme ekle (çok sık çağrılmasını önlemek için)
                await Task.Delay(1000);
                
                var selectedPeriods = GetSelectedPeriods();
                if (selectedPeriods.Any())
                {
                    LogMessage($"Seçili dönem sayısı: {selectedPeriods.Count}");
                    
                    // Seçili dönemler için SMS alıcılarını güncelle
                    await LoadSmsRecipientsForSelectedPeriods();
                }
                else
                {
                    LogMessage("Hiç dönem seçilmemiş.");
                    
                    // Dönem seçili değilse alıcı listesini ve cache'i temizle
                    await Dispatcher.InvokeAsync(() =>
                    {
                        _recipientsCollection.Clear();
                        lstSmsRecipients.ItemsSource = null;
                        UpdateRecipientCount();
                    });
                    
                    _recipientsCache.Clear();
                    LogMessage("Dönem seçimi kaldırıldı, cache ve collection temizlendi.");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Dönem seçimi değişikliği işlenirken hata: {ex.Message}");
            }
        }

        /// <summary>
        /// SMS alıcı sayısını günceller ve yenile butonunu kontrol eder
        /// </summary>
        private void UpdateRecipientCount()
        {
            try
            {
                if (_recipientsCollection != null && _recipientsCollection.Any())
                {
                    var selectedCount = _recipientsCollection.Count(r => r.IsSelected);
                    var duplicateCount = _recipientsCollection.Count(r => r.IsDuplicate);
                    var uniqueCount = _recipientsCollection.Count(r => !r.IsDuplicate);
                    
                    var countText = $"Seçili: {selectedCount} / Toplam: {_recipientsCollection.Count}";
                    if (duplicateCount > 0)
                    {
                        countText += $" (Tekrar: {duplicateCount})";
                    }
                    
                    txtRecipientCount.Text = countText;
                    LogMessage($"Alıcı sayısı güncellendi: {selectedCount}/{_recipientsCollection.Count} (Tekrar eden: {duplicateCount})");
                    
                    // Tab badge'lerini güncelle
                    if (badgeAllRecipients != null)
                    {
                        badgeAllRecipients.Text = (_recipientsCollection.Count).ToString();
                    }
                    if (badgeDuplicateRecipients != null)
                    {
                        badgeDuplicateRecipients.Text = duplicateCount.ToString();
                    }
                    
                    // SMS alıcısı varsa yenile butonunu aktif et
                    btnRefreshRecipients.IsEnabled = true;
                    btnRefreshRecipients.Content = "🔄 Yenile";
                    
                    // Update placeholder visibility
                    UpdateListPlaceholders();
                }
                else
                {
                    txtRecipientCount.Text = "Seçili: 0 / Toplam: 0";
                    if (badgeAllRecipients != null) badgeAllRecipients.Text = "0";
                    if (badgeDuplicateRecipients != null) badgeDuplicateRecipients.Text = "0";
                    
                    btnRefreshRecipients.IsEnabled = false;
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Alıcı sayısı güncellenirken hata: {ex.Message}");
            }
        }

        /// <summary>
        /// Tüm SMS alıcılarını seç
        /// </summary>
        private void chkSelectAllRecipients_Checked(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_recipientsCollection != null)
                {
                    foreach (var recipient in _recipientsCollection)
                    {
                        recipient.IsSelected = true;
                    }
                    UpdateRecipientCount();
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Tüm alıcıları seçerken hata: {ex.Message}");
            }
        }

        /// <summary>
        /// Tüm SMS alıcılarının seçimini kaldır
        /// </summary>
        private void chkSelectAllRecipients_Unchecked(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_recipientsCollection != null)
                {
                    foreach (var recipient in _recipientsCollection)
                    {
                        recipient.IsSelected = false;
                    }
                    UpdateRecipientCount();
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Tüm alıcıların seçimini kaldırırken hata: {ex.Message}");
            }
        }

        /// <summary>
        /// Liste placeholder'larının görünürlüğünü günceller
        /// </summary>
        private void UpdateListPlaceholders()
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    // Main recipients list placeholder
                    if (lstSmsRecipients.Template.FindName("EmptyStateBorder", lstSmsRecipients) is Border emptyStateBorder)
                    {
                        emptyStateBorder.Visibility = _recipientsCollection.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                    }
                    
                    // Duplicate recipients list placeholder
                    if (lstDuplicateRecipients.Template.FindName("EmptyStateBorder", lstDuplicateRecipients) is Border duplicateEmptyStateBorder)
                    {
                        duplicateEmptyStateBorder.Visibility = _duplicateRecipientsCollection.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                    }
                });
            }
            catch (Exception ex)
            {
                LogMessage($"Placeholder güncelleme hatası: {ex.Message}");
            }
        }

        /// <summary>
        /// Tüm dönemleri otomatik seç ve SMS alıcılarını yükle
        /// </summary>
        private async Task AutoSelectAllPeriodsAndLoadRecipients()
        {
            try
            {
                LogMessage("Tüm dönemler otomatik olarak seçiliyor...");
                
                // Tüm dönemleri seç (zaten filtrelenmiş)
                LogMessage($"{_availablePeriods.Count} dönem seçiliyor (hepsi onay sayısı > 0)...");
                
                foreach (var period in _availablePeriods)
                {
                    period.IsSelected = true;
                }
                
                // UI'ı güncelle
                RefreshPeriodList();
                
                LogMessage($"{_availablePeriods.Count} dönem seçildi. SIRALI olarak SMS alıcıları alınıyor...");
                
                // Aynı yapıyı kullan - LoadSmsRecipientsForSelectedPeriods ile aynı
                if (_availablePeriods.Any() && _smsService != null)
                {
                    btnRefreshRecipients.IsEnabled = false;
                    btnRefreshRecipients.Content = "Sıralı Yükleniyor...";
                    
                    var startTime = DateTime.Now;
                    
                    // Seçili dönemler için SMS alıcılarını yükle (aynı yapı)
                    await LoadSmsRecipientsForSelectedPeriods();
                    
                    var elapsed = DateTime.Now - startTime;
                    
                    LogMessage($"✅ SIRALI İŞLEM TAMAMLANDI! {_recipientsCollection.Count} alıcı {elapsed.TotalSeconds:F1} saniyede yüklendi.");
                    
                    // Yenile butonunu güncelle (UpdateRecipientCount içinde kontrol edilir)
                    UpdateRecipientCount();
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Otomatik seçim ve yükleme hatası: {ex.Message}");
                // Yenile butonunu güncelle (UpdateRecipientCount içinde kontrol edilir)
                UpdateRecipientCount();
            }
        }

        /// <summary>
        /// Otomatik olarak SMS alıcılarını yükle
        /// </summary>
        private async Task AutoLoadRecipients()
        {
            try
            {
                // Eğer zaten yükleme yapılıyorsa, çık
                if (_isLoadingRecipients)
                {
                    return;
                }
                
                var selectedPeriods = GetSelectedPeriods();
                if (selectedPeriods.Any() && !_recipientsCollection.Any())
                {
                    LogMessage("DEBUG: AutoLoadRecipients - Seçili dönemler var ama alıcı listesi boş, yükleniyor...");
                    await LoadSmsRecipientsForSelectedPeriods();
                }
            }
            catch (Exception ex)
            {
                LogMessage($"AutoLoadRecipients hatası: {ex.Message}");
            }
        }

        /// <summary>
        /// SMS alıcılarını yenile
        /// </summary>
        private async void btnRefreshRecipients_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                LogMessage("DEBUG: Yenile butonuna tıklandı!");
                LogMessage("SMS alıcıları yenileniyor...");
                
                // Cache'i temizle
                _recipientsCache.Clear();
                LogMessage("Alıcı cache'i temizlendi.");
                
                await LoadSmsRecipientsForSelectedPeriods();
            }
            catch (Exception ex)
            {
                LogMessage($"SMS alıcıları yenilenirken hata: {ex.Message}");
            }
        }

        /// <summary>
        /// "Bugün Yine Gönder" switch'i açıldığında çağrılır
        /// </summary>
        private void toggleResendToday_Checked(object sender, RoutedEventArgs e)
        {
            try
            {
                LogMessage("🔄 Bugün Yine Gönder modu etkinleştirildi. Bugün gönderilmiş dönemler seçilebilir.");
                
                // Tüm checkbox'ları aktif hale getir
                var periodCheckboxes = PeriodSelectionPanel.Children
                    .OfType<Grid>()
                    .SelectMany(grid => grid.Children.OfType<CheckBox>());
                
                foreach (var checkbox in periodCheckboxes)
                {
                    // Eğer checkbox disabled ise ve seçili ise, seçimini kaldır
                    if (!checkbox.IsEnabled && checkbox.IsChecked == true)
                    {
                        checkbox.IsChecked = false;
                    }
                    
                    checkbox.IsEnabled = true;
                }
                
                // "Tüm dönemleri seç" checkbox'ını da aktif hale getir
                chkSelectAll.IsEnabled = true;
                
                // "Tüm dönemleri seç" checkbox'ının seçimini de kontrol et
                if (chkSelectAll.IsChecked == true)
                {
                    // Tüm dönemleri yeniden seç (disabled olanlar dahil)
                    foreach (var checkbox in periodCheckboxes)
                    {
                        checkbox.IsChecked = true;
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Switch etkinleştirme hatası: {ex.Message}");
            }
        }

        /// <summary>
        /// "Bugün Yine Gönder" switch'i kapatıldığında çağrılır
        /// </summary>
        private void toggleResendToday_Unchecked(object sender, RoutedEventArgs e)
        {
            try
            {
                LogMessage("🔄 Bugün Yine Gönder modu devre dışı bırakıldı. Bugün gönderilmiş dönemler seçilemez.");
                
                // Bugün gönderilmiş dönemlerin checkbox'larını devre dışı bırak
                var periodCheckboxes = PeriodSelectionPanel.Children
                    .OfType<Grid>()
                    .SelectMany(grid => grid.Children.OfType<CheckBox>());
                
                foreach (var checkbox in periodCheckboxes)
                {
                    if (checkbox.Tag is PeriodInfo period)
                    {
                        var sentToday = WasSentToday(period.Id);
                        checkbox.IsEnabled = !sentToday; // Bugün gönderilmişse devre dışı bırak
                    }
                }
                
                // "Tüm dönemleri seç" checkbox'ını da kontrol et
                var hasAnyEnabledPeriods = _availablePeriods.Any(p => !WasSentToday(p.Id));
                chkSelectAll.IsEnabled = hasAnyEnabledPeriods;
                
                // Eğer hiç aktif dönem yoksa "Tüm dönemleri seç" checkbox'ını da devre dışı bırak
                if (!hasAnyEnabledPeriods)
                {
                    chkSelectAll.IsChecked = false;
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Switch devre dışı bırakma hatası: {ex.Message}");
            }
        }

        #region SMS Geçmişi Metodları

        /// <summary>
        /// SMS geçmişini yükler ve UI'ı günceller
        /// </summary>
        private void LoadSmsHistory()
        {
            try
            {
                // DataGrid'e kaynak ata
                dgSmsHistory.ItemsSource = _smsHistoryService.SmsHistory;
                
                // İstatistikleri güncelle
                UpdateSmsStatistics();
                
                LogMessage("SMS geçmişi yüklendi.");
            }
            catch (Exception ex)
            {
                LogMessage($"SMS geçmişi yüklenirken hata: {ex.Message}");
            }
        }

        /// <summary>
        /// SMS istatistiklerini günceller
        /// </summary>
        private void UpdateSmsStatistics()
        {
            try
            {
                txtTodayCount.Text = _smsHistoryService.GetTodaySmsCount().ToString();
                txtWeekCount.Text = _smsHistoryService.GetThisWeekSmsCount().ToString();
                txtMonthCount.Text = _smsHistoryService.GetThisMonthSmsCount().ToString();
                txtTotalCount.Text = _smsHistoryService.SmsHistory.Count.ToString();
            }
            catch (Exception ex)
            {
                LogMessage($"SMS istatistikleri güncellenirken hata: {ex.Message}");
            }
        }

        /// <summary>
        /// Arama filtresi değiştiğinde çalışır
        /// </summary>
        private void txtSearchFilter_TextChanged(object sender, TextChangedEventArgs e)
        {
            try
            {
                var filterText = txtSearchFilter.Text.ToLower();
                
                if (string.IsNullOrEmpty(filterText))
                {
                    // Filtre boşsa tüm kayıtları göster
                    dgSmsHistory.ItemsSource = _smsHistoryService.SmsHistory;
                }
                else
                {
                    // Filtreleme yap
                    var filteredItems = _smsHistoryService.SmsHistory
                        .Where(item => 
                            item.RecipientName.ToLower().Contains(filterText) ||
                            item.PhoneNumber.ToLower().Contains(filterText) ||
                            item.PeriodName.ToLower().Contains(filterText) ||
                            item.Status.ToLower().Contains(filterText))
                        .ToList();
                    
                    dgSmsHistory.ItemsSource = filteredItems;
                }
            }
            catch (Exception ex)
            {
                LogMessage($"SMS geçmişi filtrelenirken hata: {ex.Message}");
            }
        }

        /// <summary>
        /// Filtre temizleme butonu
        /// </summary>
        private void btnClearFilter_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                txtSearchFilter.Text = string.Empty;
                dgSmsHistory.ItemsSource = _smsHistoryService.SmsHistory;
                LogMessage("SMS geçmişi filtresi temizlendi.");
            }
            catch (Exception ex)
            {
                LogMessage($"Filtre temizlenirken hata: {ex.Message}");
            }
        }

        /// <summary>
        /// Geçmişi temizleme butonu
        /// </summary>
        private async void btnClearHistory_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var result = System.Windows.MessageBox.Show(
                    "Tüm SMS geçmişini silmek istediğinizden emin misiniz?\n\nBu işlem geri alınamaz!",
                    "Geçmişi Temizle",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                
                if (result == MessageBoxResult.Yes)
                {
                    await _smsHistoryService.ClearHistoryAsync();
                    UpdateSmsStatistics();
                    LogMessage("SMS geçmişi temizlendi.");
                    
                    System.Windows.MessageBox.Show(
                        "SMS geçmişi başarıyla temizlendi.",
                        "Başarılı",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                LogMessage($"SMS geçmişi temizlenirken hata: {ex.Message}");
                System.Windows.MessageBox.Show(
                    $"SMS geçmişi temizlenirken hata oluştu: {ex.Message}",
                    "Hata",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Excel export butonu
        /// </summary>
        private async void btnExportExcel_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // SMS geçmişini al
                var smsHistory = _smsHistoryService.SmsHistory.ToList();
                
                if (!smsHistory.Any())
                {
                    System.Windows.MessageBox.Show("Dışa aktarılacak SMS geçmişi bulunamadı.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Dosya kaydetme dialog'u
                var saveFileDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "Excel Dosyası (*.xlsx)|*.xlsx",
                    FileName = $"SMS_Geçmişi_{DateTime.Now:yyyy-MM-dd_HH-mm}.xlsx",
                    Title = "SMS Geçmişini Excel'e Aktar"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    // Loading overlay'i göster
                    LoadingOverlay.Visibility = Visibility.Visible;
                    LoadingText.Text = "Excel dosyası oluşturuluyor...";
                    
                    await Task.Run(() =>
                    {
                        try
                        {
                            using (var package = new OfficeOpenXml.ExcelPackage())
                            {
                                var worksheet = package.Workbook.Worksheets.Add("SMS Geçmişi");

                                // Başlık satırı
                                worksheet.Cells[1, 1].Value = "Alıcı Adı";
                                worksheet.Cells[1, 2].Value = "Telefon";
                                worksheet.Cells[1, 3].Value = "Dönem";
                                worksheet.Cells[1, 4].Value = "Gönderim Zamanı";
                                worksheet.Cells[1, 5].Value = "Durum";

                                // Başlık stilini ayarla
                                using (var range = worksheet.Cells[1, 1, 1, 5])
                                {
                                    range.Style.Font.Bold = true;
                                    range.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                                    range.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.FromArgb(76, 175, 80));
                                    range.Style.Font.Color.SetColor(System.Drawing.Color.White);
                                    range.Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Center;
                                }

                                // Verileri ekle
                                for (int i = 0; i < smsHistory.Count; i++)
                                {
                                    var record = smsHistory[i];
                                    worksheet.Cells[i + 2, 1].Value = record.RecipientName;
                                    worksheet.Cells[i + 2, 2].Value = record.PhoneNumber;
                                    worksheet.Cells[i + 2, 3].Value = record.PeriodName;
                                    worksheet.Cells[i + 2, 4].Value = record.SentTime.ToString("dd.MM.yyyy HH:mm");
                                    worksheet.Cells[i + 2, 5].Value = record.Status;
                                }

                                // Sütun genişliklerini otomatik ayarla
                                worksheet.Cells[worksheet.Dimension.Address].AutoFitColumns();

                                // Dosyayı kaydet
                                package.SaveAs(new FileInfo(saveFileDialog.FileName));
                            }

                            Dispatcher.Invoke(() =>
                            {
                                System.Windows.MessageBox.Show($"SMS geçmişi başarıyla Excel dosyasına aktarıldı!\nDosya: {saveFileDialog.FileName}", 
                                    "Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
                            });
                        }
                        catch (Exception ex)
                        {
                            Dispatcher.Invoke(() =>
                            {
                                System.Windows.MessageBox.Show($"Excel dosyası oluşturulurken hata oluştu: {ex.Message}", 
                                    "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                            });
                        }
                    });

                    // Loading overlay'i gizle
                    LoadingOverlay.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Excel export işlemi sırasında hata oluştu: {ex.Message}", 
                    "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                LoadingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        #endregion

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

        private void RecipientName_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                // TextBlock'a tıklanınca ilgili alıcının seçim durumunu değiştir
                var textBlock = sender as TextBlock;
                if (textBlock != null && textBlock.DataContext is SmsRecipientInfo recipient)
                {
                    // CheckBox'ın seçim durumunu toggle et
                    recipient.IsSelected = !recipient.IsSelected;
                    e.Handled = true; // Event'i işlendi olarak işaretle
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Alıcı adı tıklama hatası: {ex.Message}");
            }
        }

        #region Progress Bar Methods

        /// <summary>
        /// Progress bar'ı gösterir ve başlangıç değerlerini ayarlar
        /// </summary>
        private void ShowProgressBar(int totalItems)
        {
            try
            {
                // Reset progress tracking
                _totalItems = totalItems;
                _completedItems = 0;
                _errorCount = 0;
                _timeoutCount = 0;
                _errorMessages.Clear();
                
                Dispatcher.Invoke(() =>
                {
                    if (ProgressContainer != null)
                    {
                        ProgressContainer.Visibility = Visibility.Visible;
                        if (ProgressText != null) ProgressText.Text = $"0 / {totalItems} tamamlandı";
                        if (ProgressPercentage != null) ProgressPercentage.Text = "0%";
                        if (ProgressFill != null) ProgressFill.Width = 0;
                    }
                    
                    // Hide error container initially
                    if (ErrorContainer != null)
                    {
                        ErrorContainer.Visibility = Visibility.Collapsed;
                    }
                    
                    LogMessage($"Progress bar gösteriliyor: {totalItems} öğe");
                });
            }
            catch (Exception ex)
            {
                LogMessage($"Progress bar gösterme hatası: {ex.Message}");
            }
        }

        /// <summary>
        /// Progress bar'ı günceller
        /// </summary>
        private void UpdateProgressBar(int completedItems, int totalItems)
        {
            try
            {
                _completedItems = completedItems;
                _totalItems = totalItems;
                
                UpdateProgressBarWithErrors();
                LogMessage($"Progress güncellendi: {completedItems}/{totalItems}");
            }
            catch (Exception ex)
            {
                LogMessage($"Progress bar güncelleme hatası: {ex.Message}");
            }
        }

        /// <summary>
        /// Progress bar'ı gizler
        /// </summary>
        private void HideProgressBar()
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    if (ProgressContainer != null)
                    {
                        ProgressContainer.Visibility = Visibility.Collapsed;
                    }
                    if (ErrorContainer != null)
                    {
                        ErrorContainer.Visibility = Visibility.Collapsed;
                    }
                    LogMessage("Progress bar gizlendi");
                });
            }
            catch (Exception ex)
            {
                LogMessage($"Progress bar gizleme hatası: {ex.Message}");
            }
        }

        /// <summary>
        /// Hata ekler ve progress bar'ı günceller
        /// </summary>
        private void AddError(string errorMessage, bool isTimeout = false)
        {
            try
            {
                _errorCount++;
                if (isTimeout)
                {
                    _timeoutCount++;
                }
                
                // Add error message (keep last 5 errors)
                _errorMessages.Add(errorMessage);
                if (_errorMessages.Count > 5)
                {
                    _errorMessages.RemoveAt(0);
                }
                
                // Update progress bar with error info
                UpdateProgressBarWithErrors();
                
                LogMessage($"Hata eklendi: {errorMessage}");
            }
            catch (Exception ex)
            {
                LogMessage($"Hata ekleme hatası: {ex.Message}");
            }
        }

        /// <summary>
        /// Progress bar'ı hata bilgileriyle günceller
        /// </summary>
        private void UpdateProgressBarWithErrors()
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    if (ProgressContainer != null && ProgressContainer.Visibility == Visibility.Visible)
                    {
                        var percentage = _totalItems > 0 ? (double)_completedItems / _totalItems * 100 : 0;
                        var progressWidth = _totalItems > 0 ? (double)_completedItems / _totalItems * 300 : 0;
                        
                        if (ProgressText != null) ProgressText.Text = $"{_completedItems} / {_totalItems} tamamlandı";
                        if (ProgressPercentage != null) ProgressPercentage.Text = $"{percentage:F0}%";
                        if (ProgressFill != null) ProgressFill.Width = progressWidth;
                    }
                    
                    // Show error container if there are errors
                    if (_errorCount > 0 && ErrorContainer != null)
                    {
                        ErrorContainer.Visibility = Visibility.Visible;
                        
                        if (ErrorSummary != null)
                        {
                            ErrorSummary.Text = $"Hatalar: {_errorCount}";
                        }
                        
                        if (ErrorDetails != null)
                        {
                            ErrorDetails.Text = string.Join("\n", _errorMessages);
                        }
                        
                        if (TimeoutInfo != null && _timeoutCount > 0)
                        {
                            TimeoutInfo.Text = $"Timeout: {_timeoutCount}";
                            if (TimeoutInfo.Parent is Border timeoutBorder)
                            {
                                timeoutBorder.Visibility = Visibility.Visible;
                            }
                        }
                        else if (TimeoutInfo != null)
                        {
                            if (TimeoutInfo.Parent is Border timeoutBorder)
                            {
                                timeoutBorder.Visibility = Visibility.Collapsed;
                            }
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                LogMessage($"Progress bar hata güncelleme hatası: {ex.Message}");
            }
        }

        #endregion

        #region Duplicate Recipients Methods

        /// <summary>
        /// Tekrar eden alıcıları işaretler ve ayrı listeye ekler
        /// </summary>
        private void ProcessDuplicateRecipients(List<SmsRecipientInfo> allRecipients)
        {
            try
            {
                // Tekrar eden alıcıları bul
                var duplicates = allRecipients
                    .GroupBy(r => new { r.Name, r.Phone })
                    .Where(g => g.Count() > 1)
                    .SelectMany(g => g)
                    .ToList();

                // Tekrar eden alıcıları işaretle
                foreach (var duplicate in duplicates)
                {
                    duplicate.IsDuplicate = true;
                }

                // Tekrar eden alıcıları ayrı listeye ekle
                _duplicateRecipientsCollection.Clear();
                foreach (var duplicate in duplicates)
                {
                    _duplicateRecipientsCollection.Add(duplicate);
                }

                // Ana listeden tekrar edenleri tamamen çıkar (sadece duplicate listesinde kalsın)
                try
                {
                    var duplicateKeys = new HashSet<string>(duplicates.Select(d => ($"{d.Name}" + "|" + $"{d.Phone}").ToLowerInvariant().Trim()));
                    var toRemove = _recipientsCollection.Where(r => duplicateKeys.Contains(($"{r.Name}" + "|" + $"{r.Phone}").ToLowerInvariant().Trim())).ToList();
                    foreach (var rem in toRemove)
                    {
                        _recipientsCollection.Remove(rem);
                    }
                }
                catch (Exception ex)
                {
                    LogMessage($"Ana listeden tekrar edenler çıkarılırken hata: {ex.Message}");
                }

                // ListView'ları güncelle
                Dispatcher.Invoke(() =>
                {
                    lstDuplicateRecipients.ItemsSource = _duplicateRecipientsCollection;
                    lstSmsRecipients.ItemsSource = _recipientsCollection;
                    UpdateDuplicateCount();
                    UpdateRecipientCount();
                    UpdateListPlaceholders();
                });

                LogMessage($"Tekrar eden alıcılar işlendi: {duplicates.Count} alıcı (ana listeden ayrıldı)");
            }
            catch (Exception ex)
            {
                LogMessage($"Tekrar eden alıcılar işlenirken hata: {ex.Message}");
            }
        }

        /// <summary>
        /// Tekrar eden alıcı sayısını günceller
        /// </summary>
        private void UpdateDuplicateCount()
        {
            try
            {
                var selectedCount = _duplicateRecipientsCollection.Count(r => r.IsSelected);
                var totalCount = _duplicateRecipientsCollection.Count;
                txtDuplicateCount.Text = $"Seçili: {selectedCount} / Toplam: {totalCount}";
            }
            catch (Exception ex)
            {
                LogMessage($"Tekrar eden alıcı sayısı güncellenirken hata: {ex.Message}");
            }
        }

        #endregion

        #region Duplicate Recipients Event Handlers

        /// <summary>
        /// Tekrar eden alıcılar için tümünü seç
        /// </summary>
        private void chkSelectAllDuplicates_Checked(object sender, RoutedEventArgs e)
        {
            try
            {
                foreach (var recipient in _duplicateRecipientsCollection)
                {
                    recipient.IsSelected = true;
                }
                UpdateDuplicateCount();
            }
            catch (Exception ex)
            {
                LogMessage($"Tekrar eden alıcılar seçilirken hata: {ex.Message}");
            }
        }

        /// <summary>
        /// Tekrar eden alıcılar için tümünü seçme işlemini kaldır
        /// </summary>
        private void chkSelectAllDuplicates_Unchecked(object sender, RoutedEventArgs e)
        {
            try
            {
                foreach (var recipient in _duplicateRecipientsCollection)
                {
                    recipient.IsSelected = false;
                }
                UpdateDuplicateCount();
            }
            catch (Exception ex)
            {
                LogMessage($"Tekrar eden alıcılar seçimi kaldırılırken hata: {ex.Message}");
            }
        }

        /// <summary>
        /// Tekrar eden alıcıları yenile
        /// </summary>
        private void btnRefreshDuplicates_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Mevcut alıcıları tekrar işle
                var allRecipients = _recipientsCollection.ToList();
                ProcessDuplicateRecipients(allRecipients);
                LogMessage("Tekrar eden alıcılar yenilendi.");
            }
            catch (Exception ex)
            {
                LogMessage($"Tekrar eden alıcılar yenilenirken hata: {ex.Message}");
            }
        }

        #endregion

        // Dönem filtresi değerlerini doldur ve bağla
        private void PopulatePeriodFilter(List<SmsRecipientInfo> recipients)
        {
            try
            {
                var periods = recipients
                    .SelectMany(r => (r.PeriodName ?? "").Split(new[] {','}, StringSplitOptions.RemoveEmptyEntries))
                    .Select(p => p.Trim())
                    .Where(p => !string.IsNullOrWhiteSpace(p))
                    .Distinct()
                    .OrderBy(p => p)
                    .ToList();
                
                Dispatcher.Invoke(() =>
                {
                    if (cmbPeriodFilter != null)
                    {
                        cmbPeriodFilter.Items.Clear();
                        cmbPeriodFilter.Items.Add("(Tümü)");
                        foreach (var p in periods)
                        {
                            cmbPeriodFilter.Items.Add(p);
                        }
                        cmbPeriodFilter.SelectedIndex = 0; // Tümü
                    }
                });
            }
            catch (Exception ex)
            {
                LogMessage($"Dönem filtresi yüklenirken hata: {ex.Message}");
            }
        }

        // Dönem filtresi değişince ortak filtre uygula (tek tanım)
        private void cmbPeriodFilter_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            try
            {
                _pendingRecipientFilterChange = true;
                LogMessage("Dönem filtresi değişti, uygulanmayı bekliyor. 'Filtreyi Uygula'ya basın.");
            }
            catch (Exception ex)
            {
                LogMessage($"Dönem filtresi işaretlenirken hata: {ex.Message}");
            }
        }


        // Ay filtresi dropdown'unu doldur
        private void PopulateMonthFilter(List<SmsRecipientInfo> recipients)
        {
            try
            {
                var months = recipients
                    .Select(r => ExtractMonthFromDateRange(r.PeriodName ?? string.Empty))
                    .Where(m => !string.IsNullOrWhiteSpace(m))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(m => MonthOrder(m))
                    .ToList();
                
                Dispatcher.Invoke(() =>
                {
                    if (cmbMonthFilter != null)
                    {
                        cmbMonthFilter.Items.Clear();
                        cmbMonthFilter.Items.Add("(Tümü)");
                        foreach (var m in months)
                        {
                            cmbMonthFilter.Items.Add(m);
                        }
                        cmbMonthFilter.SelectedIndex = 0;
                    }
                });
            }
            catch (Exception ex)
            {
                LogMessage($"Ay filtresi yüklenirken hata: {ex.Message}");
            }
        }

        // Ay filtresi değişince uygula (period filtresi ile birlikte AND ilişkisi)
        private void cmbMonthFilter_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            try
            {
                _pendingRecipientFilterChange = true;
                LogMessage("Ay filtresi değişti, uygulanmayı bekliyor. 'Filtreyi Uygula'ya basın.");
            }
            catch (Exception ex)
            {
                LogMessage($"Ay filtresi işaretlenirken hata: {ex.Message}");
            }
        }

        // Ortak filtre uygulayıcı (dönem + ay)
        private void ApplyRecipientFilters()
        {
            _pendingRecipientFilterChange = false;
            var periodSelected = cmbPeriodFilter?.SelectedItem?.ToString() ?? "(Tümü)";
            var monthSelected = cmbMonthFilter?.SelectedItem?.ToString() ?? "(Tümü)";
            
            IEnumerable<SmsRecipientInfo> source = _recipientsCollection;
            
            if (periodSelected != "(Tümü)")
            {
                source = source.Where(r => (r.PeriodName ?? string.Empty).Contains(periodSelected, StringComparison.OrdinalIgnoreCase));
            }
            
            if (monthSelected != "(Tümü)")
            {
                var norm = NormalizeMonth(monthSelected);
                source = source.Where(r => ExtractMonthFromDateRange(r.PeriodName ?? string.Empty).Equals(norm, StringComparison.OrdinalIgnoreCase));
            }
            
            var filtered = source.ToList();
            lstSmsRecipients.ItemsSource = filtered;
            
            // Filtre sonrası sayacı güncelle
            int selected = filtered.Count(r => r.IsSelected);
            int total = filtered.Count;
            txtRecipientCount.Text = $"Seçili: {selected} / Toplam: {total}";
            
            // Filtre uygulama butonundan çağrıldıysa limit de güncellenecek; bunu çağıran taraf set edecek
            UpdateRecipientCount();
        }


        // Yardımcı: Metinden ay adını çıkar (Türkçe aylar)
        private string ExtractMonthName(string text)
        {
            var months = new[] {"Ocak","Şubat","Mart","Nisan","Mayıs","Haziran","Temmuz","Ağustos","Eylül","Ekim","Kasım","Aralık",
                                "Oca","Şub","Mar","Nis","May","Haz","Tem","Ağu","Eyl","Eki","Kas","Ara"};
            foreach (var m in months)
            {
                if (text.IndexOf(m, StringComparison.OrdinalIgnoreCase) >= 0)
                    return NormalizeMonth(m);
            }
            return string.Empty;
        }
        
        private string NormalizeMonth(string m)
        {
            switch (m.ToLowerInvariant())
            {
                case "oca": case "ocak": return "Ocak";
                case "şub": case "şubat": return "Şubat";
                case "mar": case "mart": return "Mart";
                case "nis": case "nisan": return "Nisan";
                case "may": case "mayıs": return "Mayıs";
                case "haz": case "haziran": return "Haziran";
                case "tem": case "temmuz": return "Temmuz";
                case "ağu": case "ağustos": return "Ağustos";
                case "eyl": case "eylül": return "Eylül";
                case "eki": case "ekim": return "Ekim";
                case "kas": case "kasım": return "Kasım";
                case "ara": case "aralık": return "Aralık";
            }
            return m;
        }
        
        private int MonthOrder(string m)
        {
            var order = new[]{"Ocak","Şubat","Mart","Nisan","Mayıs","Haziran","Temmuz","Ağustos","Eylül","Ekim","Kasım","Aralık"};
            var idx = Array.IndexOf(order, m);
            return idx >= 0 ? idx : int.MaxValue;
        }

        private bool MonthMatches(string periodName, string month)
        {
            if (string.IsNullOrWhiteSpace(periodName)) return false;
            var tokens = periodName.Split(new[] {' ', '_', '-', '(', ')'}, StringSplitOptions.RemoveEmptyEntries)
                                    .Select(t => t.Trim())
                                    .ToList();
            foreach (var t in tokens)
            {
                var m = ExtractMonthName(t);
                if (!string.IsNullOrEmpty(m) && string.Equals(m, month, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            // Son çare: tüm metinde ara
            var m2 = ExtractMonthName(periodName);
            return !string.IsNullOrEmpty(m2) && string.Equals(m2, month, StringComparison.OrdinalIgnoreCase);
        }

        // Tarih aralığından ("1-15 Ağu 2025") ayı güvenli çıkar: ay mutlaka sayıdan SONRA gelmeli
        private string ExtractMonthFromDateRange(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;
            try
            {
                // Örnek eşleşmeler: "01-15 Ağu 2025", "1-30 Eyl 2025"
                var pattern = @"\b\d{1,2}\s*[-–]\s*\d{1,2}\s+(Ocak|Şubat|Mart|Nisan|Mayıs|Haziran|Temmuz|Ağustos|Eylül|Ekim|Kasım|Aralık|Oca|Şub|Mar|Nis|May|Haz|Tem|Ağu|Eyl|Eki|Kas|Ara)\b";
                var m = System.Text.RegularExpressions.Regex.Match(text, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (m.Success && m.Groups.Count > 1)
                {
                    return NormalizeMonth(m.Groups[1].Value);
                }
                return string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

    }

    /// <summary>
    /// Özel alert penceresi - başarı mesajları için
    /// </summary>
    public class CustomAlertWindow : Window
    {
        public CustomAlertWindow(string title, string message, string confirmText, string cancelText)
        {
            Title = title;
            Width = 500;
            Height = 400;
            WindowStartupLocation = WindowStartupLocation.Manual; // Elle konumlandırılacak
            ResizeMode = ResizeMode.NoResize;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = System.Windows.Media.Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            FontFamily = new System.Windows.Media.FontFamily("pack://application:,,,/Fonts/Poppins-Regular.ttf#Poppins");
            
            var mainBorder = new Border
            {
                CornerRadius = new CornerRadius(12),
                Background = System.Windows.Media.Brushes.White,
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(229, 231, 235)), // Gri border
                BorderThickness = new Thickness(1), // Gri border ekle
                Padding = new Thickness(0),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = System.Windows.Media.Colors.Black,
                    BlurRadius = 24,
                    Opacity = 0.25,
                    ShadowDepth = 0
                }
            };

            // Konumlandırma: Owner belirlendikten sonra merkezle
            Loaded += (s, e) =>
            {
                try
                {
                    var owner = this.Owner as Window ?? Application.Current?.MainWindow;
                    if (owner != null)
                    {
                        // Owner'ın ekran koordinatlarını kullanarak ortala
                        var ownerLeft = owner.Left;
                        var ownerTop = owner.Top;
                        var ownerWidth = owner.ActualWidth > 0 ? owner.ActualWidth : owner.Width;
                        var ownerHeight = owner.ActualHeight > 0 ? owner.ActualHeight : owner.Height;

                        this.Left = ownerLeft + (ownerWidth - this.Width) / 2;
                        this.Top = ownerTop + (ownerHeight - this.Height) / 2;
                    }
                    else
                    {
                        // Fallback: ekran ortası
                        var screenWidth = SystemParameters.PrimaryScreenWidth;
                        var screenHeight = SystemParameters.PrimaryScreenHeight;
                        this.Left = (screenWidth - this.Width) / 2;
                        this.Top = (screenHeight - this.Height) / 2;
                    }
                }
                catch { }
            };
            
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            
            // Başlık - Gradient arka plan ile (hareket ettirilebilir)
            var titleBorder = new Border
            {
                Background = new System.Windows.Media.LinearGradientBrush(
                    System.Windows.Media.Color.FromRgb(33, 150, 243),
                    System.Windows.Media.Color.FromRgb(25, 118, 210),
                    new System.Windows.Point(0, 0),
                    new System.Windows.Point(1, 1)
                ),
                CornerRadius = new CornerRadius(13, 13, 0, 0),
                Cursor = System.Windows.Input.Cursors.SizeAll
            };
            
            // Mouse event'leri ekle (hareket ettirme için)
            bool isDragging = false;
            System.Windows.Point startPoint = new System.Windows.Point();
            System.Windows.Point windowStartPoint = new System.Windows.Point();
            
            titleBorder.MouseLeftButtonDown += (s, e) =>
            {
                isDragging = true;
                startPoint = e.GetPosition(titleBorder);
                windowStartPoint = new System.Windows.Point(this.Left, this.Top);
                titleBorder.CaptureMouse();
            };
            
            titleBorder.MouseMove += (s, e) =>
            {
                if (isDragging)
                {
                    var currentPoint = e.GetPosition(titleBorder);
                    var offset = currentPoint - startPoint;
                    this.Left = windowStartPoint.X + offset.X;
                    this.Top = windowStartPoint.Y + offset.Y;
                }
            };
            
            titleBorder.MouseLeftButtonUp += (s, e) =>
            {
                isDragging = false;
                titleBorder.ReleaseMouseCapture();
            };
            
            var titleBlock = new TextBlock
            {
                Text = title,
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                FontFamily = new System.Windows.Media.FontFamily("pack://application:,,,/Fonts/Poppins-Bold.ttf#Poppins"),
                Foreground = System.Windows.Media.Brushes.White,
                Margin = new Thickness(25, 20, 25, 15),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            
            titleBorder.Child = titleBlock;
            Grid.SetRow(titleBorder, 0);
            grid.Children.Add(titleBorder);
            
            // Mesaj - ScrollViewer ile
            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(25, 20, 25, 20)
            };
            
            var messageBlock = new TextBlock
            {
                Text = message,
                FontSize = 14,
                FontFamily = new System.Windows.Media.FontFamily("pack://application:,,,/Fonts/Poppins-Regular.ttf#Poppins"),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 22,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(51, 51, 51))
            };
            
            scrollViewer.Content = messageBlock;
            Grid.SetRow(scrollViewer, 1);
            grid.Children.Add(scrollViewer);
            
            // Butonlar - Modern tasarım
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(25, 0, 25, 25)
            };
            
            // Onay Butonu
            var confirmButton = new Button
            {
                Content = confirmText,
                Width = 120,
                Height = 40,
                Margin = new Thickness(0, 0, 15, 0),
                FontFamily = new System.Windows.Media.FontFamily("pack://application:,,,/Fonts/Poppins-Bold.ttf#Poppins"),
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = System.Windows.Media.Brushes.White,
                Background = new System.Windows.Media.LinearGradientBrush(
                    System.Windows.Media.Color.FromRgb(76, 175, 80),
                    System.Windows.Media.Color.FromRgb(56, 142, 60),
                    new System.Windows.Point(0, 0),
                    new System.Windows.Point(1, 1)
                ),
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            
            confirmButton.Template = CreateButtonTemplate();
            confirmButton.Click += (s, e) => { DialogResult = true; Close(); };
            
            // İptal Butonu
            var cancelButton = new Button
            {
                Content = cancelText,
                Width = 120,
                Height = 40,
                FontFamily = new System.Windows.Media.FontFamily("pack://application:,,,/Fonts/Poppins-Bold.ttf#Poppins"),
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = System.Windows.Media.Brushes.White,
                Background = new System.Windows.Media.LinearGradientBrush(
                    System.Windows.Media.Color.FromRgb(244, 67, 54),
                    System.Windows.Media.Color.FromRgb(211, 47, 47),
                    new System.Windows.Point(0, 0),
                    new System.Windows.Point(1, 1)
                ),
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            
            cancelButton.Template = CreateButtonTemplate();
            cancelButton.Click += (s, e) => { DialogResult = false; Close(); };
            
            buttonPanel.Children.Add(confirmButton);
            buttonPanel.Children.Add(cancelButton);
            
            Grid.SetRow(buttonPanel, 2);
            grid.Children.Add(buttonPanel);
            
            mainBorder.Child = grid;
            Content = mainBorder;
        }
        
        private ControlTemplate CreateButtonTemplate()
        {
            var template = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Button.BorderThicknessProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Button.BorderBrushProperty));
            
            var contentPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
            contentPresenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            contentPresenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            contentPresenter.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(Button.ContentProperty));
            contentPresenter.SetValue(ContentPresenter.ContentTemplateProperty, new TemplateBindingExtension(Button.ContentTemplateProperty));
            
            border.AppendChild(contentPresenter);
            
            var trigger = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
            var setter = new Setter { Property = Border.OpacityProperty, Value = 0.8 };
            trigger.Setters.Add(setter);
            
            template.Triggers.Add(trigger);
            template.VisualTree = border;
            
            return template;
        }



    }

    /// <summary>
    /// ListView item'larının index'ini döndüren converter
    /// </summary>
    public class IndexConverter : IValueConverter
    {
        public static readonly IndexConverter Instance = new IndexConverter();

        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is ListViewItem item)
            {
                var listView = ItemsControl.ItemsControlFromItemContainer(item) as ListView;
                if (listView != null)
                {
                    var index = listView.ItemContainerGenerator.IndexFromContainer(item);
                    return (index + 1).ToString();
                }
            }
            return "0";
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }



} 