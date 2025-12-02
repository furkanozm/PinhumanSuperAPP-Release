using System; using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Threading.Tasks;
using System.Threading;
using CheckBox = System.Windows.Controls.CheckBox;
using Orientation = System.Windows.Controls.Orientation;

namespace WebScraper
{
    public partial class ManualSendModal : Window
    {
        private List<KeywordNotification> _keywords;
        private Dictionary<string, List<string>> _keywordFileMappings;
        private Dictionary<string, CheckBox> _keywordCheckBoxes;
        private string _selectedFolderPath;
        private List<KeywordNotification> _allKeywords = new List<KeywordNotification>(); // Tüm keyword'ler
        private List<KeywordNotification> _filteredKeywords = new List<KeywordNotification>(); // Filtrelenmiş keyword'ler

        private string _selectedPaymentOrderCreator = ""; // Seçili ödeme emri oluşturan
        
        // SMS Alıcıları için değişkenler
        private SmsService _smsService;
        private List<SmsRecipientInfo> _allSmsRecipients = new List<SmsRecipientInfo>();
        private List<SmsRecipientInfo> _uniqueSmsRecipients = new List<SmsRecipientInfo>();
        private List<SmsRecipientInfo> _duplicateSmsRecipients = new List<SmsRecipientInfo>();
        
        // Mail geçmişi için değişkenler
        private MailHistoryService _mailHistoryService;
        private Dictionary<string, bool> _sentFilesCache = new Dictionary<string, bool>();

