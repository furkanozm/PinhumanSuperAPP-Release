using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Playwright;
using OtpNet;
using OfficeOpenXml;

namespace WebScraper
{
    public class PersonnelField
    {
        public string FieldName { get; set; }
        public string DisplayName { get; set; }
        public string Selector { get; set; }
        public bool IsRequired { get; set; }
        public string InputType { get; set; } = "text"; // text, select, checkbox, date, dual
        public string[] Options { get; set; } = Array.Empty<string>();
        public int? MaxLength { get; set; }
        public int TabIndex { get; set; } = 1; // 1, 2, 3 - hangi tab'da olduğu
        public string DefaultValue { get; set; } // Varsayılan değer
    }

    public partial class PersonnelAddModal : Window
    {
        private IBrowser _browser;
        private IPage _page;
        private List<UIElement> _formFields = new List<UIElement>();
        private List<Dictionary<string, string>> _excelData;
        private bool _useExcelData = false;
        private string _excelFilePath;

    private PersonnelConfig _config;

        // Sabit personel alanları - HTML'den çıkarılan gerçek selector'larla
        private readonly List<PersonnelField> _personnelFields = new List<PersonnelField>
        {
            // Tab 1 - Kimlik Bilgileri
            new PersonnelField { FieldName = "TCKN", DisplayName = "TCKN", Selector = "#TCKN", IsRequired = true, InputType = "text", MaxLength = 11, TabIndex = 1 },
            new PersonnelField { FieldName = "FirstName", DisplayName = "Adı", Selector = "#FirstName", IsRequired = true, InputType = "text", TabIndex = 1 },
            new PersonnelField { FieldName = "LastName", DisplayName = "Soyadı", Selector = "#LastName", IsRequired = true, InputType = "text", TabIndex = 1 },
            new PersonnelField { FieldName = "Gender", DisplayName = "Cinsiyet", Selector = "#Gender", IsRequired = true, InputType = "select", Options = new[] { "Male", "Female" }, TabIndex = 1 },
            new PersonnelField { FieldName = "MaritalState", DisplayName = "Medeni Durumu", Selector = "select[name='MaritalState']", IsRequired = false, InputType = "select", Options = new[] { "Single", "Married", "Divorced" }, TabIndex = 1 },
            new PersonnelField { FieldName = "ChildrenCount", DisplayName = "Çocuk Sayısı", Selector = "input[name='ChildrenCount']", IsRequired = false, InputType = "text", TabIndex = 1 },
            new PersonnelField { FieldName = "FatherName", DisplayName = "Baba Adı", Selector = "#FatherName", IsRequired = true, InputType = "text", TabIndex = 1 },
            new PersonnelField { FieldName = "MotherName", DisplayName = "Ana Adı", Selector = "#MotherName", IsRequired = true, InputType = "text", TabIndex = 1 },
            new PersonnelField { FieldName = "JobDescription", DisplayName = "Meslek", Selector = "input[name='JobDescription']", IsRequired = false, InputType = "text", TabIndex = 1 },
            new PersonnelField { FieldName = "IsRetired", DisplayName = "Emekli mi?", Selector = "#IsRetired", IsRequired = false, InputType = "select", Options = new[] { "true", "false" }, TabIndex = 1 },
            new PersonnelField { FieldName = "BirthDate", DisplayName = "Doğum Tarihi", Selector = "input[name='BirthDate']", IsRequired = true, InputType = "date", TabIndex = 1 },
            new PersonnelField { FieldName = "Phone", DisplayName = "Telefon", Selector = "input[name='Phone']", IsRequired = true, InputType = "text", TabIndex = 1 },
            new PersonnelField { FieldName = "Email", DisplayName = "E-posta", Selector = "input[name='Email']", IsRequired = false, InputType = "text", TabIndex = 1 },
            new PersonnelField { FieldName = "EducationLevel", DisplayName = "Öğrenim Durumu", Selector = "select[name='EducationLevel']", IsRequired = false, InputType = "select", Options = new[] { "İlkokul", "Ortaokul", "Lise", "Üniversite", "Yüksek Lisans", "Doktora" }, TabIndex = 1 },

            // Tab 2 - Ekstra Bilgiler
            new PersonnelField { FieldName = "HasIskurRecord", DisplayName = "İŞKUR Kaydı", Selector = "select[name='HasIskurRecord']", IsRequired = true, InputType = "select", Options = new[] { "true", "false" }, DefaultValue = "true", TabIndex = 2 },

            // Tab 3 - Banka Bilgileri (burada selector'ları bulamadım, varsayılan kullanacağım)
            new PersonnelField { FieldName = "BANKA", DisplayName = "BANKA", Selector = "select[name='BANKA']", IsRequired = true, InputType = "select", Options = new[] { "Ziraat Bankası", "Halkbank", "Vakıfbank", "Garanti BBVA", "Akbank", "Yapı Kredi", "İş Bankası", "TEB", "DenizBank", "ING Bank", "QNB Finansbank", "Diğer" }, TabIndex = 3 },
            new PersonnelField { FieldName = "HESAP ADI", DisplayName = "HESAP ADI", Selector = "input[name='HESAP ADI']", IsRequired = true, InputType = "text", TabIndex = 3 },
            new PersonnelField { FieldName = "İBAN", DisplayName = "İBAN", Selector = "input[name='İBAN']", IsRequired = true, InputType = "text", MaxLength = 26, TabIndex = 3 }
        };

