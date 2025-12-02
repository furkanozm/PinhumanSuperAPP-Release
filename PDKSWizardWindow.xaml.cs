using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Win32;
using System.IO;
using System.Diagnostics;

namespace WebScraper
{
    public partial class PDKSWizardWindow : Window
    {
        private int currentStep = 1;
        private const int totalSteps = 3;
        private string selectedTemplatePath = "";
        private readonly CalendarService calendarService = new CalendarService();
        private readonly DataTemplateService dataTemplateService = new DataTemplateService();
        private readonly DataTemplateDetectionService dataTemplateDetectionService = new DataTemplateDetectionService();
        private DataTemplate? detectedPdksTemplate;
        private readonly CultureInfo cultureInfo = new CultureInfo("tr-TR");
        private string selectedPDKSPath = "";
        private List<PersonnelRecord> personnelRecords = new List<PersonnelRecord>();
        private PDKSDataService pdksDataService = new PDKSDataService();
        private PDKSConfigService configService = new PDKSConfigService();
        private PDKSConfig currentConfig;
        private List<string> applicationLogs = new List<string>();
        private readonly List<StatusTagInfo> pdksStatusSnapshot = new List<StatusTagInfo>();
        private string? lastErpSummaryMessage;
        private string? lastErpOutputPath;
        private bool lastErpSuccess;
        private int lastErpRecordCount;
        private List<OvertimeDetailEntry> lastOvertimeDetails = new List<OvertimeDetailEntry>();
        private List<ConditionalEarningDetail> lastConditionalEarningsDetails = new List<ConditionalEarningDetail>();
        private string? lastErpRawOutput;
        private string companyFilterText = string.Empty;
        private bool includePreviousMonthCarryOver = false;
        private bool isUpdatingCarryOverToggle = false;
        private bool isCarryOverViewActive = false;
        private bool hasCarryOverExpanderAnimated = false;
        private CarryOverSnapshotResult? latestCarryOverSnapshot;
        private int? selectedCarryOverYear = null;
        private int? selectedCarryOverMonth = null;
        private readonly List<string> carryOverShiftOptions = new List<string>();
        private string carryOverNameFilter = string.Empty;
        private string carryOverShiftFilter = "Tümü";
        private DateTime? carryOverMinDate = null;
        private DateTime? carryOverMaxDate = null;
        private bool isUpdatingCarryOverFilters = false;

        public PDKSWizardWindow()
        {
            try
            {
                InitializeComponent();
                UpdateCompanyCalendarSummary(null);
                currentConfig = configService.LoadConfig();
                RegisterOvertimeColumnsFromConfig(currentConfig);

                // Başlatma log'u
                LogToTerminal($"[PDKS Wizard] Başlatıldı - {DateTime.Now:yyyy-MM-dd HH:mm:ss}");

                UpdateStepDisplay();

                // Python kontrolü yap (arka planda, UI'ı bloklamadan)
                Task.Run(async () =>
                {
                    try
                    {
                        await CheckPythonOnStartupAsync();
                    }
                    catch (Exception ex)
                    {
                        // Python kontrolü hatası uygulamayı durdurmamalı
                        System.Diagnostics.Debug.WriteLine($"Python kontrolü hatası: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                LogToTerminal($"[PDKS Wizard] Başlatma hatası: {ex.Message}");
                MessageBox.Show($"PDKS Wizard başlatılırken hata: {ex.Message}\n\nStack Trace: {ex.StackTrace}", "Başlatma Hatası", MessageBoxButton.OK, MessageBoxImage.Error);
                throw;
            }
        }

        private async Task CheckPythonOnStartupAsync()
        {
            try
            {
                // UI thread'de çalıştır
                await Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    if (!PythonInstallerService.IsPythonInstalled())
                    {
                        // Python yoksa kullanıcıya bilgi ver ve kurulum yap
                        await PythonInstallerService.InstallPythonWithDialogAsync();
                    }
                }, System.Windows.Threading.DispatcherPriority.Normal);
            }
            catch (Exception ex)
            {
                LogToTerminal($"[Python Kontrolü] Hata: {ex.Message}");
            }
        }

        private void UpdateStepDisplay()
        {
            // Step title güncelle
                StepTitleText.Text = $"Adım {currentStep}: {GetStepTitle(currentStep)}";

            // Progress text güncelle
            ProgressText.Text = $"Adım {currentStep} / {totalSteps}";

            // Step indicators güncelle
            UpdateStepIndicators();

            // Content visibility güncelle
            UpdateStepContentVisibility();

            // Button states güncelle
            UpdateNavigationButtons();

            // Adım1'de firma listesini doldur
            if (currentStep == 1)
            {
                PopulateCompanyList();
            }
        }

        private void ShowCompanySelectionDialog()
        {
            // Firma seçimi için basit bir modal
            if (currentConfig.CompanyConfigs == null || currentConfig.CompanyConfigs.Count == 0)
            {
                MessageBox.Show("Hiç firma tanımlanmamış. Önce ayarlar bölümünden firma ekleyin.", "Firma Yok", MessageBoxButton.OK, MessageBoxImage.Warning);
                this.Close();
                return;
            }

            var companySelectionWindow = new Window
            {
                Title = "Firma Seçimi - PDKS İşlemi",
                Width = 400,
                Height = 300,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize
            };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Başlık
            var title = new TextBlock
            {
                Text = "PDKS işlemi için firma seçin:",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(10)
            };
            Grid.SetRow(title, 0);

            // Firma listesi
            var companyList = new ListBox
            {
                Margin = new Thickness(20),
                ItemsSource = currentConfig.CompanyConfigs,
                DisplayMemberPath = "CompanyName",
                SelectedValuePath = "CompanyCode"
            };
            Grid.SetRow(companyList, 1);

            // Eğer seçili firma varsa onu seç
            if (!string.IsNullOrEmpty(currentConfig.SelectedCompanyCode))
            {
                var selectedCompany = currentConfig.CompanyConfigs.FirstOrDefault(c => c.CompanyCode == currentConfig.SelectedCompanyCode);
                if (selectedCompany != null)
                {
                    companyList.SelectedItem = selectedCompany;
                }
            }

            // Butonlar
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(10)
            };

            var btnOK = new Button
            {
                Content = "Seç ve Devam Et",
                Width = 120,
                Height = 35,
                Margin = new Thickness(5, 0, 5, 0),
                Background = Brushes.Green,
                Foreground = Brushes.White
            };

            var btnCancel = new Button
            {
                Content = "İptal",
                Width = 80,
                Height = 35,
                Margin = new Thickness(5, 0, 5, 0),
                Background = Brushes.Gray,
                Foreground = Brushes.White
            };

            btnOK.Click += (s, e) =>
            {
                if (companyList.SelectedItem is CompanyConfig selectedCompany)
                {
                    // Vardiya kuralı kontrolü
                    if (selectedCompany.ShiftRuleConfigs == null || selectedCompany.ShiftRuleConfigs.Count == 0)
                    {
                        var result = MessageBox.Show(
                            $"{selectedCompany.CompanyName} firması için hiç vardiya kuralı tanımlanmamış.\n\n" +
                            "Devam etmek için ayarlar bölümünden vardiya kuralları eklemeniz gerekir.\n\n" +
                            "Şimdi ayarlar bölümüne gitmek ister misiniz?",
                            "Vardiya Kuralı Yok",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Warning);

                        if (result == MessageBoxResult.Yes)
                        {
                            // Ayarlar modal'ını aç
                            ShowSettingsModal();

                            // Konfigürasyonu yeniden yükle
                            currentConfig = configService.LoadConfig();

                            // Eğer hala vardiya kuralı yoksa devam etme
                            if (selectedCompany.ShiftRuleConfigs == null || selectedCompany.ShiftRuleConfigs.Count == 0)
                            {
                                MessageBox.Show("Vardiya kuralı eklenmeden devam edilemez.", "Vardiya Kuralı Gerekli", MessageBoxButton.OK, MessageBoxImage.Warning);
                                return;
                            }
                        }
                        else
                        {
                            MessageBox.Show("Vardiya kuralı olmadan PDKS işlemi gerçekleştirilemez.", "İşlem İptal", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }
                    }

                    currentConfig.SelectedCompanyCode = selectedCompany.CompanyCode;
                    LogToTerminal($"[Firma Seçimi] {selectedCompany.CompanyName} ({selectedCompany.CompanyCode}) seçildi");
                    companySelectionWindow.DialogResult = true;
                }
                else
                {
                    MessageBox.Show("Lütfen bir firma seçin.", "Firma Seçilmedi", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            };

            btnCancel.Click += (s, e) =>
            {
                companySelectionWindow.DialogResult = false;
            };

            buttonPanel.Children.Add(btnOK);
            buttonPanel.Children.Add(btnCancel);

            Grid.SetRow(buttonPanel, 2);

            grid.Children.Add(title);
            grid.Children.Add(companyList);
            grid.Children.Add(buttonPanel);

            companySelectionWindow.Content = grid;

            var result = companySelectionWindow.ShowDialog();
            if (result != true)
            {
                // Firma seçilmedi, wizard'ı kapat
                LogToTerminal("[Firma Seçimi] Firma seçilmedi, işlem iptal edildi");
                this.Close();
                return;
            }
        }

        private string GetStepTitle(int step)
        {
            switch (step)
            {
                case 1: return "Firma Seçimi";
                case 2: return "Vio Şablonu Seçimi";
                case 3: return "PDKS Verileri Yükleme";
                case 4: return "Veri İşleme";
                default: return "";
            }
        }

        private void UpdateStepIndicators()
        {
            // Tüm indicator'ları önce normal yap
            Step1Indicator.Style = (Style)FindResource("StepIndicator");
            Step2Indicator.Style = (Style)FindResource("StepIndicator");
            Step3Indicator.Style = (Style)FindResource("StepIndicator");

            // Text color'ları ayarla - Parent artık Button, onun parent'ı StackPanel
            var step1StackPanel = (StackPanel)btnStep1.Parent;
            var step2StackPanel = (StackPanel)btnStep2.Parent;
            var step3StackPanel = (StackPanel)btnStep3.Parent;

            var step1Text = (TextBlock)step1StackPanel.Children[0];
            var step2Text = (TextBlock)step2StackPanel.Children[0];
            var step3Text = (TextBlock)step3StackPanel.Children[0];

            step1Text.Foreground = System.Windows.Media.Brushes.Gray;
            step2Text.Foreground = System.Windows.Media.Brushes.Gray;
            step3Text.Foreground = System.Windows.Media.Brushes.Gray;

            var step1Number = (TextBlock)Step1Indicator.Child;
            var step2Number = (TextBlock)Step2Indicator.Child;
            var step3Number = (TextBlock)Step3Indicator.Child;

            step1Number.Foreground = System.Windows.Media.Brushes.Gray;
            step2Number.Foreground = System.Windows.Media.Brushes.Gray;
            step3Number.Foreground = System.Windows.Media.Brushes.Gray;

            // Geçmiş adımları tamamlanmış yap
            if (currentStep >= 1)
            {
                Step1Indicator.Style = (Style)FindResource("CompletedStepIndicator");
                step1Text.Foreground = System.Windows.Media.Brushes.Green;
                step1Number.Foreground = System.Windows.Media.Brushes.White;
            }
            if (currentStep >= 2)
            {
                Step2Indicator.Style = (Style)FindResource("CompletedStepIndicator");
                step2Text.Foreground = System.Windows.Media.Brushes.Green;
                step2Number.Foreground = System.Windows.Media.Brushes.White;
            }
            if (currentStep >= 3)
            {
                Step3Indicator.Style = (Style)FindResource("CompletedStepIndicator");
                step3Text.Foreground = System.Windows.Media.Brushes.Green;
                step3Number.Foreground = System.Windows.Media.Brushes.White;
            }

            // Aktif adımı vurgula
            Border activeIndicator = null;
            TextBlock activeText = null;
            TextBlock activeNumber = null;

            switch (currentStep)
            {
                case 1:
                    activeIndicator = Step1Indicator;
                    activeText = step1Text;
                    activeNumber = step1Number;
                    break;
                case 2:
                    activeIndicator = Step2Indicator;
                    activeText = step2Text;
                    activeNumber = step2Number;
                    break;
                case 3:
                    activeIndicator = Step3Indicator;
                    activeText = step3Text;
                    activeNumber = step3Number;
                    break;
                
            }

            if (activeIndicator != null)
            {
                activeIndicator.Style = (Style)FindResource("ActiveStepIndicator");
                activeText.Foreground = System.Windows.Media.Brushes.Blue;
                activeNumber.Foreground = System.Windows.Media.Brushes.White;
            }
        }

        private void UpdateStepContentVisibility()
        {
            Step1Content.Visibility = currentStep == 1 ? Visibility.Visible : Visibility.Collapsed;
            Step2Content.Visibility = currentStep == 2 ? Visibility.Visible : Visibility.Collapsed;
            Step3Content.Visibility = currentStep == 3 ? Visibility.Visible : Visibility.Collapsed;
            if (currentStep == 3)
            {
                RefreshPreviousMonthCarryOverStatus();
            }
            // Step4Content yok, Step3Content kullanılıyor
        }

        private void RefreshPreviousMonthCarryOverStatus()
        {
            if (chkIncludePreviousMonthData == null || txtPreviousMonthStatus == null || PreviousMonthStatusBadge == null)
            {
                return;
            }

            var selectedCompany = currentConfig.CompanyConfigs?.FirstOrDefault(c => c.CompanyCode == currentConfig.SelectedCompanyCode);
            if (selectedCompany == null)
            {
                includePreviousMonthCarryOver = false;
                UpdateCarryOverToggle(false, false);
                SetCarryOverStatus("Firma seçilmedi", "#DC2626");
                return;
            }

            bool snapshotExists = pdksDataService.HasPreviousCarryOverSnapshot(selectedCompany);
            if (!snapshotExists)
            {
                includePreviousMonthCarryOver = false;
                UpdateCarryOverToggle(false, false);
                SetCarryOverStatus("Önceki ay devri bulunamadı", "#DC2626");
                return;
            }

            UpdateCarryOverToggle(includePreviousMonthCarryOver, true);

            if (includePreviousMonthCarryOver)
            {
                SetCarryOverStatus("Önceki ay devri uygulanacak", "#16A34A");
            }
            else
            {
                SetCarryOverStatus("Önceki ay devri hazır", "#EA580C");
            }
        }

        private void UpdateCarryOverToggle(bool isChecked, bool isEnabled)
        {
            if (chkIncludePreviousMonthData == null)
            {
                return;
            }

            chkIncludePreviousMonthData.IsEnabled = isEnabled;
            isUpdatingCarryOverToggle = true;
            chkIncludePreviousMonthData.IsChecked = isChecked;
            isUpdatingCarryOverToggle = false;
        }

        private void SetCarryOverStatus(string text, string accentHex)
        {
            if (txtPreviousMonthStatus == null || PreviousMonthStatusBadge == null)
            {
                return;
            }

            txtPreviousMonthStatus.Text = text;

            try
            {
                var accent = (Color)ColorConverter.ConvertFromString(accentHex);
                txtPreviousMonthStatus.Foreground = new SolidColorBrush(accent);
                PreviousMonthStatusBadge.BorderBrush = new SolidColorBrush(accent);
                PreviousMonthStatusBadge.Background = new SolidColorBrush(Color.FromArgb(32, accent.R, accent.G, accent.B));
            }
            catch
            {
                txtPreviousMonthStatus.Foreground = Brushes.Black;
            }
        }

        private void PopulateCarryOverShiftOptions(CarryOverSnapshotResult snapshot)
        {
            if (CarryOverShiftCombo == null || snapshot == null)
            {
                return;
            }

            try
            {
                // Mevcut seçimi koru
                string? currentSelection = CarryOverShiftCombo.SelectedItem as string;

                carryOverShiftOptions.Clear();
                carryOverShiftOptions.Add("Tümü");

                var shifts = new List<string>();
                
                // Yeni günlük kayıt formatından vardiyaları al
                if (snapshot.Records != null && snapshot.Records.Count > 0)
                {
                    shifts = snapshot.Records
                        .Where(r => r != null && !string.IsNullOrWhiteSpace(r.ShiftType))
                        .Select(r => r.ShiftType!.Trim())
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(s => s)
                        .ToList();
                }
                else if (snapshot.States != null && snapshot.States.Count > 0)
                {
                    // Backward compatibility: Eski formattan al
                    shifts = snapshot.States
                        .Where(s => s != null && !string.IsNullOrWhiteSpace(s.LastShiftType))
                        .Select(s => s.LastShiftType!.Trim())
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(s => s)
                        .ToList();
                }

                // Sadece gerçekten verilerden gelen vardiyaları ekle (statik değerler yok)
                carryOverShiftOptions.AddRange(shifts);

                // Seçenekleri güncelle - önce tamamen temizle
                isUpdatingCarryOverFilters = true;
                CarryOverShiftCombo.ItemsSource = null;
                CarryOverShiftCombo.Items.Clear(); // Tüm statik değerleri temizle
                
                // Sadece dinamik değerleri ekle
                CarryOverShiftCombo.ItemsSource = carryOverShiftOptions;
                
                // Eğer mevcut seçim hala geçerliyse koru, değilse "Tümü" seç
                if (!string.IsNullOrEmpty(currentSelection) && carryOverShiftOptions.Contains(currentSelection))
                {
                    CarryOverShiftCombo.SelectedItem = currentSelection;
                }
                else
                {
                    CarryOverShiftCombo.SelectedIndex = 0; // "Tümü" seç
                    carryOverShiftFilter = "Tümü";
                }
                isUpdatingCarryOverFilters = false;
                
                LogToTerminal($"[CarryOver] Vardiya seçenekleri güncellendi: {carryOverShiftOptions.Count} seçenek (Tümü + {shifts.Count} vardiya)");
            }
            catch (Exception ex)
            {
                LogToTerminal($"[CarryOver] Vardiya seçenekleri yüklenirken hata: {ex.Message}");
                LogToTerminal($"[CarryOver] Stack Trace: {ex.StackTrace}");
                isUpdatingCarryOverFilters = false;
            }
        }

        private void ResetCarryOverFilters(bool applyAfterReset = true)
        {
            isUpdatingCarryOverFilters = true;
            carryOverNameFilter = string.Empty;
            carryOverShiftFilter = "Tümü";
            carryOverMinDate = null;
            carryOverMaxDate = null;

            if (CarryOverPersonFilterBox != null)
                CarryOverPersonFilterBox.Text = string.Empty;
            if (CarryOverShiftCombo != null)
                CarryOverShiftCombo.SelectedIndex = 0;
            if (CarryOverMinDatePicker != null)
                CarryOverMinDatePicker.SelectedDate = null;
            if (CarryOverMaxDatePicker != null)
                CarryOverMaxDatePicker.SelectedDate = null;
            isUpdatingCarryOverFilters = false;

            if (applyAfterReset)
            {
                ApplyCarryOverFilters();
            }
        }

        private void ApplyCarryOverFilters()
        {
            if (latestCarryOverSnapshot == null)
            {
                CarryOverListView.ItemsSource = null;
                CarryOverListView.Visibility = Visibility.Collapsed;
                CarryOverEmptyText.Visibility = Visibility.Visible;
                if (CarryOverFilterResultText != null)
                {
                    CarryOverFilterResultText.Text = "0 kayıt gösteriliyor";
                }
                return;
            }

            // Yeni günlük kayıt formatını kullan
            IEnumerable<StoredPDKSRecord> filtered = latestCarryOverSnapshot.Records;

            // Eğer günlük kayıt yoksa eski formatı kullan (backward compatibility)
            if (filtered == null || !filtered.Any())
            {
                // Eski formatı StoredPDKSRecord'a dönüştür
                var convertedRecords = latestCarryOverSnapshot.States.Select(s => new StoredPDKSRecord
                {
                    PersonnelCode = s.PersonnelCode,
                    PersonnelName = s.PersonnelName,
                    Date = s.LastWorkDate,
                    CheckInTime = TimeSpan.Zero,
                    CheckOutTime = TimeSpan.FromHours(s.LastWorkedHours),
                    ShiftType = s.LastShiftType
                });
                filtered = convertedRecords;
            }

            // Dummy verileri ve boş kayıtları filtrele
            var dummyPersonnelCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "12345", "67890" };
            var dummyPersonnelNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Ahmet Yılmaz", "Ayşe Demir" };
            
            filtered = filtered.Where(r => 
                !string.IsNullOrWhiteSpace(r.PersonnelCode) &&
                !dummyPersonnelCodes.Contains(r.PersonnelCode) &&
                !string.IsNullOrWhiteSpace(r.PersonnelName) &&
                !dummyPersonnelNames.Contains(r.PersonnelName.Trim()));

            if (!string.IsNullOrWhiteSpace(carryOverNameFilter))
            {
                var term = carryOverNameFilter.Trim();
                filtered = filtered.Where(r =>
                    (!string.IsNullOrWhiteSpace(r.PersonnelName) && r.PersonnelName.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(r.PersonnelCode) && r.PersonnelCode.Contains(term, StringComparison.OrdinalIgnoreCase)));
            }

            if (!string.Equals(carryOverShiftFilter, "Tümü", StringComparison.OrdinalIgnoreCase))
            {
                filtered = filtered.Where(r => string.Equals(r.ShiftType, carryOverShiftFilter, StringComparison.OrdinalIgnoreCase));
            }


            // Tarih filtresi
            if (carryOverMinDate.HasValue)
            {
                filtered = filtered.Where(r => r.Date >= carryOverMinDate.Value.Date);
            }

            if (carryOverMaxDate.HasValue)
            {
                filtered = filtered.Where(r => r.Date <= carryOverMaxDate.Value.Date);
            }

            var filteredList = filtered
                .OrderByDescending(r => r.Date)
                .ThenBy(r => r.PersonnelName)
                .ThenBy(r => r.CheckInTime)
                .ToList();

            if (CarryOverListView != null)
            {
                CarryOverListView.ItemsSource = filteredList;
            }

            bool hasRecords = filteredList.Count > 0;
            if (CarryOverListView != null)
                CarryOverListView.Visibility = hasRecords ? Visibility.Visible : Visibility.Collapsed;
            if (CarryOverEmptyText != null)
                CarryOverEmptyText.Visibility = hasRecords ? Visibility.Collapsed : Visibility.Visible;

            if (CarryOverFilterResultText != null)
            {
                CarryOverFilterResultText.Text = $"{filteredList.Count} kayıt gösteriliyor";
            }

            // Footer butonlarını güncelle
            UpdateCarryOverFooterButtons(hasRecords);

            if (CarryOverSummaryText != null)
            {
                // Seçilen ay ve yılı kullan (snapshot içindeki değerler yerine)
                int displayYear = selectedCarryOverYear ?? latestCarryOverSnapshot.Year;
                int displayMonth = selectedCarryOverMonth ?? latestCarryOverSnapshot.Month;
                var monthName = cultureInfo.DateTimeFormat.GetMonthName(displayMonth);
                int totalCount = latestCarryOverSnapshot.Records.Count > 0 
                    ? latestCarryOverSnapshot.Records.Count 
                    : latestCarryOverSnapshot.States.Count;
                CarryOverSummaryText.Text = $"{monthName} {displayYear} • {filteredList.Count} / {totalCount} kayıt";
            }
        }

        private double? ParseNullableDouble(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            if (double.TryParse(text, NumberStyles.Any, cultureInfo, out var value))
            {
                return value;
            }

            if (double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out value))
            {
                return value;
            }

            return null;
        }

        private int? ParseNullableInt(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            if (int.TryParse(text, NumberStyles.Integer, cultureInfo, out var value))
            {
                return value;
            }

            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                return value;
            }

            // Double parse desteği ekle (örn: "8.5" -> 9)
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue))
            {
                return (int)Math.Round(doubleValue);
            }

