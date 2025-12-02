using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using OfficeOpenXml;

namespace WebScraper
{
    public partial class CompanyManagementModal : Window, INotifyPropertyChanged
    {
        public static IReadOnlyList<string> SpecialOvertimeColumnOptions { get; } = new List<string>
        {
            "B01 %50 Fazla Mesai",
            "B02 %100 Fazla Mesai",
            "B03 %150 Fazla Mesai"
        };

        // Yan ödemeler listesi (AH'dan CH'ye kadar sırasıyla)
        public static IReadOnlyList<string> ConditionalEarningsColumnOptions { get; } = new List<string>
        {
            "C01 İkramiye",
            "C02 Yakacak Yardımı",
            "C03 Bayram Parası",
            "C05 Aile Yardımı",
            "C06 Yemek Parası NET",
            "C07 Harcırah",
            "C10 Abonman",
            "C11 Prim Destek",
            "C12 Prim Brüt",
            "C13 Yol Net",
            "C14 Yemek Net",
            "C15 Yıllık İzin Ödemesi",
            "C16 Bayram Harçlığı",
            "C17 Yemek Brüt",
            "C18 Doğum Yardımı",
            "C19 Bayram Harçlığı Brüt",
            "C20 Ücret Farkı",
            "C21 Yılbaşı İkramiyesi",
            "C22 Tahirova Fm Hak.",
            "C23 Yıllık İzin Öde.Brüt",
            "C24 Migros Money Kart",
            "C25 EĞİTİM YARDIMI"
        };

        // Kesintiler listesi
        public static IReadOnlyList<string> ConditionalDeductionsColumnOptions { get; } = new List<string>
        {
            "D01 Özel Kesinti",
            "D06 AVANS KES.",
            "D07 Trafik Cezası",
            "D08 Tetkik Kesintisi",
            "D09 Yıllık İzin Ö.Kes.",
            "D11 Avans Kes.2.",
            "D12 Doğum Yar. Kes.",
            "D15 Kkm Kesintisi",
            "D16 İcra Kesintisi",
            "D17 Eksik Saat Kesintisi",
            "D18 Yemek kesintisi",
            "D19 Setcard Kesinti",
            "D25 SAĞLIK RAPORU KSNTİ"
        };

        // Koşul tipleri
        public static IReadOnlyList<string> ConditionTypeOptions { get; } = new List<string>
        {
            "Koşulsuz",
            "Devamsızlık Günü",
            "Eksik Gün",
            "Eksik Saat",
            "Fazla Mesai Saati",
            "Tatil Günü"
        };

        // Koşul operatörleri
        public static IReadOnlyList<string> ConditionOperatorOptions { get; } = new List<string>
        {
            ">",
            ">=",
            "<",
            "<=",
            "==",
            "!="
        };

        // Koşul değeri kaynakları (dinamik değerler) - ARTIK KULLANILMIYOR
        // Sadece geriye dönük uyumluluk için tutulabilir veya tamamen kaldırılabilir.
        // public static IReadOnlyList<string> ConditionValueSourceOptions { get; } = ...

        // Değer tipleri
        public static IReadOnlyList<string> ValueTypeOptions { get; } = new List<string>
        {
            "Sabit",
            "Fazla Mesai Saati x Tutar",
            "Eksik Saat x Tutar",
            "Eksik Gün x Tutar",
            "Devamsızlık Günü x Tutar"
        };