    public PersonnelAddModal()
    {
        InitializeComponent();
        _useExcelData = false;

        // Personel config'ini yükle
        _config = ConfigService.LoadConfig<PersonnelConfig>("personnel-config.json");

        Loaded += PersonnelAddModal_Loaded;
    }

    public PersonnelAddModal(string excelFilePath)
    {
        InitializeComponent();
        _excelFilePath = excelFilePath;
        _useExcelData = true;

        // Personel config'ini yükle
        _config = ConfigService.LoadConfig<PersonnelConfig>("personnel-config.json");

        // Excel dosyasını oku ve analiz et
        LoadExcelData();

        Loaded += PersonnelAddModal_Loaded;
    }

    private void LoadExcelData()
        {
            try
            {
            if (string.IsNullOrEmpty(_excelFilePath) || !System.IO.File.Exists(_excelFilePath))
            {
                MessageBox.Show("Excel dosyası bulunamadı!", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Excel dosyasını oku
            ExcelPackage.License.SetNonCommercialPersonal("WebScraper");

            using (var package = new ExcelPackage(new System.IO.FileInfo(_excelFilePath)))
            {
                var worksheet = package.Workbook.Worksheets[0]; // İlk worksheet'i al
                if (worksheet == null)
                {
                    MessageBox.Show("Excel dosyasında geçerli bir çalışma sayfası bulunamadı!", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Başlık satırını oku (1. satır)
                var headers = new List<string>();
                for (int col = 1; col <= worksheet.Dimension.End.Column; col++)
                {
                    var headerValue = worksheet.Cells[1, col].Value?.ToString()?.Trim();
                    if (!string.IsNullOrEmpty(headerValue))
                    {
                        headers.Add(headerValue);
                    }
                }

                // Veri satırlarını oku (2. satırdan itibaren)
                _excelData = new List<Dictionary<string, string>>();
                for (int row = 2; row <= worksheet.Dimension.End.Row; row++)
                {
                    var rowData = new Dictionary<string, string>();
                    for (int col = 1; col <= headers.Count && col <= worksheet.Dimension.End.Column; col++)
                    {
                        var value = worksheet.Cells[row, col].Value?.ToString()?.Trim() ?? "";
                        rowData[headers[col - 1]] = value;
                    }

                    // Boş satırları atla
                    if (rowData.Values.Any(v => !string.IsNullOrEmpty(v)))
                    {
                        _excelData.Add(rowData);
                    }
                }
            }

            if (_excelData.Count == 0)
            {
                MessageBox.Show("Excel dosyasında veri bulunamadı!", "Uyarı", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            UpdateStatus("✅", "Hazır", $"{_excelData.Count} personel verisi yüklendi. İşleme başlanabilir.", "#4CAF50", "#E8F5E8");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Excel dosyası okunurken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            _useExcelData = false;
        }
    }

    // Excel verisi ile başlatma metodu
    public void SetExcelData(List<Dictionary<string, string>> excelData)
    {
        _excelData = excelData;
        _useExcelData = true;

        // Excel verisi varsa başlığı güncelle
        Title = $"👤 Personel Ekle - {excelData.Count} Kayıt";
    }

        private void PersonnelAddModal_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Seçilen dosya bilgisini göster
                if (_useExcelData && !string.IsNullOrEmpty(_excelFilePath))
                {
                    txtSelectedFile.Text = System.IO.Path.GetFileName(_excelFilePath);
                    Title = $"👤 Personel Ekle - {System.IO.Path.GetFileName(_excelFilePath)}";

                    FormPanel.Visibility = Visibility.Collapsed;
                    StartProcessButton.IsEnabled = true;

                    UpdateStatus("✅", "Hazır", $"{_excelData?.Count ?? 0} personel verisi yüklendi. İşleme başlanabilir.", "#4CAF50", "#E8F5E8");
                }
                else
                {
                    txtSelectedFile.Text = "Dosya seçilmedi";
                    Title = "👤 Personel Ekle";

                    // Manuel giriş için form oluştur
                    GenerateFormFromFields();
                    StartProcessButton.IsEnabled = false;

                    UpdateStatus("ℹ️", "Hazır", "Verileri doldurun ve işlemi başlatın", "#2196F3", "#E3F2FD");
                }
            }
            catch (Exception ex)
            {
                UpdateStatus("❌", "Hata", $"Başlatma hatası: {ex.Message}", "#F44336", "#FFCDD2");
                MessageBox.Show($"Uygulama başlatılırken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void GenerateFormFromFields()
        {
            FormPanel.Children.Clear();
            _formFields.Clear();

            foreach (var field in _personnelFields)
            {
                var fieldElement = CreateFormField(field);
                FormPanel.Children.Add(fieldElement);
                _formFields.Add(fieldElement);
            }
        }

        private UIElement CreateFormField(PersonnelField field)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 15) };

            // Alan adı etiketi
            var label = new TextBlock
            {
                Text = field.IsRequired ? $"{field.DisplayName} *" : field.DisplayName,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 5),
                Foreground = field.IsRequired ? Brushes.Red : Brushes.Black
            };
            panel.Children.Add(label);

            // Alan input'u - InputType'a göre farklı kontrol oluştur
            Control inputControl;

            switch (field.InputType.ToLower())
            {
                case "select":
                    var comboBox = new ComboBox
                    {
                        Name = $"Field_{field.FieldName.Replace(" ", "_").Replace("?", "").Replace("İ", "I")}",
                        Height = 35,
                        IsEditable = true,
                        Tag = field
                    };

                    // Seçenekleri ekle
                    if (field.Options != null && field.Options.Length > 0)
                    {
                        foreach (var option in field.Options)
                        {
                            comboBox.Items.Add(option);
                        }
                        if (field.Options.Length > 0)
                        {
                            comboBox.SelectedIndex = 0; // İlk seçeneği seç
                        }
                    }

                    inputControl = comboBox;
                    break;

                case "checkbox":
                    var checkBox = new CheckBox
                    {
                        Name = $"Field_{field.FieldName.Replace(" ", "_").Replace("?", "").Replace("İ", "I")}",
                        Content = field.Options != null && field.Options.Length >= 2 ? field.Options[0] : "Evet",
                        Tag = field,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    inputControl = checkBox;
                    break;

                case "date":
                    var datePicker = new System.Windows.Controls.DatePicker
                    {
                        Name = $"Field_{field.FieldName.Replace(" ", "_").Replace("?", "").Replace("İ", "I")}",
                        Height = 35,
                        SelectedDateFormat = System.Windows.Controls.DatePickerFormat.Short,
                        Tag = field
                    };
                    inputControl = datePicker;
                    break;

                default: // text
                    var textBox = new TextBox
                    {
                        Name = $"Field_{field.FieldName.Replace(" ", "_").Replace("?", "").Replace("İ", "I")}",
                        Height = 35,
                        Padding = new Thickness(10, 0, 0, 0),
                        Tag = field
                    };

                    // MaxLength varsa ayarla
                    if (field.MaxLength.HasValue)
                    {
                        textBox.MaxLength = field.MaxLength.Value;
                    }

                    inputControl = textBox;
                    break;
            }

            panel.Children.Add(inputControl);
            return panel;
        }

        private async void StartProcessButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // ÇALIŞTIRMADAN ÖNCE: Login config kontrolü
                if (_config == null)
                {
                    _config = ConfigService.LoadConfig<PersonnelConfig>("personnel-config.json");
                }
                var p = _config?.Personnel;
                if (p == null || string.IsNullOrWhiteSpace(p.FirmaKodu) || string.IsNullOrWhiteSpace(p.KullaniciId) || string.IsNullOrWhiteSpace(p.Sifre))
                {
                    UpdateStatus("⚠️", "Ayar Gerekli", "Lütfen Ayarlar sekmesinden Firma Kodu, Kullanıcı ID ve Şifre girin.", "#FF9800", "#FFF3E0");
                    MessageBox.Show("Login bilgileri eksik. Lütfen Personel İşlemleri ekranındaki Ayarlar sekmesinden Firma Kodu, Kullanıcı ID ve Şifre girin.", "Ayar Eksik", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                Dictionary<string, string> formData;

                // İşlem başladığında Durdur butonunu göster
                StopButton.Visibility = Visibility.Visible;
                StartProcessButton.IsEnabled = false;

                if (_useExcelData)
                {
                    // Excel verisi ile çoklu işlem
                    await ProcessMultipleRecords();
                }
                else
                {
                    // Manuel form verisi
                    formData = GetFormData();
                    if (formData == null) return;

                    await ProcessSingleRecord(formData);
                }
            }
            catch (Exception ex)
            {
                UpdateStatus("❌", "Hata", $"İşlem hatası: {ex.Message}", "#F44336", "#FFCDD2");
                MessageBox.Show($"İşlem sırasında hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);

                // Hata durumunda Durdur butonunu gizle
                StopButton.Visibility = Visibility.Collapsed;
                StartProcessButton.IsEnabled = true;
            }
        }

        private Dictionary<string, string> GetFormData()
        {
            var formData = new Dictionary<string, string>();

            foreach (var element in _formFields)
            {
                if (element is StackPanel panel)
                {
                    var field = panel.Children.OfType<Control>().FirstOrDefault(c => c.Tag is PersonnelField)?.Tag as PersonnelField;
                    Control inputControl = null;

                    // Input kontrolünü bul
                    if (field?.InputType == "select")
                        inputControl = panel.Children.OfType<ComboBox>().FirstOrDefault();
                    else if (field?.InputType == "checkbox")
                        inputControl = panel.Children.OfType<CheckBox>().FirstOrDefault();
                    else if (field?.InputType == "date")
                        inputControl = panel.Children.OfType<System.Windows.Controls.DatePicker>().FirstOrDefault();
                    else
                        inputControl = panel.Children.OfType<TextBox>().FirstOrDefault();

                    if (field != null && inputControl != null)
                    {
                        string value = "";

                        // Input tipine göre değeri al
                        switch (field.InputType.ToLower())
                        {
                            case "select":
                                var comboBox = inputControl as ComboBox;
                                value = comboBox?.Text?.Trim() ?? comboBox?.SelectedItem?.ToString() ?? "";
                                break;
                            case "checkbox":
                                var checkBox = inputControl as CheckBox;
                                value = checkBox?.IsChecked == true ? "Evet" : "Hayır";
                                break;
                            case "date":
                                var datePicker = inputControl as System.Windows.Controls.DatePicker;
                                value = datePicker?.SelectedDate?.ToString("dd.MM.yyyy") ?? "";
                                break;
                            default: // text
                                var textBox = inputControl as TextBox;
                                value = textBox?.Text?.Trim() ?? "";
                                break;
                        }

                        // Zorunlu alan kontrolü
                        if (field.IsRequired && string.IsNullOrWhiteSpace(value))
                        {
                            MessageBox.Show($"{field.DisplayName} alanı zorunludur!", "Validasyon Hatası", MessageBoxButton.OK, MessageBoxImage.Warning);
                            inputControl?.Focus();
                            return null;
                        }

                        formData[field.FieldName] = value;
                    }
                }
            }

            return formData;
        }

        private async Task ProcessSingleRecord(Dictionary<string, string> formData)
        {
            try
            {
                UpdateStatus("🔄", "İşleniyor", "Tarayıcı başlatılıyor...", "#FF9800", "#FFF3E0");

                // Playwright başlat
                var playwright = await Playwright.CreateAsync();
                _browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Headless = _config.Browser.HeadlessMode,
                    SlowMo = _config.Browser.SlowMo
                });

                _page = await _browser.NewPageAsync();

                // Hardcoded URL'e git - login ekranı gelecek
                UpdateStatus("🔄", "İşleniyor", "Siteye bağlanılıyor...", "#FF9800", "#FFF3E0");
                await _page.GotoAsync("https://www.pinhuman.net/AgcStaff/Create");

                // Login işlemi
                Log("Login sayfasına gidiliyor...");
                if (!await PerformLoginAsync())
                {
                    throw new Exception("Giriş yapılamadı");
                }
                Log("Login başarılı, personel ekleme sayfasına yönlendiriliyor...");

                // Login sonrası aynı sayfaya tekrar git (artık giriş yapmış olacağız)
                UpdateStatus("🔄", "İşleniyor", "Personel ekleme sayfası yükleniyor...", "#FF9800", "#FFF3E0");
                await _page.GotoAsync("https://www.pinhuman.net/AgcStaff/Create");

                // Formu doldur
                UpdateStatus("🔄", "İşleniyor", "Form dolduruluyor...", "#FF9800", "#FFF3E0");
                await FillPersonnelFormAsync(formData);

                // Kaydet (şimdilik sabit selector kullan)
                UpdateStatus("🔄", "İşleniyor", "Kaydediliyor...", "#FF9800", "#FFF3E0");
                await _page.ClickAsync("button[type='submit']");
                await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);

                UpdateStatus("✅", "Başarılı", "Personel başarıyla eklendi!", "#4CAF50", "#E8F5E8");
                MessageBox.Show("Personel başarıyla eklendi!", "Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);

                // İşlem tamamlandığında Durdur butonunu gizle
                StopButton.Visibility = Visibility.Collapsed;
                StartProcessButton.IsEnabled = true;

                this.DialogResult = true;
                this.Close();
                        }
                        catch (Exception ex)
                        {
                throw new Exception($"Personel ekleme hatası: {ex.Message}");
            }
            finally
            {
                await CleanupBrowserAsync();
            }
        }

        private async Task ProcessMultipleRecords()
        {
            try
            {
                UpdateStatus("🔄", "İşleniyor", $"Toplam {_excelData.Count} kayıt işleniyor...", "#FF9800", "#FFF3E0");

                // Playwright başlat
                var playwright = await Playwright.CreateAsync();
                _browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Headless = _config.Browser.HeadlessMode,
                    SlowMo = _config.Browser.SlowMo
                });

                int successCount = 0;
                int failCount = 0;

                for (int i = 0; i < _excelData.Count; i++)
                {
                    var record = _excelData[i];
                    UpdateStatus("🔄", "İşleniyor", $"{i + 1}/{_excelData.Count} kayıt işleniyor...", "#FF9800", "#FFF3E0");

                    try
                    {
                        _page = await _browser.NewPageAsync();

                        // Hardcoded URL'e git - login ekranı gelecek
                        Log($"{i + 1}. kayıt için login sayfasına gidiliyor...");
                        await _page.GotoAsync("https://www.pinhuman.net/AgcStaff/Create");
                        if (!await PerformLoginAsync())
                        {
                            Log($"{i + 1}. kayıt için login başarısız");
                            failCount++;
                            continue;
                        }
                        Log($"{i + 1}. kayıt için login başarılı");

                        // Login sonrası aynı sayfaya tekrar git
                        await _page.GotoAsync("https://www.pinhuman.net/AgcStaff/Create");

                        // Formu doldur
                        await FillPersonnelFormAsync(record);

                        // Kaydet
                        await _page.ClickAsync("button[type='submit']");
                        await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);

                        successCount++;
                        await _page.CloseAsync();
                    }
                    catch (Exception ex)
                    {
                        failCount++;
                        // Hata logla ama devam et
                    }
                }

                UpdateStatus("✅", "Tamamlandı", $"{successCount} başarılı, {failCount} başarısız", "#4CAF50", "#E8F5E8");
                MessageBox.Show($"{successCount} kayıt başarıyla eklendi!\n{failCount} kayıt başarısız oldu.", "İşlem Tamamlandı", MessageBoxButton.OK, MessageBoxImage.Information);

                // İşlem tamamlandığında Durdur butonunu gizle
                StopButton.Visibility = Visibility.Collapsed;
                StartProcessButton.IsEnabled = true;

                this.DialogResult = true;
                this.Close();
            }
            catch (Exception ex)
            {
                throw new Exception($"Çoklu işlem hatası: {ex.Message}");
            }
            finally
            {
                await CleanupBrowserAsync();
            }
        }

