using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.ComponentModel;
using System.Globalization;
using System.IO; // Added for File.Exists
using System.Diagnostics; // Added for Process.Start

namespace WebScraper
{
    public partial class ShiftRuleGroupModal : Window, INotifyPropertyChanged
    {
        private ShiftRuleConfig _currentGroup;
        private CompanyConfig _parentCompany;
        private bool _isNewGroup;
        private string _originalGroupName = string.Empty;

        public List<string> OvertimeColumnOptions { get; } = new List<string>
        {
            "B01 %50 Fazla Mesai",
            "B04 Fazla Mesai Normal"
        };

        public List<EksColumnOption> EksColumnOptions { get; } = new List<EksColumnOption>
        {
            new EksColumnOption("EKS-01", "01-İstirahat", "G"),
            new EksColumnOption("EKS-06", "06-Kısmi İstihdam", "H"),
            new EksColumnOption("EKS-07", "07-Puantaj Kayıtları", "I"),
            new EksColumnOption("EKS-13", "13-Diğer Nedenler", "J"),
            new EksColumnOption("EKS-15", "15-Devamsızlık", "K"),
            new EksColumnOption("EKS-16", "16-Fesih tarihinde çalışmamış", "L"),
            new EksColumnOption("EKS-21", "21-Diğer Ücretsiz İzin", "M")
        };

        public ShiftRuleConfig CurrentGroup
        {
            get => _currentGroup;
            set
            {
                _currentGroup = value;
                OnPropertyChanged(nameof(CurrentGroup));
                OnPropertyChanged(nameof(TitleText));
            }
        }

        public string TitleText => _isNewGroup ? "Yeni Vardiya Grubu" : "Vardiya Grubunu Düzenle";

        public bool IsAllShifts
        {
            get => CurrentGroup?.ShiftPatterns?.Contains("*") ?? false;
            set
            {
                if (CurrentGroup != null)
                {
                    if (value)
                    {
                        CurrentGroup.ShiftPatterns = new List<string> { "*" };
                    }
                    else if (CurrentGroup.ShiftPatterns.Contains("*"))
                    {
                        CurrentGroup.ShiftPatterns = new List<string>();
                    }
                    OnPropertyChanged(nameof(IsAllShifts));
                    OnPropertyChanged(nameof(CurrentGroup));
                }
            }
        }

        public ShiftRuleGroupModal(CompanyConfig parentCompany, ShiftRuleConfig group = null)
        {
            InitializeComponent();
            _parentCompany = parentCompany;

            _isNewGroup = group == null;
            if (_isNewGroup)
            {
                // Yeni grup oluştur
                CurrentGroup = new ShiftRuleConfig
                {
                    GroupName = "",
                    ShiftPatterns = new List<string>(),
                    DefaultStartTime = TimeSpan.Parse("08:00"),
                    DefaultEndTime = TimeSpan.Parse("17:00"),
                    StandardHours = 8.0,
                    ShiftDuration = 9.0, // Yeni eklenen özellik için varsayılan değer
                    BreakHours = 1.0,
                    AssignNormalFM = true, // Varsayılan olarak işaretli gelmeli
                    ConsecutiveDaysForVacation = 6,
                    VacationDays = 1,
                    AbsentDaysColumnLetter = "K",
                    OvertimeRules = new List<OvertimeRule>(),
                    ShiftPatternMappings = new List<string>()
                };
                _originalGroupName = string.Empty;
            }
            else
            {
                // Mevcut grubu düzenle
                CurrentGroup = new ShiftRuleConfig
                {
                    GroupName = group.GroupName,
                    ShiftPatterns = new List<string>(group.ShiftPatterns),
                    DefaultStartTime = group.DefaultStartTime,
                    DefaultEndTime = group.DefaultEndTime,
                    StandardHours = group.StandardHours,
                    ShiftDuration = group.ShiftDuration, // Yeni eklenen özelliği kopyala
                    BreakHours = group.BreakHours,
                    AssignNormalFM = group.AssignNormalFM, // Yeni eklenen özelliği kopyala
                    ConsecutiveDaysForVacation = group.ConsecutiveDaysForVacation,
                    VacationDays = group.VacationDays,
                    AbsentDaysColumnLetter = string.IsNullOrWhiteSpace(group.AbsentDaysColumnLetter) ? "K" : group.AbsentDaysColumnLetter,
                    OvertimeRules = group.OvertimeRules?.Select(r => new OvertimeRule
                    {
                        StartTime = r.StartTime,
                        Rate = r.Rate,
                        Description = r.Description,
                        ColumnName = r.ColumnName,
                        DurationHours = r.DurationHours,
                        IsCatchAll = r.IsCatchAll
                    }).ToList() ?? new List<OvertimeRule>(),
                    ShiftPatternMappings = new List<string>(group.ShiftPatternMappings ?? new List<string>())
                };
                _originalGroupName = group.GroupName;
            }

            DataContext = this;

            // UI elementlerinin değerlerini güncelle
            Loaded += ShiftRuleGroupModal_Loaded;
            Unloaded += ShiftRuleGroupModal_Unloaded;

            // Grup adı değiştiğinde CurrentGroup'u güncelle
            if (txtGroupName != null)
            {
                txtGroupName.TextChanged += TxtGroupName_TextChanged;
            }
        }