        private CompanyConfig _currentCompany;
        private PDKSConfig _parentConfig;
        private bool _isNewCompany;
        private readonly PDKSConfigService _configService = new PDKSConfigService();
        private readonly CalendarService _calendarService = new CalendarService();
        private readonly DataTemplateService _dataTemplateService = new DataTemplateService();
        private readonly CultureInfo _culture = new CultureInfo("tr-TR");
        private readonly ObservableCollection<HolidaySelectionItem> _holidaySelectionItems = new ObservableCollection<HolidaySelectionItem>();
        private List<int> _holidayYears = new List<int>();
        private int _selectedHolidayYear;
        private string? _templatesLoadedForCompanyCode;
        private bool _legacyPromptHandled;
        private static readonly Dictionary<string, string> SpecialOvertimeColumnLetterMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "B01 %50 Fazla Mesai", "Y" },
            { "B02 %100 Fazla Mesai", "Z" },
            { "B03 %150 Fazla Mesai", "AA" }
        };

        // Yan ödemeler için sütun harfleri (AH'dan başlayarak)
        private static readonly Dictionary<string, string> ConditionalEarningsColumnLetterMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "C01 İkramiye", "AH" },
            { "C02 Yakacak Yardımı", "AI" },
            { "C03 Bayram Parası", "AJ" },
            { "C05 Aile Yardımı", "AK" },
            { "C06 Yemek Parası NET", "AL" },
            { "C07 Harcırah", "AM" },
            { "C10 Abonman", "AN" },
            { "C11 Prim Destek", "AO" },
            { "C12 Prim Brüt", "AP" },
            { "C13 Yol Net", "AQ" },
            { "C14 Yemek Net", "AR" },
            { "C15 Yıllık İzin Ödemesi", "AS" },
            { "C16 Bayram Harçlığı", "AT" },
            { "C17 Yemek Brüt", "AU" },
            { "C18 Doğum Yardımı", "AV" },
            { "C19 Bayram Harçlığı Brüt", "AW" },
            { "C20 Ücret Farkı", "AX" },
            { "C21 Yılbaşı İkramiyesi", "AY" },
            { "C22 Tahirova Fm Hak.", "AZ" },
            { "C23 Yıllık İzin Öde.Brüt", "BA" },
            { "C24 Migros Money Kart", "BB" },
            { "C25 EĞİTİM YARDIMI", "BC" },
            // Kesintiler
            { "D01 Özel Kesinti", "CI" },
            { "D06 AVANS KES.", "CJ" },
            { "D07 Trafik Cezası", "CK" },
            { "D08 Tetkik Kesintisi", "CL" },
            { "D09 Yıllık İzin Ö.Kes.", "CM" },
            { "D11 Avans Kes.2.", "CO" },
            { "D12 Doğum Yar. Kes.", "CP" },
            { "D15 Kkm Kesintisi", "CS" },
            { "D16 İcra Kesintisi", "CT" },
            { "D17 Eksik Saat Kesintisi", "CU" },
            { "D18 Yemek kesintisi", "CV" },
            { "D19 Setcard Kesinti", "CW" },
            { "D25 SAĞLIK RAPORU KSNTİ", "DC" }
        };

        /// <summary>
        /// Excel sütun numarasını sütun harfine çevirir (1 -> A, 27 -> AA, 34 -> AH, vb.)
        /// </summary>
        private static string GetExcelColumnLetter(int columnNumber)
        {
            string columnLetter = string.Empty;
            while (columnNumber > 0)
            {
                columnNumber--;
                columnLetter = (char)('A' + columnNumber % 26) + columnLetter;
                columnNumber /= 26;
            }
            return columnLetter;
        }

        /// <summary>
        /// Yan ödeme sütun adına göre Excel sütun harfini döndürür
        /// </summary>
        private string GetLetterForConditionalEarningsColumn(string columnName)
        {
            if (string.IsNullOrWhiteSpace(columnName))
            {
                return string.Empty;
            }

            // Önce sözlükten kontrol et
            if (ConditionalEarningsColumnLetterMap.TryGetValue(columnName, out var letter))
            {
                return letter;
            }

            // Manuel sütunlardan kontrol et
            if (CurrentCompany?.ConditionalEarningsSettings?.ManualColumns != null)
            {
                // "(Manuel)" etiketini kaldırarak kontrol et
                var cleanName = columnName.Replace(" (Manuel)", "").Trim();
                var manualColumn = CurrentCompany.ConditionalEarningsSettings.ManualColumns
                    .FirstOrDefault(m => m.ColumnName.Equals(cleanName, StringComparison.OrdinalIgnoreCase));
                
                if (manualColumn != null)
                {
                    return manualColumn.ColumnLetter;
                }
            }

            return string.Empty;
        }

        public CompanyConfig CurrentCompany
        {
            get => _currentCompany;
            set
            {
                _currentCompany = value;
                OnPropertyChanged(nameof(CurrentCompany));
                OnPropertyChanged(nameof(HorizontalTemplateShiftRuleInfo));
                OnPropertyChanged(nameof(TemplateModeDisplay));
            }
        }

        public ObservableCollection<HolidaySelectionItem> HolidaySelectionItems => _holidaySelectionItems;

        public ObservableCollection<DataTemplate> DataTemplates { get; } = new ObservableCollection<DataTemplate>();

        /// <summary>
        /// Yatay şablonlar için kullanılacak vardiya kuralı bilgisi
        /// </summary>
        public string HorizontalTemplateShiftRuleInfo
        {
            get
            {
                if (CurrentCompany?.ShiftRuleConfigs == null || CurrentCompany.ShiftRuleConfigs.Count == 0)
                {
                    return "Vardiya kuralı tanımlı değil";
                }

                var horizontalSettings = CurrentCompany.HorizontalTemplateSettings;
                ShiftRuleConfig? selectedRule = null;

                // Seçili vardiya grubunu bul
                if (horizontalSettings != null && !string.IsNullOrWhiteSpace(horizontalSettings.SelectedShiftRuleGroupName))
                {
                    selectedRule = CurrentCompany.ShiftRuleConfigs.FirstOrDefault(c =>
                        c.GroupName.Equals(horizontalSettings.SelectedShiftRuleGroupName, StringComparison.OrdinalIgnoreCase));
                }

                // Seçili grup bulunamadıysa ilk grubu kullan
                if (selectedRule == null)
                {
                    selectedRule = CurrentCompany.ShiftRuleConfigs.FirstOrDefault();
                }

                if (selectedRule == null)
                {
                    return "Vardiya kuralı bulunamadı";
                }

                // Üst üste gün sayısı override kontrolü
                int consecutiveDays = selectedRule.ConsecutiveDaysForVacation;
                if (horizontalSettings != null && horizontalSettings.OverrideConsecutiveDaysForVacation > 0)
                {
                    consecutiveDays = horizontalSettings.OverrideConsecutiveDaysForVacation;
                }

                return $"Kullanılacak kural: {selectedRule.GroupName} (Üst üste {consecutiveDays} gün çalışınca {selectedRule.VacationDays} gün tatil)";
            }
        }

        public string TemplateModeDisplay
        {
            get
            {
                if (CurrentCompany?.HorizontalTemplateSettings?.ApplyRulesWithoutShift == true)
                {
                    return "Şablon Modu: Yatay (Vardiyasız)";
                }

                return "Şablon Modu: Giriş-Çıkış (Vardiyalı)";
            }
        }

        public List<int> HolidayYears
        {
            get => _holidayYears;
            private set
            {
                _holidayYears = value ?? new List<int>();
                OnPropertyChanged(nameof(HolidayYears));
            }
        }

        public int SelectedHolidayYear
        {
            get => _selectedHolidayYear;
            set
            {
                if (_selectedHolidayYear == value)
                {
                    return;
                }

                _selectedHolidayYear = value;
                OnPropertyChanged(nameof(SelectedHolidayYear));

                if (value > 0)
                {
                    LoadHolidaySelectionForYear(value);
                }
                else
                {
                    foreach (var item in _holidaySelectionItems)
                    {
                        item.PropertyChanged -= HolidaySelectionItem_PropertyChanged;
                    }
                    _holidaySelectionItems.Clear();
                }
            }
        }

        public CompanyManagementModal(PDKSConfig parentConfig, CompanyConfig? company = null)
        {
            InitializeComponent();
            _parentConfig = parentConfig;

            _isNewCompany = company == null;
            Loaded += CompanyManagementModal_Loaded;
            Unloaded += CompanyManagementModal_Unloaded;

            if (_isNewCompany)
            {
                // Yeni firma oluştur
                CurrentCompany = new CompanyConfig
                {
                    CompanyCode = "",
                    CompanyName = "",
                    LogoPath = "",
                    ErpTemplatePath = "",
                    MonthDays = DateTime.DaysInMonth(DateTime.Now.Year, DateTime.Now.Month),
                    PayrollYear = DateTime.Now.Year,
                    PayrollMonth = DateTime.Now.Month,
                    Description = "",
                    ShiftRuleConfigs = new List<ShiftRuleConfig>(),
                    SpecialOvertimeSettings = new SpecialOvertimeSettings(),
                    ConditionalEarningsSettings = new ConditionalEarningsSettings()
                };
                UpdateSpecialOvertimeLettersFromNames();
                Title = "Yeni Firma Ekle";
            }
            else
            {
                // Mevcut firmayı düzenle - silme butonunu göster
                if (btnDelete != null)
                {
                    btnDelete.Visibility = Visibility.Visible;
                }
                Title = $"{company.CompanyName} - Firma Düzenle";

                CurrentCompany = new CompanyConfig
                {
                    CompanyCode = company.CompanyCode,
                    CompanyName = company.CompanyName,
                    LogoPath = company.LogoPath ?? "",
                    ErpTemplatePath = company.ErpTemplatePath,
                    MonthDays = company.MonthDays,
                    PayrollYear = company.PayrollYear == 0 ? DateTime.Now.Year : company.PayrollYear,
                    PayrollMonth = company.PayrollMonth == 0 ? DateTime.Now.Month : company.PayrollMonth,
                    Description = company.Description,
                    ShiftRuleConfigs = company.ShiftRuleConfigs?.Select(c => new ShiftRuleConfig
                    {
                        GroupName = c.GroupName,
                        ShiftPatterns = new List<string>(c.ShiftPatterns),
                        ShiftPatternMappings = new List<string>(c.ShiftPatternMappings ?? new List<string>()),
                        DefaultStartTime = c.DefaultStartTime,
                        DefaultEndTime = c.DefaultEndTime,
                        StandardHours = c.StandardHours,
                        BreakHours = c.BreakHours,
                        ConsecutiveDaysForVacation = c.ConsecutiveDaysForVacation,
                        VacationDays = c.VacationDays,
                        OvertimeRules = c.OvertimeRules?.Select(r => new OvertimeRule
                        {
                            StartTime = r.StartTime,
                            Rate = r.Rate,
                            Description = r.Description,
                            ColumnName = r.ColumnName,
                            DurationHours = r.DurationHours,
                            IsCatchAll = r.IsCatchAll
                        }).ToList() ?? new List<OvertimeRule>()
                    }).ToList() ?? new List<ShiftRuleConfig>(),
                    SpecialOvertimeSettings = company.SpecialOvertimeSettings != null
                        ? new SpecialOvertimeSettings
                        {
                            EarnedRestDayColumnName = company.SpecialOvertimeSettings.EarnedRestDayColumnName,
                            EarnedRestDayColumnLetter = company.SpecialOvertimeSettings.EarnedRestDayColumnLetter,
                            HolidayWorkColumnName = company.SpecialOvertimeSettings.HolidayWorkColumnName,
                            HolidayWorkColumnLetter = company.SpecialOvertimeSettings.HolidayWorkColumnLetter,
                            WeekendWorkColumnName = company.SpecialOvertimeSettings.WeekendWorkColumnName,
                            WeekendWorkColumnLetter = company.SpecialOvertimeSettings.WeekendWorkColumnLetter,
                            UseManualEarnedRestLetter = company.SpecialOvertimeSettings.UseManualEarnedRestLetter,
                            UseManualHolidayLetter = company.SpecialOvertimeSettings.UseManualHolidayLetter,
                            UseManualWeekendLetter = company.SpecialOvertimeSettings.UseManualWeekendLetter
                        }
                        : new SpecialOvertimeSettings(),
                    ActiveOfficialHolidayDates = company.ActiveOfficialHolidayDates != null
                        ? company.ActiveOfficialHolidayDates.ToDictionary(
                            kvp => kvp.Key,
                            kvp => kvp.Value != null ? new List<string>(kvp.Value) : new List<string>())
                        : new Dictionary<int, List<string>>(),
                    HorizontalTemplateSettings = company.HorizontalTemplateSettings != null
                        ? new HorizontalTemplateSettings
                        {
                            ApplyRulesWithoutShift = company.HorizontalTemplateSettings.ApplyRulesWithoutShift,
                            MinConsecutiveDaysHours = company.HorizontalTemplateSettings.MinConsecutiveDaysHours,
                            SelectedShiftRuleGroupName = company.HorizontalTemplateSettings.SelectedShiftRuleGroupName ?? string.Empty,
                            OverrideConsecutiveDaysForVacation = company.HorizontalTemplateSettings.OverrideConsecutiveDaysForVacation,
                            HasHolidaysInTemplate = company.HorizontalTemplateSettings.HasHolidaysInTemplate,
                            OfficialHolidayWorkIndicators = company.HorizontalTemplateSettings.OfficialHolidayWorkIndicators ?? "RT,R,T",
                            OfficialHolidayRestIndicators = company.HorizontalTemplateSettings.OfficialHolidayRestIndicators ?? "X,7.5"
                        }
                        : new HorizontalTemplateSettings(),
                                ConditionalEarningsSettings = company.ConditionalEarningsSettings != null
                        ? new ConditionalEarningsSettings
                        {
                            Rules = company.ConditionalEarningsSettings.Rules?.Select(r => new ConditionalEarningsRule
                            {
                                ConditionType = r.ConditionType ?? string.Empty,
                                ConditionOperator = r.ConditionOperator ?? ">",
                                ConditionValue = r.ConditionValue,
                                ConditionValueSource = r.ConditionValueSource ?? "Sabit",
                                TargetColumnName = r.TargetColumnName ?? string.Empty,
                                TargetColumnLetter = r.TargetColumnLetter ?? string.Empty,
                                EarningsValue = r.EarningsValue,
                                ValueType = r.ValueType ?? "Sabit",
                                StartDate = r.StartDate,
                                EndDate = r.EndDate,
                                IsEnabled = r.IsEnabled,
                                Description = r.Description ?? string.Empty
                            }).ToList() ?? new List<ConditionalEarningsRule>()
                        }
                        : new ConditionalEarningsSettings()
                };
                UpdateSpecialOvertimeLettersFromNames();
                Title = $"Firmayı Düzenle: {company.CompanyName}";
            }

            // DataContext olarak CurrentCompany kullanılır; böylece XAML'deki binding'ler firma alanlarına gider.
            DataContext = CurrentCompany;

            // Veri şablonlarını yükle
            LoadDataTemplates();

            InitializeHolidaySelections();
            UpdatePayrollSummary();
        }

        private void LoadDataTemplates(bool allowLegacyMigration = true)
        {
            DataTemplates.Clear();

            var companyCode = CurrentCompany?.CompanyCode?.Trim();
            if (string.IsNullOrWhiteSpace(companyCode))
            {
                _templatesLoadedForCompanyCode = null;
                return;
            }

            var templates = _dataTemplateService.LoadTemplates(companyCode);

            if (templates.Count == 0 && allowLegacyMigration && !_legacyPromptHandled && _dataTemplateService.HasLegacyTemplates())
            {
                _legacyPromptHandled = true;
                var legacyCount = _dataTemplateService.GetLegacyTemplateCount();
                if (legacyCount > 0)
                {
                    var result = MessageBox.Show(
                        $"Önceden tanımladığınız {legacyCount} veri şablonu bulundu.\n" +
                        $"Bu şablonları '{CurrentCompany.CompanyName}' firmasına taşımak ister misiniz?",
                        "Veri Şablonları",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        templates = _dataTemplateService.MigrateLegacyTemplates(companyCode);
                    }
                }
            }

            foreach (var template in templates)
            {
                if (template.SymbolHourMap == null)
                {
                    template.SymbolHourMap = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                }
                DataTemplates.Add(template);
            }

            _templatesLoadedForCompanyCode = companyCode;
        }

        private bool EnsureCompanyCodeForTemplates()
        {
            if (string.IsNullOrWhiteSpace(CurrentCompany?.CompanyCode))
            {
                MessageBox.Show("Veri şablonları oluşturmak için önce firma kodunu girmeniz gerekir.", "Uyarı",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            return true;
        }

        private void PersistDataTemplates()
        {
            var companyCode = CurrentCompany?.CompanyCode?.Trim();
            if (string.IsNullOrWhiteSpace(companyCode))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(_templatesLoadedForCompanyCode) &&
                !_templatesLoadedForCompanyCode.Equals(companyCode, StringComparison.OrdinalIgnoreCase))
            {
                _dataTemplateService.RenameCompanyTemplates(_templatesLoadedForCompanyCode, companyCode);
                _templatesLoadedForCompanyCode = companyCode;
            }

            _dataTemplateService.SaveTemplates(companyCode, DataTemplates.ToList());
            _templatesLoadedForCompanyCode = companyCode;
        }

        private void btnAddShiftGroup_Click(object sender, RoutedEventArgs e)
        {
            var shiftGroupModal = new ShiftRuleGroupModal(CurrentCompany);
            if (shiftGroupModal.ShowDialog() == true)
            {
                // Yeni grup eklendi, listeyi yenile
                lvShiftGroups.ItemsSource = null;
                lvShiftGroups.ItemsSource = CurrentCompany.ShiftRuleConfigs;
                OnPropertyChanged(nameof(CurrentCompany));
            }
        }

        private void btnEditShiftGroup_Click(object sender, RoutedEventArgs e)
        {
            if (lvShiftGroups.SelectedItem is ShiftRuleConfig selectedGroup)
            {
                var shiftGroupModal = new ShiftRuleGroupModal(CurrentCompany, selectedGroup);
                if (shiftGroupModal.ShowDialog() == true)
                {
                    // Grup güncellendi, listeyi yenile
                    lvShiftGroups.ItemsSource = null;
                    lvShiftGroups.ItemsSource = CurrentCompany.ShiftRuleConfigs;
                    OnPropertyChanged(nameof(CurrentCompany));
                }
            }
            else
            {
                MessageBox.Show("Lütfen düzenlemek istediğiniz grubu seçin.", "Uyarı",
                              MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void btnDeleteShiftGroup_Click(object sender, RoutedEventArgs e)
        {
            if (lvShiftGroups.SelectedItem is ShiftRuleConfig selectedGroup)
            {
                var result = MessageBox.Show(
                    $"'{selectedGroup.GroupName}' grubunu silmek istediğinizden emin misiniz?\n\nBu işlem geri alınamaz.",
                    "Grup Silme Onayı",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    CurrentCompany.ShiftRuleConfigs.Remove(selectedGroup);
                    lvShiftGroups.ItemsSource = null;
                    lvShiftGroups.ItemsSource = CurrentCompany.ShiftRuleConfigs;
                    OnPropertyChanged(nameof(CurrentCompany));
                }
            }
            else
            {
                MessageBox.Show("Lütfen silmek istediğiniz grubu seçin.", "Uyarı",
                              MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void btnSave_Click(object sender, RoutedEventArgs e)
        {
            // Validasyon
            EnsureSpecialOvertimeSettings();
            NormalizeSpecialOvertimeSettings();

            if (string.IsNullOrWhiteSpace(CurrentCompany.CompanyCode))
            {
                MessageBox.Show("Firma kodu zorunludur.", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (string.IsNullOrWhiteSpace(CurrentCompany.CompanyName))
            {
                MessageBox.Show("Firma adı zorunludur.", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Firma kodu benzersiz mi kontrol et
            if (_isNewCompany)
            {
                var existingCompany = _parentConfig.CompanyConfigs.FirstOrDefault(c => c.CompanyCode == CurrentCompany.CompanyCode);
                if (existingCompany != null)
                {
                    MessageBox.Show("Bu firma kodu zaten kullanılıyor.", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Yeni firmayı ekle
                _parentConfig.CompanyConfigs.Add(CurrentCompany);
            }
            else
            {
                // Mevcut firmayı güncelle
                var existingCompany = _parentConfig.CompanyConfigs.FirstOrDefault(c => c.CompanyCode == CurrentCompany.CompanyCode);
                if (existingCompany != null)
                {
                    // Firma bilgilerini güncelle
                    existingCompany.CompanyName = CurrentCompany.CompanyName;
                    existingCompany.LogoPath = CurrentCompany.LogoPath ?? "";
                    existingCompany.ErpTemplatePath = CurrentCompany.ErpTemplatePath;
                    existingCompany.MonthDays = CurrentCompany.MonthDays;
                    existingCompany.PayrollYear = CurrentCompany.PayrollYear;
                    existingCompany.PayrollMonth = CurrentCompany.PayrollMonth;
                    existingCompany.Description = CurrentCompany.Description;
                    existingCompany.ShiftRuleConfigs = CurrentCompany.ShiftRuleConfigs;
                    existingCompany.SpecialOvertimeSettings = CurrentCompany.SpecialOvertimeSettings != null
                        ? new SpecialOvertimeSettings
                        {
                            EarnedRestDayColumnName = CurrentCompany.SpecialOvertimeSettings.EarnedRestDayColumnName,
                            EarnedRestDayColumnLetter = CurrentCompany.SpecialOvertimeSettings.EarnedRestDayColumnLetter,
                            HolidayWorkColumnName = CurrentCompany.SpecialOvertimeSettings.HolidayWorkColumnName,
                            HolidayWorkColumnLetter = CurrentCompany.SpecialOvertimeSettings.HolidayWorkColumnLetter,
                            WeekendWorkColumnName = CurrentCompany.SpecialOvertimeSettings.WeekendWorkColumnName,
                            WeekendWorkColumnLetter = CurrentCompany.SpecialOvertimeSettings.WeekendWorkColumnLetter,
                            UseManualEarnedRestLetter = CurrentCompany.SpecialOvertimeSettings.UseManualEarnedRestLetter,
                            UseManualHolidayLetter = CurrentCompany.SpecialOvertimeSettings.UseManualHolidayLetter,
                            UseManualWeekendLetter = CurrentCompany.SpecialOvertimeSettings.UseManualWeekendLetter
                        }
                        : new SpecialOvertimeSettings();
                    existingCompany.ActiveOfficialHolidayDates = CurrentCompany.ActiveOfficialHolidayDates != null
                        ? CurrentCompany.ActiveOfficialHolidayDates.ToDictionary(
                            kvp => kvp.Key,
                            kvp => kvp.Value != null ? new List<string>(kvp.Value) : new List<string>())
                        : new Dictionary<int, List<string>>();
                    existingCompany.HorizontalTemplateSettings = CurrentCompany.HorizontalTemplateSettings != null
                        ? new HorizontalTemplateSettings
                        {
                            ApplyRulesWithoutShift = CurrentCompany.HorizontalTemplateSettings.ApplyRulesWithoutShift,
                            MinConsecutiveDaysHours = CurrentCompany.HorizontalTemplateSettings.MinConsecutiveDaysHours,
                            SelectedShiftRuleGroupName = CurrentCompany.HorizontalTemplateSettings.SelectedShiftRuleGroupName ?? string.Empty,
                            OverrideConsecutiveDaysForVacation = CurrentCompany.HorizontalTemplateSettings.OverrideConsecutiveDaysForVacation,
                            HasHolidaysInTemplate = CurrentCompany.HorizontalTemplateSettings.HasHolidaysInTemplate,
                            OfficialHolidayWorkIndicators = CurrentCompany.HorizontalTemplateSettings.OfficialHolidayWorkIndicators ?? "RT,R,T",
                            OfficialHolidayRestIndicators = CurrentCompany.HorizontalTemplateSettings.OfficialHolidayRestIndicators ?? "X,7.5"
                        }
                        : new HorizontalTemplateSettings();
                    existingCompany.ConditionalEarningsSettings = CurrentCompany.ConditionalEarningsSettings != null
                        ? new ConditionalEarningsSettings
                        {
                            Rules = CurrentCompany.ConditionalEarningsSettings.Rules?.Select(r => new ConditionalEarningsRule
                            {
                                ConditionType = r.ConditionType ?? string.Empty,
                                ConditionOperator = r.ConditionOperator ?? ">",
                                ConditionValue = r.ConditionValue,
                                ConditionValueSource = r.ConditionValueSource ?? "Sabit",
                                TargetColumnName = r.TargetColumnName ?? string.Empty,
                                TargetColumnLetter = r.TargetColumnLetter ?? string.Empty,
                                EarningsValue = r.EarningsValue,
                                ValueType = r.ValueType ?? "Sabit",
                                StartDate = r.StartDate,
                                EndDate = r.EndDate,
                                IsEnabled = r.IsEnabled,
                                Description = r.Description ?? string.Empty
                            }).ToList() ?? new List<ConditionalEarningsRule>()
                        }
                        : new ConditionalEarningsSettings();
                }
            }

            try
            {
                _configService.SaveConfig(_parentConfig);
                Console.WriteLine($"[CompanyManagementModal] Firma '{CurrentCompany.CompanyName}' başarıyla kaydedildi");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CompanyManagementModal] Kaydetme hatası: {ex.Message}");
                MessageBox.Show($"Değişiklikler kaydedilirken hata oluştu: {ex.Message}", "Kaydetme Hatası", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            Console.WriteLine($"[CompanyManagementModal] DialogResult = true ayarlanıyor ve modal kapatılıyor");
            DialogResult = true;
            Close();
        }

        private void CompanyManagementModal_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Payroll bilgilerini güncelle
                UpdatePayrollSummary();

                // Vardiya gruplarını listele
                if (lvShiftGroups != null)
                {
                    lvShiftGroups.ItemsSource = CurrentCompany.ShiftRuleConfigs;
                }

                // Yatay şablon için vardiya grubu ComboBox'ını doldur
                if (cmbHorizontalShiftRule != null)
                {
                    cmbHorizontalShiftRule.ItemsSource = CurrentCompany.ShiftRuleConfigs;
                    // Seçili değeri ayarla
                    if (CurrentCompany.HorizontalTemplateSettings != null && 
                        !string.IsNullOrWhiteSpace(CurrentCompany.HorizontalTemplateSettings.SelectedShiftRuleGroupName))
                    {
                        cmbHorizontalShiftRule.SelectedValue = CurrentCompany.HorizontalTemplateSettings.SelectedShiftRuleGroupName;
                    }
                }

                // Veri şablonlarını yenile
                LoadDataTemplates();

                EnsureSpecialOvertimeSettings();
                UpdateSpecialOvertimeLettersFromNames();

                // Koşullu kazanç kurallarını yükle
                if (CurrentCompany?.ConditionalEarningsSettings == null)
                {
                    CurrentCompany.ConditionalEarningsSettings = new ConditionalEarningsSettings();
                }
                var dgRules = this.FindName("dgConditionalRules") as System.Windows.Controls.DataGrid;
                if (dgRules != null && CurrentCompany.ConditionalEarningsSettings != null)
                {
                    dgRules.ItemsSource = CurrentCompany.ConditionalEarningsSettings.Rules;
                    
                    // Butonların durumunu güncelle (seçim olmadığında disabled)
                    var btnEdit = this.FindName("btnEditConditionalRule") as Button;
                    var btnDelete = this.FindName("btnDeleteConditionalRule") as Button;
                    
                    bool hasSelection = dgRules.SelectedItem != null;
                    if (btnEdit != null) btnEdit.IsEnabled = hasSelection;
                    if (btnDelete != null) btnDelete.IsEnabled = hasSelection;
                }

                // Logo önizlemesini yükle
                UpdateLogoPreview();

                Console.WriteLine($"[CompanyManagementModal] Firma '{CurrentCompany.CompanyName}' için modal yüklendi");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CompanyManagementModal] Loaded hatası: {ex.Message}");
            }
        }

        private void btnSelectLogo_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Firma Logosunu Seçin",
                Filter = "Resim Dosyaları (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif|Tüm Dosyalar (*.*)|*.*",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    // Kök dizinde CompanyLogos klasörünü oluştur
                    string appBaseDirectory = AppDomain.CurrentDomain.BaseDirectory;
                    string logosDirectory = System.IO.Path.Combine(appBaseDirectory, "CompanyLogos");
                    
                    if (!System.IO.Directory.Exists(logosDirectory))
                    {
                        System.IO.Directory.CreateDirectory(logosDirectory);
                    }

                    // Logo dosyasını CompanyLogos klasörüne kopyala
                    // Dosya adı: {FirmaKodu}_{ZamanDamgasi}.{Uzanti}
                    string sourceFile = dialog.FileName;
                    string extension = System.IO.Path.GetExtension(sourceFile);
                    string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    string companyCode = !string.IsNullOrWhiteSpace(CurrentCompany.CompanyCode) 
                        ? CurrentCompany.CompanyCode 
                        : "LOG";
                    string fileName = $"{companyCode}_{timestamp}{extension}";
                    string destinationPath = System.IO.Path.Combine(logosDirectory, fileName);

                    // Eğer eski bir logo varsa, onu sil
                    if (!string.IsNullOrWhiteSpace(CurrentCompany.LogoPath))
                    {
                        string oldLogoPath = GetFullLogoPath(CurrentCompany.LogoPath);
                        if (System.IO.File.Exists(oldLogoPath))
                        {
                            try
                            {
                                System.IO.File.Delete(oldLogoPath);
                            }
                            catch
                            {
                                // Silme hatası önemli değil, devam et
                            }
                        }
                    }

                    // Yeni logoyu kopyala
                    System.IO.File.Copy(sourceFile, destinationPath, overwrite: true);

                    // Sadece göreceli yolu kaydet (CompanyLogos\{dosya adı})
                    CurrentCompany.LogoPath = $"CompanyLogos\\{fileName}";
                    UpdateLogoPreview();

                    MessageBox.Show("Logo başarıyla kaydedildi.", "Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Logo yüklenirken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private string GetFullLogoPath(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                return null;

            // Eğer zaten tam yol ise, olduğu gibi döndür
            if (System.IO.Path.IsPathRooted(relativePath))
                return relativePath;

            // Göreceli yol ise, kök dizine ekle
            string appBaseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            return System.IO.Path.Combine(appBaseDirectory, relativePath);
        }

        private void UpdateLogoPreview()
        {
            if (imgLogoPreview == null)
                return;

            string? logoPath = null;

            // Önce firma logosunu kontrol et
            if (!string.IsNullOrWhiteSpace(CurrentCompany?.LogoPath))
            {
                try
                {
                    string fullPath = GetFullLogoPath(CurrentCompany.LogoPath);
                    
                    if (!string.IsNullOrWhiteSpace(fullPath) && File.Exists(fullPath))
                    {
                        logoPath = fullPath;
                        
                        // Logo varsa silme butonunu görünür yap (hover'da görünecek)
                        if (btnDeleteLogo != null)
                        {
                            btnDeleteLogo.Visibility = Visibility.Visible;
                            btnDeleteLogo.Opacity = 0; // Başlangıçta görünmez, hover'da görünecek
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Logo Preview] Hata: {ex.Message}");
                }
            }

            // Eğer firma logosu yoksa varsayılan logoyu göster
            if (string.IsNullOrWhiteSpace(logoPath))
            {
                try
                {
                    string appBaseDirectory = AppDomain.CurrentDomain.BaseDirectory;
                    string defaultLogoPath = System.IO.Path.Combine(appBaseDirectory, "Gemini_Generated_Image_vsio8jvsio8jvsio.png");
                    
                    if (File.Exists(defaultLogoPath))
                    {
                        logoPath = defaultLogoPath;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Logo Preview] Varsayılan logo yüklenirken hata: {ex.Message}");
                }

                // Logo yoksa silme butonunu gizle
                if (btnDeleteLogo != null)
                {
                    btnDeleteLogo.Visibility = Visibility.Collapsed;
                }
            }

            // Logoyu yükle
            if (!string.IsNullOrWhiteSpace(logoPath) && File.Exists(logoPath))
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(logoPath, UriKind.Absolute);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    imgLogoPreview.Source = bitmap;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Logo Preview] Logo yüklenirken hata: {ex.Message}");
                    imgLogoPreview.Source = null;
                }
            }
            else
            {
                imgLogoPreview.Source = null;
            }
        }

        private void txtLogoPath_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateLogoPreview();
        }

        private void btnDeleteLogo_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(CurrentCompany?.LogoPath))
                return;

            var result = MessageBox.Show(
                "Firma logosunu silmek istediğinizden emin misiniz?",
                "Logo Sil",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    // Logo dosyasını sil
                    string fullLogoPath = GetFullLogoPath(CurrentCompany.LogoPath);
                    if (!string.IsNullOrWhiteSpace(fullLogoPath) && File.Exists(fullLogoPath))
                    {
                        File.Delete(fullLogoPath);
                    }

                    // LogoPath'i temizle
                    CurrentCompany.LogoPath = "";
                    UpdateLogoPreview();

                    MessageBox.Show("Logo başarıyla silindi.", "Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Logo silinirken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void CompanyManagementModal_Unloaded(object sender, RoutedEventArgs e)
        {
            // Event handler'ları temizle
            Loaded -= CompanyManagementModal_Loaded;
            Unloaded -= CompanyManagementModal_Unloaded;
        }

        private void CompanyInfoScrollViewer_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateCompanyInfoScrollHint();
        }

        private void CompanyInfoScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            UpdateCompanyInfoScrollHint();
        }

        private void CompanyInfoScrollHint_Click(object sender, RoutedEventArgs e)
        {
            if (CompanyInfoScrollViewer == null)
            {
                return;
            }

            // Animasyonlu scroll - yumuşak geçiş
            var currentOffset = CompanyInfoScrollViewer.VerticalOffset;
            var maxOffset = CompanyInfoScrollViewer.ScrollableHeight;
            var targetOffset = maxOffset; // En alta scroll

            if (maxOffset <= 0)
            {
                UpdateCompanyInfoScrollHint();
                return;
            }

            // Animasyonlu scroll için DispatcherTimer kullan
            var startTime = DateTime.Now;
            var duration = TimeSpan.FromMilliseconds(500);
            var startOffset = currentOffset;
            var distance = targetOffset - currentOffset;

            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(16) // ~60 FPS
            };

            timer.Tick += (s, args) =>
            {
                var elapsed = DateTime.Now - startTime;
                if (elapsed >= duration)
                {
                    CompanyInfoScrollViewer.ScrollToVerticalOffset(targetOffset);
                    timer.Stop();
                    UpdateCompanyInfoScrollHint();
                    return;
                }

                // Easing function: EaseOut (quadratic)
                var progress = elapsed.TotalMilliseconds / duration.TotalMilliseconds;
                var easedProgress = 1 - Math.Pow(1 - progress, 2); // Quadratic ease out
                var newOffset = startOffset + (distance * easedProgress);
                
                CompanyInfoScrollViewer.ScrollToVerticalOffset(newOffset);
            };

            timer.Start();
        }

        private void UpdateCompanyInfoScrollHint()
        {
            if (CompanyInfoScrollViewer == null || CompanyInfoScrollHint == null)
            {
                return;
            }

            var remainingScrollable = CompanyInfoScrollViewer.ScrollableHeight - CompanyInfoScrollViewer.VerticalOffset;
            CompanyInfoScrollHint.Visibility = remainingScrollable > 12
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void btnDelete_Click(object sender, RoutedEventArgs e)
        {
            if (_isNewCompany)
            {
                MessageBox.Show("Yeni firma henüz kaydedilmediği için silinemez.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show(
                $"{CurrentCompany.CompanyName} firmasını silmek istediğinizden emin misiniz?\n\n" +
                "Bu işlem geri alınamaz ve firma ile ilgili tüm ayarlar kaybolacaktır.",
                "Firma Silme Onayı",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    // Firmayı listeden kaldır
                    var companyToRemove = _parentConfig.CompanyConfigs.FirstOrDefault(c => c.CompanyCode == CurrentCompany.CompanyCode);
                    if (companyToRemove != null)
                    {
                        _parentConfig.CompanyConfigs.Remove(companyToRemove);
                        _configService.SaveConfig(_parentConfig);

                        Console.WriteLine($"[CompanyManagementModal] Firma '{CurrentCompany.CompanyName}' başarıyla silindi");
                        MessageBox.Show("Firma başarıyla silindi.", "Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
                        Console.WriteLine($"[CompanyManagementModal] DialogResult = true ayarlanıyor ve modal kapatılıyor");
                        DialogResult = true;
                        Close();
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Firma silinirken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void btnOpenCalendar_Click(object sender, RoutedEventArgs e)
        {
            int year = CurrentCompany?.PayrollYear > 0 ? CurrentCompany.PayrollYear : DateTime.Now.Year;
            int month = CurrentCompany?.PayrollMonth > 0 ? CurrentCompany.PayrollMonth : DateTime.Now.Month;

            var calendarWindow = new HolidayCalendarWindow(_calendarService, year, month)
            {
                Owner = this
            };

            if (calendarWindow.ShowDialog() == true)
            {
                CurrentCompany.PayrollYear = calendarWindow.SelectedYear;
                CurrentCompany.PayrollMonth = calendarWindow.SelectedMonth;
                CurrentCompany.MonthDays = _calendarService.GetDaysInMonth(calendarWindow.SelectedYear, calendarWindow.SelectedMonth);
                SelectedHolidayYear = calendarWindow.SelectedYear;
                UpdatePayrollSummary();
                OnPropertyChanged(nameof(CurrentCompany));
            }
        }

        private void UpdatePayrollSummary()
        {
            if (CurrentCompany == null || txtPayrollPeriod == null)
            {
                return;
            }

            int year = CurrentCompany.PayrollYear > 0 ? CurrentCompany.PayrollYear : DateTime.Now.Year;
            int month = CurrentCompany.PayrollMonth > 0 ? CurrentCompany.PayrollMonth : DateTime.Now.Month;

            CurrentCompany.PayrollYear = year;
            CurrentCompany.PayrollMonth = month;
            CurrentCompany.MonthDays = _calendarService.GetDaysInMonth(year, month);
            SelectedHolidayYear = year;

            txtPayrollPeriod.Text = $"{_culture.DateTimeFormat.GetMonthName(month)} {year}";

            var holidays = _calendarService.GetHolidays(year, month);
            if (holidays.Count == 0)
            {
                txtPayrollDetails.Text = $"Bu ay {_calendarService.GetDaysInMonth(year, month)} gün sürüyor ve resmi tatil bulunmuyor.";
            }
            else
            {
                int halfDays = holidays.Count(h => h.IsHalfDay);
                string holidaySummary = holidays.Count == 1
                    ? $"1 resmi tatil var."
                    : $"{holidays.Count} resmi tatil var.";

                if (halfDays > 0)
                {
                    holidaySummary += $" Bunların {halfDays} adedi yarım gün.";
                }

                txtPayrollDetails.Text = $"{_calendarService.GetDaysInMonth(year, month)} gün • {holidaySummary}";
            }
        }

        private void InitializeHolidaySelections()
        {
            if (CurrentCompany != null && CurrentCompany.ActiveOfficialHolidayDates == null)
            {
                CurrentCompany.ActiveOfficialHolidayDates = new Dictionary<int, List<string>>();
            }

            var supportedYears = _calendarService.GetSupportedYears()?.OrderBy(y => y).ToList() ?? new List<int>();

            if (CurrentCompany?.ActiveOfficialHolidayDates != null)
            {
                foreach (var yearKey in CurrentCompany.ActiveOfficialHolidayDates.Keys)
                {
                    if (!supportedYears.Contains(yearKey))
                    {
                        supportedYears.Add(yearKey);
                    }
                }
            }
            int currentYear = CurrentCompany?.PayrollYear > 0 ? CurrentCompany.PayrollYear : DateTime.Now.Year;

            if (currentYear > 0 && !supportedYears.Contains(currentYear))
            {
                supportedYears.Add(currentYear);
            }

            HolidayYears = supportedYears
                .Distinct()
                .OrderBy(y => y)
                .ToList();

            if (HolidayYears.Count == 0 && currentYear <= 0)
            {
                SelectedHolidayYear = 0;
                return;
            }

            int defaultYear = currentYear > 0
                ? currentYear
                : (HolidayYears.Count > 0 ? HolidayYears[HolidayYears.Count - 1] : DateTime.Now.Year);

            if (HolidayYears.Count > 0 && !HolidayYears.Contains(defaultYear))
            {
                defaultYear = HolidayYears[HolidayYears.Count - 1];
            }

            SelectedHolidayYear = defaultYear;
        }

        private void LoadHolidaySelectionForYear(int year)
        {
            if (CurrentCompany == null)
            {
                return;
            }

            if (CurrentCompany.ActiveOfficialHolidayDates == null)
            {
                CurrentCompany.ActiveOfficialHolidayDates = new Dictionary<int, List<string>>();
            }

            var allHolidays = _calendarService.GetHolidaysForYear(year)?.OrderBy(h => h.Date).ToList()
                              ?? new List<HolidayInfo>();

            if (!CurrentCompany.ActiveOfficialHolidayDates.TryGetValue(year, out var storedDates) || storedDates == null)
            {
                storedDates = allHolidays.Select(h => FormatHolidayDate(h.Date)).ToList();
                CurrentCompany.ActiveOfficialHolidayDates[year] = storedDates;
            }

            foreach (var item in _holidaySelectionItems)
            {
                item.PropertyChanged -= HolidaySelectionItem_PropertyChanged;
            }
            _holidaySelectionItems.Clear();

            var activeSet = new HashSet<string>(
                storedDates.Where(d => !string.IsNullOrWhiteSpace(d)).Select(d => d.Trim()),
                StringComparer.OrdinalIgnoreCase);

            var activeSelectionMap = new Dictionary<string, bool?>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in storedDates)
            {
                if (HolidaySelectionSerializer.TryDeserialize(entry, out var parsedDate, out var treatAsHalfDay))
                {
                    var key = FormatHolidayDate(parsedDate);
                    activeSelectionMap[key] = treatAsHalfDay;
                }
                else if (!string.IsNullOrWhiteSpace(entry))
                {
                    activeSelectionMap[entry.Trim()] = null;
                }
            }

            foreach (var holiday in allHolidays)
            {
                var dateKey = FormatHolidayDate(holiday.Date);
                bool isSelected = activeSelectionMap.ContainsKey(dateKey);
                bool? treatOverride = null;
                if (activeSelectionMap.TryGetValue(dateKey, out var overrideValue))
                {
                    treatOverride = overrideValue;
                }

                var item = new HolidaySelectionItem(holiday, isSelected, treatOverride);
                item.PropertyChanged += HolidaySelectionItem_PropertyChanged;
                _holidaySelectionItems.Add(item);
            }

            if (!_holidaySelectionItems.Any())
            {
                CurrentCompany.ActiveOfficialHolidayDates[year] = new List<string>();
            }

            PersistHolidaySelections();
        }

        private static string FormatHolidayDate(DateTime date)
        {
            return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        private void PersistHolidaySelections()
        {
            if (CurrentCompany == null || SelectedHolidayYear <= 0)
            {
                return;
            }

            if (CurrentCompany.ActiveOfficialHolidayDates == null)
            {
                CurrentCompany.ActiveOfficialHolidayDates = new Dictionary<int, List<string>>();
            }

            var selectedDates = _holidaySelectionItems
                .Where(item => item.IsSelected)
                .Select(item => HolidaySelectionSerializer.Serialize(item.Date, item.TreatAsHalfDay))
                .ToList();

            CurrentCompany.ActiveOfficialHolidayDates[SelectedHolidayYear] = selectedDates;
        }

        private void HolidaySelectionItem_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(HolidaySelectionItem.IsSelected) ||
                e.PropertyName == nameof(HolidaySelectionItem.TreatAsHalfDay))
            {
                PersistHolidaySelections();
            }
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void btnAddDataTemplate_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureCompanyCodeForTemplates())
            {
                return;
            }

            try
            {
                // 1) Kullanıcıdan örnek Excel dosyası seçmesini iste
                var ofd = new OpenFileDialog
                {
                    Title = "Veri şablonu için örnek Excel dosyasını seçin",
                    Filter = "Excel Dosyaları (*.xlsx)|*.xlsx|Tüm Dosyalar (*.*)|*.*"
                };

                if (ofd.ShowDialog() != true)
                {
                    return;
                }

                string filePath = ofd.FileName;

                // 2) Excel başlıklarını oku
                // EPPlus 8+ için lisans bilgisini (kişisel/kurumsal olmayan kullanım) burada da set ediyoruz
                OfficeOpenXml.ExcelPackage.License.SetNonCommercialPersonal("WebScraper");
                List<string> headers = new List<string>();

                using (var package = new ExcelPackage(new System.IO.FileInfo(filePath)))
                {
                    var sheet = package.Workbook.Worksheets.FirstOrDefault();
                    if (sheet == null || sheet.Dimension == null)
                    {
                        MessageBox.Show("Seçilen dosyada geçerli bir çalışma sayfası bulunamadı.", "Hata",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    int headerRow = 1;
                    int lastCol = sheet.Dimension.End.Column;
                    for (int col = 1; col <= lastCol; col++)
                    {
                        var value = sheet.Cells[headerRow, col].Text?.Trim();
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            headers.Add(value);
                        }
                    }
                }

                if (headers.Count == 0)
                {
                    MessageBox.Show("Başlık satırı boş görünüyor. İlk satırda sütun adları olmalı.", "Uyarı",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // 3) Kullanıcıya şablon tipini sor
                var isHorizontal = MessageBox.Show(
                    "Bu dosya satır başına 1 personel, sütunlarda gün bazında çalışma saati içeren yatay bir tablo mu?\n\n" +
                    "Evet: Yatay Gün-Saat Tablosu\nHayır: Klasik PDKS Log (giriş/çıkış kayıtları)",
                    "Şablon Türü",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question) == MessageBoxResult.Yes;

                string templateType = isHorizontal ? "Horizontal_DailyHours" : "PDKS_Log";

                // 4) Şablon adı ve açıklama üret
                string shortFileName = System.IO.Path.GetFileName(filePath);
                string name = isHorizontal
                    ? $"Yatay Şablon - {shortFileName}"
                    : $"PDKS Log Şablonu - {shortFileName}";

                string hint = isHorizontal
                    ? "Satır başına 1 personel, sütunlarda gün bazında çalışma saati"
                    : "Giriş/çıkış saatlerinden oluşan PDKS log dosyası";

                // 5) Şablonu oluştur ve ExpectedColumns'u başlıklardan doldur
                var newTemplate = _dataTemplateService.CreateTemplate(name, templateType, hint);
                newTemplate.ExpectedColumns = headers;

                // Yatay şablonlar için varsayılan sembol eşleştirmesi ekle
                if (isHorizontal)
                {
                    newTemplate.SymbolHourMap = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                    {
                        { "x", 7.5 }
                    };
                }

                DataTemplates.Add(newTemplate);
                PersistDataTemplates();

                MessageBox.Show(
                    "Örnek Excel dosyasından veri şablonu oluşturuldu.\n\n" +
                    "Sistem, benzer başlıklara sahip dosyaları bu şablonla eşleştirmeyi deneyecek.",
                    "Veri Şablonu Eklendi",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Veri şablonu oluşturulurken hata oluştu: {ex.Message}", "Hata",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnEditDataTemplate_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureCompanyCodeForTemplates())
            {
                return;
            }

            if (dgDataTemplates?.SelectedItem is not DataTemplate selected)
            {
                MessageBox.Show("Lütfen düzenlemek istediğiniz veri şablonunu seçin.", "Uyarı",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var editWindow = new Window
                {
                    Title = "Veri Şablonunu Düzenle",
                    Width = 700,
                    Height = 360,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    ResizeMode = ResizeMode.NoResize,
                    Owner = this
                };

                var grid = new Grid
                {
                    Margin = new Thickness(16)
                };
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                // Ad
                var lblName = new TextBlock
                {
                    Text = "Şablon Adı:",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 8)
                };
                Grid.SetRow(lblName, 0);
                Grid.SetColumn(lblName, 0);

                var txtName = new TextBox
                {
                    Text = selected.Name,
                    Margin = new Thickness(0, 0, 0, 8)
                };
                Grid.SetRow(txtName, 0);
                Grid.SetColumn(txtName, 1);

                // Tür
                var lblType = new TextBlock
                {
                    Text = "Tür:",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 8)
                };
                Grid.SetRow(lblType, 1);
                Grid.SetColumn(lblType, 0);

                var cmbType = new ComboBox
                {
                    Margin = new Thickness(0, 0, 0, 8),
                    Height = 28
                };
                cmbType.Items.Add(new { Key = "PDKS_Log", Display = "PDKS Log" });
                cmbType.Items.Add(new { Key = "Horizontal_DailyHours", Display = "Yatay Gün-Saat" });
                cmbType.DisplayMemberPath = "Display";
                cmbType.SelectedItem = cmbType.Items
                    .Cast<object>()
                    .FirstOrDefault(i => (string)i.GetType().GetProperty("Key")!.GetValue(i)! == selected.TemplateType)
                    ?? cmbType.Items[0];
                Grid.SetRow(cmbType, 1);
                Grid.SetColumn(cmbType, 1);

                // Kaynak / Açıklama
                var lblHint = new TextBlock
                {
                    Text = "Kaynak / Açıklama:",
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                Grid.SetRow(lblHint, 2);
                Grid.SetColumn(lblHint, 0);

                var txtHint = new TextBox
                {
                    Text = selected.SourceHint,
                    AcceptsReturn = true,
                    TextWrapping = TextWrapping.Wrap,
                    Height = 70
                };
                Grid.SetRow(txtHint, 2);
                Grid.SetColumn(txtHint, 1);

                // Sembolik saat eşleştirmeleri
                var lblSymbols = new TextBlock
                {
                    Text = "Semboller (x=7.5 satır başına):",
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 6, 8, 0)
                };
                Grid.SetRow(lblSymbols, 3);
                Grid.SetColumn(lblSymbols, 0);

                var txtSymbols = new TextBox
                {
                    AcceptsReturn = true,
                    TextWrapping = TextWrapping.Wrap,
                    Height = 70,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Margin = new Thickness(0, 0, 0, 4)
                };

                // Mevcut sembolleri satır satır x=7.5 formatında doldur
                if (selected.SymbolHourMap != null && selected.SymbolHourMap.Count > 0)
                {
                    txtSymbols.Text = string.Join(Environment.NewLine,
                        selected.SymbolHourMap.Select(kvp => $"{kvp.Key}={kvp.Value}"));
                }

                // Sembol açıklaması
                var lblSymbolHint = new TextBlock
                {
                    Text = "Not: Şablonunuzda x, *, T gibi semboller varsa burada eşleştirin (örn: x=7.5).\n" +
                           "Eğer şablonunuzda direkt saat değerleri (7.5, 8, vb.) yazıyorsa bu alanı boş bırakabilirsiniz.",
                    FontSize = 11,
                    Foreground = System.Windows.Media.Brushes.Gray,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 8)
                };

                // Sembol alanını ve açıklamayı StackPanel içine al
                var symbolsPanel = new StackPanel();
                symbolsPanel.Children.Add(txtSymbols);
                symbolsPanel.Children.Add(lblSymbolHint);

                Grid.SetRow(symbolsPanel, 3);
                Grid.SetColumn(symbolsPanel, 1);

                // Yeniden Dosya Yükleme
                var lblReload = new TextBlock
                {
                    Text = "Örnek Dosya:",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 6, 8, 8)
                };
                Grid.SetRow(lblReload, 4);
                Grid.SetColumn(lblReload, 0);

                var reloadPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 0, 0, 8)
                };

                var txtFilePath = new TextBox
                {
                    Text = "Örnek Excel dosyasından başlıkları yeniden yükle",
                    IsReadOnly = true,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Height = 28,
                    Margin = new Thickness(0, 0, 8, 0)
                };

                var btnReloadFile = new Button
                {
                    Content = "Dosya Seç",
                    Width = 100,
                    Height = 28,
                    VerticalAlignment = VerticalAlignment.Top
                };

                btnReloadFile.Click += (_, _) =>
                {
                    var dialog = new Microsoft.Win32.OpenFileDialog
                    {
                        Title = "Örnek Excel Dosyasını Seçin",
                        Filter = "Excel Dosyaları (*.xlsx;*.xls)|*.xlsx;*.xls|Tüm Dosyalar (*.*)|*.*",
                        InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                    };

                    if (dialog.ShowDialog() == true)
                    {
                        try
                        {
                            txtFilePath.Text = System.IO.Path.GetFileName(dialog.FileName);

                            // Excel dosyasından başlıkları oku
                            OfficeOpenXml.ExcelPackage.License.SetNonCommercialPersonal("WebScraper");
                            using (var package = new ExcelPackage(new System.IO.FileInfo(dialog.FileName)))
                            {
                                var sheet = package.Workbook.Worksheets.FirstOrDefault();
                                if (sheet == null || sheet.Dimension == null)
                                {
                                    MessageBox.Show(editWindow, "Excel dosyasında çalışma sayfası bulunamadı.", "Hata",
                                        MessageBoxButton.OK, MessageBoxImage.Warning);
                                    return;
                                }

                                // İlk satırdan başlıkları oku
                                var headers = new List<string>();
                                for (int col = 1; col <= sheet.Dimension.End.Column; col++)
                                {
                                    var headerText = sheet.Cells[1, col].Text?.Trim();
                                    if (!string.IsNullOrWhiteSpace(headerText))
                                    {
                                        headers.Add(headerText);
                                    }
                                }

                                if (headers.Count > 0)
                                {
                                    selected.ExpectedColumns = headers;
                                    MessageBox.Show(editWindow, 
                                        $"{headers.Count} sütun başlığı başarıyla yüklendi.\n\n" +
                                        $"İlk başlıklar: {string.Join(", ", headers.Take(5))}" +
                                        (headers.Count > 5 ? "..." : ""),
                                        "Başlıklar Yüklendi",
                                        MessageBoxButton.OK,
                                        MessageBoxImage.Information);
                                }
                                else
                                {
                                    MessageBox.Show(editWindow, "Excel dosyasında başlık satırı bulunamadı.", "Uyarı",
                                        MessageBoxButton.OK, MessageBoxImage.Warning);
                                }

                            }
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show(editWindow, $"Dosya yüklenirken hata oluştu: {ex.Message}", "Hata",
                                MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                };

                reloadPanel.Children.Add(txtFilePath);
                reloadPanel.Children.Add(btnReloadFile);

                Grid.SetRow(reloadPanel, 4);
                Grid.SetColumn(reloadPanel, 1);

                var buttonsPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 16, 0, 0)
                };
                Grid.SetRow(buttonsPanel, 5);
                Grid.SetColumnSpan(buttonsPanel, 2);

                var btnOk = new Button
                {
                    Content = "Kaydet",
                    Width = 90,
                    Height = 30,
                    Margin = new Thickness(0, 0, 8, 0),
                    IsDefault = true
                };

                var btnCancel = new Button
                {
                    Content = "İptal",
                    Width = 80,
                    Height = 30,
                    IsCancel = true
                };

                btnOk.Click += (_, _) =>
                {
                    if (string.IsNullOrWhiteSpace(txtName.Text))
                    {
                        MessageBox.Show(editWindow, "Şablon adı boş olamaz.", "Uyarı",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    editWindow.DialogResult = true;
                    editWindow.Close();
                };

                buttonsPanel.Children.Add(btnOk);
                buttonsPanel.Children.Add(btnCancel);

                grid.Children.Add(lblName);
                grid.Children.Add(txtName);
                grid.Children.Add(lblType);
                grid.Children.Add(cmbType);
                grid.Children.Add(lblHint);
                grid.Children.Add(txtHint);
                grid.Children.Add(lblSymbols);
                grid.Children.Add(symbolsPanel);
                grid.Children.Add(lblReload);
                grid.Children.Add(reloadPanel);
                grid.Children.Add(buttonsPanel);

                editWindow.Content = grid;

                if (editWindow.ShowDialog() == true)
                {
                    selected.Name = txtName.Text.Trim();
                    selected.SourceHint = txtHint.Text.Trim();

                    var selectedTypeItem = cmbType.SelectedItem;
                    if (selectedTypeItem != null)
                    {
                        var keyProp = selectedTypeItem.GetType().GetProperty("Key");
                        if (keyProp != null)
                        {
                            selected.TemplateType = (string)keyProp.GetValue(selectedTypeItem)!;
                        }
                    }

                    // Sembolik saat eşleştirmelerini parse et (sadece yatay şablonlar için anlamlı)
                    selected.SymbolHourMap ??= new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                    selected.SymbolHourMap.Clear();

                    foreach (var line in txtSymbols.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        var parts = line.Split('=', ':');
                        if (parts.Length >= 2)
                        {
                            var key = parts[0].Trim();
                            var valueText = parts[1].Trim().Replace(",", ".");
                            if (!string.IsNullOrWhiteSpace(key) &&
                                double.TryParse(valueText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var hours))
                            {
                                selected.SymbolHourMap[key] = hours;
                            }
                        }
                    }

                    PersistDataTemplates();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Veri şablonu düzenlenirken hata oluştu: {ex.Message}", "Hata",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnDeleteDataTemplate_Click(object sender, RoutedEventArgs e)
        {
            if (!EnsureCompanyCodeForTemplates())
            {
                return;
            }

            if (dgDataTemplates?.SelectedItem is not DataTemplate selected)
            {
                MessageBox.Show("Lütfen silmek istediğiniz veri şablonunu seçin.", "Uyarı",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (MessageBox.Show(
                    $"'{selected.Name}' veri şablonunu silmek istediğinizden emin misiniz?\n\nBu işlem geri alınamaz.",
                    "Şablonu Sil",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                DataTemplates.Remove(selected);
                PersistDataTemplates();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Veri şablonu silinirken hata oluştu: {ex.Message}", "Hata",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void EnsureSpecialOvertimeSettings()
        {
            if (CurrentCompany != null && CurrentCompany.SpecialOvertimeSettings == null)
            {
                CurrentCompany.SpecialOvertimeSettings = new SpecialOvertimeSettings();
                UpdateSpecialOvertimeLettersFromNames();
            }
        }

        private void NormalizeSpecialOvertimeSettings()
        {
            if (CurrentCompany?.SpecialOvertimeSettings == null)
            {
                return;
            }

            var settings = CurrentCompany.SpecialOvertimeSettings;

            settings.EarnedRestDayColumnName = settings.EarnedRestDayColumnName?.Trim();
            settings.HolidayWorkColumnName = settings.HolidayWorkColumnName?.Trim();
            settings.WeekendWorkColumnName = settings.WeekendWorkColumnName?.Trim();
            if (settings.UseManualEarnedRestLetter && !string.IsNullOrWhiteSpace(settings.EarnedRestDayColumnLetter))
            {
                settings.EarnedRestDayColumnLetter = settings.EarnedRestDayColumnLetter.Trim().ToUpperInvariant();
            }
            if (settings.UseManualHolidayLetter && !string.IsNullOrWhiteSpace(settings.HolidayWorkColumnLetter))
            {
                settings.HolidayWorkColumnLetter = settings.HolidayWorkColumnLetter.Trim().ToUpperInvariant();
            }
            if (settings.UseManualWeekendLetter && !string.IsNullOrWhiteSpace(settings.WeekendWorkColumnLetter))
            {
                settings.WeekendWorkColumnLetter = settings.WeekendWorkColumnLetter.Trim().ToUpperInvariant();
            }
            UpdateSpecialOvertimeLettersFromNames();
        }

        private void UpdateSpecialOvertimeLettersFromNames()
        {
            if (CurrentCompany?.SpecialOvertimeSettings == null)
            {
                return;
            }

            var settings = CurrentCompany.SpecialOvertimeSettings;
            if (!settings.UseManualEarnedRestLetter)
            {
                settings.EarnedRestDayColumnLetter = GetLetterForColumn(settings.EarnedRestDayColumnName);
            }
            if (!settings.UseManualHolidayLetter)
            {
                settings.HolidayWorkColumnLetter = GetLetterForColumn(settings.HolidayWorkColumnName);
            }
            if (!settings.UseManualWeekendLetter)
            {
                settings.WeekendWorkColumnLetter = GetLetterForColumn(settings.WeekendWorkColumnName);
            }
            OnPropertyChanged(nameof(CurrentCompany));
        }

        private static string GetLetterForColumn(string columnName)
        {
            if (string.IsNullOrWhiteSpace(columnName))
            {
                return string.Empty;
            }

            return SpecialOvertimeColumnLetterMap.TryGetValue(columnName, out var letter) ? letter : string.Empty;
        }

        private void EarnedRestColumnCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateSpecialOvertimeLettersFromNames();
        }

        private void HolidayColumnCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateSpecialOvertimeLettersFromNames();
        }

        private void EarnedRestManualCheckbox_Changed(object sender, RoutedEventArgs e)
        {
            if (CurrentCompany?.SpecialOvertimeSettings == null)
            {
                return;
            }

            bool isManual = (sender as CheckBox)?.IsChecked == true;
            CurrentCompany.SpecialOvertimeSettings.UseManualEarnedRestLetter = isManual;
            if (!isManual)
            {
                CurrentCompany.SpecialOvertimeSettings.EarnedRestDayColumnLetter = GetLetterForColumn(CurrentCompany.SpecialOvertimeSettings.EarnedRestDayColumnName);
            }
            OnPropertyChanged(nameof(CurrentCompany));
        }

        private void HolidayManualCheckbox_Changed(object sender, RoutedEventArgs e)
        {
            if (CurrentCompany?.SpecialOvertimeSettings == null)
            {
                return;
            }

            bool isManual = (sender as CheckBox)?.IsChecked == true;
            CurrentCompany.SpecialOvertimeSettings.UseManualHolidayLetter = isManual;
            if (!isManual)
            {
                CurrentCompany.SpecialOvertimeSettings.HolidayWorkColumnLetter = GetLetterForColumn(CurrentCompany.SpecialOvertimeSettings.HolidayWorkColumnName);
            }
            OnPropertyChanged(nameof(CurrentCompany));
        }

        private void HorizontalShiftRule_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CurrentCompany?.HorizontalTemplateSettings == null || cmbHorizontalShiftRule == null)
            {
                return;
            }

            var selectedRule = cmbHorizontalShiftRule.SelectedItem as ShiftRuleConfig;
            if (selectedRule != null)
            {
                CurrentCompany.HorizontalTemplateSettings.SelectedShiftRuleGroupName = selectedRule.GroupName;
                OnPropertyChanged(nameof(CurrentCompany));
                OnPropertyChanged(nameof(HorizontalTemplateShiftRuleInfo));
            }
        }

        private void TextBox_PreviewTextInput_Integer(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            // Sadece sayılara izin ver
            foreach (char c in e.Text)
            {
                if (!char.IsDigit(c))
                {
                    e.Handled = true;
                    return;
                }
            }
            e.Handled = false;
        }

        private void OverrideConsecutiveDays_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            // Üst üste gün sayısı değiştiğinde bilgi kutusunu güncelle
            OnPropertyChanged(nameof(HorizontalTemplateShiftRuleInfo));
        }

        private void HorizontalTemplateModeChanged(object sender, RoutedEventArgs e)
        {
            OnPropertyChanged(nameof(TemplateModeDisplay));
        }

        private void ChkHasHolidaysInTemplate_Changed(object sender, RoutedEventArgs e)
        {
            if (CurrentCompany?.HorizontalTemplateSettings == null)
                return;

            bool isChecked = chkHasHolidaysInTemplate?.IsChecked == true;
            CurrentCompany.HorizontalTemplateSettings.HasHolidaysInTemplate = isChecked;

            // UI güncellemesi - DataTrigger'lar zaten bunu hallediyor ama manuel kontrol edelim
            if (cmbHorizontalShiftRule != null)
            {
                cmbHorizontalShiftRule.IsEnabled = !isChecked;
            }

            if (txtOverrideConsecutiveDays != null)
            {
                txtOverrideConsecutiveDays.IsEnabled = !isChecked;
            }

            OnPropertyChanged(nameof(CurrentCompany));
            Console.WriteLine($"[Yatay Şablon] Tatiller şablonda: {isChecked}");
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void btnAddConditionalRule_Click(object sender, RoutedEventArgs e)
        {
            if (CurrentCompany?.ConditionalEarningsSettings == null)
            {
                CurrentCompany.ConditionalEarningsSettings = new ConditionalEarningsSettings();
            }

            var newRule = new ConditionalEarningsRule
            {
                ConditionType = ConditionTypeOptions[0],
                ConditionOperator = ">",
                ConditionValue = 0,
                ConditionValueSource = "Sabit",
                ColumnType = "Yan Ödeme",
                TargetColumnName = ConditionalEarningsColumnOptions[0],
                TargetColumnLetter = GetLetterForConditionalEarningsColumn(ConditionalEarningsColumnOptions[0]),
                EarningsValue = 0,
                ValueType = "Sabit",
                IsEnabled = true,
                Description = ""
            };

            ShowConditionalRuleEditor(newRule, true);
        }

        private void dgConditionalRules_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var dgRules = sender as System.Windows.Controls.DataGrid;
            if (dgRules == null) return;
            
            var btnEdit = this.FindName("btnEditConditionalRule") as Button;
            var btnDelete = this.FindName("btnDeleteConditionalRule") as Button;

            bool hasSelection = dgRules.SelectedItem != null;

            if (btnEdit != null)
            {
                btnEdit.IsEnabled = hasSelection;
            }

            if (btnDelete != null)
            {
                btnDelete.IsEnabled = hasSelection;
            }
        }

        private void btnEditConditionalRule_Click(object sender, RoutedEventArgs e)
        {
            var dgRules = this.FindName("dgConditionalRules") as System.Windows.Controls.DataGrid;
            if (dgRules == null || dgRules.SelectedItem is not ConditionalEarningsRule selected)
            {
                MessageBox.Show("Lütfen düzenlemek istediğiniz kuralı seçin.", "Uyarı",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ShowConditionalRuleEditor(selected, false);
        }

        private void btnDeleteConditionalRule_Click(object sender, RoutedEventArgs e)
        {
            var dgRules = this.FindName("dgConditionalRules") as System.Windows.Controls.DataGrid;
            if (dgRules == null || dgRules.SelectedItem is not ConditionalEarningsRule selected)
            {
                MessageBox.Show("Lütfen silmek istediğiniz kuralı seçin.", "Uyarı",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (MessageBox.Show(
                    $"'{selected.Description ?? "Seçili kural"}' kuralını silmek istediğinizden emin misiniz?\n\nBu işlem geri alınamaz.",
                    "Kuralı Sil",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                if (CurrentCompany?.ConditionalEarningsSettings?.Rules != null)
                {
                    CurrentCompany.ConditionalEarningsSettings.Rules.Remove(selected);
                    if (dgRules != null)
                    {
                        dgRules.ItemsSource = null;
                        dgRules.ItemsSource = CurrentCompany.ConditionalEarningsSettings.Rules;
                    }
                    OnPropertyChanged(nameof(CurrentCompany));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Kural silinirken hata oluştu: {ex.Message}", "Hata",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ShowConditionalRuleEditor(ConditionalEarningsRule rule, bool isNew)
        {
            try
            {
                // Poppins font tanımı
                var poppinsFont = new System.Windows.Media.FontFamily("pack://application:,,,/Fonts/Poppins-Regular.ttf#Poppins");
                var poppinsSemiBoldFont = new System.Windows.Media.FontFamily("pack://application:,,,/Fonts/Poppins-SemiBold.ttf#Poppins");
                var modalFontSize = 13; // Font boyutu küçültüldü
                
                var editWindow = new Window
                {
                    Title = isNew ? "Yeni Koşullu Kazanç Kuralı" : "Koşullu Kazanç Kuralını Düzenle",
                    Width = 680,
                    MinHeight = 600,
                    MaxHeight = 800,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    ResizeMode = ResizeMode.CanResize,
                    Owner = this,
                    FontFamily = poppinsFont
                };

                // Animasyonlu genişleme için orijinal genişlik
                double originalWidth = 680;
                double expandedWidth = 980;
                bool isExpanded = false;

                // Ana container - yatay olarak iki bölüm (ana içerik ve genişleyen panel)
                var mainContainer = new Grid
                {
                    Margin = new Thickness(20, 20, 20, 10)
                };
                
                // İki sütun: Ana içerik ve genişleyen panel
                mainContainer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 560 });
                var expandColumn = new ColumnDefinition { Width = new GridLength(0), MinWidth = 0, MaxWidth = 0 }; // Başlangıçta gizli
                mainContainer.ColumnDefinitions.Add(expandColumn);
                
                // expandColumn referansını tutmak için (lambda içinde kullanılacak)
                var expandColumnRef = expandColumn;

                // Ana grid (sol taraf - mevcut içerik)
                var grid = new Grid();
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Koşul Tipi
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Operatör
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Koşul Değeri Kaynağı
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Koşul Değeri Kaynağı Açıklama
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Sabit Değer
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Sütun Türü
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Hedef Sütun
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Sütun Harfi
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Değer Tipi
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Değer Tipi Açıklama
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Kazanç Değeri
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Başlangıç Tarihi
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Bitiş Tarihi
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Tarih Açıklama
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Açıklama
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Kural Aktif ve Butonlar

                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                
                Grid.SetColumn(grid, 0);

                int row = 0;

                // Koşul Tipi
                var lblConditionType = new TextBlock
                {
                    Text = "Koşul Tipi:",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 8),
                    FontFamily = poppinsFont,
                    FontSize = modalFontSize
                };
                Grid.SetRow(lblConditionType, row);
                Grid.SetColumn(lblConditionType, 0);

                var cmbConditionType = new ComboBox
                {
                    ItemsSource = ConditionTypeOptions,
                    SelectedItem = ConditionTypeOptions.FirstOrDefault(c => c == rule.ConditionType) ?? ConditionTypeOptions[0],
                    Margin = new Thickness(0, 0, 0, 8),
                    Height = 28,
                    FontFamily = poppinsFont,
                    FontSize = modalFontSize
                };
                Grid.SetRow(cmbConditionType, row);
                Grid.SetColumn(cmbConditionType, 1);
                row++;

                // Operatör
                var lblOperator = new TextBlock
                {
                    Text = "Operatör:",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 8),
                    FontFamily = poppinsFont,
                    FontSize = modalFontSize
                };
                Grid.SetRow(lblOperator, row);
                Grid.SetColumn(lblOperator, 0);

                var cmbOperator = new ComboBox
                {
                    ItemsSource = ConditionOperatorOptions,
                    SelectedItem = ConditionOperatorOptions.FirstOrDefault(o => o == rule.ConditionOperator) ?? ConditionOperatorOptions[0],
                    Margin = new Thickness(0, 0, 0, 8),
                    Height = 28,
                    FontFamily = poppinsFont,
                    FontSize = modalFontSize
                };
                Grid.SetRow(cmbOperator, row);
                Grid.SetColumn(cmbOperator, 1);
                row++;

                // Koşul Değeri
                var lblConditionValue = new TextBlock
                {
                    Text = "Koşul Değeri:",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 8),
                    FontFamily = poppinsFont,
                    FontSize = modalFontSize
                };
                Grid.SetRow(lblConditionValue, row);
                Grid.SetColumn(lblConditionValue, 0);

                var txtConditionValue = new TextBox
                {
                    Text = rule.ConditionValue.ToString(CultureInfo.InvariantCulture),
                    Margin = new Thickness(0, 0, 0, 8),
                    Height = 28,
                    FontFamily = poppinsFont,
                    FontSize = modalFontSize
                };
                Grid.SetRow(txtConditionValue, row);
                Grid.SetColumn(txtConditionValue, 1);
                
                // Açıklama metni
                var txtConditionValueInfo = new TextBlock
                {
                    Text = "ℹ️ Koşulun sağlanması için karşılaştırılacak değeri girin. Örnek: > 5",
                    FontSize = 11,
                    FontFamily = poppinsFont,
                    Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(59, 130, 246)),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 2, 0, 8)
                };
                Grid.SetRow(txtConditionValueInfo, row + 1);
                Grid.SetColumn(txtConditionValueInfo, 1);
                Grid.SetColumnSpan(txtConditionValueInfo, 1);
                
                row += 2;

                // Koşul tipi değiştiğinde operatör ve değer alanlarını göster/gizle
                void UpdateConditionFieldsVisibility()
                {
                    bool isUnconditional = cmbConditionType.SelectedItem?.ToString() == "Koşulsuz";
                    lblOperator.Visibility = isUnconditional ? Visibility.Collapsed : Visibility.Visible;
                    cmbOperator.Visibility = isUnconditional ? Visibility.Collapsed : Visibility.Visible;
                    lblConditionValue.Visibility = isUnconditional ? Visibility.Collapsed : Visibility.Visible;
                    txtConditionValue.Visibility = isUnconditional ? Visibility.Collapsed : Visibility.Visible;
                    txtConditionValueInfo.Visibility = isUnconditional ? Visibility.Collapsed : Visibility.Visible;
                }

                cmbConditionType.SelectionChanged += (s, e) => 
                {
                    UpdateConditionFieldsVisibility();
                    // UpdateValueTypeOptions'ı daha sonra çağıracağız (cmbValueType tanımlandıktan sonra)
                };
                UpdateConditionFieldsVisibility();

                // Sütun Türü
                var lblColumnType = new TextBlock
                {
                    Text = "Sütun Türü:",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 8),
                    FontFamily = poppinsFont,
                    FontSize = modalFontSize
                };
                Grid.SetRow(lblColumnType, row);
                Grid.SetColumn(lblColumnType, 0);

                var cmbColumnType = new ComboBox
                {
                    ItemsSource = new[] { "Yan Ödeme", "Kesintiler" },
                    SelectedItem = string.IsNullOrWhiteSpace(rule.ColumnType) ? "Yan Ödeme" : rule.ColumnType,
                    Margin = new Thickness(0, 0, 0, 8),
                    Height = 28,
                    FontFamily = poppinsFont,
                    FontSize = modalFontSize
                };
                Grid.SetRow(cmbColumnType, row);
                Grid.SetColumn(cmbColumnType, 1);
                row++;

                // Hedef Sütun
                var lblTargetColumn = new TextBlock
                {
                    Text = "Hedef Sütun:",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 8),
                    FontFamily = poppinsFont,
                    FontSize = modalFontSize
                };
                Grid.SetRow(lblTargetColumn, row);
                Grid.SetColumn(lblTargetColumn, 0);

                // Sütun Harfi (önce tanımlanmalı çünkü cmbTargetColumn'da kullanılıyor)
                var txtColumnLetter = new TextBox
                {
                    Text = rule.TargetColumnLetter,
                    IsReadOnly = true,
                    Background = System.Windows.Media.Brushes.LightGray,
                    Margin = new Thickness(0, 0, 0, 8),
                    Height = 28,
                    FontFamily = poppinsFont,
                    FontSize = modalFontSize
                };
                
                // Hedef Sütun ComboBox ve + Butonu için StackPanel
                var targetColumnPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 0, 0, 8)
                };

                var cmbTargetColumn = new ComboBox
                {
                    Height = 28,
                    Width = 250,
                    FontFamily = poppinsFont,
                    FontSize = modalFontSize
                };

                var btnAddManualColumn = new Button
                {
                    Content = "+",
                    Width = 32,
                    Height = 28,
                    Margin = new Thickness(5, 0, 0, 0),
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(16, 185, 129)),
                    Foreground = System.Windows.Media.Brushes.White,
                    BorderThickness = new Thickness(0),
                    FontSize = 16,
                    FontWeight = FontWeights.Bold,
                    Cursor = Cursors.Hand,
                    ToolTip = "Excel'den sütun ekle veya manuel gir"
                };

                targetColumnPanel.Children.Add(cmbTargetColumn);
                targetColumnPanel.Children.Add(btnAddManualColumn);
                
                // Sütun türüne göre hedef sütun listesini güncelle
                void UpdateTargetColumnOptions()
                {
                    var columnType = cmbColumnType.SelectedItem?.ToString() ?? "Yan Ödeme";
                    var optionsList = new List<string>();
                    
                    // Standart sütunları ekle
                    if (columnType == "Kesintiler")
                    {
                        optionsList.AddRange(ConditionalDeductionsColumnOptions);
                    }
                    else
                    {
                        optionsList.AddRange(ConditionalEarningsColumnOptions);
                    }
                    
                    // Manuel sütunları ekle
                    if (CurrentCompany?.ConditionalEarningsSettings?.ManualColumns != null)
                    {
                        var manualColumns = CurrentCompany.ConditionalEarningsSettings.ManualColumns
                            .Where(m => m.ColumnType == columnType)
                            .Select(m => m.ColumnName + " (Manuel)")
                            .ToList();
                        optionsList.AddRange(manualColumns);
                    }
                    
                    var currentSelection = cmbTargetColumn.SelectedItem?.ToString();
                    cmbTargetColumn.ItemsSource = optionsList;
                    
                    // Mevcut seçimi koru, yoksa ilk öğeyi seç
                    if (!string.IsNullOrWhiteSpace(currentSelection) && optionsList.Contains(currentSelection))
                    {
                        cmbTargetColumn.SelectedItem = currentSelection;
                    }
                    else
                    {
                        cmbTargetColumn.SelectedItem = optionsList.Count > 0 ? optionsList[0] : null;
                    }
                    
                    // Harfi güncelle
                    if (cmbTargetColumn.SelectedItem is string selectedColumn)
                    {
                        var letter = GetLetterForConditionalEarningsColumn(selectedColumn);
                        txtColumnLetter.Text = letter;
                    }
                }
                
                // İlk yüklemede doğru listeyi seç
                var initialColumnType = string.IsNullOrWhiteSpace(rule.ColumnType) ? "Yan Ödeme" : rule.ColumnType;
                var initialOptionsList = new List<string>();
                
                if (initialColumnType == "Kesintiler")
                {
                    initialOptionsList.AddRange(ConditionalDeductionsColumnOptions);
                }
                else
                {
                    initialOptionsList.AddRange(ConditionalEarningsColumnOptions);
                }
                
                // Manuel sütunları ekle
                if (CurrentCompany?.ConditionalEarningsSettings?.ManualColumns != null)
                {
                    var manualColumns = CurrentCompany.ConditionalEarningsSettings.ManualColumns
                        .Where(m => m.ColumnType == initialColumnType)
                        .Select(m => m.ColumnName + " (Manuel)")
                        .ToList();
                    initialOptionsList.AddRange(manualColumns);
                }
                
                cmbTargetColumn.ItemsSource = initialOptionsList;
                
                // Mevcut sütun adını bul (manuel etiketi varsa onu kontrol et)
                var targetName = rule.TargetColumnName;
                if (!string.IsNullOrWhiteSpace(targetName))
                {
                    // Manuel etiketi olmayan sütun adı için manuel etiketli olanı da kontrol et
                    var foundItem = initialOptionsList.FirstOrDefault(c => 
                        c == targetName || 
                        c == targetName + " (Manuel)" ||
                        (targetName.EndsWith(" (Manuel)") && c == targetName));
                    
                    if (foundItem != null)
                    {
                        cmbTargetColumn.SelectedItem = foundItem;
                    }
                    else if (initialOptionsList.Count > 0)
                    {
                        cmbTargetColumn.SelectedItem = initialOptionsList[0];
                    }
                }
                else if (initialOptionsList.Count > 0)
                {
                    cmbTargetColumn.SelectedItem = initialOptionsList[0];
                }
                
                cmbColumnType.SelectionChanged += (s, e) =>
                {
                    UpdateTargetColumnOptions();
                };
                
                cmbTargetColumn.SelectionChanged += (s, e) =>
                {
                    if (cmbTargetColumn.SelectedItem is string selectedColumn)
                    {
                        var letter = GetLetterForConditionalEarningsColumn(selectedColumn);
                        txtColumnLetter.Text = letter;
                    }
                };
                
                Grid.SetRow(targetColumnPanel, row);
                Grid.SetColumn(targetColumnPanel, 1);
                row++;

                // Sütun Harfi Label
                var lblColumnLetter = new TextBlock
                {
                    Text = "Sütun Harfi:",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 8),
                    FontFamily = poppinsFont,
                    FontSize = modalFontSize
                };
                Grid.SetRow(lblColumnLetter, row);
                Grid.SetColumn(lblColumnLetter, 0);

                Grid.SetRow(txtColumnLetter, row);
                Grid.SetColumn(txtColumnLetter, 1);
                row++;

                // Değer Tipi
                var lblValueType = new TextBlock
                {
                    Text = "Değer Tipi:",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 8),
                    FontFamily = poppinsFont,
                    FontSize = modalFontSize
                };
                Grid.SetRow(lblValueType, row);
                Grid.SetColumn(lblValueType, 0);

                // Değer Tipi için filtrelenmiş liste (başlangıçta tüm seçenekler)
                var filteredValueTypeOptions = new ObservableCollection<string>(ValueTypeOptions);
                
                var cmbValueType = new ComboBox
                {
                    ItemsSource = filteredValueTypeOptions,
                    SelectedItem = ValueTypeOptions.FirstOrDefault(v => v == rule.ValueType) ?? ValueTypeOptions[0],
                    Margin = new Thickness(0, 0, 0, 8),
                    Height = 28,
                    FontFamily = poppinsFont,
                    FontSize = modalFontSize
                };
                Grid.SetRow(cmbValueType, row);
                Grid.SetColumn(cmbValueType, 1);
                row++;

                // Koşul Tipi'ne göre Değer Tipi seçeneklerini filtrele
                void UpdateValueTypeOptions()
                {
                    var selectedConditionType = cmbConditionType.SelectedItem?.ToString() ?? "Koşulsuz";
                    
                    // Tüm seçenekleri temizle
                    filteredValueTypeOptions.Clear();
                    
                    // Her zaman "Sabit" ekle
                    filteredValueTypeOptions.Add("Sabit");
                    
                    // Koşul Tipi'ne göre ilgili seçeneği ekle
                    switch (selectedConditionType)
                    {
                        case "Devamsızlık Günü":
                            filteredValueTypeOptions.Add("Devamsızlık Günü x Tutar");
                            break;
                        case "Eksik Gün":
                            filteredValueTypeOptions.Add("Eksik Gün x Tutar");
                            break;
                        case "Eksik Saat":
                            filteredValueTypeOptions.Add("Eksik Saat x Tutar");
                            break;
                        case "Fazla Mesai Saati":
                            filteredValueTypeOptions.Add("Fazla Mesai Saati x Tutar");
                            break;
                        case "Tatil Günü":
                            // Tatil Günü için özel bir "x Tutar" seçeneği yok, sadece Sabit
                            break;
                        case "Koşulsuz":
                            // Koşulsuz ise tüm seçenekler görünsün
                            filteredValueTypeOptions.Add("Fazla Mesai Saati x Tutar");
                            filteredValueTypeOptions.Add("Eksik Saat x Tutar");
                            filteredValueTypeOptions.Add("Eksik Gün x Tutar");
                            filteredValueTypeOptions.Add("Devamsızlık Günü x Tutar");
                            break;
                    }
                    
                    // Eğer mevcut seçili değer filtrelenmiş listede yoksa, "Sabit" seç
                    var currentSelection = cmbValueType.SelectedItem?.ToString() ?? "";
                    if (!filteredValueTypeOptions.Contains(currentSelection))
                    {
                        cmbValueType.SelectedItem = "Sabit";
                    }
                }

                // Değer tipi açıklama metni - Değer Tipi dropdown'ının hemen altında (yeni satır)
                var txtValueTypeInfo = new TextBlock
                {
                    Text = "",
                    FontSize = 11,
                    FontFamily = poppinsFont,
                    Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(59, 130, 246)),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 2, 0, 8),
                    Visibility = Visibility.Collapsed
                };
                Grid.SetRow(txtValueTypeInfo, row); // Dropdown'ın hemen altındaki satır
                Grid.SetColumn(txtValueTypeInfo, 1);
                Grid.SetColumnSpan(txtValueTypeInfo, 1);
                row++; // Açıklama metni için satır kullanıldı

                // Kazanç Değeri - Label'ı değer tipine göre dinamik yap
                var lblEarningsValue = new TextBlock
                {
                    Text = "Kazanç Değeri:",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 8),
                    FontFamily = poppinsFont,
                    FontSize = modalFontSize
                };
                Grid.SetRow(lblEarningsValue, row);
                Grid.SetColumn(lblEarningsValue, 0);

                var txtEarningsValue = new TextBox
                {
                    Text = rule.EarningsValue.ToString(CultureInfo.InvariantCulture),
                    Margin = new Thickness(0, 0, 0, 8),
                    Height = 28,
                    FontFamily = poppinsFont,
                    FontSize = modalFontSize
                };
                Grid.SetRow(txtEarningsValue, row);
                Grid.SetColumn(txtEarningsValue, 1);

                // Değer tipine göre label'ı ve açıklama metnini güncelle
                void UpdateEarningsValueLabel()
                {
                    var selectedValueType = cmbValueType.SelectedItem?.ToString() ?? "Sabit";
                    
                    if (selectedValueType == "Fazla Mesai Saati x Tutar")
                    {
                        lblEarningsValue.Text = "Birim Başına Tutar:";
                        txtEarningsValue.ToolTip = "Örnek: 10 saat mesai ve birim başına 50 TL girerseniz, toplam 500 TL yazılır (10 x 50)";
                        txtValueTypeInfo.Text = "ℹ️ Personelin toplam fazla mesai saati (Normal + %50) ile girilen tutarı çarpar.\nKoşul tipinden bağımsız hesaplanır.";
                        txtValueTypeInfo.Visibility = Visibility.Visible;
                    }
                    else if (selectedValueType == "Eksik Saat x Tutar")
                    {
                        lblEarningsValue.Text = "Birim Başına Tutar:";
                        txtEarningsValue.ToolTip = "Eksik saat başına ödenecek tutarı girin.";
                        txtValueTypeInfo.Text = "ℹ️ Personelin eksik saat toplamı ile girilen tutarı çarpar.\nKoşul tipinden bağımsız hesaplanır.";
                        txtValueTypeInfo.Visibility = Visibility.Visible;
                    }
                    else if (selectedValueType == "Eksik Gün x Tutar")
                    {
                        lblEarningsValue.Text = "Birim Başına Tutar:";
                        txtEarningsValue.ToolTip = "Eksik gün başına ödenecek tutarı girin.";
                        txtValueTypeInfo.Text = "ℹ️ Personelin eksik gün sayısı ile girilen tutarı çarpar.\nKoşul tipinden bağımsız hesaplanır.";
                        txtValueTypeInfo.Visibility = Visibility.Visible;
                    }
                    else if (selectedValueType == "Devamsızlık Günü x Tutar")
                    {
                        lblEarningsValue.Text = "Birim Başına Tutar:";
                        txtEarningsValue.ToolTip = "Devamsızlık günü başına ödenecek tutarı girin.";
                        txtValueTypeInfo.Text = "ℹ️ Personelin devamsızlık günü sayısı ile girilen tutarı çarpar.\nKoşul tipinden bağımsız hesaplanır.";
                        txtValueTypeInfo.Visibility = Visibility.Visible;
                    }
                    else // Sabit
                    {
                        lblEarningsValue.Text = "Kazanç Değeri:";
                        txtEarningsValue.ToolTip = "Doğrudan yazılacak tutarı girin.";
                        txtValueTypeInfo.Text = "ℹ️ Girilen tutar direkt olarak yazılır. Koşul değeri dikkate alınmaz. Örnek: 3333 TL girerseniz → 3333 TL yazılır.";
                        txtValueTypeInfo.Visibility = Visibility.Visible;
                    }
                }

                cmbValueType.SelectionChanged += (s, e) => 
                {
                    UpdateEarningsValueLabel();
                };
                
                // Koşul Tipi değiştiğinde Değer Tipi'ni de güncelle
                cmbConditionType.SelectionChanged += (s, e) => 
                {
                    UpdateValueTypeOptions();
                };
                
                // Başlangıçta filtrele ve label'ı güncelle
                UpdateValueTypeOptions();
                UpdateEarningsValueLabel();
                row++;

                // Başlangıç Tarihi
                var lblStartDate = new TextBlock
                {
                    Text = "Başlangıç Tarihi:",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 8),
                    FontFamily = poppinsFont,
                    FontSize = modalFontSize
                };
                Grid.SetRow(lblStartDate, row);
                Grid.SetColumn(lblStartDate, 0);

                var dpStartDate = new DatePicker
                {
                    SelectedDate = rule.StartDate,
                    Margin = new Thickness(0, 0, 0, 8),
                    Height = 28,
                    FontFamily = poppinsFont,
                    FontSize = modalFontSize
                };
                Grid.SetRow(dpStartDate, row);
                Grid.SetColumn(dpStartDate, 1);
                row++;

                // Bitiş Tarihi
                var lblEndDate = new TextBlock
                {
                    Text = "Bitiş Tarihi:",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 8),
                    FontFamily = poppinsFont,
                    FontSize = modalFontSize
                };
                Grid.SetRow(lblEndDate, row);
                Grid.SetColumn(lblEndDate, 0);

                var dpEndDate = new DatePicker
                {
                    SelectedDate = rule.EndDate,
                    Margin = new Thickness(0, 0, 0, 8),
                    Height = 28,
                    FontFamily = poppinsFont,
                    FontSize = modalFontSize
                };
                Grid.SetRow(dpEndDate, row);
                Grid.SetColumn(dpEndDate, 1);
                row++;

                // Tarih bilgilendirmesi
                var txtDateInfo = new TextBlock
                {
                    Text = "Not: Tarih belirtilmezse kural her zaman geçerlidir. Bordro ayı bu tarih aralığında değilse kural uygulanmaz.",
                    FontSize = 11,
                    FontFamily = poppinsFont,
                    Foreground = System.Windows.Media.Brushes.Gray,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 8)
                };
                Grid.SetRow(txtDateInfo, row);
                Grid.SetColumn(txtDateInfo, 0);
                Grid.SetColumnSpan(txtDateInfo, 2);
                row++;

                // Açıklama
                var lblDescription = new TextBlock
                {
                    Text = "Açıklama:",
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 0, 8, 0),
                    FontFamily = poppinsFont,
                    FontSize = modalFontSize
                };
                Grid.SetRow(lblDescription, row);
                Grid.SetColumn(lblDescription, 0);

                var txtDescription = new TextBox
                {
                    Text = rule.Description,
                    AcceptsReturn = true,
                    TextWrapping = TextWrapping.Wrap,
                    Height = 70,
                    FontFamily = poppinsFont,
                    FontSize = modalFontSize
                };
                Grid.SetRow(txtDescription, row);
                Grid.SetColumn(txtDescription, 1);
                row++;

                // Aktif CheckBox - Butonların yanına taşındı
                var chkIsEnabled = new CheckBox
                {
                    Content = "Kural Aktif",
                    IsChecked = rule.IsEnabled,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 16, 0, 0),
                    FontFamily = poppinsFont,
                    FontSize = modalFontSize
                };
                Grid.SetRow(chkIsEnabled, row);
                Grid.SetColumn(chkIsEnabled, 0);

                var buttonsPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 16, 0, 0)
                };
                Grid.SetRow(buttonsPanel, row);
                Grid.SetColumn(buttonsPanel, 1);

                // Kaydet Butonu
                var btnOk = new Button
                {
                    Width = 110,
                    Height = 36,
                    Margin = new Thickness(0, 0, 10, 0),
                    IsDefault = true,
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(16, 185, 129)), // #10B981
                    Foreground = System.Windows.Media.Brushes.White,
                    BorderThickness = new Thickness(0),
                    FontSize = 14,
                    FontWeight = FontWeights.SemiBold,
                    FontFamily = new System.Windows.Media.FontFamily("pack://application:,,,/Fonts/Poppins-SemiBold.ttf#Poppins"),
                    Cursor = Cursors.Hand
                };
                
                // Kaydet butonu içeriği (Icon + Text)
                var btnOkContent = new Grid();
                btnOkContent.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                btnOkContent.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                
                var btnOkIcon = new TextBlock
                {
                    Text = "\uE74E", // Save icon (Segoe MDL2 Assets)
                    FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
                    FontSize = 16,
                    Foreground = System.Windows.Media.Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                Grid.SetColumn(btnOkIcon, 0);
                
                var btnOkText = new TextBlock
                {
                    Text = "Kaydet",
                    Foreground = System.Windows.Media.Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 14,
                    FontFamily = poppinsSemiBoldFont
                };
                Grid.SetColumn(btnOkText, 1);
                
                btnOkContent.Children.Add(btnOkIcon);
                btnOkContent.Children.Add(btnOkText);
                btnOk.Content = btnOkContent;
                
                // Buton template (Border radius için)
                var btnOkTemplate = new ControlTemplate(typeof(Button));
                var btnOkBorder = new FrameworkElementFactory(typeof(Border));
                btnOkBorder.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
                btnOkBorder.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Button.BorderThicknessProperty));
                btnOkBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
                btnOkBorder.SetValue(Border.PaddingProperty, new Thickness(12, 8, 12, 8));
                
                var btnOkPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
                btnOkPresenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
                btnOkPresenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
                
                btnOkBorder.AppendChild(btnOkPresenter);
                btnOkTemplate.VisualTree = btnOkBorder;
                btnOk.Template = btnOkTemplate;
                
                // Hover efekti
                btnOk.MouseEnter += (s, e) => 
                {
                    btnOk.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(5, 150, 105)); // Daha koyu yeşil
                };
                btnOk.MouseLeave += (s, e) => 
                {
                    btnOk.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(16, 185, 129));
                };

                // İptal Butonu
                var btnCancel = new Button
                {
                    Width = 100,
                    Height = 36,
                    IsCancel = true,
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(107, 114, 128)), // #6B7280
                    Foreground = System.Windows.Media.Brushes.White,
                    BorderThickness = new Thickness(0),
                    FontSize = 14,
                    FontWeight = FontWeights.SemiBold,
                    FontFamily = new System.Windows.Media.FontFamily("pack://application:,,,/Fonts/Poppins-SemiBold.ttf#Poppins"),
                    Cursor = Cursors.Hand
                };
                
                // İptal butonu içeriği (Icon + Text)
                var btnCancelContent = new Grid();
                btnCancelContent.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                btnCancelContent.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                
                var btnCancelIcon = new TextBlock
                {
                    Text = "\uE711", // Cancel icon (Segoe MDL2 Assets)
                    FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
                    FontSize = 16,
                    Foreground = System.Windows.Media.Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                Grid.SetColumn(btnCancelIcon, 0);
                
                var btnCancelText = new TextBlock
                {
                    Text = "İptal",
                    Foreground = System.Windows.Media.Brushes.White,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 14,
                    FontFamily = poppinsSemiBoldFont
                };
                Grid.SetColumn(btnCancelText, 1);
                
                btnCancelContent.Children.Add(btnCancelIcon);
                btnCancelContent.Children.Add(btnCancelText);
                btnCancel.Content = btnCancelContent;
                
                // Buton template (Border radius için)
                var btnCancelTemplate = new ControlTemplate(typeof(Button));
                var btnCancelBorder = new FrameworkElementFactory(typeof(Border));
                btnCancelBorder.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
                btnCancelBorder.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Button.BorderThicknessProperty));
                btnCancelBorder.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
                btnCancelBorder.SetValue(Border.PaddingProperty, new Thickness(12, 8, 12, 8));
                
                var btnCancelPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
                btnCancelPresenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
                btnCancelPresenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
                
                btnCancelBorder.AppendChild(btnCancelPresenter);
                btnCancelTemplate.VisualTree = btnCancelBorder;
                btnCancel.Template = btnCancelTemplate;
                
                // Hover efekti
                btnCancel.MouseEnter += (s, e) => 
                {
                    btnCancel.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(75, 85, 99)); // Daha koyu gri
                };
                btnCancel.MouseLeave += (s, e) => 
                {
                    btnCancel.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(107, 114, 128));
                };

                btnOk.Click += (_, _) =>
                {
                    bool isUnconditional = cmbConditionType.SelectedItem?.ToString() == "Koşulsuz";
                    double conditionValue = 0;

                    // Koşulsuz değilse koşul değerini kontrol et (artık her zaman sabit değer)
                    if (!isUnconditional)
                    {
                        if (!double.TryParse(txtConditionValue.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out conditionValue))
                        {
                            MessageBox.Show(editWindow, "Koşul değeri geçerli bir sayı olmalıdır.", "Uyarı",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }
                    }

                    if (!double.TryParse(txtEarningsValue.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var earningsValue))
                    {
                        MessageBox.Show(editWindow, "Kazanç değeri geçerli bir sayı olmalıdır.", "Uyarı",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    // Tarih kontrolü - Bordro ayı ile uyumlu olmalı
                    var payrollYear = CurrentCompany?.PayrollYear > 0 ? CurrentCompany.PayrollYear : DateTime.Now.Year;
                    var payrollMonth = CurrentCompany?.PayrollMonth > 0 ? CurrentCompany.PayrollMonth : DateTime.Now.Month;
                    var payrollStartDate = new DateTime(payrollYear, payrollMonth, 1);
                    var payrollEndDate = payrollStartDate.AddMonths(1).AddDays(-1);

                    var startDate = dpStartDate.SelectedDate;
                    var endDate = dpEndDate.SelectedDate;

                    // Tarih aralığı varsa bordro ayı ile kesişmeli
                    if (startDate.HasValue || endDate.HasValue)
                    {
                        if (startDate.HasValue && endDate.HasValue && startDate.Value > endDate.Value)
                        {
                            MessageBox.Show(editWindow, "Başlangıç tarihi bitiş tarihinden sonra olamaz.", "Uyarı",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }

                        // Bordro ayı ile kesişim kontrolü
                        bool intersects = true;
                        if (startDate.HasValue && startDate.Value > payrollEndDate)
                        {
                            intersects = false;
                        }
                        if (endDate.HasValue && endDate.Value < payrollStartDate)
                        {
                            intersects = false;
                        }

                        if (!intersects)
                        {
                            var result = MessageBox.Show(
                                $"Seçilen tarih aralığı ({startDate?.ToString("dd.MM.yyyy") ?? "Başlangıçsız"} - {endDate?.ToString("dd.MM.yyyy") ?? "Bitişsiz"}) " +
                                $"bordro ayı ({payrollStartDate:dd.MM.yyyy} - {payrollEndDate:dd.MM.yyyy}) ile kesişmiyor.\n\n" +
                                $"Bu kural bu bordro ayı için uygulanmayacaktır. Devam etmek istiyor musunuz?",
                                "Tarih Uyarısı",
                                MessageBoxButton.YesNo,
                                MessageBoxImage.Warning);
                            
                            if (result != MessageBoxResult.Yes)
                            {
                                return;
                            }
                        }
                    }

                    rule.ConditionType = cmbConditionType.SelectedItem?.ToString() ?? string.Empty;
                    rule.ConditionOperator = isUnconditional ? string.Empty : (cmbOperator.SelectedItem?.ToString() ?? ">");
                    
                    // Artık hep "Sabit" (elle girilen değer)
                    rule.ConditionValueSource = "Sabit";
                    rule.ConditionValue = conditionValue;
                    
                    rule.ColumnType = cmbColumnType.SelectedItem?.ToString() ?? "Yan Ödeme";
                    rule.TargetColumnName = cmbTargetColumn.SelectedItem?.ToString() ?? string.Empty;
                    rule.TargetColumnLetter = txtColumnLetter.Text;
                    rule.EarningsValue = earningsValue;
                    rule.ValueType = cmbValueType.SelectedItem?.ToString() ?? "Sabit";
                    rule.StartDate = startDate;
                    rule.EndDate = endDate;
                    rule.Description = txtDescription.Text.Trim();
                    rule.IsEnabled = chkIsEnabled.IsChecked == true;

                    if (isNew && CurrentCompany?.ConditionalEarningsSettings?.Rules != null)
                    {
                        CurrentCompany.ConditionalEarningsSettings.Rules.Add(rule);
                    }

                    var dgRules = editWindow.Owner?.FindName("dgConditionalRules") as System.Windows.Controls.DataGrid;
                    if (dgRules != null && CurrentCompany?.ConditionalEarningsSettings?.Rules != null)
                    {
                        dgRules.ItemsSource = null;
                        dgRules.ItemsSource = CurrentCompany.ConditionalEarningsSettings.Rules;
                    }
                    OnPropertyChanged(nameof(CurrentCompany));

                    editWindow.DialogResult = true;
                    editWindow.Close();
                };

                buttonsPanel.Children.Add(btnOk);
                buttonsPanel.Children.Add(btnCancel);

                grid.Children.Add(lblConditionType);
                grid.Children.Add(cmbConditionType);
                grid.Children.Add(lblOperator);
                grid.Children.Add(cmbOperator);
                // grid.Children.Add(lblConditionValueSource); // KALDIRILDI
                // grid.Children.Add(cmbConditionValueSource); // KALDIRILDI
                // grid.Children.Add(txtConditionValueSourceInfo); // KALDIRILDI
                grid.Children.Add(lblConditionValue);
                grid.Children.Add(txtConditionValue);
                grid.Children.Add(txtConditionValueInfo); // EKLENDİ
                grid.Children.Add(lblColumnType);
                grid.Children.Add(cmbColumnType);
                grid.Children.Add(lblTargetColumn);
                grid.Children.Add(targetColumnPanel); // cmbTargetColumn artık targetColumnPanel içinde
                grid.Children.Add(lblColumnLetter);
                grid.Children.Add(txtColumnLetter);
                grid.Children.Add(lblValueType);
                grid.Children.Add(cmbValueType);
                grid.Children.Add(txtValueTypeInfo);
                grid.Children.Add(lblEarningsValue);
                grid.Children.Add(txtEarningsValue);
                grid.Children.Add(lblStartDate);
                grid.Children.Add(dpStartDate);
                grid.Children.Add(lblEndDate);
                grid.Children.Add(dpEndDate);
                grid.Children.Add(txtDateInfo);
                grid.Children.Add(lblDescription);
                grid.Children.Add(txtDescription);
                grid.Children.Add(chkIsEnabled);
                grid.Children.Add(buttonsPanel);

                // ScrollViewer ekle - içerik uzun olabilir
                var scrollViewer = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Content = grid,
                    Padding = new Thickness(0)
                };

                mainContainer.Children.Add(scrollViewer);

                // Genişleyen Panel (sağ taraf - başlangıçta gizli ama görünür, sadece genişlik 0)
                var expandPanel = CreateExpandablePanel(editWindow, cmbColumnType, cmbTargetColumn, txtColumnLetter, UpdateTargetColumnOptions, originalWidth, expandedWidth, expandColumnRef);
                expandPanel.Visibility = Visibility.Visible; // İçerik render edilsin
                expandPanel.Width = 0; // Başlangıçta genişlik 0
                expandPanel.HorizontalAlignment = HorizontalAlignment.Stretch; // Tam genişlik
                expandPanel.VerticalAlignment = VerticalAlignment.Stretch;
                Grid.SetColumn(expandPanel, 1);
                mainContainer.Children.Add(expandPanel);

                // + Butonu click handler
                btnAddManualColumn.Click += (s, e) =>
                {
                    if (isExpanded)
                    {
                        // Kapat
                        ToggleExpandPanel(editWindow, expandPanel, expandColumnRef, originalWidth, expandedWidth, false);
                        isExpanded = false;
                    }
                    else
                    {
                        // Aç
                        ToggleExpandPanel(editWindow, expandPanel, expandColumnRef, originalWidth, expandedWidth, true);
                        isExpanded = true;
                    }
                };

                editWindow.Content = mainContainer;
                editWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Kural düzenlenirken hata oluştu: {ex.Message}", "Hata",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private Border CreateExpandablePanel(Window parentWindow, ComboBox cmbColumnType, ComboBox targetColumnComboBox, TextBox columnLetterTextBox, Action updateTargetColumnOptions, double originalWidth, double expandedWidth, ColumnDefinition expandColumn)
        {
            var poppinsFont = new System.Windows.Media.FontFamily("pack://application:,,,/Fonts/Poppins-Regular.ttf#Poppins");
            var modalFontSize = 13;

            var panel = new Border
            {
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(248, 249, 250)),
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(229, 231, 235)),
                BorderThickness = new Thickness(1, 0, 0, 0), // Sol tarafta dikey çizgi
                Padding = new Thickness(20),
                MinWidth = 300,
                MaxWidth = 300,
                Width = 0, // Başlangıçta 0, animasyonla genişleyecek
                ClipToBounds = true, // Animasyon sırasında taşmayı önle
                VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalAlignment = HorizontalAlignment.Stretch // Grid column'un tamamını kaplasın
            };

            var panelGrid = new Grid();
            panelGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Başlık
            panelGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Excel Dosyası Label
            panelGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Excel Path
            panelGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Excel Seç Butonu
            panelGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Sütun Seç Label
            panelGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Sütun ComboBox
            panelGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Separator
            panelGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Veya Label
            panelGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Manuel Sütun Adı Label
            panelGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Manuel Sütun Adı TextBox
            panelGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Manuel Sütun Harfi Label
            panelGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Manuel Sütun Harfi TextBox
            panelGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Butonlar

            int row = 0;

            // Başlık
            var lblTitle = new TextBlock
            {
                Text = "Yeni Sütun Ekle",
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 15),
                FontFamily = poppinsFont
            };
            Grid.SetRow(lblTitle, row++);

            // Excel Dosyası
            var lblExcelFile = new TextBlock
            {
                Text = "Excel Şablonu:",
                FontSize = modalFontSize,
                Margin = new Thickness(0, 0, 0, 5),
                FontFamily = poppinsFont
            };
            Grid.SetRow(lblExcelFile, row++);

            var txtExcelPath = new TextBox
            {
                Text = CurrentCompany?.ErpTemplatePath ?? "",
                IsReadOnly = true,
                Height = 28,
                Margin = new Thickness(0, 0, 0, 5),
                FontFamily = poppinsFont,
                FontSize = modalFontSize
            };
            Grid.SetRow(txtExcelPath, row++);

            var btnSelectExcel = new Button
            {
                Content = "Excel Seç",
                Width = 100,
                Height = 28,
                Margin = new Thickness(0, 0, 0, 15),
                FontFamily = poppinsFont,
                FontSize = modalFontSize,
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(59, 130, 246)),
                Foreground = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };
            Grid.SetRow(btnSelectExcel, row++);

            // Sütun Listesi
            var lblColumnList = new TextBlock
            {
                Text = "Sütun Seç:",
                FontSize = modalFontSize,
                Margin = new Thickness(0, 0, 0, 5),
                FontFamily = poppinsFont
            };
            Grid.SetRow(lblColumnList, row++);

            var cmbExcelColumns = new ComboBox
            {
                Height = 28,
                Margin = new Thickness(0, 0, 0, 15),
                FontFamily = poppinsFont,
                FontSize = modalFontSize,
                DisplayMemberPath = "DisplayName"
            };
            Grid.SetRow(cmbExcelColumns, row++);

            // Veya Ayırıcı
            var separator = new Separator
            {
                Margin = new Thickness(0, 5, 0, 10)
            };
            Grid.SetRow(separator, row++);

            var lblOr = new TextBlock
            {
                Text = "Veya Manuel Gir:",
                FontSize = modalFontSize,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 10),
                FontFamily = poppinsFont
            };
            Grid.SetRow(lblOr, row++);

            // Manuel Sütun Adı
            var lblManualName = new TextBlock
            {
                Text = "Sütun Adı:",
                FontSize = modalFontSize,
                Margin = new Thickness(0, 0, 0, 5),
                FontFamily = poppinsFont
            };
            Grid.SetRow(lblManualName, row++);

            var txtManualName = new TextBox
            {
                Height = 28,
                Margin = new Thickness(0, 0, 0, 10),
                FontFamily = poppinsFont,
                FontSize = modalFontSize
            };
            Grid.SetRow(txtManualName, row++);

            // Manuel Sütun Harfi
            var lblManualLetter = new TextBlock
            {
                Text = "Sütun Harfi:",
                FontSize = modalFontSize,
                Margin = new Thickness(0, 0, 0, 5),
                FontFamily = poppinsFont
            };
            Grid.SetRow(lblManualLetter, row++);

            var txtManualLetter = new TextBox
            {
                Height = 28,
                Text = "CI",
                Margin = new Thickness(0, 0, 0, 20),
                FontFamily = poppinsFont,
                FontSize = modalFontSize
            };
            Grid.SetRow(txtManualLetter, row++);

            // Butonlar
            var btnSave = new Button
            {
                Content = "Kaydet",
                Width = 120,
                Height = 32,
                Margin = new Thickness(0, 0, 5, 0),
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(16, 185, 129)),
                Foreground = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0),
                FontSize = modalFontSize,
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand
            };

            var btnCancelExpand = new Button
            {
                Content = "İptal",
                Width = 120,
                Height = 32,
                Margin = new Thickness(5, 0, 0, 0),
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(107, 114, 128)),
                Foreground = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0),
                FontSize = modalFontSize,
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand
            };

            var buttonsStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 10, 0, 0)
            };
            buttonsStack.Children.Add(btnSave);
            buttonsStack.Children.Add(btnCancelExpand);
            Grid.SetRow(buttonsStack, row++);

            panelGrid.Children.Add(lblTitle);
            panelGrid.Children.Add(lblExcelFile);
            panelGrid.Children.Add(txtExcelPath);
            panelGrid.Children.Add(btnSelectExcel);
            panelGrid.Children.Add(lblColumnList);
            panelGrid.Children.Add(cmbExcelColumns);
            panelGrid.Children.Add(separator);
            panelGrid.Children.Add(lblOr);
            panelGrid.Children.Add(lblManualName);
            panelGrid.Children.Add(txtManualName);
            panelGrid.Children.Add(lblManualLetter);
            panelGrid.Children.Add(txtManualLetter);
            panelGrid.Children.Add(buttonsStack);

            // Panel içeriği için ScrollViewer ekle
            var panelScrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = panelGrid,
                Padding = new Thickness(0)
            };

            panel.Child = panelScrollViewer;

            // Excel dosyası seçme
            btnSelectExcel.Click += (s, e) =>
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "ERP Excel Şablonunu Seçin",
                    Filter = "Excel Dosyaları (*.xlsx;*.xls)|*.xlsx;*.xls|Tüm Dosyalar (*.*)|*.*",
                    InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                };

                if (dialog.ShowDialog() == true)
                {
                    txtExcelPath.Text = dialog.FileName;
                    LoadExcelColumns(dialog.FileName, cmbExcelColumns);
                }
            };

            // İlk yüklemede ERP şablonunu yükle
            if (!string.IsNullOrWhiteSpace(CurrentCompany?.ErpTemplatePath) && File.Exists(CurrentCompany.ErpTemplatePath))
            {
                LoadExcelColumns(CurrentCompany.ErpTemplatePath, cmbExcelColumns);
            }

            // Excel sütunu seçildiğinde otomatik doldur
            cmbExcelColumns.SelectionChanged += (s, e) =>
            {
                if (cmbExcelColumns.SelectedItem != null)
                {
                    var selectedType = cmbExcelColumns.SelectedItem.GetType();
                    var nameProp = selectedType.GetProperty("ColumnName");
                    var letterProp = selectedType.GetProperty("ColumnLetter");
                    
                    if (nameProp != null && letterProp != null)
                    {
                        txtManualName.Text = nameProp.GetValue(cmbExcelColumns.SelectedItem)?.ToString() ?? "";
                        txtManualLetter.Text = letterProp.GetValue(cmbExcelColumns.SelectedItem)?.ToString() ?? "";
                    }
                }
            };

            // Kaydet butonu
            btnSave.Click += (s, e) =>
            {
                string columnName = "";
                string columnLetter = "";

                // Excel'den seçim varsa onu kullan
                if (cmbExcelColumns.SelectedItem != null)
                {
                    var selectedType = cmbExcelColumns.SelectedItem.GetType();
                    var nameProp = selectedType.GetProperty("ColumnName");
                    var letterProp = selectedType.GetProperty("ColumnLetter");
                    
                    if (nameProp != null && letterProp != null)
                    {
                        columnName = nameProp.GetValue(cmbExcelColumns.SelectedItem)?.ToString()?.Trim() ?? "";
                        columnLetter = letterProp.GetValue(cmbExcelColumns.SelectedItem)?.ToString()?.Trim().ToUpper() ?? "";
                    }
                }
                
                // Excel'den seçim yoksa manuel girişi kullan
                if (string.IsNullOrWhiteSpace(columnName))
                {
                    columnName = txtManualName.Text.Trim();
                    columnLetter = txtManualLetter.Text.Trim().ToUpper();
                }

                if (string.IsNullOrWhiteSpace(columnName))
                {
                    MessageBox.Show(parentWindow, "Lütfen sütun adı girin.", "Uyarı",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (string.IsNullOrWhiteSpace(columnLetter))
                {
                    MessageBox.Show(parentWindow, "Lütfen sütun harfi girin.", "Uyarı",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var columnType = cmbColumnType.SelectedItem?.ToString() ?? "Yan Ödeme";

                // Manuel sütunu ekle
                if (CurrentCompany?.ConditionalEarningsSettings == null)
                {
                    CurrentCompany.ConditionalEarningsSettings = new ConditionalEarningsSettings();
                }
                if (CurrentCompany.ConditionalEarningsSettings.ManualColumns == null)
                {
                    CurrentCompany.ConditionalEarningsSettings.ManualColumns = new List<ManualColumn>();
                }

                // Aynı sütun zaten var mı kontrol et
                if (CurrentCompany.ConditionalEarningsSettings.ManualColumns.Any(m => 
                    m.ColumnName.Equals(columnName, StringComparison.OrdinalIgnoreCase) &&
                    m.ColumnType == columnType))
                {
                    MessageBox.Show(parentWindow, "Bu sütun zaten eklenmiş.", "Uyarı",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var manualColumn = new ManualColumn
                {
                    ColumnName = columnName,
                    ColumnLetter = columnLetter,
                    ColumnType = columnType
                };

                CurrentCompany.ConditionalEarningsSettings.ManualColumns.Add(manualColumn);

                // Sözlüğe ekle (Manuel etiketiyle)
                string displayName = columnName + " (Manuel)";
                ConditionalEarningsColumnLetterMap[displayName] = columnLetter;

                // ComboBox'ı güncelle
                updateTargetColumnOptions?.Invoke();
                
                // Eklenen sütunu seç
                targetColumnComboBox.SelectedItem = displayName;
                columnLetterTextBox.Text = columnLetter;

                OnPropertyChanged(nameof(CurrentCompany));

                // Panel'i kapat
                ToggleExpandPanel(parentWindow, panel, expandColumn, originalWidth, expandedWidth, false);

                // Alanları temizle
                txtManualName.Text = "";
                txtManualLetter.Text = "CI";
                cmbExcelColumns.SelectedItem = null;
            };

            // İptal butonu
            btnCancelExpand.Click += (s, e) =>
            {
                ToggleExpandPanel(parentWindow, panel, expandColumn, originalWidth, expandedWidth, false);

                // Alanları temizle
                txtManualName.Text = "";
                txtManualLetter.Text = "CI";
                cmbExcelColumns.SelectedItem = null;
            };

            return panel;
        }

        private void ToggleExpandPanel(Window window, Border expandPanel, ColumnDefinition expandColumn, double originalWidth, double expandedWidth, bool expand)
        {
            if (expandColumn == null) return;

            double targetPanelWidth = expand ? 300 : 0;
            double fromPanelWidth = expand ? 0 : 300;

            double targetWindowWidth = expand ? expandedWidth : originalWidth;
            double fromWindowWidth = expand ? originalWidth : expandedWidth;

            if (expand)
            {
                expandColumn.Width = new GridLength(300);
                expandColumn.MaxWidth = 300;
                expandPanel.Visibility = Visibility.Visible;
            }
            else
            {
                // Kapanırken görünür kalsın, animasyon tamamlanınca gizlenecek
                expandPanel.Visibility = Visibility.Visible;
            }

            var panelAnimation = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = fromPanelWidth,
                To = targetPanelWidth,
                Duration = new System.Windows.Duration(TimeSpan.FromMilliseconds(300)),
                EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut }
            };

            panelAnimation.Completed += (_, _) =>
            {
                expandPanel.Width = targetPanelWidth;
                if (!expand)
                {
                    expandColumn.Width = new GridLength(0);
                    expandColumn.MaxWidth = 0;
                    expandPanel.Visibility = Visibility.Collapsed;
                }
            };

            expandPanel.BeginAnimation(FrameworkElement.WidthProperty, panelAnimation);

            var windowAnimation = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = fromWindowWidth,
                To = targetWindowWidth,
                Duration = new System.Windows.Duration(TimeSpan.FromMilliseconds(300)),
                EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseInOut }
            };

            window.BeginAnimation(FrameworkElement.WidthProperty, windowAnimation);
        }

        private void ShowManualColumnDialog(string columnType, ComboBox targetColumnComboBox, TextBox columnLetterTextBox, Action updateTargetColumnOptions)
        {
            try
            {
                var poppinsFont = new System.Windows.Media.FontFamily("pack://application:,,,/Fonts/Poppins-Regular.ttf#Poppins");
                var modalFontSize = 13;

                var dialogWindow = new Window
                {
                    Title = "Manuel Sütun Ekle",
                    Width = 600,
                    Height = 450,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    ResizeMode = ResizeMode.CanResize,
                    Owner = this,
                    FontFamily = poppinsFont
                };

                var grid = new Grid
                {
                    Margin = new Thickness(20)
                };
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Excel Dosyası
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Sütun Listesi
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Veya
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Manuel Giriş - Adı
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Manuel Giriş - Harfi
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Butonlar

                int row = 0;

                // Excel Dosyası Seç
                var lblExcelFile = new TextBlock
                {
                    Text = "Excel Şablonu:",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 8),
                    FontFamily = poppinsFont,
                    FontSize = modalFontSize
                };
                Grid.SetRow(lblExcelFile, row);
                Grid.SetColumn(lblExcelFile, 0);

                var txtExcelPath = new TextBox
                {
                    Text = CurrentCompany?.ErpTemplatePath ?? "",
                    IsReadOnly = true,
                    Margin = new Thickness(0, 0, 8, 8),
                    Height = 28,
                    FontFamily = poppinsFont,
                    FontSize = modalFontSize
                };

                var btnSelectExcel = new Button
                {
                    Content = "Excel Seç",
                    Width = 100,
                    Height = 28,
                    Margin = new Thickness(0, 0, 0, 8),
                    FontFamily = poppinsFont,
                    FontSize = modalFontSize
                };

                var excelPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 0, 0, 8)
                };
                excelPanel.Children.Add(txtExcelPath);
                excelPanel.Children.Add(btnSelectExcel);

                Grid.SetRow(excelPanel, row);
                Grid.SetColumn(excelPanel, 1);
                row++;

                // Excel'den Sütun Listesi
                var lblColumnList = new TextBlock
                {
                    Text = "Sütun Seç:",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 8),
                    FontFamily = poppinsFont,
                    FontSize = modalFontSize
                };
                Grid.SetRow(lblColumnList, row);
                Grid.SetColumn(lblColumnList, 0);

                var cmbExcelColumns = new ComboBox
                {
                    Height = 28,
                    Margin = new Thickness(0, 0, 0, 8),
                    FontFamily = poppinsFont,
                    FontSize = modalFontSize,
                    DisplayMemberPath = "DisplayName"
                };
                Grid.SetRow(cmbExcelColumns, row);
                Grid.SetColumn(cmbExcelColumns, 1);
                row++;

                // Veya Ayırıcı
                var separator = new Separator
                {
                    Margin = new Thickness(0, 10, 0, 10)
                };
                Grid.SetRow(separator, row);
                Grid.SetColumn(separator, 0);
                Grid.SetColumnSpan(separator, 2);
                row++;

                var lblOr = new TextBlock
                {
                    Text = "Veya Manuel Gir:",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 8),
                    FontFamily = poppinsFont,
                    FontSize = modalFontSize,
                    FontWeight = FontWeights.SemiBold
                };
                Grid.SetRow(lblOr, row);
                Grid.SetColumn(lblOr, 0);
                Grid.SetColumnSpan(lblOr, 2);
                row++;

                // Manuel Sütun Adı
                var lblManualName = new TextBlock
                {
                    Text = "Sütun Adı:",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 8),
                    FontFamily = poppinsFont,
                    FontSize = modalFontSize
                };
                Grid.SetRow(lblManualName, row);
                Grid.SetColumn(lblManualName, 0);

                var txtManualName = new TextBox
                {
                    Margin = new Thickness(0, 0, 0, 8),
                    Height = 28,
                    FontFamily = poppinsFont,
                    FontSize = modalFontSize
                };
                Grid.SetRow(txtManualName, row);
                Grid.SetColumn(txtManualName, 1);
                row++;

                // Manuel Sütun Harfi
                var lblManualLetter = new TextBlock
                {
                    Text = "Sütun Harfi:",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 8),
                    FontFamily = poppinsFont,
                    FontSize = modalFontSize
                };
                Grid.SetRow(lblManualLetter, row);
                Grid.SetColumn(lblManualLetter, 0);

                var txtManualLetter = new TextBox
                {
                    Margin = new Thickness(0, 0, 0, 8),
                    Height = 28,
                    FontFamily = poppinsFont,
                    FontSize = modalFontSize,
                    Text = "CI"
                };
                Grid.SetRow(txtManualLetter, row);
                Grid.SetColumn(txtManualLetter, 1);
                row++;

                // Butonlar
                var buttonsPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Margin = new Thickness(0, 20, 0, 0)
                };

                var btnAdd = new Button
                {
                    Content = "Ekle",
                    Width = 100,
                    Height = 32,
                    Margin = new Thickness(0, 0, 10, 0),
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(16, 185, 129)),
                    Foreground = System.Windows.Media.Brushes.White,
                    BorderThickness = new Thickness(0),
                    FontSize = modalFontSize,
                    FontWeight = FontWeights.SemiBold,
                    Cursor = Cursors.Hand
                };

                var btnCancel = new Button
                {
                    Content = "İptal",
                    Width = 100,
                    Height = 32,
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(107, 114, 128)),
                    Foreground = System.Windows.Media.Brushes.White,
                    BorderThickness = new Thickness(0),
                    FontSize = modalFontSize,
                    FontWeight = FontWeights.SemiBold,
                    Cursor = Cursors.Hand,
                    IsCancel = true
                };

                buttonsPanel.Children.Add(btnAdd);
                buttonsPanel.Children.Add(btnCancel);

                Grid.SetRow(buttonsPanel, row);
                Grid.SetColumn(buttonsPanel, 0);
                Grid.SetColumnSpan(buttonsPanel, 2);

                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                grid.Children.Add(lblExcelFile);
                grid.Children.Add(excelPanel);
                grid.Children.Add(lblColumnList);
                grid.Children.Add(cmbExcelColumns);
                grid.Children.Add(separator);
                grid.Children.Add(lblOr);
                grid.Children.Add(lblManualName);
                grid.Children.Add(txtManualName);
                grid.Children.Add(lblManualLetter);
                grid.Children.Add(txtManualLetter);
                grid.Children.Add(buttonsPanel);

                dialogWindow.Content = grid;

                // Excel dosyası seçme
                btnSelectExcel.Click += (s, e) =>
                {
                    var dialog = new Microsoft.Win32.OpenFileDialog
                    {
                        Title = "ERP Excel Şablonunu Seçin",
                        Filter = "Excel Dosyaları (*.xlsx;*.xls)|*.xlsx;*.xls|Tüm Dosyalar (*.*)|*.*",
                        InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                    };

                    if (dialog.ShowDialog() == true)
                    {
                        txtExcelPath.Text = dialog.FileName;
                        LoadExcelColumns(dialog.FileName, cmbExcelColumns);
                    }
                };

                // İlk yüklemede ERP şablonunu yükle
                if (!string.IsNullOrWhiteSpace(CurrentCompany?.ErpTemplatePath) && File.Exists(CurrentCompany.ErpTemplatePath))
                {
                    LoadExcelColumns(CurrentCompany.ErpTemplatePath, cmbExcelColumns);
                }

                // Excel sütunu seçildiğinde otomatik doldur
                cmbExcelColumns.SelectionChanged += (s, e) =>
                {
                    if (cmbExcelColumns.SelectedItem != null)
                    {
                        var selectedType = cmbExcelColumns.SelectedItem.GetType();
                        var nameProp = selectedType.GetProperty("ColumnName");
                        var letterProp = selectedType.GetProperty("ColumnLetter");
                        
                        if (nameProp != null && letterProp != null)
                        {
                            txtManualName.Text = nameProp.GetValue(cmbExcelColumns.SelectedItem)?.ToString() ?? "";
                            txtManualLetter.Text = letterProp.GetValue(cmbExcelColumns.SelectedItem)?.ToString() ?? "";
                        }
                    }
                };

                // Ekle butonu
                btnAdd.Click += (s, e) =>
                {
                    string columnName = "";
                    string columnLetter = "";

                    // Excel'den seçim varsa onu kullan
                    if (cmbExcelColumns.SelectedItem != null)
                    {
                        var selectedType = cmbExcelColumns.SelectedItem.GetType();
                        var nameProp = selectedType.GetProperty("ColumnName");
                        var letterProp = selectedType.GetProperty("ColumnLetter");
                        
                        if (nameProp != null && letterProp != null)
                        {
                            columnName = nameProp.GetValue(cmbExcelColumns.SelectedItem)?.ToString()?.Trim() ?? "";
                            columnLetter = letterProp.GetValue(cmbExcelColumns.SelectedItem)?.ToString()?.Trim().ToUpper() ?? "";
                        }
                    }
                    
                    // Excel'den seçim yoksa manuel girişi kullan
                    if (string.IsNullOrWhiteSpace(columnName))
                    {
                        columnName = txtManualName.Text.Trim();
                        columnLetter = txtManualLetter.Text.Trim().ToUpper();
                    }

                    if (string.IsNullOrWhiteSpace(columnName))
                    {
                        MessageBox.Show(dialogWindow, "Lütfen sütun adı girin.", "Uyarı",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    if (string.IsNullOrWhiteSpace(columnLetter))
                    {
                        MessageBox.Show(dialogWindow, "Lütfen sütun harfi girin.", "Uyarı",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    // Manuel sütunu ekle
                    if (CurrentCompany?.ConditionalEarningsSettings == null)
                    {
                        CurrentCompany.ConditionalEarningsSettings = new ConditionalEarningsSettings();
                    }
                    if (CurrentCompany.ConditionalEarningsSettings.ManualColumns == null)
                    {
                        CurrentCompany.ConditionalEarningsSettings.ManualColumns = new List<ManualColumn>();
                    }

                    // Aynı sütun zaten var mı kontrol et
                    if (CurrentCompany.ConditionalEarningsSettings.ManualColumns.Any(m => 
                        m.ColumnName.Equals(columnName, StringComparison.OrdinalIgnoreCase) &&
                        m.ColumnType == columnType))
                    {
                        MessageBox.Show(dialogWindow, "Bu sütun zaten eklenmiş.", "Uyarı",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    var manualColumn = new ManualColumn
                    {
                        ColumnName = columnName,
                        ColumnLetter = columnLetter,
                        ColumnType = columnType
                    };

                    CurrentCompany.ConditionalEarningsSettings.ManualColumns.Add(manualColumn);

                    // Sözlüğe ekle (Manuel etiketiyle)
                    string displayName = columnName + " (Manuel)";
                    ConditionalEarningsColumnLetterMap[displayName] = columnLetter;

                    // ComboBox'ı güncelle
                    updateTargetColumnOptions?.Invoke();
                    
                    // Eklenen sütunu seç
                    targetColumnComboBox.SelectedItem = displayName;
                    columnLetterTextBox.Text = columnLetter;

                    OnPropertyChanged(nameof(CurrentCompany));
                    dialogWindow.DialogResult = true;
                    dialogWindow.Close();
                };

                dialogWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Manuel sütun eklenirken hata oluştu: {ex.Message}", "Hata",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadExcelColumns(string filePath, ComboBox comboBox)
        {
            try
            {
                OfficeOpenXml.ExcelPackage.License.SetNonCommercialPersonal("WebScraper");
                using (var package = new ExcelPackage(new FileInfo(filePath)))
                {
                    var sheet = package.Workbook.Worksheets.FirstOrDefault();
                    if (sheet == null || sheet.Dimension == null)
                    {
                        return;
                    }

                    var columns = new List<dynamic>();
                    int lastCol = sheet.Dimension.End.Column;

                    for (int col = 1; col <= lastCol; col++)
                    {
                        var headerValue = sheet.Cells[1, col].Value?.ToString()?.Trim();
                        if (!string.IsNullOrWhiteSpace(headerValue))
                        {
                            string columnLetter = GetExcelColumnLetter(col);
                            columns.Add(new
                            {
                                ColumnName = headerValue,
                                ColumnLetter = columnLetter,
                                DisplayName = $"{headerValue} ({columnLetter})"
                            });
                        }
                    }

                    comboBox.ItemsSource = columns;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Excel dosyası okunurken hata oluştu: {ex.Message}", "Hata",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

    }

    public class HolidaySelectionItem : INotifyPropertyChanged
    {
        private static readonly CultureInfo TurkishCulture = CultureInfo.GetCultureInfo("tr-TR");
        private bool _isSelected;
        private bool _treatAsHalfDay;

        public HolidaySelectionItem(HolidayInfo holiday, bool isSelected, bool? treatAsHalfDayOverride = null)
        {
            Date = holiday.Date.Date;
            Name = holiday.Name;
            DefaultIsHalfDay = holiday.IsHalfDay;
            _treatAsHalfDay = treatAsHalfDayOverride ?? holiday.IsHalfDay;
            _isSelected = isSelected;
        }

        public DateTime Date { get; }
        public string Name { get; }
        public bool DefaultIsHalfDay { get; }

        public string DisplayText
        {
            get
            {
                var dayName = TurkishCulture.DateTimeFormat.GetDayName(Date.DayOfWeek);
                var duration = TreatAsHalfDay ? "½ Gün" : "Tam Gün";
                return $"{Date:dd.MM.yyyy} {dayName} – {Name} ({duration})";
            }
        }

        public string TooltipText
        {
            get
            {
                var dayName = TurkishCulture.DateTimeFormat.GetDayName(Date.DayOfWeek);
                var durationText = TreatAsHalfDay ? "Yarım Gün" : "Tam Gün";
                return $"{Name}\n{dayName}, {Date:dd MMMM yyyy}\n{durationText}";
            }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                {
                    return;
                }

                _isSelected = value;
                OnPropertyChanged(nameof(IsSelected));
            }
        }

        public bool TreatAsHalfDay
        {
            get => _treatAsHalfDay;
            set
            {
                if (_treatAsHalfDay == value)
                {
                    return;
                }

                _treatAsHalfDay = value;
                OnPropertyChanged(nameof(TreatAsHalfDay));
                OnPropertyChanged(nameof(DisplayText));
                OnPropertyChanged(nameof(TooltipText));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    // Yardımcı Converter'lar
    public class ShiftPatternsConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is List<string> patterns)
            {
                if (patterns.Contains("*"))
                    return "Tüm vardiyalar";
                return string.Join(", ", patterns);
            }
            return "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class NullToVisibilityConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return value == null ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class LogoPathToImageConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            string? logoPath = value as string;
            string? resolvedPath = ResolvePath(logoPath);

            if (string.IsNullOrWhiteSpace(resolvedPath) || !File.Exists(resolvedPath))
            {
                resolvedPath = ResolvePath("Gemini_Generated_Image_vsio8jvsio8jvsio.png");
            }

            if (string.IsNullOrWhiteSpace(resolvedPath) || !File.Exists(resolvedPath))
            {
                return null;
            }

            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(resolvedPath, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                return bitmap;
            }
            catch
            {
                return null;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        private static string? ResolvePath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            if (System.IO.Path.IsPathRooted(path))
                return path;

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            return System.IO.Path.Combine(baseDir, path);
        }
    }
}