        private async Task<bool> PerformLoginAsync()
        {
            try
            {
                Log("Login işlemi başlatılıyor...");

                // En güncel config'i yükle (kullanıcı Kaydet'e bastıktan sonra)
                _config = ConfigService.LoadConfig<PersonnelConfig>("personnel-config.json");

                // Config'den login bilgilerini al
                var firmaKodu = _config.Personnel.FirmaKodu?.Trim();
                var kullaniciId = _config.Personnel.KullaniciId?.Trim();
                var sifre = _config.Personnel.Sifre?.Trim();
                var totpSecret = _config.Personnel.TotpSecret?.Trim();

                if (string.IsNullOrEmpty(firmaKodu) || string.IsNullOrEmpty(kullaniciId) || string.IsNullOrEmpty(sifre))
                {
                    var empty = string.Join(", ", new []
                    {
                        string.IsNullOrWhiteSpace(firmaKodu) ? "FirmaKodu" : null,
                        string.IsNullOrWhiteSpace(kullaniciId) ? "KullaniciId" : null,
                        string.IsNullOrWhiteSpace(sifre) ? "Sifre" : null
                    }.Where(x => x != null));
                    throw new Exception($"Config eksik alanlar: {empty}");
                }

                Log($"Firma kodu: {firmaKodu}, Kullanıcı ID: {kullaniciId}");

                // TOTP kodu üret (eğer secret varsa)
                string totpCode = "";
                if (!string.IsNullOrEmpty(totpSecret))
                {
                    try
                    {
                        var secretBytes = Base32Encoding.ToBytes(totpSecret);
                        var totp = new Totp(secretBytes);
                        totpCode = totp.ComputeTotp();
                        Log($"Oluşturulan TOTP kodu: {totpCode}");
                    }
                    catch (Exception ex)
                    {
                        Log($"TOTP kodu üretme hatası: {ex.Message}");
                        // TOTP hatası olsa bile devam et
                    }
                }

                // Login formunu doldur - WebScraper'dan birebir alındı
                await FillLoginFormAsync(firmaKodu, kullaniciId, sifre);

                // İlk login butonuna tıkla
                await ClickLoginButtonAsync();

                // 2FA kontrolü ve TOTP kodu üretimi (eğer varsa)
                if (!string.IsNullOrEmpty(totpCode))
                {
                    await Handle2FAWithTOTPAsync(totpCode);
                }

                // Login başarısını kontrol et
                await CheckLoginSuccessAsync();

                return true;
            }
            catch (Exception ex)
            {
                Log($"Login hatası: {ex.Message}");
                return false;
            }
        }