        private void ShiftRuleGroupModal_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Mevcut grubu düzenlerken UI elementlerini güncelle
                if (!_isNewGroup && CurrentGroup != null)
                {
                    // Grup adı
                    txtGroupName.Text = CurrentGroup.GroupName;

                    // Standart saatler
                    txtStandardHours.Text = CurrentGroup.StandardHours.ToString("0.##", CultureInfo.InvariantCulture);
                    txtBreakHours.Text = FormatBreakHours(CurrentGroup.BreakHours);

                    // Tatil kuralları
                    txtConsecutiveDays.Text = CurrentGroup.ConsecutiveDaysForVacation.ToString();
                    txtVacationDays.Text = CurrentGroup.VacationDays.ToString();

                    // Vardiya türleri
                    lbShiftTypes.ItemsSource = CurrentGroup.ShiftPatterns;
                    lbShiftTypes.Items.Refresh();

                    if (lbShiftMappings.ItemsSource == null)
                    {
                        lbShiftMappings.ItemsSource = CurrentGroup.ShiftPatternMappings;
                    }
                    lbShiftMappings.Items.Refresh();

                            // FM kuralları - Yeni UI ile yönetiliyor

                    // Yeni FM UI elementleri için varsayılan değerler
                    if (cbSelectedShift != null)
                    {
                        cbSelectedShift.ItemsSource = CurrentGroup.ShiftPatterns;
                        cbSelectedShift.Items.Refresh();
                        if (cbSelectedShift.Items.Count > 0)
                            cbSelectedShift.SelectedIndex = 0;
                    }
                    if (txtNormalWorkingHours != null)
                        txtNormalWorkingHours.Text = CurrentGroup.StandardHours.ToString("0.##", CultureInfo.InvariantCulture);
                    if (txtShiftDuration != null)
                    {
                        txtShiftDuration.Text = CurrentGroup.ShiftDuration.ToString("0.##", CultureInfo.InvariantCulture);
                        Console.WriteLine($"[ShiftRuleGroupModal_Loaded] txtShiftDuration.Text güncellendi: {CurrentGroup.ShiftDuration}");
                    }

                    if (txtAbsentColumn != null)
                    {
                        txtAbsentColumn.Text = string.IsNullOrWhiteSpace(CurrentGroup.AbsentDaysColumnLetter)
                            ? "K"
                            : CurrentGroup.AbsentDaysColumnLetter.ToUpperInvariant();
                    }

                    // FM Atamaları onay kutusu
                    chkAssignNormalFM.IsChecked = CurrentGroup.AssignNormalFM;

                    // Tümünü kapsa checkbox'ı
                    chkAllShifts.IsChecked = CurrentGroup.ShiftPatterns.Contains("*");

                    Console.WriteLine($"[ShiftRuleGroupModal] Mevcut grup '{CurrentGroup.GroupName}' düzenleme için yüklendi");
                }
                else
                {
                    // Yeni grup için varsayılan değerler
                    txtStandardHours.Text = CurrentGroup.StandardHours.ToString("0.##", CultureInfo.InvariantCulture);
                    txtBreakHours.Text = FormatBreakHours(CurrentGroup.BreakHours);
                    txtConsecutiveDays.Text = "6";
                    txtVacationDays.Text = "1";
                    // Yeni vardiya alanları için varsayılan değerler
                    txtShiftStartTime.Text = "08:00";
                    txtShiftEndTime.Text = "17:00";
                    txtShiftBreakTime.Text = "1.0";

                    // Yeni FM UI elementleri için varsayılan değerler
                    cbSelectedShift.ItemsSource = CurrentGroup.ShiftPatterns;
                    cbSelectedShift.Items.Refresh();
                    txtNormalWorkingHours.Text = CurrentGroup.StandardHours.ToString("0.##", CultureInfo.InvariantCulture);
                    if (txtShiftDuration != null)
                    {
                        txtShiftDuration.Text = CurrentGroup.ShiftDuration.ToString("0.##", CultureInfo.InvariantCulture);
                        Console.WriteLine($"[ShiftRuleGroupModal_Loaded] txtShiftDuration.Text güncellendi (yeni grup): {CurrentGroup.ShiftDuration}");
                    }

                    // FM Atamaları onay kutusu
                    chkAssignNormalFM.IsChecked = CurrentGroup.AssignNormalFM;

                    Console.WriteLine("[ShiftRuleGroupModal] Yeni grup için modal yüklendi");
                }