        // Log mesajları için event handler
        private void OnLogMessage(string message)
        {
            Dispatcher.Invoke(() =>
            {
                var timestamp = DateTime.Now.ToString("HH:mm:ss");
                txtLog.Text += $"[{timestamp}] {message}\n";
                
                // ScrollViewer'ı en alta kaydır
                var scrollViewer = txtLog.Parent as ScrollViewer;
                if (scrollViewer != null)
                {
                    scrollViewer.ScrollToBottom();
                }
                
                // Log istatistiklerini güncelle
                UpdateLogStatistics();
            });
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
                    
                    var errorCount = lines.Count(line => line.Contains("❌") || line.ToLower().Contains("hata"));
                    var warningCount = lines.Count(line => line.Contains("⚠") || line.ToLower().Contains("uyarı"));
                    var successCount = lines.Count(line => line.Contains("✅") || line.ToLower().Contains("başarı"));
                    
                    if (txtLogLineCount != null) txtLogLineCount.Text = $"{lineCount} satır";
                    if (txtLogErrorCount != null) txtLogErrorCount.Text = $"{errorCount} hata";
                    if (txtLogWarningCount != null) txtLogWarningCount.Text = $"{warningCount} uyarı";
                    if (txtLogSuccessCount != null) txtLogSuccessCount.Text = $"{successCount} başarı";
                }
            }
            catch (Exception ex)
            {
                // Hata durumunda sessizce devam et
            }
        }

        public ManualSendModal()
        {
            InitializeComponent();
            _keywordFileMappings = new Dictionary<string, List<string>>();
            _keywordCheckBoxes = new Dictionary<string, CheckBox>();
            
            // SMS Service'i başlat
            var config = ConfigManager.LoadConfig();
            _smsService = new SmsService("https://pinhuman.net");
            _smsService.LogMessage += (sender, e) => OnLogMessage(e.Message);
            
            // Mail History Service'i başlat
            _mailHistoryService = new MailHistoryService();
            
            // Config'den keyword'leri yükle
            LoadKeywords();
            
            // Varsayılan klasör yolunu ayarla
            if (!string.IsNullOrEmpty(config.Download.OutputFolder) && Directory.Exists(config.Download.OutputFolder))
            {
                _selectedFolderPath = config.Download.OutputFolder;
                txtFolderPath.Text = _selectedFolderPath;
            }
            else
            {
                // Varsayılan olarak dist/cikti klasörünü kullan
                var defaultPath = Path.Combine(Directory.GetCurrentDirectory(), "dist", "cikti");
                if (Directory.Exists(defaultPath))
                {
                    _selectedFolderPath = defaultPath;
                    txtFolderPath.Text = _selectedFolderPath;
                }
                else
                {
                    // dist/cikti yoksa sadece dist klasörünü dene
                    var distPath = Path.Combine(Directory.GetCurrentDirectory(), "dist");
                    if (Directory.Exists(distPath))
                    {
                        _selectedFolderPath = distPath;
                        txtFolderPath.Text = _selectedFolderPath;
                    }
                }
            }
            
            // Varsayılan tarihi ayarla (bugün)
            dpSelectedDate.SelectedDate = DateTime.Today;
            
            // Buton metnini ayarla
            btnSendMails.Content = "📧 Mail Gönder";
            
            // Ödeme emri oluşturan dropdown'ını doldur
            LoadPaymentOrderCreators();
        }

        private void LoadPaymentOrderCreators()
        {
            try
            {
                cmbPaymentOrderCreator.Items.Clear();
                cmbPaymentOrderCreator.Items.Add("Tümü"); // Varsayılan seçenek
                
                OnLogMessage($"Dropdown dolduruluyor... Klasör yolu: {_selectedFolderPath}");
                
                if (!string.IsNullOrEmpty(_selectedFolderPath) && Directory.Exists(_selectedFolderPath))
                {
                    var directories = Directory.GetDirectories(_selectedFolderPath);
                    OnLogMessage($"Bulunan klasör sayısı: {directories.Length}");
                    
                    var creators = new HashSet<string>();
                    
                    foreach (var dir in directories)
                    {
                        var dirName = Path.GetFileName(dir);
                        OnLogMessage($"İşlenen klasör: {dirName}");
                        
                        if (!string.IsNullOrEmpty(dirName))
                        {
                            // Klasör isminden ödeme emri oluşturanı çıkar
                            // Örnek: "28.08.2025 Furkan_ÖZMEN_51856069550" -> "Furkan_ÖZMEN_51856069550"
                            var parts = dirName.Split(' ', 2);
                            if (parts.Length > 1)
                            {
                                creators.Add(parts[1]);
                                OnLogMessage($"Ödeme emri oluşturan eklendi: {parts[1]}");
                            }
                            else
                            {
                                OnLogMessage($"Klasör ismi parçalanamadı: {dirName}");
                            }
                        }
                    }
                    
                    OnLogMessage($"Toplam ödeme emri oluşturan sayısı: {creators.Count}");
                    
                    foreach (var creator in creators.OrderBy(c => c))
                    {
                        cmbPaymentOrderCreator.Items.Add(creator);
                        OnLogMessage($"Dropdown'a eklendi: {creator}");
                    }
                }
                else
                {
                    OnLogMessage($"Klasör yolu geçersiz veya klasör bulunamadı: {_selectedFolderPath}");
                }
                
                cmbPaymentOrderCreator.SelectedIndex = 0; // İlk öğeyi seç
                OnLogMessage($"Dropdown doldurma tamamlandı. Toplam öğe: {cmbPaymentOrderCreator.Items.Count}");
            }
            catch (Exception ex)
            {
                OnLogMessage($"Ödeme emri oluşturan listesi yüklenirken hata: {ex.Message}");
            }
        }

        private void LoadKeywords()
        {
            try
            {
                var config = ConfigManager.LoadConfig();
                _allKeywords = config.Notification.Keywords.Where(k => k.Enabled).ToList();
                _filteredKeywords = new List<KeywordNotification>(_allKeywords);
                _keywords = _filteredKeywords;
                
                // Keyword seçim panelini oluştur
                CreateKeywordSelectionPanel();
                
                // Mail geçmişini yükle ve dosya gönderim durumlarını güncelle
                UpdateKeywordSelectionPanelWithSentStatus();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Keyword'ler yüklenirken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CreateKeywordSelectionPanel()
        {
            spKeywordSelection.Children.Clear();
            _keywordCheckBoxes.Clear();

            foreach (var keyword in _keywords)
            {
                var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };

                var checkBox = new CheckBox
                {
                    IsChecked = false, // Varsayılan olarak seçili değil
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Cursor = System.Windows.Input.Cursors.Hand
                };

                var textBlock = new TextBlock
                {
                    Text = $"{keyword.Keyword} → {keyword.EmailRecipient}",
                    FontSize = 13,
                    FontFamily = new System.Windows.Media.FontFamily("pack://application:,,,/Fonts/Poppins-Regular.ttf#Poppins"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0)
                };

                // Checkbox değişiklik event'ini ekle
                checkBox.Checked += (s, e) => OnKeywordSelectionChanged(keyword.Keyword, true);
                checkBox.Unchecked += (s, e) => OnKeywordSelectionChanged(keyword.Keyword, false);

                // Dosya sayısını ve gönderim durumunu hesapla
                var fileCount = 0;
                var hasFiles = false;
                var sentFileCount = 0;
                var hasSentFiles = false;
                
                if (_keywordFileMappings.ContainsKey(keyword.Keyword))
                {
                    var files = _keywordFileMappings[keyword.Keyword];
                    fileCount = files.Count;
                    hasFiles = fileCount > 0;
                    
                    // Daha önce gönderilmiş dosyaları say
                    foreach (var file in files)
                    {
                        if (IsFilePreviouslySent(file))
                        {
                            sentFileCount++;
                            hasSentFiles = true;
                        }
                    }
                }

                // Dosya sayısı label'ı
                var fileCountLabel = new Border
                {
                    Background = hasFiles ? 
                        new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(76, 175, 80)) : // Yeşil - dosya var
                        new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(158, 158, 158)), // Gri - dosya yok
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(6, 2, 6, 2),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 5, 0),
                    Child = new TextBlock
                    {
                        Text = $"{fileCount} dosya",
                        FontSize = 11,
                        Foreground = System.Windows.Media.Brushes.White,
                        FontFamily = new System.Windows.Media.FontFamily("pack://application:,,,/Fonts/Poppins-Regular.ttf#Poppins"),
                        FontWeight = FontWeights.Medium
                    }
                };

                // Gönderilmiş dosya sayısı label'ı (eğer varsa)
                Border? sentFileCountLabel = null;
                if (hasSentFiles)
                {
                    sentFileCountLabel = new Border
                    {
                        Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 152, 0)), // Turuncu - gönderilmiş
                        CornerRadius = new CornerRadius(10),
                        Padding = new Thickness(6, 2, 6, 2),
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 5, 0),
                        Child = new TextBlock
                        {
                            Text = $"📧 {sentFileCount} gönderildi",
                            FontSize = 11,
                            Foreground = System.Windows.Media.Brushes.White,
                            FontFamily = new System.Windows.Media.FontFamily("pack://application:,,,/Fonts/Poppins-Regular.ttf#Poppins"),
                            FontWeight = FontWeights.Medium
                        }
                    };
                }

                panel.Children.Add(checkBox);
                panel.Children.Add(textBlock);
                panel.Children.Add(fileCountLabel);
                
                // Gönderilmiş dosya label'ını ekle (eğer varsa)
                if (sentFileCountLabel != null)
                {
                    panel.Children.Add(sentFileCountLabel);
                }

                spKeywordSelection.Children.Add(panel);
                _keywordCheckBoxes[keyword.Keyword] = checkBox;
            }
        }

        private void OnKeywordSelectionChanged(string keyword, bool isSelected)
        {
            try
            {
                // Sıralamayı güncelle
                UpdateKeywordOrder();

                OnLogMessage($"📋 Keyword sıralaması güncellendi: {keyword} {(isSelected ? "seçildi" : "kaldırıldı")}");
            }
            catch (Exception ex)
            {
                OnLogMessage($"❌ Keyword sıralaması güncellenirken hata: {ex.Message}");
            }
        }

        private void btnSelectFolder_Click(object sender, RoutedEventArgs e)
        {
            using (var folderDialog = new FolderBrowserDialog())
            {
                folderDialog.Description = "Analiz edilecek klasörü seçin";
                folderDialog.ShowNewFolderButton = false;

                if (folderDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    _selectedFolderPath = folderDialog.SelectedPath;
                    txtFolderPath.Text = _selectedFolderPath;
                    
                    // Ödeme emri oluşturan dropdown'ını güncelle
                    LoadPaymentOrderCreators();
                    
                    // Klasör seçildiğinde otomatik analiz yap
                    AnalyzeFolder();
                }
            }
        }

        private void btnAnalyzeFolder_Click(object sender, RoutedEventArgs e)
        {
            AnalyzeFolder();
        }

        private void AnalyzeFolder()
        {
            if (string.IsNullOrEmpty(_selectedFolderPath) || !Directory.Exists(_selectedFolderPath))
            {
                System.Windows.MessageBox.Show("Lütfen geçerli bir klasör seçin.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // Analiz sonuçlarını temizle
                spAnalysisResults.Children.Clear();
                _keywordFileMappings.Clear();

                // Her keyword için boş liste oluştur
                foreach (var keyword in _keywords)
                {
                    _keywordFileMappings[keyword.Keyword] = new List<string>();
                }

                // Klasör yapısını analiz et
                var folderStructure = AnalyzeFolderStructure(_selectedFolderPath);
                
                // Tarih filtresi uygula (her zaman aktif)
                var filteredFolders = folderStructure;
                if (dpSelectedDate.SelectedDate.HasValue)
                {
                    var selectedDate = dpSelectedDate.SelectedDate.Value.Date;

                    filteredFolders = folderStructure.Where(folder =>
                    {
                        try
                        {
                            var folderInfo = new DirectoryInfo(folder.FolderPath);
                            var folderDate = folderInfo.CreationTime.Date;
                            return folderDate == selectedDate;
                        }
                        catch
                        {
                            return false;
                        }
                    }).ToList();
                }
                else
                {
                    // Tarih seçilmemişse uyarı ver
                    System.Windows.MessageBox.Show("Lütfen bir tarih seçin.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Klasörleri keyword'lere göre eşleştir
                foreach (var folder in filteredFolders)
                {
                    var folderName = Path.GetFileName(folder.FolderPath).ToUpper();
                    
                    foreach (var keyword in _keywords)
                    {
                        var keywordUpper = keyword.Keyword.ToUpper();
                        if (folderName.Contains(keywordUpper))
                        {
                            // Klasördeki tüm dosyaları ekle
                            _keywordFileMappings[keyword.Keyword].AddRange(folder.Files);
                            break; // İlk eşleşen keyword'i bulduk
                        }
                    }
                }

                // Ödeme emri oluşturanları göster
                DisplayPaymentOrderCreators();

                // Analiz sonuçlarını göster
                DisplayAnalysisResults(filteredFolders.Sum(f => f.Files.Count));

                // Keyword seçim panelini güncelle
                UpdateKeywordSelectionPanel();
                
                // Mail geçmişini kontrol et ve dosya gönderim durumlarını güncelle
                UpdateKeywordSelectionPanelWithSentStatus();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Klasör analizi sırasında hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private class FolderInfo
        {
            public string FolderPath { get; set; } = "";
            public List<string> Files { get; set; } = new List<string>();
            public string? PeriodId { get; set; }
        }

        private List<FolderInfo> AnalyzeFolderStructure(string rootPath)
        {
            var folders = new List<FolderInfo>();
            
            try
            {
                // Tarih klasörlerini bul (örn: 28.08.2025)
                var dateFolders = Directory.GetDirectories(rootPath)
                    .Where(dir => System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(dir), @"^\d{2}\.\d{2}\.\d{4}$"))
                    .ToList();

                foreach (var dateFolder in dateFolders)
                {
                    // Kişi klasörlerini bul
                    var personFolders = Directory.GetDirectories(dateFolder);
                    
                    foreach (var personFolder in personFolders)
                    {
                        // Dönem klasörlerini bul (örn: 01-15_Tem_2025_İZMİR_MONSANTO)
                        var periodFolders = Directory.GetDirectories(personFolder);
                        
                        foreach (var periodFolder in periodFolders)
                        {
                            var folderInfo = new FolderInfo
                            {
                                FolderPath = periodFolder,
                                Files = Directory.GetFiles(periodFolder, "*.*", SearchOption.TopDirectoryOnly)
                                    .Where(f => Path.GetExtension(f).ToLower() != ".tmp" && 
                                               Path.GetExtension(f).ToLower() != ".temp" &&
                                               !Path.GetFileName(f).StartsWith("."))
                                    .ToList()
                            };

                            // Dönem ID'sini oku
                            var periodIdFile = Path.Combine(periodFolder, ".period_id.txt");
                            if (File.Exists(periodIdFile))
                            {
                                folderInfo.PeriodId = File.ReadAllText(periodIdFile).Trim();
                            }

                            folders.Add(folderInfo);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                OnLogMessage($"Klasör yapısı analizi hatası: {ex.Message}");
            }

            return folders;
        }

        private void DisplayAnalysisResults(int totalFiles)
        {
            spAnalysisResults.Children.Clear();

            // Tarih filtresi bilgisi (her zaman göster)
            if (dpSelectedDate.SelectedDate.HasValue)
            {
                var dateFilterLabel = new TextBlock
                {
                    Text = $"📅 Seçili Tarih: {dpSelectedDate.SelectedDate.Value:dd.MM.yyyy}",
                    FontWeight = FontWeights.Normal,
                    FontSize = 13,
                    FontFamily = new System.Windows.Media.FontFamily("pack://application:,,,/Fonts/Poppins-Regular.ttf#Poppins"),
                    Foreground = System.Windows.Media.Brushes.Blue,
                    Margin = new Thickness(0, 0, 0, 8)
                };
                spAnalysisResults.Children.Add(dateFilterLabel);
            }

            // Toplam dosya sayısı
            var totalLabel = new TextBlock
            {
                Text = $"📁 Toplam Dosya: {totalFiles}",
                FontWeight = FontWeights.Normal,
                FontSize = 13,
                FontFamily = new System.Windows.Media.FontFamily("pack://application:,,,/Fonts/Poppins-Regular.ttf#Poppins"),
                Margin = new Thickness(0, 0, 0, 8)
            };
            spAnalysisResults.Children.Add(totalLabel);

            // Klasör yapısı bilgisi
            var folderStructure = AnalyzeFolderStructure(_selectedFolderPath);
            var folderCount = folderStructure.Count;
            var folderLabel = new TextBlock
            {
                Text = $"📂 Analiz Edilen Klasör: {folderCount}",
                FontWeight = FontWeights.Normal,
                FontSize = 13,
                FontFamily = new System.Windows.Media.FontFamily("pack://application:,,,/Fonts/Poppins-Regular.ttf#Poppins"),
                Margin = new Thickness(0, 0, 0, 8)
            };
            spAnalysisResults.Children.Add(folderLabel);

            // Keyword bazlı sonuçlar
            var activeKeywords = _keywords.Where(k => _keywordFileMappings.ContainsKey(k.Keyword) && _keywordFileMappings[k.Keyword].Any()).ToList();

            if (activeKeywords.Any())
            {
                var activeLabel = new TextBlock
                {
                    Text = $"✅ Eşleşen Keyword'ler: {activeKeywords.Count}",
                    FontWeight = FontWeights.Normal,
                    FontSize = 13,
                    FontFamily = new System.Windows.Media.FontFamily("pack://application:,,,/Fonts/Poppins-Regular.ttf#Poppins"),
                    Foreground = System.Windows.Media.Brushes.Green,
                    Margin = new Thickness(0, 0, 0, 8)
                };
                spAnalysisResults.Children.Add(activeLabel);

                foreach (var keyword in activeKeywords)
                {
                    var fileCount = _keywordFileMappings.ContainsKey(keyword.Keyword) ? _keywordFileMappings[keyword.Keyword].Count : 0;
                    var keywordLabel = new TextBlock
                    {
                        Text = $"• {keyword.Keyword}: {fileCount} dosya → {keyword.EmailRecipient}",
                        Margin = new Thickness(8, 1, 0, 1),
                        FontSize = 13,
                        FontFamily = new System.Windows.Media.FontFamily("pack://application:,,,/Fonts/Poppins-Regular.ttf#Poppins"),
                        TextWrapping = TextWrapping.Wrap
                    };
                    spAnalysisResults.Children.Add(keywordLabel);
                }
            }
            else
            {
                var noMatchLabel = new TextBlock
                {
                    Text = "❌ Hiçbir keyword için dosya bulunamadı.",
                    Foreground = System.Windows.Media.Brushes.Red,
                    FontSize = 13,
                    FontFamily = new System.Windows.Media.FontFamily("pack://application:,,,/Fonts/Poppins-Regular.ttf#Poppins"),
                    Margin = new Thickness(0, 0, 0, 8)
                };
                spAnalysisResults.Children.Add(noMatchLabel);
            }

            // Ödeme emri oluşturanları göster (analiz butonundan önce)
            // DisplayPaymentOrderCreators(); // Bu kısım AnalyzeFolder metodunda çağrılıyor
        }

        private void DisplayPaymentOrderCreators()
        {
            spPaymentOrderCreators.Children.Clear();

            try
            {
                if (string.IsNullOrEmpty(_selectedFolderPath) || !Directory.Exists(_selectedFolderPath))
                {
                    var noFolderLabel = new TextBlock
                    {
                        Text = "📁 Klasör seçilmedi",
                        FontSize = 13,
                        FontFamily = new System.Windows.Media.FontFamily("pack://application:,,,/Fonts/Poppins-Regular.ttf#Poppins"),
                        Foreground = System.Windows.Media.Brushes.Gray,
                        Margin = new Thickness(0, 0, 0, 8)
                    };
                    spPaymentOrderCreators.Children.Add(noFolderLabel);
                    return;
                }

                // Klasör yapısını analiz et
                var folderStructure = AnalyzeFolderStructure(_selectedFolderPath);
                
                // Ödeme emri oluşturanları topla
                var creators = new HashSet<string>();
                
                foreach (var folder in folderStructure)
                {
                    var folderPath = folder.FolderPath;
                    var pathParts = folderPath.Split(Path.DirectorySeparatorChar);
                    
                    // Tarih klasöründen sonraki kişi adını al
                    for (int i = 0; i < pathParts.Length - 1; i++)
                    {
                        if (System.Text.RegularExpressions.Regex.IsMatch(pathParts[i], @"^\d{2}\.\d{2}\.\d{4}$"))
                        {
                            if (i + 1 < pathParts.Length)
                            {
                                creators.Add(pathParts[i + 1]);
                            }
                            break;
                        }
                    }
                }

                // Dropdown'ı güncelle - bulunan kişileri ekle
                UpdatePaymentOrderCreatorDropdown(creators);

                if (creators.Any())
                {
                    var creatorCountLabel = new TextBlock
                    {
                        Text = $"👥 Toplam {creators.Count} ödeme emri oluşturan bulundu:",
                        FontSize = 13,
                        FontFamily = new System.Windows.Media.FontFamily("pack://application:,,,/Fonts/Poppins-Regular.ttf#Poppins"),
                        FontWeight = FontWeights.Normal,
                        Margin = new Thickness(0, 0, 0, 8)
                    };
                    spPaymentOrderCreators.Children.Add(creatorCountLabel);

                    foreach (var creator in creators.OrderBy(c => c))
                    {
                        var creatorLabel = new TextBlock
                        {
                            Text = $"• {creator}",
                            FontSize = 13,
                            FontFamily = new System.Windows.Media.FontFamily("pack://application:,,,/Fonts/Poppins-Regular.ttf#Poppins"),
                            Margin = new Thickness(8, 1, 0, 1),
                            TextWrapping = TextWrapping.Wrap
                        };
                        spPaymentOrderCreators.Children.Add(creatorLabel);
                    }
                }
                else
                {
                    var noCreatorLabel = new TextBlock
                    {
                        Text = "❌ Ödeme emri oluşturan bulunamadı",
                        FontSize = 13,
                        FontFamily = new System.Windows.Media.FontFamily("pack://application:,,,/Fonts/Poppins-Regular.ttf#Poppins"),
                        Foreground = System.Windows.Media.Brushes.Red,
                        Margin = new Thickness(0, 0, 0, 8)
                    };
                    spPaymentOrderCreators.Children.Add(noCreatorLabel);
                }
            }
            catch (Exception ex)
            {
                var errorLabel = new TextBlock
                {
                    Text = $"❌ Hata: {ex.Message}",
                    FontSize = 13,
                    FontFamily = new System.Windows.Media.FontFamily("pack://application:,,,/Fonts/Poppins-Regular.ttf#Poppins"),
                    Foreground = System.Windows.Media.Brushes.Red,
                    Margin = new Thickness(0, 0, 0, 8)
                };
                spPaymentOrderCreators.Children.Add(errorLabel);
            }
        }

        private void UpdatePaymentOrderCreatorDropdown(HashSet<string> creators)
        {
            try
            {
                cmbPaymentOrderCreator.Items.Clear();
                cmbPaymentOrderCreator.Items.Add("Tümü"); // Varsayılan seçenek
                
                OnLogMessage($"Dropdown güncelleniyor... Bulunan kişi sayısı: {creators.Count}");
                
                foreach (var creator in creators.OrderBy(c => c))
                {
                    cmbPaymentOrderCreator.Items.Add(creator);
                    OnLogMessage($"Dropdown'a eklendi: {creator}");
                }
                
                cmbPaymentOrderCreator.SelectedIndex = 0; // İlk öğeyi seç (Tümü)
                OnLogMessage($"Dropdown güncelleme tamamlandı. Toplam öğe: {cmbPaymentOrderCreator.Items.Count}");
            }
            catch (Exception ex)
            {
                OnLogMessage($"Dropdown güncellenirken hata: {ex.Message}");
            }
        }

        private void UpdateKeywordSelectionPanel()
        {
            // Keyword seçim panelini güncelle
            spKeywordSelection.Children.Clear();
            _keywordCheckBoxes.Clear();

            foreach (var keyword in _keywords)
            {
                var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };

                var checkBox = new CheckBox
                {
                    Content = $"{keyword.Keyword} → {keyword.EmailRecipient}",
                    IsChecked = _keywordFileMappings.ContainsKey(keyword.Keyword) && _keywordFileMappings[keyword.Keyword].Any(), // Sadece dosyası olan keyword'ler seçili
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 13,
                    FontFamily = new System.Windows.Media.FontFamily("pack://application:,,,/Fonts/Poppins-Regular.ttf#Poppins"),
                    Cursor = System.Windows.Input.Cursors.Hand
                };

                var fileCount = _keywordFileMappings.ContainsKey(keyword.Keyword) ? _keywordFileMappings[keyword.Keyword].Count : 0;
                var fileCountLabel = new Border
                {
                    Background = fileCount > 0 ? 
                        new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(76, 175, 80)) : 
                        new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(158, 158, 158)),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(6, 2, 6, 2),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = $"{fileCount} dosya",
                        FontSize = 11,
                        Foreground = System.Windows.Media.Brushes.White,
                        FontFamily = new System.Windows.Media.FontFamily("pack://application:,,,/Fonts/Poppins-Regular.ttf#Poppins"),
                        FontWeight = FontWeights.Medium
                    }
                };

                panel.Children.Add(checkBox);
                panel.Children.Add(fileCountLabel);

                spKeywordSelection.Children.Add(panel);
                _keywordCheckBoxes[keyword.Keyword] = checkBox;
            }
        }

        private void btnSelectAll_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Önce tüm checkbox durumlarını true yap
                var checkboxStates = new Dictionary<string, bool>();
                foreach (var kvp in _keywordCheckBoxes)
                {
                    checkboxStates[kvp.Key] = true;
                }

                // Keyword listesini yeniden sırala - tümü seçili olduğu için hepsi üstte
                var selectedKeywords = _keywords.ToList(); // Tümü seçili
                var unselectedKeywords = new List<KeywordNotification>(); // Boş liste

                // Seçili olanları önce, seçili olmayanları sonra ekle
                _keywords = selectedKeywords.Concat(unselectedKeywords).ToList();

                // Panel'i yeniden oluştur ve checkbox durumlarını geri yükle
                RecreateKeywordPanelWithStates(checkboxStates);
                
                OnLogMessage("✅ Tüm keyword'ler seçildi ve üst sıraya taşındı");
            }
            catch (Exception ex)
            {
                OnLogMessage($"❌ Tümünü seçerken hata: {ex.Message}");
            }
        }

        private void btnDeselectAll_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Önce tüm checkbox durumlarını false yap
                var checkboxStates = new Dictionary<string, bool>();
                foreach (var kvp in _keywordCheckBoxes)
                {
                    checkboxStates[kvp.Key] = false;
                }

                // Keyword listesini yeniden sırala - seçili olanlar üstte (hiçbiri seçili değil)
                var selectedKeywords = new List<KeywordNotification>(); // Boş liste
                var unselectedKeywords = _keywords.ToList(); // Tümü seçili değil

                // Seçili olanları önce, seçili olmayanları sonra ekle
                _keywords = selectedKeywords.Concat(unselectedKeywords).ToList();

                // Panel'i yeniden oluştur ve checkbox durumlarını geri yükle
                RecreateKeywordPanelWithStates(checkboxStates);
                
                OnLogMessage("❌ Tüm keyword'ler kaldırıldı");
            }
            catch (Exception ex)
            {
                OnLogMessage($"❌ Tümünü kaldırırken hata: {ex.Message}");
            }
        }

        private void UpdateKeywordOrder()
        {
            try
            {
                // Mevcut checkbox durumlarını kaydet
                var checkboxStates = new Dictionary<string, bool>();
                foreach (var kvp in _keywordCheckBoxes)
                {
                    checkboxStates[kvp.Key] = kvp.Value.IsChecked == true;
                }

                // Keyword listesini yeniden sırala - seçili olanlar üstte
                var selectedKeywords = _keywords.Where(k => 
                    checkboxStates.ContainsKey(k.Keyword) && 
                    checkboxStates[k.Keyword]).ToList();
                
                var unselectedKeywords = _keywords.Where(k => 
                    checkboxStates.ContainsKey(k.Keyword) && 
                    !checkboxStates[k.Keyword]).ToList();

                // Seçili olanları önce, seçili olmayanları sonra ekle
                _keywords = selectedKeywords.Concat(unselectedKeywords).ToList();

                // Panel'i yeniden oluştur ve checkbox durumlarını geri yükle
                RecreateKeywordPanelWithStates(checkboxStates);
            }
            catch (Exception ex)
            {
                OnLogMessage($"❌ Keyword sıralaması güncellenirken hata: {ex.Message}");
            }
        }

        private void RecreateKeywordPanelWithStates(Dictionary<string, bool> checkboxStates)
        {
            spKeywordSelection.Children.Clear();
            _keywordCheckBoxes.Clear();

            foreach (var keyword in _keywords)
            {
                var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };

                var checkBox = new CheckBox
                {
                    Content = $"{keyword.Keyword} → {keyword.EmailRecipient}",
                    IsChecked = checkboxStates.ContainsKey(keyword.Keyword) ? checkboxStates[keyword.Keyword] : false,
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 13,
                    FontFamily = new System.Windows.Media.FontFamily("pack://application:,,,/Fonts/Poppins-Regular.ttf#Poppins"),
                    Cursor = System.Windows.Input.Cursors.Hand
                };

                // Event handler'ları ekle
                checkBox.Checked += (s, e) => OnKeywordSelectionChanged(keyword.Keyword, true);
                checkBox.Unchecked += (s, e) => OnKeywordSelectionChanged(keyword.Keyword, false);

                // Dosya sayısını _keywordFileMappings'den al
                var fileCount = _keywordFileMappings.ContainsKey(keyword.Keyword) ? _keywordFileMappings[keyword.Keyword].Count : 0;
                
                var fileCountLabel = new Border
                {
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(158, 158, 158)),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(6, 2, 6, 2),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock
                    {
                        Text = $"{fileCount} dosya",
                        FontSize = 11,
                        Foreground = System.Windows.Media.Brushes.White,
                        FontFamily = new System.Windows.Media.FontFamily("pack://application:,,,/Fonts/Poppins-Regular.ttf#Poppins"),
                        FontWeight = FontWeights.Medium
                    }
                };

                panel.Children.Add(checkBox);
                panel.Children.Add(fileCountLabel);

                spKeywordSelection.Children.Add(panel);
                _keywordCheckBoxes[keyword.Keyword] = checkBox;
            }
        }

        private async void btnSendSms_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Seçili keyword'leri al
                var selectedKeywords = _keywords.Where(k => 
                    _keywordCheckBoxes.ContainsKey(k.Keyword) && 
                    _keywordCheckBoxes[k.Keyword].IsChecked == true &&
                    _keywordFileMappings.ContainsKey(k.Keyword) && 
                    _keywordFileMappings[k.Keyword].Any()).ToList();

                if (!selectedKeywords.Any())
                {
                    System.Windows.MessageBox.Show("Gönderilecek keyword seçilmedi veya seçili keyword'ler için dosya bulunamadı.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Onay al
                var result = System.Windows.MessageBox.Show(
                    $"{selectedKeywords.Count} keyword için SMS gönderilecek. Devam etmek istiyor musunuz?",
                    "Onay",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes)
                    return;

                // SMS gönderimi başlat
                // btnSendSms butonu kaldırıldı

                var successCount = 0;
                var totalCount = selectedKeywords.Count;

                foreach (var keyword in selectedKeywords)
                {
                    try
                    {
                        var keywordFiles = _keywordFileMappings.ContainsKey(keyword.Keyword) ? _keywordFileMappings[keyword.Keyword] : new List<string>();
                        
                        // Mail gönderimi için özel metod
                        await SendMailForKeywordAsync(keyword, keywordFiles);
                        successCount++;
                    }
                    catch (Exception ex)
                    {
                        System.Windows.MessageBox.Show($"'{keyword.Keyword}' için SMS gönderimi başarısız: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }

                // Sonuç göster
                System.Windows.MessageBox.Show(
                    $"Mail gönderimi tamamlandı!\n\nBaşarılı: {successCount}/{totalCount}",
                    "Tamamlandı",
                    MessageBoxButton.OK,
                    successCount == totalCount ? MessageBoxImage.Information : MessageBoxImage.Warning);

                // Modal'ı kapat
                Close();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Mail gönderimi sırasında hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // btnSendSms butonu kaldırıldı
            }
        }

        private async void btnSendMails_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Dropdown'dan seçili kişiyi al
                var selectedCreator = cmbPaymentOrderCreator.SelectedItem?.ToString();
                
                if (string.IsNullOrEmpty(selectedCreator))
                {
                    System.Windows.MessageBox.Show("Lütfen ödeme emri oluşturan seçin.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                OnLogMessage($"👤 Seçili ödeme emri oluşturan: {selectedCreator}");

                // Seçili keyword'leri al
                var selectedKeywords = _keywords.Where(k => 
                    _keywordCheckBoxes.ContainsKey(k.Keyword) && 
                    _keywordCheckBoxes[k.Keyword].IsChecked == true &&
                    _keywordFileMappings.ContainsKey(k.Keyword) && 
                    _keywordFileMappings[k.Keyword].Any()).ToList();

                if (!selectedKeywords.Any())
                {
                    System.Windows.MessageBox.Show("Gönderilecek keyword seçilmedi veya seçili keyword'ler için dosya bulunamadı.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Seçili kişiye göre dosyaları filtrele
                var filteredKeywords = new List<(KeywordNotification keyword, List<string> files)>();
                
                foreach (var keyword in selectedKeywords)
                {
                    var keywordFiles = _keywordFileMappings.ContainsKey(keyword.Keyword) ? _keywordFileMappings[keyword.Keyword] : new List<string>();
                    
                    if (selectedCreator == "Tümü")
                    {
                        // Tüm dosyaları kullan
                        filteredKeywords.Add((keyword, keywordFiles));
                        OnLogMessage($"📧 '{keyword.Keyword}' için {keywordFiles.Count} dosya (Tümü seçili)");
                    }
                    else
                    {
                        // Sadece seçili kişiye ait dosyaları filtrele
                        var filteredFiles = keywordFiles.Where(file => 
                        {
                            var filePath = file;
                            var pathParts = filePath.Split(Path.DirectorySeparatorChar);
                            
                            // Dosya yolunda seçili kişinin adını ara
                            return pathParts.Any(part => part.Contains(selectedCreator));
                        }).ToList();
                        
                        if (filteredFiles.Any())
                        {
                            filteredKeywords.Add((keyword, filteredFiles));
                            OnLogMessage($"📧 '{keyword.Keyword}' için {filteredFiles.Count} dosya ({selectedCreator} için filtrelendi)");
                        }
                        else
                        {
                            OnLogMessage($"⚠️ '{keyword.Keyword}' için {selectedCreator} kişisine ait dosya bulunamadı");
                        }
                    }
                }

                if (!filteredKeywords.Any())
                {
                    System.Windows.MessageBox.Show($"Seçili kişi ({selectedCreator}) için gönderilecek dosya bulunamadı.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Onay al
                var result = System.Windows.MessageBox.Show(
                    $"{filteredKeywords.Count} keyword için {selectedCreator} kişisine mail gönderilecek. Devam etmek istiyor musunuz?",
                    "Onay",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes)
                    return;

                // Mail gönderimi başlat
                btnSendMails.IsEnabled = false;
                btnSendMails.Content = "📤 Gönderiliyor...";

                var successCount = 0;
                var totalCount = filteredKeywords.Count;

                foreach (var (keyword, files) in filteredKeywords)
                {
                    try
                    {
                        // Mail gönderimi için özel metod
                        await SendMailForKeywordAsync(keyword, files);
                        successCount++;
                    }
                    catch (Exception ex)
                    {
                        System.Windows.MessageBox.Show($"'{keyword.Keyword}' için mail gönderimi başarısız: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }

                // Sonuç göster
                System.Windows.MessageBox.Show(
                    $"Mail gönderimi tamamlandı!\n\nSeçili Kişi: {selectedCreator}\nBaşarılı: {successCount}/{totalCount}",
                    "Tamamlandı",
                    MessageBoxButton.OK,
                    successCount == totalCount ? MessageBoxImage.Information : MessageBoxImage.Warning);

                // Modal'ı kapat
                Close();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Mail gönderimi sırasında hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btnSendMails.IsEnabled = true;
                btnSendMails.Content = "📤 Seçili Mail'leri Gönder";
            }
        }

        private async Task SendMailForKeywordAsync(KeywordNotification keyword, List<string> files)
        {
            try
            {
                OnLogMessage($"📧 '{keyword.Keyword}' için mail gönderimi başlatılıyor...");
                
                // Config'den mail ayarlarını al
                var config = ConfigManager.LoadConfig();
                
                // Dönem adını al (dosya yolundan)
                var periodName = GetPeriodNameFromFiles(files);
                
                // Mail konusu ve içeriği oluştur (otomatik bildirimlerle aynı format)
                var subject = $"✅ Ödeme Emri Tamamlandı - {keyword.Keyword} - {periodName}";
                var body = $@"Merhaba,

'{periodName}' dönemi için ödeme emri oluşturma işlemi tamamlanmıştır.

Ödeme emri muhasebe birimine gönderilmiştir.

İyi çalışmalar dilerim.";
                
                // EmailNotificationService kullanarak mail gönder (mail geçmişine kayıt için)
                var emailService = new EmailNotificationService(config.Notification);
                await emailService.SendManualEmailAsync(keyword.EmailRecipient, subject, body);
                
                OnLogMessage($"✅ '{keyword.Keyword}' için mail başarıyla gönderildi! Alıcı: {keyword.EmailRecipient}");
            }
            catch (Exception ex)
            {
                OnLogMessage($"❌ '{keyword.Keyword}' için mail gönderimi hatası: {ex.Message}");
                throw;
            }
        }

        private async Task SendMailViaOutlookAsync(string recipient, string subject, string body)
        {
            try
            {
                OnLogMessage("📧 Outlook Classic açılıyor...");
                
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
                    // Outlook Classic ile mail aç
                    var mailtoUrl = $"mailto:{recipient}?subject={Uri.EscapeDataString(subject)}&body={Uri.EscapeDataString(body)}";

                    var process = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = foundOutlookPath,
                            Arguments = $"/c ipm.note /m \"{mailtoUrl}\"",
                            UseShellExecute = false
                        }
                    };

                    process.Start();
                    OnLogMessage($"✅ Outlook Classic açıldı: {foundOutlookPath}");

                    // Mail açıldıktan sonra Ctrl+Enter ile gönderme için kısa bekleme
                    await Task.Delay(2000);

                    // Ctrl+Enter tuş kombinasyonunu simüle et
                    OnLogMessage("📤 Ctrl+Enter ile mail gönderiliyor...");

                    // SendKeys kullanarak Ctrl+Enter gönder
                    System.Windows.Forms.SendKeys.SendWait("^{ENTER}");

                    // Outlook kapatılana kadar bekle
                    process.WaitForExit();
                    OnLogMessage("📧 Outlook kapatıldı, mail gönderme işlemi tamamlandı.");
                }
                else
                {
                    // Outlook bulunamazsa varsayılan mail uygulamasını kullan
                    var mailtoUrl = $"mailto:{recipient}?subject={Uri.EscapeDataString(subject)}&body={Uri.EscapeDataString(body)}";
                    
                    var process = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = mailtoUrl,
                            UseShellExecute = true
                        }
                    };
                    
                    process.Start();
                    OnLogMessage("✅ Varsayılan mail uygulaması açıldı.");
                    
                    // Varsayılan uygulama için de Ctrl+Enter dene
                    await Task.Delay(2000);
                    System.Windows.Forms.SendKeys.SendWait("^{ENTER}");
                }
                
                // Kısa bir bekleme süresi
                await Task.Delay(1000);
            }
            catch (Exception ex)
            {
                OnLogMessage($"❌ Outlook açılırken hata: {ex.Message}");
                throw;
            }
        }



        private async Task SendMailForPaymentOrderCreatorAsync(KeywordNotification keyword, List<string> files)
        {
            try
            {
                OnLogMessage($"📧 '{keyword.Keyword}' için ödeme emri oluşturan mail gönderimi başlatılıyor...");
                
                // Dosya isimlerinden dönem bilgisi çıkar
                OnLogMessage("📅 Dosya isimlerinden dönem bilgisi çıkarılıyor...");
                var periodName = ExtractPeriodFromFiles(files);
                OnLogMessage($"📅 Bulunan dönem: {periodName}");
                
                // Mail konusu ve içeriği oluştur
                var subject = $"Ödeme Emri Bildirimi - {keyword.Keyword} - {periodName}";
                var body = $@"Merhaba,

Ödeme emri işlemi tamamlandı.

İşlem Detayları:
- Dönem: {periodName}
- İşlem Tarihi: {DateTime.Now:dd.MM.yyyy HH:mm:ss}

Bu mail otomatik olarak gönderilmiştir.

Saygılarımla,
Ödeme Emri Oluşturucu Sistemi";
                
                // Outlook Classic'i aç ve mail gönder
                await SendMailViaOutlookAsync(keyword.EmailRecipient, subject, body);
                
                OnLogMessage("✅ Mail gönderimi tamamlandı!");
                    
                    System.Windows.MessageBox.Show(
                    $"Ödeme Emri Oluşturan mail gönderimi tamamlandı!\n\nDönem: {periodName}\nAlıcı: {keyword.EmailRecipient}",
                    "Mail Gönderimi Tamamlandı",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                OnLogMessage($"❌ Hata: {ex.Message}");
                System.Windows.MessageBox.Show(
                    $"Mail gönderimi sırasında hata oluştu: {ex.Message}",
                    "Hata",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private string ExtractPeriodFromFiles(List<string> files)
        {
            // Önce dosyaların bulunduğu klasörde gizli txt dosyasından dönem ID'sini okumaya çalış
            var periodId = ExtractPeriodIdFromFolder(files);
            if (!string.IsNullOrEmpty(periodId))
            {
                // Dönem ID'sini yıl-ay formatına çevir
                if (periodId.Length == 6) // "202507" formatı
                {
                    var year = periodId.Substring(0, 4);
                    var month = periodId.Substring(4, 2);
                    return $"{year}-{month}";
                }
            }
            
            // Dosya isimlerinden dönem bilgisi çıkarmaya çalış
            foreach (var file in files)
            {
                var fileName = Path.GetFileName(file);
                
                // Dosya adında tarih formatı ara (örnek: "2024-01", "2024_01", "2024.01")
                var periodMatch = System.Text.RegularExpressions.Regex.Match(fileName, @"(\d{4})[-_.](\d{1,2})");
                if (periodMatch.Success)
                {
                    var year = periodMatch.Groups[1].Value;
                    var month = periodMatch.Groups[2].Value.PadLeft(2, '0');
                    return $"{year}-{month}";
                }
                
                // Türkçe ay isimleri ile tarih formatı ara (örnek: "01-15_Tem_2025")
                var turkishMonthMatch = System.Text.RegularExpressions.Regex.Match(fileName, @"(\d{1,2})-(\d{1,2})_([A-Za-z]+)_(\d{4})");
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
                    
                    return $"{year}-{monthNumber}";
                }
            }
            
            // Eğer dosya adından çıkarılamazsa, mevcut ayı kullan
            return DateTime.Now.ToString("yyyy-MM");
        }

        /// <summary>
        /// Dosyaların bulunduğu klasörden dönem ID'sini okur
        /// </summary>
        private string ExtractPeriodIdFromFolder(List<string> files)
        {
            if (!files.Any())
                return string.Empty;

            try
            {
                var firstFile = files.First();
                var folderPath = Path.GetDirectoryName(firstFile);
                var periodIdFilePath = Path.Combine(folderPath, ".period_id.txt");
                
                if (File.Exists(periodIdFilePath))
                {
                    var periodId = File.ReadAllText(periodIdFilePath).Trim();
                    OnLogMessage($"📝 Klasörden dönem ID'si okundu: {periodId}");
                    return periodId;
                }
            }
            catch (Exception ex)
            {
                OnLogMessage($"⚠️ Dönem ID'si okunamadı: {ex.Message}");
            }
            
            return string.Empty;
        }



        private void btnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void txtFilter_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter();
        }

        private void btnClearFilter_Click(object sender, RoutedEventArgs e)
        {
            txtFilter.Text = "";
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            var filterText = txtFilter.Text.Trim().ToLower();
            
            if (string.IsNullOrEmpty(filterText))
            {
                _filteredKeywords = new List<KeywordNotification>(_allKeywords);
            }
            else
            {
                _filteredKeywords = _allKeywords.Where(k => 
                    k.Keyword.ToLower().Contains(filterText) || 
                    k.EmailRecipient.ToLower().Contains(filterText)).ToList();
            }
            
            _keywords = _filteredKeywords;
            UpdateKeywordSelectionPanel(); // CreateKeywordSelectionPanel yerine UpdateKeywordSelectionPanel kullan
        }

        private void btnFilterHasFiles_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Sadece dosyası olan keyword'leri filtrele
                _filteredKeywords = _allKeywords.Where(k => 
                    _keywordFileMappings.ContainsKey(k.Keyword) && 
                    _keywordFileMappings[k.Keyword].Any()).ToList();
                
                _keywords = _filteredKeywords;
                CreateKeywordSelectionPanel();
                
                OnLogMessage($"📁 Dosya var filtresi uygulandı: {_filteredKeywords.Count} keyword gösteriliyor");
            }
            catch (Exception ex)
            {
                OnLogMessage($"❌ Dosya var filtresi uygulanırken hata: {ex.Message}");
            }
        }

        private void btnFilterNoFiles_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Sadece dosyası olmayan keyword'leri filtrele
                _filteredKeywords = _allKeywords.Where(k => 
                    !_keywordFileMappings.ContainsKey(k.Keyword) || 
                    !_keywordFileMappings[k.Keyword].Any()).ToList();
                
                _keywords = _filteredKeywords;
                CreateKeywordSelectionPanel();
                
                OnLogMessage($"📂 Dosya yok filtresi uygulandı: {_filteredKeywords.Count} keyword gösteriliyor");
            }
            catch (Exception ex)
            {
                OnLogMessage($"❌ Dosya yok filtresi uygulanırken hata: {ex.Message}");
            }
        }

        private void btnFilterAll_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Tüm keyword'leri göster
                _filteredKeywords = new List<KeywordNotification>(_allKeywords);
                _keywords = _filteredKeywords;
                CreateKeywordSelectionPanel();
                
                OnLogMessage($"🔍 Tümü filtresi uygulandı: {_filteredKeywords.Count} keyword gösteriliyor");
            }
            catch (Exception ex)
            {
                OnLogMessage($"❌ Tümü filtresi uygulanırken hata: {ex.Message}");
            }
        }

        private void btnUpdateSentStatus_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                OnLogMessage("📧 Dosya gönderim durumları güncelleniyor...");
                UpdateKeywordSelectionPanelWithSentStatus();
            }
            catch (Exception ex)
            {
                OnLogMessage($"❌ Gönderim durumları güncellenirken hata: {ex.Message}");
            }
        }



        private void cmbPaymentOrderCreator_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbPaymentOrderCreator.SelectedItem != null)
            {
                _selectedPaymentOrderCreator = cmbPaymentOrderCreator.SelectedItem.ToString();
                OnLogMessage($"Ödeme emri oluşturan seçildi: {_selectedPaymentOrderCreator}");
            }
        }



        private void btnClearLog_Click(object sender, RoutedEventArgs e)
        {
            txtLog.Text = "";
            UpdateLogStatistics();
            OnLogMessage("Log temizlendi.");
        }

        private void btnExportLog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var saveFileDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "Metin Dosyası (*.txt)|*.txt|Tüm Dosyalar (*.*)|*.*",
                    DefaultExt = "txt",
                    FileName = $"manual_send_log_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    File.WriteAllText(saveFileDialog.FileName, txtLog.Text);
                    OnLogMessage($"Log dosyası dışa aktarıldı: {saveFileDialog.FileName}");
                    System.Windows.MessageBox.Show($"Log dosyası başarıyla dışa aktarıldı.\nDosya: {Path.GetFileName(saveFileDialog.FileName)}", "Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                OnLogMessage($"Log dışa aktarılırken hata oluştu: {ex.Message}");
                System.Windows.MessageBox.Show($"Log dışa aktarılırken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }



        private string GetPeriodNameFromFiles(List<string> files)
        {
            try
            {
                if (files == null || !files.Any())
                    return "Bilinmeyen Dönem";
                
                // İlk dosyadan dönem adını al
                var firstFile = files.First();
                var directoryPath = Path.GetDirectoryName(firstFile);
                
                if (string.IsNullOrEmpty(directoryPath))
                    return "Bilinmeyen Dönem";
                
                // Klasör adını al (dönem adı)
                var periodName = Path.GetFileName(directoryPath);
                
                // Eğer klasör adı boşsa veya null ise
                if (string.IsNullOrEmpty(periodName))
                    return "Bilinmeyen Dönem";
                
                return periodName;
            }
            catch (Exception ex)
            {
                OnLogMessage($"❌ Dönem adı alınırken hata: {ex.Message}");
                return "Bilinmeyen Dönem";
            }
        }

        #region SMS Alıcıları Yönetimi

        private async void btnRefreshSmsRecipients_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                OnLogMessage("📱 SMS alıcıları yenileniyor...");
                await LoadSmsRecipients();
            }
            catch (Exception ex)
            {
                OnLogMessage($"❌ SMS alıcıları yenilenirken hata: {ex.Message}");
            }
        }

        private async Task LoadSmsRecipients()
        {
            try
            {
                // Seçili dönemleri al
                var selectedPeriods = GetSelectedPeriodsFromFiles();
                
                if (!selectedPeriods.Any())
                {
                    OnLogMessage("⚠️ Hiç dönem seçilmemiş. Önce klasör analizi yapın.");
                    return;
                }

                OnLogMessage($"📱 {selectedPeriods.Count} dönem için SMS alıcıları yükleniyor...");
                
                _allSmsRecipients.Clear();
                
                foreach (var period in selectedPeriods)
                {
                    try
                    {
                        OnLogMessage($"📱 {period.Name} dönemi için SMS alıcıları alınıyor...");
                        
                        var recipients = await _smsService.GetSmsRecipientsForPeriodAsync(period);
                        _allSmsRecipients.AddRange(recipients);
                        
                        OnLogMessage($"✅ {period.Name} dönemi için {recipients.Count} SMS alıcısı bulundu.");
                    }
                    catch (Exception ex)
                    {
                        OnLogMessage($"❌ {period.Name} dönemi için SMS alıcıları alınırken hata: {ex.Message}");
                    }
                }

                // Tekrar eden alıcıları ayır
                SeparateUniqueAndDuplicateRecipients();
                
                // UI'ı güncelle
                UpdateSmsRecipientsUI();
                
                OnLogMessage($"✅ SMS alıcıları yüklendi. Toplam: {_allSmsRecipients.Count}, Benzersiz: {_uniqueSmsRecipients.Count}, Tekrar: {_duplicateSmsRecipients.Count}");
            }
            catch (Exception ex)
            {
                OnLogMessage($"❌ SMS alıcıları yüklenirken hata: {ex.Message}");
            }
        }

        private void SeparateUniqueAndDuplicateRecipients()
        {
            try
            {
                _uniqueSmsRecipients.Clear();
                _duplicateSmsRecipients.Clear();
                
                var seenCombinations = new Dictionary<string, int>();

                foreach (var recipient in _allSmsRecipients)
                {
                    // İsim ve telefon numarasını birleştirerek benzersiz bir anahtar oluştur
                    var key = $"{recipient.Name.Trim().ToLowerInvariant()}_{recipient.Phone.Trim()}";
                    
                    if (!seenCombinations.ContainsKey(key))
                    {
                        seenCombinations[key] = 1;
                        recipient.IsDuplicate = false;
                        _uniqueSmsRecipients.Add(recipient);
                    }
                    else
                    {
                        seenCombinations[key]++;
                        recipient.IsDuplicate = true;
                        recipient.PeriodName = $"{recipient.PeriodName} (Tekrar #{seenCombinations[key]})";
                        _duplicateSmsRecipients.Add(recipient);
                    }
                }
            }
            catch (Exception ex)
            {
                OnLogMessage($"❌ Tekrar eden alıcıları ayırırken hata: {ex.Message}");
            }
        }

        private void UpdateSmsRecipientsUI()
        {
            try
            {
                // ListView'ları güncelle
                lstUniqueRecipients.ItemsSource = _uniqueSmsRecipients;
                lstDuplicateRecipients.ItemsSource = _duplicateSmsRecipients;
                
                // İstatistikleri güncelle
                txtUniqueRecipientsCount.Text = $"{_uniqueSmsRecipients.Count} alıcı";
                txtDuplicateRecipientsCount.Text = $"{_duplicateSmsRecipients.Count} alıcı";
                
                // Genel istatistikleri güncelle
                var totalRecipients = _allSmsRecipients.Count;
                var selectedRecipients = _allSmsRecipients.Count(r => r.IsSelected);
                
                txtTotalSmsRecipients.Text = $"Toplam: {totalRecipients}";
                txtSelectedSmsRecipients.Text = $"Seçili: {selectedRecipients}";
                txtUniqueSmsRecipients.Text = $"Benzersiz: {_uniqueSmsRecipients.Count}";
                txtDuplicateSmsRecipients.Text = $"Tekrar: {_duplicateSmsRecipients.Count}";
            }
            catch (Exception ex)
            {
                OnLogMessage($"❌ SMS alıcıları UI güncellenirken hata: {ex.Message}");
            }
        }

        private List<PeriodInfo> GetSelectedPeriodsFromFiles()
        {
            try
            {
                var periods = new List<PeriodInfo>();
                
                foreach (var kvp in _keywordFileMappings)
                {
                    var files = kvp.Value;
                    if (files.Any())
                    {
                        var periodName = GetPeriodNameFromFiles(files);
                        var periodId = GeneratePeriodId(periodName);
                        
                        periods.Add(new PeriodInfo
                        {
                            Id = periodId,
                            Name = periodName
                        });
                    }
                }
                
                return periods;
            }
            catch (Exception ex)
            {
                OnLogMessage($"❌ Seçili dönemler alınırken hata: {ex.Message}");
                return new List<PeriodInfo>();
            }
        }

        private string GeneratePeriodId(string periodName)
        {
            // Basit bir ID oluştur
            return periodName.Replace(" ", "_").Replace("-", "_").ToLowerInvariant();
        }



        #endregion

        #region Mail Geçmişi Kontrolü

        private void LoadSentFilesCache()
        {
            try
            {
                _sentFilesCache.Clear();
                var mailHistory = _mailHistoryService.GetAllMailHistory();
                
                foreach (var mail in mailHistory)
                {
                    if (mail.Attachments != null)
                    {
                        foreach (var attachment in mail.Attachments)
                        {
                            var normalizedPath = NormalizeFilePath(attachment);
                            _sentFilesCache[normalizedPath] = true;
                        }
                    }
                }
                
                OnLogMessage($"📧 Mail geçmişi yüklendi: {_sentFilesCache.Count} dosya daha önce gönderilmiş");
            }
            catch (Exception ex)
            {
                OnLogMessage($"❌ Mail geçmişi yüklenirken hata: {ex.Message}");
            }
        }

        private string NormalizeFilePath(string filePath)
        {
            try
            {
                // Dosya yolunu normalize et
                var normalizedPath = Path.GetFullPath(filePath).ToLowerInvariant();
                return normalizedPath;
            }
            catch
            {
                return filePath.ToLowerInvariant();
            }
        }

        private bool IsFilePreviouslySent(string filePath)
        {
            try
            {
                var normalizedPath = NormalizeFilePath(filePath);
                return _sentFilesCache.ContainsKey(normalizedPath);
            }
            catch
            {
                return false;
            }
        }

        private void UpdateKeywordSelectionPanelWithSentStatus()
        {
            try
            {
                // Mail geçmişini yükle
                LoadSentFilesCache();
                
                // Keyword seçim panelini yeniden oluştur
                CreateKeywordSelectionPanel();
                
                OnLogMessage("📧 Dosya gönderim durumları güncellendi");
            }
            catch (Exception ex)
            {
                OnLogMessage($"❌ Dosya gönderim durumları güncellenirken hata: {ex.Message}");
            }
        }

        #endregion
    }
} 