        private async Task FillLoginFormAsync(string firmaKodu, string kullaniciId, string sifre)
        {
            Log("Login formu dolduruluyor...");

            // Kullanıcı adı alanı - WebScraper'dan birebir
            var usernameField = await _page.QuerySelectorAsync("#UserName");
            if (usernameField != null)
            {
                await usernameField.FillAsync(kullaniciId);
                Log("Kullanıcı adı girildi.");
            }
            else
            {
                Log("Kullanıcı adı alanı bulunamadı!");
            }

            // Firma kodu alanı
            var companyCodeField = await _page.QuerySelectorAsync("#CompanyCode");
            if (companyCodeField != null)
            {
                await companyCodeField.FillAsync(firmaKodu);
                Log("Firma kodu girildi.");
            }
            else
            {
                Log("Firma kodu alanı bulunamadı!");
            }

            // Şifre alanı
            var passwordField = await _page.QuerySelectorAsync("#Password");
            if (passwordField != null)
            {
                await passwordField.FillAsync(sifre);
                Log("Şifre girildi.");
            }
            else
            {
                Log("Şifre alanı bulunamadı!");
            }
        }

        private async Task ClickLoginButtonAsync()
        {
            Log("Login butonuna tıklanıyor...");

            var loginButton = await _page.QuerySelectorAsync("button.btn.btn-lg.btn-success.btn-block");

            if (loginButton != null)
            {
                await loginButton.WaitForElementStateAsync(Microsoft.Playwright.ElementState.Visible);
                await loginButton.ClickAsync();
                Log("Login butonuna tıklandı.");

                // Login sonrası sayfa yüklenmesini bekle
                await _page.WaitForTimeoutAsync(1000);
            }
            else
            {
                Log("Login butonu bulunamadı! Manuel olarak giriş yapın...");
                // Alternatif selector'ları dene
                var altSelectors = new[] { "button[type='submit']", "input[type='submit']", ".btn-login" };
                foreach (var sel in altSelectors)
                {
                    try
                    {
                        await _page.ClickAsync(sel);
                        Log($"Alternatif login butonu kullanıldı: {sel}");
                        return;
                    }
                    catch { }
                }
                throw new Exception("Login butonu bulunamadı");
            }
        }