                if (lbShiftMappings.ItemsSource == null)
                {
                    lbShiftMappings.ItemsSource = CurrentGroup.ShiftPatternMappings;
                }
                lbShiftMappings.Items.Refresh();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ShiftRuleGroupModal] Loaded event hatası: {ex.Message}");
            }
        }

        private void btnAddShiftType_Click(object sender, RoutedEventArgs e)
        {
            Console.WriteLine("[ShiftRuleGroupModal] Vardiya ekle butonuna tıklandı");

            string startTime = txtShiftStartTime.Text?.Trim();
            string endTime = txtShiftEndTime.Text?.Trim();
            string breakTime = txtShiftBreakTime.Text?.Trim();

            Console.WriteLine($"[ShiftRuleGroupModal] Başlangıç: '{startTime}', Bitiş: '{endTime}', Mola: '{breakTime}'");

            // Validasyon
            if (string.IsNullOrWhiteSpace(startTime) || string.IsNullOrWhiteSpace(endTime) || string.IsNullOrWhiteSpace(breakTime))
            {
                MessageBox.Show("Başlangıç saati, bitiş saati ve mola süresi zorunludur.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!TimeSpan.TryParse(startTime, out TimeSpan startTimeSpan))
            {
                MessageBox.Show("Geçersiz başlangıç saati formatı. Örnek: 08:00", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!TimeSpan.TryParse(endTime, out TimeSpan endTimeSpan))
            {
                MessageBox.Show("Geçersiz bitiş saati formatı. Örnek: 17:00", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!TryParseBreakDuration(breakTime, out double breakTimeValue, out string breakDisplay))
            {
                MessageBox.Show("Geçersiz mola süresi. Örnek: 01:00 ya da 1.5", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Başlangıç saati bitiş saatinden önce olmalı
            if (startTimeSpan >= endTimeSpan)
            {
                MessageBox.Show("Başlangıç saati bitiş saatinden önce olmalıdır.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Vardiya formatını oluştur: 08:00-17:00 (01:00)
            string newShiftType = $"{NormalizeTimeDisplay(startTimeSpan)}-{NormalizeTimeDisplay(endTimeSpan)} ({breakDisplay})";

            if (CurrentGroup.ShiftPatterns.Contains(newShiftType))
            {
                MessageBox.Show("Bu vardiya türü zaten eklenmiş.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Tümünü kapsa seçeneği varsa, onu kaldır
            if (CurrentGroup.ShiftPatterns.Contains("*"))
            {
                CurrentGroup.ShiftPatterns.Remove("*");
                IsAllShifts = false;
            }

            CurrentGroup.ShiftPatterns.Add(newShiftType);
            lbShiftTypes.Items.Refresh();
            OnPropertyChanged(nameof(CurrentGroup));

            // Vardiya seçim combobox'ını güncelle
            cbSelectedShift.ItemsSource = null;
            cbSelectedShift.ItemsSource = CurrentGroup.ShiftPatterns;
            cbSelectedShift.Items.Refresh();
            cbSelectedShift.SelectedItem = newShiftType;

            UpdateDefaultValues(startTimeSpan, endTimeSpan, breakTimeValue, breakDisplay);

            Console.WriteLine($"[ShiftRuleGroupModal] Vardiya türü '{newShiftType}' başarıyla eklendi");

            txtShiftStartTime.Text = NormalizeTimeDisplay(startTimeSpan);
            txtShiftEndTime.Text = NormalizeTimeDisplay(endTimeSpan);
            txtShiftBreakTime.Text = breakDisplay;
        }

        private void btnRemoveShiftType_Click(object sender, RoutedEventArgs e)
        {
            if (lbShiftTypes.SelectedItem is string selectedShift)
            {
                var result = MessageBox.Show($"'{selectedShift}' vardiyasını silmek istediğinizden emin misiniz?",
                                           "Vardiya Sil",
                                           MessageBoxButton.YesNo,
                                           MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    CurrentGroup.ShiftPatterns.Remove(selectedShift);
                    lbShiftTypes.Items.Refresh();
                    OnPropertyChanged(nameof(CurrentGroup));

                    // Vardiya seçim combobox'ını güncelle
                    cbSelectedShift.ItemsSource = null;
                    cbSelectedShift.ItemsSource = CurrentGroup.ShiftPatterns;
                    cbSelectedShift.Items.Refresh();

                    // İlk öğeyi seç (varsa)
                    if (CurrentGroup.ShiftPatterns.Count > 0)
                    {
                        cbSelectedShift.SelectedIndex = 0;
                    }

                    Console.WriteLine($"[ShiftRuleGroupModal] Vardiya türü '{selectedShift}' başarıyla silindi");
                }
            }
            else
            {
                MessageBox.Show("Lütfen silmek istediğiniz vardiyayı seçin.", "Uyarı",
                              MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void btnEditShiftType_Click(object sender, RoutedEventArgs e)
        {
            if (lbShiftTypes.SelectedItem is string selectedShift)
            {
                try
                {
                    // Örnek format: "08:00-17:00 (01:00)"
                    var match = System.Text.RegularExpressions.Regex.Match(selectedShift, @"^(\d{2}:\d{2})-(\d{2}:\d{2}) \((\d{2}:\d{2}|\d+\.\d+|\d+)\)$");
                    if (match.Success)
                    {
                        txtShiftStartTime.Text = match.Groups[1].Value;
                        txtShiftEndTime.Text = match.Groups[2].Value;
                        txtShiftBreakTime.Text = match.Groups[3].Value;

                        // Mevcut vardiyayı listeden sil ki güncellenmiş hali eklenebilsin.
                        // Kullanıcı sonra Ekle butonuna basarak güncelleyebilir.
                        CurrentGroup.ShiftPatterns.Remove(selectedShift);
                        lbShiftTypes.Items.Refresh();
                        OnPropertyChanged(nameof(CurrentGroup));

                        MessageBox.Show("Seçili vardiya bilgileri düzenleme alanına yüklendi. Değişiklikleri yapıp 'Ekle' butonuna basarak güncelleyebilirsiniz.", "Vardiya Düzenle", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show("Seçili vardiya formatı ayrıştırılamadı. Lütfen geçerli bir format seçin. (Örnek: 08:00-17:00 (01:00))", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Vardiya düzenlenirken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                MessageBox.Show("Lütfen düzenlemek istediğiniz bir vardiya seçin.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void btnUpdateFMRules_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Vardiya seçimi kontrolü
                string selectedShift = cbSelectedShift.SelectedItem as string;
                if (string.IsNullOrWhiteSpace(selectedShift))
                {
                    MessageBox.Show("Lütfen bir vardiya seçin.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Normal çalışma saati kontrolü
                if (!double.TryParse(txtNormalWorkingHours.Text?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double normalWorkingHours) || normalWorkingHours <= 0)
                {
                    MessageBox.Show("Geçersiz normal çalışma saati. Örnek: 7.5", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Vardiya aralığı kontrolü
                if (!double.TryParse(txtShiftDuration.Text?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double shiftDuration) || shiftDuration <= 0)
                {
                    MessageBox.Show("Geçersiz vardiya aralığı. Örnek: 9.0", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // FM atama seçeneklerini kontrol et
                bool assignNormalFM = chkAssignNormalFM.IsChecked == true;
                bool assign50PercentFM = chkAssign50PercentFM.IsChecked == true;

                CurrentGroup.AssignNormalFM = assignNormalFM; // Onay kutusu durumunu kaydet

                // Normal çalışma saati, vardiya aralığından küçük olmalı
                if (assignNormalFM && normalWorkingHours >= shiftDuration)
                {
                    MessageBox.Show("Normal çalışma saati, vardiya aralığından küçük olmalıdır.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                CurrentGroup.StandardHours = normalWorkingHours;
                CurrentGroup.ShiftDuration = shiftDuration; // Yeni eklenen ShiftDuration özelliğini güncelle

                if (!assignNormalFM && !assign50PercentFM)
                {
                    MessageBox.Show("En az bir FM atama seçeneği işaretlenmelidir.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Mevcut OvertimeRules'u temizle
                CurrentGroup.OvertimeRules.Clear();

                // Yeni FM kurallarını oluştur
                double firstRuleDuration = shiftDuration - normalWorkingHours; // Örnek: 9.0 - 7.5 = 1.5

                if (assignNormalFM)
                {
                    // İlk kural: Normal çalışma saati ile vardiya aralığı arası → FM Normal
                    CurrentGroup.OvertimeRules.Add(new OvertimeRule
                    {
                        DurationHours = firstRuleDuration,
                        IsCatchAll = false,
                        ColumnName = "B04 Fazla Mesai Normal",
                        Description = $"İlk {firstRuleDuration:F1} saat FM Normal ({normalWorkingHours:F1}-{shiftDuration:F1}h arası)",
                        Rate = 1.0
                    });
                }

                if (assign50PercentFM)
                {
                    // İkinci kural: Vardiya aralığı üzeri → FM %50
                    CurrentGroup.OvertimeRules.Add(new OvertimeRule
                    {
                        DurationHours = null,
                        IsCatchAll = true,
                        ColumnName = "B01 %50 Fazla Mesai",
                        Description = $"{shiftDuration:F1} saat üzeri tüm fazla çalışma FM %50",
                        Rate = 1.5
                    });
                }

                // Sadece FM %50 seçildiyse, tüm fazla çalışmayı FM %50'ye yönlendir
                if (!assignNormalFM && assign50PercentFM)
                {
                    CurrentGroup.OvertimeRules.Clear();
                    CurrentGroup.OvertimeRules.Add(new OvertimeRule
                    {
                        DurationHours = null,
                        IsCatchAll = true,
                        ColumnName = "B01 %50 Fazla Mesai",
                        Description = $"Tüm fazla çalışma FM %50 ({normalWorkingHours:F1}h üzeri)",
                        Rate = 1.5
                    });
                }

                // Kurallar güncellendi - UI otomatik olarak güncellenecek

                OnPropertyChanged(nameof(CurrentGroup));

                MessageBox.Show($"FM kuralları '{selectedShift}' vardiyası için başarıyla güncellendi.", "Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);

                Console.WriteLine($"[FM Kuralları] '{selectedShift}' için güncellendi - Normal: {normalWorkingHours}h, Vardiya: {shiftDuration}h");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"FM kuralları güncellenirken hata: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                Console.WriteLine($"[FM Kuralları] Güncelleme hatası: {ex.Message}");
            }
        }

        private void btnOpenConfigFile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string configFilePath = "pdks-config.json";
                if (File.Exists(configFilePath))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(configFilePath) { UseShellExecute = true });
                }
                else
                {
                    MessageBox.Show("Config dosyası bulunamadı.", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Config dosyası açılırken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ShiftRuleGroupModal_Unloaded(object sender, RoutedEventArgs e)
        {
            // Event handler'ları temizle
            if (txtGroupName != null)
            {
                txtGroupName.TextChanged -= TxtGroupName_TextChanged;
            }
        }

        private void TxtGroupName_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Grup adı değiştiğinde CurrentGroup'u güncelle
            if (CurrentGroup != null && txtGroupName != null)
            {
                CurrentGroup.GroupName = txtGroupName.Text?.Trim() ?? "";
            }
        }

        private void btnRefreshShiftList_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Vardiya listesini güncelle
                cbSelectedShift.ItemsSource = null;
                cbSelectedShift.ItemsSource = CurrentGroup.ShiftPatterns;
                cbSelectedShift.Items.Refresh();

                // İlk öğeyi seç
                if (CurrentGroup.ShiftPatterns != null && CurrentGroup.ShiftPatterns.Count > 0)
                {
                    cbSelectedShift.SelectedIndex = 0;
                }

                Console.WriteLine($"[Vardiya Listesi] Güncellendi - {CurrentGroup.ShiftPatterns?.Count ?? 0} vardiya bulundu");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Vardiya listesi güncellenirken hata: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                Console.WriteLine($"[Vardiya Listesi] Güncelleme hatası: {ex.Message}");
            }
        }

        private void btnGenerateMappings_Click(object sender, RoutedEventArgs e)
        {
            string startText = txtShiftStartTime.Text?.Trim();
            string endText = txtShiftEndTime.Text?.Trim();

            if (string.IsNullOrWhiteSpace(startText) || string.IsNullOrWhiteSpace(endText))
            {
                MessageBox.Show("Lütfen alternatif oluşturmak için başlangıç ve bitiş saatlerini girin.", "Eksik Bilgi", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!TimeSpan.TryParse(startText, out var startSpan))
            {
                MessageBox.Show("Geçersiz başlangıç saati formatı. Örnek: 08:00", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (!TimeSpan.TryParse(endText, out var endSpan))
            {
                MessageBox.Show("Geçersiz bitiş saati formatı. Örnek: 17:00", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (startSpan >= endSpan)
            {
                MessageBox.Show("Başlangıç saati bitiş saatinden önce olmalıdır.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var alternatives = GenerateMappingAlternatives(startSpan, endSpan);
            int addedCount = 0;
            foreach (var alt in alternatives)
            {
                if (string.IsNullOrWhiteSpace(alt))
                    continue;

                if (!CurrentGroup.ShiftPatternMappings.Any(m => string.Equals(m, alt, StringComparison.OrdinalIgnoreCase)))
                {
                    CurrentGroup.ShiftPatternMappings.Add(alt);
                    addedCount++;
                }
            }

            lbShiftMappings.Items.Refresh();
            OnPropertyChanged(nameof(CurrentGroup));

            if (addedCount > 0)
            {
                MessageBox.Show($"{addedCount} alternatif eklendi.", "Alternatifler Oluşturuldu", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("Yeni alternatif eklenmedi; mevcut kombinasyonlar zaten tanımlı.", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private IEnumerable<string> GenerateMappingAlternatives(TimeSpan start, TimeSpan end)
        {
            var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var startVariants = GetTimeVariants(start);
            var endVariants = GetTimeVariants(end);
            var separators = new[] { "-", " - ", "/", " / ", "*", " * " };

            foreach (var s in startVariants)
            {
                foreach (var e in endVariants)
                {
                    foreach (var sep in separators)
                    {
                        results.Add($"{s}{sep}{e}");
                    }
                }
            }

            if (start.Minutes == 0 && end.Minutes == 0)
            {
                var startHours = new[] { start.Hours.ToString("00"), start.Hours.ToString() };
                var endHours = new[] { end.Hours.ToString("00"), end.Hours.ToString() };
                var simpleSeparators = new[] { "-", "/", "*" };

                foreach (var sep in simpleSeparators)
                {
                    foreach (var s in startHours)
                    {
                        foreach (var e in endHours)
                        {
                            results.Add($"{s}{sep}{e}");
                        }
                    }
                }
            }

            return results;
        }

        private IEnumerable<string> GetTimeVariants(TimeSpan time)
        {
            var variants = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                NormalizeTimeDisplay(time),
                string.Format("{0:00}{1:00}", time.Hours, time.Minutes),
                string.Format("{0}{1:00}", time.Hours, time.Minutes)
            };

            string shortColon = string.Format("{0}:{1:00}", time.Hours, time.Minutes);
            variants.Add(shortColon);

            if (time.Minutes == 0)
            {
                variants.Add(time.Hours.ToString("00"));
                variants.Add(time.Hours.ToString());
                variants.Add(string.Format("{0:00}:00", time.Hours));
                variants.Add(string.Format("{0}:00", time.Hours));
            }

            return variants;
        }

        private double ResolveRateFromColumn(string columnName)
        {
            if (string.IsNullOrWhiteSpace(columnName))
            {
                return 1.0;
            }

            string normalized = columnName.ToLowerInvariant();
            if (normalized.Contains("%50") || normalized.Contains("b01") || normalized.Contains("50"))
            {
                return 1.5;
            }

            return 1.0;
        }

        private bool TryParseBreakDuration(string input, out double hours, out string display)
        {
            hours = 0;
            display = string.Empty;
            if (string.IsNullOrWhiteSpace(input))
            {
                return false;
            }

            string trimmed = input.Trim().Replace(',', '.');

            if (TimeSpan.TryParse(trimmed, out var timeSpanValue))
            {
                hours = timeSpanValue.TotalHours;
                display = NormalizeTimeDisplay(timeSpanValue);
                return true;
            }

            string digitsOnly = new string(trimmed.Where(char.IsDigit).ToArray());
            if (!string.IsNullOrEmpty(digitsOnly))
            {
                if (digitsOnly.Length == 4 &&
                    int.TryParse(digitsOnly.Substring(0, 2), out var hFour) &&
                    int.TryParse(digitsOnly.Substring(2, 2), out var mFour))
                {
                    var ts = new TimeSpan(hFour, mFour, 0);
                    hours = ts.TotalHours;
                    display = NormalizeTimeDisplay(ts);
                    return true;
                }

                if (digitsOnly.Length == 3 &&
                    int.TryParse(digitsOnly.Substring(0, 1), out var hThree) &&
                    int.TryParse(digitsOnly.Substring(1, 2), out var mThree))
                {
                    var ts = new TimeSpan(hThree, mThree, 0);
                    hours = ts.TotalHours;
                    display = NormalizeTimeDisplay(ts);
                    return true;
                }
            }

            if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out double decimalHours) && decimalHours >= 0)
            {
                hours = decimalHours;
                var ts = TimeSpan.FromHours(decimalHours);
                display = NormalizeTimeDisplay(ts);
                return true;
            }

            return false;
        }

        private void UpdateDefaultValues(TimeSpan startTime, TimeSpan endTime, double breakHours, string breakDisplay)
        {
            CurrentGroup.DefaultStartTime = startTime;
            CurrentGroup.DefaultEndTime = endTime;
            CurrentGroup.BreakHours = breakHours;

            var totalHours = (endTime - startTime).TotalHours - breakHours;
            if (totalHours < 0)
            {
                totalHours = 0;
            }

            CurrentGroup.StandardHours = Math.Round(totalHours, 2);
            txtStandardHours.Text = CurrentGroup.StandardHours.ToString("0.##", CultureInfo.InvariantCulture);
            txtBreakHours.Text = breakDisplay;
        }

        private string FormatBreakHours(double breakHours)
        {
            var ts = TimeSpan.FromHours(Math.Max(0, breakHours));
            return NormalizeTimeDisplay(ts);
        }

        private string NormalizeTimeDisplay(TimeSpan time)
        {
            var positiveTime = time;
            if (positiveTime < TimeSpan.Zero)
            {
                positiveTime = TimeSpan.Zero;
            }

            int hours = (int)Math.Floor(positiveTime.TotalHours);
            int minutes = positiveTime.Minutes;
            return string.Format("{0:00}:{1:00}", hours, minutes);
        }

        private void btnSave_Click(object sender, RoutedEventArgs e)
        {
            // Grup adını güncelle
            if (txtGroupName != null && !string.IsNullOrWhiteSpace(txtGroupName.Text))
            {
                CurrentGroup.GroupName = txtGroupName.Text.Trim();
            }

            // Tatil hakları alanlarını güncelle
            if (txtConsecutiveDays != null && int.TryParse(txtConsecutiveDays.Text, out int consecutiveDays))
            {
                CurrentGroup.ConsecutiveDaysForVacation = consecutiveDays;
            }

            if (txtVacationDays != null && int.TryParse(txtVacationDays.Text, out int vacationDays))
            {
                CurrentGroup.VacationDays = vacationDays;
            }

            // Validasyon
            if (string.IsNullOrWhiteSpace(CurrentGroup.GroupName))
            {
                MessageBox.Show("Grup adı zorunludur.", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (CurrentGroup.ShiftPatterns.Count == 0)
            {
                MessageBox.Show("En az bir vardiya türü seçmelisiniz.", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (CurrentGroup.StandardHours <= 0)
            {
                MessageBox.Show("Standart saat 0'dan büyük olmalıdır.", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (txtAbsentColumn != null)
            {
                var letter = txtAbsentColumn.Text?.Trim().ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(letter))
                {
                    MessageBox.Show("Devamsızlık kolonu boş olamaz.", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                CurrentGroup.AbsentDaysColumnLetter = letter;
            }

            // Grup adı benzersiz mi kontrol et
            bool nameExists = _parentCompany.ShiftRuleConfigs.Any(g =>
                string.Equals(g.GroupName, CurrentGroup.GroupName, StringComparison.OrdinalIgnoreCase) &&
                (_isNewGroup || !string.Equals(g.GroupName, _originalGroupName, StringComparison.OrdinalIgnoreCase)));
            if (nameExists)
            {
                MessageBox.Show("Bu grup adı zaten kullanılıyor.", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (_isNewGroup)
            {
                // Yeni grubu ekle
                _parentCompany.ShiftRuleConfigs.Add(CurrentGroup);
            }
            else
            {
                // Mevcut grubu güncelle
                var existingIndex = _parentCompany.ShiftRuleConfigs.FindIndex(g => string.Equals(g.GroupName, _originalGroupName, StringComparison.OrdinalIgnoreCase));
                if (existingIndex >= 0)
                {
                    _parentCompany.ShiftRuleConfigs[existingIndex] = CurrentGroup;
                }
                else
                {
                    _parentCompany.ShiftRuleConfigs.Add(CurrentGroup);
                }
            }

            _originalGroupName = CurrentGroup.GroupName;
            DialogResult = true;
            Close();
        }

        private void cmbAbsentColumnOptions_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (cmbAbsentColumnOptions?.SelectedValue is string letter && txtAbsentColumn != null)
            {
                txtAbsentColumn.Text = letter;
                CurrentGroup.AbsentDaysColumnLetter = letter;
            }
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        // Input validation handlers
        private void TextBox_PreviewTextInput_AllowAll(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            // Tüm karakterlere izin ver
            e.Handled = false;
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

        private void TextBox_PreviewTextInput_Double(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            // Sayılara ve nokta/virgül'e izin ver (decimal için)
            foreach (char c in e.Text)
            {
                if (!char.IsDigit(c) && c != '.' && c != ',')
                {
                    e.Handled = true;
                    return;
                }
            }
            e.Handled = false;
        }

        private void TextBox_PreviewTextInput_LettersOnly(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Text))
            {
                e.Handled = false;
                return;
            }

            foreach (char c in e.Text)
            {
                if (!char.IsLetter(c))
                {
                    e.Handled = true;
                    return;
                }
            }

            e.Handled = false;
        }

        public class EksColumnOption
        {
            public EksColumnOption(string code, string description, string columnLetter)
            {
                Code = code;
                Description = description;
                ColumnLetter = columnLetter;
            }

            public string Code { get; }
            public string Description { get; }
            public string ColumnLetter { get; }
            public string Display => $"{Code} {Description} ({ColumnLetter})";
        }

        private void TextBox_PreviewTextInput_Time(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            TextBox textBox = sender as TextBox;
            if (textBox == null) return;

            string currentText = textBox.Text;
            string newText = currentText.Insert(textBox.SelectionStart, e.Text);

            // Saat formatı kontrolü: HH:mm
            // Sadece sayılara ve : karakterine izin ver
            foreach (char c in e.Text)
            {
                if (!char.IsDigit(c) && c != ':')
                {
                    e.Handled = true;
                    return;
                }
            }

            // Format kontrolü
            if (newText.Length > 5) // HH:mm = 5 karakter
            {
                e.Handled = true;
                return;
            }

            // Pozisyona göre kontrol
            int colonCount = newText.Count(c => c == ':');
            if (colonCount > 1)
            {
                e.Handled = true;
                return;
            }

            // Saat kısmının kontrolü (ilk iki karakter)
            if (newText.Contains(":"))
            {
                string[] parts = newText.Split(':');
                if (parts.Length > 0 && parts[0].Length > 0)
                {
                    if (!int.TryParse(parts[0], out int hour) || hour > 23)
                    {
                        e.Handled = true;
                        return;
                    }
                }
                // Dakika kısmının kontrolü
                if (parts.Length > 1 && parts[1].Length > 0)
                {
                    if (!int.TryParse(parts[1], out int minute) || minute > 59)
                    {
                        e.Handled = true;
                        return;
                    }
                }
            }
            else if (newText.Length >= 2)
            {
                // Saat kısmının kontrolü
                if (!int.TryParse(newText.Substring(0, Math.Min(2, newText.Length)), out int hour) || hour > 23)
                {
                    e.Handled = true;
                    return;
                }
            }

            e.Handled = false;
        }

        private void TextBox_Time_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            TextBox textBox = sender as TextBox;
            if (textBox == null) return;

            string text = textBox.Text;

            // Sadece sayı karakterleri ve : karakterini tut
            string cleanText = new string(text.Where(c => char.IsDigit(c) || c == ':').ToArray());

            // Otomatik : ekleme
            if (cleanText.Length == 2 && !cleanText.Contains(":"))
            {
                cleanText = cleanText + ":";
            }
            else if (cleanText.Length == 3 && cleanText[2] != ':')
            {
                cleanText = cleanText.Insert(2, ":");
            }

            // Maksimum 5 karakter (HH:mm)
            if (cleanText.Length > 5)
            {
                cleanText = cleanText.Substring(0, 5);
            }

            // Text değiştiyse güncelle
            if (cleanText != text)
            {
                textBox.Text = cleanText;
                textBox.SelectionStart = cleanText.Length; // Cursor'ı sona getir
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
