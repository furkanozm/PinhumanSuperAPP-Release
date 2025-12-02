using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace WebScraper
{
    public partial class PeriodSelectionModal : Window
    {
        public List<(string Value, string Text)> SelectedPeriods { get; private set; }
        private List<PeriodItem> _allPeriodItems;
        private List<PeriodItem> _filteredPeriodItems;

        public PeriodSelectionModal(List<(string Value, string Text)> periods)
        {
            InitializeComponent();
            
            // Dönemleri PeriodItem listesine çevir
            _allPeriodItems = periods.Select(p => new PeriodItem 
            { 
                Value = p.Value, 
                Text = p.Text, 
                IsSelected = false // Varsayılan olarak hiçbiri seçili değil
            }).ToList();
            
            // Ay filtresini ayarla
            SetupMonthFilter();
            
            // İlk filtreleme (tüm dönemler)
            _filteredPeriodItems = new List<PeriodItem>(_allPeriodItems);
            
            // UI'yi güncelle
            PeriodsList.ItemsSource = _filteredPeriodItems;
            PeriodCountText.Text = $"{_allPeriodItems.Count} adet";
            UpdateSelectedCount();
            UpdateFilteredCount();
        }

        private void SetupMonthFilter()
        {
            // Event handler'ı geçici olarak devre dışı bırak
            MonthFilterComboBox.SelectionChanged -= MonthFilterComboBox_SelectionChanged;
            
            // Mevcut ayları bul
            var months = new List<string>();
            months.Add("Tüm Aylar");
            
            foreach (var period in _allPeriodItems)
            {
                var month = ExtractMonthFromPeriod(period.Text);
                if (!string.IsNullOrEmpty(month) && !months.Contains(month))
                {
                    months.Add(month);
                }
            }
            
            // Debug için konsola yazdır
            System.Diagnostics.Debug.WriteLine($"Bulunan aylar: {string.Join(", ", months)}");
            
            // ComboBox'ı doldur
            MonthFilterComboBox.ItemsSource = months;
            
            // İşlem yapılan ayı otomatik seç (bugünün ayı)
            var currentMonth = GetCurrentMonthName();
            var currentMonthIndex = months.IndexOf(currentMonth);
            
            System.Diagnostics.Debug.WriteLine($"Mevcut ay: {currentMonth}, Index: {currentMonthIndex}");
            
            if (currentMonthIndex >= 0)
            {
                MonthFilterComboBox.SelectedIndex = currentMonthIndex;
                System.Diagnostics.Debug.WriteLine($"Mevcut ay seçildi: {currentMonth}");
            }
            else
            {
                MonthFilterComboBox.SelectedIndex = 0; // Tüm Aylar
                System.Diagnostics.Debug.WriteLine($"Mevcut ay bulunamadı, 'Tüm Aylar' seçildi");
            }
            
            // Event handler'ı tekrar etkinleştir
            MonthFilterComboBox.SelectionChanged += MonthFilterComboBox_SelectionChanged;
        }
        
        private string ExtractMonthFromPeriod(string periodText)
        {
            // Dönem metninden ay adını çıkar - daha geniş pattern'ler
            var monthPatterns = new[]
            {
                @"(\d{1,2}-\d{1,2}\s+)(Oca|Şub|Mar|Nis|May|Haz|Tem|Ağu|Eyl|Eki|Kas|Ara)(\s+\d{4})",
                @"(\d{1,2}-\d{1,2}\s+)(Ocak|Şubat|Mart|Nisan|Mayıs|Haziran|Temmuz|Ağustos|Eylül|Ekim|Kasım|Aralık)(\s+\d{4})",
                @"(\d{4})-(\d{2})", // 2025-01 formatı
                @"(\d{4})/(\d{2})", // 2025/01 formatı
                @"(\d{1,2})/(\d{1,2})/(\d{4})", // 01/15/2025 formatı
                @"(\d{1,2})-(\d{1,2})-(\d{4})" // 01-15-2025 formatı
            };
            
            foreach (var pattern in monthPatterns)
            {
                var match = System.Text.RegularExpressions.Regex.Match(periodText, pattern);
                if (match.Success)
                {
                    // Farklı formatlar için ay çıkarma
                    if (pattern.Contains(@"\d{4}") && pattern.Contains(@"\d{2}"))
                    {
                        // Yıl-ay formatı (2025-01)
                        if (match.Groups.Count >= 3)
                        {
                            var monthNum = int.Parse(match.Groups[2].Value);
                            var monthNames = new[]
                            {
                                "Ocak", "Şubat", "Mart", "Nisan", "Mayıs", "Haziran",
                                "Temmuz", "Ağustos", "Eylül", "Ekim", "Kasım", "Aralık"
                            };
                            return monthNames[monthNum - 1];
                        }
                    }
                    else if (match.Groups.Count >= 2)
                    {
                        // Normal ay adı formatı
                        return match.Groups[2].Value;
                    }
                }
            }
            
            return "";
        }
        
        private string GetCurrentMonthName()
        {
            var currentMonth = DateTime.Now.Month;
            var monthNames = new[]
            {
                "Ocak", "Şubat", "Mart", "Nisan", "Mayıs", "Haziran",
                "Temmuz", "Ağustos", "Eylül", "Ekim", "Kasım", "Aralık"
            };
            
            var currentMonthName = monthNames[currentMonth - 1];
            
            // Debug için konsola yazdır
            System.Diagnostics.Debug.WriteLine($"Mevcut ay: {currentMonthName}");
            
            return currentMonthName;
        }
        
        private void UpdateSelectedCount()
        {
            var selectedCount = _allPeriodItems.Count(p => p.IsSelected);
            SelectedCountText.Text = $"{selectedCount} dönem seçildi";
            
            // Renk değiştir
            if (selectedCount == 0)
            {
                SelectedCountText.Foreground = System.Windows.Media.Brushes.Red;
            }
            else if (selectedCount == _allPeriodItems.Count)
            {
                SelectedCountText.Foreground = System.Windows.Media.Brushes.Green;
            }
            else
            {
                SelectedCountText.Foreground = System.Windows.Media.Brushes.Orange;
            }
        }
        
        private void UpdateFilteredCount()
        {
            var filteredCount = _filteredPeriodItems.Count;
            FilteredCountText.Text = $"{filteredCount} dönem bulundu";
        }

        private void SelectAllButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _filteredPeriodItems)
            {
                item.IsSelected = true;
            }
            UpdateSelectedCount();
        }

        private void DeselectAllButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _filteredPeriodItems)
            {
                item.IsSelected = false;
            }
            UpdateSelectedCount();
        }
        
        private void MonthFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyFilters();
        }
        
        private void CompanyFilterTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilters();
        }
        
        private void RegionFilterTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilters();
        }
        
        private void ApplyFilters()
        {
            var filteredItems = new List<PeriodItem>(_allPeriodItems);
            
            // Ay filtresi
            if (MonthFilterComboBox.SelectedItem != null)
            {
                var selectedMonth = MonthFilterComboBox.SelectedItem.ToString();
                if (selectedMonth != "Tüm Aylar")
                {
                    filteredItems = filteredItems
                        .Where(p => ExtractMonthFromPeriod(p.Text) == selectedMonth)
                        .ToList();
                }
            }
            
            // Firma filtresi
            var companyFilter = CompanyFilterTextBox.Text?.Trim();
            if (!string.IsNullOrEmpty(companyFilter))
            {
                filteredItems = filteredItems
                    .Where(p => ExtractCompanyFromPeriod(p.Text).Contains(companyFilter, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
            
            // Bölge filtresi
            var regionFilter = RegionFilterTextBox.Text?.Trim();
            if (!string.IsNullOrEmpty(regionFilter))
            {
                filteredItems = filteredItems
                    .Where(p => ExtractRegionFromPeriod(p.Text).Contains(regionFilter, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
            
            _filteredPeriodItems = filteredItems;
            
            // UI'yi güncelle - sadece filtreleme sonuçlarını göster
            PeriodsList.ItemsSource = null; // Önce temizle
            PeriodsList.ItemsSource = _filteredPeriodItems; // Sonra yeniden ata
            UpdateFilteredCount();
        }
        
        private string ExtractCompanyFromPeriod(string periodText)
        {
            try
            {
                // Parantez içindeki firma adını çıkar
                var match = System.Text.RegularExpressions.Regex.Match(periodText, @"\(([^)]+)\)");
                if (match.Success)
                {
                    return match.Groups[1].Value.Trim();
                }
                return "";
            }
            catch
            {
                return "";
            }
        }
        
        private string ExtractRegionFromPeriod(string periodText)
        {
            try
            {
                // Parantez içindeki metinden bölge adını çıkar
                var companyText = ExtractCompanyFromPeriod(periodText);
                if (!string.IsNullOrEmpty(companyText))
                {
                    // Türkiye şehir isimleri listesi
                    var turkishCities = new[]
                    {
                        "ADANA", "ADIYAMAN", "AFYONKARAHİSAR", "AĞRI", "AMASYA", "ANKARA", "ANTALYA", "ARTVİN", "AYDIN", "BALIKESİR",
                        "BİLECİK", "BİNGÖL", "BİTLİS", "BOLU", "BURDUR", "BURSA", "ÇANAKKALE", "ÇANKIRI", "ÇORUM", "DENİZLİ",
                        "DİYARBAKIR", "EDİRNE", "ELAZIĞ", "ERZİNCAN", "ERZURUM", "ESKİŞEHİR", "GAZİANTEP", "GİRESUN", "GÜMÜŞHANE", "HAKKARİ",
                        "HATAY", "ISPARTA", "MERSİN", "İSTANBUL", "İZMİR", "KARS", "KASTAMONU", "KAYSERİ", "KIRKLARELİ", "KIRŞEHİR",
                        "KOCAELİ", "KONYA", "KÜTAHYA", "MALATYA", "MANİSA", "KAHRAMANMARAŞ", "MARDİN", "MUĞLA", "MUŞ", "NEVŞEHİR",
                        "NİĞDE", "ORDU", "RİZE", "SAKARYA", "SAMSUN", "SİİRT", "SİNOP", "SİVAS", "TEKİRDAĞ", "TOKAT", "TRABZON",
                        "TUNCELİ", "ŞANLIURFA", "UŞAK", "VAN", "YOZGAT", "ZONGULDAK", "AKSARAY", "BAYBURT", "KARAMAN", "KIRIKKALE",
                        "BATMAN", "ŞIRNAK", "BARTIN", "ARDAHAN", "IĞDIR", "YALOVA", "KARABÜK", "KİLİS", "OSMANİYE", "DÜZCE", "YENİŞEHİR"
                    };
                    
                    // Debug için konsola yazdır
                    System.Diagnostics.Debug.WriteLine($"ExtractRegionFromPeriod: '{periodText}' -> Company: '{companyText}'");
                    
                    // Metni büyük harfe çevir ve kelimelere böl
                    var upperText = companyText.ToUpper();
                    var words = upperText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    
                    System.Diagnostics.Debug.WriteLine($"Words: [{string.Join(", ", words)}]");
                    
                    // Son kelimeden başlayarak şehir ismi ara
                    for (int i = words.Length - 1; i >= 0; i--)
                    {
                        System.Diagnostics.Debug.WriteLine($"Checking word '{words[i]}' against cities list...");
                        if (turkishCities.Contains(words[i]))
                        {
                            System.Diagnostics.Debug.WriteLine($"Found city: {words[i]}");
                            return words[i]; // Şehir ismini bulduk
                        }
                    }
                    
                    // Şehir ismi bulunamazsa son kelimeyi döndür
                    if (words.Length > 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"No city found, returning last word: {words[words.Length - 1]}");
                        return words[words.Length - 1];
                    }
                }
                return "";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ExtractRegionFromPeriod error: {ex.Message}");
                return "";
            }
        }

        private void PeriodCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            UpdateSelectedCount();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedCount = _allPeriodItems.Count(p => p.IsSelected);
            
            if (selectedCount == 0)
            {
                MessageBox.Show("Lütfen en az bir dönem seçin!", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            // Seçilen dönemleri döndür
            SelectedPeriods = _allPeriodItems
                .Where(p => p.IsSelected)
                .Select(p => (p.Value, p.Text))
                .ToList();
            
            // Modal'ı kapatma, sadece DialogResult'ı true yap
            DialogResult = true;
            // Close(); // Bu satırı kaldırdık - modal kapanmayacak
        }
    }

    public class PeriodItem : System.ComponentModel.INotifyPropertyChanged
    {
        private bool _isSelected;
        private string _value;
        private string _text;

        public string Value
        {
            get => _value;
            set
            {
                _value = value;
                OnPropertyChanged(nameof(Value));
            }
        }

        public string Text
        {
            get => _text;
            set
            {
                _text = value;
                OnPropertyChanged(nameof(Text));
            }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                OnPropertyChanged(nameof(IsSelected));
            }
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
        }
    }
} 