            return null;
        }

        private void btnCarryOverView_Click(object sender, RoutedEventArgs e)
        {
            ShowCarryOverView();
        }

        private void btnBackFromCarryOver_Click(object sender, RoutedEventArgs e)
        {
            HideCarryOverView();
        }

        private void CarryOverPersonFilterBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (isUpdatingCarryOverFilters)
            {
                return;
            }

            carryOverNameFilter = (sender as TextBox)?.Text ?? string.Empty;
            ApplyCarryOverFilters();
        }

        private void CarryOverShiftCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isUpdatingCarryOverFilters)
            {
                return;
            }

            if (CarryOverShiftCombo?.SelectedItem is string selectedShift)
            {
                carryOverShiftFilter = selectedShift;
            }
            else
            {
                carryOverShiftFilter = "Tümü";
            }

            ApplyCarryOverFilters();
        }


        private void CarryOverMinDatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isUpdatingCarryOverFilters)
            {
                return;
            }

            if (sender is DatePicker picker)
            {
                carryOverMinDate = picker.SelectedDate;
            }
            ApplyCarryOverFilters();
        }

        private void CarryOverMaxDatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
        {
            if (isUpdatingCarryOverFilters)
            {
                return;
            }

            if (sender is DatePicker picker)
            {
                carryOverMaxDate = picker.SelectedDate;
            }
            ApplyCarryOverFilters();
        }

        private void btnClearCarryOverFilters_Click(object sender, RoutedEventArgs e)
        {
            ResetCarryOverFilters();
        }

        private void ShowCarryOverView()
        {
            if (isCarryOverViewActive)
            {
                return;
            }

            try
            {
                var selectedCompany = currentConfig.CompanyConfigs?.FirstOrDefault(c => c.CompanyCode == currentConfig.SelectedCompanyCode);
                if (selectedCompany == null)
                {
                    MessageBox.Show("Hareket kayıtlarını görüntülemek için önce bir firma seçin.", "Firma Seçilmedi", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Ay seçimi için modal göster
                int currentYear = selectedCompany.PayrollYear > 0 ? selectedCompany.PayrollYear : DateTime.Now.Year;
                int currentMonth = selectedCompany.PayrollMonth > 0 ? selectedCompany.PayrollMonth : DateTime.Now.Month;

                var monthSelectionWindow = new Window
                {
                    Title = "Ay Seçimi",
                    Width = 400,
                    Height = 250,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = this,
                    ResizeMode = ResizeMode.NoResize
                };

                var grid = new Grid();
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var title = new TextBlock
                {
                    Text = "Görüntülenecek ayı seçin:",
                    FontSize = 16,
                    FontWeight = FontWeights.Bold,
                    FontFamily = new FontFamily("pack://application:,,,/Fonts/Poppins-Regular.ttf#Poppins"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(20, 20, 20, 20)
                };
                Grid.SetRow(title, 0);

                var yearPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(20, 0, 20, 10)
                };
                yearPanel.Children.Add(new TextBlock { Text = "Yıl:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
                var txtYear = new TextBox
                {
                    Width = 150,
                    Height = 30,
                    Text = currentYear.ToString(),
                    VerticalContentAlignment = VerticalAlignment.Center,
                    FontFamily = new FontFamily("pack://application:,,,/Fonts/Poppins-Regular.ttf#Poppins")
                };
                // Border radius için Border ile sarmala
                var yearBorder = new Border
                {
                    Width = 150,
                    Height = 30,
                    CornerRadius = new CornerRadius(6),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                    BorderThickness = new Thickness(1),
                    Background = Brushes.White,
                    Child = txtYear
                };
                txtYear.BorderThickness = new Thickness(0);
                txtYear.Background = Brushes.Transparent;
                yearPanel.Children.Add(yearBorder);
                Grid.SetRow(yearPanel, 1);

                var monthPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(20, 0, 20, 20)
                };
                monthPanel.Children.Add(new TextBlock { Text = "Ay:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
                var cmbMonth = new ComboBox
                {
                    Width = 150,
                    Height = 30,
                    FontFamily = new FontFamily("pack://application:,,,/Fonts/Poppins-Regular.ttf#Poppins"),
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Padding = new Thickness(0)
                };
                // Border radius için Border ile sarmala
                var monthBorder = new Border
                {
                    Width = 150,
                    Height = 30,
                    CornerRadius = new CornerRadius(6),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                    BorderThickness = new Thickness(1),
                    Background = Brushes.White,
                    Child = cmbMonth
                };
                cmbMonth.BorderThickness = new Thickness(0);
                cmbMonth.Background = Brushes.Transparent;
                for (int i = 1; i <= 12; i++)
                {
                    cmbMonth.Items.Add(new { Value = i, Display = new CultureInfo("tr-TR").DateTimeFormat.GetMonthName(i) });
                }
                cmbMonth.DisplayMemberPath = "Display";
                cmbMonth.SelectedValuePath = "Value";
                cmbMonth.SelectedValue = currentMonth;
                monthPanel.Children.Add(monthBorder);
                Grid.SetRow(monthPanel, 2);

                var buttonPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(20, 0, 20, 20)
                };

                var btnOK = new Button
                {
                    Content = "Görüntüle",
                    Width = 100,
                    Height = 35,
                    Margin = new Thickness(5, 0, 5, 0),
                    Background = Brushes.Green,
                    Foreground = Brushes.White
                };

                var btnCancel = new Button
                {
                    Content = "İptal",
                    Width = 80,
                    Height = 35,
                    Margin = new Thickness(5, 0, 5, 0),
                    Background = Brushes.Gray,
                    Foreground = Brushes.White
                };

                int selectedYear = currentYear;
                int selectedMonth = currentMonth;

                btnOK.Click += (s, e) =>
                {
                    if (int.TryParse(txtYear.Text, out int year) && year >= 2000 && year <= 2100)
                    {
                        selectedYear = year;
                    }
                    if (cmbMonth.SelectedValue is int month)
                    {
                        selectedMonth = month;
                    }
                    monthSelectionWindow.DialogResult = true;
                };

                btnCancel.Click += (s, e) =>
                {
                    monthSelectionWindow.DialogResult = false;
                };

                buttonPanel.Children.Add(btnOK);
                buttonPanel.Children.Add(btnCancel);
                Grid.SetRow(buttonPanel, 3);

                grid.Children.Add(title);
                grid.Children.Add(yearPanel);
                grid.Children.Add(monthPanel);
                grid.Children.Add(buttonPanel);

                monthSelectionWindow.Content = grid;

                if (monthSelectionWindow.ShowDialog() == true)
                {
                    // Seçilen ayın verilerini yükle
                    latestCarryOverSnapshot = pdksDataService.GetCarryOverSnapshot(selectedCompany, selectedYear, selectedMonth);
                    
                    if (latestCarryOverSnapshot != null)
                    {
                        // Seçilen ay ve yılı sakla
                        selectedCarryOverYear = selectedYear;
                        selectedCarryOverMonth = selectedMonth;
                        
                        PopulateCarryOverShiftOptions(latestCarryOverSnapshot);
                        ResetCarryOverFilters(applyAfterReset: false);
                        ApplyCarryOverFilters();
                    }
                    else
                    {
                        MessageBox.Show("Seçilen ay için veri bulunamadı.", "Veri Yok", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }

                    StepContentGrid.Visibility = Visibility.Collapsed;
                    CarryOverContent.Visibility = Visibility.Visible;
                    var monthName = new CultureInfo("tr-TR").DateTimeFormat.GetMonthName(selectedMonth);
                    StepTitleText.Text = $"Hareket Kayıtları - {monthName} {selectedYear}";
                    ProgressText.Text = $"Hareket Kayıtları Görüntüleme";
                    btnPrevious.IsEnabled = false;
                    btnNext.IsEnabled = false;
                    isCarryOverViewActive = true;
                    
                    // Accordion'ı ilk render'da bir kere açıp kapat (accordion olduğunu göster)
                    if (CarryOverFilterExpander != null && !hasCarryOverExpanderAnimated)
                    {
                        hasCarryOverExpanderAnimated = true;
                        // İlk önce aç
                        CarryOverFilterExpander.IsExpanded = true;
                        
                        // UI render edildikten sonra bir animasyon göster ve kapat
                        RoutedEventHandler? loadedHandler = null;
                        loadedHandler = (s, e) =>
                        {
                            // Event handler'ı kaldır (sadece bir kere çalışsın)
                            if (loadedHandler != null)
                            {
                                CarryOverContent.Loaded -= loadedHandler;
                            }
                            
                            // Biraz bekle ki açılış animasyonu görünsün
                            Dispatcher.InvokeAsync(async () =>
                            {
                                await Task.Delay(400); // 400ms bekle (açılış animasyonu için)
                                if (CarryOverFilterExpander != null)
                                {
                                    CarryOverFilterExpander.IsExpanded = false;
                                }
                            }, System.Windows.Threading.DispatcherPriority.Loaded);
                        };
                        CarryOverContent.Loaded += loadedHandler;
                    }
                }
            }
            catch (Exception ex)
            {
                LogToTerminal($"[CarryOver] Hareket kayıtları yüklenemedi: {ex.Message}");
                LogToTerminal($"[CarryOver] Stack Trace: {ex.StackTrace}");
                MessageBox.Show($"Hareket kayıtları yüklenemedi: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void HideCarryOverView()
        {
            if (!isCarryOverViewActive)
            {
                return;
            }

            CarryOverContent.Visibility = Visibility.Collapsed;
            StepContentGrid.Visibility = Visibility.Visible;
            isCarryOverViewActive = false;
            UpdateStepDisplay();
        }

        private void btnManualAddCarryOver_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var selectedCompany = currentConfig.CompanyConfigs?.FirstOrDefault(c => c.CompanyCode == currentConfig.SelectedCompanyCode);
                if (selectedCompany == null)
                {
                    MessageBox.Show("Manuel veri eklemek için önce bir firma seçin.", "Firma Seçilmedi", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                int year = selectedCompany.PayrollYear > 0 ? selectedCompany.PayrollYear : DateTime.Now.Year;
                int month = selectedCompany.PayrollMonth > 0 ? selectedCompany.PayrollMonth : DateTime.Now.Month;
                
                // Önceki ay bilgisini al
                var prev = GetPreviousPeriod(year, month);
                
                var modal = new StoredPDKSRecordsModal(selectedCompany.CompanyCode, prev.year, prev.month)
                {
                    Owner = this
                };

                if (modal.ShowDialog() == true)
                {
                    // Başarıyla kaydedildi, listeyi yenile
                    LogToTerminal($"[Kayıtlı PDKS] Manuel veri eklendi: {modal.ImportedRecords?.Count ?? 0} günlük kayıt");
                    
                    // Snapshot'ı yeniden yükle
                    int snapshotYear = selectedCompany.PayrollYear > 0 ? selectedCompany.PayrollYear : DateTime.Now.Year;
                    int snapshotMonth = selectedCompany.PayrollMonth > 0 ? selectedCompany.PayrollMonth : DateTime.Now.Month;
                    var prevPeriod = GetPreviousPeriod(snapshotYear, snapshotMonth);
                    latestCarryOverSnapshot = pdksDataService.GetCarryOverSnapshot(selectedCompany, prevPeriod.year, prevPeriod.month);
                    PopulateCarryOverShiftOptions(latestCarryOverSnapshot);
                    ResetCarryOverFilters(applyAfterReset: false);
                    ApplyCarryOverFilters();
                    
                    MessageBox.Show(
                        $"{modal.ImportedRecords?.Count ?? 0} günlük kayıt başarıyla eklendi ve liste güncellendi.",
                        "Başarılı",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                LogToTerminal($"[CarryOver] Manuel veri ekleme hatası: {ex.Message}");
                MessageBox.Show($"Manuel veri eklenirken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RefreshCarryOverList(CompanyConfig selectedCompany)
        {
            try
            {
                if (selectedCompany == null)
                {
                    return;
                }

                int year = selectedCompany.PayrollYear > 0 ? selectedCompany.PayrollYear : DateTime.Now.Year;
                int month = selectedCompany.PayrollMonth > 0 ? selectedCompany.PayrollMonth : DateTime.Now.Month;
                var prevPeriod = GetPreviousPeriod(year, month);
                latestCarryOverSnapshot = pdksDataService.GetCarryOverSnapshot(selectedCompany, prevPeriod.year, prevPeriod.month);
                PopulateCarryOverShiftOptions(latestCarryOverSnapshot);
                ResetCarryOverFilters(applyAfterReset: false);
                ApplyCarryOverFilters();
                
                LogToTerminal($"[CarryOver] Liste yenilendi: {latestCarryOverSnapshot?.Records?.Count ?? 0} kayıt");
            }
            catch (Exception ex)
            {
                LogToTerminal($"[CarryOver] Liste yenileme hatası: {ex.Message}");
            }
        }

        private (int year, int month) GetPreviousPeriod(int year, int month)
        {
            if (month <= 1)
            {
                return (year - 1, 12);
            }
            return (year, month - 1);
        }

        private void btnSelectAllCarryOver_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (CarryOverListView.ItemsSource == null)
                {
                    return;
                }

                var records = CarryOverListView.ItemsSource.Cast<StoredPDKSRecord>().ToList();
                bool allSelected = records.All(r => r.IsSelected);

                // Tümünü seç veya seçimi kaldır
                foreach (var record in records)
                {
                    record.IsSelected = !allSelected;
                }

                // ListView'i yenile
                CarryOverListView.Items.Refresh();
            }
            catch (Exception ex)
            {
                LogToTerminal($"[CarryOver] Tümünü seç hatası: {ex.Message}");
                MessageBox.Show($"Tümünü seç işlemi sırasında hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void btnDeleteSelectedCarryOver_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (CarryOverListView.ItemsSource == null)
                {
                    MessageBox.Show("Silinecek kayıt bulunamadı.", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var records = CarryOverListView.ItemsSource.Cast<StoredPDKSRecord>().ToList();
                var selectedRecords = records.Where(r => r.IsSelected).ToList();

                if (selectedRecords.Count == 0)
                {
                    MessageBox.Show("Lütfen silmek istediğiniz kayıtları seçin.", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Admin doğrulama
                var adminModal = new AdminVerificationModal
                {
                    Owner = this
                };

                bool? dialogResult = adminModal.ShowDialog();
                
                // DialogResult == true ise doğrulama başarılı demektir
                if (dialogResult != true)
                {
                    LogToTerminal("[CarryOver] Admin doğrulama iptal edildi veya başarısız oldu.");
                    return; // Kullanıcı iptal etti veya doğrulama başarısız
                }

                LogToTerminal("[CarryOver] Admin doğrulama başarılı, silme işlemi devam ediyor...");

                // Onay mesajı
                var result = MessageBox.Show(
                    $"{selectedRecords.Count} kayıt silinecek. Bu işlem geri alınamaz. Devam etmek istiyor musunuz?",
                    "Silme Onayı",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes)
                {
                    return;
                }

                // Seçili firma bilgilerini al
                var selectedCompany = currentConfig.CompanyConfigs?.FirstOrDefault(c => c.CompanyCode == currentConfig.SelectedCompanyCode);
                if (selectedCompany == null)
                {
                    MessageBox.Show("Firma bilgisi bulunamadı.", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                int year = selectedCompany.PayrollYear > 0 ? selectedCompany.PayrollYear : DateTime.Now.Year;
                int month = selectedCompany.PayrollMonth > 0 ? selectedCompany.PayrollMonth : DateTime.Now.Month;
                var prevPeriod = GetPreviousPeriod(year, month);

                // Kayıtları sil
                var carryOverService = new CarryOverStateService();
                carryOverService.DeleteStoredPDKSRecords(
                    selectedCompany.CompanyCode,
                    prevPeriod.year,
                    prevPeriod.month,
                    selectedRecords);

                LogToTerminal($"[CarryOver] {selectedRecords.Count} kayıt silindi");

                // Listeyi yenile
                RefreshCarryOverList(selectedCompany);

                MessageBox.Show(
                    $"{selectedRecords.Count} kayıt başarıyla silindi.",
                    "Başarılı",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                LogToTerminal($"[CarryOver] Silme hatası: {ex.Message}");
                MessageBox.Show($"Kayıtlar silinirken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateCarryOverFooterButtons(bool hasRecords)
        {
            if (!isCarryOverViewActive)
            {
                return;
            }

            // Footer butonlarını güncelle
            if (btnPrevious != null)
            {
                btnPrevious.IsEnabled = true; // Geri butonu her zaman aktif (CarryOver view'dan çıkmak için)
            }

            if (btnNext != null)
            {
                btnNext.IsEnabled = hasRecords; // İleri butonu sadece veri varsa aktif
            }

            // Progress text'i güncelle
            if (ProgressText != null)
            {
                if (hasRecords)
                {
                    ProgressText.Text = $"Kayıtlı PDKS Verileri - {latestCarryOverSnapshot?.Records?.Count ?? 0} kayıt";
                }
                else
                {
                    ProgressText.Text = "Kayıtlı PDKS Verileri - Henüz veri yok";
                }
            }
        }

        private void UpdateNavigationButtons()
        {
            btnPrevious.IsEnabled = currentStep > 1;
            btnNext.Content = currentStep == totalSteps ? "Bitir ✅" : "İleri ➡️";
        }

        private void chkIncludePreviousMonthData_Checked(object sender, RoutedEventArgs e)
        {
            if (isUpdatingCarryOverToggle)
            {
                return;
            }

            includePreviousMonthCarryOver = true;
            RefreshPreviousMonthCarryOverStatus();
        }

        private void chkIncludePreviousMonthData_Unchecked(object sender, RoutedEventArgs e)
        {
            if (isUpdatingCarryOverToggle)
            {
                return;
            }

            includePreviousMonthCarryOver = false;
            RefreshPreviousMonthCarryOverStatus();
        }

        private void btnSelectTemplate_Click(object sender, RoutedEventArgs e)
        {
            LogToTerminal($"[Step 1] Şablon seçimi butonuna tıklandı");
            if (CompanyListBox.SelectedItem is not CompanyConfig)
            {
                MessageBox.Show("Lütfen önce bir firma seçin.", "Firma Seçilmedi", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!OpenTemplateSelectionDialog(selectedTemplatePath))
                {
                    LogToTerminal($"[Step 1] Şablon seçimi iptal edildi");
            }
        }

        private string? FindPythonExecutable()
        {
            // Windows Python Launcher'ı dene (en güvenilir)
            string[] pythonCommands = { "py", "python", "python3" };
            
            foreach (var cmd in pythonCommands)
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = cmd,
                        Arguments = "--version",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    var process = Process.Start(psi);
                    if (process != null)
                    {
                        process.WaitForExit(2000); // 2 saniye timeout
                        if (process.ExitCode == 0 || process.ExitCode == 1) // --version genelde 0 veya 1 döner
                        {
                            LogToTerminal($"[Python] Bulundu: {cmd}");
                            return cmd;
                        }
                    }
                }
                catch
                {
                    // Bu komut bulunamadı, bir sonrakini dene
                    continue;
                }
            }
            
            return null;
        }

        /// <summary>
        /// Gerekli Python paketlerinin (pandas, openpyxl) yüklü olduğunu garanti eder.
        /// Eksikse arka planda pip ile kurar.
        /// </summary>
        private bool EnsurePythonPackages(string pythonCmd)
        {
            try
            {
                // Önce pandas yüklü mü diye kontrol et
                var checkPsi = new ProcessStartInfo
                {
                    FileName = pythonCmd,
                    Arguments = "-c \"import pandas, openpyxl\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                var checkProcess = Process.Start(checkPsi);
                if (checkProcess != null)
                {
                    checkProcess.WaitForExit(10000); // 10 saniye
                    if (checkProcess.ExitCode == 0)
                    {
                        // Zaten kurulu
                        LogToTerminal("[Python] Gerekli paketler (pandas, openpyxl) zaten yüklü");
                        return true;
                    }
                }

                LogToTerminal("[Python] Gerekli paketler eksik, pip ile kurulacak (pandas, openpyxl)");

                // Kullanıcıya teknik detay vermeden kısa bilgi mesajı
                Application.Current.Dispatcher.Invoke(() =>
                {
                    MessageBox.Show(
                        "Gerekli bileşenler yükleniyor. Bu işlem sırasında lütfen uygulamayı kapatmayın.",
                        "Hazırlık Yapılıyor",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                });

                // pip ile kurulum
                var installPsi = new ProcessStartInfo
                {
                    FileName = pythonCmd,
                    Arguments = "-m pip install pandas openpyxl",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                var installProcess = Process.Start(installPsi);
                if (installProcess == null)
                {
                    LogToTerminal("[Python] pip kurulumu başlatılamadı");
                    return false;
                }

                string installOutput = installProcess.StandardOutput.ReadToEnd();
                string installError = installProcess.StandardError.ReadToEnd();
                installProcess.WaitForExit();

                LogToTerminal($"[Python] pip çıktısı: {installOutput}");
                if (installProcess.ExitCode != 0)
                {
                    LogToTerminal($"[Python] pip hatası: {installError}");
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show(
                            "Gerekli bileşenler yüklenirken hata oluştu. Lütfen internet bağlantınızı ve Python/pip kurulumunuzu kontrol edin.",
                            "Kurulum Hatası",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    });
                    return false;
                }

                LogToTerminal("[Python] Gerekli paketler başarıyla kuruldu");
                return true;
            }
            catch (Exception ex)
            {
                LogToTerminal($"[Python] Paket kontrol/kurulum hatası: {ex.Message}");
                return false;
            }
        }

        private void AnalyzeTemplate(string filePath)
        {
            LogToTerminal($"[Step 1] Şablon analizi başlatılıyor: {System.IO.Path.GetFileName(filePath)}");
            selectedTemplatePath = filePath;
            UpdateTemplateSummary();

            try
            {
                // Python'u bul
                string? pythonCmd = FindPythonExecutable();
                if (pythonCmd == null)
                {
                    string errorMsg = "Python bulunamadı. Lütfen Python'u yükleyin ve PATH'e ekleyin, veya Windows Python Launcher (py) kullanın.";
                    LogToTerminal($"[Step 1] {errorMsg}");
                    selectedTemplatePath = "";
                    UpdateTemplateSummary();
                    MessageBox.Show(errorMsg, "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Gerekli Python paketlerini kontrol et / kur
                if (!EnsurePythonPackages(pythonCmd))
                {
                    selectedTemplatePath = "";
                    UpdateTemplateSummary();
                    LogToTerminal("[Step 1] Gerekli Python paketleri yüklenemedi, şablon analizi iptal edildi");
                    return;
                }

                // Python ile şablonu analiz et
                var psi = new ProcessStartInfo
                {
                    FileName = pythonCmd,
                    Arguments = $"-c \"import pandas as pd; df = pd.read_excel('{filePath.Replace("\\", "\\\\")}'); print(f'Satır sayısı: {{len(df)}}'); print(f'Sütun sayısı: {{df.shape[1]}}'); print('İlk satır:', df.iloc[0].tolist() if len(df) > 0 else 'Boş dosya')\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                var process = Process.Start(psi);
                if (process == null)
                {
                    throw new Exception("Python işlemi başlatılamadı");
                }

                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode == 0)
                {
                    LogToTerminal($"[Step 1] Şablon başarıyla analiz edildi");

                    // Python çıktısını parse et ve anlamlı bilgi çıkar
                    string templateInfo = ParseTemplateInfo(output, filePath);
                    UpdateTemplateSummary();

                    // Şablon önizlemesi bilgilerini logla
                    LogToTerminal($"[Şablon Önizleme] {templateInfo.Replace("\n", " | ")}");

                    // Personel kayıtlarını yükle
                    LoadPersonnelRecords(filePath);
                }
                else
                {
                    string errorMsg = string.IsNullOrWhiteSpace(error) ? "Bilinmeyen hata" : error.Trim();
                    LogToTerminal($"[Step 1] Şablon analiz edilemedi: {errorMsg}");
                    selectedTemplatePath = "";
                    UpdateTemplateSummary();
                    MessageBox.Show($"Şablon analiz edilemedi: {errorMsg}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                LogToTerminal($"[Step 1] Şablon analizi hatası: {ex.Message}");
                selectedTemplatePath = "";
                UpdateTemplateSummary();
                MessageBox.Show($"Şablon analizi sırasında hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ShowPersonnelPreviewModal()
        {
            try
            {
                var modal = new PersonnelPreviewModal(personnelRecords);
                modal.Owner = this; // Ana pencereyi owner olarak ayarla
                modal.ShowDialog(); // Modal olarak aç

                LogToTerminal($"[Personel Önizleme] Modal kapatıldı - kullanıcı onayladı");
            }
            catch (Exception ex)
            {
                LogToTerminal($"[Personel Önizleme] Modal açma hatası: {ex.Message}");
                MessageBox.Show($"Personel önizleme modal'ı açılırken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdatePDKSMatchingRecords(List<PDKSMatchingRecord> pdksMatchingRecords)
        {
            try
            {
                var pdksRecords = pdksDataService.GetAllRecords();
                var uniquePersonnel = new Dictionary<string, bool>();

                // Her PDKS kaydının eşleştirme durumunu topla
                foreach (var record in pdksRecords)
                {
                    string key = $"{record.PersonnelCode}_{record.PersonnelName}";
                    if (!uniquePersonnel.ContainsKey(key))
                    {
                        uniquePersonnel[key] = record.IsMatched;
                    }
                }

                // PDKSMatchingRecord'ları güncelle
                foreach (var matchingRecord in pdksMatchingRecords)
                {
                    string key = $"{matchingRecord.PersonnelCode}_{matchingRecord.Name}";
                    if (uniquePersonnel.ContainsKey(key))
                    {
                        matchingRecord.IsMatched = uniquePersonnel[key];
                        if (matchingRecord.IsMatched)
                        {
                            var matchedPDKSRecord = pdksRecords.FirstOrDefault(r =>
                                r.PersonnelCode == matchingRecord.PersonnelCode &&
                                r.PersonnelName == matchingRecord.Name);

                            if (matchedPDKSRecord != null && matchedPDKSRecord.MatchedPersonnel != null)
                            {
                                matchingRecord.MatchedPersonnelCode = matchedPDKSRecord.MatchedPersonnel.PersonnelCode;
                                matchingRecord.MatchedPersonnelName = matchedPDKSRecord.MatchedPersonnel.Name;
                                matchingRecord.MatchType = matchedPDKSRecord.MatchType;
                            }
                        }
                    }
                }

                LogToTerminal($"[PDKS Eşleştirme Güncelleme] {pdksMatchingRecords.Count(r => r.IsMatched)} kayıt eşleştirildi");
            }
            catch (Exception ex)
            {
                LogToTerminal($"[PDKS Eşleştirme Güncelleme] Hata: {ex.Message}");
            }
        }

        private List<PDKSMatchingRecord> ExtractPersonnelFromPDKSData()
        {
            try
            {
                var pdksRecords = pdksDataService.GetAllRecords();
                var uniquePersonnel = new Dictionary<string, PDKSMatchingRecord>();

                LogToTerminal($"[PDKS Personel Çıkarma] {pdksRecords.Count} kayıt işleniyor");

                foreach (var record in pdksRecords)
                {
                    string key = $"{record.PersonnelCode}_{record.PersonnelName}";
                    if (!uniquePersonnel.ContainsKey(key))
                    {
                        uniquePersonnel[key] = new PDKSMatchingRecord
                        {
                            PersonnelCode = record.PersonnelCode,
                            Name = record.PersonnelName,
                            TCNo = "", // PDKS dosyasında TCKN yok
                            IsMatched = false,
                            TotalRecords = 0
                        };
                    }
                    uniquePersonnel[key].TotalRecords++;
                }

                LogToTerminal($"[PDKS Personel Çıkarma] {uniquePersonnel.Count} benzersiz personel çıkarıldı");
                return uniquePersonnel.Values.ToList();
            }
            catch (Exception ex)
            {
                LogToTerminal($"[PDKS Personel Çıkarma] Hata: {ex.Message}");
                return new List<PDKSMatchingRecord>();
            }
        }



        private void ShowPDKSMatchingModal(List<PDKSMatchingRecord> pdksPersonnelRecords)
        {
            try
            {
                var modal = new PDKSMatchingModal(pdksPersonnelRecords);
                modal.Owner = this;
                modal.ShowDialog();

                LogToTerminal($"[PDKS Eşleştirme] Modal kapatıldı - eşleştirme tamamlandı");
            }
            catch (Exception ex)
            {
                LogToTerminal($"[PDKS Eşleştirme] Modal açma hatası: {ex.Message}");
                MessageBox.Show($"PDKS eşleştirme modal'ı açılırken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadPersonnelRecords(string filePath)
        {
            LogToTerminal($"[Step 1] Personel kayıtları yükleniyor: {System.IO.Path.GetFileName(filePath)}");
            try
            {
                // Python'u bul
                string? pythonCmd = FindPythonExecutable();
                if (pythonCmd == null)
                {
                    string errorMsg = "Python bulunamadı. Lütfen Python'u yükleyin ve PATH'e ekleyin, veya Windows Python Launcher (py) kullanın.";
                    LogToTerminal($"[Step 1] {errorMsg}");
                    MessageBox.Show(errorMsg, "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Gerekli Python paketlerini kontrol et / kur
                if (!EnsurePythonPackages(pythonCmd))
                {
                    LogToTerminal("[Step 1] Gerekli Python paketleri yüklenemedi, personel kayıtları yükleme iptal edildi");
                    return;
                }

                // Python ile personel verilerini oku - tüm kayıtları al
                // Vio şablonunda: Sicil No | Ad Soyad (bitişik) | TCKN
                string pythonScript = @"
import pandas as pd
import json

file_path = r'" + filePath.Replace("\\", "\\\\") + @"'
df = pd.read_excel(file_path)

entry_candidates = ['(Bu Ay) Giriş Gün', '(Bu Ay) Giris Gun', '(Bu Ay) Giriş Gun', '(Bu Ay) Giris Gün', 'Bu Ay Giriş Gün', 'Bu Ay Giris Gun']
exit_candidates = ['(Bu Ay) Çıkış Gün', '(Bu Ay) Cikis Gun', '(Bu Ay) Çıkış Gun', '(Bu Ay) Cikis Gün', 'Bu Ay Çıkış Gün', 'Bu Ay Cikis Gun']

def normalize_day(value):
    if pd.isna(value):
        return ''
    try:
        num = int(float(value))
        if num <= 0:
            return ''
        return str(num)
    except Exception:
        return ''

records = []
for _, row in df.iterrows():
    entry_day = ''
    exit_day = ''

    for col in entry_candidates:
        if col in df.columns:
            entry_day = normalize_day(row[col])
            if entry_day:
                break

    for col in exit_candidates:
        if col in df.columns:
            exit_day = normalize_day(row[col])
            if exit_day:
                break

    if not entry_day and len(df.columns) > 4:
        entry_day = normalize_day(row[df.columns[4]])
    if not exit_day and len(df.columns) > 5:
        exit_day = normalize_day(row[df.columns[5]])

    code_col = df.columns[0]
    name_col = df.columns[1] if len(df.columns) > 1 else code_col
    tc_col = df.columns[2] if len(df.columns) > 2 else None

    personnel_code = str(row[code_col]).strip() if pd.notna(row[code_col]) else ''
    name_value = str(row[name_col]).strip() if pd.notna(row[name_col]) else ''
    tc_value = ''
    if tc_col is not None and pd.notna(row[tc_col]):
        tc_value = str(row[tc_col]).strip()

    records.append({
        'personnelCode': personnel_code,
        'name': name_value,
        'surname': '',
        'tc': tc_value,
        'entryDay': entry_day,
        'exitDay': exit_day
    })

print(json.dumps(records))
";

                var psi = new ProcessStartInfo
                {
                    FileName = pythonCmd,
                    Arguments = $"-c \"{pythonScript}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                var process = Process.Start(psi);
                if (process == null)
                {
                    throw new Exception("Python işlemi başlatılamadı");
                }

                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode == 0)
                {
                    // JSON parse et ve personelRecords'a ekle
                    personnelRecords.Clear();

                    try
                    {
                        var records = System.Text.Json.JsonSerializer.Deserialize<List<Dictionary<string, string>>>(output);
                        Console.WriteLine($"[ERP Load] JSON parsed, {records.Count} records from ERP template");

                        foreach (var record in records)
                        {
                            var personnel = new PersonnelRecord
                            {
                                PersonnelCode = record.GetValueOrDefault("personnelCode", ""),
                                Name = record.GetValueOrDefault("name", ""),
                                Surname = record.GetValueOrDefault("surname", ""),
                                TCNo = record.GetValueOrDefault("tc", "")
                            };

                            personnel.EntryDay = ParseNullableInt(record.GetValueOrDefault("entryDay", ""));
                            personnel.ExitDay = ParseNullableInt(record.GetValueOrDefault("exitDay", ""));

                            personnelRecords.Add(personnel);
                            if (personnelRecords.Count <= 10) // İlk 10'u logla
                            {
                                Console.WriteLine($"[ERP Load] Loaded: '{personnel.Name}' ({personnel.PersonnelCode}) - TC: {personnel.TCNo}");
                            }
                        }
                    }
                    catch (Exception parseEx)
                    {
                        LogToTerminal($"[Step 1] JSON parse hatası: {parseEx.Message}");
                        // Fallback: basit parse dene
                        if (output.Contains("[") && output.Contains("]"))
                        {
                            // Basit parse fallback - ileride geliştirilecek
                            personnelRecords.Add(new PersonnelRecord { PersonnelCode = "Parse Hatası", Name = "Veri", Surname = "Okunamadı", TCNo = "00000000000" });
                        }
                    }

                    LogToTerminal($"[Step 1] {personnelRecords.Count} personel kaydı yüklendi (Sicil No | Ad Soyad | TCKN formatında)");

                    // Personel listesini göster
                    ShowPersonnelPreviewModal();
                }
                else
                {
                    LogToTerminal($"[Step 1] Python script hatası: {error}");
                    MessageBox.Show($"Personel verileri okunurken hata oluştu: {error}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                LogToTerminal($"[Step 1] Personel kayıtları yüklenirken hata: {ex.Message}");
                MessageBox.Show($"Personel kayıtları yüklenirken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        private void btnLoadPDKSData_Click(object sender, RoutedEventArgs e)
        {
            LogToTerminal($"[Step 2] PDKS veri dosyası seçimi butonuna tıklandı");
            try
            {
                OpenFileDialog openFileDialog = new OpenFileDialog
                {
                    Title = "PDKS Veri Dosyasını Seçin",
                    Filter = "Excel Dosyaları (*.xlsx)|*.xlsx|CSV Dosyaları (*.csv)|*.csv|Tüm Dosyalar (*.*)|*.*",
                    InitialDirectory = Directory.GetCurrentDirectory()
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    selectedPDKSPath = openFileDialog.FileName;
                    LogToTerminal($"[Step 2] PDKS dosyası seçildi: {System.IO.Path.GetFileName(selectedPDKSPath)}");
                    LoadPDKSData(selectedPDKSPath);
                }
                else
                {
                    LogToTerminal($"[Step 2] PDKS dosya seçimi iptal edildi");
                }
            }
            catch (Exception ex)
            {
                LogToTerminal($"[Step 2] PDKS veri seçimi hatası: {ex.Message}");
                MessageBox.Show($"PDKS veri seçimi sırasında hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadPDKSData(string filePath)
        {
            LogToTerminal($"[Step 2] PDKS verileri yükleniyor: {System.IO.Path.GetFileName(filePath)}");
            try
            {
                var selectedCompany = currentConfig.CompanyConfigs?.FirstOrDefault(c =>
                    c.CompanyCode.Equals(currentConfig.SelectedCompanyCode, StringComparison.OrdinalIgnoreCase));

                // Veri şablonu algılamayı dene
                var templates = dataTemplateService.LoadTemplates(selectedCompany?.CompanyCode ?? currentConfig.SelectedCompanyCode);
                string detectionReason;
                detectedPdksTemplate = dataTemplateDetectionService.DetectTemplate(filePath, templates, out detectionReason);

                if (detectedPdksTemplate != null)
                {
                    LogToTerminal($"[Step 2] Veri şablonu algılandı: {detectedPdksTemplate.Name} (Tür: {detectedPdksTemplate.TemplateTypeDisplay}) - {detectionReason}");
                }
                else
                {
                    LogToTerminal($"[Step 2] Veri şablonu algılanamadı: {detectionReason}");
                }

                // PDKS verilerini şablona göre yükle
                if (detectedPdksTemplate != null && detectedPdksTemplate.TemplateType == "Horizontal_DailyHours")
                {
                    // Yatay puantaj şablonu - gün bazlı saatler
                    int year = selectedCompany?.PayrollYear > 0 ? selectedCompany.PayrollYear : DateTime.Now.Year;
                    int month = selectedCompany?.PayrollMonth > 0 ? selectedCompany.PayrollMonth : DateTime.Now.Month;

                    LogToTerminal($"[Step 2] Yatay puantaj şablonu ile PDKS verileri yükleniyor (Yıl={year}, Ay={month})");
                    pdksDataService.LoadHorizontalDailyHours(filePath, detectedPdksTemplate, year, month, LogToTerminal, selectedCompany);
                }
                else
                {
                    // Varsayılan PDKS log formatı (Python ile)
                    pdksDataService.LoadPDKSData(filePath);
                }

                var loadedCount = pdksDataService.GetAllRecords().Count;
                LogToTerminal($"[Step 2] PDKS verileri yüklendi. Toplam kayıt sayısı: {loadedCount}");

                if (loadedCount == 0)
                {
                    LogToTerminal("[Step 2] Uyarı: Yüklenen PDKS verisi 0 kayıt içeriyor. Şablon/kolon yapısı beklendiği gibi olmayabilir.");
                }
                LogToTerminal($"[Step 2] PDKS verileri yüklendi");

                // PDKS verilerinden personel listesini çıkar
                var pdksPersonnelRecords = ExtractPersonnelFromPDKSData();
                LogToTerminal($"[Step 2] PDKS'den {pdksPersonnelRecords.Count} benzersiz personel çıkarıldı");

                // ERP şablonu ile PDKS personel eşleştirmesi yap
                pdksDataService.MatchPersonnelRecords(personnelRecords);
                var matchedRecords = pdksDataService.GetAllRecords().Where(r => r.IsMatched).ToList();
                LogToTerminal($"[Step 2] PDKS-ERP personel eşleştirmesi tamamlandı: {matchedRecords.Count} eşleşen");

                // PDKS personel eşleştirme sonuçlarını güncelle
                UpdatePDKSMatchingRecords(pdksPersonnelRecords);

                // PDKS personel listesini modal ile göster (eşleştirme sonuçlarıyla birlikte)
                ShowPDKSMatchingModal(pdksPersonnelRecords);

                // Sonuçları göster
                DisplayPDKSResults();

                PDKSDataPreviewBorder.Visibility = Visibility.Visible;
                LogToTerminal($"[Step 2] PDKS veri önizlemesi gösterildi");
            }
            catch (Exception ex)
            {
                LogToTerminal($"[Step 2] PDKS verileri yüklenirken hata: {ex.Message}");
                MessageBox.Show($"PDKS verileri yüklenirken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DisplayPDKSResults()
        {
            var summary = pdksDataService.GetMatchingSummary();

            int total = summary["Toplam Kayıt"];
            int matched = summary["Eşleşen Kayıt"];
            int unmatched = summary["Eşleşmeyen Kayıt"];
            double ratio = total > 0 ? Math.Round((double)matched / total * 100, 1) : 0;

            txtTotalRecords.Text = total.ToString("N0");
            txtMatchedRecords.Text = matched.ToString("N0");
            txtUnmatchedRecords.Text = unmatched.ToString("N0");

            // Dosya adı ve eşleşme oranı gösterimi kaldırıldı

            if (PDKSDataDetailsText != null)
            {
                PDKSDataDetailsText.Text = unmatched > 0
                    ? "Bazı kayıtlar personel ile eşleşmedi. Detayları inceleyip gerekli düzeltmeleri yapabilirsiniz."
                    : "Tüm kayıtlar başarıyla personel ile eşleştirildi. Aşağıdaki özetler sonucu doğrulamaktadır.";
            }

            if (PDKSDataInfoList != null)
            {
                PDKSDataInfoList.Children.Clear();
                AddPDKSInfoLine("✅", "PDKS verileri başarıyla yüklendi.");
                AddPDKSInfoLine("📄", $"Toplam satır: {total:N0}");
                AddPDKSInfoLine("🧾", $"Eşleşen kayıt: {matched:N0}");

                if (unmatched > 0)
                {
                    AddPDKSInfoLine("⚠️", $"{unmatched:N0} kayıt eşleştirilemedi. Manuel kontrol önerilir.", Brushes.DarkOrange);
                }
                else
                {
                    AddPDKSInfoLine("🎉", "Tüm kayıtlar eşleşti.", Brushes.SeaGreen);
                }
            }

            pdksStatusSnapshot.Clear();
            AddPDKSStatusTag("PDKS verileri okundu ve analiz edildi.", Color.FromRgb(5, 150, 105));

            if (unmatched > 0)
            {
                AddPDKSStatusTag($"{unmatched} kayıt eşleşmedi. Detay kontrolü önerilir.", Color.FromRgb(220, 38, 38));
            }
            else
            {
                AddPDKSStatusTag("Tüm personel kayıtları başarıyla eşleşti.", Color.FromRgb(37, 99, 235));
            }

            UpdatePDKSStatusSummary();

            // PDKS sonuçlarını logla
            LogToTerminal($"[PDKS Sonuçları] Toplam: {total}, Eşleşen: {matched}, Eşleşmeyen: {unmatched}");
            LogToTerminal($"[PDKS Özet] Eşleşme oranı %{ratio:0.#}, Dosya: {System.IO.Path.GetFileName(selectedPDKSPath)}");
        }

        private void AddPDKSInfoLine(string icon, string text, Brush? brush = null)
        {
            if (PDKSDataInfoList == null)
            {
                return;
            }

            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, PDKSDataInfoList.Children.Count > 0 ? 4 : 0, 0, 0)
            };

            panel.Children.Add(new TextBlock
            {
                Text = icon,
                FontSize = 14,
                Margin = new Thickness(0, 0, 6, 0)
            });

            panel.Children.Add(new TextBlock
            {
                Text = text,
                FontSize = 13,
                Foreground = brush ?? Brushes.DimGray,
                FontFamily = new FontFamily("pack://application:,,,/Fonts/Poppins-Medium.ttf#Poppins"),
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            });

            PDKSDataInfoList.Children.Add(panel);
        }

        private void AddPDKSStatusTag(string text, Color backgroundColor)
        {
            pdksStatusSnapshot.Add(new StatusTagInfo
            {
                Text = text,
                Background = new SolidColorBrush(backgroundColor)
            });

        }

        private void UpdatePDKSStatusSummary()
        {
            if (PDKSStatusSummaryText == null || btnShowPDKSStatus == null)
            {
                return;
            }

            if (pdksStatusSnapshot.Count == 0)
            {
                PDKSStatusSummaryText.Text = "İşlem özeti henüz oluşturulmadı.";
                PDKSStatusSummaryText.Foreground = new SolidColorBrush(Color.FromRgb(75, 85, 99));
                btnShowPDKSStatus.IsEnabled = false;
                return;
            }

            var first = pdksStatusSnapshot[0];
            PDKSStatusSummaryText.Text = first.Text;

            if (first.Background is SolidColorBrush brush)
            {
                PDKSStatusSummaryText.Foreground = brush;
            }
            else
            {
                PDKSStatusSummaryText.Foreground = new SolidColorBrush(Color.FromRgb(37, 99, 235));
            }

            btnShowPDKSStatus.IsEnabled = true;
        }


        private async void btnProcessData_Click(object sender, RoutedEventArgs e)
        {
            LogToTerminal($"[Step 3] Veri işleme butonuna tıklandı");
            try
            {
                // İlerleme çubuğunu göster
                ProgressBorder.Visibility = Visibility.Visible;
                Step3ProgressText.Text = "Veriler işleniyor...";
                btnProcessData.IsEnabled = false;
                LogToTerminal($"[Step 3] Veri işleme başladı");

                // FM ve devamsızlık hesaplamalarını yap
                await ProcessPDKSData();

                // ERP şablonuna veri yaz
                await ExportToERP();

                // Sonuçları göster
                DisplayProcessingResults();

                ProgressBorder.Visibility = Visibility.Collapsed;
                btnProcessData.IsEnabled = true;

                LogToTerminal($"[Step 3] Veri işleme tamamlandı");
            }
            catch (Exception ex)
            {
                ProgressBorder.Visibility = Visibility.Collapsed;
                btnProcessData.IsEnabled = true;
                LogToTerminal($"[Step 3] Veri işleme hatası: {ex.Message}");
                MessageBox.Show($"Veri işleme sırasında hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnShowPDKSStatus_Click(object sender, RoutedEventArgs e)
        {
            if (pdksStatusSnapshot.Count == 0)
            {
                MessageBox.Show("Gösterilecek işlem özeti bulunmuyor.", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var modal = new PDKSStatusModal(pdksStatusSnapshot)
            {
                Owner = this
            };
            modal.ShowDialog();
        }

        private async Task ProcessPDKSData()
        {
            LogToTerminal($"[Step 3] PDKS veri işleme başlatılıyor");
            var selectedCompany = currentConfig.CompanyConfigs?.FirstOrDefault(c => c.CompanyCode == currentConfig.SelectedCompanyCode);
            if (selectedCompany == null)
            {
                throw new InvalidOperationException("Seçili firma bulunamadı. Lütfen Adım 1'de bir firma seçin.");
            }

            pdksDataService.ConfigureCarryOverTracking(selectedCompany, includePreviousMonthCarryOver);
            LogToTerminal(includePreviousMonthCarryOver
                ? "[Step 3] Önceki ay tatil devri işleme dahil edildi"
                : "[Step 3] Önceki ay tatil devri kapalı");

            await Task.Run(() =>
            {
                try
                {
                    // FM hesaplamalarını yap
                    pdksDataService.CalculateOvertimeAndAttendance(currentConfig);
                    LogToTerminal($"[Step 3] Fazla mesai hesaplamaları tamamlandı");
                }
                catch (Exception ex)
                {
                    LogToTerminal($"[Step 3] FM hesaplama hatası: {ex.Message}");
                    throw;
                }

                try
                {
                    // Devamsızlık hesaplamasını yap (firma bazlı)
                    int monthDays = DateTime.DaysInMonth(
                        selectedCompany.PayrollYear > 0 ? selectedCompany.PayrollYear : DateTime.Now.Year,
                        selectedCompany.PayrollMonth > 0 ? selectedCompany.PayrollMonth : DateTime.Now.Month);
                    pdksDataService.CalculateAbsenteeism(monthDays);
                    LogToTerminal($"[Step 3] Devamsızlık hesaplamaları tamamlandı");
                }
                catch (Exception ex)
                {
                    LogToTerminal($"[Step 3] Devamsızlık hesaplama hatası: {ex.Message}");
                    throw;
                }
            });
            LogToTerminal($"[Step 3] PDKS veri işleme tamamlandı");
        }

        private async Task ExportToERP()
        {
            Step3ProgressText.Text = "ERP şablonuna veri aktarılıyor...";
            LogToTerminal($"[Step 3] ERP aktarımı başlatılıyor");

            // ERP şablonuna veri yazma işlemi - UI işlemleri ana iş parçacığında yapılacak
            var selectedCompany = currentConfig.CompanyConfigs?.FirstOrDefault(c => c.CompanyCode == currentConfig.SelectedCompanyCode);
            string templateFilePath = selectedTemplatePath;

            if (string.IsNullOrWhiteSpace(templateFilePath) || !File.Exists(templateFilePath))
            {
                throw new InvalidOperationException("Geçerli bir ERP şablonu seçilmeden veri aktarılamaz.");
            }

            await Task.Run(() =>
            {
                WriteToERP(templateFilePath);
            });
            LogToTerminal($"[Step 3] ERP aktarımı tamamlandı");
        }

        private void WriteToERP(string templatePath)
        {
            LogToTerminal($"[Step 3] ERP dosyasına yazma işlemi başlatılıyor: {templatePath}");
            try
            {
                // Python ile ERP şablonuna veri yaz
                var matchedRecords = pdksDataService.GetAllRecords().Where(r => r.IsMatched).ToList();
                LogToTerminal($"[Step 3] {matchedRecords.Count} eşleşen kayıt ERP'ye aktarılacak");

                if (matchedRecords.Any())
                {
                    // Seçili firmayı bul
                    var selectedCompany = currentConfig.CompanyConfigs?.FirstOrDefault(c => c.CompanyCode == currentConfig.SelectedCompanyCode);
                    if (selectedCompany == null)
                    {
                        ShowERPResultModal(false, "", 0, $"Hata: Seçili firma ({currentConfig.SelectedCompanyCode}) bulunamadı!");
                        return;
                    }

                    // Personel bazında yekün hesaplamalar yap (firma bazlı)
                    var personnelSummaries = pdksDataService.CalculatePersonnelSummaries(selectedCompany);

                    // Çıktı klasörü oluştur
                    string outputDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ERP_Cikti");
                    if (!System.IO.Directory.Exists(outputDir))
                    {
                        System.IO.Directory.CreateDirectory(outputDir);
                    }

                    string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    string outputPath = System.IO.Path.Combine(outputDir, $"ERP_{selectedCompany.CompanyCode}_{timestamp}.xlsx");

                    // Python script ile personel yekün verilerini Excel'e yaz
                    var pythonScript = GenerateERPPythonScript(templatePath, personnelSummaries, outputPath, selectedCompany);

                    // Python'u bul
                    string? pythonCmd = FindPythonExecutable();
                    if (pythonCmd == null)
                    {
                        string errorMsg = "Python bulunamadı. Lütfen Python'u yükleyin ve PATH'e ekleyin, veya Windows Python Launcher (py) kullanın.";
                        LogToTerminal($"[Step 3] {errorMsg}");
                        Dispatcher.Invoke(() =>
                        {
                            ShowERPResultModal(false, "", 0, errorMsg, null, errorMsg);
                        });
                        return;
                    }

                    // Python scripti geçici bir dosyaya yaz
                    string tempScriptPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"erp_export_{timestamp}.py");
                    System.IO.File.WriteAllText(tempScriptPath, pythonScript);

                    var psi = new ProcessStartInfo
                    {
                        FileName = pythonCmd,
                        Arguments = $"{tempScriptPath}", // Geçici dosyayı çalıştır
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    var process = Process.Start(psi);
                    if (process == null)
                    {
                        throw new Exception("Python işlemi başlatılamadı");
                    }

                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();
                    process.WaitForExit();

                    // Geçici script dosyasını sil
                    System.IO.File.Delete(tempScriptPath);

                    if (process.ExitCode == 0)
                    {
                        LogToTerminal($"[Step 3] ERP dosyası başarıyla oluşturuldu: {outputPath}");
                        LogToTerminal($"[Step 3] Python output: {output}");

                        // Mevcut ayın işlenmiş PDKS verilerini StoredPDKSRecords olarak kaydet (bir sonraki ay için)
                        int year = selectedCompany.PayrollYear > 0 ? selectedCompany.PayrollYear : DateTime.Now.Year;
                        int month = selectedCompany.PayrollMonth > 0 ? selectedCompany.PayrollMonth : DateTime.Now.Month;
                        
                        var carryOverService = new CarryOverStateService();
                        var allProcessedRecords = pdksDataService.GetAllRecords().Where(r => r.IsMatched).ToList();
                        
                        if (allProcessedRecords.Any())
                        {
                            // PDKSDataModel'leri StoredPDKSRecord'a dönüştür
                            var storedRecordsToSave = allProcessedRecords
                                .Where(r => r.Date.Year == year && r.Date.Month == month)
                                .Select(r => new StoredPDKSRecord
                                {
                                    PersonnelCode = r.PersonnelCode,
                                    PersonnelName = r.PersonnelName,
                                    Date = r.Date,
                                    CheckInTime = r.CheckInTime,
                                    CheckOutTime = r.CheckOutTime,
                                    ShiftType = r.ShiftType ?? "Bilinmiyor"
                                })
                                .ToList();
                            
                            if (storedRecordsToSave.Any())
                            {
                                carryOverService.SaveStoredPDKSRecords(selectedCompany.CompanyCode, year, month, storedRecordsToSave);
                                LogToTerminal($"[Step 3] {storedRecordsToSave.Count} günlük kayıt StoredPDKSRecords olarak kaydedildi (bir sonraki ay için)");
                            }
                        }

                        var overtimeDetails = BuildOvertimeDetailEntries(selectedCompany);
                        var conditionalEarningsDetails = BuildConditionalEarningsDetails(personnelSummaries, selectedCompany);
                        var carryOverVacationLogs = PDKSDataService.GetAndClearCarryOverVacationLogs();
                        var summaryMessage = BuildErpResultSummary(outputPath, personnelSummaries.Count, overtimeDetails, conditionalEarningsDetails, carryOverVacationLogs);

                        // Başarı modal'ı göster - Ana UI iş parçacığında
                        Dispatcher.Invoke(() =>
                        {
                            ShowERPResultModal(true, outputPath, personnelSummaries.Count, summaryMessage, overtimeDetails, output, conditionalEarningsDetails);
                            
                            // CarryOver listesini refresh et
                            RefreshCarryOverList(selectedCompany);
                        });
                    }
                    else
                    {
                        LogToTerminal($"[Step 3] ERP yazma hatası: {error}");
                        Dispatcher.Invoke(() =>
                        {
                            ShowERPResultModal(false, "", 0, $"Hata: {error}", null, error);
                        });
                    }
                }
                else
                {
                    LogToTerminal($"[Step 3] Uyarı: Eşleşen kayıt bulunamadı, ERP aktarımı yapılmadı");
                    Dispatcher.Invoke(() =>
                    {
                        ShowERPResultModal(false, "", 0, "Eşleşen kayıt bulunamadı");
                    });
                }
            }
            catch (Exception ex)
            {
                LogToTerminal($"[Step 3] ERP yazma hatası: {ex.Message}");
                Dispatcher.Invoke(() =>
                {
                    ShowERPResultModal(false, "", 0, $"Hata: {ex.Message}", null, ex.Message);
                });
            }
        }

        private string GenerateERPPythonScript(string templatePath, List<PersonnelSummary> personnelSummaries, string outputPath, CompanyConfig companyConfig)
        {
            var scriptBuilder = new StringBuilder();
            scriptBuilder.AppendLine("import pandas as pd");
            scriptBuilder.AppendLine("import openpyxl");
            scriptBuilder.AppendLine("from openpyxl.utils import get_column_letter");
            scriptBuilder.AppendLine("from openpyxl.styles import PatternFill");
            scriptBuilder.AppendLine();
            scriptBuilder.AppendLine($"wb = openpyxl.load_workbook(r'{templatePath}')");
            scriptBuilder.AppendLine("ws = wb.active");
            scriptBuilder.AppendLine();
            scriptBuilder.AppendLine("# Veri yazılan satırları renklendirmek için soft mavi arka plan");
            scriptBuilder.AppendLine("highlight_fill = PatternFill(start_color='E3F2FD', end_color='E3F2FD', fill_type='solid')");
            scriptBuilder.AppendLine("highlighted_rows = []  # Renklendirilen satırları takip et");
            scriptBuilder.AppendLine();

            // ŞABLONDAKİ MEVCUT PERSONELLERİ OKU VE VERİLERİNİ GÜNCELLE!
            scriptBuilder.AppendLine("# Şablondaki mevcut personelleri oku ve hesaplanan verileri karşılarına yaz");
            scriptBuilder.AppendLine("existing_personnel = {}");
            scriptBuilder.AppendLine("for row_idx in range(2, ws.max_row + 1):");
            scriptBuilder.AppendLine("    name_cell = ws[f'B{row_idx}'].value");
            scriptBuilder.AppendLine("    if name_cell is not None and str(name_cell).strip():");
            scriptBuilder.AppendLine("        existing_personnel[str(name_cell).strip()] = row_idx");
            scriptBuilder.AppendLine("print(f'Şablonda {len(existing_personnel)} mevcut personel bulundu')");
            scriptBuilder.AppendLine();

            // Personel verilerini MEVCUT PERSONELLERİN KARŞISINA yaz!
            scriptBuilder.AppendLine("# Hesaplanan verileri MEVCUT personellerin karşısına yaz - isim eşleştirme ile!");
            scriptBuilder.AppendLine("updated_count = 0");

            foreach (var summary in personnelSummaries)
            {
                // Güvenli string escaping
                string name = summary.PersonnelName?.Replace("'", "\\'") ?? "";
                string tcNo = summary.TCNo?.Replace("'", "\\'") ?? "";
                string personnelCode = summary.PersonnelCode?.Replace("'", "\\'") ?? "";
                string absentColumnLetter = GetAbsentColumnLetterForSummary(summary, companyConfig);

                // Bu personelin şablonda var mı kontrol et (isim eşleştirme)
                scriptBuilder.AppendLine($"if '{name}' in existing_personnel:");
                scriptBuilder.AppendLine($"    target_row = existing_personnel['{name}']");
                scriptBuilder.AppendLine($"    print(f'[EXCEL YAZMA] {name} icin veriler yaziliyor - Satir: {{target_row}}')");
                scriptBuilder.AppendLine($"    print(f'[EXCEL YAZMA] {name} - Eksik Gun: {summary.TotalAbsentDays}, Eksik Saat: {summary.TotalAbsentHours:F2}')");
                scriptBuilder.AppendLine($"    print(f'[EXCEL YAZMA] {name} - Eksik Gün kolonu: {absentColumnLetter}')");
                scriptBuilder.AppendLine($"    # Hesaplanan verileri mevcut personelin karşısına yaz");
                scriptBuilder.AppendLine($"    ws[f'{absentColumnLetter}{{target_row}}'] = {summary.TotalAbsentDays}  # Eksik Gün/Devamsızlık (gün)");
                scriptBuilder.AppendLine($"    ws[f'N{{target_row}}'] = {summary.TotalAbsentHours.ToString("F2", CultureInfo.InvariantCulture)} # Eksik Saat (saat) - N sütunu");
                scriptBuilder.AppendLine($"    ws[f'Y{{target_row}}'] = {summary.FMColumnTotals.GetValueOrDefault("B01 %50 Fazla Mesai").ToString("F2", CultureInfo.InvariantCulture)} # FM %50 - Y sütunu");
                scriptBuilder.AppendLine($"    ws[f'AB{{target_row}}'] = {summary.FMColumnTotals.GetValueOrDefault("B04 Fazla Mesai Normal").ToString("F2", CultureInfo.InvariantCulture)} # FM Normal - AB sütunu");

                var overtimeAssignments = GetOvertimeColumnAssignments(summary).ToList();
                if (overtimeAssignments.Any())
                {
                    foreach (var assignment in overtimeAssignments)
                    {
                        scriptBuilder.AppendLine($"    print(f'[EXCEL YAZMA] {name} - {assignment.columnName}: {assignment.hours.ToString("F2", CultureInfo.InvariantCulture)} saat -> {assignment.columnLetter} sutunu')");
                        scriptBuilder.AppendLine($"    ws[f'{assignment.columnLetter}{{target_row}}'] = {assignment.hours.ToString("F2", CultureInfo.InvariantCulture)}  # {assignment.columnName}");
                    }
                }
                else
                {
                    // Eğer özel fazla mesai ataması yoksa varsayılan kolonlara yaz
                    scriptBuilder.AppendLine($"    print(f'[EXCEL YAZMA] {name} - B04 Fazla Mesai Normal: {summary.TotalFMNormalHours.ToString("F2", CultureInfo.InvariantCulture)} saat -> AB sutunu')");
                    scriptBuilder.AppendLine($"    ws[f'AB{{target_row}}'] = {summary.TotalFMNormalHours.ToString("F2", CultureInfo.InvariantCulture)}  # Aylık FM Normal (saat) - AB sütunu");
                    scriptBuilder.AppendLine($"    print(f'[EXCEL YAZMA] {name} - B01 %50 Fazla Mesai: {summary.TotalFM50PercentHours.ToString("F2", CultureInfo.InvariantCulture)} saat -> Y sutunu')");
                    scriptBuilder.AppendLine($"    ws[f'Y{{target_row}}'] = {summary.TotalFM50PercentHours.ToString("F2", CultureInfo.InvariantCulture)}  # Aylık FM %50 (saat) - Y sütunu");
                }

                // Koşullu kazanç kurallarını uygula
                // NOT: Bu fonksiyon sadece koşulu sağlayan personellere kural uygular
                // - "Koşulsuz" kuralı: Herkese uygulanır (PDKS verilerine bakmaz)
                // - Diğer kurallar: Sadece koşulu sağlayan personellere uygulanır
                var conditionalEarnings = ApplyConditionalEarningsRules(summary, companyConfig);
                if (conditionalEarnings.Any())
                {
                    scriptBuilder.AppendLine($"    # Koşullu kazançlar (sadece koşulu sağlayan personellere uygulanır)");
                    foreach (var earning in conditionalEarnings)
                    {
                        string conditionInfo = earning.ConditionType == "Koşulsuz" 
                            ? "Koşulsuz (herkese)" 
                            : $"Koşul: {earning.ConditionType}";
                        scriptBuilder.AppendLine($"    print(f'[KOŞULLU KAZANÇ] {name} - {earning.ColumnName}: {earning.Value.ToString("F2", CultureInfo.InvariantCulture)} -> {earning.ColumnLetter} sutunu ({conditionInfo})')");
                        scriptBuilder.AppendLine($"    ws[f'{earning.ColumnLetter}{{target_row}}'] = {earning.Value.ToString("F2", CultureInfo.InvariantCulture)}  # {earning.ColumnName} ({conditionInfo})");
                    }
                }

                scriptBuilder.AppendLine($"    # Veri yazılan satırı soft mavi renkle vurgula");
                scriptBuilder.AppendLine($"    if target_row not in highlighted_rows:");
                scriptBuilder.AppendLine($"        highlighted_rows.append(target_row)");
                scriptBuilder.AppendLine($"        # Tüm satırı renklendir (max column'a kadar)");
                scriptBuilder.AppendLine($"        for col_idx in range(1, ws.max_column + 1):");
                scriptBuilder.AppendLine($"            cell = ws.cell(row=target_row, column=col_idx)");
                scriptBuilder.AppendLine($"            cell.fill = highlight_fill");
                scriptBuilder.AppendLine($"    print(f'{name} için veriler güncellendi - satır {{target_row}} (renklendirildi)')");
                scriptBuilder.AppendLine("    updated_count += 1");
                scriptBuilder.AppendLine("else:");
                scriptBuilder.AppendLine($"    print(f'UYARI: {name} şablonda bulunamadı - veri yazılmadı')");

                scriptBuilder.AppendLine();
            }

            scriptBuilder.AppendLine($"wb.save(r'{outputPath}')");
            scriptBuilder.AppendLine($"print(f'ERP şablonu başarıyla güncellendi: {personnelSummaries.Count} personel işlendi, {{updated_count}} kayıt güncellendi')");

            return scriptBuilder.ToString();
        }

        private string GetAbsentColumnLetterForSummary(PersonnelSummary summary, CompanyConfig companyConfig)
        {
            const string fallbackLetter = "K";

            if (summary == null || companyConfig?.ShiftRuleConfigs == null || companyConfig.ShiftRuleConfigs.Count == 0)
            {
                return fallbackLetter;
            }

            ShiftRuleConfig? targetConfig = null;
            if (!string.IsNullOrWhiteSpace(summary.PrimaryShiftGroupName))
            {
                targetConfig = companyConfig.ShiftRuleConfigs
                    .FirstOrDefault(cfg => string.Equals(cfg.GroupName, summary.PrimaryShiftGroupName, StringComparison.OrdinalIgnoreCase));
            }

            targetConfig ??= companyConfig.ShiftRuleConfigs.FirstOrDefault();

            if (targetConfig == null || string.IsNullOrWhiteSpace(targetConfig.AbsentDaysColumnLetter))
            {
                return fallbackLetter;
            }

            return targetConfig.AbsentDaysColumnLetter.Trim().ToUpperInvariant();
        }

        /// <summary>
        /// Koşullu kazanç kurallarını personel özetine göre uygular ve uygulanabilir kuralları döndürür
        /// </summary>
        private List<ConditionalEarningResult> ApplyConditionalEarningsRules(PersonnelSummary summary, CompanyConfig companyConfig)
        {
            var results = new List<ConditionalEarningResult>();

            if (summary == null || companyConfig?.ConditionalEarningsSettings?.Rules == null)
            {
                return results;
            }

            var payrollYear = companyConfig.PayrollYear > 0 ? companyConfig.PayrollYear : DateTime.Now.Year;
            var payrollMonth = companyConfig.PayrollMonth > 0 ? companyConfig.PayrollMonth : DateTime.Now.Month;
            var payrollStartDate = new DateTime(payrollYear, payrollMonth, 1);
            var payrollEndDate = payrollStartDate.AddMonths(1).AddDays(-1);

            foreach (var rule in companyConfig.ConditionalEarningsSettings.Rules)
            {
                // Kural aktif değilse atla
                if (!rule.IsEnabled)
                {
                    continue;
                }

                // Tarih kontrolü - Bordro ayı ile kesişmeli
                if (rule.StartDate.HasValue || rule.EndDate.HasValue)
                {
                    bool intersects = true;
                    if (rule.StartDate.HasValue && rule.StartDate.Value > payrollEndDate)
                    {
                        intersects = false;
                    }
                    if (rule.EndDate.HasValue && rule.EndDate.Value < payrollStartDate)
                    {
                        intersects = false;
                    }

                    if (!intersects)
                    {
                        continue; // Bu bordro ayı için geçerli değil
                    }
                }

                // Koşul kontrolü
                bool conditionMet = false;
                double conditionValue = 0;
                bool isUnconditional = rule.ConditionType == "Koşulsuz";

                if (isUnconditional)
                {
                    // Koşulsuz: Herkese uygulanır, PDKS verilerine bakmaz
                    conditionMet = true;
                    conditionValue = 0; // Koşulsuz durumda conditionValue kullanılmaz
                }
                else
                {
                    // Koşullu: PDKS verilerine göre kontrol et
                    switch (rule.ConditionType)
                    {
                        case "Devamsızlık Günü":
                            // TotalAbsentDays = Eksik gün (effectiveDays - (TotalWorkDays + TotalVacationDays))
                            conditionValue = summary.TotalAbsentDays;
                            break;

                        case "Eksik Gün":
                            // TotalAbsentDays = Eksik gün (effectiveDays - (TotalWorkDays + TotalVacationDays))
                            conditionValue = summary.TotalAbsentDays;
                            break;

                        case "Eksik Saat":
                            // TotalAbsentHours = Çalışılan günlerdeki eksik saatler
                            conditionValue = summary.TotalAbsentHours;
                            break;

                        case "Fazla Mesai Saati":
                            // Toplam fazla mesai (normal + %50)
                            conditionValue = summary.TotalFMNormalHours + summary.TotalFM50PercentHours;
                            break;

                        case "Tatil Günü":
                            // TotalVacationDays = Kullanılan tatil günleri
                            conditionValue = summary.TotalVacationDays;
                            break;

                        default:
                            // Bilinmeyen koşul tipi - atla
                            conditionMet = false;
                            break;
                    }

                    // Koşul değeri kaynağını belirle (backward compatibility için varsayılan "Sabit")
                    var conditionValueSource = rule.ConditionValueSource ?? "Sabit";
                    double comparisonValue;

                    if (conditionValueSource == "Sabit")
                    {
                        // Sabit değer kullan
                        comparisonValue = rule.ConditionValue;
                    }
                    else
                    {
                        // Dinamik değer kullan - koşul değeri kaynağına göre hesapla
                        comparisonValue = conditionValueSource switch
                        {
                            "Devamsızlık Günü Kadar" => summary.TotalAbsentDays,
                            "Eksik Gün Kadar" => summary.TotalAbsentDays,
                            "Eksik Saat Kadar" => summary.TotalAbsentHours,
                            "Fazla Mesai Saati Kadar" => summary.TotalFMNormalHours + summary.TotalFM50PercentHours,
                            "Tatil Günü Kadar" => summary.TotalVacationDays,
                            "Çalışılan Gün Kadar" => summary.TotalWorkDays,
                            _ => rule.ConditionValue // Fallback: sabit değer
                        };
                    }

                    // Koşul değerlendirmesi yap
                    conditionMet = EvaluateCondition(conditionValue, rule.ConditionOperator, comparisonValue);
                }

                if (conditionMet)
                {
                    // Kazanç değerini hesapla
                    double earningsValue = rule.EarningsValue;
                    
                    if (isUnconditional)
                    {
                        // Koşulsuz durumda sadece sabit değer kullanılır
                        earningsValue = rule.EarningsValue;
                    }
                    else
                    {
                        // Koşullu durumda değer tipine göre hesapla
                        if (rule.ValueType == "Fazla Mesai Saati x Tutar")
                        {
                            // Fazla Mesai (Normal + %50) x Tutar
                            earningsValue = (summary.TotalFMNormalHours + summary.TotalFM50PercentHours) * rule.EarningsValue;
                        }
                        else if (rule.ValueType == "Eksik Saat x Tutar")
                        {
                            // Eksik Saat x Tutar
                            earningsValue = summary.TotalAbsentHours * rule.EarningsValue;
                        }
                        else if (rule.ValueType == "Eksik Gün x Tutar")
                        {
                            // Eksik Gün x Tutar
                            earningsValue = summary.TotalAbsentDays * rule.EarningsValue;
                        }
                        else if (rule.ValueType == "Devamsızlık Günü x Tutar")
                        {
                            // Devamsızlık Günü x Tutar (Eksik Gün ile aynı)
                            earningsValue = summary.TotalAbsentDays * rule.EarningsValue;
                        }
                        // "Sabit" ise zaten rule.EarningsValue kullanılır
                    }

                    results.Add(new ConditionalEarningResult
                    {
                        ColumnName = rule.TargetColumnName,
                        ColumnLetter = rule.TargetColumnLetter,
                        Value = earningsValue,
                        RuleDescription = rule.Description,
                        ConditionType = rule.ConditionType
                    });
                }
            }

            return results;
        }

        /// <summary>
        /// Koşul değerlendirmesi yapar
        /// </summary>
        private bool EvaluateCondition(double actualValue, string operatorStr, double conditionValue)
        {
            return operatorStr switch
            {
                ">" => actualValue > conditionValue,
                ">=" => actualValue >= conditionValue,
                "<" => actualValue < conditionValue,
                "<=" => actualValue <= conditionValue,
                "==" => Math.Abs(actualValue - conditionValue) < 0.001, // Double karşılaştırma için tolerans
                "!=" => Math.Abs(actualValue - conditionValue) >= 0.001,
                _ => false
            };
        }

        /// <summary>
        /// Koşullu kazanç sonucu
        /// </summary>
        private class ConditionalEarningResult
        {
            public string ColumnName { get; set; } = string.Empty;
            public string ColumnLetter { get; set; } = string.Empty;
            public double Value { get; set; }
            public string RuleDescription { get; set; } = string.Empty;
            public string ConditionType { get; set; } = string.Empty;
        }

        private void ShowERPResultModal(bool success, string filePath, int recordCount, string message, List<OvertimeDetailEntry>? overtimeDetails = null, string? rawOutput = null, List<ConditionalEarningDetail>? conditionalEarningsDetails = null)
        {
            try
            {
                lastErpSuccess = success;
                lastErpSummaryMessage = message;
                lastErpRecordCount = recordCount;
                lastErpOutputPath = success && !string.IsNullOrWhiteSpace(filePath) ? filePath : null;
                lastErpRawOutput = rawOutput;
                lastOvertimeDetails = overtimeDetails != null
                    ? new List<OvertimeDetailEntry>(overtimeDetails)
                    : new List<OvertimeDetailEntry>();
                lastConditionalEarningsDetails = conditionalEarningsDetails != null
                    ? new List<ConditionalEarningDetail>(conditionalEarningsDetails)
                    : new List<ConditionalEarningDetail>();

                var modal = new ERPResultModal(success, filePath, recordCount, message, overtimeDetails, rawOutput, conditionalEarningsDetails);
                modal.Owner = this;
                modal.ShowDialog();
            }
            catch (Exception ex)
            {
                LogToTerminal($"[ERP Modal Error] {ex.Message}");
                MessageBox.Show($"ERP sonuç modal'ı gösterilemedi: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private readonly Dictionary<string, string> overtimeColumnMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "B04 Fazla Mesai Normal", "AB" },
            { "B01 %50 Fazla Mesai", "Y" }
        };

        private void RegisterOvertimeColumnsFromConfig(PDKSConfig config)
        {
            if (config?.CompanyConfigs == null)
            {
                return;
            }

            overtimeColumnMap["B04 Fazla Mesai Normal"] = "AB";
            overtimeColumnMap["B01 %50 Fazla Mesai"] = "Y";

            foreach (var company in config.CompanyConfigs)
            {
                if (company?.SpecialOvertimeSettings == null)
                {
                    continue;
                }

                RegisterOvertimeColumn(company.SpecialOvertimeSettings.EarnedRestDayColumnName,
                                       company.SpecialOvertimeSettings.EarnedRestDayColumnLetter);
                RegisterOvertimeColumn(company.SpecialOvertimeSettings.HolidayWorkColumnName,
                                       company.SpecialOvertimeSettings.HolidayWorkColumnLetter);
            }
        }

        private void RegisterOvertimeColumn(string columnName, string columnLetter)
        {
            if (string.IsNullOrWhiteSpace(columnName) || string.IsNullOrWhiteSpace(columnLetter))
            {
                return;
            }

            overtimeColumnMap[columnName] = columnLetter.Trim().ToUpperInvariant();
        }

        private IEnumerable<(string columnLetter, double hours, string columnName)> GetOvertimeColumnAssignments(PersonnelSummary summary)
        {
            if (summary?.FMColumnTotals == null || summary.FMColumnTotals.Count == 0)
            {
                yield break;
            }

            foreach (var kvp in summary.FMColumnTotals)
            {
                if (!overtimeColumnMap.TryGetValue(kvp.Key, out var columnLetter))
                {
                    continue;
                }

                yield return (columnLetter, kvp.Value, kvp.Key);
            }
        }

        private void DisplayProcessingResults()
        {
            LogToTerminal("[Step 3] İşleme sonuçları gösteriliyor");

            int totalRecords = pdksDataService.GetTotalCount();
            int matchedRecords = pdksDataService.GetMatchedCount(); // Tekil personel sayısı
            // İşlenen kayıt sayısı = Excel'e gerçekten yazılan personel sayısı (ERP işlemi yapıldıysa)
            // Eğer henüz ERP işlemi yapılmadıysa, eşleşen tekil personel sayısını göster
            int processedRecords = lastErpRecordCount > 0 
                ? lastErpRecordCount 
                : pdksDataService.GetAllRecords()
                                 .Where(r => r.IsMatched)
                                 .GroupBy(r => r.PersonnelCode)
                                 .Count();

            if (TotalRecordsValueText != null)
                TotalRecordsValueText.Text = totalRecords.ToString("N0", cultureInfo);
            if (MatchedRecordsValueText != null)
                MatchedRecordsValueText.Text = matchedRecords.ToString("N0", cultureInfo);
            if (ProcessedRecordsValueText != null)
                ProcessedRecordsValueText.Text = processedRecords.ToString("N0", cultureInfo);

            if (ProcessingResultsSubtitle != null)
            {
                var firstLine = ExtractFirstLine(lastErpSummaryMessage);
                if (lastErpSuccess)
                {
                    ProcessingResultsSubtitle.Text = !string.IsNullOrWhiteSpace(firstLine)
                        ? firstLine
                        : "PDKS verileri başarıyla işlendi ve ERP şablonu güncellendi.";
                }
                else
                {
                    ProcessingResultsSubtitle.Text = !string.IsNullOrWhiteSpace(firstLine)
                        ? firstLine
                        : "Veri işleme tamamlandı. ERP aktarımı sırasında bir hata oluştu.";
                }
            }

            if (ProcessingResultsStatusPanel != null)
            {
                ProcessingResultsStatusPanel.Children.Clear();

                AddStatusRow("", $"Toplam {totalRecords:N0} PDKS kaydı analiz edildi.", "#2563EB");
                AddStatusRow("", $"{matchedRecords:N0} personel eşleştirildi.", matchedRecords > 0 ? "#0EA5E9" : "#F97316");
                AddStatusRow("", $"{processedRecords:N0} kayıt ERP aktarımı için hazırlandı.", processedRecords > 0 ? "#22C55E" : "#F97316");

                if (lastErpSuccess)
                {
                    if (!string.IsNullOrWhiteSpace(lastErpSummaryMessage))
                    {
                        var lines = lastErpSummaryMessage
                            .Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(line => line.Trim())
                            .ToList();

                        foreach (var line in lines)
                        {
                            if (string.IsNullOrWhiteSpace(line))
                                continue;

                            AddStatusRow("", line.StartsWith("-") ? line.TrimStart('-', ' ', '•') : line, "#16A34A");
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(lastErpOutputPath) && File.Exists(lastErpOutputPath))
                    {
                        string fileName = System.IO.Path.GetFileName(lastErpOutputPath);
                        AddStatusRow("", $"{fileName} dosyasını aç", "#2563EB", () => SafeOpenFile(lastErpOutputPath!));
                        AddStatusRow("", "Çıktı klasörünü görüntüle", "#CA8A04", () => SafeOpenFolder(lastErpOutputPath!));
                    }
                }
                else
                {
                    string errorMessage = !string.IsNullOrWhiteSpace(lastErpSummaryMessage)
                        ? lastErpSummaryMessage.Trim()
                        : "ERP aktarımı sırasında bir hata oluştu. Detaylar için logları kontrol edin.";
                    AddStatusRow("", errorMessage, "#DC2626");
                }

                if (lastOvertimeDetails.Count > 0 || !string.IsNullOrWhiteSpace(lastErpRawOutput))
                {
                    AddStatusRow("", "Detaylı raporu görüntüle", "#334155", () =>
                    {
                        var detailModal = new ERPResultDetailsModal(lastOvertimeDetails, lastErpRawOutput)
                        {
                            Owner = this
                        };
                        detailModal.ShowDialog();
                    });
                }
            }

            ProcessingResultsBorder.Visibility = Visibility.Visible;
            LogToTerminal("[Step 3] İşleme sonuçları gösterildi");
        }

        private static string ExtractFirstLine(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            return text.Replace("\r", string.Empty)
                       .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                       .FirstOrDefault()?.Trim() ?? string.Empty;
        }

        private void AddStatusRow(string iconGlyph, string text, string hexColor, Action? onClick = null)
        {
            if (ProcessingResultsStatusPanel == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            var color = (Color)ColorConverter.ConvertFromString(hexColor);
            var accentBrush = new SolidColorBrush(color);
            var backgroundBrush = new SolidColorBrush(Color.FromArgb(28, color.R, color.G, color.B));
            var iconBackgroundBrush = new SolidColorBrush(Color.FromArgb(48, color.R, color.G, color.B));

            var container = new Border
            {
                Background = backgroundBrush,
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 4, 0, 4)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var iconHolder = new Border
            {
                Width = 30,
                Height = 30,
                CornerRadius = new CornerRadius(15),
                Background = iconBackgroundBrush,
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = new TextBlock
                {
                    Text = iconGlyph,
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    FontSize = 16,
                    Foreground = accentBrush,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };

                var textBlock = new TextBlock
                {
                FontFamily = new FontFamily("pack://application:,,,/Fonts/Poppins-Regular.ttf#Poppins"),
                    FontSize = 14,
                Foreground = accentBrush,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(12, 0, 0, 0)
            };

            if (onClick != null)
            {
                var hyperlink = new Hyperlink(new Run(text))
                {
                    Foreground = accentBrush,
                    Cursor = Cursors.Hand
                };
                hyperlink.Click += (s, e) => onClick();
                textBlock.Inlines.Add(hyperlink);
            }
            else
            {
                textBlock.Text = text;
            }

            Grid.SetColumn(iconHolder, 0);
            Grid.SetColumn(textBlock, 1);

            grid.Children.Add(iconHolder);
            grid.Children.Add(textBlock);

            container.Child = grid;

            ProcessingResultsStatusPanel.Children.Add(container);
        }

        private void SafeOpenFile(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    MessageBox.Show($"Dosya bulunamadı: {path}", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Dosya açılamadı: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SafeOpenFolder(string path)
        {
            try
            {
                string directory = System.IO.Path.GetDirectoryName(path) ?? path;
                if (!Directory.Exists(directory))
                {
                    MessageBox.Show($"Klasör bulunamadı: {directory}", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{path}\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Klasör açılamadı: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnNext_Click(object sender, RoutedEventArgs e)
        {
            LogToTerminal($"[Navigation] İleri butonuna tıklandı - Mevcut Step: {currentStep}");

            // Adım1'de firma seçimi kontrolü
            if (currentStep == 1)
            {
                if (CompanyListBox.SelectedItem is CompanyConfig selectedCompany)
                {
                    // Vardiya kuralı kontrolü
                    if (selectedCompany.ShiftRuleConfigs == null || selectedCompany.ShiftRuleConfigs.Count == 0)
                    {
                        var result = MessageBox.Show(
                            $"{selectedCompany.CompanyName} firması için hiç vardiya kuralı tanımlanmamış.\n\n" +
                            "Devam etmek için ayarlar bölümünden vardiya kuralları eklemeniz gerekir.\n\n" +
                            "Şimdi ayarlar bölümüne gitmek ister misiniz?",
                            "Vardiya Kuralı Yok",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Warning);

                        if (result == MessageBoxResult.Yes)
                        {
                            // Ayarlar modal'ını aç
                            ShowSettingsModal();

                            // Konfigürasyonu yeniden yükle
                            currentConfig = configService.LoadConfig();

                            // Firma listesini yeniden doldur
                            PopulateCompanyList();

                            // Eğer hala vardiya kuralı yoksa devam etme
                            var updatedCompany = currentConfig.CompanyConfigs?.FirstOrDefault(c => c.CompanyCode == selectedCompany.CompanyCode);
                            if (updatedCompany?.ShiftRuleConfigs == null || updatedCompany.ShiftRuleConfigs.Count == 0)
                            {
                                MessageBox.Show("Vardiya kuralı eklenmeden devam edilemez.", "Vardiya Kuralı Gerekli", MessageBoxButton.OK, MessageBoxImage.Warning);
                                return;
                            }
                        }
                        else
                        {
                            MessageBox.Show("Vardiya kuralı olmadan PDKS işlemi gerçekleştirilemez.", "İşlem İptal", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }
                    }

                    currentConfig.SelectedCompanyCode = selectedCompany.CompanyCode;
                    LogToTerminal($"[Firma Seçimi] {selectedCompany.CompanyName} ({selectedCompany.CompanyCode}) seçildi");

                    if (!EnsureTemplateSelected())
                    {
                        return;
                    }

                    if (string.IsNullOrWhiteSpace(selectedTemplatePath) || !File.Exists(selectedTemplatePath))
                    {
                        MessageBox.Show("Lütfen seçili firma için geçerli bir Vio şablonu belirleyin.", "Şablon Seçilmedi", MessageBoxButton.OK, MessageBoxImage.Warning);
                        LogToTerminal($"[Step 1] Şablon kontrolü başarısız - geçerli şablon bulunamadı");
                        return;
                    }
                }
                else
                {
                    MessageBox.Show("Lütfen bir firma seçin.", "Firma Seçilmedi", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            if (currentStep < totalSteps)
            {
                int oldStep = currentStep;
                currentStep++;
                UpdateStepDisplay();
                LogToTerminal($"[Navigation] Step {oldStep}'den Step {currentStep}'e geçildi");
            }
            else if (currentStep == totalSteps)
            {
                LogToTerminal($"[Navigation] Son adıma ulaşıldı, veri işleme başlatılıyor");
                // Son adım - veri işlemesini başlat
                btnProcessData_Click(sender, e);
            }
        }

        private void btnPrevious_Click(object sender, RoutedEventArgs e)
        {
            LogToTerminal($"[Navigation] Geri butonuna tıklandı - Mevcut Step: {currentStep}");
            if (currentStep > 1)
            {
                int oldStep = currentStep;
                currentStep--;
                UpdateStepDisplay();
                LogToTerminal($"[Navigation] Step {oldStep}'den Step {currentStep}'e geri dönüldü");
            }
            else
            {
                LogToTerminal($"[Navigation] İlk adımdayken geri gidilemez");
            }
        }

        private void btnStep1_Click(object sender, RoutedEventArgs e)
        {
            // Step 1'e geri dönülür, herhangi bir şart gerekmez
            if (currentStep != 1)
            {
                LogToTerminal($"[Navigation] Step 1'e tıklandı - Mevcut Step: {currentStep}");
                currentStep = 1;
                UpdateStepDisplay();
                LogToTerminal($"[Navigation] Step 1'e geçildi");
            }
        }

        private void btnStep2_Click(object sender, RoutedEventArgs e)
        {
            LogToTerminal($"[Navigation] Step 2'ye tıklandı - Mevcut Step: {currentStep}");

            // Eğer zaten Step 2'deysek, bir şey yapma
            if (currentStep == 2)
            {
                return;
            }

            // Step 2'ye geçmek için Step 1'in şartlarını kontrol et
            if (currentStep < 2)
            {
                // Step 1'den Step 2'ye geçiş için kontrol yap
                if (CompanyListBox.SelectedItem is CompanyConfig selectedCompany)
                {
                    // Vardiya kuralı kontrolü
                    if (selectedCompany.ShiftRuleConfigs == null || selectedCompany.ShiftRuleConfigs.Count == 0)
                    {
                        MessageBox.Show(
                            $"{selectedCompany.CompanyName} firması için hiç vardiya kuralı tanımlanmamış.\n\n" +
                            "Devam etmek için ayarlar bölümünden vardiya kuralları eklemeniz gerekir.",
                            "Vardiya Kuralı Yok",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        LogToTerminal($"[Navigation] Step 2'ye geçiş reddedildi - vardiya kuralı yok");
                        return;
                    }

                    currentConfig.SelectedCompanyCode = selectedCompany.CompanyCode;

                    if (!EnsureTemplateSelected())
                    {
                        return;
                    }

                    if (string.IsNullOrWhiteSpace(selectedTemplatePath) || !File.Exists(selectedTemplatePath))
                    {
                        MessageBox.Show("Lütfen seçili firma için geçerli bir Vio şablonu belirleyin.", "Şablon Seçilmedi", MessageBoxButton.OK, MessageBoxImage.Warning);
                        LogToTerminal($"[Navigation] Step 2'ye geçiş reddedildi - şablon seçilmedi");
                        return;
                    }
                }
                else
                {
                    MessageBox.Show("Lütfen bir firma seçin.", "Firma Seçilmedi", MessageBoxButton.OK, MessageBoxImage.Warning);
                    LogToTerminal($"[Navigation] Step 2'ye geçiş reddedildi - firma seçilmedi");
                    return;
                }
            }

            // Step 2'ye geç
            currentStep = 2;
            UpdateStepDisplay();
            LogToTerminal($"[Navigation] Step 2'ye geçildi");
        }

        private void btnStep3_Click(object sender, RoutedEventArgs e)
        {
            LogToTerminal($"[Navigation] Step 3'e tıklandı - Mevcut Step: {currentStep}");

            // Eğer zaten Step 3'teysek, bir şey yapma
            if (currentStep == 3)
            {
                return;
            }

            // Step 3'e geçmek için önceki adımların şartlarını kontrol et
            if (currentStep < 3)
            {
                // Önce Step 2'nin şartlarını kontrol et
                if (currentStep == 1)
                {
                    // Step 1'den direkt Step 3'e geçiş yapılamaz, önce Step 2'ye git
                    MessageBox.Show("Step 3'e geçmek için önce Step 2'yi tamamlamalısınız.", "Adım Atlandı", MessageBoxButton.OK, MessageBoxImage.Information);
                    LogToTerminal($"[Navigation] Step 3'e geçiş reddedildi - Step 2 atlandı");
                    return;
                }

                // Step 2'nin şartlarını kontrol et
                if (string.IsNullOrWhiteSpace(selectedPDKSPath) || !File.Exists(selectedPDKSPath))
                {
                    MessageBox.Show("Lütfen bir PDKS dosyası seçin.", "PDKS Dosyası Seçilmedi", MessageBoxButton.OK, MessageBoxImage.Warning);
                    LogToTerminal($"[Navigation] Step 3'e geçiş reddedildi - PDKS dosyası seçilmedi");
                    return;
                }
            }

            // Step 3'e geç
            currentStep = 3;
            UpdateStepDisplay();
            LogToTerminal($"[Navigation] Step 3'e geçildi");
        }


        private async void btnPythonInstaller_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                LogToTerminal($"[PDKS Wizard] Python kurulum butonuna tıklandı");
                
                // Butonu devre dışı bırak
                btnPythonInstaller.IsEnabled = false;
                var originalContent = btnPythonInstaller.Content;
                
                // UI thread'de içeriği güncelle
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var grid = btnPythonInstaller.Content as Grid;
                    if (grid != null && grid.Children.Count > 1)
                    {
                        var textBlock = grid.Children[1] as TextBlock;
                        if (textBlock != null)
                        {
                            textBlock.Text = "⏳ Kontrol ediliyor...";
                        }
                    }
                });

                // Python kontrolü ve kurulumu
                if (PythonInstallerService.IsPythonInstalled())
                {
                    var version = PythonInstallerService.GetPythonVersion();
                    var versionText = !string.IsNullOrEmpty(version) 
                        ? $"Kurulu sürüm: Python {version}" 
                        : "Sürüm bilgisi alınamadı";
                    
                    MessageBox.Show(
                        "Python zaten sisteminizde kurulu! ✅\n\n" +
                        $"{versionText}\n\n" +
                        "Python sürümünü kontrol etmek için komut satırında 'python --version' komutunu çalıştırabilirsiniz.",
                        "Python Kurulu",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    // Python kurulumunu başlat
                    var result = await PythonInstallerService.InstallPythonWithDialogAsync();
                    if (result)
                    {
                        // Kurulum başarılı, tekrar kontrol et
                        if (PythonInstallerService.IsPythonInstalled())
                        {
                            var version = PythonInstallerService.GetPythonVersion();
                            var versionText = !string.IsNullOrEmpty(version) 
                                ? $"Kurulu sürüm: Python {version}\n\n" 
                                : "";
                            
                            MessageBox.Show(
                                "Python başarıyla kuruldu ve doğrulandı! ✅\n\n" +
                                $"{versionText}" +
                                "Uygulamayı yeniden başlatmanız önerilir.",
                                "Kurulum Başarılı",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogToTerminal($"[PDKS Wizard] Python kurulum hatası: {ex.Message}");
                MessageBox.Show(
                    $"Python kurulumu sırasında hata oluştu:\n\n{ex.Message}",
                    "Hata",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                // Butonu tekrar etkinleştir
                btnPythonInstaller.IsEnabled = true;
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var grid = btnPythonInstaller.Content as Grid;
                    if (grid != null && grid.Children.Count > 1)
                    {
                        var textBlock = grid.Children[1] as TextBlock;
                        if (textBlock != null)
                        {
                            textBlock.Text = "Python";
                        }
                    }
                });
            }
        }

        private void btnSettings_Click(object sender, RoutedEventArgs e)
        {
            LogToTerminal($"[PDKS Wizard] Ayarlar butonuna tıklandı");
            ShowSettingsModal();
        }

        private void btnLogs_Click(object sender, RoutedEventArgs e)
        {
            LogToTerminal($"[PDKS Wizard] Loglar butonuna tıklandı");
            ShowLogsModal();
        }

        private void ShowLogsModal()
        {
            try
            {
                LogToTerminal($"[Loglar Modal] Modal açılmaya başlanıyor, mevcut log sayısı: {applicationLogs.Count}");
                var modal = new LogsModal(applicationLogs);
                modal.Owner = this;
                LogToTerminal($"[Loglar Modal] Modal açılıyor...");
                modal.ShowDialog();
                LogToTerminal($"[Loglar Modal] Modal kapatıldı");
            }
            catch (Exception ex)
            {
                LogToTerminal($"[Loglar Modal] Modal açma hatası: {ex.Message}");
                MessageBox.Show($"Loglar modal'ı açılırken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnQuickAddCompany_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                LogToTerminal($"[Hızlı Firma] 'Yeni Firma +' butonuna tıklandı");
                var modal = new CompanyManagementModal(currentConfig);
                modal.Owner = this;
                if (modal.ShowDialog() == true)
                {
                    LogToTerminal($"[Hızlı Firma] Modal başarıyla tamamlandı, konfigürasyon yeniden yükleniyor");
                    currentConfig = configService.LoadConfig();
                    RegisterOvertimeColumnsFromConfig(currentConfig);

                    if (currentConfig.CompanyConfigs != null && currentConfig.CompanyConfigs.Count > 0)
                    {
                        var newestCompany = currentConfig.CompanyConfigs.Last();
                        currentConfig.SelectedCompanyCode = newestCompany.CompanyCode;
                    }

                    PopulateCompanyList();
                    if (!string.IsNullOrEmpty(currentConfig.SelectedCompanyCode))
                    {
                        var selectedCompany = currentConfig.CompanyConfigs.FirstOrDefault(c => c.CompanyCode == currentConfig.SelectedCompanyCode);
                        if (selectedCompany != null)
                        {
                            CompanyListBox.SelectedItem = selectedCompany;
                            CompanyListBox.ScrollIntoView(selectedCompany);
                        }
                    }
                    LogToTerminal($"[Hızlı Firma] Firma listesi güncellendi");
                }
                else
                {
                    LogToTerminal($"[Hızlı Firma] Modal iptal edildi");
                }
            }
            catch (Exception ex)
            {
                LogToTerminal($"[Hızlı Firma] Firma ekleme işlemi hatası: {ex.Message}");
                MessageBox.Show($"Yeni firma eklenirken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenCompanyEditModal(CompanyConfig company)
        {
            if (company == null)
            {
                return;
            }

            try
            {
                LogToTerminal($"[Hızlı Düzenleme] '{company.CompanyName}' firması için düzenleme başlatılıyor");
                var editModal = new CompanyManagementModal(currentConfig, company)
                {
                    Owner = this
                };

                if (editModal.ShowDialog() == true)
                {
                    LogToTerminal($"[Hızlı Düzenleme] Modal kaydedildi, konfigürasyon yeniden yükleniyor");
                    currentConfig = configService.LoadConfig();
                    RegisterOvertimeColumnsFromConfig(currentConfig);
                    currentConfig.SelectedCompanyCode = company.CompanyCode;

                    PopulateCompanyList();

                    var refreshedCompany = currentConfig.CompanyConfigs?
                        .FirstOrDefault(c => c.CompanyCode == company.CompanyCode);

                    if (refreshedCompany != null)
                    {
                        CompanyListBox.SelectedItem = refreshedCompany;
                        CompanyListBox.ScrollIntoView(refreshedCompany);
                        LogToTerminal($"[Hızlı Düzenleme] Firma bilgileri güncellendi");
                    }
                }
                else
                {
                    LogToTerminal($"[Hızlı Düzenleme] Modal iptal edildi");
                }
            }
            catch (Exception ex)
            {
                LogToTerminal($"[Hızlı Düzenleme] Hata: {ex.Message}");
                MessageBox.Show($"Firma düzenleme ekranı açılırken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CompanyEditButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button button && button.Tag is CompanyConfig company)
                {
                    CompanyListBox.SelectedItem = company;
                    OpenCompanyEditModal(company);
                }
            }
            catch (Exception ex)
            {
                LogToTerminal($"[Hızlı Düzenleme] Hata: {ex.Message}");
                MessageBox.Show($"Firma düzenleme ekranı açılırken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void PopulateCompanyList()
        {
            try
            {
                if (currentConfig.CompanyConfigs == null || currentConfig.CompanyConfigs.Count == 0)
                {
                    // Firma yok uyarısı
                    MessageBox.Show("Hiç firma tanımlanmamış. Önce ayarlar bölümünden firma ekleyin.", "Firma Yok", MessageBoxButton.OK, MessageBoxImage.Warning);
                    CompanyListBox.ItemsSource = null;
                    CompanyInfoBorder.Visibility = Visibility.Collapsed;
                    return;
                }

                ApplyCompanyFilter(preserveSelection: false);

                LogToTerminal($"[Firma Listesi] {currentConfig.CompanyConfigs.Count} firma yüklendi (filtre: '{companyFilterText}')");
            }
            catch (Exception ex)
            {
                LogToTerminal($"[Firma Listesi] Firma listesi doldurulurken hata: {ex.Message}");
                MessageBox.Show($"Firma listesi yüklenirken hata: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ApplyCompanyFilter(bool preserveSelection = true)
        {
            if (CompanyListBox == null)
            {
                return;
            }

            var allCompanies = currentConfig?.CompanyConfigs ?? new List<CompanyConfig>();
            CompanyConfig previouslySelected = null;

            if (preserveSelection && CompanyListBox.SelectedItem is CompanyConfig selected)
            {
                previouslySelected = selected;
            }
            else if (!preserveSelection && !string.IsNullOrEmpty(currentConfig?.SelectedCompanyCode))
            {
                previouslySelected = allCompanies.FirstOrDefault(c => c.CompanyCode == currentConfig.SelectedCompanyCode);
            }

            var filter = (companyFilterText ?? string.Empty).Trim();
            IEnumerable<CompanyConfig> filteredQuery = allCompanies;

            if (!string.IsNullOrWhiteSpace(filter))
            {
                filteredQuery = filteredQuery.Where(company =>
                    (!string.IsNullOrEmpty(company.CompanyName) &&
                     company.CompanyName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (!string.IsNullOrEmpty(company.CompanyCode) &&
                     company.CompanyCode.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0));
            }

            var filteredCompanies = filteredQuery
                .OrderBy(company => company.CompanyName ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(company => company.CompanyCode ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            CompanyListBox.ItemsSource = filteredCompanies;

            if (filteredCompanies.Count == 0)
            {
                CompanyListBox.SelectedIndex = -1;
                CompanyInfoBorder.Visibility = Visibility.Collapsed;
                return;
            }

            if (previouslySelected != null && filteredCompanies.Contains(previouslySelected))
            {
                CompanyListBox.SelectedItem = previouslySelected;
            }
            else
            {
                CompanyListBox.SelectedIndex = 0;
            }
        }

        private void btnToggleCompanyFilter_Click(object sender, RoutedEventArgs e)
        {
            if (CompanyFilterPopup == null)
            {
                return;
            }

            CompanyFilterPopup.IsOpen = !CompanyFilterPopup.IsOpen;
        }

        private void CompanyFilterPopup_Opened(object sender, EventArgs e)
        {
            if (CompanyFilterTextBox == null)
            {
                return;
            }

            CompanyFilterTextBox.Text = companyFilterText;
            CompanyFilterTextBox.Focus();
            CompanyFilterTextBox.SelectAll();
        }

        private void CompanyFilterPopup_Closed(object sender, EventArgs e)
        {
            if (btnToggleCompanyFilter != null)
            {
                btnToggleCompanyFilter.Focus();
            }
        }

        private void CompanyFilterTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (CompanyFilterTextBox == null)
            {
                return;
            }

            var newText = CompanyFilterTextBox.Text ?? string.Empty;
            if (string.Equals(newText, companyFilterText, StringComparison.Ordinal))
            {
                return;
            }

            companyFilterText = newText;
            ApplyCompanyFilter();
        }

        private void CompanyFilterTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                if (CompanyFilterPopup != null)
                {
                    CompanyFilterPopup.IsOpen = false;
                }
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                if (CompanyFilterPopup != null)
                {
                    CompanyFilterPopup.IsOpen = false;
                }
                e.Handled = true;
            }
        }

        private void btnClearCompanyFilter_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(companyFilterText))
            {
                if (CompanyFilterPopup != null)
                {
                    CompanyFilterPopup.IsOpen = false;
                }
                return;
            }

            companyFilterText = string.Empty;

            if (CompanyFilterTextBox != null)
            {
                CompanyFilterTextBox.Text = string.Empty;
            }
            else
            {
                ApplyCompanyFilter();
            }
        }

        private void CompanyListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            try
            {
                if (CompanyListBox.SelectedItem is CompanyConfig selectedCompany)
                {
                    currentConfig.SelectedCompanyCode = selectedCompany.CompanyCode;

                    // Firma bilgilerini göster
                    CompanyCodeText.Text = $"{selectedCompany.CompanyCode} - Düzenle";
                    CompanyNameText.Text = $"{selectedCompany.CompanyName} - Düzenle";

                    int shiftRuleCount = selectedCompany.ShiftRuleConfigs?.Count ?? 0;
                    ShiftRuleCountText.Text = $"{shiftRuleCount}";
                    UpdateTemplateModeInfo(selectedCompany);

                    // Firma logosunu güncelle
                    UpdateCompanyLogo(selectedCompany);

                    ResetTemplateSelectionUI();

                    UpdateShiftRuleSummaries(selectedCompany);

                    ShiftRulesPopup.IsOpen = false;
                    CompanyInfoBorder.Visibility = Visibility.Visible;
                    UpdateCompanyCalendarSummary(selectedCompany);

                    LogToTerminal($"[Firma Seçimi] {selectedCompany.CompanyName} firması seçildi");
                    RefreshPreviousMonthCarryOverStatus();
                }
                else
                {
                    CompanyInfoBorder.Visibility = Visibility.Collapsed;
                    ShiftRulesPopup.IsOpen = false;
                    ResetTemplateSelectionUI();
                    UpdateTemplateSummary();
                    UpdateTemplateModeInfo(null);
                    // Firma seçilmediğinde varsayılan logo göster
                    UpdateCompanyLogo(null);
                    RefreshPreviousMonthCarryOverStatus();
                }
            }
            catch (Exception ex)
            {
                LogToTerminal($"[Firma Seçimi] Firma seçimi işlenirken hata: {ex.Message}");
            }
        }

        private void ResetTemplateSelectionUI()
        {
            selectedTemplatePath = "";
            UpdateTemplateSummary();
        }

        private bool OpenTemplateSelectionDialog(string initialPath = null)
        {
            try
            {
                var modal = new TemplateUploadModal(initialPath);
                modal.Owner = this;

                if (modal.ShowDialog() == true && File.Exists(modal.SelectedFilePath))
                {
                    selectedTemplatePath = modal.SelectedFilePath;
                    UpdateTemplateSummary();

                    LogToTerminal($"[Step 1] Şablon seçildi: {System.IO.Path.GetFileName(selectedTemplatePath)}");

                    AnalyzeTemplate(selectedTemplatePath);
                    return true;
                }

                if (string.IsNullOrWhiteSpace(selectedTemplatePath))
                {
                    ResetTemplateSelectionUI();
                }
            }
            catch (Exception ex)
            {
                LogToTerminal($"[Step 1] Şablon seçimi hatası: {ex.Message}");
                MessageBox.Show($"Şablon seçimi sırasında hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            return false;
        }

        private bool EnsureTemplateSelected()
        {
            if (!string.IsNullOrWhiteSpace(selectedTemplatePath) && File.Exists(selectedTemplatePath))
            {
                return true;
            }

            return OpenTemplateSelectionDialog(selectedTemplatePath);
        }

        private void UpdateTemplateSummary()
        {
            if (ErpTemplateText == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(selectedTemplatePath) && File.Exists(selectedTemplatePath))
            {
                ErpTemplateText.Text = $"{System.IO.Path.GetFileName(selectedTemplatePath)}";
            }
            else
            {
                ErpTemplateText.Text = "Seçilmedi";
            }
        }

        private void HideTemplateSection()
        {
            ResetTemplateSelectionUI();

            UpdateCompanyCalendarSummary(null);
        }

        private void UpdateCompanyCalendarSummary(CompanyConfig company)
        {
            if (CompanyCalendarText == null || btnOpenCompanyCalendar == null)
            {
                return;
            }

            if (company == null)
            {
                CompanyCalendarText.Text = "Bordro dönemi seçilmedi.";
                btnOpenCompanyCalendar.IsEnabled = false;
                return;
            }

            btnOpenCompanyCalendar.IsEnabled = true;

            // Her zaman mevcut aya göre dinamik olarak ayarla
            var now = DateTime.Now;
            int year = now.Year;
            int month = now.Month;
            
            // Şirket ayarlarını da güncelle (opsiyonel, ama tutarlılık için iyi)
            if (company != null)
            {
                company.PayrollYear = year;
                company.PayrollMonth = month;
            }

            var monthName = CultureInfo.CurrentCulture.DateTimeFormat.GetMonthName(month);
            monthName = char.ToUpper(monthName[0]) + monthName.Substring(1);
            var daysInMonth = DateTime.DaysInMonth(year, month);

            company.MonthDays = daysInMonth;

            var summary = calendarService.GetMonthSummary(year, month);
            var holidays = summary.Holidays.OrderBy(h => h.Date).ToList();

            string infoText = $"{monthName} {year} • {summary.TotalDays} gün";
            if (holidays.Count > 0)
            {
                int halfDays = holidays.Count(h => h.IsHalfDay);
                infoText += holidays.Count == 1 ? " • 1 resmi tatil" : $" • {holidays.Count} resmi tatil";
                if (halfDays > 0)
                {
                    infoText += $" ({halfDays} yarım gün)";
                }
            }
            else
            {
                infoText += " • Resmi tatil yok";
            }

            CompanyCalendarText.Text = infoText;
        }

        private void UpdateCompanyLogo(CompanyConfig company)
        {
            if (CompanyLogoImage == null)
                return;

            try
            {
                string logoPath = null;

                // Önce firma logosunu kontrol et
                if (!string.IsNullOrWhiteSpace(company?.LogoPath))
                {
                    string fullLogoPath = company.LogoPath;
                    if (!System.IO.Path.IsPathRooted(fullLogoPath))
                    {
                        // Göreceli yol ise, kök dizine ekle
                        string appBaseDirectory = System.AppDomain.CurrentDomain.BaseDirectory;
                        fullLogoPath = System.IO.Path.Combine(appBaseDirectory, company.LogoPath);
                    }

                    if (System.IO.File.Exists(fullLogoPath))
                    {
                        logoPath = fullLogoPath;
                    }
                }

                // Eğer logo yoksa varsayılan logoyu kullan
                if (string.IsNullOrWhiteSpace(logoPath))
                {
                    string appBaseDirectory = System.AppDomain.CurrentDomain.BaseDirectory;
                    string defaultLogoPath = System.IO.Path.Combine(appBaseDirectory, "Gemini_Generated_Image_vsio8jvsio8jvsio.png");
                    
                    if (System.IO.File.Exists(defaultLogoPath))
                    {
                        logoPath = defaultLogoPath;
                    }
                }

                // Logoyu yükle
                if (!string.IsNullOrWhiteSpace(logoPath) && System.IO.File.Exists(logoPath))
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(logoPath, UriKind.Absolute);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    CompanyLogoImage.Source = bitmap;
                }
                else
                {
                    CompanyLogoImage.Source = null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Company Logo] Logo yüklenirken hata: {ex.Message}");
                CompanyLogoImage.Source = null;
            }
        }

        private void btnHeaderCalendar_Click(object sender, RoutedEventArgs e)
        {
            var company = CompanyListBox.SelectedItem as CompanyConfig;
            OpenCalendarDialog(company);
        }

        private void btnOpenCompanyCalendar_Click(object sender, RoutedEventArgs e)
        {
            if (CompanyListBox.SelectedItem is CompanyConfig selectedCompany)
            {
                OpenCalendarDialog(selectedCompany);
            }
            else
            {
                MessageBox.Show("Lütfen önce bir firma seçin.", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OpenCalendarDialog(CompanyConfig company)
        {
            int defaultYear = DateTime.Now.Year;
            int defaultMonth = DateTime.Now.Month;

            if (company != null)
            {
                defaultYear = company.PayrollYear > 0 ? company.PayrollYear : defaultYear;
                defaultMonth = company.PayrollMonth > 0 ? company.PayrollMonth : defaultMonth;
            }

            var calendarWindow = new HolidayCalendarWindow(calendarService, defaultYear, defaultMonth)
            {
                Owner = this
            };

            if (calendarWindow.ShowDialog() == true && company != null)
            {
                company.PayrollYear = calendarWindow.SelectedYear;
                company.PayrollMonth = calendarWindow.SelectedMonth;
                company.MonthDays = calendarService.GetDaysInMonth(calendarWindow.SelectedYear, calendarWindow.SelectedMonth);
                UpdateCompanyCalendarSummary(company);

                try
                {
                    configService.SaveConfig(currentConfig);
                    LogToTerminal($"[Takvim] {company.CompanyName} için dönem {calendarWindow.SelectedMonth}/{calendarWindow.SelectedYear} olarak güncellendi.");
                }
                catch (Exception ex)
                {
                    LogToTerminal($"[Takvim] Takvim bilgileri kaydedilirken hata: {ex.Message}");
                }
            }
        }

        private void UpdateShiftRuleSummaries(CompanyConfig company)
        {
            try
            {
                var rules = company.ShiftRuleConfigs ?? new List<ShiftRuleConfig>();

                ShiftRuleCountText.Text = $"{rules.Count}";
                UpdateTemplateModeInfo(company);

                if (rules.Count == 0)
                {
                    ShiftRulesItemsControl.ItemsSource = null;
                    ShiftRulesItemsControl.Visibility = Visibility.Collapsed;
                    ShiftRulesEmptyText.Visibility = Visibility.Visible;
                    return;
                }

                var summaries = rules.Select(rule =>
                {
                    string patterns = rule.ShiftPatterns != null && rule.ShiftPatterns.Count > 0
                        ? string.Join(", ", rule.ShiftPatterns)
                        : "Tanımlı vardiya saatleri bulunmuyor.";

                    string details = $"Varsayılan: {rule.DefaultStartTime:hh\\:mm}-{rule.DefaultEndTime:hh\\:mm} (Mola: {rule.BreakHours} saat)\n" +
                                     $"Vardiyalar: {patterns}";

                    return new ShiftRuleSummary
                    {
                        Title = string.IsNullOrWhiteSpace(rule.GroupName) ? "İsimsiz Kural" : rule.GroupName,
                        Details = details
                    };
                }).ToList();

                ShiftRulesItemsControl.ItemsSource = summaries;
                ShiftRulesItemsControl.Visibility = Visibility.Visible;
                ShiftRulesEmptyText.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                LogToTerminal($"[Firma Seçimi] Vardiya kural özetleri hazırlanırken hata: {ex.Message}");
                ShiftRulesItemsControl.ItemsSource = null;
                ShiftRulesItemsControl.Visibility = Visibility.Collapsed;
                ShiftRulesEmptyText.Visibility = Visibility.Visible;
            }
        }

        private void UpdateTemplateModeInfo(CompanyConfig company)
        {
            if (TemplateModeInfoText == null)
            {
                return;
            }

            if (company?.HorizontalTemplateSettings?.ApplyRulesWithoutShift == true)
            {
                TemplateModeInfoText.Text = "Şablon Modu: Yatay (Vardiyasız)";
            }
            else if (company != null)
            {
                TemplateModeInfoText.Text = "Şablon Modu: Giriş-Çıkış (Vardiyalı)";
            }
            else
            {
                TemplateModeInfoText.Text = "Şablon Modu: -";
            }
        }

        private List<OvertimeDetailEntry> BuildOvertimeDetailEntries(CompanyConfig company)
        {
            var details = new List<OvertimeDetailEntry>();
            if (company == null)
            {
                return details;
            }

            // Ay filtrelemesi ekle
            int year = company.PayrollYear > 0 ? company.PayrollYear : DateTime.Now.Year;
            int month = company.PayrollMonth > 0 ? company.PayrollMonth : DateTime.Now.Month;

            string restColumnName = company.SpecialOvertimeSettings?.EarnedRestDayColumnName;
            string holidayColumnName = company.SpecialOvertimeSettings?.HolidayWorkColumnName;

            // Sadece seçili ayın kayıtlarını işle
            foreach (var record in pdksDataService.GetAllRecords().Where(r => r.IsMatched && r.Date.Year == year && r.Date.Month == month))
            {
                string personnelCode = !string.IsNullOrWhiteSpace(record.MatchedPersonnelCode)
                    ? record.MatchedPersonnelCode
                    : record.PersonnelCode;
                string personnelName = !string.IsNullOrWhiteSpace(record.MatchedPersonnelName)
                    ? record.MatchedPersonnelName
                    : record.PersonnelName;

                if (record.WorkedOnOfficialHoliday)
                {
                    double hours = GetRecordColumnHours(record, holidayColumnName);
                    string eventTitle = record.IsHalfDayHoliday ? "Resmi tatilde çalışma (yarım gün)" : "Resmi tatilde çalışma";
                    string notes = string.IsNullOrWhiteSpace(record.HolidayName) ? string.Empty : record.HolidayName;
                    var effectiveDate = record.SpecialOvertimeEffectiveDate ?? record.Date;

                    details.Add(new OvertimeDetailEntry
                    {
                        PersonnelCode = personnelCode,
                        PersonnelName = personnelName,
                        Date = effectiveDate,
                        EventType = eventTitle,
                        Hours = hours,
                        ColumnName = holidayColumnName ?? "Bilinmiyor",
                        Notes = notes,
                        EarnedRange = record.EarnedRestSourceRange,
                        RuleName = record.EarnedRestRuleName
                    });
                }

                if (record.WorkedOnEarnedRestDay)
                {
                    double hours = GetRecordColumnHours(record, restColumnName);
                    var effectiveDate = record.SpecialOvertimeEffectiveDate ?? record.Date;
                    details.Add(new OvertimeDetailEntry
                    {
                        PersonnelCode = personnelCode,
                        PersonnelName = personnelName,
                        Date = effectiveDate,
                        EventType = "Hak edilen tatilde çalışma",
                        Hours = hours,
                        ColumnName = restColumnName ?? "Bilinmiyor",
                        Notes = string.IsNullOrWhiteSpace(restColumnName) ? string.Empty : $"ERP sütunu: {restColumnName}",
                        EarnedRange = record.EarnedRestSourceRange,
                        RuleName = record.EarnedRestRuleName
                    });
                }

                if (record.VacationDays > 0 && record.WorkedHours <= 0)
                {
                    var effectiveDate = record.SpecialOvertimeEffectiveDate ?? record.Date;
                    details.Add(new OvertimeDetailEntry
                    {
                        PersonnelCode = personnelCode,
                        PersonnelName = personnelName,
                        Date = effectiveDate,
                        EventType = "Hak edilen tatil (izin)",
                        Hours = 0,
                        ColumnName = restColumnName ?? "Bilinmiyor",
                        Notes = "Otomatik izin günü",
                        EarnedRange = record.EarnedRestSourceRange,
                        RuleName = record.EarnedRestRuleName
                    });
                }
            }

            return details
                .OrderBy(d => d.PersonnelName)
                .ThenBy(d => d.Date)
                .ToList();
        }

        private double GetRecordColumnHours(PDKSDataModel record, string columnName)
        {
            if (record == null)
            {
                return 0;
            }

            if (!string.IsNullOrWhiteSpace(columnName) &&
                record.FMColumnHours != null &&
                record.FMColumnHours.TryGetValue(columnName, out var value))
            {
                return value;
            }

            return record.WorkedHours;
        }

        private string BuildErpResultSummary(string filePath, int processedCount, List<OvertimeDetailEntry> details, List<ConditionalEarningDetail>? conditionalEarningsDetails = null, List<string>? carryOverVacationLogs = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine("ERP şablonu başarıyla güncellendi.");
            sb.AppendLine($"Dosya: {System.IO.Path.GetFileName(filePath)}");
            sb.AppendLine($"İşlenen personel sayısı: {processedCount}");

            if (details != null && details.Count > 0)
            {
                int restWork = details.Count(d => d.EventType == "Hak edilen tatilde çalışma");
                int restDays = details.Count(d => d.EventType == "Hak edilen tatil (izin)");
                int holidayWork = details.Count(d => d.EventType.StartsWith("Resmi tatilde çalışma"));

                sb.AppendLine();
                sb.AppendLine("Özet:");
                if (restDays > 0)
                {
                    sb.AppendLine($"• {restDays} hak edilen tatil günü sisteme işlendi.");
                }
                if (restWork > 0)
                {
                    sb.AppendLine($"• {restWork} kayıt hak edilen tatilde çalışma olarak kaydedildi.");
                }
                if (holidayWork > 0)
                {
                    sb.AppendLine($"• {holidayWork} kayıt resmi tatilde çalışma olarak kaydedildi.");
                }
            }

            // Bir önceki aydan devir eden günlerle tatil hakkı kazanıldığında log mesajları
            if (carryOverVacationLogs != null && carryOverVacationLogs.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Tatil Hakkı Devir Bilgileri:");
                foreach (var log in carryOverVacationLogs)
                {
                    sb.AppendLine($"• {log}");
                }
            }

            if (conditionalEarningsDetails != null && conditionalEarningsDetails.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Koşullu Kazançlar:");
                var groupedByColumn = conditionalEarningsDetails.GroupBy(d => d.ColumnName);
                foreach (var group in groupedByColumn)
                {
                    int count = group.Count();
                    double totalValue = group.Sum(d => d.Value);
                    sb.AppendLine($"• {group.Key}: {count} personel için toplam {totalValue:N2} TL");
                }
            }

            sb.AppendLine();
            sb.Append("Detaylar için 'Detaylı Bilgi' butonunu kullanabilirsiniz.");
            return sb.ToString();
        }

        /// <summary>
        /// Koşullu kazanç detaylarını oluşturur
        /// </summary>
        private List<ConditionalEarningDetail> BuildConditionalEarningsDetails(List<PersonnelSummary> personnelSummaries, CompanyConfig companyConfig)
        {
            var details = new List<ConditionalEarningDetail>();

            if (personnelSummaries == null || companyConfig == null)
            {
                return details;
            }

            foreach (var summary in personnelSummaries)
            {
                var conditionalEarnings = ApplyConditionalEarningsRules(summary, companyConfig);
                foreach (var earning in conditionalEarnings)
                {
                    details.Add(new ConditionalEarningDetail
                    {
                        PersonnelCode = summary.PersonnelCode,
                        PersonnelName = summary.PersonnelName,
                        ColumnName = earning.ColumnName,
                        ColumnLetter = earning.ColumnLetter,
                        Value = earning.Value,
                        RuleDescription = earning.RuleDescription,
                        ConditionType = earning.ConditionType
                    });
                }
            }

            return details.OrderBy(d => d.PersonnelName).ThenBy(d => d.ColumnName).ToList();
        }

        private class ShiftRuleSummary
        {
            public string Title { get; set; }
            public string Details { get; set; }
        }

        private void ShowSettingsModal()
        {
            try
            {
                LogToTerminal($"[Ayarlar Modal] Modal açılmaya başlanıyor");
                var modal = new SettingsModal(currentConfig, configService);
                modal.Owner = this;
                LogToTerminal($"[Ayarlar Modal] Modal açılıyor...");
                modal.ShowDialog();

                // Modal kapatıldıktan sonra konfigürasyonu yeniden yükle
                currentConfig = configService.LoadConfig();
                LogToTerminal($"[Ayarlar Modal] Modal kapatıldı, konfigürasyon yeniden yüklendi");
            }
            catch (Exception ex)
            {
                LogToTerminal($"[Ayarlar Modal] Modal açma hatası: {ex.Message}");
                MessageBox.Show($"Ayarlar modal'ı açılırken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void ReloadConfig()
        {
            try
            {
                Console.WriteLine($"[ReloadConfig] Konfigürasyon yeniden yükleniyor");
                currentConfig = configService.LoadConfig();
                Console.WriteLine($"[ReloadConfig] Config yüklendi, firma sayısı: {currentConfig.CompanyConfigs.Count}");
                PopulateCompanyList();
                Console.WriteLine($"[ReloadConfig] Firma listesi yenilendi");
                LogToTerminal($"[Config Reload] Konfigürasyon yeniden yüklendi");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ReloadConfig] Hata: {ex.Message}");
                LogToTerminal($"[Config Reload] Hata: {ex.Message}");
            }
        }

        private void btnClose_Click(object sender, RoutedEventArgs e)
        {
            LogToTerminal($"[PDKS Wizard] Kapatma butonuna tıklandı - Step: {currentStep}");
            if (MessageBox.Show("Wizard'ı kapatmak istediğinizden emin misiniz? İşlem kaydedilmeyecektir.",
                              "Onay", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                LogToTerminal($"[PDKS Wizard] Wizard kapatıldı");
                this.Close();
            }
        }


        #region Terminal Logging

        private void LogToTerminal(string message)
        {
            string logMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
            Console.WriteLine(logMessage);
            applicationLogs.Add(logMessage);
            File.AppendAllText("debug_log.txt", logMessage + Environment.NewLine);
        }

        #endregion

        #region Template Info Parsing

        private string ParseTemplateInfo(string pythonOutput, string filePath)
        {
            try
            {
                string[] lines = pythonOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                string rowCount = "Bilinmiyor";
                string colCount = "Bilinmiyor";
                string firstRowInfo = "";

                foreach (string line in lines)
                {
                    if (line.Contains("Satır sayısı:"))
                    {
                        rowCount = line.Replace("Satır sayısı:", "").Trim();
                    }
                    else if (line.Contains("Sütun sayısı:"))
                    {
                        colCount = line.Replace("Sütun sayısı:", "").Trim();
                    }
                    else if (line.Contains("İlk satır:"))
                    {
                        firstRowInfo = line.Replace("İlk satır:", "").Trim();
                        break;
                    }
                }

                // İlk satır bilgisini parse et
                string personnelInfo = "";
                if (!string.IsNullOrEmpty(firstRowInfo) && firstRowInfo.Length > 10)
                {
                    try
                    {
                        // Numpy array format'ını temizle
                        firstRowInfo = firstRowInfo.Replace("np.int64(", "").Replace("np.float64(", "").Replace(")", "").Replace("nan", "0");

                        // Köşeli parantezleri kaldır
                        if (firstRowInfo.StartsWith("[") && firstRowInfo.EndsWith("]"))
                        {
                            firstRowInfo = firstRowInfo.Substring(1, firstRowInfo.Length - 2);
                        }

                        string[] values = firstRowInfo.Split(',');
                        if (values.Length >= 3)
                        {
                            string personnelCode = values[0].Trim('\'', ' ', '"');
                            string name = values[1].Trim('\'', ' ', '"');
                            string tcNo = values[2].Trim('\'', ' ', '"');

                            personnelInfo = $"\n\n👤 Örnek Personel Bilgisi:\n• Sicil No: {personnelCode}\n• Ad Soyad: {name}\n• TC No: {tcNo}";
                        }
                    }
                    catch
                    {
                        personnelInfo = "\n\n📋 Veri formatı karmaşık - detaylar log'da mevcut";
                    }
                }

                // Sonuç string'ini oluştur
                return $"✅ Şablon başarıyla yüklendi!\n\n" +
                       $"📊 Dosya Bilgileri:\n" +
                       $"• Dosya Adı: {System.IO.Path.GetFileName(filePath)}\n" +
                       $"• Toplam Satır: {rowCount}\n" +
                       $"• Toplam Sütun: {colCount}\n" +
                       $"• Veri Tipi: Excel (.xlsx)\n" +
                       $"{personnelInfo}\n\n" +
                       $"💡 Bu şablondaki personel bilgileri PDKS verileriyle eşleştirilecektir.";
            }
            catch (Exception ex)
            {
                LogToTerminal($"[Şablon Parse Hatası] {ex.Message}");
                return $"✅ Şablon yüklendi!\n\nDosya: {System.IO.Path.GetFileName(filePath)}\n\n(Ham veri işlenirken hata oluştu, detaylar log'da mevcut)";
            }
        }

        #endregion
    }


    public class StatusTagInfo
    {
        public string Text { get; set; } = string.Empty;
        public Brush Background { get; set; } = Brushes.Gray;
    }


    public class OvertimeDetailEntry
    {
        public string PersonnelCode { get; set; }
        public string PersonnelName { get; set; }
        public DateTime Date { get; set; }
        public string EventType { get; set; }
        public double Hours { get; set; }
        public string ColumnName { get; set; }
        public string Notes { get; set; }
        public string EarnedRange { get; set; }
        public string RuleName { get; set; }
    }

    public class ConditionalEarningDetail
    {
        public string PersonnelCode { get; set; } = string.Empty;
        public string PersonnelName { get; set; } = string.Empty;
        public string ColumnName { get; set; } = string.Empty;
        public string ColumnLetter { get; set; } = string.Empty;
        public double Value { get; set; }
        public string RuleDescription { get; set; } = string.Empty;
        public string ConditionType { get; set; } = string.Empty;
    }


    public class ERPResultModal : Window
    {
        private readonly List<OvertimeDetailEntry> overtimeDetails;
        private readonly List<ConditionalEarningDetail> conditionalEarningsDetails;
        private readonly string? rawOutput;

        public ERPResultModal(bool success, string filePath, int recordCount, string message, List<OvertimeDetailEntry>? overtimeDetails, string? rawOutput, List<ConditionalEarningDetail>? conditionalEarningsDetails = null)
        {
            this.overtimeDetails = overtimeDetails ?? new List<OvertimeDetailEntry>();
            this.conditionalEarningsDetails = conditionalEarningsDetails ?? new List<ConditionalEarningDetail>();
            this.rawOutput = rawOutput;
            InitializeModal(success, filePath, recordCount, message);
        }

        private void InitializeModal(bool success, string filePath, int recordCount, string message)
        {
            Title = success ? "ERP Aktarımı Başarılı" : "ERP Aktarımı Başarısız";
            Width = success ? 880 : 760;
            Height = success ? 640 : 520;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.CanResize;
            MinWidth = 640;
            MinHeight = 420;
            Background = new SolidColorBrush(Color.FromRgb(245, 247, 252));
            
            // Poppins font yükleme - fallback ile
            FontFamily poppinsFont;
            try
            {
                poppinsFont = new FontFamily("pack://application:,,,/Fonts/Poppins-Regular.ttf#Poppins");
            }
            catch
            {
                // Font yüklenemezse sistem fontunu kullan
                poppinsFont = new FontFamily("Segoe UI");
            }
            FontFamily = poppinsFont;

            // Global font stilleri ekle
            var textBlockStyle = new Style(typeof(TextBlock));
            textBlockStyle.Setters.Add(new Setter(TextBlock.FontFamilyProperty, poppinsFont));
            Resources.Add(typeof(TextBlock), textBlockStyle);
            
            var buttonStyle = new Style(typeof(Button));
            buttonStyle.Setters.Add(new Setter(Button.FontFamilyProperty, poppinsFont));
            Resources.Add(typeof(Button), buttonStyle);
            
            var labelStyle = new Style(typeof(Label));
            labelStyle.Setters.Add(new Setter(Label.FontFamilyProperty, poppinsFont));
            Resources.Add(typeof(Label), labelStyle);

            var scrollViewer = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };

            var rootStack = new StackPanel
            {
                Margin = new Thickness(28, 24, 28, 24)
            };
            var defaultTextStyle = new Style(typeof(TextBlock));
            defaultTextStyle.Setters.Add(new Setter(TextBlock.FontFamilyProperty, new FontFamily("pack://application:,,,/Fonts/Poppins-Regular.ttf#Poppins")));
            defaultTextStyle.Setters.Add(new Setter(TextBlock.FontSizeProperty, 14.0));
            rootStack.Resources.Add(typeof(TextBlock), defaultTextStyle);

            var messageLines = (message ?? string.Empty)
                .Replace("\r", string.Empty)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .ToList();

            var primaryLine = messageLines.FirstOrDefault() ?? (success
                ? "ERP şablonu başarıyla güncellendi."
                : "ERP aktarımı sırasında bir hata oluştu.");

            var headerCard = new Border
            {
                Background = success ? new SolidColorBrush(Color.FromRgb(236, 253, 245)) : new SolidColorBrush(Color.FromRgb(254, 242, 242)),
                BorderBrush = success ? new SolidColorBrush(Color.FromRgb(167, 243, 208)) : new SolidColorBrush(Color.FromRgb(254, 202, 202)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(18),
                Padding = new Thickness(26),
                Margin = new Thickness(0, 0, 0, 24)
            };

            var headerStack = new StackPanel();

            var headerRow = new StackPanel
            {
                Orientation = Orientation.Horizontal
            };

            var headerIcon = new Border
            {
                Width = 52,
                Height = 52,
                CornerRadius = new CornerRadius(26),
                Background = success ? new SolidColorBrush(Color.FromRgb(34, 197, 94)) : new SolidColorBrush(Color.FromRgb(248, 113, 113)),
                Child = new TextBlock
                {
                    Text = success ? "" : "",
                    FontFamily = new FontFamily("Segoe MDL2 Assets"),
                    FontSize = 26,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };

            var headerTextStack = new StackPanel
            {
                Margin = new Thickness(16, 0, 0, 0)
            };

            headerTextStack.Children.Add(new TextBlock
            {
                Text = success ? "ERP Şablonu başarıyla oluşturuldu!" : "ERP aktarımı tamamlanamadı",
                FontSize = 22,
                FontFamily = poppinsFont,
                FontWeight = FontWeights.SemiBold,
                Foreground = success ? new SolidColorBrush(Color.FromRgb(21, 128, 61)) : new SolidColorBrush(Color.FromRgb(220, 38, 38)),
                    TextWrapping = TextWrapping.Wrap
            });

            headerTextStack.Children.Add(new TextBlock
            {
                Text = primaryLine,
                FontSize = 14,
                Foreground = success ? new SolidColorBrush(Color.FromRgb(22, 101, 52)) : new SolidColorBrush(Color.FromRgb(185, 28, 28)),
                Margin = new Thickness(0, 6, 0, 0),
                    TextWrapping = TextWrapping.Wrap
            });

            headerRow.Children.Add(headerIcon);
            headerRow.Children.Add(headerTextStack);
            headerStack.Children.Add(headerRow);

            if (messageLines.Count > 1)
            {
                var bulletPanel = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
                var bulletBrush = success ? new SolidColorBrush(Color.FromRgb(22, 101, 52)) : new SolidColorBrush(Color.FromRgb(185, 28, 28));

                foreach (var line in messageLines.Skip(1))
                {
                    bulletPanel.Children.Add(CreateBulletText(line, bulletBrush));
                }

                headerStack.Children.Add(bulletPanel);
            }

            headerCard.Child = headerStack;
            rootStack.Children.Add(headerCard);

            if (success)
            {
                var summaryGrid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 12) };
                summaryGrid.Children.Add(CreateInfoCard("", "İşlenen Kayıt", recordCount.ToString("N0"), Color.FromRgb(34, 197, 94)));

                // Koşullu kazanç bilgisi
                int conditionalEarningsCount = conditionalEarningsDetails?.Count ?? 0;
                if (conditionalEarningsCount > 0)
                {
                    double totalConditionalValue = conditionalEarningsDetails!.Sum(d => d.Value);
                    summaryGrid.Children.Add(CreateInfoCard("", "Koşullu Kazanç", $"{conditionalEarningsCount} kural\n{totalConditionalValue:N2} TL", Color.FromRgb(168, 85, 247)));
                }
                else
                {
                    summaryGrid.Children.Add(CreateInfoCard("", "Koşullu Kazanç", "Yok", Color.FromRgb(168, 85, 247)));
                }

                rootStack.Children.Add(summaryGrid);
            }
            else
            {
                var errorCard = CreateInfoCard("", "Hata Detayı", message, Color.FromRgb(220, 38, 38));
                errorCard.Margin = new Thickness(0, 0, 0, 12);
                rootStack.Children.Add(errorCard);
            }

            rootStack.Children.Add(CreateDivider());

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            if (success)
            {
                if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
                {
                    buttonPanel.Children.Add(CreateIconButton("", "Dosyayı Aç", Color.FromRgb(37, 99, 235), (s, e) =>
                    {
                        try
                        {
                            Process.Start(new ProcessStartInfo
                        {
                            FileName = filePath,
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Dosya açılamadı: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    }));

                    buttonPanel.Children.Add(CreateIconButton("", "Klasörü Aç", Color.FromRgb(234, 88, 12), (s, e) =>
                    {
                    try
                    {
                            Process.Start(new ProcessStartInfo
                        {
                            FileName = "explorer.exe",
                            Arguments = $"/select,\"{filePath}\"",
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Klasör açılamadı: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    }));
                }

                buttonPanel.Children.Add(CreateIconButton("", "Detaylı Bilgi", Color.FromRgb(59, 130, 246), (s, e) =>
                {
                    var detailModal = new ERPResultDetailsModal(overtimeDetails, rawOutput, conditionalEarningsDetails)
                    {
                        Owner = this
                    };
                    detailModal.ShowDialog();
                }, overtimeDetails.Any() || !string.IsNullOrWhiteSpace(rawOutput) || (conditionalEarningsDetails?.Any() == true)));

                buttonPanel.Children.Add(CreateIconButton("", "Tamam", Color.FromRgb(22, 163, 74), (s, e) => Close()));
            }
            else
            {
                buttonPanel.Children.Add(CreateIconButton("", "Detaylı Bilgi", Color.FromRgb(107, 114, 128), (s, e) =>
                {
                    var detailModal = new ERPResultDetailsModal(overtimeDetails, rawOutput, conditionalEarningsDetails)
                    {
                        Owner = this
                    };
                    detailModal.ShowDialog();
                }, overtimeDetails.Any() || !string.IsNullOrWhiteSpace(rawOutput) || (conditionalEarningsDetails?.Any() == true)));

                buttonPanel.Children.Add(CreateIconButton("", "Tamam", Color.FromRgb(220, 38, 38), (s, e) => Close()));
            }

            rootStack.Children.Add(buttonPanel);

            scrollViewer.Content = rootStack;
            Content = scrollViewer;
        }

        private Border CreateInfoCard(string iconGlyph, string title, string value, Color accentColor)
        {
            var backgroundColor = Color.FromArgb(28, accentColor.R, accentColor.G, accentColor.B);

            var card = new Border
            {
                Background = new SolidColorBrush(backgroundColor),
                BorderBrush = new SolidColorBrush(Color.FromArgb(64, accentColor.R, accentColor.G, accentColor.B)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(18),
                Margin = new Thickness(0, 0, 16, 16),
                MinHeight = 120
            };

            var stack = new StackPanel();

            stack.Children.Add(new TextBlock
            {
                Text = iconGlyph,
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 20,
                Foreground = new SolidColorBrush(accentColor),
                Margin = new Thickness(0, 0, 0, 8),
                HorizontalAlignment = HorizontalAlignment.Left
            });

            // Poppins font yükleme - fallback ile
            FontFamily poppinsMedium;
            FontFamily poppinsSemiBold;
            try
            {
                poppinsMedium = new FontFamily("pack://application:,,,/Fonts/Poppins-Medium.ttf#Poppins");
            }
            catch
            {
                poppinsMedium = new FontFamily("Segoe UI");
            }
            try
            {
                poppinsSemiBold = new FontFamily("pack://application:,,,/Fonts/Poppins-SemiBold.ttf#Poppins");
            }
            catch
            {
                poppinsSemiBold = new FontFamily("Segoe UI");
            }

            stack.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
                FontFamily = poppinsMedium,
                Margin = new Thickness(0, 0, 0, 4)
            });

            stack.Children.Add(new TextBlock
            {
                Text = value,
                FontSize = 18,
                FontFamily = poppinsSemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 4)
            });

            card.Child = stack;
            return card;
        }

        private string GetPathAfterUser(string fullPath)
        {
            if (string.IsNullOrWhiteSpace(fullPath) || fullPath == "-")
            {
                return "-";
            }

            try
            {
                // Users klasörünü bul
                string usersPattern = @"\Users\";
                int usersIndex = fullPath.IndexOf(usersPattern, StringComparison.OrdinalIgnoreCase);
                
                if (usersIndex >= 0)
                {
                    // Users\ kısmından sonrasını al
                    int startIndex = usersIndex + usersPattern.Length;
                    string afterUsers = fullPath.Substring(startIndex);
                    
                    // Kullanıcı adından sonrasını bul (ilk \ karakterinden sonrası)
                    int firstSlashIndex = afterUsers.IndexOf('\\');
                    if (firstSlashIndex >= 0 && firstSlashIndex < afterUsers.Length - 1)
                    {
                        // Kullanıcı adını atla, sonrasını döndür
                        return afterUsers.Substring(firstSlashIndex + 1);
                    }
                    
                    // Eğer \ yoksa, kullanıcı adından sonrasını döndür
                    return afterUsers;
                }
                
                // Users klasörü bulunamadıysa, orijinal path'i döndür
                return fullPath;
            }
            catch
            {
                return fullPath;
            }
        }

        private Border CreateDivider()
        {
            return new Border
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Margin = new Thickness(0, 12, 0, 20)
            };
        }

        private TextBlock CreateBulletText(string text, Brush brush)
        {
            bool isBullet = text.StartsWith("-") || text.StartsWith("•");
            string content = isBullet ? text.TrimStart('-', '•', ' ') : text;

            return new TextBlock
            {
                Text = isBullet ? $"• {content}" : content,
                FontSize = 13,
                FontFamily = new FontFamily("pack://application:,,,/Fonts/Poppins-Regular.ttf#Poppins"),
                Foreground = brush,
                Margin = new Thickness(0, 4, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };
        }

        private Button CreateIconButton(string iconGlyph, string text, Color baseColor, RoutedEventHandler onClick, bool isEnabled = true)
        {
            var button = new Button
            {
                Margin = new Thickness(6, 0, 6, 0),
                IsEnabled = isEnabled
            };

            ApplyRoundedButtonStyle(button, baseColor);

            var contentStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            contentStack.Children.Add(new TextBlock
            {
                Text = iconGlyph,
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 16,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center
            });

            contentStack.Children.Add(new TextBlock
            {
                Text = text,
                FontFamily = new FontFamily("pack://application:,,,/Fonts/Poppins-Medium.ttf#Poppins"),
                FontSize = 14,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            });

            button.Content = contentStack;
            button.Click += onClick;

            return button;
        }

        private void ApplyRoundedButtonStyle(Button button, Color baseColor)
        {
            var style = new Style(typeof(Button));
            style.Setters.Add(new Setter(Button.BackgroundProperty, new SolidColorBrush(baseColor)));
            style.Setters.Add(new Setter(Button.ForegroundProperty, Brushes.White));
            style.Setters.Add(new Setter(Button.BorderThicknessProperty, new Thickness(0)));
            style.Setters.Add(new Setter(Button.PaddingProperty, new Thickness(18, 10, 18, 10)));
            style.Setters.Add(new Setter(Button.CursorProperty, Cursors.Hand));
            style.Setters.Add(new Setter(Button.FontFamilyProperty, new FontFamily("pack://application:,,,/Fonts/Poppins-Medium.ttf#Poppins")));
            style.Setters.Add(new Setter(Button.FontSizeProperty, 14.0));

            var template = new ControlTemplate(typeof(Button));
            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
            borderFactory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            borderFactory.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Button.PaddingProperty));

            var contentPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
            contentPresenter.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
            contentPresenter.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);

            borderFactory.AppendChild(contentPresenter);
            template.VisualTree = borderFactory;
            style.Setters.Add(new Setter(Button.TemplateProperty, template));

            var hoverTrigger = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
            hoverTrigger.Setters.Add(new Setter(Button.BackgroundProperty, new SolidColorBrush(DarkenColor(baseColor, 0.12))));
            style.Triggers.Add(hoverTrigger);

            var disabledTrigger = new Trigger { Property = Button.IsEnabledProperty, Value = false };
            disabledTrigger.Setters.Add(new Setter(Button.CursorProperty, Cursors.Arrow));
            disabledTrigger.Setters.Add(new Setter(Button.OpacityProperty, 0.6));
            style.Triggers.Add(disabledTrigger);

            button.Style = style;
        }

        private Color DarkenColor(Color color, double factor)
        {
            factor = Math.Max(0, Math.Min(1, factor));
            return Color.FromRgb(
                (byte)Math.Max(0, color.R * (1 - factor)),
                (byte)Math.Max(0, color.G * (1 - factor)),
                (byte)Math.Max(0, color.B * (1 - factor)));
        }
    }

    public class ERPResultDetailsModal : Window
    {
        public ERPResultDetailsModal(IEnumerable<OvertimeDetailEntry> overtimeDetails, string rawOutput, IEnumerable<ConditionalEarningDetail>? conditionalEarningsDetails = null)
        {
            Title = "ERP Çıktı Detayları";
            Width = 920;
            Height = 640;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.CanResize;
            Background = new SolidColorBrush(Color.FromRgb(248, 249, 250));
            FontFamily = new FontFamily("pack://application:,,,/Fonts/Poppins-Regular.ttf#Poppins");

            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var header = new TextBlock
            {
                Text = "Özel FM, tatil işlemleri ve koşullu kazançlara dair detaylı bilgiler aşağıda yer almaktadır.",
                FontFamily = new FontFamily("pack://application:,,,/Fonts/Poppins-Regular.ttf#Poppins"),
                FontSize = 14,
                Margin = new Thickness(20, 20, 20, 10),
                Foreground = Brushes.DimGray
            };

            var tabControl = new TabControl
            {
                Margin = new Thickness(20),
                FontFamily = new FontFamily("pack://application:,,,/Fonts/Poppins-Regular.ttf#Poppins")
            };

            var detailTab = new TabItem { Header = "Özel FM Olayları" };
            if (overtimeDetails != null && overtimeDetails.Any())
            {
                var dataGrid = new DataGrid
                {
                    ItemsSource = overtimeDetails,
                    AutoGenerateColumns = false,
                    CanUserAddRows = false,
                    IsReadOnly = true,
                    AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(248, 249, 250)),
                    RowHeight = 38,
                    ColumnWidth = new DataGridLength(1, DataGridLengthUnitType.Star),
                    HeadersVisibility = DataGridHeadersVisibility.Column
                };

                dataGrid.Columns.Add(new DataGridTextColumn { Header = "Sicil", Binding = new Binding(nameof(OvertimeDetailEntry.PersonnelCode)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
                dataGrid.Columns.Add(new DataGridTextColumn { Header = "Ad Soyad", Binding = new Binding(nameof(OvertimeDetailEntry.PersonnelName)), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
                dataGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = "Tarih",
                    Binding = new Binding(nameof(OvertimeDetailEntry.Date)) { StringFormat = "dd.MM.yyyy" },
                    Width = new DataGridLength(1.05, DataGridLengthUnitType.Star)
                });
                dataGrid.Columns.Add(new DataGridTextColumn { Header = "Olay", Binding = new Binding(nameof(OvertimeDetailEntry.EventType)), Width = new DataGridLength(1.6, DataGridLengthUnitType.Star) });
                dataGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = "Hak Ediş Periyodu",
                    Binding = new Binding(nameof(OvertimeDetailEntry.EarnedRange)),
                    Width = new DataGridLength(1.6, DataGridLengthUnitType.Star)
                });
                dataGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = "Uygulanan Kural",
                    Binding = new Binding(nameof(OvertimeDetailEntry.RuleName)),
                    Width = new DataGridLength(1.4, DataGridLengthUnitType.Star)
                });
                dataGrid.Columns.Add(new DataGridTextColumn { Header = "ERP Sütunu", Binding = new Binding(nameof(OvertimeDetailEntry.ColumnName)), Width = new DataGridLength(0.9, DataGridLengthUnitType.Star) });
                dataGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = "Saat",
                    Binding = new Binding(nameof(OvertimeDetailEntry.Hours)) { StringFormat = "0.##" },
                    Width = new DataGridLength(0.8, DataGridLengthUnitType.Star)
                });
                dataGrid.Columns.Add(new DataGridTextColumn { Header = "Not", Binding = new Binding(nameof(OvertimeDetailEntry.Notes)), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });

                detailTab.Content = dataGrid;
            }
            else
            {
                detailTab.Content = new TextBlock
                {
                    Text = "Gösterilecek özel FM bilgisi bulunmuyor.",
                    Margin = new Thickness(15),
                    FontSize = 14,
                    Foreground = Brushes.Gray
                };
            }

            // Koşullu Kazançlar Tab
            var conditionalEarningsTab = new TabItem { Header = "Koşullu Kazançlar" };
            if (conditionalEarningsDetails != null && conditionalEarningsDetails.Any())
            {
                var conditionalDataGrid = new DataGrid
                {
                    ItemsSource = conditionalEarningsDetails,
                    AutoGenerateColumns = false,
                    CanUserAddRows = false,
                    IsReadOnly = true,
                    AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(248, 249, 250)),
                    RowHeight = 38,
                    ColumnWidth = new DataGridLength(1, DataGridLengthUnitType.Star),
                    HeadersVisibility = DataGridHeadersVisibility.Column
                };

                conditionalDataGrid.Columns.Add(new DataGridTextColumn { Header = "Sicil", Binding = new Binding(nameof(ConditionalEarningDetail.PersonnelCode)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
                conditionalDataGrid.Columns.Add(new DataGridTextColumn { Header = "Ad Soyad", Binding = new Binding(nameof(ConditionalEarningDetail.PersonnelName)), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });
                conditionalDataGrid.Columns.Add(new DataGridTextColumn { Header = "Koşul Tipi", Binding = new Binding(nameof(ConditionalEarningDetail.ConditionType)), Width = new DataGridLength(1.2, DataGridLengthUnitType.Star) });
                conditionalDataGrid.Columns.Add(new DataGridTextColumn { Header = "Hedef Sütun", Binding = new Binding(nameof(ConditionalEarningDetail.ColumnName)), Width = new DataGridLength(1.5, DataGridLengthUnitType.Star) });
                conditionalDataGrid.Columns.Add(new DataGridTextColumn { Header = "Sütun Harfi", Binding = new Binding(nameof(ConditionalEarningDetail.ColumnLetter)), Width = new DataGridLength(0.8, DataGridLengthUnitType.Star) });
                conditionalDataGrid.Columns.Add(new DataGridTextColumn
                {
                    Header = "Değer",
                    Binding = new Binding(nameof(ConditionalEarningDetail.Value)) { StringFormat = "N2" },
                    Width = new DataGridLength(1, DataGridLengthUnitType.Star)
                });
                conditionalDataGrid.Columns.Add(new DataGridTextColumn { Header = "Açıklama", Binding = new Binding(nameof(ConditionalEarningDetail.RuleDescription)), Width = new DataGridLength(2, DataGridLengthUnitType.Star) });

                conditionalEarningsTab.Content = conditionalDataGrid;
            }
            else
            {
                conditionalEarningsTab.Content = new TextBlock
                {
                    Text = "Gösterilecek koşullu kazanç bilgisi bulunmuyor.",
                    Margin = new Thickness(15),
                    FontSize = 14,
                    Foreground = Brushes.Gray
                };
            }

            var rawTab = new TabItem { Header = "Ham Çıktı" };
            rawTab.Content = new ScrollViewer
            {
                Content = new TextBox
                {
                    Text = string.IsNullOrWhiteSpace(rawOutput) ? "Ham çıktı üretilemedi veya yakalanamadı." : rawOutput,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 13,
                    TextWrapping = TextWrapping.Wrap,
                    IsReadOnly = true,
                    Background = Brushes.White,
                    BorderThickness = new Thickness(0),
                    AcceptsReturn = true,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                },
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            tabControl.Items.Add(detailTab);
            tabControl.Items.Add(conditionalEarningsTab);
            tabControl.Items.Add(rawTab);

            var closeButton = new Button
            {
                Content = "Kapat",
                Width = 100,
                Height = 36,
                Margin = new Thickness(0, 0, 20, 20),
                HorizontalAlignment = HorizontalAlignment.Right,
                Background = new SolidColorBrush(Color.FromRgb(33, 150, 243)),
                Foreground = Brushes.White
            };
            closeButton.Click += (s, e) => Close();

            Grid.SetRow(header, 0);
            Grid.SetRow(tabControl, 1);
            Grid.SetRow(closeButton, 2);

            mainGrid.Children.Add(header);
            mainGrid.Children.Add(tabControl);
            mainGrid.Children.Add(closeButton);

            Content = mainGrid;
        }
    }


    public class PDKSStatusModal : Window
    {
        public PDKSStatusModal(IEnumerable<StatusTagInfo> statuses)
        {
            Title = "PDKS İşlem Özeti";
            Width = 520;
            Height = 360;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            Background = new SolidColorBrush(Color.FromRgb(248, 249, 250));
            FontFamily = new FontFamily("pack://application:,,,/Fonts/Poppins-Regular.ttf#Poppins");

            var statusList = statuses?.ToList() ?? new List<StatusTagInfo>();

            var rootGrid = new Grid();
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            rootGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var header = new TextBlock
            {
                Text = "PDKS veri yükleme ve eşleştirme adımlarına ait kısa özet aşağıda listelenmiştir.",
                FontSize = 14,
                Foreground = Brushes.DimGray,
                Margin = new Thickness(20, 20, 20, 10),
                TextWrapping = TextWrapping.Wrap
            };

            var listPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 0) };

            foreach (var status in statusList)
            {
                var border = new Border
                {
                    Background = status.Background ?? new SolidColorBrush(Color.FromRgb(59, 130, 246)),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(14, 8, 14, 8),
                    Margin = new Thickness(20, 4, 20, 4)
                };

                border.Child = new TextBlock
                {
                    Text = status.Text,
                    Foreground = Brushes.White,
                    FontSize = 13,
                    FontFamily = new FontFamily("pack://application:,,,/Fonts/Poppins-Medium.ttf#Poppins"),
                    TextWrapping = TextWrapping.Wrap
                };

                listPanel.Children.Add(border);
            }

            if (statusList.Count == 0)
            {
                listPanel.Children.Add(new TextBlock
                {
                    Text = "Gösterilecek özet bulunamadı.",
                    Margin = new Thickness(20),
                    Foreground = Brushes.Gray,
                    FontSize = 13
                });
            }

            var scrollViewer = new ScrollViewer
            {
                Content = listPanel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(0, 0, 0, 0)
            };

            var closeButton = new Button
            {
                Content = "Kapat",
                Width = 100,
                Height = 32,
                Margin = new Thickness(0, 0, 20, 20),
                HorizontalAlignment = HorizontalAlignment.Right,
                Background = new SolidColorBrush(Color.FromRgb(37, 99, 235)),
                Foreground = Brushes.White
            };
            closeButton.Click += (s, e) => Close();

            Grid.SetRow(header, 0);
            Grid.SetRow(scrollViewer, 1);
            Grid.SetRow(closeButton, 2);

            rootGrid.Children.Add(header);
            rootGrid.Children.Add(scrollViewer);
            rootGrid.Children.Add(closeButton);

            Content = rootGrid;
        }
    }

    public class PersonnelRecord
    {
        public string PersonnelCode { get; set; } // Sicil numarası
        public string Name { get; set; }
        public string Surname { get; set; }
        public string TCNo { get; set; }
        public int? EntryDay { get; set; }
        public int? ExitDay { get; set; }
    }

    public class PDKSMatchingRecord
    {
        public string PersonnelCode { get; set; }
        public string Name { get; set; }
        public string TCNo { get; set; }
        public bool IsMatched { get; set; }
        public string MatchedPersonnelCode { get; set; }
        public string MatchedPersonnelName { get; set; }
        public string MatchType { get; set; } = "İsim Benzerliği";
        public int TotalRecords { get; set; }
    }
}