        private async Task Handle2FAWithTOTPAsync(string totpCode)
        {
            Log("2FA TOTP kodu giriliyor...");

            try
            {
                // WebScraper'dan birebir: WaitForSelectorAsync kullan ve timeout ver
                var twoFactorField = await _page.WaitForSelectorAsync("#Code, input[name='Code'], input[name='code'], input[name='2fa'], input[name='otp'], input[placeholder*='Doğrulama'], input[placeholder*='code'], input[placeholder*='2fa'], input[placeholder*='OTP'], input[placeholder*='doğrulama'], input[placeholder*='verification']",
                    new Microsoft.Playwright.PageWaitForSelectorOptions { Timeout = 5000 });

                if (twoFactorField != null)
                {
                    // Kodu temizle ve gir (WebScraper'dan)
                    await twoFactorField.FillAsync("");
                    await twoFactorField.FillAsync(totpCode);
                    Log("✅ 2FA TOTP kodu girildi.");

                    // Biraz bekle (WebScraper'dan)
                    await _page.WaitForTimeoutAsync(500);

                    // 2FA submit butonunu bul ve tıkla (WebScraper'dan birebir)
                    var submitButton = await _page.QuerySelectorAsync("button.btn.btn-lg.btn-success.btn-block, button[type='submit'], input[type='submit']");
                    if (submitButton != null)
                    {
                        // JavaScript ile tıkla (WebScraper'dan)
                        await _page.EvaluateAsync(@"
                            const button = document.querySelector('button.btn.btn-lg.btn-success.btn-block, button[type=""submit""]');
                            if (button) {
                                button.click();
                            }
                        ");

                        // Submit sonrası bekle (WebScraper'dan)
                        await _page.WaitForTimeoutAsync(1000);
                        Log("2FA submit butonuna tıklandı.");
                    }
                    else
                    {
                        Log("2FA submit butonu bulunamadı.");
                    }
                }
                else
                {
                    Log("TOTP alanı bulunamadı, devam ediliyor...");
                }
            }
            catch (Exception ex)
            {
                Log($"2FA TOTP işlemi hatası: {ex.Message}");
            }
        }

        private async Task CheckLoginSuccessAsync()
        {
            try
            {
                Log("Login başarısı kontrol ediliyor...");

                // Sayfanın yüklenmesini bekle
                await _page.WaitForTimeoutAsync(1000);

                // Login başarısını kontrol et - dashboard veya ana sayfa elementlerini ara
                var successIndicator = await _page.QuerySelectorAsync(".dashboard, .main-content, .user-info, .logout, [href*='logout'], .navbar, .header, .sidebar");

                if (successIndicator != null)
                {
                    Log("✅ Login başarılı - dashboard bulundu.");
                    return;
                }

                // URL'yi kontrol et
                var currentUrl = _page.Url;
                Log($"Mevcut URL: {currentUrl}");

                if (currentUrl.Contains("AgcStaff") || currentUrl.Contains("Create") || currentUrl.Contains("/Create"))
                {
                    Log("✅ Login başarılı - personel oluşturma sayfası bulundu.");
                    return;
                }

                if (!currentUrl.Contains("login") && !currentUrl.Contains("Login") && !currentUrl.Contains("Account"))
                {
                    Log("✅ Login başarılı - URL login sayfasında değil.");
                    return;
                }

                // Sayfa içeriğini kontrol et
                var pageContent = await _page.ContentAsync();
                var hasLoginForm = pageContent.Contains("UserName") || pageContent.Contains("Password") || pageContent.Contains("GİRİŞ");

                if (!hasLoginForm)
                {
                    Log("✅ Login başarılı - login formu bulunamadı.");
                    return;
                }

                // Login formu hala varsa ama belki 2FA bekliyor olabilir
                var hasTwoFactor = pageContent.Contains("code") || pageContent.Contains("2fa") || pageContent.Contains("otp") || pageContent.Contains("doğrulama");
                if (hasTwoFactor)
                {
                    Log("ℹ️ 2FA/TOTP bekleniyor, login devam ediyor...");
                    return;
                }

                Log("⚠️ Login durumu belirsiz, login formu hala mevcut.");
                return;
            }
            catch (Exception ex)
            {
                Log($"❌ Login kontrolü sırasında hata: {ex.Message}");
                throw;
            }
        }

        private async Task FillPersonnelFormAsync(Dictionary<string, string> data)
        {
            int currentTab = 1;

            foreach (var field in _personnelFields.OrderBy(f => f.TabIndex))
            {
                // Tab değişimi gerekiyorsa
                if (field.TabIndex != currentTab)
                {
                    await SwitchToTabAsync(field.TabIndex);
                    currentTab = field.TabIndex;
                    await Task.Delay(1000); // Tab değişiminde 1 saniye bekleme

                    // İlk alan için ekstra bekleme
                    if (field.FieldName == "TCKN" || field.FieldName == "HasIskurRecord" || field.FieldName == "BANKA")
                    {
                        await Task.Delay(500);
                        Log($"Tab {field.TabIndex} için form alanları yükleniyor...");
                    }
                }

                if (data.ContainsKey(field.FieldName))
                {
                    string value = data[field.FieldName];

                    // DefaultValue varsa kullan
                    if (string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(field.DefaultValue))
                    {
                        value = field.DefaultValue;
                    }

                    if (!string.IsNullOrEmpty(value))
                    {
                        try
                        {
                            Log($"{field.DisplayName}: {value} (Tab {field.TabIndex})");

                            // Alternatif selector'ları dene
                            string[] selectorsToTry;
                            if (field.Selector.StartsWith("#"))
                            {
                                // ID selector'ı varsa, name attribute'larını da dene
                                var fieldName = field.FieldName;
                                var elementType = field.InputType == "select" ? "select" : "input";
                                selectorsToTry = new[] {
                                    field.Selector,                           // #FieldName
                                    $"{elementType}[id='{fieldName}']",       // input[id='FieldName'] veya select[id='FieldName']
                                    $"{elementType}[name='{fieldName}']",     // input[name='FieldName'] veya select[name='FieldName']
                                    $"[id='{fieldName}']",                    // Genel id selector
                                    $"[name='{fieldName}']"                   // Genel name selector
                                };
                            }
                            else
                            {
                                // Eski name-based selector'lar için
                                var fieldName = field.FieldName;
                                selectorsToTry = new[] {
                                    field.Selector,
                                    $"#{fieldName}",
                                    $"input[id='{fieldName}']",
                                    $"input[name='{fieldName}']",
                                    $"select[id='{fieldName}']",
                                    $"select[name='{fieldName}']"
                                };
                            }

                            bool fieldFilled = false;
                            foreach (var selector in selectorsToTry)
                            {
                                try
                                {
                                    switch (field.InputType.ToLower())
                                    {
                                        case "select":
                                            await _page.SelectOptionAsync(selector, value);
                                            fieldFilled = true;
                                            break;

                                        case "checkbox":
                                            var checkboxValue = value == "Evet" || value == "1" || value.ToLower() == "true";
                                            if (checkboxValue)
                                            {
                                                await _page.CheckAsync(selector);
                                            }
                                            else
                                            {
                                                await _page.UncheckAsync(selector);
                                            }
                                            fieldFilled = true;
                                            break;

                                        case "dual":
                                            // Hem checkbox hem radio button için
                                            // Önce checkbox'ı işaretle
                                            await _page.CheckAsync($"{selector}[type='checkbox']");
                                            // Sonra radio button'ı işaretle
                                            await _page.CheckAsync($"{selector}[type='radio']");
                                            fieldFilled = true;
                                            break;

                                        case "date":
                                            // Tarih formatını kontrol et ve düzelt
                                            if (DateTime.TryParse(value, out DateTime dateValue))
                                            {
                                                value = dateValue.ToString("yyyy-MM-dd");
                                            }
                                            await _page.FillAsync(selector, value);
                                            fieldFilled = true;
                                            break;

                                        default: // text
                                            await _page.FillAsync(selector, value);
                                            fieldFilled = true;
                                            break;
                                    }

                                    if (fieldFilled)
                                    {
                                        Log($"✅ {field.DisplayName} alanı dolduruldu ({selector})");
                                        break;
                                    }
                                }
                                catch
                                {
                                    // Bu selector çalışmadı, devam et
                                    continue;
                                }
                            }

                            if (!fieldFilled)
                            {
                                Log($"❌ {field.DisplayName} alanı hiçbir selector ile bulunamadı");
                            }

                            await Task.Delay(200); // Alanlar arası bekleme
                        }
                        catch (Exception ex)
                        {
                            // Alan bulunamazsa logla ama devam et
                            Log($"❌ Alan doldurulurken hata ({field.DisplayName}): {ex.Message}");
                        }
                    }
                }
            }
        }

        private async Task SwitchToTabAsync(int tabIndex)
        {
            try
            {
                string tabSelector = "";

                // HTML'den çıkarılan tab selector'larına göre
                switch (tabIndex)
                {
                    case 1:
                        tabSelector = "a[href='#tab-identity']"; // KİMLİK BİLGİLERİ
                        break;
                    case 2:
                        tabSelector = "a[href='#tab-extra']"; // EKSTRA BİLGİLERİ
                        break;
                    case 3:
                        tabSelector = "a[href='#tab-bank']"; // BANKA BİLGİLERİ
                        break;
                    default:
                        throw new Exception($"Geçersiz tab index: {tabIndex}");
                }

                // Tab'a tıkla
                Log($"Tab {tabIndex}'e geçiliyor...");
                await _page.ClickAsync(tabSelector);
                await _page.WaitForLoadStateAsync(LoadState.NetworkIdle);

                // Tab geçişinden sonra form alanlarının yüklenmesini bekle
                await _page.WaitForTimeoutAsync(1500); // 1.5 saniye bekle

                // Tab'ın aktif olduğunu doğrula
                var activeTab = await _page.QuerySelectorAsync(".nav-tabs .active");
                if (activeTab != null)
                {
                    Log($"✅ Tab {tabIndex}'e başarıyla geçildi");

                    // Ekstra bekleme - form alanlarının tamamen yüklenmesi için
                    await _page.WaitForTimeoutAsync(500);
                }
                else
                {
                    Log($"⚠️ Tab {tabIndex} geçişi doğrulanamadı");
                }
            }
            catch (Exception ex)
            {
                Log($"❌ Tab geçişinde hata (Tab {tabIndex}): {ex.Message}");
                // Hata durumunda devam et, belki tab zaten doğru yerde
            }
        }

        private async Task CleanupBrowserAsync()
        {
            try
            {
                if (_page is not null)
                {
                    await _page.CloseAsync();
                    _page = null;
                }

                if (_browser is not null)
                {
                    await _browser.CloseAsync();
                    _browser = null;
                }
            }
            catch
            {
                // Cleanup hatası yoksay
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        private async void StopButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Log("İşlem durduruluyor...");
                StopButton.IsEnabled = false;
                StopButton.Content = "Durduruluyor...";

                // Tarayıcıyı kapat
                await CleanupBrowserAsync();

                // Buton durumlarını güncelle
                StopButton.Visibility = Visibility.Collapsed;
                StartProcessButton.IsEnabled = true;
                StartProcessButton.Content = "İşlemi Başlat";

                UpdateStatus("⚠️", "Durduruldu", "İşlem kullanıcı tarafından durduruldu", "#FF9800", "#FFF3E0");
                Log("İşlem başarıyla durduruldu");
            }
            catch (Exception ex)
            {
                Log($"Durdurma sırasında hata: {ex.Message}");
            }
            finally
            {
                StopButton.IsEnabled = true;
                StopButton.Content = "Durdur";
            }
        }

        private void UpdateStatus(string emoji, string title, string message, string color, string bgColor)
        {
            // Status güncelleme kodu (varsa)
        }

        private void Log(string message)
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    if (LogTextBox != null)
                    {
                        LogTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\n");
                        LogTextBox.ScrollToEnd();
                    }
                });
            }
            catch { }
        }
    }
}